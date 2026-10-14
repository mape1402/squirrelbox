namespace SquirrelBox.Tests;

public sealed class DefaultOutboxPayloadTypeNameResolverTests
{
    [Fact]
    public void GetName_returns_compact_type_full_name()
    {
        var resolver = new DefaultOutboxPayloadTypeNameResolver();

        var name = resolver.GetName(typeof(TestPayload));

        Assert.Equal(typeof(TestPayload).FullName, name);
        Assert.DoesNotContain(",", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_supports_compact_and_assembly_qualified_names()
    {
        var resolver = new DefaultOutboxPayloadTypeNameResolver();

        var compact = resolver.Resolve(typeof(TestPayload).FullName!);
        var assemblyQualified = resolver.Resolve(typeof(TestPayload).AssemblyQualifiedName!);

        Assert.Same(typeof(TestPayload), compact);
        Assert.Same(typeof(TestPayload), assemblyQualified);
    }

    [Fact]
    public void Resolve_throws_clear_error_for_unknown_alias()
    {
        var resolver = new DefaultOutboxPayloadTypeNameResolver();

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("missing.alias"));

        Assert.Contains(nameof(IOutboxPayloadTypeNameResolver), exception.Message, StringComparison.Ordinal);
    }

    private sealed record TestPayload(string Id);
}
