using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkInternalBranchTests
{
    private static readonly Assembly EntityFrameworkAssembly =
        typeof(EntityFrameworkSquirrelBoxServiceCollectionExtensions).Assembly;

    [Fact]
    public void Internal_factories_and_builders_reject_null_dependencies()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<InternalDbContext>().Options;
        var factoryType = GetGenericType("SquirrelBox.EntityFrameworkCore.SquirrelBoxDbContextFactory`1");
        var adapterType = GetGenericType("SquirrelBox.EntityFrameworkCore.SquirrelBoxDbContextFactoryAdapter`1");

        AssertArgumentNull(factoryType, null, options);
        AssertArgumentNull(factoryType, provider, null);
        AssertArgumentNull(adapterType, (object)null);
        AssertArgumentNull(typeof(SquirrelBoxEntityFrameworkOptionsBuilder), (object)null);
        AssertArgumentNull(typeof(SquirrelBoxSqlServerStorageOptionsBuilder), (object)null);
    }

    [Fact]
    public void Options_builder_handles_blank_schema_and_optional_sql_callback()
    {
        var options = new SquirrelBoxEntityFrameworkOptions { Schema = "original" };
        var builder = Create<SquirrelBoxEntityFrameworkOptionsBuilder>(options);

        var returned = builder.UseSchema(" ").UseSqlServerDefaults();

        Assert.Same(builder, returned);
        Assert.Null(options.Schema);
    }

    [Fact]
    public void Model_mapping_uses_default_options_when_none_are_supplied()
    {
        var extensionType = EntityFrameworkAssembly.GetType(
            "SquirrelBox.EntityFrameworkCore.SquirrelBoxModelBuilderExtensions",
            throwOnError: true)!;

        foreach (var methodName in new[] { "ApplySquirrelBoxInbox", "ApplySquirrelBoxOutbox" })
        {
            var modelBuilder = new ModelBuilder(new ConventionSet());
            var method = extensionType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;

            var returned = method.Invoke(null, new object[] { modelBuilder, null });

            Assert.Same(modelBuilder, returned);
        }
    }

    [Fact]
    public void Options_extension_handles_null_options_and_compares_model_identity()
    {
        var extensionType = EntityFrameworkAssembly.GetType(
            "SquirrelBox.EntityFrameworkCore.Internal.SquirrelBoxDbContextOptionsExtension",
            throwOnError: true)!;
        var defaults = (IDbContextOptionsExtension)Create(extensionType, (object)null);
        var same = (IDbContextOptionsExtension)Create(extensionType, new SquirrelBoxEntityFrameworkOptions());
        var changedOptions = new SquirrelBoxEntityFrameworkOptions { Schema = "changed" };
        var changed = (IDbContextOptionsExtension)Create(extensionType, changedOptions);
        var providerExtension = new DbContextOptionsBuilder()
            .UseSqlServer("Server=(local);Database=SquirrelBoxBranchTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options.Extensions.Single(extension => extension.Info.IsDatabaseProvider);

        Assert.True(defaults.Info.ShouldUseSameServiceProvider(same.Info));
        Assert.False(defaults.Info.ShouldUseSameServiceProvider(changed.Info));
        Assert.False(defaults.Info.ShouldUseSameServiceProvider(providerExtension.Info));
    }

    [Fact]
    public void Model_customizer_applies_defaults_without_squirrelbox_extension()
    {
        var contextOptions = new DbContextOptionsBuilder<InternalDbContext>()
            .UseSqlServer("Server=(local);Database=SquirrelBoxBranchTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var context = new InternalDbContext(contextOptions);
        var dependencies = context.GetService<ModelCustomizerDependencies>();
        var customizerType = EntityFrameworkAssembly.GetType(
            "SquirrelBox.EntityFrameworkCore.Internal.SquirrelBoxModelCustomizer",
            throwOnError: true)!;
        var customizer = Create(customizerType, dependencies);
        var modelBuilder = new ModelBuilder(new ConventionSet());

        customizerType.GetMethod("Customize")!.Invoke(customizer, new object[] { modelBuilder, context });

        Assert.NotNull(modelBuilder.Model.FindEntityType(typeof(SquirrelBoxInboxEntryRecord)));
        Assert.NotNull(modelBuilder.Model.FindEntityType(typeof(SquirrelBoxOutboxEnvelopeRecord)));
    }

    [Fact]
    public void Options_resolution_supports_implementation_types_and_rejects_invalid_descriptors()
    {
        var method = typeof(EntityFrameworkSquirrelBoxServiceCollectionExtensions)
            .GetMethod("ResolveOptions", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(InternalDbContext));
        using var provider = new ServiceCollection().BuildServiceProvider();
        var implementationDescriptor = ServiceDescriptor.Singleton<
            DbContextOptions<InternalDbContext>, DerivedDbContextOptions>();

        var resolved = method.Invoke(null, new object[] { implementationDescriptor, provider });

        Assert.IsType<DerivedDbContextOptions>(resolved);

        var invalidDescriptor = (ServiceDescriptor)RuntimeHelpers.GetUninitializedObject(typeof(ServiceDescriptor));
        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object[] { invalidDescriptor, provider }));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static Type GetGenericType(string name)
        => EntityFrameworkAssembly.GetType(name, throwOnError: true)!
            .MakeGenericType(typeof(InternalDbContext));

    private static T Create<T>(params object[] arguments)
        => (T)Create(typeof(T), arguments);

    private static object Create(Type type, params object[] arguments)
        => Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: arguments,
            culture: null)!;

    private static void AssertArgumentNull(Type type, params object[] arguments)
    {
        var constructor = Assert.Single(type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        var exception = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(arguments));
        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    private sealed class InternalDbContext : DbContext
    {
        public InternalDbContext(DbContextOptions<InternalDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class DerivedDbContextOptions : DbContextOptions<InternalDbContext>
    {
        public DerivedDbContextOptions()
            : base(new Dictionary<Type, IDbContextOptionsExtension>())
        {
        }
    }
}
