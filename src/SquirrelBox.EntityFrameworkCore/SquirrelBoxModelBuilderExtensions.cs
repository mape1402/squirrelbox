using Microsoft.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Provides Entity Framework Core model configuration for SquirrelBox.
/// </summary>
public static class SquirrelBoxModelBuilderExtensions
{
    /// <summary>
    /// Adds the SquirrelBox inbox table and indexes to the model.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <param name="tableName">The inbox table name.</param>
    /// <param name="schema">The optional table schema.</param>
    /// <returns>The same model builder for fluent configuration.</returns>
    public static ModelBuilder ApplySquirrelBoxInbox(
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
            entity.Property(entry => entry.IdempotencyKeySource).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.PayloadHash).HasMaxLength(128);
            entity.Property(entry => entry.PayloadType).HasMaxLength(1024);
            entity.Property(entry => entry.CorrelationId).HasMaxLength(256);
            entity.Property(entry => entry.Status).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.ExecutionMode).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.CompletionJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.FailureDetailsJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.Failure).HasColumnType("nvarchar(max)");
        });

        return modelBuilder;
    }
}
