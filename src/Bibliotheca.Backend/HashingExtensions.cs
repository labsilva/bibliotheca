using System.IO;

namespace Bibliotheca.Backend;

public static class HashingExtensions
{
    public static string Sha256(this string text)
    {
        var sha = System.Security.Cryptography.SHA256.Create();
        byte[] checksum = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return BitConverter.ToString(checksum).Replace("-", String.Empty).ToLower();
        
    }
}
