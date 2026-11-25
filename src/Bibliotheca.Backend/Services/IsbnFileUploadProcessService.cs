using Bibliotheca.Backend.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace Bibliotheca.Backend.Services;

public class IsbnFileUploadProcessService(BookImportQueue queue)
    : IServiceExecutor<IsbnFileUploadProcessArgs, IActionResult>
{
    public async Task<IActionResult> ExecuteAsync(
        IsbnFileUploadProcessArgs args, CancellationToken cancellationToken = default)
    {
        using var stream = args.File.OpenReadStream();
        using var reader = new StreamReader(stream);
        var isbns = new List<string>();
        while (isbns.Count <= BookImportQueue.Capacity &&
               await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            isbns.Add(line.Split(args.Separator)[0]);
        }

        return await queue.SubmitAsync(args.Owner, isbns.ToArray(), cancellationToken);
    }
}

public record IsbnFileUploadProcessArgs
{
    public string? Separator { get; init; }
    public required string Owner { get; init; }
    public required IFormFile File { get; init; }
}
