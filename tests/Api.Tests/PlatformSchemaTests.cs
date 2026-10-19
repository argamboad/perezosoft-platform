using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Infrastructure.Persistence;
using Perezosoft.Testing.Suites;
using Xunit.Abstractions;

namespace Perezosoft.Api.Tests;

/// <summary>The platform's schema gate: <see cref="PlatformSchemaSuite{TProgram, TContext}"/> (Pkg 5.1b) over this repo's host.
/// On the platform, regenerate after a schema migration: <c>PLATFORM_SCHEMA_WRITE=1 dotnet test --filter PlatformSchemaTests</c>.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlatformSchemaTests(IntegrationTestFactory factory, ITestOutputHelper output)
    : PlatformSchemaSuite<Program, AppDbContext>(factory, output);
