using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Core.Abstractions;
using Perezosoft.Infrastructure;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api.Tests;

/// <summary>
/// Pkg 3.5 (ADR-034 §5, "one context, the app owns every migration"): the platform's model ships as an abstract
/// <see cref="PlatformDbContext"/> in the Infrastructure package; the app's <c>AppDbContext</c> derives from it in the host,
/// and the migrations live beside it — so a package bump followed by <c>dotnet ef migrations add</c> puts only the
/// platform's delta into the app's own history. Platform code never names the app's context: it asks for
/// <see cref="PlatformDbContext"/> (or its factory) and DI hands it the app's.
/// </summary>
public class PlatformDbContextTests
{
    [Fact]
    public void ThePlatformsContext_IsAnAbstractBase_InTheInfrastructurePackage_AndTheAppsDerivesInTheHost()
    {
        Assert.True(typeof(PlatformDbContext).IsAbstract);
        Assert.Equal("Perezosoft.Infrastructure", typeof(PlatformDbContext).Assembly.GetName().Name);
        Assert.Equal("Perezosoft.Host", typeof(AppDbContext).Assembly.GetName().Name);
        Assert.Equal(typeof(PlatformDbContext), typeof(AppDbContext).BaseType);
    }

    [Fact]
    public void TheMigrations_AreTheApps_NoneShipInThePackage()
    {
        static bool IsMigrationType(Type t) => typeof(Migration).IsAssignableFrom(t) || typeof(ModelSnapshot).IsAssignableFrom(t);
        var packaged = typeof(PlatformDbContext).Assembly.GetTypes().Where(IsMigrationType).Select(t => t.FullName).ToList();
        Assert.True(packaged.Count == 0, "a migration ships inside the Infrastructure package — migrations are the app's (Pkg 3.5): " + string.Join(", ", packaged));

        var hostMigrations = typeof(AppDbContext).Assembly.GetTypes().Where(IsMigrationType).ToList();
        Assert.True(hostMigrations.Count > 10, $"probe: the host carries the migration history ({hostMigrations.Count} types)");
        Assert.All(hostMigrations, t => Assert.Equal(typeof(AppDbContext),
            ((DbContextAttribute)Attribute.GetCustomAttribute(t, typeof(DbContextAttribute))!).ContextType));
    }

    [Fact]
    public void TheInfrastructurePackage_CarriesThePlatformSchema()
    {
        Assert.Contains("Perezosoft.Infrastructure.platform-schema.json", typeof(PlatformDbContext).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public void TheInfrastructurePackage_CarriesNoDesignTooling()
    {
        // EF's Design package belongs where the migrations are; in the library it also dropped a stray runtimeconfig.json.
        var root = RepoRoot();
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Design", File.ReadAllText(Path.Combine(root, "src/Infrastructure/Perezosoft.Infrastructure.csproj")), StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.Design", File.ReadAllText(Path.Combine(root, "src/Host/Perezosoft.Host.csproj")), StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_HandsPlatformCode_TheAppsContext()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=x;Username=x;Password=x",
        }).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure<AppDbContext>(configuration, new FakeHostEnvironment(Environments.Development), billingEnabled: false);
        services.AddScoped<ICurrentTenant, TestCurrentTenant>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        Assert.IsType<AppDbContext>(context);
        Assert.Same(scope.ServiceProvider.GetRequiredService<AppDbContext>(), context); // one unit of work per scope

        using var fresh = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContext();
        Assert.IsType<AppDbContext>(fresh);
        Assert.NotSame(context, fresh); // the out-of-band writer gets its own
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("CLAUDE.md").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
