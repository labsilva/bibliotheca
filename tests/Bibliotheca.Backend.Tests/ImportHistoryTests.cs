using System.Security.Claims;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bibliotheca.Backend.Tests;

public class ImportHistoryTests
{
    [Fact]
    public async Task LargeBatchSummariesAndFailurePagesAreScopedToTheReader()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var batch = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var imports = Enumerable.Range(0, 512).Select(i => new BookImport
        {
            BatchId = batch, Owner = "alice", CreatedAtUtc = start.AddSeconds(i),
            Isbns = [$"good-{i}", $"failed-{i}-1", $"failed-{i}-2"], Total = 3, Failed = 2,
            IsbnFailurePositions = [1, 2], IsbnFailureReasons = ["reason 1", "reason 2"],
            Succeeded = i == 511 ? 0 : 1, CompletedAtUtc = i == 511 ? null : start.AddHours(1)
        }).ToArray();
        db.BookImports.AddRange(imports);
        db.BookImports.Add(new BookImport
        {
            BatchId = batch, Owner = "bob", CreatedAtUtc = start, Isbns = ["private"], Total = 1, Failed = 1,
            IsbnFailurePositions = [0], IsbnFailureReasons = ["private reason"]
        });
        await db.SaveChangesAsync();

        var alice = For("alice");
        var summary = Assert.Single(await alice.ListAsync(db, default));
        Assert.Equal((1536, 511, 1, 1024, 1024, "processing"), (summary.Total, summary.Succeeded, summary.Pending,
            summary.Failed, summary.IsbnFailureCount, summary.Status));
        var first = await Page(alice, 0);
        var second = await Page(alice, 1);
        var last = await Page(alice, 53);
        Assert.Equal(1024, second.Total);
        Assert.Equal(19, first.Items.Length);
        Assert.Equal(new BatchIsbnFailure(imports[9].Id, 1, "failed-9-1", "reason 1"), first.Items[^1]);
        Assert.Equal(new BatchIsbnFailure(imports[9].Id, 2, "failed-9-2", "reason 2"), second.Items[0]);
        Assert.Equal(17, last.Items.Length);
        Assert.Empty((await Page(alice, 54)).Items);

        var bob = For("bob");
        Assert.Equal("private", Assert.Single((await Page(bob, 0)).Items).Isbn);
        Assert.Equal(1, Assert.Single(await bob.ListAsync(db, default)).Failed);
        Assert.IsType<NotFoundResult>((await For("unknown").FailuresAsync(batch, 0, 19, db, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await alice.FailuresAsync(batch, 0, 101, db, default)).Result);

        async Task<PaginatedResult<BatchIsbnFailure>> Page(ImportsController controller, int index)
        {
            var response = await controller.FailuresAsync(batch, index, 19, db, default);
            return Assert.IsType<PaginatedResult<BatchIsbnFailure>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        }
    }

    private static ImportsController For(string owner) => new()
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", owner)], "test"))
            }
        }
    };
}
