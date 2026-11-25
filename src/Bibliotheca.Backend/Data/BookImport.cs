namespace Bibliotheca.Backend.Data;

public sealed class BookImport
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Owner { get; init; }
    public required string[] Isbns { get; init; }
    public string? EditionProviderId { get; init; }
    public string? EditionExternalId { get; init; }
    public Services.ExternalEdition? SelectedEdition { get; init; }
    public Guid? BatchId { get; init; }
    public string[] IsbnFailureReasons { get; set; } = [];
    public int[] IsbnFailurePositions { get; set; } = [];
    public int? ShelfId { get; init; }
    public int? ResultBookId { get; set; }
    public int Total { get; init; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
