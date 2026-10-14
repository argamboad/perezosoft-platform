using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Infrastructure.Persistence;

namespace Perezosoft.Api.Tests.Integration;

/// <summary>
/// v4 T42 (JOBS-10): the webhook handler writes its failure row through <c>IDbContextFactory&lt;PlatformDbContext&gt;</c> (the app's factory underneath, Pkg 3.5)
/// from inside the outbox's scope. That registration (scoped, next to the scoped context it must not share a
/// connection with) had no DI test — a change to its lifetime would only show as a runtime resolution error the
/// first time a delivery failed.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DbContextFactoryScopeTests(IntegrationTestFactory factory)
{
    [Fact]
    public async Task DbContextFactory_ResolvesInsideAScope_AndHandsOutAnIndependentContext()
    {
        using var scope = factory.Services.CreateScope();

        var scoped = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var made = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        await using (made)
        {
            Assert.NotSame(scoped, made); // its own connection: a failure row survives the ambient rollback
            Assert.True(await made.Database.CanConnectAsync());
        }
    }
}
