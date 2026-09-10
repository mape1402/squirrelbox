using System.Reflection;
using Pigeon.Messaging.Producing;

namespace SquirrelBox.Messaging.Pigeon;

internal static class SquirrelBoxPigeonPublishContextMetadata
{
    private static readonly MethodInfo GetMetadataMethod = typeof(PublishContext)
        .GetMethod("GetMetadata", BindingFlags.Instance | BindingFlags.NonPublic);

    public static IReadOnlyDictionary<string, object> Read(PublishContext context)
    {
        if (GetMetadataMethod?.Invoke(context, Array.Empty<object>()) is IReadOnlyDictionary<string, object> metadata)
            return metadata;

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}
