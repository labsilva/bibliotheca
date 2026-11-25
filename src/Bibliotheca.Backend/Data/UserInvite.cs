using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bibliotheca.Backend.Data;

public class UserInvite
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int InviteId { get; set; }
    public string InviteCode { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset InviteExpiresOn { get; set; }
}
