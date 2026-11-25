namespace Bibliotheca.Backend.Data;

public sealed class CatalogueRefresh
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RequestedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int? ActiveSlot { get; set; } = 1;
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public ICollection<CatalogueRefreshItem> Items { get; set; } = [];
}

public sealed class CatalogueRefreshItem
{
    public int Id { get; set; }
    public Guid CatalogueRefreshId { get; set; }
    public CatalogueRefresh CatalogueRefresh { get; set; } = null!;
    // Snapshot identifiers deliberately have no book FK: deleted books remain in the report.
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
}
