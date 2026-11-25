using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Data;

[PrimaryKey(nameof(UserId), nameof(BookId))]
public class UserBook
{
    public int UserId { get; set; }
    public int BookId { get; set; }
    public int ShelveId { get; set; }
    public virtual User User { get; set; }
    public virtual Book Book { get; set; }
    public BookReadingStatus ReadingStatus { get; set; }
    public DateTime? ReadingTimestamp { get; set; }
    public short? Rating { get; set; }
    public string? Comments { get; set; }
    public bool Favorite { get; set; }
    public DateTime? AddedToFavoritesAt { get; set; }
    public Shelve Shelve { get; set; }
    public DateTime? ModifiedOn { get; set; }
}