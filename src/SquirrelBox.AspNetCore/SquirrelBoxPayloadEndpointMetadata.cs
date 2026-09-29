namespace SquirrelBox.AspNetCore;

internal sealed class SquirrelBoxPayloadEndpointMetadata : ISquirrelBoxPayloadMetadata
{
    public SquirrelBoxPayloadEndpointMetadata(string argumentName)
    {
        ArgumentName = string.IsNullOrWhiteSpace(argumentName) ? "request" : argumentName;
    }

    public string ArgumentName { get; }
}
