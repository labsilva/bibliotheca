
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text.RegularExpressions;

namespace Bibliotheca.Backend.Services;

public class IsbnFileUploadPreProcessService : IServiceExecutor<IsbnFileUploadPreProcessArgs, IsbnFileUploadPreProcessResult>
{
    private readonly ILogger<IsbnFileUploadPreProcessService> _logger;

    public IsbnFileUploadPreProcessService(
        ILogger<IsbnFileUploadPreProcessService> logger)
    {
        _logger = logger;
    }

    public static readonly string[] SupportedExtensions = new[]
    {
        ".txt",
        ".tsv",
        ".csv"
    };

    public static readonly string[] SupportedSeparators = new[]
    {
        "\t",
        ";",
        ",",
        "|",
        " ",
    };

    public static readonly Regex Isbn10Only = new Regex("^\\d{10}$", RegexOptions.Compiled);
    public static readonly Regex Isbn13Only = new Regex("^\\d{13}$", RegexOptions.Compiled);

    public static readonly Regex Isbn10AndSeparator = new Regex("^(\\d{10})(\\D)", RegexOptions.Compiled);
    public static readonly Regex Isbn13AndSeparator = new Regex("^(\\d{13})(\\D)", RegexOptions.Compiled);

    public static readonly Regex Isbn10 = new Regex("^\\d{10}", RegexOptions.Compiled);
    public static readonly Regex Isbn13 = new Regex("^\\d{13}", RegexOptions.Compiled);

    public async Task<IsbnFileUploadPreProcessResult> ExecuteAsync(
        IsbnFileUploadPreProcessArgs args, 
        CancellationToken cancellationToken = default)
    {
        if(args.File is null)
        {
            return new IsbnFileUploadPreProcessResult { Success = false, ProblemDetails = new ProblemDetails { Status = 400, Detail = "No file provided" } };
        }

        var fileInfo = new FileInfo(args.File.FileName);
        if(!SupportedExtensions.Contains(fileInfo.Extension))
        {
            return new IsbnFileUploadPreProcessResult { Success = false, ProblemDetails = new ProblemDetails { Status = 400, Detail = "File type is not supported" } };
        }

        using var reader = new StreamReader(args.File.OpenReadStream());
        bool isFirstLine = true;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            // Check if there are only ISBN's
            if(Isbn10Only.IsMatch(line) || Isbn13Only.IsMatch(line))
            {
                return new IsbnFileUploadPreProcessResult { Success = true, Separator = null, HasReadingStatus = false };
            }


            // Check if line contains an ISBN
            if(Isbn10.IsMatch(line) || Isbn13.IsMatch(line))
            {
                var match = Isbn10AndSeparator.IsMatch(line) ? Isbn10AndSeparator.Match(line) : Isbn13AndSeparator.Match(line);
                var separator = line.Replace(match.Groups[1].ToString(), string.Empty).First().ToString();
                if (!SupportedSeparators.Contains(separator))
                {
                    return new IsbnFileUploadPreProcessResult { Success = false, ProblemDetails = new ProblemDetails { Status = 400, Detail = "Separator not supported" } };
                }
                return new IsbnFileUploadPreProcessResult { Success = true, Separator = separator, HasReadingStatus = true };
            }

            // Header check
            if(isFirstLine)
            {
                continue;
            }
            break;
        }

        return new IsbnFileUploadPreProcessResult { Success = false, Separator = null, HasReadingStatus = false, ProblemDetails = new ProblemDetails { Status = 500, Detail = "Could not pre-process file" } };
    }
}

public record IsbnFileUploadPreProcessArgs
{
    public IFormFile File { get; init; }
}

public record IsbnFileUploadPreProcessResult
{
    public bool Success { get; init; }
    public ProblemDetails? ProblemDetails { get; init; }
    public string? Separator { get; init; }
    public bool HasReadingStatus { get; init; }
}