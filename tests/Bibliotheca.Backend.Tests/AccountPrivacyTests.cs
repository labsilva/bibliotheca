using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    [Fact]
    public async Task ShelvesRemainPrivateInSearchListsDetailsAndDirectAccess()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-shelf-privacy-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var aliceBrowser = CreateClient(factory);
            using var anonymous = await aliceBrowser.GetAsync("/api/search?text=Private");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var anonymousShelf = await aliceBrowser.GetAsync("/api/library/shelves/Private%20Bob/books");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousShelf.StatusCode);
            await RegisterAndLoginAsync(aliceBrowser);
            using var aliceApi = CreateApiClient(factory, aliceBrowser);
            var aliceTokens = await GetTokensAsync(aliceBrowser, aliceApi);
            aliceApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceTokens.GetProperty("access_token").GetString());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var alice = await db.Users.SingleAsync();
            var bob = new User { Name = "bob", FullName = "Bob", Email = "bob@example.invalid", Settings = "{}" };
            bob.Password = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(bob, "private test password");
            var aliceBook = new Book { Title = "Private shared catalogue A", Isbn = "", CreatedBy = "test", ModifiedBy = "test" };
            var bobBook = new Book { Title = "Private shared catalogue B", Isbn = "", CreatedBy = "test", ModifiedBy = "test" };
            var aliceShelf = new Shelve { ShelveId = 8, User = alice, ShelveName = "Private same name" };
            var bobShelf = new Shelve { ShelveId = 8, User = bob, ShelveName = "Private same name" };
            db.Shelves.AddRange(aliceShelf, bobShelf,
                new Shelve { ShelveId = 9, User = alice, ShelveName = "Private Alice" },
                new Shelve { ShelveId = 9, User = bob, ShelveName = "Private Bob" });
            db.UsersBooks.AddRange(
                new UserBook { User = alice, Book = aliceBook, Shelve = aliceShelf },
                new UserBook { User = bob, Book = bobBook, Shelve = bobShelf });
            await db.SaveChangesAsync();

            using var bobBrowser = CreateClient(factory);
            await LoginAsync(bobBrowser, "bob");
            using var bobApi = CreateApiClient(factory, bobBrowser);
            var bobTokens = await GetTokensAsync(bobBrowser, bobApi);
            bobApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobTokens.GetProperty("access_token").GetString());
            foreach (var (api, ownName, foreignName, ownBook) in new[]
            {
                (aliceApi, "Private Alice", "Private Bob", aliceBook.BookId),
                (bobApi, "Private Bob", "Private Alice", bobBook.BookId),
            })
            {
                using var search = await api.GetAsync("/api/search?text=Private");
                Assert.Contains("no-store", search.Headers.CacheControl!.ToString());
                var matches = (await search.Content.ReadFromJsonAsync<SearchController.SearchResultItem[]>())!;
                Assert.Equal(new[] { "Private same name", ownName }.Order(), matches.Where(item => item.Area == "Shelve").Select(item => item.Title).Order());
                Assert.Equal(2, matches.Count(item => item.Area == "Book"));
                var shelves = await api.GetStringAsync("/api/library/shelves");
                Assert.Contains(ownName, shelves);
                Assert.DoesNotContain(foreignName, shelves);
                using var foreign = await api.GetAsync("/api/library/shelves/" + Uri.EscapeDataString(foreignName) + "/books");
                Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
                using var foreignWrite = await api.PutAsync("/api/library/shelves/" + Uri.EscapeDataString(foreignName) + "/books/" + ownBook, null);
                Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
                var books = await api.GetFromJsonAsync<JsonElement>("/api/library/shelves/Private%20same%20name/books");
                Assert.Equal(ownBook, Assert.Single(books.EnumerateArray()).GetProperty("id").GetInt32());
                var detail = await api.GetStringAsync("/api/books/" + ownBook);
                Assert.DoesNotContain(foreignName, detail);
                Assert.DoesNotContain("\"password\"", detail);
                Assert.DoesNotContain("\"users\"", detail);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task InvitationsRequireExplicitAdministratorConfiguration()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-invitation-permissions-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
                (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authorization:CatalogueManagerUserIds:0"] = "1",
                    ["Authorization:AdministratorUserIds:0"] = "2",
                })));
            using var aliceBrowser = CreateClient(factory);
            using var anonymous = await aliceBrowser.PostAsync("/api/account/invite-code", null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            await RegisterAndLoginAsync(aliceBrowser);
            using var aliceApi = CreateApiClient(factory, aliceBrowser);
            var aliceTokens = await GetTokensAsync(aliceBrowser, aliceApi);
            aliceApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceTokens.GetProperty("access_token").GetString());
            using var forbidden = await aliceApi.PostAsync("/api/account/invite-code", null);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.False((await aliceApi.GetFromJsonAsync<AccountController.AccountPermissionsResponse>("/api/account/permissions"))!.CanInvite);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            Assert.Empty(await db.UserInvites.ToArrayAsync());
            var bob = new User { Id = 2, Name = "bob", FullName = "Bob", Email = "bob@example.invalid", Settings = "{}" };
            bob.Password = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(bob, "private test password");
            db.Users.Add(bob);
            await db.SaveChangesAsync();
            using var bobBrowser = CreateClient(factory);
            await LoginAsync(bobBrowser, "bob");
            using var bobApi = CreateApiClient(factory, bobBrowser);
            var bobTokens = await GetTokensAsync(bobBrowser, bobApi);
            bobApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobTokens.GetProperty("access_token").GetString());
            Assert.True((await bobApi.GetFromJsonAsync<AccountController.AccountPermissionsResponse>("/api/account/permissions"))!.CanInvite);
            using var accepted = await bobApi.PostAsync("/api/account/invite-code", null);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Contains("no-store", accepted.Headers.CacheControl!.ToString());
            var invite = await db.UserInvites.SingleAsync();
            Assert.False(invite.Used);
            Assert.InRange(invite.InviteExpiresOn, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));
        }
        finally { directory.Delete(recursive: true); }
    }
}
