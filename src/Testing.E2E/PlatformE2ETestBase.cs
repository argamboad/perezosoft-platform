using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Perezosoft.Testing.E2E;

/// <summary>
/// Base for E2E journeys, packaged (Pkg 5.1d): drives a real browser against the running Web app. An app's E2E project
/// derives it once (its own <c>E2ETestBase</c>) and adds what knows its pages — the sign-in and invite helpers over its
/// page objects.
/// <para>
/// Prereqs (see docs/WAYS_OF_WORKING.md): <c>docker compose up -d</c>, then start the API
/// (https profile) and the Web app, then run. Base URL defaults to the Web app's https
/// profile and is overridable via <c>PLAYWRIGHT_BASE_URL</c>.
/// </para>
/// </summary>
public abstract class PlatformE2ETestBase : PageTest
{
    // Resolution order: the runsettings TestRunParameter wins (so `-- TestRunParameters...` /
    // playwright.runsettings actually take effect), then the environment variable, then the Web
    // app's https launch profile (the app's local-ports.props, handed over in PlatformE2EHosts).
    protected static string BaseUrl => PlatformE2EHosts.Resolve("PLAYWRIGHT_BASE_URL", PlatformE2EHosts.Web);

    // The API's own origin, for tests that simulate an external caller (e.g. the billing-provider
    // webhook). Defaults to the API's https launch profile; CI overrides to its single-origin base.
    protected static string ApiBaseUrl => PlatformE2EHosts.Resolve("E2E_API_BASE_URL", PlatformE2EHosts.Api);

    /// <summary>HttpClient for API calls made by tests themselves (accepts the dev self-signed cert).</summary>
    protected static HttpClient NewApiClient() => new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    })
    { BaseAddress = new Uri(ApiBaseUrl) };

    /// <summary>
    /// POSTs the billing-provider webhook exactly as Stripe would, accepted by the
    /// FakeBillingProvider (the E2E API runs without a Stripe key — README): PascalCase body
    /// (BillingWebhookEvent uses DEFAULT System.Text.Json options, so don't serialize camelCase)
    /// + the fake's always-valid signature. Lets tests drive plan changes — upgrades AND
    /// downgrades — with no Stripe. Pass a strictly-later <paramref name="occurredAt"/> for a
    /// follow-up event: the projection applies only newer events (R29).
    /// </summary>
    protected static async Task PostBillingWebhookAsync(string tenantId, string status, string planKey = "pro",
        DateTimeOffset? occurredAt = null)
    {
        using var api = NewApiClient();
        var payload = JsonSerializer.Serialize(new
        {
            EventId = $"evt_e2e_{Guid.NewGuid():N}",
            TenantId = tenantId,
            PlanKey = planKey,
            Status = status,
            StripeCustomerId = "cus_e2e",
            StripeSubscriptionId = "sub_e2e",
            CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(30),
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
        });
        var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        webhook.Headers.Add("Stripe-Signature", "valid"); // FakeBillingProvider.ValidSignature
        var response = await api.SendAsync(webhook);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"webhook returned {(int)response.StatusCode}");
    }

    // Dev runs on a self-signed cert, so ignore HTTPS errors for the test browser.
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
    };

    /// <summary>Keep the browser console of the main page from its first line (BlazorBoot watches secondary
    /// pages from their first navigation).</summary>
    [SetUp]
    public void WatchBrowserConsole() => BlazorBoot.Watch(Page);

    /// <summary>A red journey prints what the browser saw — the only way to tell a dead Blazor boot from
    /// a real regression after the fact.</summary>
    [TearDown]
    public void ReportBrowserConsoleOnFailure()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            TestContext.Out.WriteLine($"Browser console of {Page.Url}:\n{BlazorBoot.ConsoleOf(Page)}");
    }

    protected static string UniqueEmail(string role) => $"e2e-{role}-{Guid.NewGuid():N}@example.com";
}
