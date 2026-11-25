using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class BookSeries
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SeriesId { get; set; }
    public string Name { get; set; }
    public string Search { get; set; }
    public virtual ICollection<Book> Books { get; set; }
}
