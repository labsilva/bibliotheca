using System.ComponentModel.DataAnnotations;

namespace Bibliotheca.Backend.Data;

public class BookCover
{
    [Key]
    public int BookId { get; set; }
    public string MimeType { get; set; }
    public byte[] Cover { get; set; }
}
