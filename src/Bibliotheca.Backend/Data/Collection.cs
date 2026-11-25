using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class Collection
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CollectionId { get; set; }
    public string Name { get; set; }
    public virtual ICollection<Book> Books { get; set; }
}
