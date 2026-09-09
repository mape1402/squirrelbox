using Microsoft.Extensions.Options;

namespace SquirrelBox;

/// <summary>
/// Default policy resolver backed by <see cref="SquirrelBoxOptions"/>.
/// </summary>
public sealed class DefaultInboxPolicyResolver : IInboxPolicyResolver
{
    private readonly IOptions<SquirrelBoxOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxPolicyResolver"/> class.
    /// </summary>
    /// <param name="options">The SquirrelBox options.</param>
    public DefaultInboxPolicyResolver(IOptions<SquirrelBoxOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public InboxDecision Resolve(InboxOpenResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new InboxDecision(result.State, _options.Value.Policies.Resolve(result.State), result.Entry);
    }
}
