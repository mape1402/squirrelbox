using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SquirrelBox;

/// <summary>
/// Default correlation id resolver that evaluates scoped strategies in deterministic order.
/// </summary>
public sealed class DefaultCorrelationIdResolver : ICorrelationIdResolver
{
    private readonly ICorrelationIdFactory _factory;
    private readonly IReadOnlyList<ICorrelationIdResolutionStrategy> _strategies;
    private readonly ILogger<DefaultCorrelationIdResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultCorrelationIdResolver"/> class.
    /// </summary>
    /// <param name="factory">The terminal correlation id factory.</param>
    /// <param name="strategies">The registered application resolution strategies.</param>
    /// <param name="logger">The logger used to emit resolution diagnostics.</param>
    public DefaultCorrelationIdResolver(
        ICorrelationIdFactory factory,
        IEnumerable<ICorrelationIdResolutionStrategy> strategies,
        ILogger<DefaultCorrelationIdResolver> logger = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies
            .Select((strategy, index) => (
                Strategy: strategy ?? throw new ArgumentException("Correlation id strategies cannot contain null values.", nameof(strategies)),
                Index: index))
            .OrderBy(item => item.Strategy.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Strategy)
            .ToArray();
        _logger = logger ?? NullLogger<DefaultCorrelationIdResolver>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<CorrelationIdResolutionResult> ResolveAsync(
        InboxIdentityFactoryContext context,
        CorrelationIdResolutionMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (mode is not CorrelationIdResolutionMode.FactoryOnly and
            not CorrelationIdResolutionMode.ResolveThenGenerate and
            not CorrelationIdResolutionMode.ResolveRequired)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "A concrete correlation id resolution mode is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(context.IncomingCorrelationId))
            return CreateFactoryResult(context, SquirrelBoxMetadataValueSource.Incoming);

        if (mode is not CorrelationIdResolutionMode.FactoryOnly)
        {
            foreach (var strategy in _strategies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CorrelationIdResolutionResult result;
                try
                {
                    result = await strategy.TryResolveAsync(context, cancellationToken) ??
                             throw new InvalidOperationException(
                                 $"Correlation id strategy '{strategy.Name}' returned a null result.");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(
                        SquirrelBoxLogEvents.CorrelationIdResolutionFailed,
                        exception,
                        "SquirrelBox correlation id strategy failed. Strategy={Strategy} Source={Source} Operation={Operation} IdempotencyKey={IdempotencyKey}",
                        strategy.Name,
                        context.Source,
                        context.Operation,
                        context.IdempotencyKey);
                    throw;
                }

                if (result.State is CorrelationIdResolutionState.Resolved)
                {
                    var resolved = result.WithResolverName(ResolveName(strategy.Name, strategy.GetType()));
                    _logger.LogInformation(
                        SquirrelBoxLogEvents.CorrelationIdResolved,
                        "SquirrelBox resolved correlation id. Strategy={Strategy} Source={Source} Operation={Operation} IdempotencyKey={IdempotencyKey} CorrelationId={CorrelationId}",
                        resolved.ResolverName,
                        context.Source,
                        context.Operation,
                        context.IdempotencyKey,
                        resolved.CorrelationId);
                    return resolved;
                }

                if (result.State is not CorrelationIdResolutionState.NotApplicable and
                    not CorrelationIdResolutionState.NotFound)
                {
                    throw new InvalidOperationException(
                        $"Correlation id strategy '{strategy.Name}' returned unsupported state '{result.State}'.");
                }
            }

            if (mode is CorrelationIdResolutionMode.ResolveRequired)
            {
                _logger.LogWarning(
                    SquirrelBoxLogEvents.CorrelationIdResolutionExhausted,
                    "SquirrelBox could not resolve the required correlation id. Source={Source} Operation={Operation} IdempotencyKey={IdempotencyKey} StrategyCount={StrategyCount}",
                    context.Source,
                    context.Operation,
                    context.IdempotencyKey,
                    _strategies.Count);
                return CorrelationIdResolutionResult.NotFound;
            }

            _logger.LogDebug(
                SquirrelBoxLogEvents.CorrelationIdResolutionExhausted,
                "SquirrelBox correlation id strategies were exhausted; the factory will create a value. Source={Source} Operation={Operation} IdempotencyKey={IdempotencyKey} StrategyCount={StrategyCount}",
                context.Source,
                context.Operation,
                context.IdempotencyKey,
                _strategies.Count);
        }

        return CreateFactoryResult(context, SquirrelBoxMetadataValueSource.Generated);
    }

    private CorrelationIdResolutionResult CreateFactoryResult(
        InboxIdentityFactoryContext context,
        SquirrelBoxMetadataValueSource source)
        => CorrelationIdResolutionResult.FromFactory(
            _factory.Create(context),
            source,
            _factory.GetType().Name);

    private static string ResolveName(string name, Type strategyType)
        => string.IsNullOrWhiteSpace(name) ? strategyType.Name : name;
}
