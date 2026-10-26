using AppBrand.Host.Features.Notes;
using Perezosoft.Core;
using Perezosoft.Core.Abstractions;

namespace AppBrand.Host;

/// <summary>
/// The app's half of composition: the one file outside <c>Features/</c> that may name a slice. <c>Program.cs</c> calls
/// <see cref="ConfigurePlatform"/> for the platform's options, <see cref="AddAppServices"/> after the platform's
/// registrations and <see cref="MapAppEndpoints"/> after its routes. A slice registers its handler, its
/// <see cref="ITenantDataContributor"/> and any <see cref="IStartupTask"/> here and maps its group here.
/// </summary>
public static class AppComposition
{
    /// <summary>The app's options for the platform: its brand, its plans, its own permissions.</summary>
    public static void ConfigurePlatform(PlatformOptions options)
    {
        options.Brand.Name = "AppBrand";
        // Also, for example:
        //   options.Brand.Colors.Primary = "#1f6f8b"; options.Brand.Logo = <PNG bytes>;
        //   options.Permissions.Add(new Permission("ManageProjects"), TenantRoles.Admin);
    }

    /// <summary>Registers every slice's services.</summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 🗑️ DELETE-ME: the sample slice (Features/Notes) — a handler + a tenant-data contributor here, its endpoints below.
        services.AddScoped<NotesHandler>();
        services.AddScoped<ITenantDataContributor, NotesDataContributor>();
        return services;
    }

    /// <summary>Maps every slice's endpoint group (each through <c>MapTenantFeatureGroup</c>).</summary>
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        // 🗑️ DELETE-ME: the sample slice's endpoints (remove with Features/Notes).
        app.MapNotes();
        return app;
    }
}
