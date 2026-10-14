using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Builds SquirrelBox Entity Framework Core storage options.
/// </summary>
public sealed class SquirrelBoxEntityFrameworkOptionsBuilder
{
    private readonly SquirrelBoxEntityFrameworkOptions _options;

    internal SquirrelBoxEntityFrameworkOptionsBuilder(SquirrelBoxEntityFrameworkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Uses the specified database schema for all SquirrelBox tables.
    /// </summary>
    /// <param name="schema">The database schema.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder UseSchema(string schema)
    {
        _options.Schema = string.IsNullOrWhiteSpace(schema) ? null : schema;
        return this;
    }

    /// <summary>
    /// Stores ULID values as canonical 26-character text.
    /// </summary>
    /// <param name="columnType">An optional provider column type.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder StoreUlidsAsText(string columnType = null)
    {
        _options.Ulids.Mode = SquirrelBoxUlidStorageMode.Text;
        _options.Ulids.ColumnType = columnType;
        return this;
    }

    /// <summary>
    /// Stores ULID values as 16-byte binary values.
    /// </summary>
    /// <param name="columnType">An optional provider column type.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder StoreUlidsAsBinary16(string columnType = null)
    {
        _options.Ulids.Mode = SquirrelBoxUlidStorageMode.Binary16;
        _options.Ulids.ColumnType = columnType;
        return this;
    }

    /// <summary>
    /// Applies provider-specific SQL Server defaults.
    /// </summary>
    /// <param name="configure">Optional SQL Server storage configuration.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder UseSqlServerDefaults(
        Action<SquirrelBoxSqlServerStorageOptionsBuilder> configure = null)
    {
        configure?.Invoke(new SquirrelBoxSqlServerStorageOptionsBuilder(this));
        return this;
    }

    /// <summary>
    /// Applies a custom configuration callback to every SquirrelBox ULID property.
    /// </summary>
    /// <param name="configure">The ULID property configuration callback.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder ConfigureUlidProperties(
        Action<PropertyBuilder<Ulid>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options.Ulids.PropertyConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Applies custom configuration to the inbox entries entity after SquirrelBox defaults.
    /// </summary>
    /// <param name="configure">The entity configuration callback.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder ConfigureInboxEntries(
        Action<EntityTypeBuilder<SquirrelBoxInboxEntryRecord>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options.InboxEntryConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Applies custom configuration to the inbox attempts entity after SquirrelBox defaults.
    /// </summary>
    /// <param name="configure">The entity configuration callback.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder ConfigureInboxAttempts(
        Action<EntityTypeBuilder<SquirrelBoxInboxAttemptRecord>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options.InboxAttemptConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Applies custom configuration to the outbox envelopes entity after SquirrelBox defaults.
    /// </summary>
    /// <param name="configure">The entity configuration callback.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder ConfigureOutboxEnvelopes(
        Action<EntityTypeBuilder<SquirrelBoxOutboxEnvelopeRecord>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options.OutboxEnvelopeConfigurators.Add(configure);
        return this;
    }

    /// <summary>
    /// Applies custom configuration to the outbox attempts entity after SquirrelBox defaults.
    /// </summary>
    /// <param name="configure">The entity configuration callback.</param>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxEntityFrameworkOptionsBuilder ConfigureOutboxAttempts(
        Action<EntityTypeBuilder<SquirrelBoxOutboxAttemptRecord>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options.OutboxAttemptConfigurators.Add(configure);
        return this;
    }
}
