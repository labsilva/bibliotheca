namespace Bibliotheca.Backend.Models;

public class AuthorDetailsModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Biography { get; set; }
    public string? BiographySourceUrl { get; set; }
    public BookIndexItem[] Books { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? BooksTotal { get; set; }
}
