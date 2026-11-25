using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Bibliotheca.Backend.Services;

public class StoreFetchedBookService : IServiceExecutor<StoreFetchedBookArgs, StoreFetchedBookResult>
{
    private readonly BibliothecaDbContext _dbContext;
    private readonly ILogger<StoreFetchedBookService> _logger;
    private readonly IAuthorProfileLookup _authorProfiles;

    public StoreFetchedBookService(
        BibliothecaDbContext dbContext,
        ILogger<StoreFetchedBookService> logger,
        IAuthorProfileLookup authorProfiles)
    {
        _dbContext = dbContext;
        _logger = logger;
        _authorProfiles = authorProfiles;
    }

    public async Task<StoreFetchedBookResult> ExecuteAsync(
        StoreFetchedBookArgs args, 
        CancellationToken cancellationToken = default)
    {
        args.Book = args.Book with
        {
            Isbn = args.Book.Isbn.Replace("-", string.Empty)
        };
        var book = await _dbContext
            .Books
            .AsSplitQuery()
            .AsTracking()
            .Include(i => i.BookCover)
            .Include(i => i.Authors)
            .Include(i => i.Collection)
            .Include(i => i.ExternalLinks)
            .Include(i => i.Publisher)
            .Include(i => i.Genres)
            .FirstOrDefaultAsync(a => a.Isbn.Equals(args.Book.Isbn), cancellationToken: cancellationToken);

        if(book is null)
        {
            _logger.LogInformation("Creating new book");
            book = await ProcessNewBook(args, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Updating book");
            book = await ProcessExistingBook(book, args, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new StoreFetchedBookResult { Result = true, BookId = book.BookId };
    }

    public async Task<bool> RefreshExistingAsync(Book book, StoreFetchedBookArgs args, CancellationToken cancellationToken)
    {
        var fetched = args.Book;
        var previous = (book.Title, book.Subtitle, book.PublishedOn, book.Publisher, book.Collection, book.Genres?.Count ?? 0);
        book.LastMetadataRefreshOn = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(fetched.Title))
            book.Title = fetched.Title;
        if (!string.IsNullOrWhiteSpace(fetched.Subtitle))
            book.Subtitle = fetched.Subtitle;
        if (fetched.PublishedOn.HasValue)
            book.PublishedOn = fetched.PublishedOn;
        if (!string.IsNullOrWhiteSpace(fetched.PublishedBy) && fetched.PublishedBy != book.Publisher?.Name)
            await ProcessPublisherAsync(book, args, cancellationToken);

        var coverChanged = RefreshCover(book, args.Cover);
        var metadataChanged = MergeMetadata(book, fetched);
        var authorsChanged = await ProcessAuthorsAsync(book, args, cancellationToken, useExistingAuthors: true);
        await ProcessMetadataAsync(book, args, cancellationToken);
        var changed = coverChanged || metadataChanged || authorsChanged ||
            previous != (book.Title, book.Subtitle, book.PublishedOn, book.Publisher, book.Collection, book.Genres?.Count ?? 0);
        if (changed)
            book.ModifiedOn = book.LastMetadataRefreshOn;
        return changed;
    }

    private static bool RefreshCover(Book book, FetchedCover? cover)
    {
        if (cover is not { Success: true, Image.Length: > 0 } || string.IsNullOrWhiteSpace(cover.MimeType))
            return false;

        var changed = book.BookCover?.MimeType != cover.MimeType ||
            !cover.Image.AsSpan().SequenceEqual(book.BookCover?.Cover);
        if (changed)
        {
            book.BookCover ??= new BookCover();
            book.BookCover.Cover = cover.Image;
            book.BookCover.MimeType = cover.MimeType;
        }

        if (string.IsNullOrWhiteSpace(cover.Url))
            return changed;

        var link = book.ExternalLinks.FirstOrDefault(link => link.Notes == "Book cover");
        if (link?.Url == cover.Url)
            return changed;

        if (link is null)
            book.ExternalLinks.Add(new BookExternalLinks { Url = cover.Url, Notes = "Book cover" });
        else
            link.Url = cover.Url;
        return true;
    }

    private static bool MergeMetadata(Book book, FetchedBook fetched)
    {
        Dictionary<string, object>? metadata = null;
        foreach (var (key, value) in fetched.Metadata)
        {
            if (value is null ||
                value is string text && string.IsNullOrWhiteSpace(text) ||
                value is string[] values && !values.Any(text => !string.IsNullOrWhiteSpace(text)) ||
                value is System.Collections.ICollection { Count: 0 })
                continue;

            if (book.AdditionalData?.TryGetValue(key, out var current) == true &&
                JsonSerializer.Serialize(current) == JsonSerializer.Serialize(value))
                continue;

            metadata ??= new Dictionary<string, object>(book.AdditionalData ?? []);
            metadata[key] = value;
        }
        if (metadata is not null)
            book.AdditionalData = metadata;
        return metadata is not null;
    }

    private async Task<Book> ProcessExistingBook(
        Book book,
        StoreFetchedBookArgs args, 
        CancellationToken cancellationToken)
    {
        book.LastMetadataRefreshOn = DateTime.Now;
        book.ModifiedOn = DateTime.Now;
        book.PublishedOn = args.Book.PublishedOn;
        if (args.Cover is not null)
        {
            if (book.BookCover is not null)
            {
                book.BookCover = new BookCover
                {
                    Cover = args.Cover.Image,
                    MimeType = args.Cover.MimeType,
                };
                var externalLinkBookCover = book.ExternalLinks.FirstOrDefault(a => a.Notes.Equals("Book cover"));
                if (externalLinkBookCover is not null)
                {
                    externalLinkBookCover.Url = args.Cover.Url;
                }
                else
                {
                    book.ExternalLinks.Add(new BookExternalLinks { Url = args.Cover.Url, Notes = "Book cover" });
                }
            }
        }
        book.AdditionalData = args.Book.Metadata;
        
        await ProcessAuthorsAsync(book, args, cancellationToken);
        await ProcessPublisherAsync(book, args, cancellationToken);
        await ProcessMetadataAsync(book, args, cancellationToken);
        return book;
    }

    internal async Task<Book> ProcessNewBook(StoreFetchedBookArgs args, CancellationToken cancellationToken)
    {
        var book = new Book
        {
            Title = args.Book.Title,
            Subtitle = args.Book.Subtitle,
            Isbn = args.Book.Isbn,
            Authors = new List<Author>(),
            CreatedAt = DateTime.Now,
            CreatedBy = "System",
            ModifiedBy = "System",
            ModifiedOn = DateTime.Now,
            LastMetadataRefreshOn = DateTime.Now,
            ExternalLinks = new List<BookExternalLinks>(),
            Genres = new List<BookGenre>(),
            PublishedOn = args.Book.PublishedOn,
        };

        if (args.Cover != null)
        {
            book.BookCover = new BookCover
            {
                Cover = args.Cover.Image,
                MimeType = args.Cover.MimeType
            };
            book.ExternalLinks.Add(new BookExternalLinks { Url = args.Cover.Url, Notes = "Book cover" });
        }

        book.ExternalLinks.Add(new BookExternalLinks { Url = args.Book.Url, Notes = "Metadata fetched from" });
        book.AdditionalData = args.Book.Metadata;

        await ProcessAuthorsAsync(book, args, cancellationToken);
        await ProcessPublisherAsync(book, args, cancellationToken);
        await ProcessMetadataAsync(book, args, cancellationToken);
        await _dbContext.Books.AddAsync(book, cancellationToken);
        return book;
    }

    private async Task ProcessMetadataAsync(
        Book book,
        StoreFetchedBookArgs args,
        CancellationToken cancellationToken)
    {
        if (args.Book.Metadata.TryGetValue(FetchedBook.FetchedBookMetadataGenres, out var genres) &&
            genres is string[] genreNames)
        {
            book.Genres ??= new List<BookGenre>();
            foreach (var name in genreNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct())
            {
                if (book.Genres.Any(genre => genre.Genre == name))
                    continue;
                var genre = _dbContext.Genres.Local.FirstOrDefault(genre => genre.Genre == name)
                    ?? await _dbContext.Genres.FirstOrDefaultAsync(genre => genre.Genre == name, cancellationToken);
                if (genre is null)
                    _dbContext.Genres.Add(genre = new BookGenre { Genre = name });
                book.Genres.Add(genre);
            }
        }

        if (args.Book.Metadata.TryGetValue(FetchedBook.FetchedBookMetadataCollection, out var collection) &&
            collection?.ToString() is { } collectionName && !string.IsNullOrWhiteSpace(collectionName) &&
            book.Collection?.Name != collectionName)
        {
            _logger.LogDebug("Collection metadata present, processing");
            var findCollectionQuery = _dbContext.Collections.Where(w => w.Name.Equals(collectionName));
            var collectionData = await findCollectionQuery.FirstOrDefaultAsync(cancellationToken);
            if (collectionData is not null)
            {
                _logger.LogDebug("Found collection in database");
                book.Collection = collectionData;
            }
            else
            {
                _logger.LogDebug("Collection not found in database, creating");
                collectionData = new Collection
                {
                    Name = collectionName
                };
                book.Collection = collectionData;
                await _dbContext.Collections.AddAsync(collectionData, cancellationToken);
            }
        }
    }

    private async Task<bool> ProcessAuthorsAsync(
        Book book,
        StoreFetchedBookArgs args,
        CancellationToken cancellationToken,
        bool useExistingAuthors = false)
    {
        book.Authors ??= new List<Author>();
        var changed = false;
        var incompleteProfiles = new List<Author>();
        var names = (args.Book.Authors ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal).ToArray();
        string[][] evidence = args.Book.HasStructuredAuthors && names.Length > 0 ? [names] : [];
        if (useExistingAuthors && names.Length == 0)
            names = book.Authors.Select(author => author.AuthorName).Distinct().ToArray();

        foreach (var name in names)
        {
            var author = await AuthorCreditRepair.FindOrCreateAuthorAsync(_dbContext, book, name, cancellationToken);
            if (!book.Authors.Contains(author))
            {
                book.Authors.Add(author);
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(author.PhotoUrl) || string.IsNullOrWhiteSpace(author.Biography)
                || string.IsNullOrWhiteSpace(author.BiographySourceUrl))
                incompleteProfiles.Add(author);
        }

        if (book.BookId != 0)
        {
            var repaired = await AuthorCreditRepair.RepairBookAsync(_dbContext, book, evidence,
                WookCatalogue.IsWookAddress(args.Book.Url), null, cancellationToken);
            changed |= repaired > 0;
            incompleteProfiles.RemoveAll(author => !book.Authors.Contains(author));
        }

        if (incompleteProfiles.Count == 0)
            return changed;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var namesToEnrich = incompleteProfiles.Select(author => author.AuthorName).ToArray();
            var profiles = await _authorProfiles.FindProfilesAsync(args.Book, namesToEnrich, deadline.Token).WaitAsync(deadline.Token);
            foreach (var author in incompleteProfiles)
            {
                if (profiles.TryGetValue(author.AuthorName, out var profile))
                    changed |= profile.ApplyTo(author);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not look up profiles for imported book authors.");
        }
        return changed;
    }

    private async Task ProcessPublisherAsync(
        Book book,
        StoreFetchedBookArgs args, 
        CancellationToken cancellationToken)
    {
        var findPublisherQuery = _dbContext.Publishers.Where(w => w.Name.Equals(args.Book.PublishedBy));
        var publisherData = await findPublisherQuery.FirstOrDefaultAsync(cancellationToken);
        if (publisherData is not null)
        {
            _logger.LogDebug("Publisher found in database");
            book.Publisher = publisherData;
        }
        else
        {
            _logger.LogDebug("Publisher not found, creating");
            var newPublisher = new Publisher
            {
                Name = args.Book.PublishedBy,
            };
            book.Publisher = newPublisher;
            await _dbContext.Publishers.AddAsync(newPublisher, cancellationToken);
        }
    }
}

public record StoreFetchedBookArgs
{
    public FetchedBook Book { get; set; }
    public FetchedCover? Cover { get; set; }
}

public record StoreFetchedBookResult
{
    public bool Result { get; init; }
    public int? BookId { get; init; }
}
