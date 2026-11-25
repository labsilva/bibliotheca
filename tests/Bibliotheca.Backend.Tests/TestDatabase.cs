using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Tests;

internal static class TestDatabase
{
    internal static async Task<BibliothecaDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<BibliothecaDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=True")
            .AddInterceptors(new DiacriticsInterceptor())
            .Options;
        var db = new BibliothecaDbContext(options);
        try
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.MigrateAsync();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
}
