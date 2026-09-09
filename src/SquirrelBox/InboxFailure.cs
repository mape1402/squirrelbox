namespace SquirrelBox;

/// <summary>
/// Stores structured failure details for a failed inbox entry.
/// </summary>
public sealed class InboxFailure
{
    /// <summary>
    /// Gets the failure type.
    /// </summary>
    public string ErrorType { get; init; }

    /// <summary>
    /// Gets the failure message.
    /// </summary>
    public string ErrorMessage { get; init; }

    /// <summary>
    /// Gets detailed failure text.
    /// </summary>
    public string Details { get; init; }

    /// <summary>
    /// Gets failure metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates failure details from an exception.
    /// </summary>
    /// <param name="exception">The exception to capture.</param>
    /// <returns>The captured failure details.</returns>
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
