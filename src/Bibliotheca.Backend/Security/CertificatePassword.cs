namespace Bibliotheca.Backend.Security;

public static class CertificatePassword
{
    public static string? Read(IConfigurationSection configuration)
    {
        var file = configuration["PasswordFile"];
        var password = configuration["Password"];
        if (string.IsNullOrWhiteSpace(file)) return password;
        if (!string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"{configuration.Path}: configure either Password or PasswordFile, not both.");
        // Text secret files commonly end in one newline; preserve other whitespace.
        var contents = File.ReadAllText(file);
        if (contents.EndsWith("\r\n", StringComparison.Ordinal)) return contents[..^2];
        return contents.EndsWith('\n') ? contents[..^1] : contents;
    }
}
