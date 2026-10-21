using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Infrastructure.Persistence;
using Perezosoft.Testing.Suites;

namespace Perezosoft.Api.Tests;

/// <summary>The platform's composition smoke: <see cref="PlatformCompositionSmoke{TProgram, TContext}"/> (Pkg 5.1b) over this repo's host.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class CompositionSmokeTests(IntegrationTestFactory factory) : PlatformCompositionSmoke<Program, AppDbContext>(factory);
