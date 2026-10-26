using AppBrand.Api.Tests.Infrastructure;
using AppBrand.Host.Persistence;
using Perezosoft.Testing.Suites;
using Xunit.Abstractions;

namespace AppBrand.Api.Tests;

/// <summary>This app's migrated database builds the platform's tables exactly as the platform's own migrations do.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlatformSchemaTests(IntegrationTestFactory factory, ITestOutputHelper output)
    : PlatformSchemaSuite<Program, AppDbContext>(factory, output);
