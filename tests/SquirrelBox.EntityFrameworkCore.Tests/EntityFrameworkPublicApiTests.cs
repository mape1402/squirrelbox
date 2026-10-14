using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox;
using SquirrelBox.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkPublicApiTests
{
    [Fact]
    public void EntityFramework_package_does_not_expose_model_builder_configuration_methods()
    {
        var assembly = typeof(EntityFrameworkSquirrelBoxServiceCollectionExtensions).Assembly;
        var exportedMembers = assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .ToArray();

        Assert.DoesNotContain(exportedMembers, member => member.Contains("SquirrelBoxModelBuilderExtensions"));
        Assert.DoesNotContain(exportedMembers, member => member.EndsWith(".ApplySquirrelBox", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedMembers, member => member.EndsWith(".ApplySquirrelBoxInbox", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedMembers, member => member.EndsWith(".ApplySquirrelBoxOutbox", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedMembers, member => member.EndsWith(".UseSquirrelBoxModel", StringComparison.Ordinal));
    }

    [Fact]
    public void EntityFramework_package_exposes_storage_registration_only_on_squirrelbox_builder()
    {
        var methods = typeof(EntityFrameworkSquirrelBoxServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(EntityFrameworkSquirrelBoxServiceCollectionExtensions))
            .ToArray();

        Assert.Contains(methods, method => method.Name == "UseEntityFramework");
        Assert.Contains(methods, method => method.Name == "UseEntityFrameworkInbox");
        Assert.Contains(methods, method => method.Name == "UseEntityFrameworkOutbox");
        Assert.All(methods, method =>
        {
            var firstParameter = method.GetParameters().FirstOrDefault();
            Assert.NotNull(firstParameter);
            Assert.Equal(typeof(ISquirrelBoxBuilder), firstParameter.ParameterType);
            Assert.NotEqual(typeof(IServiceCollection), firstParameter.ParameterType);
        });
    }

    [Fact]
    public void Internal_options_extension_exposes_stable_model_cache_metadata()
    {
        var extensionType = typeof(EntityFrameworkSquirrelBoxServiceCollectionExtensions)
            .Assembly
            .GetType("SquirrelBox.EntityFrameworkCore.Internal.SquirrelBoxDbContextOptionsExtension", throwOnError: true);
        var extension = (IDbContextOptionsExtension)Activator.CreateInstance(extensionType);
        var debugInfo = new Dictionary<string, string>();

        Assert.False(extension.Info.IsDatabaseProvider);
        Assert.Equal("using SquirrelBox ", extension.Info.LogFragment);
        Assert.NotEqual(0, extension.Info.GetServiceProviderHashCode());

        extension.Info.PopulateDebugInfo(debugInfo);

        Assert.True(debugInfo.ContainsKey("SquirrelBox"));
    }
}
