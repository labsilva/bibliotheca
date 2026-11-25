namespace Bibliotheca.Backend.Models;

public class BookIndexItem
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string? Subtitle { get; set; }
    public int? SeriesId { get; set; }
    public string? Series { get; set; }
    public int? AuthorId { get; set; }
    public string Author { get; set; }
    public string Status { get; set; }
    public string Cover { get; set; }
}
