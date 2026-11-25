using AngleSharp.Html.Parser;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    [Fact]
    public async Task RegistrationSupportsBrowserOnlyFragmentInvitationsAndLegacyOrManualCodes()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-invitation-links-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var client = CreateClient(factory);
            var parser = new HtmlParser();
            var fragmentPage = await client.GetStringAsync("/account/register#inviteCode=client-only-invite");
            Assert.DoesNotContain("client-only-invite", fragmentPage);
            var document = parser.ParseDocument(fragmentPage);
            var input = document.QuerySelector("input[name='InviteCode']")!;
            Assert.True(input.HasAttribute("required"));
            Assert.True(string.IsNullOrEmpty(input.GetAttribute("value")));
            Assert.Equal("post", document.QuerySelector("form")!.GetAttribute("method"));
            Assert.NotNull(document.QuerySelector("script[src='/js/register-invitation.js']"));

            var legacy = parser.ParseDocument(await client.GetStringAsync("/account/register?inviteCode=legacy-invite"));
            Assert.Equal("legacy-invite", legacy.QuerySelector("input[name='InviteCode']")!.GetAttribute("value"));
            // The existing form POST accepts an entered code without JavaScript.
            await RegisterAndLoginAsync(client);
        }
        finally { directory.Delete(true); }
    }
}
