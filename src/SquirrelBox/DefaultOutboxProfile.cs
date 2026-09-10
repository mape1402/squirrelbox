namespace SquirrelBox;

/// <summary>
/// Default outbox profile used when no discovered profile handles an enqueue request.
/// </summary>
public sealed class DefaultOutboxProfile : IOutboxProfile
{
    /// <inheritdoc />
    public bool CanHandle(OutboxEnqueueRequest request) => true;

    /// <inheritdoc />
    public OutboxEnvelope CreateEnvelope(OutboxEnqueueRequest request, OutboxProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var payloadType = request.PayloadType ?? request.Payload.GetType();
        var envelope = new OutboxEnvelope
        {
            Id = Ulid.NewUlid(),
            Transport = string.IsNullOrWhiteSpace(request.Transport)
                ? context.Options.DefaultTransport
                : request.Transport,
            Operation = string.IsNullOrWhiteSpace(request.Operation)
                ? payloadType.FullName ?? payloadType.Name
                : request.Operation,
            Destination = request.Destination,
            PayloadType = payloadType.AssemblyQualifiedName,
            Payload = context.Serializer.Serialize(request.Payload, payloadType),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? context.Serializer.ContentType
                : request.ContentType,
            CorrelationId = request.CorrelationId,
            TraceId = request.TraceId,
            Status = OutboxStatus.Pending,
            CreatedOnUtc = context.CreatedOnUtc,
            UpdatedOnUtc = context.CreatedOnUtc
        };

        foreach (var item in request.Headers)
            envelope.Headers[item.Key] = item.Value;

        foreach (var item in request.Metadata)
            envelope.Metadata[item.Key] = item.Value;

        return envelope;
    }
}
