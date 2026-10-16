namespace Perezosoft.Client.Maui;

/// <summary>
/// The four colours behind Android's status and navigation bars (Pkg 4.3): the header's in each theme, and the page's
/// ground in each theme (the sign-in screens and the boot state have no header). They are the APP's brand tokens — its
/// app.css <c>--bs-primary</c> and <c>--app-bg</c> — so the app supplies them (its <c>SystemBarColors</c>, held to
/// app.css by <c>NativeChromeGateTests</c>); the platform paints them.
/// </summary>
public sealed record SystemBarPalette(string Light, string Dark, string LightGround, string DarkGround)
{
    /// <summary>The strip's colour for a theme and screen: the ground where the page has no header.</summary>
    public string For(bool dark, bool ground) => (dark, ground) switch
    {
        (true, true) => DarkGround,
        (true, false) => Dark,
        (false, true) => LightGround,
        _ => Light,
    };

    /// <summary>True when dark text reads better than white on <paramref name="hex"/> (WCAG relative luminance).</summary>
    public static bool IsLight(string hex)
    {
        static double Channel(string h, int i)
        {
            var c = Convert.ToInt32(h.Substring(i, 2), 16) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var h = hex.TrimStart('#');
        var luminance = 0.2126 * Channel(h, 0) + 0.7152 * Channel(h, 2) + 0.0722 * Channel(h, 4);
        // Contrast against white equals contrast against black at L ≈ 0.179.
        return luminance > 0.179;
    }
}

#if ANDROID
/// <summary>Paints Android's system bars from a <see cref="SystemBarPalette"/>.</summary>
public static class SystemBars
{
    /// <summary>Paints the strip behind the status bar and picks icons that read on it.</summary>
    public static void Paint(Android.App.Activity? activity, SystemBarPalette palette, bool dark, bool ground)
    {
        if (activity?.Window is not { } window) return;

        var colour = palette.For(dark, ground);
        activity.FindViewById(Android.Resource.Id.Content)?.SetBackgroundColor(Android.Graphics.Color.ParseColor(colour));

        var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller is null) return;
        // Dark icons only on a light strip. Decided by the colour rather than the theme: the platform's header is dark
        // in BOTH themes, while an app may give it a light surface in one.
        controller.AppearanceLightStatusBars = SystemBarPalette.IsLight(colour);
        // The navigation bar floats over the page itself (the bottom stays edge-to-edge).
        controller.AppearanceLightNavigationBars = !dark;
    }
}

/// <summary>Follows the page's theme (the seeded UI's <c>SystemBarThemeSync</c> calls it) with the app's palette.</summary>
public sealed class AndroidSystemBarTheme(SystemBarPalette palette) : ISystemBarTheme
{
    public Task ApplyAsync(string resolvedTheme, bool ground) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            SystemBars.Paint(Platform.CurrentActivity, palette, resolvedTheme == "dark", ground));
}
#endif
