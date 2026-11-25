namespace Bibliotheca.Backend.Fetchers;

public interface ICoverFetcher
{
    Task<FetchedCover> FetchCoverAsync(
        string isbn,
        CancellationToken cancellationToken);
}

public record FetchedCover
{
    public bool Success { get; set; }
    public string Url { get; set; }
    public byte[] Image { get; set; }
    public string MimeType { get; set; }
}