namespace Bibliotheca.Backend.Options;

public sealed class ExternalEditionSearchOptions
{
    public const string SectionName = "ExternalEditionSearch";

    public int MaxConcurrentRequests { get; set; } = 4;
    public Dictionary<string, ExternalEditionProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsValid() => MaxConcurrentRequests is >= 1 and <= 32 &&
        Providers.Values.All(provider => provider.TimeoutSeconds is >= 1 and <= 60 &&
            provider.ResultLimit is >= 1 and <= 100);
}

public sealed class ExternalEditionProviderOptions
{
    public bool Enabled { get; set; }
    public int Priority { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 5;
    public int ResultLimit { get; set; } = 20;
    public string? CredentialKey { get; set; }
}
