namespace Bibliotheca.Backend.Models;

public class RegisterViewModel
{
    public string? InviteCode { get; set; }
    public string Username { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }
}
