namespace Bibliotheca.Backend.Models;

public class CollectionDetailsModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public BookIndexItem[] Books { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? BooksTotal { get; set; }
}
