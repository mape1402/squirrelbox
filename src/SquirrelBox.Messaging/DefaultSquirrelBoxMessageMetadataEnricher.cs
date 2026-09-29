namespace SquirrelBox.Messaging;

/// <summary>
/// Default implementation of <see cref="ISquirrelBoxMessageMetadataEnricher"/>.
/// </summary>
public sealed class DefaultSquirrelBoxMessageMetadataEnricher : ISquirrelBoxMessageMetadataEnricher
{
    private readonly ISquirrelBoxIdentityAccessor _identityAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSquirrelBoxMessageMetadataEnricher"/> class.
    /// </summary>
    /// <param name="identityAccessor">The current SquirrelBox identity accessor.</param>
    public DefaultSquirrelBoxMessageMetadataEnricher(ISquirrelBoxIdentityAccessor identityAccessor)
    {
        _identityAccessor = identityAccessor ?? throw new ArgumentNullException(nameof(identityAccessor));
    }

    /// <inheritdoc />
    public SquirrelBoxMessageMetadata Create(SquirrelBoxIdentity identity)
        => SquirrelBoxMessageMetadata.FromIdentity(identity);

    /// <inheritdoc />
    public SquirrelBoxMessageMetadata CreateCurrent()
        => Create(_identityAccessor.Current);

    /// <inheritdoc />
    public void Enrich(IDictionary<string, string> target, SquirrelBoxIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(target);
        Create(identity).WriteTo(target);
    }

    /// <inheritdoc />
    public void EnrichCurrent(IDictionary<string, string> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        CreateCurrent().WriteTo(target);
    }
}
