using Microsoft.EntityFrameworkCore;
using Perezosoft.Core.Abstractions;

// The context keeps the namespace it had before the platform half moved into the package (Pkg 3.5): every migration says
// `[DbContext(typeof(AppDbContext))]` with `using Perezosoft.Infrastructure.Persistence;`, and they move into this host
// byte for byte. The starter (M6) gives a new app its own namespace; its migrations are its own from the start.
namespace Perezosoft.Infrastructure.Persistence;

/// <summary>
/// This app's database context (Pkg 3.5, ADR-034): the platform's half is <see cref="PlatformDbContext"/>, in the
/// Infrastructure package; this file wires the app's half in and is the same in every app. The app's sets and model rules
/// are in <c>AppDbContext.App.cs</c>; its entity configurations in this assembly are discovered; its migrations live
/// beside it (Persistence/Migrations), because a migration belongs to its context's assembly.
/// </summary>
public partial class AppDbContext : PlatformDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : this((DbContextOptions)options, currentTenant) { }

    /// <summary>
    /// Non-generic-options constructor so a test harness can subclass this context (e.g. to add a test-only
    /// <c>ITenantScoped</c> fixture entity) with its own <c>DbContextOptions&lt;TDerived&gt;</c>, without the platform tests
    /// depending on the DELETE-ME sample slice (v2 audit TR-1). Not used by the app itself.
    /// </summary>
    protected AppDbContext(DbContextOptions options, ICurrentTenant currentTenant)
        : base(options, currentTenant) { }

    // The app's entity configurations: this assembly's (a test subclass in another assembly still gets this one's).
    protected override IEnumerable<System.Reflection.Assembly> AppModelAssemblies => [typeof(AppDbContext).Assembly];

    protected override void ApplyAppModel(ModelBuilder builder) => OnAppModelCreating(builder);

    /// <summary>The app's half of the model (Arch A1, R159): its own filters and conventions, in <c>AppDbContext.App.cs</c>.
    /// Unimplemented on the platform; the compiler drops the call.</summary>
    partial void OnAppModelCreating(ModelBuilder builder);
}
