using Perezosoft.Testing;

namespace Perezosoft.Api.Tests.Infrastructure;

/// <summary>The platform's database fixture: the packaged one (Pkg 5.1a) over this repo's <see cref="TestAppDbContext"/>.</summary>
public sealed class PostgresFixture : PlatformPostgresFixture<TestAppDbContext>;

/// <summary>
/// xUnit collection so the (relatively expensive) container is created once and
/// shared by every relational test class. Decorate such classes with
/// <c>[Collection(PostgresCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
