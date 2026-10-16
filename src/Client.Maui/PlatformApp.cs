using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Perezosoft.Client.Maui;

/// <summary>What <see cref="PlatformMauiHosting.UsePlatformShell{TRoot}"/> was told: the root component, the window's
/// title, the host page.</summary>
public sealed record PlatformShellOptions(Type RootComponent, string Title, string HostPage);

/// <summary>
/// The MAUI application of every app (Pkg 4.3, ADR-034): one window holding the BlazorWebView page. An app adds its
/// own <c>Application</c> only if it wants native pages; the platform's shell is all a Blazor Hybrid app needs.
/// </summary>
public sealed class PlatformApp(AppResumeNotifier resumeNotifier, PlatformShellOptions shell) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new PlatformPage(shell)) { Title = shell.Title };
        // External round-trips (billing checkout/portal in the system browser) return here with no navigation — tell
        // the open page to refresh itself (NATIVE-4, parity gap G2). Two hooks because the platforms signal the return
        // differently: Android covers the Activity (onStop) and comes back via Resumed; on desktop the browser only
        // steals focus, so the return is Activated (which also fires on plain refocus — the refresh is one cheap GET,
        // standard refresh-on-focus behavior).
        window.Resumed += (_, _) => resumeNotifier.Notify();
        window.Activated += (_, _) => resumeNotifier.Notify();
        return window;
    }
}

/// <summary>The page: a BlazorWebView over the app's host page, rooted on the app's root component (its Router).</summary>
public sealed class PlatformPage : ContentPage
{
    private readonly BlazorWebView _blazorWebView;

    public PlatformPage(PlatformShellOptions shell)
    {
        _blazorWebView = new BlazorWebView { HostPage = shell.HostPage };
        _blazorWebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = shell.RootComponent });
        Content = _blazorWebView;
    }

    // Android hardware back (NATIVE-4, parity gap G3): walk the WebView's history — which includes Blazor's pushState
    // route changes — instead of the MAUI default (background/exit the Activity on every press). At the history root,
    // fall through to the default so back still leaves the app. No-op on other platforms (no hardware back button).
    protected override bool OnBackButtonPressed()
    {
#if ANDROID
        if (_blazorWebView.Handler?.PlatformView is Android.Webkit.WebView webView && webView.CanGoBack())
        {
            webView.GoBack();
            return true;
        }
#endif
        return base.OnBackButtonPressed();
    }
}
