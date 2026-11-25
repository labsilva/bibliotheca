namespace Bibliotheca.Backend.Data;

public sealed class BookEditionReference
{
    public required string ProviderId { get; init; }
    public required string ExternalId { get; init; }
    public required Book Book { get; init; }
    public int BookId { get; private set; }
}
