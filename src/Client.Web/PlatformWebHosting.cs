using System.Globalization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Perezosoft.Client.Auth;
using Perezosoft.Client.Web.Http;

namespace Perezosoft.Client.Web;

/// <summary>
/// The web host's platform half (Pkg 4.2, ADR-034). A Blazor WebAssembly host adds its root components, calls
/// <see cref="AddPlatformWeb"/>, builds, applies the saved culture with <see cref="UseStoredCultureAsync"/> and runs:
/// everything the platform's client needs in the browser is registered here, the same in every app.
/// </summary>
public static class PlatformWebHosting
{
    /// <summary>
    /// Registers the platform client for the browser. <paramref name="apiBase"/> is the API's origin; when null, the
    /// configuration's <c>ApiBaseUrl</c> (local dev's wwwroot/appsettings.json, the e2e override), else this app's own
    /// origin — the single-origin deployment, where the API serves the bundle (DEPLOY-1, ADR-017).
    /// </summary>
    public static WebAssemblyHostBuilder AddPlatformWeb(this WebAssemblyHostBuilder builder, string? apiBase = null)
    {
        apiBase ??= builder.Configuration["ApiBaseUrl"] is { Length: > 0 } configured
            ? configured
            : builder.HostEnvironment.BaseAddress;
        var apiOrigin = new Uri(apiBase);
        var services = builder.Services;

        // Delegating handlers:
        //  - CookieHandler includes browser credentials (the HttpOnly refresh cookie).
        //  - BearerScopedHandler (shared with the native host) attaches the in-memory JWT as a Bearer header —
        //    to the API's own origin only, never to a foreign absolute URL (v4 T49).
        // IHttpClientFactory supplies the browser HTTP handler as the primary handler (you cannot
        // `new HttpClientHandler()` in Blazor WASM).
        services.AddScoped<CookieHandler>();
        services.AddScoped(sp => new BearerScopedHandler(sp.GetRequiredService<AuthService>(), apiOrigin));

        // "Api" — the general-purpose client the pages use: credentials + Bearer token.
        services.AddHttpClient("Api", client => client.BaseAddress = apiOrigin)
            .AddHttpMessageHandler<CookieHandler>()
            .AddHttpMessageHandler<BearerScopedHandler>();

        // "ApiAuth" — used only by AuthService for refresh/logout. Credentials only (no Bearer handler) to avoid a DI
        // cycle (BearerScopedHandler depends on AuthService).
        services.AddHttpClient("ApiAuth", client => client.BaseAddress = apiOrigin)
            .AddHttpMessageHandler<CookieHandler>();

        services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

        // Localization — IStringLocalizer<AppStrings> resolves the seeded UI's .resx resources.
        services.AddLocalization();
        services.AddSingleton<ICulturePersistence, LocalStorageCulturePersistence>();
        services.AddSingleton<IThemePersistence, LocalStorageThemePersistence>();
        services.AddSingleton<IFileDownloadLauncher, BrowserFileDownloadLauncher>();
        // Never notified on web — external flows return via a full redirect (fresh page load); the registration only
        // satisfies the pages' injection (see AppResumeNotifier).
        services.AddSingleton<AppResumeNotifier>();

        // Web session store: the browser owns the HttpOnly refresh cookie, so this is a no-op.
        services.AddSingleton<ISessionStore, CookieSessionStore>();
        // The clock client components schedule with (R148): the bell's poll, the billing refetch. Tests swap it.
        services.AddSingleton(TimeProvider.System);

        // AuthService is a SINGLETON: IHttpClientFactory resolves message handlers (BearerScopedHandler) in a separate
        // DI scope, so a scoped AuthService would give the handler a different instance with no token — and the Bearer
        // header would never be attached. Singleton guarantees the app and the handler share one token store.
        services.AddSingleton(sp => new AuthService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiAuth"),
            sp.GetRequiredService<ILogger<AuthService>>(),
            sp.GetRequiredService<ISessionStore>()));

        return builder;
    }

    /// <summary>
    /// Applies the user's saved UI culture before the app renders, falling back to English. Signed-in users are
    /// further reconciled to their server-side locale by the layout.
    /// </summary>
    public static async Task<WebAssemblyHost> UseStoredCultureAsync(this WebAssemblyHost host)
    {
        var js = host.Services.GetRequiredService<IJSRuntime>();
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", LocalStorageCulturePersistence.StorageKey);
        try
        {
            var culture = string.IsNullOrWhiteSpace(stored) ? "en" : stored;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(culture);
        }
        catch (CultureNotFoundException) { /* corrupt stored value — keep the default culture */ }
        return host;
    }
}
