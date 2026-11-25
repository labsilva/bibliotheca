using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class Book
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int BookId { get; set; }
    public string Isbn { get; set; }
    public string Title { get; set; }
    public string? Subtitle { get; set; }
    public string Search { get; set; }
    public virtual ICollection<Author>? Authors { get; set; }
    public virtual ICollection<BookGenre>? Genres { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
    public virtual BookCover? BookCover { get; set; }
    public virtual BookSeries? Series { get; set; }
    public virtual Collection? Collection { get; set; }
    public virtual Publisher? Publisher { get; set; }
    public DateTime? PublishedOn { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
    public DateTime LastMetadataRefreshOn { get; set; }
    public virtual ICollection<BookExternalLinks> ExternalLinks { get; set; }
    public virtual ICollection<BookTag> Tags { get; set; } = new List<BookTag>();
    [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<UserBook> Users { get; set; }
}

public enum BookReadingStatus
{
    Read,
    Reading,
    Unread,
}
