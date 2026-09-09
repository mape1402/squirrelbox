namespace SquirrelBox;

/// <summary>
/// Describes the transport-neutral action associated with an inbox decision.
/// </summary>
public enum InboxPolicyAction
{
    /// <summary>
    /// Continue with normal execution.
    /// </summary>
    Continue,

    /// <summary>
    /// Skip execution because the work is already in progress.
    /// </summary>
    Skip,

    /// <summary>
    /// Reject the incoming request or message.
    /// </summary>
    Reject,

    /// <summary>
    /// Replay a stored completion when the adapter supports it.
    /// </summary>
    Replay,

    /// <summary>
    /// Retry previously failed work.
    /// </summary>
    Retry,

    /// <summary>
    /// Reopen an expired or stale entry.
    /// </summary>
    Reopen,

    /// <summary>
    /// Mark the current attempt as failed.
    /// </summary>
    Fail
}
