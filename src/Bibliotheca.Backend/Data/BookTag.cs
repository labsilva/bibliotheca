using Diacritics.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class BookTag
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int TagId { get; set; }
    public string Tag { get; set; }
    public string TagSearch { get; set; }
    public virtual ICollection<Book> Books { get; set; }
}
