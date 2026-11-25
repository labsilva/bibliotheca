using Microsoft.Data.Sqlite;
using NLog;
using NLog.Targets;

namespace Bibliotheca.Backend.Initialization;

public static class PrivateStorageValidation
{
    public static void Validate(IWebHostEnvironment environment, string connectionString)
    {
        if (environment.IsDevelopment()) return;
        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var sqlite = new SqliteConnectionStringBuilder(connectionString);
        if (sqlite.Mode != SqliteOpenMode.Memory && sqlite.DataSource != ":memory:")
        {
            // SQLite URI filenames have different escaping and query semantics.
            // Require a normal filesystem path rather than guessing their destination.
            if (sqlite.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Production SQLite storage requires a filesystem path outside the web root; URI filenames are not supported.");
            RequirePrivate(webRoot, environment.ContentRootPath, sqlite.DataSource, "SQLite database");
        }

        foreach (var target in LogManager.Configuration?.AllTargets.OfType<FileTarget>() ?? [])
        {
            // Request/event-derived paths cannot be proved private at startup.
            var layout = target.FileName?.ToString() ?? "";
            if (layout.Contains("${event-properties", StringComparison.OrdinalIgnoreCase) ||
                layout.Contains("${aspnet-request", StringComparison.OrdinalIgnoreCase) ||
                layout.Contains("${message", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Production file logs must use a fixed private directory.");
            var file = target.FileName?.Render(LogEventInfo.CreateNullEvent()) ?? "";
            RequirePrivate(webRoot, environment.ContentRootPath, file, "Log file");
        }
    }

    private static void RequirePrivate(string webRoot, string contentRoot, string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{description} must have an explicit private storage path.");
        var relative = Path.GetRelativePath(Path.GetFullPath(webRoot), Path.GetFullPath(path, contentRoot));
        if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            throw new InvalidOperationException($"{description} must be stored outside the web root. Configure a private path to the existing data; files are never moved automatically.");
    }
}
