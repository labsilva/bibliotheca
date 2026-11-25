using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Jobs;

public sealed class FetcherDiagnosticsCleanup(
    IOptions<FetcherOptions> options, ILogger<FetcherDiagnosticsCleanup> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.SaveTempFiles)
            Directory.CreateDirectory(options.Value.TempFilesSaveLocation);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SaveTempFiles)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-7);
                var files = new DirectoryInfo(options.Value.TempFilesSaveLocation).EnumerateFiles();
                foreach (var file in files)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && file.LastWriteTimeUtc < cutoff &&
                        file.Extension.ToLowerInvariant() is ".html" or ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".avif")
                        file.Delete();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not clean the fetcher diagnostics directory");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
