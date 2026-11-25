using Bibliotheca.Backend.Data;
using Diacritics.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SearchController : Controller
{
    private const int MaxSearchLength = 200;
    private const int MaxResults = 50;

    /// <summary>Search the catalogue and return a bounded, ordered list of matches.</summary>
    [HttpGet]
    [ProducesResponseType<SearchResultItem[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAsync(
        [FromServices] BibliothecaDbContext dbContext,
        [FromQuery] string? text,
        CancellationToken cancellationToken)
    {
        var searchText = text?.RemoveDiacritics().Trim().ToLower() ?? string.Empty;
        if (searchText.Length is 0 or > MaxSearchLength)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(text)] = [$"The normalized search term must contain between 1 and {MaxSearchLength} characters."]
            }));

        var books = dbContext.Books
            .Where(book => book.Search.Contains(searchText) || book.Isbn.Contains(searchText))
            .Select(book => new
            {
                Area = SearchArea.Book,
                Id = book.BookId,
                Title = book.Title,
                ExactIsbnMatch = book.Isbn == searchText
            });

        var series = dbContext.BookSeries
            .Where(series => series.Search.Contains(searchText))
            .Select(series => new
            {
                Area = SearchArea.Series,
                Id = series.SeriesId,
                Title = series.Name,
                ExactIsbnMatch = false
            });

        var collections = dbContext.Collections
            .Where(collection => collection.Name.Contains(searchText))
            .Select(collection => new
            {
                Area = SearchArea.Collection,
                Id = collection.CollectionId,
                Title = collection.Name,
                ExactIsbnMatch = false
            });

        var authors = dbContext.Authors
            .Where(author => author.AuthorSearch.Contains(searchText))
            .Select(author => new
            {
                Area = SearchArea.Author,
                Id = author.AuthorId,
                Title = author.AuthorName,
                ExactIsbnMatch = false
            });

        // Shelf links use names as identifiers, so collapse duplicate names before limiting the results.
        var username = User.GetUsername();
        var shelves = dbContext.Shelves
            .Where(shelf => username != "" && shelf.User.Name == username && shelf.ShelveSearch.Contains(searchText))
            .Select(shelf => new
            {
                Area = SearchArea.Shelve,
                Id = 0,
                Title = shelf.ShelveName,
                ExactIsbnMatch = false
            })
            .Distinct();

        var matches = await books.Concat(series).Concat(collections).Concat(authors).Concat(shelves)
            .OrderByDescending(match => match.ExactIsbnMatch)
            .ThenBy(match => match.Area)
            .ThenBy(match => match.Title)
            .ThenBy(match => match.Id)
            .Take(MaxResults)
            .ToArrayAsync(cancellationToken);

        var results = Array.ConvertAll(matches, match => new SearchResultItem(
            match.Area.ToString(),
            match.Area == SearchArea.Shelve ? match.Title : match.Id.ToString(),
            match.Title,
            match.Area switch
            {
                SearchArea.Book => $"/api/books/{match.Id}/cover",
                _ => string.Empty
            }));
        return Ok(results);
    }

    private enum SearchArea
    {
        Book,
        Series,
        Collection,
        Author,
        Shelve
    }

    /// <summary>A catalogue match with its navigation identifier and optional image path.</summary>
    public sealed record SearchResultItem(string Area, string Id, string Title, string ImagePath);
}
