using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Perezosoft.Client.Auth;
using Perezosoft.Client.Maui.Auth;

namespace Perezosoft.Client.Maui;

/// <summary>What an app tells <see cref="PlatformMauiHosting.UsePlatformMaui"/>.</summary>
public sealed class PlatformMauiOptions
{
    /// <summary>
    /// The API's origin. The app resolves it, because the choice depends on ITS build: a Debug build may fall back to
    /// the dev localhost (and an env override), a Release build must carry the origin ReleaseGuards compiled in
    /// (v3 NAT-3). A package is compiled once, in Release, so it cannot make that choice. A trailing slash is trimmed.
    /// </summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>The custom URL scheme of the OAuth callback (<c>perezosoft://auth</c>): the API's
    /// Auth:Native:CallbackScheme and the Android callback activity's intent filter say the same.</summary>
    public required string CallbackScheme { get; init; }

    /// <summary>The app's own build is Debug (it sets this under its <c>#if DEBUG</c>): developer tools, debug logging,
    /// and on Mac Catalyst the file session store an ad-hoc-signed build needs.</summary>
    public bool DebugBuild { get; init; }

    /// <summary>The app's status/navigation bar colours (Android); null leaves the OS's.</summary>
    public SystemBarPalette? SystemBars { get; init; }
}

/// <summary>
/// The MAUI host's platform half (Pkg 4.3, ADR-034). An app's <c>MauiProgram</c> calls
/// <see cref="UsePlatformShell{TRoot}"/> and <see cref="UsePlatformMaui"/>, adds its fonts, and builds: everything the
/// platform client needs on a device is registered here, the same in every app.
/// </summary>
public static class PlatformMauiHosting
{
    /// <summary>The application and its one page: a BlazorWebView over <paramref name="hostPage"/> rooted on
    /// <typeparamref name="TRoot"/> (the app's root component, its Router).</summary>
    public static MauiAppBuilder UsePlatformShell<TRoot>(this MauiAppBuilder builder, string title, string hostPage = "wwwroot/index.html")
        where TRoot : IComponent
    {
        builder.Services.AddSingleton(new PlatformShellOptions(typeof(TRoot), title, hostPage));
        builder.UseMauiApp<PlatformApp>();
        builder.Services.AddMauiBlazorWebView();
        return builder;
    }

    /// <summary>Registers the platform client for a device: persistences, session store, OAuth, AuthService and the
    /// API clients, the clock, the resume and download seams.</summary>
    public static MauiAppBuilder UsePlatformMaui(this MauiAppBuilder builder, PlatformMauiOptions options)
    {
        // Normalized: no trailing slash. The OAuth initiators build the browser URL by concatenating
        // "/api/auth/native/login/…" onto this, and a base passed as "https://host/" produced "https://host//api/…" —
        // which the API serves as the web client's fallback page instead of the OAuth challenge, so the browser signed
        // the user into the WEB app and the native app never got its code (found downstream 2026-09-14). HttpClient
        // tolerated the slash; the concatenation did not.
        var apiBaseUrl = options.ApiBaseUrl.TrimEnd('/');
        var services = builder.Services;

        if (options.DebugBuild)
        {
            services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
        }

        // Localization — IStringLocalizer<AppStrings> resolves the seeded UI's .resx resources. Apply the device-local
        // saved culture before the first render (NATIVE-5, parity with the web host's localStorage bootstrap): the
        // LanguageSwitcher persists the choice to OS Preferences on native, the only store readable this early — the
        // WebView (and its localStorage) doesn't exist yet. No saved value → OS culture. Signed-in users are further
        // reconciled to their server-side locale by the layout.
        var savedCulture = Preferences.Default.Get<string?>(PreferencesCulturePersistence.PreferenceKey, null);
        if (!string.IsNullOrWhiteSpace(savedCulture))
        {
            try
            {
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(savedCulture);
            }
            catch (CultureNotFoundException) { /* corrupt stored value — keep the OS culture */ }
        }
        services.AddLocalization();
        services.AddSingleton<ICulturePersistence, PreferencesCulturePersistence>();

        // Theme needs no Preferences bootstrap: it's pure DOM, and theme.js reads the WebView's own localStorage before
        // first paint (THEME-1).
        services.AddSingleton<IThemePersistence, LocalStorageThemePersistence>();
#if ANDROID
        // The status and navigation bars are the OS's, not the page's: paint them to match the theme the page applies
        // (SystemBarThemeSync), in the app's colours. Other hosts register none.
        if (options.SystemBars is { } palette)
            services.AddSingleton<ISystemBarTheme>(new AndroidSystemBarTheme(palette));
#endif

        // Signed-URL downloads can't ride a WebView navigation — fetch + OS share sheet instead (NATIVE-3). A PLAIN
        // client (v4 T49): the URL is either the API's tokenized /api/files/{token} (anonymous; relative, hence the
        // BaseAddress) or a presigned S3/MinIO link, which AWS refuses when an Authorization header rides along — and
        // the default client's handler would also spend a refresh on the file host's behalf. Nothing here needs the bearer.
        services.AddSingleton<IFileDownloadLauncher>(_ =>
            new ShareFileDownloadLauncher(new HttpClient { BaseAddress = new Uri(apiBaseUrl) }));

        // Fired by the window's Resumed/Activated events (PlatformApp) so pages can refresh after an external
        // round-trip returns to the app (NATIVE-4, G2).
        services.AddSingleton<AppResumeNotifier>();

        // Refresh token lives in the OS secure store (the native equivalent of the web's HttpOnly cookie). OAuth is
        // platform-specific: Windows captures the callback via a loopback HTTP listener; Android/iOS/macCatalyst via a
        // custom-scheme WebAuthenticator. Both sit behind IOAuthInitiator so AuthService and the Login page stay
        // platform-agnostic. EVERY target platform must register one — the AuthService factory below resolves it with
        // GetRequiredService, so a missing branch here crashes the app at first resolve (exactly how iOS/macCatalyst
        // were dead-on-arrival before G7).
#if MACCATALYST
        // Ad-hoc-signed local (Debug) builds can't reach the data-protection keychain (restricted entitlement) — see
        // DebugFileSessionStore. Signed builds use the secure store.
        if (options.DebugBuild)
            services.AddSingleton<ISessionStore, DebugFileSessionStore>();
        else
            services.AddSingleton<ISessionStore, SecureStorageSessionStore>();
#else
        services.AddSingleton<ISessionStore, SecureStorageSessionStore>();
#endif
        // The clock client components schedule with (R148): the bell's poll, the billing refetch. Same line on the web.
        services.AddSingleton(TimeProvider.System);
#if ANDROID || IOS || MACCATALYST
        services.AddSingleton<IOAuthInitiator>(sp =>
            new WebAuthenticatorOAuthInitiator(apiBaseUrl, options.CallbackScheme, sp.GetRequiredService<ILogger<WebAuthenticatorOAuthInitiator>>()));
        // WebAuthenticator's pending state is in-memory only — persist an in-flight marker (+ a stashed callback on
        // Android cold starts) so OAuth survives the OS killing the process during the browser round-trip (NATIVE-12).
        // Windows' loopback flow blocks in-process and has no custom-scheme relaunch, so it registers no store.
        services.AddSingleton<IOAuthResumeStore, PreferencesOAuthResumeStore>();
#elif WINDOWS
        services.AddSingleton<IOAuthInitiator>(sp =>
            new LoopbackOAuthInitiator(apiBaseUrl, sp.GetRequiredService<ILogger<LoopbackOAuthInitiator>>()));
#endif

        // Two clients, mirroring the web host (Api / ApiAuth) to avoid a DI cycle:
        //  - AuthService gets its OWN client with NO Bearer handler. refresh/logout/otp/exchange are anonymous or carry
        //    the body refresh token — they need no Bearer, and this is what the handler below would depend on.
        //  - The default HttpClient (what the pages inject) attaches the in-memory JWT as a Bearer header so
        //    [Authorize] endpoints work — through the shared BearerScopedHandler, so only requests to the API's own
        //    origin carry it (v4 T49).
        // The X-Native-Client header on both selects the API's body-token transport.
        services.AddSingleton(sp =>
        {
            var authClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
            authClient.DefaultRequestHeaders.Add("X-Native-Client", "true");
            return new AuthService(
                authClient,
                sp.GetRequiredService<ILogger<AuthService>>(),
                sp.GetRequiredService<ISessionStore>(),
                sp.GetRequiredService<IOAuthInitiator>(),
                sp.GetService<IOAuthResumeStore>()); // null on Windows (loopback flow)
        });

        services.AddSingleton(sp =>
        {
            var handler = new BearerScopedHandler(sp.GetRequiredService<AuthService>(), new Uri(apiBaseUrl))
            {
                InnerHandler = new HttpClientHandler()
            };
            var client = new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
            client.DefaultRequestHeaders.Add("X-Native-Client", "true");
            return client;
        });

        return builder;
    }
}
