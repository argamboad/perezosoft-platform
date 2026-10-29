# Creating an App from this Platform — the end-to-end guide

> The spine document: every phase from "I have an idea" to "customers are using it in
> production", in order, with links to the detailed doc for each step. The README gives the
> short version; this is the full path with nothing implicit.

**An app is not a copy of this repo** (ADR-034). The platform ships as the `Perezosoft.Platform.*` packages; an app
is what `dotnet new perezosoft-app` writes from `starter/`: a host with your slices, the seeded UI, the platform's
suites inherited, and about twenty files held in step with the platform (the register). A fix to the platform reaches
your app as a package bump, not as a port.

**The journey at a glance:**

| Phase | What happens | Where | Typical time |
|---|---|---|---|
| 0 | Install tools, get the feed token | your machine | 30 min (once) |
| 1 | Conceptualize the app | Claude **chat** | a half-day of thinking |
| 2 | Create the app from the template | your machine + GitHub | 15 min |
| 3 | Brand it, pin its ports, fill its secrets | Claude **Code** | ~1 hour |
| 4 | First local run | your machine | 15 min |
| 5 | Model the domain | Claude Code | hours |
| 6 | Build features, slice by slice | Claude Code | your actual product work |
| 7 | Stand up staging | Neon/Brevo/Stripe/Render | an afternoon (once) |
| 8 | Activate production | Render + GitHub | ~10 min |
| 9 | Native apps (optional, later) | — | when the web product is stable |

Phases 0–4 are a **single day**. Everything the platform already does (auth, tenancy, billing,
GDPR, admin, CI/CD — see `OVERVIEW.md`) never appears on this list: that's the point. Taking a
platform release later is its own short section, after Phase 6.

---

## Phase 0 — Tools (once per machine)

- **Git**, and a **GitHub** account (repo + CI live there).
- **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>. The app pins the exact SDK in its
  `global.json` (a register file: it moves with the platform's releases).
- **Docker Desktop** — runs local Postgres + the Mailpit email trap, and the Testcontainers tests.
- **Claude** — chat access for Phase 1, Claude Code for Phases 3+.
- **The feed token.** The packages live on GitHub Packages (the `perezosoft` source in the app's `nuget.config`), and
  restoring them needs a classic GitHub token with only `read:packages`. Put it in your user NuGet config once — never
  in a repo file:

  ```bash
  dotnet nuget add source https://nuget.pkg.github.com/argamboad/index.json --name perezosoft --username <your GitHub user> --password <the token> --store-password-in-clear-text
  ```

  (On Windows drop `--store-password-in-clear-text`: the password is encrypted there.) Keep the token in the app's
  `.env` too, as `PLATFORM_PACKAGES_TOKEN`: the workspace's `ws setup` and `bin/cloud-setup` write the credential from it
  on a fresh machine or codespace.
- An IDE is optional (Claude Code does the driving); Rider/VS/VS Code all work. For VS and VS Code, run
  `dotnet platform dev-profiles` once per checkout: it writes the start profiles into gitignored files (VS: API + Web,
  API + app on the toolbar's Debug Target; VS Code: API + Web, Windows desktop, Android phone/tablet on a device or an
  emulator) — `MOBILE_TESTING.md` §3.

No cloud accounts are needed until Phase 7.

## Phase 1 — Conceptualize (in Claude chat, not Code)

Paste **`_PLATFORM_PRIMER.md`** (in this repo) into a fresh Claude chat and describe your app. The primer makes
Claude run a structured session: clarifying questions, recommendations, scope discipline
(an explicit OUT list), and ADRs for every settled decision. The stack is frozen, so the whole
conversation is about *your product*.

**Leave this phase holding:** filled-in `PROJECT_BRIEF.md`, `FEATURES.md`, `DATA_MODEL.md`,
`DECISIONS.md` — plus three things the next phase needs: an **app name**, the **tenant's
app-facing label** (Team? Workspace? Household?), and a **logo file** (SVG or large PNG).

Two traps the first downstream app fell into, so the conceptualization avoids them:
- **Don't re-model `Tenant` and `User`.** They are platform entities; tenancy is a
  `TenantMembership` row, not a `tenant_id` on `User` (ADR-003). The app's `DATA_MODEL.md` references
  them and adds only per-user preference fields.
- **If the app has a shared, seeded catalog next to tenant-owned rows** (recipes, templates, a product
  list), decide *before Phase 5* how `tenant_id = null` rows coexist with the global tenant filter and
  RLS, which hide them by default. The platform's answer is `ISharedOrTenantScoped` (Arch A4; Phase 5 below).

## Phase 2 — Create the app from the template

1. **Install the template** (once per machine, and again for each release you start from):

   ```bash
   dotnet new install Perezosoft.Platform.Templates::<version>
   ```

   The version is a platform release (`1.0.0-beta.1`, …; the release notes name the current one). It restores from the
   feed, so Phase 0's token must be in place.
2. **Write the app:** `dotnet new perezosoft-app -n <Brand> -o <brand>`. `<Brand>` is the name in its .NET form
   (`JiggerJot`): it becomes the solution, the projects and the namespaces (`JiggerJot.Host`, `JiggerJot.Api.Tests`),
   its lower-case form the identifiers (`com.jiggerjot.app`), its upper-case form the environment prefixes. The template
   renames its placeholder (`AppBrand`, `appbrand`, `APPBRAND`) and nothing else: the packages keep their names
   (`Perezosoft.Platform.*`, `using Perezosoft.Infrastructure…`) in every app, by design.
3. **Make it a repository:** `git init -b develop`, commit, create the GitHub repo and push. **`main` is deploy-only**
   (nothing lands there except release merges), **`develop` is the working branch** — one branch + PR per slice.
   Adopting it into a repo you already have (a docs-only repo from Phase 1) works the same: write the app into that
   folder and commit on top of your history.
4. **Merge** the Phase-1 docs into a `docs/` folder of your own. The template ships no docs beyond the Postman
   collection: your `PROJECT_BRIEF.md`, `FEATURES.md`, `DATA_MODEL.md` and `DECISIONS.md` are the app's. Number app
   decisions with an app prefix (`JJ-001…` for JiggerJot); cite the platform's as `ADR-…`.
5. **The token for CI:** the app's `ci.yml` calls the platform's reusable workflow (`app-ci.yml`) at the tag of the
   release it is on, with `packages: read`. The job token reads the feed only once the packages grant your repository
   access: on each private package's page (GitHub → Packages → *Manage Actions access*), add the repo with *Read*.
6. Open a pull request (a push alone runs nothing — ADR-031). **Its CI run should be green**: build, the inherited
   suites, the UI tests, secret/license gates, the browser journeys. The device legs (MAUI builds + native smokes) run
   from *Run workflow* → `devices`; deploys from *Run workflow* → `deploy`, once Phase 7's secrets exist.
7. **No branch protection** (ADR-031): wait for a pull request's run to finish green before merging it.

## Phase 3 — Brand it, pin its ports, fill its secrets (first Claude Code session)

Open Claude Code in the repo with the app name, the tenant label and the logo attached. Claude then:

- Works **`docs/REBRANDING.md` end to end** — it is short now, because the template already wrote your name into
  every identifier: the brand assets (logo, icons, splash, the rasters generated from your SVGs), the **colour palette
  derived from your logo** (`src/Shared.Ui/wwwroot/css/app.css`, the Android system bars), the **email brand** in
  `AppComposition.ConfigurePlatform` (`options.Brand` — the mails are the package's, configured, never edited), the
  product copy in both `.resx` files, the native app's id and OAuth callback scheme, and your tenant noun.
- Writes the app's `CLAUDE.md` (golden rules, conventions) from your Phase-1 docs.
- Verifies: a test OTP email arrives with the new brand, and REBRANDING's verify list passes.

### Ports: re-pin before the first run

If anything else from this platform runs on the same machine — the platform itself, another
app — the defaults collide. `docker ps` first: every compose stack binds Postgres and
Mailpit host ports from its own `.env`. The **app** ports are pinned in the launch profiles and
mirrored across the tree, so pick a free set and change it everywhere in **one** pass. JiggerJot took API
`7360`/`5438`, Web `7208`/`5369`, `DB_PORT=5435`, `MAIL_SMTP_PORT=1027`, `MAIL_UI_PORT=8027`, `APP_PORT=8280`; the
platform sits on `7160`/`5238`, `7008`/`5169`, `5433`, `1025`/`8025`, `8080` (the README's table has every app's).

The block is stated once, in **`local-ports.props`** (Arch A10). Edit the eight numbers there, then
`dotnet platform ports -Apply`: it rewrites every file that cannot read MSBuild — both
`Properties/launchSettings.json`, `src/Host/appsettings.Development.json` (SMTP port, CORS origins,
`Auth:AppBaseUrl`), `src/Web/wwwroot/appsettings.json`, `docker-compose.yml`'s `${VAR:-default}` fallbacks,
`.env.example` (`*_PORT` and the connection string), `tests/E2E.Tests/playwright.runsettings` and the Postman
local environment. Code (the API's dev fallback, `MauiProgram.cs`, the E2E defaults, the test harness) and the
MAUI `adb reverse` read the props at build time, so nothing to edit there. Compose already namespaces containers,
network and the `db_data` volume by folder name, so no rename is needed there.

### Secrets: `.env`

`cp .env.example .env`, then fill it: `Jwt__Secret` (any random string of 32+ characters) is the one key the API
refuses to start without; `PLATFORM_PACKAGES_TOKEN` (Phase 0) keeps the feed working on the next machine. The file is
gitignored and every value the app reads lives there, never in user or machine environment variables. The rest of
`.env.example` is the configuration reference — every key the API reads, with its default — to uncomment as Phase 7
needs it (`DEPLOYMENT.md` has the full table).

### The platform boundary: stamp, register, seed (Arch A2, Pkg 6.4)

Your app says which platform it is on. `platform-stamp.json` says `"packages": true`, the release (`commit`: the
version, `0.0.0-platform` until your first port), and the brand map (`Perezosoft` → your brand). `platform-ownership.json`
classes every tracked file:

| Class | What it is | What holds it |
|---|---|---|
| `register` | held in step with the platform: the source switch (`build/PlatformSource.*`), `global.json`, `nuget.config`, the tool manifest, the map itself, `Directory.Build.*`, `.gitattributes`, the RLS role script, the host's `Program.cs` and `AppDbContextFactory.cs`, the test host and fixtures (`tests/Api.Tests/Infrastructure/**`), the four one-line suites | `OwnershipTests` compares each with the copy the Testing package embeds; a difference fails unless `tests/Api.Tests/App/PlatformDivergences.json` lists it with a reason |
| `seed` | yours once written: the UI, the journeys, the project files, `Directory.Packages.props`, the CI caller, the config, the docs | nothing — the port tool lists what the platform changed in them, for you to take or leave |
| `app` | yours alone: `src/Host/Features/**`, `AppComposition.cs`, `AppDbContext.cs`, every migration, `tests/Api.Tests/App/**`, `ArchitectureTests.cs` | the inherited architecture and tenancy suites |
| `sample` | the DELETE-ME Notes slice | removable (Phase 6) |
| `platform`, `producer` | the packaged source and the release machinery | never in an app |

The map's `keep` list (the package ids `Perezosoft.Platform.*`, the feed key `perezosoft`) is never renamed. A folder of
your own at the top of the repo (jigger-jot's `seed/`) is classed in `platform-ownership.App.json`, same shape, applied
after the platform's rules. `/api/version` reports the platform release beside your own commit.

## Phase 4 — First local run

```bash
dotnet tool restore            # dotnet-ef, the license checker, `dotnet platform`
dotnet platform up             # Postgres 17 + Mailpit (docker compose), and the telemetry dashboard
dotnet run --project src/Host --launch-profile https    # migrates on start; the API on https://localhost:7160
dotnet run --project src/Web                           # the pages on https://localhost:7008
```

**The initial migrations are already there.** `src/Host/Persistence/Migrations` holds three, written when the template
was packed: `InitialCreate` (the platform's tables and the sample's), `RlsTenancyBackstop` (the row-level-security
policies, through `ApplyPlatformStep`) and `NotesSampleRlsPolicy`. They are the start of *your* history (ADR-034
point 5: the app owns every migration); the host applies them at startup, and `PlatformSchemaTests` holds the migrated
database to the `platform-schema.json` the package embeds.

Sign in with **"Email me a 6-digit code"** and read the code from Mailpit at
<http://localhost:8025> — no OAuth keys needed yet (Google/Microsoft buttons need Phase 7 §5
credentials; everything else works without them). You now have a branded, multi-tenant,
running app with zero features. `dotnet test tests/Api.Tests` and `dotnet test tests/Ui.Tests` run the inherited
suites and the pages' tests; `dotnet platform e2e` runs the browser journeys on a stack of their own.

## Phase 5 — Model the domain

From `DATA_MODEL.md`, Claude Code adds your entities under **`src/Host/Entities/`** (tenant-owned ones implement
`ITenantScoped` — the global query filter then isolates them automatically), their sets on `AppDbContext`, and a
migration on top of the three above (`dotnet ef migrations add <Name> --project src/Host`). The inherited architecture
suite fails the build if an entity dodges the tenancy rules. Read your `DATA_MODEL.md`'s derived-rules section first:
**derived values are computed, never stored**. The checklist, step by step, is `WAYS_OF_WORKING.md`'s
"Add-a-slice mechanical checklist".

### If your app has a shared catalog beside tenant-owned rows

This is the Phase-1 decision coming due. A table holding **both** shared rows (`tenant_id` null, readable by every
tenant, owned by none) and tenant-owned rows cannot implement `ITenantScoped`: that interface's `TenantId` is
non-nullable, and both the global filter and the forced RLS policy test `tenant_id = current`, so a shared row is
invisible in the app *and* at the database.

The platform's shape is **`ISharedOrTenantScoped`** (Arch A4): its own query filter
(`tenant_id == null || tenant_id == current`), and `RlsDdl.SharedOrTenantStatementsFor` for the policies.
⚠️ **Do not write that policy as a single `FOR ALL` statement.** The obvious predicate —
`USING (tenant_id IS NULL OR tenant_id = current OR bypass)` — is right for reading and wrong for
everything else. Postgres checks `DELETE` against `USING` and **never** against `WITH CHECK`, so one
`FOR ALL` policy carrying it lets any tenant delete or rewrite any shared row, which is the one thing a
shared catalog exists to prevent. The statements the platform generates are **four command-scoped policies per
table**: `SELECT` admits shared rows, `INSERT`/`UPDATE`/`DELETE` admit the tenant's own only. Seeding shared rows then
requires the bypass GUC, which is the right answer anyway — it makes seeding deliberate and greppable.

Test both walls separately, and include the mirror case (a tenant **can** delete its own row) — without
it, a policy set that forbids everything passes. `EverySharedOrTenantEntity_ShipsItsLifecycleSpec` asks for those
facet tests by name. Worked example, tests included: `jigger-jot`'s `JJ-031` and its CKTL-1 slice.

## Phase 6 — Build features, slice by slice

The rhythm, per `docs/WAYS_OF_WORKING.md`:

1. Pick the next epic; Claude writes its user stories into `docs/stories/<epic>.md` (Gherkin).
2. Each slice = one branch + one PR off `develop`, test-first (the failing test precedes the
   code), end-to-end (API + UI + tests), leaving the app working.
3. **The first slice** copies **`src/Host/Features/Notes`** as the reference shape: endpoints through
   `MapTenantFeatureGroup`, a handler on `IRepository<T>`, co-located models, an `ITenantDataContributor`, all
   registered in `AppComposition.cs` (never in `Program.cs`, a register file). The add-a-slice checklist in
   `WAYS_OF_WORKING.md` names each file it touches and the gate that fails when one is skipped. **Delete the Notes
   sample** when that slice lands — the recipe is below.
4. CI gates every PR; once Phase 7 is done, a manual GitHub *Run workflow* with `deploy=staging` deploys
   `develop` to staging (ADR-031).

Scope discipline: before building anything, check the OUT list in `PROJECT_BRIEF.md`.

### Removing the Notes sample (touches none of the platform's tables)

The sample is a removable unit (Arch A6). Its entity, `Note`, is the platform's (it ships in the Core package, so the
sample builds), and nothing else of it is: `git grep DELETE-ME` finds every line to take out.

1. Delete the code: `src/Host/Features/Notes/`, `src/Host/Persistence/Configurations/NoteConfiguration.cs`,
   `tests/Api.Tests/NotesSliceTests.cs`; the `Notes` lines in `src/Host/AppComposition.cs`, the `DbSet<Note>` in
   `src/Host/Persistence/AppDbContext.cs`, the `nameof(Note)` entries in `tests/Api.Tests/App/AppAllowlists.cs` and the
   contributor in `tests/Api.Tests/App/AppTestComposition.cs`, and the Notes folder of the Postman collection with its
   `noteId` variable.
2. Drop the table with a migration: `dotnet ef migrations add RemoveNotesSample --project src/Host` scaffolds the
   `DropTable("Notes")` from the model change and regenerates the snapshot. Say "Data loss on Down: the sample's rows"
   in its summary.
3. Leave `InitialCreate` and `NotesSampleRlsPolicy` in place as history: a migration that already ran anywhere is never
   edited or deleted.

### Taking a platform release

A release is a bump, a port, and maybe a migration — in that order, on one branch:

1. **Bump:** `PlatformPackagesVersion` in `Directory.Packages.props`, and the tag in `.github/workflows/ci.yml`
   (`app-ci.yml@v<version>`), to the same release — both are seed, so they are yours to move. The release
   note says three things: **schema change**, **register change**, **breaking** (ADR-034 point 7).
2. **Port the register:** `dotnet platform port -Platform <a platform checkout>` (a dry run: the register files it
   would merge, the seed files the platform added, changed or deleted, anything listed for removal), then the same with
   `-Apply`. It reads `starter/` at your release's tag and at the target's, merges each register file three ways into
   your copy (`<<<<<<<` markers for a human where both sides changed one spot) — the SDK pin and the tool manifest
   (`dotnet platform` itself) among them — never writes a seed file, and stamps the new version. An app still on `0.0.0-platform` (no tag) passes `-From <the platform commit the template was packed
   from>` once.
3. **Migrate:** if the release note says *schema change*, `dotnet ef migrations add Platform_<version> --project src/Host`
   emits only the platform's delta into your history; a published SQL step is a `migrationBuilder.ApplyPlatformStep(...)`
   line the note names. `PlatformSchemaTests` fails the PR until it is done.
4. Build and run the tests: `OwnershipTests` holds every register file to the new package.

To work on the platform and your app together — a platform fix you want to see in your app before it is released —
set `PLATFORM_SOURCE=<a platform checkout>` in `.env`: the build then references the checkout's projects instead of the
packages (`WAYS_OF_WORKING.md`, "Platform and app together").

## Phase 7 — Stand up staging (an afternoon, once)

Your app ships the template's `Dockerfile` and `render.yaml`. The packages are private, so the image restores them
with the feed's token as the BuildKit secret `nuget`, which reaches no layer (`DEPLOYMENT.md` §0). Check it locally
first: `docker compose --profile app up -d --build` builds it with `.env`'s `PLATFORM_PACKAGES_TOKEN` and runs it against
the compose Postgres and Mailpit.

Follow **`docs/DEPLOYMENT.md`** top to bottom — it's the runbook. The order and the accounts:

1. **§1 Neon** — free serverless Postgres; copy the connection string.
2. **§2 Brevo** — free SMTP (300/day); the four `Email__Smtp__*` values.
3. **§3 Stripe (test mode)** — **required**: the image runs as Production, which fail-closes
   without `Billing__Stripe__SecretKey`; a `sk_test_…` key is fine.
4. **§4 Render** — apply `render.yaml` as a Blueprint (rename its service first: the name is your public URL),
   paste the secrets from 1–3, add the secret file `nuget` (the feed's token, for the image build), deploy.
5. **§5 OAuth (optional)** — register your staging domain with Google/Microsoft, add the client
   id/secret env vars. One provider console entry per domain.
6. **§6 CI deploy** — the **`RENDER_DEPLOY_HOOK_STAGING`** secret on GitHub; a manual *Run workflow* with `deploy=staging` deploys `develop` and runs the version-gated
   smoke (§6, ADR-031).

⚠️ **Verify you are pointed at YOUR database — a green health check does not prove it.** Every app
from this platform shares the same base schema, so if a connection string names *another* app's
database the service starts, `/health/ready` returns `Healthy`, and the outbox poller happily reads
`OutboxMessages` — all against the wrong data. The first downstream app ran this way until it was
caught by inspecting Neon (2026-09-09). Confirm all three, before and after the first deploy:

1. **The endpoint id belongs to this app's project.** Neon endpoint ids (`ep-…`) are unique per
   project and easy to transpose between `.env` files; the region/proxy segment is NOT distinctive,
   since projects in one region share it. Check the id against the project, not just the hostname.
2. **The passwords match that project.** Each Neon project has its own `neondb_owner` password.
   Correcting a host but keeping the old password fails at boot with
   `28P01: password authentication failed` inside `Migrate()` — startup migrations run before the app
   serves, so the whole service stays down.
3. **The schema landed where you expect.** After the first successful deploy, the app's own database
   should hold the platform's tables and a matching `__EFMigrationsHistory`; the other app's history
   should be unchanged. An empty table count on your project means you are still connected elsewhere.

## Phase 8 — Activate production (~10 min, when ready for customers)

Per `DEPLOYMENT.md` §6 / `STATUS.md` §5: create the prod Render service (separate Neon DB;
a **live** Stripe key *if you are charging* — see the gates below, since the startup guard only
demands one when billing is switched on), add GitHub secret
**`RENDER_DEPLOY_HOOK_PROD`**, and create the **`production` environment with a required
reviewer**. Releases become: PR `develop → main`, merge, click approve.

⚠️ Heads-up: the platform itself never activated production (ADR-017 amendment — staging is its terminal
environment), so **your** activation is the first real run of two guards:
the **RLS two-role setup + posture guard** (`DEPLOYMENT.md` §7) and the **live-Stripe-key startup
guard**. Both are tested in CI, but budget a smoke check (sign-in + a checkout round-trip) right
after flipping them on.

### Launch day — the two pre-launch gates (GATES, ADR-027)

Production being *reachable* and the app being *open* are separate decisions, and the platform ships
both switches closed so you can run a private, free deployment first — friends testing, nothing on
sale, no stranger signing up off the URL. Neither can be flipped from inside the app; both are read
at startup, so changing either needs a restart.

| Switch | Shipped default | What "off" means |
|---|---|---|
| `Billing__Enabled` | off | `/api/billing` and the provider webhook **do not exist** (404), no billing link in the header, every tenant is on Free, and nothing anywhere offers an upgrade. No Stripe account needed to deploy. |
| `Signup__AllowedEmails__0…` / `Signup__AllowedDomains__0…` | empty (= open) | Non-empty restricts **account creation** to those addresses, plus anyone invited into a household whose **owner** is listed. Existing accounts always sign in. |

Publishing is therefore three steps, in whichever order suits you:

1. Set `Billing__Enabled=true` — **or leave it off** to publish free. Only with it on does the
   live-Stripe-key startup guard apply.
2. Clear the `Signup__Allowed*` entries to open signup to anyone.
3. Restart the service, then check the startup log line beginning `Pre-launch gates:` — it states the
   posture it actually booted with, which is the cheapest way to catch a misspelled key.

Run **QA-GATE-01..06** (`QA_TEST_PLAN.md` §14c) before and after the flip.

## Phase 9 — Native apps (optional, and deliberately last)

The Windows/Android/iOS/macOS shells already build in CI and carry your brand from Phase 3. When
the web product is stable and you want installable apps, this phase is **yours, not the
platform's** — signing identity (keystore, certs, store listings) is inherently per-app, so the
platform deliberately ships no release workflow (ADR-024). What it ships instead is this
checklist, plus the full scoping notes in `docs/stories/native.md` Wave 4 (read the matching
section before each step — the traps below were found the hard way).

**First, verify:** run the native QA pass (`docs/QA_TEST_PLAN.md` §12–13, guides in the QA PDFs).
Don't sign an app you haven't seen working.

**Sideloading a build at a host** (before any of that, and how you will test on a real device):
`dotnet platform publish-native -ApiBaseUrl https://<your-host>` — see `docs/DEPLOYMENT.md` §9,
which names the four ways an Android sideload fails **silently** (the unsigned twin APK, a v1-only
signature, an upgrade over a running app, a build signed with a different key than the installed one). Use the
command; every one of those is a wrong-file or wrong-state trap the phone will not explain.

**Then, the first-native-release checklist:**

1. **Android** (native.md → NATIVE-8): generate the release keystore once with `keytool` — it IS
   your app's identity; losing it is unrecoverable. Base64 → repo secrets + an offline backup,
   never git (keep the file outside the repo; `.gitignore` covers `*.jks`/`*.keystore`/`*.p12`/`*.pfx` as
   the net under that). Build with `AndroidKeyStore=true` + the signing props, the two passwords as
   `env:NAME` — never a `-p:` literal (`dotnet platform publish-native … -KeyStore … -KeyAlias …` does this),
   `AndroidPackageFormat=aab`, in a **tag-triggered** release workflow (`ubuntu-latest`); assert
   the artifact with `apksigner verify --verbose` (v2/v3 — `jarsigner` passes a v1-only file that Android 11+
   refuses to install). A store-signed build will not install over a debug-signed sideload: testers
   uninstall the sideload first (DEPLOYMENT §9). Enroll in **Play App Signing** — your keystore becomes
   the upload key (Google holds the app-signing key; effectively required for new Play apps).
2. **Windows** (native.md → NATIVE-9): the app runs unpackaged today (`WindowsPackageType=None`);
   add a packaged Release flavor (MSIX + `Package.appxmanifest`). Don't buy a code-signing cert —
   real OV certs need an HSM since 2023; **let the Microsoft Store sign the package** (~$19
   one-time account), or self-sign for sideloading only. ⚠️ **Trap: packaged MSIX runs
   containerized** — manually boot the PACKAGED build and re-verify sign-in +
   Preferences/SecureStorage/file paths before shipping; every QA pass so far ran unpackaged
   (same failure class as the Catalyst keychain surprise, platform PR #125).
3. **iOS/macOS** (native.md → NATIVE-10): needs the Apple Developer account ($99/**yr**,
   recurring — certs lapse if it stops) + certs/provisioning profiles as base64 secrets, built on
   a macOS runner (iOS `.ipa`, macCatalyst `.pkg`). ⚠️ **Trap: re-verify SecureStorage under the
   real signing identity** — properly-provisioned builds can claim `keychain-access-groups`, at
   which point the platform's `DebugFileSessionStore` fallback (Catalyst Debug, PR #125) should be
   re-tested and considered for retirement in your app.
4. **Store submission** (native.md → NATIVE-11): Play Console / App Store Connect / MS Store —
   accounts: Google $25 once, Apple $99/yr, Microsoft ~$19 once. Wire the upload step guarded/off
   by default, or document your manual path.
5. **Every native release thereafter:** gate on the §13c checklist in `docs/QA_TEST_PLAN.md`
   (smoke cases per platform + one feature spot-check; full §12–13b regression when native glue or
   the toolchain changed).

Until you need installable apps, web-first costs you nothing — golden rule 5.

---

## The other direction: what NOT to redo

A reminder that saves the most time of all — **none of this is your work anymore**: sign-in flows,
MFA, tenant isolation, invitations, roles, Stripe billing/quotas/trials/dunning, background jobs,
notifications, file storage, GDPR export/erasure, audit logging, the admin console, localization
plumbing, the CI pipeline, or the deploy story. If a feature request looks like one of those,
check `docs/ROADMAP.md` / `docs/PLATFORM_BACKLOG.md` first — it's probably built, config-gated,
or consciously deferred with a design sketch waiting. And when the platform lacks a seam you need, it goes upstream as
a new seam (ADR-034 point 4), never as an edit to a package's code or a register file.

| Stuck on… | Read |
|---|---|
| What the platform includes | `docs/OVERVIEW.md`, and the capabilities card on the signed-in Home |
| A decision you're about to reverse | `docs/DECISIONS.md` (add an ADR, don't silently change) |
| Slice/PR/commit mechanics | `docs/WAYS_OF_WORKING.md` |
| Brand completeness | `docs/REBRANDING.md` (verify section) |
| Deploy details / env vars | `docs/DEPLOYMENT.md` (reference table at the end) |
| Android emulator sign-in | `docs/MOBILE_TESTING.md` |
