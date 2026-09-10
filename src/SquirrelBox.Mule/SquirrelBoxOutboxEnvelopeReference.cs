namespace SquirrelBox.Mule;

/// <summary>
/// Durable Mule payload that references a persisted SquirrelBox outbox envelope.
/// </summary>
public sealed class SquirrelBoxOutboxEnvelopeReference
{
    /// <summary>
    /// Gets or sets the ULID outbox envelope id stored as text.
    /// </summary>
    public string EnvelopeId { get; set; }
}
