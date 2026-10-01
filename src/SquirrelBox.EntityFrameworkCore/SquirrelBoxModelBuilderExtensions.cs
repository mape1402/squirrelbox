using Microsoft.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore;

internal static class SquirrelBoxModelBuilderExtensions
{
    internal static ModelBuilder ApplySquirrelBoxInbox(
        this ModelBuilder modelBuilder,
        string tableName = "SquirrelBoxInboxEntries",
        string schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SquirrelBoxInboxEntryRecord>(entity =>
        {
            entity.ToTable(tableName, schema);
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => new { entry.Source, entry.Operation, entry.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("UX_SquirrelBoxInbox_Source_Operation_Key");
            entity.Property(entry => entry.Id).HasMaxLength(26).IsRequired();
            entity.Property(entry => entry.Source).HasMaxLength(128).IsRequired();
            entity.Property(entry => entry.Operation).HasMaxLength(512).IsRequired();
            entity.Property(entry => entry.IdempotencyKey).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.IdempotencyKeyName).HasMaxLength(128);
            entity.Property(entry => entry.IdempotencyKeySource).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.PayloadHash).HasMaxLength(128);
            entity.Property(entry => entry.PayloadType).HasMaxLength(1024);
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
            entity.Property(entry => entry.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.CompletionJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.FailureDetailsJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.Failure).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<SquirrelBoxInboxAttemptRecord>(entity =>
        {
            entity.ToTable("SquirrelBoxInboxAttempts", schema);
            entity.HasKey(attempt => attempt.Id);
            entity.HasIndex(attempt => attempt.InboxEntryId).HasDatabaseName("IX_SquirrelBoxInboxAttempts_EntryId");
            entity.HasIndex(attempt => attempt.AttemptId).HasDatabaseName("IX_SquirrelBoxInboxAttempts_AttemptId");
            entity.HasIndex(attempt => attempt.TraceId).HasDatabaseName("IX_SquirrelBoxInboxAttempts_TraceId");
            entity.Property(attempt => attempt.Id).HasMaxLength(26).IsRequired();
            entity.Property(attempt => attempt.InboxEntryId).HasMaxLength(26).IsRequired();
            entity.Property(attempt => attempt.AttemptId).HasMaxLength(256).IsRequired();
            entity.Property(attempt => attempt.AttemptIdName).HasMaxLength(128);
            entity.Property(attempt => attempt.AttemptIdSource).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.TraceId).HasMaxLength(256);
            entity.Property(attempt => attempt.TraceIdName).HasMaxLength(128);
            entity.Property(attempt => attempt.TraceIdSource).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.State).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.MetadataJson).HasColumnType("nvarchar(max)");
        });

        return modelBuilder;
    }

    internal static ModelBuilder ApplySquirrelBoxOutbox(
        this ModelBuilder modelBuilder,
        string tableName = "SquirrelBoxOutboxEnvelopes",
        string schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SquirrelBoxOutboxEnvelopeRecord>(entity =>
        {
            entity.ToTable(tableName, schema);
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.Status).HasDatabaseName("IX_SquirrelBoxOutbox_Status");
            entity.HasIndex(entry => entry.Transport).HasDatabaseName("IX_SquirrelBoxOutbox_Transport");
            entity.HasIndex(entry => entry.Operation).HasDatabaseName("IX_SquirrelBoxOutbox_Operation");
            entity.HasIndex(entry => entry.CorrelationId).HasDatabaseName("IX_SquirrelBoxOutbox_CorrelationId");
            entity.HasIndex(entry => entry.CreatedOnUtc).HasDatabaseName("IX_SquirrelBoxOutbox_CreatedOnUtc");
            entity.Property(entry => entry.Id).HasMaxLength(26).IsRequired();
            entity.Property(entry => entry.Transport).HasMaxLength(128).IsRequired();
            entity.Property(entry => entry.Operation).HasMaxLength(512).IsRequired();
            entity.Property(entry => entry.Destination).HasMaxLength(1024);
            entity.Property(entry => entry.PayloadType).HasMaxLength(1024).IsRequired();
            entity.Property(entry => entry.Payload).IsRequired();
            entity.Property(entry => entry.ContentType).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.CorrelationId).HasMaxLength(256);
            entity.Property(entry => entry.TraceId).HasMaxLength(256);
            entity.Property(entry => entry.Status).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.HeadersJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.FailureJson).HasColumnType("nvarchar(max)");
        });

        return modelBuilder;
    }

    internal static ModelBuilder ApplySquirrelBox(
        this ModelBuilder modelBuilder,
        string schema = null)
        => modelBuilder.ApplySquirrelBoxInbox(schema: schema).ApplySquirrelBoxOutbox(schema: schema);
}
