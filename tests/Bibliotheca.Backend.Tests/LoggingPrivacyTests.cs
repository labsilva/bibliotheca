using Bibliotheca.Backend.Services;
using Bibliotheca.Backend.Fetchers;
using NLog;
using NLog.Layouts;
using NLog.Config;
using NLog.Targets;

namespace Bibliotheca.Backend.Tests;

public sealed class LoggingPrivacyTests
{
    [Fact]
    public void ProductionConfigurationSuppressesProtocolPayloadsAndRedactsEveryConsoleTarget()
    {
        using var factory = new LogFactory();
        factory.Setup().SetupExtensions(extensions => extensions.RegisterLayoutRenderer<SafeLogMessage>("safe-message"));
        var configuration = new XmlLoggingConfiguration(Path.Combine(AppContext.BaseDirectory, "nlog.config"), factory);
        factory.Configuration = configuration;
        Assert.False(factory.GetLogger("OpenIddict.Server.OpenIddictServerDispatcher").IsInfoEnabled);
        Assert.False(factory.GetLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsInfoEnabled);
        Assert.False(factory.GetLogger("OpenIddict.Validation.OpenIddictValidationDispatcher").IsErrorEnabled);
        var databaseCommands = factory.GetLogger("Microsoft.EntityFrameworkCore.Database.Command");
        Assert.False(databaseCommands.IsInfoEnabled);
        Assert.True(databaseCommands.IsWarnEnabled);
        Assert.True(databaseCommands.IsErrorEnabled);
        var routing = factory.GetLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware");
        Assert.False(routing.IsInfoEnabled);
        Assert.True(routing.IsWarnEnabled);
        foreach (var target in configuration.AllTargets.OfType<TargetWithLayout>())
        {
            var entry = new LogEventInfo(LogLevel.Error, "Test", "Authorization: Bearer credential-marker")
            {
                Exception = new Exception("credential-marker")
            };
            var result = target.Layout.Render(entry);
            Assert.DoesNotContain("credential-marker", result);
            Assert.Contains("[redacted]", result);
        }
        Assert.NotEmpty(configuration.AllTargets);
    }

    [Theory]
    [InlineData("GET https://api.example/connect/authorize?code=credential-marker&state=credential-marker")]
    [InlineData("GET /api/bibliotheca-hub?access_token=credential-marker")]
    [InlineData("Authorization: Bearer credential-marker")]
    [InlineData("Cookie: idp_auth=credential-marker; other=credential-marker")]
    [InlineData("{\"access_token\":\"credential-marker\",\"refresh_token\":\"credential-marker\"}")]
    [InlineData("code=credential-marker code_verifier=credential-marker password=credential-marker")]
    public void RedactsSensitiveRequestAndProtocolValues(string input)
    {
        var rendered = SafeLogMessage.Redact(input);
        Assert.DoesNotContain("credential-marker", rendered);
        Assert.Contains("[redacted]", rendered);
    }

    [Fact]
    public void ConsoleRendererRedactsStructuredMessagesAndOmitsExceptionPayloads()
    {
        LogManager.Setup().SetupExtensions(extensions => extensions.RegisterLayoutRenderer<SafeLogMessage>("safe-message"));
        var entry = new LogEventInfo(LogLevel.Error, "Test", "GET {Url}")
        {
            Parameters = ["https://api.example/connect/token?code=credential-marker"],
            Exception = new Exception("credential-marker")
        };
        var result = new SimpleLayout("${safe-message}").Render(entry);
        Assert.Contains("https://api.example/connect/token?[redacted]", result);
        Assert.DoesNotContain("credential-marker", result);
    }

    [Fact]
    public void ConsoleRendererIncludesSafeExceptionDiagnosticsWithoutRawExceptionMessages()
    {
        var renderer = new SimpleLayout("${safe-message}");
        LogManager.Setup().SetupExtensions(extensions => extensions.RegisterLayoutRenderer<SafeLogMessage>("safe-message"));
        var entry = new LogEventInfo(LogLevel.Error, "Test", "Fetch failed")
        {
            Exception = new HttpRequestException("password=credential-marker", new PageNavigationException(403,
                "https://user:credential-marker@www.wook.pt/pesquisa?keyword=credential-marker"))
        };
        var result = renderer.Render(entry);
        Assert.Contains("System.Net.Http.HttpRequestException", result);
        Assert.Contains("PageNavigationException (HTTP 403)", result);
        Assert.Contains("https://www.wook.pt/pesquisa?[redacted]", result);
        Assert.DoesNotContain("credential-marker", result);
        entry.Exception = new PageNavigationException(403, "https://www.wook.pt/pesquisa?keyword=9789895870714");
        Assert.Contains("https://www.wook.pt/pesquisa?keyword=9789895870714", renderer.Render(entry));
    }

    [Fact]
    public void ConsoleRendererOnlyIncludesAllowlistedDataErrors()
    {
        LogManager.Setup().SetupExtensions(extensions => extensions.RegisterLayoutRenderer<SafeLogMessage>("safe-message"));
        var renderer = new SimpleLayout("${safe-message}");
        var entry = new LogEventInfo(LogLevel.Error, "Test", "Fetch failed")
        { Exception = new InvalidDataException("Wook product metadata could not be recognized.") };
        Assert.Contains("Wook product metadata could not be recognized.", renderer.Render(entry));
        entry.Exception = new InvalidDataException("unknown secret-value");
        Assert.DoesNotContain("secret-value", renderer.Render(entry));
    }
}
