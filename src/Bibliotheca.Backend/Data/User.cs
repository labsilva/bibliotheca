using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Password { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Settings { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<UserBook> Books { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public virtual ICollection<Shelve> Shelves { get; set; }
}
