using Microsoft.Extensions.Configuration.Memory;

namespace Perezosoft.Api.Configuration;

/// <summary>
/// The platform's configuration defaults (Pkg 3.4, ADR-034), shipped inside this assembly: <c>platform-defaults.json</c>
/// is an embedded resource, so every app runs on the defaults of the package version it references and a default changed
/// in the platform reaches every app with the package. <see cref="AddPlatformDefaults"/> adds them as the LOWEST
/// configuration source — the host's appsettings*.json, user secrets, environment variables (the .env included) and the
/// command line all win — so a host's appsettings.json keeps only what the app overrides. AddPlatform calls it first.
/// </summary>
public static class PlatformDefaults
{
    /// <summary>The manifest name of the embedded defaults (see the Api project file).</summary>
    public const string ResourceName = "Perezosoft.Api.platform-defaults.json";

    /// <summary>The shipped defaults, flattened (Section:Key → value).</summary>
    public static IReadOnlyDictionary<string, string?> Read()
    {
        using var stream = typeof(PlatformDefaults).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in {typeof(PlatformDefaults).Assembly.GetName().Name}");
        return new ConfigurationBuilder().AddJsonStream(stream).Build()
            .AsEnumerable()
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inserts the platform's defaults at the bottom of the configuration sources. Idempotent. An in-memory source parsed
    /// once, not a JSON stream source: the configuration manager rebuilds every source each time one is added, and a stream
    /// can be read only once.
    /// </summary>
    public static IConfigurationBuilder AddPlatformDefaults(this IConfigurationBuilder builder)
    {
        if (!builder.Sources.OfType<PlatformDefaultsSource>().Any())
            builder.Sources.Insert(0, new PlatformDefaultsSource());
        return builder;
    }

    private sealed class PlatformDefaultsSource : MemoryConfigurationSource
    {
        public PlatformDefaultsSource() => InitialData = Read();
    }
}
