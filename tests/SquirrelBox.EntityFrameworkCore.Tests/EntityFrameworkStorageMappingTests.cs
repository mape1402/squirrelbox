using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkStorageMappingTests
{
    [Fact]
    public void Default_mapping_stores_ulids_as_fixed_non_unicode_text()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<MappingDbContext>().Model;

        var id = FindProperty<SquirrelBoxInboxEntryRecord>(model, nameof(SquirrelBoxInboxEntryRecord.Id));
        var outboxId = FindProperty<SquirrelBoxOutboxEnvelopeRecord>(model, nameof(SquirrelBoxOutboxEnvelopeRecord.Id));

        Assert.Equal(typeof(string), id.GetTypeMapping().Converter.ProviderClrType);
        Assert.Equal(SquirrelBoxUlidStorageOptions.TextLength, id.GetMaxLength());
        Assert.False(id.IsUnicode());
        Assert.True(id.IsFixedLength());
        Assert.Equal(typeof(string), outboxId.GetTypeMapping().Converter.ProviderClrType);
    }

    [Fact]
    public void SqlServer_binary16_mapping_stores_ulids_as_binary_columns()
    {
        using var provider = CreateProvider(ef => ef.UseSqlServerDefaults(sql => sql.StoreUlidsAsBinary16()));
        using var scope = provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<MappingDbContext>().Model;

        var id = FindProperty<SquirrelBoxInboxEntryRecord>(model, nameof(SquirrelBoxInboxEntryRecord.Id));
        var attemptEntryId = FindProperty<SquirrelBoxInboxAttemptRecord>(model, nameof(SquirrelBoxInboxAttemptRecord.InboxEntryId));

        Assert.Equal(typeof(byte[]), id.GetTypeMapping().Converter.ProviderClrType);
        Assert.Equal("binary(16)", id.GetColumnType());
        Assert.Equal(SquirrelBoxUlidStorageOptions.BinaryLength, id.GetMaxLength());
        Assert.True(id.IsFixedLength());
        Assert.Equal(typeof(byte[]), attemptEntryId.GetTypeMapping().Converter.ProviderClrType);
    }

    [Fact]
    public void Entity_overrides_are_applied_after_squirrelbox_defaults()
    {
        using var provider = CreateProvider(ef =>
        {
            ef.ConfigureOutboxEnvelopes(entity =>
            {
                entity.Property(envelope => envelope.PayloadType).HasMaxLength(128);
                entity.Property(envelope => envelope.Payload).HasColumnType("varbinary(4096)");
            });
        });

        using var scope = provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<MappingDbContext>().Model;

        var payloadType = FindProperty<SquirrelBoxOutboxEnvelopeRecord>(model, nameof(SquirrelBoxOutboxEnvelopeRecord.PayloadType));
        var payload = FindProperty<SquirrelBoxOutboxEnvelopeRecord>(model, nameof(SquirrelBoxOutboxEnvelopeRecord.Payload));

        Assert.Equal(128, payloadType.GetMaxLength());
        Assert.Equal("varbinary(4096)", payload.GetColumnType());
    }

    [Fact]
    public void SquirrelBox_model_is_attached_without_dbsets_or_on_model_creating()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<MappingDbContext>().Model;

        Assert.NotNull(model.FindEntityType(typeof(SquirrelBoxInboxEntryRecord)));
        Assert.NotNull(model.FindEntityType(typeof(SquirrelBoxInboxAttemptRecord)));
        Assert.NotNull(model.FindEntityType(typeof(SquirrelBoxOutboxEnvelopeRecord)));
        Assert.NotNull(model.FindEntityType(typeof(SquirrelBoxOutboxAttemptRecord)));
    }

    private static ServiceProvider CreateProvider(
        Action<SquirrelBoxEntityFrameworkOptionsBuilder> configure = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<MappingDbContext>(options =>
            options.UseSqlServer("Server=(local);Database=SquirrelBoxMappingTests;Trusted_Connection=True;TrustServerCertificate=True"));
        services
            .AddSquirrelBox()
            .UseEntityFramework<MappingDbContext>(configure);

        return services.BuildServiceProvider();
    }

    private static IProperty FindProperty<TEntity>(IModel model, string propertyName)
        => model.FindEntityType(typeof(TEntity))?.FindProperty(propertyName)
           ?? throw new InvalidOperationException($"Property '{typeof(TEntity).Name}.{propertyName}' was not found.");

    private sealed class MappingDbContext : DbContext
    {
        public MappingDbContext(DbContextOptions<MappingDbContext> options)
            : base(options)
        {
        }
    }
}
