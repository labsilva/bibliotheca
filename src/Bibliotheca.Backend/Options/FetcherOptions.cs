namespace Bibliotheca.Backend.Options;

public class FetcherOptions
{
    public const string ConfigurationName = "Fetchers";
    public bool SaveTempFiles { get; set; }
    public string TempFilesSaveLocation { get; set; }
}
