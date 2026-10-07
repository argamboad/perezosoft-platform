using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Core.Abstractions;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api.Tests.App;

/// <summary>
/// The app's half of the test chassis (Arch A1, R159). <c>ServiceHarness</c> and <c>IntegrationTestFactory</c> are
/// the platform's and stay identical across repos; what an app adds to them is said here: the contributors its
/// slices register, so an accept-and-dissolve test consults what production consults (an empty household must read
/// as empty to every slice, and every slice's wipe runs inside the dissolve); and the pins its integration host
/// needs regardless of the developer's <c>.env</c> (a rate provider that must not be reached, a consent app that
/// must read as unconfigured). The platform has none of either: the Notes sample is kept out of the platform tests
/// on purpose (<c>PlatformTests_DoNotDependOnTheDeleteMeNotesSample</c>, R9).
/// </summary>
internal static class AppTestComposition
{
    /// <summary>The app's <see cref="ITenantDataContributor"/>s as production resolves them, built on the harness's context and clock.</summary>
    public static IEnumerable<ITenantDataContributor> Contributors(AppDbContext db, TimeProvider clock) => [];

    /// <summary>Process-level pins the host reads at <c>CreateBuilder</c> time (environment variables); runs before the host is built.</summary>
    public static void PinEnvironment()
    {
    }

    /// <summary>Service swaps for the integration host, after the platform's own (the throwaway database, the test auth handler).</summary>
    public static void ConfigureTestServices(IServiceCollection services)
    {
    }
}
