using Android.App;
using AndroidX.Core.View;
using Perezosoft.Shared.Ui;

namespace Perezosoft.Maui;

/// <summary>
/// The colours behind Android's status and navigation bars, so the status bar reads as part of whatever
/// sits under it: the header (app.css <c>--bs-primary</c>, the header's background in both themes) on the
/// app's screens, the page itself (<c>--app-bg</c>) on the sign-in screens and the boot state, which have
/// no header. Change them with those tokens — REBRANDING.md §4 lists this file, and
/// <c>NativeChromeGateTests</c> fails while they disagree with app.css.
/// </summary>
public static class SystemBarColors
{
    public const string Light = "#465D4D";       // app.css :root --bs-primary (the header)
    public const string Dark = "#465D4D";        // app.css [data-bs-theme=dark] — the header keeps --bs-primary
    public const string LightGround = "#F5F7F4"; // app.css :root --app-bg
    public const string DarkGround = "#171C19";  // app.css [data-bs-theme=dark] --app-bg

    /// <summary>Paints the strip behind the status bar and picks icons that read on it.</summary>
    public static void Paint(Activity? activity, bool dark, bool ground)
    {
        if (activity?.Window is not { } window) return;

        var colour = (dark, ground) switch
        {
            (true, true) => DarkGround,
            (true, false) => Dark,
            (false, true) => LightGround,
            _ => Light,
        };
        activity.FindViewById(Android.Resource.Id.Content)?.SetBackgroundColor(Android.Graphics.Color.ParseColor(colour));

        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller is null) return;
        // Dark icons only on a light strip. Decided by the colour rather than the theme: the platform's
        // header is dark in BOTH themes, while a downstream app may give it a light surface in one.
        controller.AppearanceLightStatusBars = IsLight(colour);
        // The navigation bar floats over the page itself (the bottom stays edge-to-edge).
        controller.AppearanceLightNavigationBars = !dark;
    }

    /// <summary>True when dark text reads better than white on <paramref name="hex"/> (WCAG relative luminance).</summary>
    internal static bool IsLight(string hex)
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

/// <summary>Follows the page's theme (<see cref="Shared.Ui.Components.SystemBarThemeSync"/>).</summary>
public sealed class AndroidSystemBarTheme : ISystemBarTheme
{
    public Task ApplyAsync(string resolvedTheme, bool ground) =>
        MainThread.InvokeOnMainThreadAsync(() =>
            SystemBarColors.Paint(Platform.CurrentActivity, resolvedTheme == "dark", ground));
}
