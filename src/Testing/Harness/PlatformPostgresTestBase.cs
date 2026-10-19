using Perezosoft.Infrastructure.Persistence;
using Xunit;

namespace Perezosoft.Testing;

/// <summary>
/// Base for relational test classes that share the Postgres container. Truncates the database before EVERY test via
/// <see cref="IAsyncLifetime.InitializeAsync"/>, so isolation is structural rather than by-convention: there is no per-test
/// <c>ResetAsync()</c> to forget, and absolute-count assertions are safe by construction (B6-3). xUnit runs the members of a
/// collection serially, so the shared database is never reset out from under a concurrent test. Packaged (Pkg 5.1b),
/// generic over the app's test context.
/// </summary>
public abstract class PlatformPostgresTestBase<TTestContext>(PlatformPostgresFixture<TTestContext> fixture) : IAsyncLifetime
    where TTestContext : PlatformDbContext
{
    /// <summary>The shared container fixture — derived tests build contexts from it.</summary>
    protected PlatformPostgresFixture<TTestContext> Fixture { get; } = fixture;

    public Task InitializeAsync() => Fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}
