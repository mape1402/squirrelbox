namespace SquirrelBox;

/// <summary>
/// Default outbox payload type name resolver based on CLR full names and loaded assemblies.
/// </summary>
public sealed class DefaultOutboxPayloadTypeNameResolver : IOutboxPayloadTypeNameResolver
{
    /// <inheritdoc />
    public string GetName(Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        return payloadType.FullName ?? payloadType.Name;
    }

    /// <inheritdoc />
    public Type Resolve(string payloadTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadTypeName);

        if (Type.GetType(payloadTypeName, throwOnError: false) is { } directType)
            return directType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType(payloadTypeName, throwOnError: false) is { } type)
                return type;
        }

        throw new InvalidOperationException(
            $"Outbox payload type '{payloadTypeName}' could not be resolved. Register a custom {nameof(IOutboxPayloadTypeNameResolver)} when using custom payload type aliases.");
    }
}
