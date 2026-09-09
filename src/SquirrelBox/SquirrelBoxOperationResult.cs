namespace SquirrelBox;

/// <summary>
/// Represents the result of invoking a SquirrelBox operation.
/// </summary>
/// <typeparam name="TResult">The operation result type.</typeparam>
public sealed class SquirrelBoxOperationResult<TResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxOperationResult{TResult}"/> class.
    /// </summary>
    public SquirrelBoxOperationResult(
        SquirrelBoxOperationExecutionState state,
        TResult result,
        InboxDecision decision,
        InboxContext context)
    {
        State = state;
        Result = result;
        Decision = decision;
        Context = context;
    }

    /// <summary>
    /// Gets the invocation state.
    /// </summary>
    public SquirrelBoxOperationExecutionState State { get; }

    /// <summary>
    /// Gets the operation result when one is available.
    /// </summary>
    public TResult Result { get; }

    /// <summary>
    /// Gets the inbox policy decision that governed execution.
    /// </summary>
    public InboxDecision Decision { get; }

    /// <summary>
    /// Gets the inbox context associated with this invocation when one is available.
    /// </summary>
    public InboxContext Context { get; }

    /// <summary>
    /// Gets the effective idempotency key associated with this invocation.
    /// </summary>
    public string EffectiveIdempotencyKey => Decision?.EffectiveIdempotencyKey ?? Context?.EffectiveIdempotencyKey;

    /// <summary>
    /// Gets a value indicating whether the operation ran inline.
    /// </summary>
    public bool Executed => State is SquirrelBoxOperationExecutionState.Executed;

    /// <summary>
    /// Gets a value indicating whether the operation was deferred.
    /// </summary>
    public bool Deferred => State is SquirrelBoxOperationExecutionState.Deferred;
}
