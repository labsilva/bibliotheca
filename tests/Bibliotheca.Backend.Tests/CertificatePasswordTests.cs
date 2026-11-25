using Bibliotheca.Backend.Security;
using Microsoft.Extensions.Configuration;

namespace Bibliotheca.Backend.Tests;

public class CertificatePasswordTests
{
    [Theory]
    [InlineData(" password \n", " password ")]
    [InlineData("password\r\n", "password")]
    [InlineData("password", "password")]
    public void ReadsMountedSecretAndPreservesPasswordWhitespace(string contents, string expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, contents);
            Assert.Equal(expected, CertificatePassword.Read(Section(null, path)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsAmbiguousSourcesBeforeReadingSecret()
    {
        Assert.Throws<InvalidOperationException>(() => CertificatePassword.Read(Section("password", "missing-file")));
    }

    [Fact]
    public void RetainsExistingPasswordConfiguration()
    {
        Assert.Equal(" password ", CertificatePassword.Read(Section(" password ", null)));
    }

    [Fact]
    public void MissingSecretFailsWithoutFallingBackToEmptyPassword()
    {
        Assert.Throws<FileNotFoundException>(() => CertificatePassword.Read(Section(null, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()))));
    }

    private static IConfigurationSection Section(string? password, string? file) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Certificate:Password"] = password,
            ["Certificate:PasswordFile"] = file
        }).Build().GetSection("Certificate");
}
