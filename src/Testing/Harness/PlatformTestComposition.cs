using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Core.Abstractions;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Testing;

/// <summary>
/// What an app's test assembly tells the platform's harness (Pkg 5.1a, Arch A1): the contributors its slices register,
/// the process-level pins its integration host needs, its own service swaps, and its web origin. The harness is the
/// platform's and identical in every app; the app fills these once, from a <c>[ModuleInitializer]</c> in its own test
/// assembly (the platform's is <c>AppTestComposition</c>), so every test that builds a harness or a host sees them.
/// Each defaults to "the app adds nothing".
/// </summary>
public static class PlatformTestComposition
{
    /// <summary>The app's <see cref="ITenantDataContributor"/>s as production resolves them, built on the harness's
    /// context and clock (<see cref="ServiceHarness.Contributors"/>).</summary>
    public static Func<PlatformDbContext, TimeProvider, IEnumerable<ITenantDataContributor>> Contributors { get; set; } = (_, _) => [];

    /// <summary>Process-level pins the host reads at <c>CreateBuilder</c> time (environment variables); runs before the
    /// integration host is built.</summary>
    public static Action PinEnvironment { get; set; } = () => { };

    /// <summary>Service swaps for the integration host, after the platform's own (the throwaway database, the test auth
    /// handler).</summary>
    public static Action<IServiceCollection> ConfigureTestServices { get; set; } = _ => { };

    /// <summary>The app's web origin, for the settings double the services read links from (its LocalPorts).</summary>
    public static string ClientUrl { get; set; } = "https://localhost";
}
