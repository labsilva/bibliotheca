using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class BookGenre
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int BookGenreId { get; set; }
    public string Genre { get; set; }
    public virtual ICollection<Book> Books { get; set; }
}
