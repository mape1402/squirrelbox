namespace SquirrelBox;

/// <summary>
/// Resolves transport-neutral inbox decisions from core open results.
/// </summary>
public interface IInboxPolicyResolver
{
    /// <summary>
    /// Resolves a policy decision for an open result.
    /// </summary>
    /// <param name="result">The open result to classify.</param>
    /// <returns>The resolved decision.</returns>
    InboxDecision Resolve(InboxOpenResult result);
}
