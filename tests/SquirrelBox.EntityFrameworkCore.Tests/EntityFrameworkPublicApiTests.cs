using System.Reflection;
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
    }
}
