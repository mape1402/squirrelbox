namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Exception thrown by the Pigeon consume interceptor when SquirrelBox policy rejects message execution.
/// </summary>
public sealed class SquirrelBoxPigeonRejectedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonRejectedException"/> class.
    /// </summary>
    /// <param name="decision">The decision that rejected execution.</param>
    public SquirrelBoxPigeonRejectedException(InboxDecision decision)
        : base($"SquirrelBox rejected Pigeon message execution with state '{decision?.State}' and action '{decision?.Action}'.")
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    /// <summary>
    /// Gets the decision that rejected message execution.
    /// </summary>
    public InboxDecision Decision { get; }
}
