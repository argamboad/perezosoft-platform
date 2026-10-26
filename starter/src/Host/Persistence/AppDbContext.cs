using Microsoft.EntityFrameworkCore;
using Perezosoft.Core.Abstractions;
using Perezosoft.Core.Entities;
using Perezosoft.Infrastructure.Persistence;

namespace AppBrand.Host.Persistence;

/// <summary>
/// AppBrand's database context: the platform's half is <see cref="PlatformDbContext"/> (its sets, the tenant filter, the
/// interceptors), in the Infrastructure package; this is the app's half — its sets here, its model rules in
/// <see cref="ApplyAppModel"/>, its entity configurations discovered from this assembly. Its migrations live beside it.
/// </summary>
public class AppDbContext : PlatformDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : this((DbContextOptions)options, currentTenant) { }

    /// <summary>For a test harness that subclasses this context with its own options (a test-only fixture entity).</summary>
    protected AppDbContext(DbContextOptions options, ICurrentTenant currentTenant)
        : base(options, currentTenant) { }

    // 🗑️ DELETE-ME: the sample slice's set (remove with Features/Notes).
    public DbSet<Note> Notes => Set<Note>();

    // The app's entity configurations: this assembly's (a test subclass in another assembly still gets this one's).
    protected override IEnumerable<System.Reflection.Assembly> AppModelAssemblies => [typeof(AppDbContext).Assembly];

    /// <summary>The app's own model rules, applied after the platform's.</summary>
    protected override void ApplyAppModel(ModelBuilder builder) { }
}
