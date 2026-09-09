namespace SquirrelBox;

public sealed class InboxFailure
{
    public string ErrorType { get; init; }

    public string ErrorMessage { get; init; }

    public string Details { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static InboxFailure FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new InboxFailure
        {
            ErrorType = exception.GetType().FullName,
            ErrorMessage = exception.Message,
            Details = exception.ToString()
        };
    }
}
