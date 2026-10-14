using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Perezosoft.Api.Configuration;

namespace Perezosoft.Api.Tests.Configuration;

/// <summary>
/// Pkg 3.4 (ADR-034): the platform's configuration defaults ship INSIDE the Api package (src/Api/platform-defaults.json,
/// an embedded resource), and <c>AddPlatform</c> adds them as the lowest-precedence source — below the host's
/// appsettings*.json, user secrets, environment variables (the .env included) and the command line. The host's
/// appsettings.json keeps only what the app overrides. Before, every app carried a copy of the platform's defaults, and a
/// default changed in the platform reached an app only by a port.
/// </summary>
public class PlatformDefaultsTests
{
    [Fact]
    public void TheDefaults_ShipInsideTheApiAssembly_AsTheRepoFileSays()
    {
        var shipped = PlatformDefaults.Read();
        Assert.Equal("60", shipped["RefreshToken:ReuseGraceSeconds"]); // probe: the values a deployment runs on
        Assert.Equal("60", shipped["Jwt:ExpiryMinutes"]);
        Assert.Equal("6", shipped["Auth:Otp:Length"]);

        // The resource IS the repo file: what the docs and gates read is what every app runs.
        var file = new ConfigurationBuilder().AddJsonFile(Path.Combine(RepoRoot(), "src", "Api", "platform-defaults.json")).Build()
            .AsEnumerable().Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(file.OrderBy(kv => kv.Key), shipped.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void TheDefaults_AreTheLowestSource_TheHostFilesAndTheCommandLineWin()
    {
        var root = Directory.CreateTempSubdirectory("defaults-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), """{ "Jwt": { "Issuer": "Acme" } }""");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = root,
                EnvironmentName = "Production",
                Args = ["--Auth:Otp:Length=8"],
            });
            builder.Configuration.AddPlatformDefaults();
            builder.Configuration.AddPlatformDefaults(); // idempotent: a second call adds nothing

            Assert.Equal("Acme", builder.Configuration["Jwt:Issuer"]);          // the host's appsettings beat the default
            Assert.Equal("8", builder.Configuration["Auth:Otp:Length"]);        // the command line beats both
            Assert.Equal("60", builder.Configuration["Jwt:ExpiryMinutes"]);     // what nobody overrides comes from the package
            Assert.Equal(1, builder.Configuration.Sources.Count(s => s.GetType().Name == "PlatformDefaultsSource"));
            Assert.Equal("PlatformDefaultsSource", builder.Configuration.Sources[0].GetType().Name); // the very bottom
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TheHostsAppSettings_HoldOnlyOverrides()
    {
        // An app sets what it changes (its brand: the token issuer, its native callback scheme, its mail sender name) and
        // inherits the rest. A key the platform does not define would be a setting nothing reads.
        var defaults = PlatformDefaults.Read();
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Host", "appsettings.json")));
        var keys = new ConfigurationBuilder().AddJsonFile(Path.Combine(RepoRoot(), "src", "Host", "appsettings.json")).Build()
            .AsEnumerable().Where(kv => kv.Value is not null && !kv.Key.Contains("//", StringComparison.Ordinal)).Select(kv => kv.Key).ToList();
        Assert.True(keys.Count is > 0 and <= 6, $"src/Host/appsettings.json should carry only the app's overrides; it has {keys.Count}: {string.Join(", ", keys)}");
        var unknown = keys.Where(k => !defaults.ContainsKey(k)).ToList();
        Assert.True(unknown.Count == 0, "host settings the platform defines no default for (a typo, or a setting nothing reads): " + string.Join(", ", unknown));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found.");
    }
}
