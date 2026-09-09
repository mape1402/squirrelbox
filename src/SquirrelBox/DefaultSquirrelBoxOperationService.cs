using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox;

/// <summary>
/// Default operation service that applies inbox policy before executing declared operations.
/// </summary>
public sealed class DefaultSquirrelBoxOperationService : ISquirrelBoxOperationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IEnumerable<ISquirrelBoxOperationDeferredScheduler> _deferredSchedulers;
    private readonly IInboxPolicyResolver _policyResolver;
    private readonly IInboxService _inbox;
    private readonly IServiceProvider _services;
    private readonly ISquirrelBoxOperationRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSquirrelBoxOperationService"/> class.
    /// </summary>
    public DefaultSquirrelBoxOperationService(
        IServiceProvider services,
        IInboxService inbox,
        IInboxPolicyResolver policyResolver,
        ISquirrelBoxOperationRegistry registry,
        IEnumerable<ISquirrelBoxOperationDeferredScheduler> deferredSchedulers)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _deferredSchedulers = deferredSchedulers ?? throw new ArgumentNullException(nameof(deferredSchedulers));
    }

    /// <inheritdoc />
    public ValueTask<SquirrelBoxOperationResult<TResult>> ExecuteAsync<TOperation, TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TOperation : SquirrelBoxOperation<TRequest, TResult>
        => ExecuteAsync<TRequest, TResult>(typeof(TOperation), request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<SquirrelBoxOperationResult<TResult>> ExecuteAsync<TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteAsync<TRequest, TResult>(
            _registry.Resolve(typeof(TRequest), typeof(TResult)),
            request,
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask<object> ContinueAsync(
        SquirrelBoxOperationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var operationType = ResolveType(envelope.OperationType, nameof(envelope.OperationType));
        var requestType = ResolveType(envelope.RequestType, nameof(envelope.RequestType));
        var request = JsonSerializer.Deserialize(envelope.RequestJson, requestType, JsonOptions);

        return await ExecuteOperationAsync(
            operationType,
            request,
            _inbox.Current,
            cancellationToken);
    }

    private async ValueTask<SquirrelBoxOperationResult<TResult>> ExecuteAsync<TRequest, TResult>(
        Type operationType,
        TRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationType);
        ArgumentNullException.ThrowIfNull(request);

        var open = await _inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = "operation",
            Operation = operationType.FullName ?? operationType.Name,
            Payload = request,
            PayloadType = typeof(TRequest).AssemblyQualifiedName,
            Owner = "operation"
        }, cancellationToken);

        var decision = _policyResolver.Resolve(open);
        if (!decision.ShouldExecute || open.Context is null)
            return CreateNonExecutingResult<TResult>(decision, open.Context);

        if (open.Context.Entry.ExecutionMode == InboxExecutionMode.Deferred)
            return await ScheduleDeferredAsync<TRequest, TResult>(operationType, request, decision, open.Context, cancellationToken);

        try
        {
            var result = (TResult)await ExecuteOperationAsync(
                operationType,
                request,
                open.Context,
                cancellationToken);

            if (open.State == InboxOpenState.Opened && ReferenceEquals(_inbox.Current, open.Context))
                await _inbox.CompleteCurrentAsync(CreateCompletion(result), cancellationToken);

            return new SquirrelBoxOperationResult<TResult>(
                SquirrelBoxOperationExecutionState.Executed,
                result,
                decision,
                open.Context);
        }
        catch (Exception exception)
        {
            if (open.State == InboxOpenState.Opened && ReferenceEquals(_inbox.Current, open.Context))
                await _inbox.FailCurrentAsync(exception, cancellationToken);

            throw;
        }
    }

    private async ValueTask<SquirrelBoxOperationResult<TResult>> ScheduleDeferredAsync<TRequest, TResult>(
        Type operationType,
        TRequest request,
        InboxDecision decision,
        InboxContext context,
        CancellationToken cancellationToken)
    {
        var scheduler = _deferredSchedulers.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "SquirrelBox deferred operation execution requires an ISquirrelBoxOperationDeferredScheduler. Add SquirrelBox.Mule to enable deferred operations.");

        var envelope = new SquirrelBoxOperationEnvelope
        {
            OperationType = operationType.AssemblyQualifiedName,
            RequestType = typeof(TRequest).AssemblyQualifiedName,
            ResultType = typeof(TResult).AssemblyQualifiedName,
            RequestJson = JsonSerializer.Serialize(request, typeof(TRequest), JsonOptions)
        };

        await scheduler.ScheduleAsync(context, envelope, cancellationToken);

        if (ReferenceEquals(_inbox.Current, context))
            _inbox.ReleaseCurrent();

        return new SquirrelBoxOperationResult<TResult>(
            SquirrelBoxOperationExecutionState.Deferred,
            default,
            decision,
            context);
    }

    private async ValueTask<object> ExecuteOperationAsync(
        Type operationType,
        object request,
        InboxContext inboxContext,
        CancellationToken cancellationToken)
    {
        var operation = (ISquirrelBoxOperation)(_services.GetService(operationType) ??
                                                ActivatorUtilities.CreateInstance(_services, operationType));
        var context = new SquirrelBoxOperationContext(_services, _inbox, inboxContext);
        return await operation.ExecuteAsync(request, context, cancellationToken);
    }

    private static SquirrelBoxOperationResult<TResult> CreateNonExecutingResult<TResult>(
        InboxDecision decision,
        InboxContext context)
    {
        var state = decision.Action switch
        {
            InboxPolicyAction.Replay => SquirrelBoxOperationExecutionState.Replayed,
            InboxPolicyAction.Reject or InboxPolicyAction.Fail => SquirrelBoxOperationExecutionState.Rejected,
            _ => SquirrelBoxOperationExecutionState.Skipped
        };

        var result = state == SquirrelBoxOperationExecutionState.Replayed
            ? TryReadCompletion<TResult>(decision.Entry?.Completion)
            : default;

        return new SquirrelBoxOperationResult<TResult>(state, result, decision, context);
    }

    private static TResult TryReadCompletion<TResult>(InboxCompletion completion)
    {
        if (completion?.ResultPayload is not { Length: > 0 } payload)
            return default;

        return JsonSerializer.Deserialize<TResult>(payload, JsonOptions);
    }

    private static InboxCompletion CreateCompletion(object result)
    {
        if (result is null)
            return InboxCompletion.Empty;

        var completion = new InboxCompletion
        {
            ContentType = "application/json",
            ResultPayload = JsonSerializer.SerializeToUtf8Bytes(result, result.GetType(), JsonOptions),
            ResultType = result.GetType().AssemblyQualifiedName
        };

        completion.Metadata["result-type"] = result.GetType().AssemblyQualifiedName;
        return completion;
    }

    private static Type ResolveType(string typeName, string propertyName)
        => !string.IsNullOrWhiteSpace(typeName) && Type.GetType(typeName) is { } type
            ? type
            : throw new InvalidOperationException($"SquirrelBox operation envelope property '{propertyName}' does not contain a resolvable type name.");
}
