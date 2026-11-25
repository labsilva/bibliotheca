using Bibliotheca.Backend.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Bibliotheca.Backend.Tests.TestDatabase;

namespace Bibliotheca.Backend.Tests;

public class BookImportQueueTests
{
    [Fact]
    public async Task FullQueueRejectsTheWholeBatchAndOversizedBatchesAreInvalid()
    {
        await using var database = await CreateDatabaseAsync();
        var queue = new BookImportQueue(database, new BookImportSignal());
        var capacity = Enumerable.Repeat("9780140328721", BookImportQueue.Capacity).ToArray();

        Assert.IsType<BadRequestObjectResult>(await queue.SubmitAsync("alice", [.. capacity, "9780140328721"], default));
        var accepted = Assert.IsType<AcceptedResult>(await queue.SubmitAsync("alice", capacity, default));
        Assert.NotEmpty(accepted.Location!);
        var rejection = Assert.IsType<ObjectResult>(await queue.SubmitAsync("alice", ["9780140328721"], default));
        Assert.Equal(429, rejection.StatusCode);
        Assert.Equal(BookImportQueue.Capacity, (await database.BookImports.SingleAsync()).Isbns.Length);
    }
}
