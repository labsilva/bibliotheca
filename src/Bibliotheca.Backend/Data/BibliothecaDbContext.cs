using Diacritics.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OpenIddict.EntityFrameworkCore.Models;
using System.Text.Json;

namespace Bibliotheca.Backend.Data;

public class BibliothecaDbContext : DbContext
{
    public BibliothecaDbContext(DbContextOptions<BibliothecaDbContext> options) : base(options)
    {

    }
    public DbSet<OpenIddictEntityFrameworkCoreApplication> Applications => Set<OpenIddictEntityFrameworkCoreApplication>();
    public DbSet<OpenIddictEntityFrameworkCoreAuthorization> Authorizations => Set<OpenIddictEntityFrameworkCoreAuthorization>();
    public DbSet<OpenIddictEntityFrameworkCoreScope> Scopes => Set<OpenIddictEntityFrameworkCoreScope>();
    public DbSet<OpenIddictEntityFrameworkCoreToken> Tokens => Set<OpenIddictEntityFrameworkCoreToken>();

    public DbSet<Author> Authors { get; set; }
    public DbSet<AuthorDuplicateScan> AuthorDuplicateScans => Set<AuthorDuplicateScan>();
    public DbSet<AuthorDuplicateSuggestion> AuthorDuplicateSuggestions => Set<AuthorDuplicateSuggestion>();
    public DbSet<AuthorRefresh> AuthorRefreshes => Set<AuthorRefresh>();
    public DbSet<AuthorRefreshItem> AuthorRefreshItems => Set<AuthorRefreshItem>();
    public DbSet<Book> Books { get; set; }
    public DbSet<BookImport> BookImports => Set<BookImport>();
    public DbSet<CatalogueRefresh> CatalogueRefreshes => Set<CatalogueRefresh>();
    public DbSet<CatalogueRefreshItem> CatalogueRefreshItems => Set<CatalogueRefreshItem>();
    public DbSet<BookEditionReference> BookEditionReferences => Set<BookEditionReference>();
    public DbSet<BookSeries> BookSeries { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<BookGenre> Genres { get; set; }
    public DbSet<Publisher> Publishers { get; set; }
    public DbSet<BookTag> Tags { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserInvite> UserInvites { get; set; }
    public DbSet<UserBook> UsersBooks { get; set; }
    public DbSet<Shelve> Shelves { get; set; }

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.UseOpenIddict();
        modelBuilder.Entity<AuthorDuplicateScan>().HasIndex(row => row.ActiveSlot).IsUnique();
        modelBuilder.Entity<AuthorDuplicateSuggestion>().HasIndex(row => new { row.FirstAuthorId, row.SecondAuthorId }).IsUnique();
        modelBuilder.Entity<AuthorDuplicateSuggestion>().HasIndex(row => row.SecondAuthorId);
        modelBuilder.Entity<AuthorDuplicateSuggestion>().HasIndex(row => new { row.Status, row.Id });
        modelBuilder.Entity<AuthorRefresh>().HasIndex(row => row.ActiveSlot).IsUnique();
        modelBuilder.Entity<AuthorRefreshItem>().HasIndex(row => new { row.AuthorRefreshId, row.Status, row.Id });
        modelBuilder.Entity<CatalogueRefresh>().HasIndex(row => row.ActiveSlot).IsUnique();
        modelBuilder.Entity<CatalogueRefreshItem>().HasIndex(row => new { row.CatalogueRefreshId, row.Status, row.Id });
        modelBuilder.Entity<BookImport>().HasIndex(import => new { import.CompletedAtUtc, import.CreatedAtUtc });
        modelBuilder.Entity<BookImport>().HasIndex(import => new { import.Owner, import.BatchId });
        modelBuilder.Entity<BookImport>().Property(import => import.SelectedEdition).HasConversion(
            edition => JsonSerializer.Serialize(edition, _jsonSerializerOptions),
            value => JsonSerializer.Deserialize<Services.ExternalEdition>(value, _jsonSerializerOptions));
        modelBuilder.Entity<BookEditionReference>().HasKey(reference => new { reference.ProviderId, reference.ExternalId });

        modelBuilder.Entity<Book>(build =>
        {
            build.Property(p => p.AdditionalData).HasConversion(
                p => JsonSerializer.Serialize<Dictionary<string, object>>(p, _jsonSerializerOptions), 
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, _jsonSerializerOptions));

            build.HasIndex(i => i.Isbn).IsUnique(true).HasFilter("Isbn <> ''");
            build.HasIndex(i => i.Search);
        });

        modelBuilder.Entity<BookSeries>(build =>
        {
            build.HasIndex(i => i.Name).IsUnique(true);
        });

        modelBuilder.Entity<UserBook>(build =>
        {
            build.Property(p => p.ReadingStatus).HasConversion<string>();
        });

        modelBuilder.Entity<BookTag>(build =>
        {
            build.HasIndex(i => i.Tag);
            build.HasIndex(i => i.TagSearch);
        });
    }
}
