using System.ComponentModel.DataAnnotations;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Bibliotheca.Backend.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/fetch")]
public class FetchController : Controller
{
    private readonly ILogger<FetchController> _logger;

    public FetchController(ILogger<FetchController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    [Route("")]
    [Authorize(Policy = CataloguePolicies.Manage)]
    public async Task<IActionResult> Fetch(
        [FromServices] IEnumerable<IBookFetcher> fetchers,
        string isbn,
        CancellationToken cancellationToken)
    {
        var books = new List<FetchedBook>();
        foreach(var fetcher in fetchers)
        {
            books.AddRange(await fetcher.FetchBookAsync(isbn, cancellationToken));
        }
        return Ok(books);
    }

    [HttpPut]
    [RequestSizeLimit(1024 * 1024)]
    [Authorize(Policy = CataloguePolicies.Import)]
    [Route("isbn")]
    public Task<IActionResult> ProcessIsbnsAsync(
        [FromServices] BookImportQueue bookImportQueue,
        ProcessIsbnsBody request,
        CancellationToken cancellationToken)
        => bookImportQueue.SubmitAsync(User.GetUsername(), request.Isbns, cancellationToken, request.ShelfId, request.BatchId);

    /// <summary>ISBNs to queue and an optional destination shelf owned by the caller.</summary>
    public sealed record ProcessIsbnsBody
    {
        public required string[] Isbns { get; init; }

        [Range(1, int.MaxValue)]
        public int? ShelfId { get; init; }
        public Guid? BatchId { get; init; }
    }

[HttpPost]
    [Authorize(Policy = CataloguePolicies.Manage)]
    [Route("process")]
    public async Task<IActionResult> ProcessExisting(
        [FromServices]IEnumerable<ICoverFetcher> coverFetchers,
        [FromServices]StoreFetchedBookService storeFetchedBookService,
        [FromServices]WookBookFetcher wookBookFetcher,
        [FromServices]IOptions<FetcherOptions>fetcherOptions,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, bool>(100);
        if (!Directory.Exists(fetcherOptions.Value.TempFilesSaveLocation))
            return Ok(results);

        foreach(var file in Directory.GetFiles(fetcherOptions.Value.TempFilesSaveLocation, "*.html"))
        {
            _logger.LogInformation("Processing file {file}", file);
            var lines = System.IO.File.ReadAllLines(file);
            var url = lines[0].Replace("<!--", string.Empty).Replace("-->", string.Empty).Trim();
            var content = System.IO.File.ReadAllText(file);
            var book = wookBookFetcher.ParseResponse(url, ref content);
            var isbn = book.Isbn.Replace("-", string.Empty);
            try
            {
                var coverImage = System.IO.Directory.GetFiles(fetcherOptions.Value.TempFilesSaveLocation, $"{isbn}.*").Where(w => !w.EndsWith(".html")).First();
                var cover = new FetchedCover
                {
                    Image = await System.IO.File.ReadAllBytesAsync(coverImage),
                    MimeType = "image/jpeg",
                    Success = true,
                    Url = string.Empty
                };
                var args = new StoreFetchedBookArgs
                {
                    Book = book,
                    Cover = cover
                };
                var result = await storeFetchedBookService.ExecuteAsync(args, cancellationToken);
                results.Add(isbn, result.Result);
            }
            catch (Exception ex)
            {
                results.Add(isbn, false);
            }
        }
        return Ok(results);
    }
}
