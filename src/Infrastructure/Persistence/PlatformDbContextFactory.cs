using Microsoft.EntityFrameworkCore;

namespace Perezosoft.Infrastructure.Persistence;

/// <summary>
/// The app's context factory as the platform sees it (Pkg 3.5): platform code asks for
/// <c>IDbContextFactory&lt;PlatformDbContext&gt;</c> (the webhook delivery recorder writes out-of-band through it) and gets
/// a fresh instance of the app's own context, which carries the app's whole model.
/// </summary>
internal sealed class PlatformDbContextFactory<TContext>(IDbContextFactory<TContext> inner) : IDbContextFactory<PlatformDbContext>
    where TContext : PlatformDbContext
{
    public PlatformDbContext CreateDbContext() => inner.CreateDbContext();
}
