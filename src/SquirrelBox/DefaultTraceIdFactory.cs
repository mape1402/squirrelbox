using System.Diagnostics;

namespace SquirrelBox;

/// <summary>
/// Default trace id factory that reuses incoming tracing data or generates ULIDs.
/// </summary>
public sealed class DefaultTraceIdFactory : ITraceIdFactory
{
    /// <inheritdoc />
    public string Create(InboxIdentityFactoryContext context)
    {
        if (!string.IsNullOrWhiteSpace(context?.IncomingTraceId))
            return context.IncomingTraceId;

        if (Activity.Current is { } activity)
            return activity.TraceId.ToString();

        return Ulid.NewUlid().ToString();
    }
}
