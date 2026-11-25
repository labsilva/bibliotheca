using Bibliotheca.Backend.Initialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using System.Net;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    [Fact]
    public async Task PrivateStaticDirectoriesNeverServeFilesEvenWithKnownMimeTypes()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-private-files-");
        try
        {
            var webRoot = Directory.CreateDirectory(Path.Combine(directory.FullName, "webroot"));
            Directory.CreateDirectory(Path.Combine(webRoot.FullName, "data"));
            Directory.CreateDirectory(Path.Combine(webRoot.FullName, "temp"));
            foreach (var path in new[] { "data/canary.png", "temp/canary.html", "data/database.db", "temp/trace.txt" })
                await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, path), "PRIVATE-CANARY");
            await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, "public.txt"), "PUBLIC-CANARY");
            await using var original = CreateFactory(directory);
            await using var factory = original.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot.FullName));
            using var client = CreateClient(factory);
            foreach (var path in new[] { "/data/canary.png", "/temp/canary.html", "/data/database.db", "/temp/trace.txt", "/DATA/canary.png", "/TEMP/canary.html" })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain("PRIVATE-CANARY", await response.Content.ReadAsStringAsync());
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
            Assert.Equal("PUBLIC-CANARY", await client.GetStringAsync("/public.txt"));
        }
        finally { directory.Delete(true); }
    }
}

public sealed class PrivateStorageTests
{
    [Theory]
    [InlineData("Data Source=wwwroot/data/catalogue.db")]
    [InlineData("Data Source=wwwroot/logs/catalogue.db")]
    [InlineData("Data Source=file:wwwroot/data/catalogue.db")]
    [InlineData("Data Source=file:/private/catalogue.db")]
    public void ProductionRejectsPublicOrAmbiguousDatabasePaths(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => PrivateStorageValidation.Validate(new StorageEnvironment(), connectionString));

    [Theory]
    [InlineData("Data Source=data/catalogue.db")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=fixture;Mode=Memory")]
    public void ProductionAcceptsPrivateFilesystemPathsAndMemoryDatabases(string connectionString) =>
        PrivateStorageValidation.Validate(new StorageEnvironment(), connectionString);

    internal sealed class StorageEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Bibliotheca";
        public string ContentRootPath { get; set; } = Path.GetFullPath("private-storage-fixture");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetFullPath("private-storage-fixture/wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
