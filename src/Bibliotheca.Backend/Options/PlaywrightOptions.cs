namespace Bibliotheca.Backend.Options;

public class PlaywrightOptions
{
    public const string ConfigurationName = "Playwright";

    public string Remote { get; set; }
    public bool Headless { get; set; } = true;
    public string? Channel { get; set; }
    public bool IsPlaywrightRemoteEnabled
    {
        get
        {
            return !string.IsNullOrEmpty(Remote);
        }
    }
}
