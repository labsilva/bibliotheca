namespace Bibliotheca.Backend.Models;

public class PaginatedResult<TResult>
{
    public int Page { get; set; }
    public int Total { get; set; }
    public TResult[] Items { get; set; }
}
