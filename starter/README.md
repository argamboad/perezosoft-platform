# AppBrand

An app on the Perezosoft platform (`dotnet new perezosoft-app`). The platform — auth, tenancy, billing, jobs, files,
GDPR, MFA, notifications, admin, public API, webhooks — comes as the `Perezosoft.Platform.*` packages; this repo holds the
app: its host, its slices, its seeded UI and its tests.

## Run it

```bash
dotnet tool restore
dotnet platform up                 # Postgres + Mailpit (docker compose) and the telemetry dashboard
dotnet run --project src/Host      # migrates on start, serves the API
dotnet test                        # the platform's inherited suites against this app, and the sample's tests
```

Copy `.env.example` to `.env` first and set `Jwt__Secret`. Restoring needs a token for the platform's feed in your user
NuGet config (GitHub Packages, `read:packages`).

## Grow it

- A feature is a vertical slice under `src/Host/Features/<Name>/`, registered in `AppComposition.cs`. The DELETE-ME
  `Notes` sample is the reference; remove it when your first slice lands.
- A bump is one line: `PlatformPackagesVersion` in `Directory.Packages.props` (and the tag in `.github/workflows/ci.yml`).
- To work on the platform and this app together, set `PLATFORM_SOURCE=<a platform checkout>` in `.env`.
