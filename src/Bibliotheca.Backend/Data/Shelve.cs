using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

[PrimaryKey(nameof(ShelveId), nameof(UserId))]
public class Shelve
{
    public int ShelveId { get; set; }
    public int UserId { get; set; }
    public string ShelveName { get; set; }
    public string ShelveSearch { get; set; }

    [NotMapped]
    public bool IsFavorites => ShelveName.Equals("Favorites", StringComparison.OrdinalIgnoreCase) ||
        ShelveName.Equals("Favourites", StringComparison.OrdinalIgnoreCase) ||
        ShelveName.Equals("Favoritos", StringComparison.OrdinalIgnoreCase);
    public virtual User User { get; set; }
    public virtual ICollection<UserBook> Books { get; set; }
}
