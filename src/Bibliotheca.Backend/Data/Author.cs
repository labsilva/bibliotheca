using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class Author
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int AuthorId { get; set; }
    public string AuthorName { get; set; }
    public string AuthorSearch { get; set; }
    public string? PhotoUrl { get; set; }
    public string? SourceAuthorKey { get; set; }
    public string? Biography { get; set; }
    public string? BiographySourceUrl { get; set; }
    public virtual ICollection<Book> Books { get; set; }
}
