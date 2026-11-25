using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Fetchers;

public class WookBookFetcher : PlaywrightFetcher, IBookFetcher, IFetcherPriority
{
    private readonly IOptions<FetcherOptions> _options;
    private readonly ILogger<WookBookFetcher> _logger;

    public int Priority => 5;

    public WookBookFetcher(
        IOptions<FetcherOptions> fetcherOptions,
        IOptions<PlaywrightOptions> playwrightOptions,
        ILogger<WookBookFetcher> logger) : base(playwrightOptions)
    {
        _options = fetcherOptions;
        _logger = logger;
    }

    public async Task<FetchedBook> FetchBookAsync(
        string isbn, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching book metadata for isbn {isbn}", isbn);
        var (url, html) = await ReadPageAsync($"https://www.wook.pt/pesquisa?keyword={isbn}", cancellationToken);

        if(html.Contains("NÃO ENCONTROU O QUE PROCURA?"))
        {
            _logger.LogWarning("ISBN {isbn} not found", isbn);
        }

        if (_options.Value.SaveTempFiles)
        {
            var path = string.Concat(_options.Value.TempFilesSaveLocation, isbn, ".html");
            await File.WriteAllTextAsync(path, $"<!--{url}-->\n{html}", cancellationToken);
        }
        
        return ParseResponse(url, ref html);
    }
    
    public FetchedBook ParseResponse(string url, ref string content)
    {
        var location = Uri.TryCreate(url, UriKind.Absolute, out var address) ? address : WookCatalogue.Origin;
        if (WookCatalogue.IsWookAddress(url) &&
            location.AbsolutePath.TrimEnd('/') is "/pesquisa" or "/en/pesquisa" &&
            WookCatalogue.ReadSearch(new WookPage(url, 200, content), 1, CancellationToken.None).Count == 0)
            return new FetchedBook { Success = false, Url = url };

        var detail = WookCatalogue.ReadProduct(content, location);
        var book = new FetchedBook
        {
            Title = detail.Title,
            Subtitle = detail.Subtitle!,
            Isbn = detail.Isbn ?? "",
            Authors = detail.Authors,
            HasStructuredAuthors = detail.HasStructuredAuthors,
            PublishedBy = detail.Publisher!,
            PublishedOn = detail.PublishedOn,
            Url = url,
            Success = detail.Isbn is not null
        };
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataCollection, detail.Collection);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataTotalPages, detail.Pages);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataLanguage, detail.Language);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataSynopsis, detail.Synopsis);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataGenres, detail.Genres);
        return book;
    }
}

public static class WookExtensions
{
    public static string StripCharacters(this string text)
    {
        return text.Replace("\\n", string.Empty).Trim();
    }

    public static IEnumerable<string> ProcessGenres(this string text)
    {
        var parts = text.Split('>');
        yield return parts.Last().StripCharacters();
    }

    public static IEnumerable<string> ProcessAuthor(this string author)
    {
        foreach(var section in author.Split("\n"))
        {
            var text = section.Trim();
            if(text.StartsWith("de"))
            {
                text = text.Remove(0, 3);
            }
            if(text.Contains(" e "))
            {
                foreach (var subText in text.Split(" e "))
                {
                    if (string.IsNullOrEmpty(subText?.Trim()))
                    {
                        continue;
                    }
                    yield return subText.Trim();
                }
            }
            else
            {
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                else
                {
                    yield return text.Trim();
                }
                    
            }
        }
    }

    public static string SanitizeAuthor(this string author)
    {
        if (author.Trim().StartsWith("de"))
        {
            author = author.Remove(0, 3);
        }
        return author.Trim();
    }

    public static DateTime? ToDate(this string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var components = text.StripCharacters().Replace("de", string.Empty).Replace(",", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var monthText = components[0];
        var yearText = components[1];
        var day = 1;
        var year = int.Parse(yearText);
        var month = monthText.ToLower() switch
        {
            "janeiro" => 1,
            "fevereiro" => 2,
            "março" => 3,
            "marco" => 3,
            "abril" => 4,
            "maio" => 5,
            "junho" => 6,
            "julho" => 7,
            "agosto" => 8,
            "setembro" => 9,
            "outubro" => 10,
            "novembro" => 11,
            "dezembro" => 12,
            _ => throw new ArgumentOutOfRangeException("month")
        };

        return new DateTime(year, month, day);
    }

    public static DateTime? ToDateFromEnglishDate(this string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var components = text.StripCharacters().Replace("de", string.Empty).Replace(",", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var monthText = components[0];
        var yearText = components[2];
        var day = 1;
        var year = int.Parse(yearText);
        var month = monthText.ToLower() switch
        {
            "january" => 1,
            "february" => 2,
            "march" => 3,
            "april" => 4,
            "may" => 5,
            "june" => 6,
            "july" => 7,
            "august" => 8,
            "september" => 9,
            "october" => 10,
            "november" => 11,
            "december" => 12,
            _ => throw new ArgumentOutOfRangeException("month")
        };

        return new DateTime(year, month, day);
    }
}

