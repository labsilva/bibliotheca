using Bibliotheca.Backend.Data;
using Diacritics.Extensions;

namespace Bibliotheca.Backend.Migrations;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class SearchFieldsBackfill
{
    private readonly BibliothecaDbContext _context;

    public SearchFieldsBackfill(BibliothecaDbContext context)
    {
        _context = context;

        foreach (var book in context.Books)
        {
            book.Search = string.Format("{0} - {1}", book.Title.RemoveDiacritics().ToLower(), book.Subtitle?.RemoveDiacritics().ToLower());
        }

        foreach (var author in context.Authors)
        {
            author.AuthorSearch = author.AuthorName?.RemoveDiacritics().ToLower();
        }

        foreach (var shelve in context.Shelves)
        {
            shelve.ShelveSearch = shelve.ShelveName?.RemoveDiacritics().ToLower();
        }

        foreach (var series in context.BookSeries)
        {
            series.Search = series.Name?.RemoveDiacritics().ToLower();
        }

        context.SaveChanges();
    }
}
