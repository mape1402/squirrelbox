using Pigeon.Messaging.Consuming.Dispatching;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Pigeon consume execution interceptor that completes or fails inline inbox entries.
/// </summary>
public sealed class SquirrelBoxPigeonExecutionInterceptor : IConsumeExecutionInterceptor
{
    private readonly IInboxService _inbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonExecutionInterceptor"/> class.
    /// </summary>
    /// <param name="inbox">The inbox service.</param>
    public SquirrelBoxPigeonExecutionInterceptor(IInboxService inbox)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    }

    /// <inheritdoc />
    public async ValueTask InvokeAsync(
        ConsumeContext context,
        ConsumeExecutionDelegate next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.ExecutionSource == ConsumeExecutionSource.DeferredReplay)
        {
            await next(context, cancellationToken);
            return;
        }

        try
        {
            await next(context, cancellationToken);

            if (_inbox.Current is { OwnsCompletion: true } current &&
                ReferenceEquals(_inbox.Current, current))
            {
                await _inbox.CompleteCurrentAsync(InboxCompletion.Empty, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            if (_inbox.Current is { OwnsCompletion: true } current &&
                ReferenceEquals(_inbox.Current, current))
            {
                await _inbox.FailCurrentAsync(exception, cancellationToken);
            }

            throw;
        }
    }
}
