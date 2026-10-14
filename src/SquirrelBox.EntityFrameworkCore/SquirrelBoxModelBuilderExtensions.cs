using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SquirrelBox.EntityFrameworkCore;

internal static class SquirrelBoxModelBuilderExtensions
{
    private static readonly ValueConverter<Ulid, string> UlidTextConverter = new(
        value => value.ToString(),
        value => Ulid.Parse(value));

    private static readonly ValueConverter<Ulid, byte[]> UlidBinaryConverter = new(
        value => ToBytes(value),
        value => FromBytes(value));

    internal static ModelBuilder ApplySquirrelBoxInbox(
        this ModelBuilder modelBuilder,
        SquirrelBoxEntityFrameworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        options ??= new SquirrelBoxEntityFrameworkOptions();

        modelBuilder.Entity<SquirrelBoxInboxEntryRecord>(entity =>
        {
            entity.ToTable(options.InboxEntriesTableName, options.Schema);
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => new { entry.Source, entry.Operation, entry.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("UX_SquirrelBoxInbox_Source_Operation_Key");
            ConfigureUlid(entity.Property(entry => entry.Id).IsRequired(), options);
            entity.Property(entry => entry.Source).HasMaxLength(128).IsRequired();
            entity.Property(entry => entry.Operation).HasMaxLength(512).IsRequired();
            entity.Property(entry => entry.IdempotencyKey).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.IdempotencyKeyName).HasMaxLength(128);
            entity.Property(entry => entry.IdempotencyKeySource).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.PayloadHash).HasMaxLength(128);
            entity.Property(entry => entry.PayloadType).HasMaxLength(512);
            entity.Property(entry => entry.CorrelationId).HasMaxLength(256);
            entity.Property(entry => entry.CorrelationIdName).HasMaxLength(128);
            entity.Property(entry => entry.CorrelationIdSource).HasMaxLength(64);
            entity.Property(entry => entry.OriginalAttemptId).HasMaxLength(256);
            entity.Property(entry => entry.OriginalTraceId).HasMaxLength(256);
            entity.Property(entry => entry.LastAttemptId).HasMaxLength(256);
            entity.Property(entry => entry.LastTraceId).HasMaxLength(256);
            entity.Property(entry => entry.Status).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.ExecutionMode).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.PolicyName).HasMaxLength(128);
            entity.Property(entry => entry.CompletedLock).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.DeferredPolicyName).HasMaxLength(128);
            entity.Property(entry => entry.DeferredLane).HasMaxLength(256);
            entity.Property(entry => entry.DeferredBackoff).HasMaxLength(64);
            entity.Property(entry => entry.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.CompletionJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.FailureDetailsJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.Failure).HasMaxLength(2048);

            foreach (var configure in options.InboxEntryConfigurators)
                configure(entity);
        });

        modelBuilder.Entity<SquirrelBoxInboxAttemptRecord>(entity =>
        {
            entity.ToTable(options.InboxAttemptsTableName, options.Schema);
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.InboxEntryId).HasDatabaseName("IX_SquirrelBoxInboxAttempts_EntryId");
            entity.HasIndex(attempt => attempt.AttemptId).HasDatabaseName("IX_SquirrelBoxInboxAttempts_AttemptId");
            entity.HasIndex(attempt => attempt.TraceId).HasDatabaseName("IX_SquirrelBoxInboxAttempts_TraceId");
            ConfigureUlid(entity.Property(attempt => attempt.Id).IsRequired(), options);
            ConfigureUlid(entity.Property(attempt => attempt.InboxEntryId).IsRequired(), options);
            entity.Property(attempt => attempt.AttemptId).HasMaxLength(256).IsRequired();
            entity.Property(attempt => attempt.AttemptIdName).HasMaxLength(128);
            entity.Property(attempt => attempt.AttemptIdSource).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.TraceId).HasMaxLength(256);
            entity.Property(attempt => attempt.TraceIdName).HasMaxLength(128);
            entity.Property(attempt => attempt.TraceIdSource).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.State).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.MetadataJson).HasColumnType("nvarchar(max)");

            foreach (var configure in options.InboxAttemptConfigurators)
                configure(entity);
        });

        return modelBuilder;
    }

    internal static ModelBuilder ApplySquirrelBoxOutbox(
        this ModelBuilder modelBuilder,
        SquirrelBoxEntityFrameworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        options ??= new SquirrelBoxEntityFrameworkOptions();

        modelBuilder.Entity<SquirrelBoxOutboxEnvelopeRecord>(entity =>
        {
            entity.ToTable(options.OutboxEnvelopesTableName, options.Schema);
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.Status).HasDatabaseName("IX_SquirrelBoxOutbox_Status");
            entity.HasIndex(entry => entry.Transport).HasDatabaseName("IX_SquirrelBoxOutbox_Transport");
            entity.HasIndex(entry => entry.Operation).HasDatabaseName("IX_SquirrelBoxOutbox_Operation");
            entity.HasIndex(entry => entry.CorrelationId).HasDatabaseName("IX_SquirrelBoxOutbox_CorrelationId");
            entity.HasIndex(entry => entry.CreatedOnUtc).HasDatabaseName("IX_SquirrelBoxOutbox_CreatedOnUtc");
            ConfigureUlid(entity.Property(entry => entry.Id).IsRequired(), options);
            entity.Property(entry => entry.Transport).HasMaxLength(128).IsRequired();
            entity.Property(entry => entry.Operation).HasMaxLength(512).IsRequired();
            entity.Property(entry => entry.Destination).HasMaxLength(512);
            entity.Property(entry => entry.PayloadType).HasMaxLength(512).IsRequired();
            entity.Property(entry => entry.Payload).IsRequired();
            entity.Property(entry => entry.ContentType).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.CorrelationId).HasMaxLength(256);
            entity.Property(entry => entry.TraceId).HasMaxLength(256);
            entity.Property(entry => entry.Status).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.HeadersJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.FailureJson).HasColumnType("nvarchar(max)");

            foreach (var configure in options.OutboxEnvelopeConfigurators)
                configure(entity);
        });

        modelBuilder.Entity<SquirrelBoxOutboxAttemptRecord>(entity =>
        {
            entity.ToTable(options.OutboxAttemptsTableName, options.Schema);
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.OutboxEnvelopeId).HasDatabaseName("IX_SquirrelBoxOutboxAttempts_EnvelopeId");
            entity.HasIndex(attempt => attempt.AttemptId).HasDatabaseName("IX_SquirrelBoxOutboxAttempts_AttemptId");
            entity.HasIndex(attempt => attempt.TraceId).HasDatabaseName("IX_SquirrelBoxOutboxAttempts_TraceId");
            entity.HasIndex(attempt => attempt.StartedOnUtc).HasDatabaseName("IX_SquirrelBoxOutboxAttempts_StartedOnUtc");
            ConfigureUlid(entity.Property(attempt => attempt.Id).IsRequired(), options);
            ConfigureUlid(entity.Property(attempt => attempt.OutboxEnvelopeId).IsRequired(), options);
            entity.Property(attempt => attempt.AttemptId).HasMaxLength(256).IsRequired();
            entity.Property(attempt => attempt.TraceId).HasMaxLength(256);
            entity.Property(attempt => attempt.State).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.FailureJson).HasColumnType("nvarchar(max)");
            entity.Property(attempt => attempt.MetadataJson).HasColumnType("nvarchar(max)");

            foreach (var configure in options.OutboxAttemptConfigurators)
                configure(entity);
        });

        return modelBuilder;
    }

    internal static ModelBuilder ApplySquirrelBox(
        this ModelBuilder modelBuilder,
        SquirrelBoxEntityFrameworkOptions options)
        => modelBuilder.ApplySquirrelBoxInbox(options).ApplySquirrelBoxOutbox(options);

    private static void ConfigureUlid(
        PropertyBuilder<Ulid> property,
        SquirrelBoxEntityFrameworkOptions options)
    {
        if (options.Ulids.Mode == SquirrelBoxUlidStorageMode.Binary16)
        {
            property.HasConversion(UlidBinaryConverter);
            property.HasMaxLength(SquirrelBoxUlidStorageOptions.BinaryLength);
            property.IsFixedLength();
        }
        else
        {
            property.HasConversion(UlidTextConverter);
            property.HasMaxLength(SquirrelBoxUlidStorageOptions.TextLength);
            property.IsUnicode(false);
            property.IsFixedLength();
        }

        if (!string.IsNullOrWhiteSpace(options.Ulids.ColumnType))
            property.HasColumnType(options.Ulids.ColumnType);

        foreach (var configure in options.Ulids.PropertyConfigurators)
            configure(property);
    }

    private static byte[] ToBytes(Ulid value)
        => value.ToByteArray();

    private static Ulid FromBytes(byte[] value)
        => new(value);
}
