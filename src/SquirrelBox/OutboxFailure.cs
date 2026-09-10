namespace SquirrelBox;

/// <summary>
/// Describes a failed outbox publication attempt.
/// </summary>
public sealed class OutboxFailure
{
    /// <summary>
    /// Gets or sets a concise failure description.
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// Gets or sets the exception type that caused the failure, when available.
    /// </summary>
    public string ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets the full exception text captured for diagnostics.
    /// </summary>
    public string Exception { get; set; }

    /// <summary>
    /// Creates failure details from an exception.
    /// </summary>
    /// <param name="exception">The exception to capture.</param>
    /// <returns>The captured failure details.</returns>
    public static OutboxFailure FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new OutboxFailure
        {
            Details = exception.Message,
            ExceptionType = exception.GetType().FullName,
            Exception = exception.ToString()
        };
    }
}
