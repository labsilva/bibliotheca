using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class CoverCacheTests
{
    [Theory]
    [InlineData("3600", "public, max-age=3600")]
    [InlineData("0", "public, max-age=0")]
    public async Task CoverUsesConfiguredCacheDuration(string seconds, string expected)
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var book = new Book { Title = "Cover", Isbn = "1", CreatedBy = "test", ModifiedBy = "test",
            BookCover = new BookCover { Cover = [1], MimeType = "image/png" } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CoverCache:MaxAgeSeconds"] = seconds
        }).Build();
        var controller = new BooksController(NullLogger<BooksController>.Instance, config)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<FileContentResult>(await controller.CoverAsync(db, book.BookId, default));
        Assert.Equal(expected, controller.Response.Headers.CacheControl.ToString());
    }
}
