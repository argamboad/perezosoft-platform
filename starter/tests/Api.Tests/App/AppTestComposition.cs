using AppBrand.Host.Features.Notes;
using AppBrand.Host.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Entities;
using Perezosoft.Infrastructure.Repositories;
using Perezosoft.Testing;

namespace AppBrand.Api.Tests.App;

/// <summary>
/// What this app tells the packaged harness: the contributors its slices register (so a dissolve in a test consults what
/// production consults), the process-level pins its integration host needs, its own service swaps, its web origin.
/// </summary>
internal static class AppTestComposition
{
#pragma warning disable CA2255 // a module initializer in a test assembly is the point: it runs before any test builds a harness
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
    {
        PlatformTestComposition.Contributors = (db, clock) => Contributors((AppDbContext)db, clock);
        PlatformTestComposition.PinEnvironment = PinEnvironment;
        PlatformTestComposition.ConfigureTestServices = ConfigureTestServices;
        PlatformTestComposition.ClientUrl = LocalPorts.WebHttpsUrl;
    }

    /// <summary>The app's <see cref="ITenantDataContributor"/>s as production resolves them.</summary>
    public static IEnumerable<ITenantDataContributor> Contributors(AppDbContext db, TimeProvider clock) =>
    [
        new NotesDataContributor(new EfRepository<Note>(db)), // 🗑️ DELETE-ME with the sample slice
    ];

    /// <summary>Process-level pins the host reads at <c>CreateBuilder</c> time (environment variables).</summary>
    public static void PinEnvironment()
    {
    }

    /// <summary>Service swaps for the integration host, after the platform's own.</summary>
    public static void ConfigureTestServices(IServiceCollection services)
    {
    }
}
