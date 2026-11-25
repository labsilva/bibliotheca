using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;
public class BookExternalLinks
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string ExternalLinkId { get; set; }
    public int BookId { get; set; }
    public string Url { get; set; }
    public string Notes { get; set; }
    //public virtual Fetcher Fetcher { get; set; }
}
