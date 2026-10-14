using SquirrelBox.Messaging;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxMessageMetadataTests
{
    [Fact]
    public void WriteTo_preserves_existing_section_when_overwrite_is_disabled()
    {
        var metadata = CreateMetadata();
        var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SquirrelBoxMetadataNames.MetadataSection] = "{\"idempotencyKey\":\"existing\"}"
        };

        metadata.WriteTo(target, overwrite: false);

        Assert.Contains("existing", target[SquirrelBoxMetadataNames.MetadataSection], StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadFrom_handles_missing_invalid_and_empty_sections()
    {
        Assert.False(SquirrelBoxMessageMetadata.TryReadFrom(null, out _));
        Assert.False(SquirrelBoxMessageMetadata.TryReadFrom(
            new Dictionary<string, string>
            {
                [SquirrelBoxMetadataNames.MetadataSection] = "{not-json"
            },
            out _));
        Assert.False(SquirrelBoxMessageMetadata.TryReadFrom(
            new Dictionary<string, string>
            {
                [SquirrelBoxMetadataNames.MetadataSection] = "{}"
            },
            out _));
    }

    [Fact]
    public void Enricher_creates_and_writes_current_identity_metadata()
    {
        var accessor = new AsyncLocalSquirrelBoxIdentityAccessor();
        accessor.Current = CreateIdentity();
        var enricher = new DefaultSquirrelBoxMessageMetadataEnricher(accessor);
        var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        enricher.EnrichCurrent(target);
        var current = enricher.CreateCurrent();
        var explicitTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        enricher.Enrich(explicitTarget, accessor.Current);

        Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(target, out var parsed));
        Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(explicitTarget, out _));
        Assert.Equal("key-1", parsed.IdempotencyKey);
        Assert.Equal("corr-1", current.CorrelationId);
        Assert.Throws<ArgumentNullException>(() => enricher.Enrich(null, accessor.Current));
        Assert.Throws<ArgumentNullException>(() => enricher.EnrichCurrent(null));
    }

    [Fact]
    public void Enricher_requires_identity_accessor()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultSquirrelBoxMessageMetadataEnricher(null));
    }

    [Fact]
    public void Inbox_message_open_result_disabled_uses_empty_metadata()
    {
        var disabled = InboxMessageOpenResult.Disabled;

        Assert.False(disabled.Enabled);
        Assert.True(disabled.ShouldExecute);
        Assert.Same(SquirrelBoxMessageMetadata.Empty, disabled.EffectiveMetadata);
    }

    private static SquirrelBoxMessageMetadata CreateMetadata()
        => new()
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = "key-1",
                CorrelationId = "corr-1",
                AttemptId = "attempt-1",
                TraceId = "trace-1"
            }
        };

    private static SquirrelBoxIdentity CreateIdentity()
        => new()
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.IdempotencyKey,
                    "key-1",
                    SquirrelBoxMetadataValueSource.Incoming),
                CorrelationId = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.CorrelationId,
                    "corr-1",
                    SquirrelBoxMetadataValueSource.Generated)
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.AttemptId,
                    "attempt-1",
                    SquirrelBoxMetadataValueSource.Generated),
                TraceId = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.TraceId,
                    "trace-1",
                    SquirrelBoxMetadataValueSource.Generated)
            }
        };
}
