using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Configures the Entity Framework Core storage model used by SquirrelBox.
/// </summary>
public sealed class SquirrelBoxEntityFrameworkOptions
{
    /// <summary>
    /// Gets or sets the database schema used by SquirrelBox tables.
    /// </summary>
    public string Schema { get; set; }

    /// <summary>
    /// Gets or sets the inbox entries table name.
    /// </summary>
    public string InboxEntriesTableName { get; set; } = "SquirrelBoxInboxEntries";

    /// <summary>
    /// Gets or sets the inbox attempts table name.
    /// </summary>
    public string InboxAttemptsTableName { get; set; } = "SquirrelBoxInboxAttempts";

    /// <summary>
    /// Gets or sets the outbox envelopes table name.
    /// </summary>
    public string OutboxEnvelopesTableName { get; set; } = "SquirrelBoxOutboxEnvelopes";

    /// <summary>
    /// Gets or sets the outbox attempts table name.
    /// </summary>
    public string OutboxAttemptsTableName { get; set; } = "SquirrelBoxOutboxAttempts";

    /// <summary>
    /// Gets the ULID storage options.
    /// </summary>
    public SquirrelBoxUlidStorageOptions Ulids { get; } = new();

    internal IList<Action<EntityTypeBuilder<SquirrelBoxInboxEntryRecord>>> InboxEntryConfigurators { get; } = [];

    internal IList<Action<EntityTypeBuilder<SquirrelBoxInboxAttemptRecord>>> InboxAttemptConfigurators { get; } = [];

    internal IList<Action<EntityTypeBuilder<SquirrelBoxOutboxEnvelopeRecord>>> OutboxEnvelopeConfigurators { get; } = [];

    internal IList<Action<EntityTypeBuilder<SquirrelBoxOutboxAttemptRecord>>> OutboxAttemptConfigurators { get; } = [];

    internal SquirrelBoxEntityFrameworkOptions Clone()
    {
        var clone = new SquirrelBoxEntityFrameworkOptions
        {
            Schema = Schema,
            InboxEntriesTableName = InboxEntriesTableName,
            InboxAttemptsTableName = InboxAttemptsTableName,
            OutboxEnvelopesTableName = OutboxEnvelopesTableName,
            OutboxAttemptsTableName = OutboxAttemptsTableName
        };

        clone.Ulids.Mode = Ulids.Mode;
        clone.Ulids.ColumnType = Ulids.ColumnType;

        foreach (var configurator in Ulids.PropertyConfigurators)
            clone.Ulids.PropertyConfigurators.Add(configurator);

        foreach (var configurator in InboxEntryConfigurators)
            clone.InboxEntryConfigurators.Add(configurator);

        foreach (var configurator in InboxAttemptConfigurators)
            clone.InboxAttemptConfigurators.Add(configurator);

        foreach (var configurator in OutboxEnvelopeConfigurators)
            clone.OutboxEnvelopeConfigurators.Add(configurator);

        foreach (var configurator in OutboxAttemptConfigurators)
            clone.OutboxAttemptConfigurators.Add(configurator);

        return clone;
    }

    internal int GetModelHashCode()
    {
        var hash = new HashCode();
        hash.Add(Schema, StringComparer.Ordinal);
        hash.Add(InboxEntriesTableName, StringComparer.Ordinal);
        hash.Add(InboxAttemptsTableName, StringComparer.Ordinal);
        hash.Add(OutboxEnvelopesTableName, StringComparer.Ordinal);
        hash.Add(OutboxAttemptsTableName, StringComparer.Ordinal);
        hash.Add(Ulids.Mode);
        hash.Add(Ulids.ColumnType, StringComparer.Ordinal);
        hash.Add(Ulids.PropertyConfigurators.Count);
        hash.Add(InboxEntryConfigurators.Count);
        hash.Add(InboxAttemptConfigurators.Count);
        hash.Add(OutboxEnvelopeConfigurators.Count);
        hash.Add(OutboxAttemptConfigurators.Count);
        return hash.ToHashCode();
    }
}
