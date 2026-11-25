using System.Text;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class IsbnUploadTests
{
    private readonly IsbnFileUploadPreProcessService _service =
        new(NullLogger<IsbnFileUploadPreProcessService>.Instance);

    [Theory]
    [InlineData("0140328726")]
    [InlineData("9780140328721")]
    public async Task RecognizesPlainIsbnLines(string isbn)
    {
        var result = await InspectAsync("books.txt", isbn);

        Assert.True(result.Success);
        Assert.Null(result.Separator);
        Assert.False(result.HasReadingStatus);
    }

    [Theory]
    [InlineData("books.csv", ",")]
    [InlineData("books.csv", ";")]
    [InlineData("books.tsv", "\t")]
    public async Task DetectsSeparatorAndReadingStatusAfterHeader(string fileName, string separator)
    {
        var result = await InspectAsync(fileName, $"ISBN{separator}Status\n9780140328721{separator}Read");

        Assert.True(result.Success);
        Assert.Equal(separator, result.Separator);
        Assert.True(result.HasReadingStatus);
    }

    [Theory]
    [InlineData("books.pdf", "9780140328721", "File type is not supported")]
    [InlineData("books.txt", "9780140328721:Read", "Separator not supported")]
    public async Task RejectsUnsupportedUpload(string name, string content, string message)
    {
        var result = await InspectAsync(name, content);

        Assert.False(result.Success);
        Assert.Equal(400, result.ProblemDetails!.Status);
        Assert.Equal(message, result.ProblemDetails.Detail);
    }

    private async Task<IsbnFileUploadPreProcessResult> InspectAsync(string name, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var file = new FormFile(stream, 0, stream.Length, "file", name);
        return await _service.ExecuteAsync(new IsbnFileUploadPreProcessArgs { File = file });
    }
}
