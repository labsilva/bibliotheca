using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class Fetcher
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FetcherId { get; set; }
    public string FetcherName { get; set; }
}
