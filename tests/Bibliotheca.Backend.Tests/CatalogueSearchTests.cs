using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Mvc;
using static Bibliotheca.Backend.Tests.TestDatabase;

namespace Bibliotheca.Backend.Tests;

public class CatalogueSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task SearchRejectsMissingAndBlankTerms(string? text)
    {
        await using var db = await CreateDatabaseAsync();

        var response = await new SearchController().SearchAsync(db, text, default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(response).Value);
        Assert.NotEmpty(problem.Errors["text"]);
    }

    [Fact]
    public async Task SearchValidatesLengthAfterNormalizingTheTerm()
    {
        await using var db = await CreateDatabaseAsync();
        var controller = new SearchController();
        var title = new string('a', 200);
        db.Books.Add(BookWithTitle(1, title));
        await db.SaveChangesAsync();

        var matches = Matches(await controller.SearchAsync(db, $"  {new string('Á', 200)}  ", default));
        var tooLong = await controller.SearchAsync(db, new string('Á', 201), default);

        Assert.Equal(title, Assert.Single(matches).Title);
        Assert.IsType<BadRequestObjectResult>(tooLong);
    }

    [Fact]
    public async Task SearchAppliesOneLimitAcrossCategoriesAndBreaksTitleTiesById()
    {
        await using var db = await CreateDatabaseAsync();
        db.Books.AddRange(Enumerable.Range(1, 29).Reverse().Select(id => BookWithTitle(id, "Match Z")));
        db.Books.Add(BookWithTitle(40, "Match A"));
        db.Authors.AddRange(Enumerable.Range(1, 30).Reverse().Select(id => new Author
        {
            AuthorId = id,
            AuthorName = "Match author"
        }));
        await db.SaveChangesAsync();
        var controller = new SearchController();

        var first = Matches(await controller.SearchAsync(db, "match", default));
        var repeated = Matches(await controller.SearchAsync(db, " MATCH ", default));

        Assert.Equal(50, first.Length);
        Assert.Equal(first, repeated);
        Assert.Equal("Match A", first[0].Title);
        Assert.All(first.Take(30), item => Assert.Equal("Book", item.Area));
        Assert.Equal(Enumerable.Range(1, 29).Select(id => id.ToString()), first.Skip(1).Take(29).Select(item => item.Id));
        Assert.All(first.Skip(30), item => Assert.Equal("Author", item.Area));
        Assert.Equal(Enumerable.Range(1, 20).Select(id => id.ToString()), first.Skip(30).Select(item => item.Id));
    }

    [Fact]
    public async Task ExactIsbnMatchComesBeforeMatchingTitles()
    {
        await using var db = await CreateDatabaseAsync();
        const string isbn = "9780140328721";
        db.Books.AddRange(Enumerable.Range(1, 55).Select(id => BookWithTitle(id, $"Title containing {isbn}")));
        db.Books.Add(BookWithTitle(56, "Z title", isbn));
        await db.SaveChangesAsync();

        var results = Matches(await new SearchController().SearchAsync(db, isbn, default));

        Assert.Equal("56", results[0].Id);
    }

    private static SearchController.SearchResultItem[] Matches(IActionResult result)
        => Assert.IsType<SearchController.SearchResultItem[]>(Assert.IsType<OkObjectResult>(result).Value);

    private static Book BookWithTitle(int id, string title, string isbn = "") => new()
    {
        BookId = id,
        Title = title,
        Isbn = isbn,
        CreatedBy = "test",
        ModifiedBy = "test"
    };
}
