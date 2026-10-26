using AppBrand.Api.Tests.Infrastructure;
using AppBrand.Host.Persistence;
using Perezosoft.Testing.Suites;

namespace AppBrand.Api.Tests;

/// <summary>This app's real host starts, reports ready, names the platform and serves a tenant call.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class CompositionSmokeTests(IntegrationTestFactory factory) : PlatformCompositionSmoke<Program, AppDbContext>(factory);
