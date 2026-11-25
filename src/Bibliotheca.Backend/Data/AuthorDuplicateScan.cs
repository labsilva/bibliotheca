namespace Bibliotheca.Backend.Data;

public sealed class AuthorDuplicateScan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int? ActiveSlot { get; set; } = 1;
    public int LastAuthorId { get; set; }
    public int MaxAuthorId { get; set; }
    public int Total { get; set; }
    public int Processed { get; set; }
    public int NewSuggestions { get; set; }
    public string RequestedBy { get; set; } = "";
}

public sealed class AuthorDuplicateSuggestion
{
    public const string Pending = "pending";
    public const string Deferred = "deferred";
    public const string Rejected = "rejected";
    public const string Merged = "merged";
    public const string Superseded = "superseded";

    public int Id { get; set; }
    // Keep these IDs after a merge for both audit and rejection history.
    public int FirstAuthorId { get; set; }
    public int SecondAuthorId { get; set; }
    public string Status { get; set; } = Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? KeptAuthorId { get; set; }
}
