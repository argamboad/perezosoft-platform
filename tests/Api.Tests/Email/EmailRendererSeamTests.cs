using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Services;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Api.Tests.Notify;
using Perezosoft.Core.Abstractions;
using Perezosoft.Infrastructure.Email;

namespace Perezosoft.Api.Tests.Email;

/// <summary>
/// Pkg 7.y (#462): the platform's mails are rendered behind <see cref="IBrandedEmail"/>, so an app whose mails are its own
/// design replaces the renderer (ADR-034 point 4) instead of the platform's code. Found by the jigger-jot pilot: its mails
/// (an embedded serif, a lockup and an illustration per mail, its own layout) fit no option of <c>PlatformOptions.Brand</c>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class EmailRendererSeamTests(IntegrationTestFactory factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    [Fact]
    public void ByDefault_TheMailsAreThePlatformsBrandedEmail()
    {
        Assert.IsType<BrandedEmail>(_factory.Services.GetRequiredService<IBrandedEmail>());
        Assert.Same(_factory.Services.GetRequiredService<BrandedEmail>(), _factory.Services.GetRequiredService<IBrandedEmail>());
    }

    [Fact]
    public async Task AnAppsRenderer_RendersEveryPlatformMail()
    {
        // An app registers its renderer after the platform's (AddAppServices; here the test's services, which come later
        // still), and every kind of mail the platform sends goes through it: sign-in by code, sign-in by link, an
        // invitation, a notification's email copy.
        var renderer = new MarkedEmails();
        var sent = new CapturingEmailSender();
        using var host = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<IBrandedEmail>(renderer);
            services.AddSingleton<IEmailSender>(sent);
        }));
        var anonymous = host.CreateClient();
        var owner = await _factory.SeedUserAsync();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/otp/send", new { email = $"code-{Guid.NewGuid():N}@test.local" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/magic-link/send", new { email = $"link-{Guid.NewGuid():N}@test.local" })).StatusCode);
        var inviter = host.CreateClient();
        inviter.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.IssueAccessToken(owner));
        Assert.True((await inviter.PostAsJsonAsync("/api/household/invitations", new { email = $"invitee-{Guid.NewGuid():N}@test.local" })).IsSuccessStatusCode);
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(owner.UserId, "seam.check", "A title", "A body");

        Assert.Equal(["app:otp", "app:magic-link", "app:invitation", "app:notification"], sent.Sent.Select(m => m.Subject));
        Assert.All(sent.Sent, m => Assert.StartsWith("<p data-app-renderer>", m.Html, StringComparison.Ordinal));
        Assert.Equal(4, renderer.Calls);
    }

    // An app's own renderer, reduced to a mark per kind.
    private sealed class MarkedEmails : IBrandedEmail
    {
        private int _calls;
        public int Calls => _calls;

        public EmailBody Otp(string code, int lifespanMinutes, CultureInfo culture) => Mark("otp");
        public EmailBody MagicLink(string link, int lifespanMinutes, CultureInfo culture) => Mark("magic-link");
        public EmailBody Invitation(string joinUrl, string token, CultureInfo culture) => Mark("invitation");
        public EmailBody Notification(string title, string body, CultureInfo culture) => Mark("notification");

        private EmailBody Mark(string kind)
        {
            Interlocked.Increment(ref _calls);
            return new($"app:{kind}", $"<p data-app-renderer>{kind}</p>", []);
        }
    }
}
