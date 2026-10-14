using Perezosoft.Api;
using Perezosoft.Api.Endpoints;
using Perezosoft.Infrastructure.Persistence;

// The host's whole composition (Pkg 3.3, ADR-034): the platform (Perezosoft.Api's PlatformHosting), configured by the app's
// options (AppComposition.ConfigurePlatform: brand, plans, permissions), then the app's half (AppComposition.cs — after,
// so an app can replace a platform service), then the guard. This file is the same in the platform and every app; a change
// belongs in PlatformHosting or AppComposition, never here.

// Local dev: load secrets/config from the repo-root .env (the single local source of truth — see docs/DECISIONS.md),
// walking up from the working dir; a no-op without one (production uses real env vars) and skipped under SKIP_DOTENV=1,
// which the test assembly sets so test hosts never read a developer's .env.
LocalDotEnv.Load();

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform<AppDbContext>(AppComposition.ConfigurePlatform);
builder.Services.AddAppServices(builder.Configuration);

var app = builder.Build();
await app.MapPlatformAsync();
app.MapAppEndpoints();

// Refuse to boot on two endpoints with the same method and pattern (v4 T13): they would start fine and then answer
// every request to that route with a 500. Last before Run, so every mapping above is in the table.
RouteTableGuard.EnsureUnique(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));
app.Run();
