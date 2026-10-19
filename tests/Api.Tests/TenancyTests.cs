using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Testing.Suites;

namespace Perezosoft.Api.Tests;

/// <summary>The platform's tenancy tests: <see cref="PlatformTenancySuite{TTestContext}"/> (Pkg 5.1b) on this repo's test context.</summary>
[Collection(PostgresCollection.Name)]
public class TenancyTests(PostgresFixture fixture) : PlatformTenancySuite<TestAppDbContext>(fixture);
