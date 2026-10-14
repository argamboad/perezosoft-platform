using Perezosoft.Api.Features.Notes;
using Perezosoft.Core;
using Perezosoft.Core.Abstractions;

namespace Perezosoft.Api;

/// <summary>
/// The app's half of composition (Arch A1, R159): the one file outside <c>src/Host/Features/</c> that may name a
/// slice (R8, as amended). <c>Program.cs</c> calls <see cref="AddAppServices"/> once after the platform's
/// registrations and <see cref="MapAppEndpoints"/> once after the platform's routes, and is otherwise identical in
/// the platform and every app. A slice registers its handler, its <see cref="ITenantDataContributor"/> and any
/// <see cref="IStartupTask"/> here and maps its group here; nothing else central is edited (the add-a-slice
/// checklist in <c>docs/WAYS_OF_WORKING.md</c>). The platform ships this file with the Notes sample as its content;
/// an app replaces the body and keeps the three method names.
/// </summary>
public static class AppComposition
{
    /// <summary>
    /// The app's options for the platform (Pkg 3.9, ADR-034 §4): its plans, its own permissions, its brand. Program.cs hands
    /// this to <c>AddPlatform</c>, so the app configures the packaged platform here and Program.cs stays identical. The
    /// platform's own host sets nothing: every option starts at the platform's default.
    /// </summary>
    public static void ConfigurePlatform(PlatformOptions options)
    {
        // An app sets, for example:
        //   options.Brand.Name = "Vuelto"; options.Brand.Colors.Primary = "#1f6f8b"; options.Brand.Logo = <PNG bytes>;
        //   options.Brand.Strings.Add(new ResourceManager("Vuelto.Host.Email.EmailStrings", typeof(AppComposition).Assembly));
        //   options.Permissions.Add(new Permission("ManageProjects"), TenantRoles.Admin);
    }

    /// <summary>Registers every slice's services. Behind each slice's own <c>Enabled</c> setting where it has one.</summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 🗑️ DELETE-ME: sample feature slice (Features/Notes) — the reference for how a vertical slice wires up:
        // a handler + a tenant-data contributor here, its endpoints mapped below.
        services.AddScoped<NotesHandler>();
        services.AddScoped<ITenantDataContributor, NotesDataContributor>();
        return services;
    }

    /// <summary>Maps every slice's endpoint group (each through <c>MapTenantFeatureGroup</c>, R6).</summary>
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        // 🗑️ DELETE-ME: sample feature slice endpoints (remove with Features/Notes).
        app.MapNotes();
        return app;
    }
}
