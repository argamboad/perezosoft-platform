# AppBrand

An app on the Perezosoft platform (`dotnet new perezosoft-app`). The platform — auth, tenancy, billing, jobs, files,
GDPR, MFA, notifications, admin, public API, webhooks — comes as the `Perezosoft.Platform.*` packages; this repo holds the
app: its host, its slices, its seeded UI and its tests.

## Run it

```bash
dotnet tool restore
dotnet platform up                 # Postgres + Mailpit (docker compose) and the telemetry dashboard
dotnet run --project src/Host      # migrates on start, serves the API
dotnet run --project src/Web       # the pages (Blazor WebAssembly), against the API above
dotnet test tests/Api.Tests        # the platform's inherited suites against this app, and the sample's tests
dotnet test tests/Ui.Tests         # the pages' tests
dotnet platform e2e                # the browser journeys, on a stack of their own (-Gates: the shipped defaults' lane)
dotnet build src/Maui -f net10.0-android   # the native app (the MAUI workload); dotnet platform android-emulator
```

Copy `.env.example` to `.env` first and set `Jwt__Secret`. Restoring needs a token for the platform's feed in your user
NuGet config (GitHub Packages, `read:packages`).

## Grow it

- A feature is a vertical slice under `src/Host/Features/<Name>/`, registered in `AppComposition.cs`. The DELETE-ME
  `Notes` sample is the reference; remove it when your first slice lands.
- The pages are the app's own: `src/Shared.Ui` (a Razor class library, so a native app can render the same pages),
  their strings in `Resources/AppStrings.resx` (English) and `AppStrings.es.resx` (Spanish), their tests in
  `tests/Ui.Tests`. The brand images in `src/Shared.Ui/wwwroot/brand`, `src/Web/wwwroot` and `src/Maui/Resources` are
  placeholders.
- The native app (`src/Maui`) renders the same pages. Its id, `com.appbrand.app` in `src/Maui/AppBrand.Maui.csproj`, is
  the store identity: settle it before the first release.
- The journeys (`tests/E2E.Tests`) drive the web host in a browser; CI runs them, and the Android smoke
  (`tests/native-smoke-android`), only when asked.
- A bump is one line: `PlatformPackagesVersion` in `Directory.Packages.props` (and the tag in `.github/workflows/ci.yml`).
- To work on the platform and this app together, set `PLATFORM_SOURCE=<a platform checkout>` in `.env`.
