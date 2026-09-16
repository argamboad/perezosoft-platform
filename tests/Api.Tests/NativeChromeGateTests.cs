using System.Text.RegularExpressions;

namespace Perezosoft.Api.Tests;

/// <summary>
/// The Android shell's chrome (2026-09-16), found downstream (JiggerJot, y-el-vuelto) by running the real
/// Android app on an emulator rather than a phone-width browser window. The status bar is drawn by the OS,
/// so its colour lives in C# and XML where no stylesheet reaches it: the template painted it one hard-coded
/// green in every theme, and no rebrand touched it. These gates hold those colours to app.css's tokens, so
/// a palette change that forgets them fails here instead of on a phone.
/// </summary>
public class NativeChromeGateTests
{
    private static readonly string AndroidDir = Path.Combine("src", "Maui", "Platforms", "Android");

    private static string ReadRaw(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static string StripCssComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    private static string Hex(string value) => value.Trim().ToUpperInvariant();

    /// <summary>The app.css custom properties declared in the block the selector opens (the first such block).</summary>
    private static Dictionary<string, string> Tokens(string css, string selectorPattern)
    {
        var block = Regex.Match(css, selectorPattern + @"\s*\{([^}]*)\}");
        Assert.True(block.Success, $"no block for {selectorPattern} in app.css");
        return Regex.Matches(block.Groups[1].Value, @"(--[\w-]+)\s*:\s*(#[0-9A-Fa-f]{6})\b")
            .ToDictionary(m => m.Groups[1].Value, m => Hex(m.Groups[2].Value));
    }

    private static Dictionary<string, string> SystemBarConstants()
    {
        var source = ReadRaw(AndroidDir, "AndroidSystemBarTheme.cs");
        Assert.Contains("class SystemBarColors", source);
        return Regex.Matches(source, @"const\s+string\s+(\w+)\s*=\s*""(#[0-9A-Fa-f]{6})""")
            .ToDictionary(m => m.Groups[1].Value, m => Hex(m.Groups[2].Value));
    }

    [Fact]
    public void MainActivity_CarriesNoLiteralColour()
    {
        // It painted "#6B8A72" behind the status bar in every theme. The colour now comes from SystemBarColors.
        var activity = ReadRaw(AndroidDir, "MainActivity.cs");

        Assert.DoesNotMatch(new Regex(@"""#[0-9A-Fa-f]{6}"""), activity);
        Assert.Contains("SystemBarColors.Paint(", activity);
    }

    [Fact]
    public void SystemBarColors_AreAppCssTokens_InEachTheme()
    {
        var css = StripCssComments(ReadRaw("src", "Shared.Ui", "wwwroot", "css", "app.css"));
        var light = Tokens(css, @":root");
        var dark = Tokens(css, @"\[data-bs-theme=""dark""\]");
        var bars = SystemBarConstants();

        // The status bar sits over the header on the app's screens. The premise: the header is the primary.
        var header = StripCssComments(ReadRaw("src", "Shared.Ui", "Components", "AppHeader.razor.css"));
        Assert.Matches(new Regex(@"\.app-header\s*\{[^}]*background:\s*var\(--bs-primary\)"), header);

        Assert.Equal(light["--bs-primary"], bars["Light"]);
        Assert.Equal(dark.GetValueOrDefault("--bs-primary", light["--bs-primary"]), bars["Dark"]);
        // ...and over the bare page on the sign-in screens and the boot state.
        Assert.Equal(light["--app-bg"], bars["LightGround"]);
        Assert.Equal(dark["--app-bg"], bars["DarkGround"]);
    }

    [Fact]
    public void AndroidThemeColours_AreTheBrandTokens()
    {
        var css = StripCssComments(ReadRaw("src", "Shared.Ui", "wwwroot", "css", "app.css"));
        var light = Tokens(css, @":root");
        var xml = ReadRaw(AndroidDir, "Resources", "values", "colors.xml");

        string Colour(string name)
        {
            var m = Regex.Match(xml, $@"<color\s+name=""{name}"">\s*(#[0-9A-Fa-f]{{6}})\s*</color>");
            Assert.True(m.Success, $"colors.xml has no {name}");
            return Hex(m.Groups[1].Value);
        }

        Assert.Equal(light["--bs-primary"], Colour("colorPrimary"));
        Assert.Equal(light["--brand-dark"], Colour("colorPrimaryDark"));
        Assert.Equal(light["--bs-primary"], Colour("colorAccent"));
    }

    [Fact]
    public void TheAndroidShell_PaintsTheBarsFromThePage()
    {
        var program = ReadRaw("src", "Maui", "MauiProgram.cs");
        var layout = ReadRaw("src", "Shared.Ui", "Layout", "MainLayout.razor");

        Assert.Matches(new Regex(@"#if ANDROID\s+(//[^\n]*\n\s*)*builder\.Services\.AddSingleton<ISystemBarTheme,\s*AndroidSystemBarTheme>\(\);"), program);
        Assert.Contains("<SystemBarThemeSync", layout);
    }

    [Fact]
    public void TheAndroidShell_PadsForTheStatusBarOnce()
    {
        var activity = ReadRaw(AndroidDir, "MainActivity.cs");

        // The shell pads its content below the status bar. Current Android WebViews also report that inset
        // to CSS, so the header's env(safe-area-inset-top) added it a second time — a 55px empty band.
        // The shell hands the WebView insets with the top already spent.
        Assert.Contains("SetPadding(bars.Left, bars.Top, bars.Right, 0)", activity);
        Assert.Matches(new Regex(@"SetInsets\(\s*WindowInsetsCompat\.Type\.StatusBars\(\)"), activity);
        Assert.Matches(new Regex(@"SetInsets\(\s*WindowInsetsCompat\.Type\.DisplayCutout\(\)"), activity);
    }

    [Fact]
    public void ThemeJs_TellsAWatcherEveryThemeItApplies()
    {
        var js = ReadRaw("src", "Shared.Ui", "wwwroot", "js", "theme.js");

        Assert.Contains("watch:", js);
        Assert.Contains("unwatch:", js);
        Assert.Contains("invokeMethodAsync('OnThemeApplied'", js);
    }

    [Fact]
    public void Rebranding_NamesTheAndroidSystemBars()
    {
        var rebranding = ReadRaw("docs", "REBRANDING.md");

        Assert.Contains("Platforms/Android/Resources/values/colors.xml", rebranding);
        Assert.Contains("SystemBarColors", rebranding);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root.");
    }
}
