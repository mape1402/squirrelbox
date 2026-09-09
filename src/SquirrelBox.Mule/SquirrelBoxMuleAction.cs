using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Base Mule action that continues a SquirrelBox inbox entry from Mule metadata before executing work.
/// </summary>
/// <typeparam name="TPayload">The Mule payload type.</typeparam>
public abstract class SquirrelBoxMuleAction<TPayload> : IMuleAction<TPayload>
{
    private readonly IInboxService _inbox;
    private readonly string _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxMuleAction{TPayload}"/> class.
    /// </summary>
    /// <param name="inbox">The inbox service used to continue and complete the stored entry.</param>
    /// <param name="owner">The owner assigned to the continued inbox context.</param>
    protected SquirrelBoxMuleAction(IInboxService inbox, string owner = "mule")
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _owner = string.IsNullOrWhiteSpace(owner) ? "mule" : owner;
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(MuleActionContext<TPayload> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var inboxContext = await _inbox.ContinueAsync(
            ResolveInboxEntryId(context),
            _owner,
            ownsCompletion: true,
            cancellationToken);
        var squirrelContext = new SquirrelBoxMuleActionContext<TPayload>(inboxContext, context);

        try
        {
            await ExecuteInboxAsync(squirrelContext, cancellationToken);

            if (ReferenceEquals(_inbox.Current, inboxContext))
            {
                var completion = await CreateCompletionAsync(squirrelContext, cancellationToken);
                await _inbox.CompleteCurrentAsync(completion, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_inbox.Current, inboxContext))
                await _inbox.FailCurrentAsync(exception, cancellationToken);

            throw;
        }
    }

    /// <summary>
    /// Executes the durable action while the related inbox context is current.
    /// </summary>
    /// <param name="context">The SquirrelBox-aware Mule action context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when action work finishes.</returns>
    protected abstract ValueTask ExecuteInboxAsync(
        SquirrelBoxMuleActionContext<TPayload> context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates optional completion details persisted when the action succeeds.
    /// </summary>
    /// <param name="context">The SquirrelBox-aware Mule action context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The completion details to store for the inbox entry.</returns>
    protected virtual ValueTask<InboxCompletion> CreateCompletionAsync(
        SquirrelBoxMuleActionContext<TPayload> context,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(InboxCompletion.Empty);

    private static Ulid ResolveInboxEntryId(MuleActionContext<TPayload> context)
    {
        if (!context.Metadata.TryGetValue(SquirrelBoxMuleMetadata.InboxEntryId, out var rawEntryId) ||
            string.IsNullOrWhiteSpace(rawEntryId))
        {
            throw new InvalidOperationException(
                $"Mule action metadata is missing '{SquirrelBoxMuleMetadata.InboxEntryId}'.");
        }

        try
        {
            return Ulid.Parse(rawEntryId);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"Mule action metadata '{SquirrelBoxMuleMetadata.InboxEntryId}' is not a valid ULID.",
                exception);
        }
    }
}
