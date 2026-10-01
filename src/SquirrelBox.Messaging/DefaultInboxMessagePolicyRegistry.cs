using Microsoft.Extensions.Options;

namespace SquirrelBox.Messaging;

/// <summary>
/// Default messaging inbox policy registry backed by discovered policy profiles.
/// </summary>
public sealed class DefaultInboxMessagePolicyRegistry : IInboxMessagePolicyRegistry
{
    private readonly IReadOnlyList<InboxMessagePolicy> _policies;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxMessagePolicyRegistry"/> class.
    /// </summary>
    /// <param name="options">The messaging options.</param>
    public DefaultInboxMessagePolicyRegistry(IOptions<SquirrelBoxMessagingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _policies = Discover(options.Value);
    }

    /// <inheritdoc />
    public bool TryResolve(InboxMessageContext context, out InboxMessagePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(context);

        policy = _policies
            .Where(candidate => candidate.Matches(context))
            .OrderByDescending(candidate => candidate.Specificity)
            .ThenBy(candidate => candidate.Order)
            .FirstOrDefault();

        return policy is not null;
    }

    private static IReadOnlyList<InboxMessagePolicy> Discover(SquirrelBoxMessagingOptions options)
    {
        var policies = new List<InboxMessagePolicy>();
        foreach (var assembly in options.ProfileAssemblies.Distinct())
        {
            foreach (var type in assembly.GetTypes().Where(IsProfileType))
            {
                var profile = (InboxMessagePolicyProfile)Activator.CreateInstance(type);
                profile.Configure(new InboxMessagePolicyProfileBuilder(policies));
            }
        }

        return policies;
    }

    private static bool IsProfileType(Type type)
        => !type.IsAbstract &&
           !type.IsInterface &&
           typeof(InboxMessagePolicyProfile).IsAssignableFrom(type) &&
           type.GetConstructor(Type.EmptyTypes) is not null;
}
