namespace Bibliotheca.Backend.Data;

public sealed class AuthorRefresh
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RequestedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int? ActiveSlot { get; set; } = 1;
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public ICollection<AuthorRefreshItem> Items { get; set; } = [];
}

public sealed class AuthorRefreshItem
{
    public int Id { get; set; }
    public Guid AuthorRefreshId { get; set; }
    public AuthorRefresh AuthorRefresh { get; set; } = null!;
    // Snapshot the name and ID so a deleted author still appears in the report.
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
}
