using Diacritics.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bibliotheca.Backend.Data;

public class DiacriticsInterceptor : SaveChangesInterceptor, ISaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            Normalize(eventData.Context);
        return base.SavingChanges(eventData, result);
    }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            Normalize(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Normalize(DbContext ctx)
    {
        foreach (var e in ctx.ChangeTracker.Entries<BookTag>()
                 .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            e.Entity.TagSearch = e.Entity.Tag.RemoveDiacritics().ToLower();
        }

        foreach (var e in ctx.ChangeTracker.Entries<Book>()
                 .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            e.Entity.Search = string.Format("{0} - {1}", e.Entity.Title.RemoveDiacritics().ToLower(), e.Entity.Subtitle?.RemoveDiacritics().ToLower());
        }

        foreach (var e in ctx.ChangeTracker.Entries<Author>()
                 .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            e.Entity.AuthorSearch = e.Entity.AuthorName.RemoveDiacritics().ToLower();
        }

        foreach (var e in ctx.ChangeTracker.Entries<BookSeries>()
                 .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            e.Entity.Search = e.Entity.Name.RemoveDiacritics().ToLower();
        }

        foreach (var e in ctx.ChangeTracker.Entries<Shelve>()
                 .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            e.Entity.ShelveSearch = e.Entity.ShelveName.RemoveDiacritics().ToLower();
        }
    }
}
