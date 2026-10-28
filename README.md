# Perezosoft Platform

The Perezosoft foundation for building multi-tenant SaaS apps on a fixed stack
(ASP.NET Core API + Blazor WASM + shared RCL + PostgreSQL/EF Core + custom JWT auth, with
MAUI Blazor Hybrid shells for mobile + Win/macOS desktop). It lets a new project skip
re-deciding the stack and jump straight to discussing **what the app does**.

## What's in here

```
_PLATFORM_PRIMER.md     ← paste into a new Claude chat to start conceptualizing a project
CLAUDE.md               ← Claude Code operating manual skeleton (constant rules pre-filled)
docs/                   ← doc skeletons (constant parts filled, app-specific = TODO)
  PROJECT_BRIEF.md
  FEATURES.md
  DATA_MODEL.md
  TECH_STACK.md         ← almost entirely reusable; only re-verify versions
  DECISIONS.md          ← pre-seeded with constant ADRs (C1–C15); add app ADRs from 001
  stories/
src/ , tests/           ← the complete platform (auth, tenancy, billing, jobs, …) + Notes sample slice
```

## How to use it

**Step 1 — Conceptualize (in Claude chat).**
Start a new chat and paste `_PLATFORM_PRIMER.md`. Describe your app. Claude runs the
conceptualization session — clarifying questions, recommendations, ADRs, scope discipline — and
fills in the doc skeletons. The stack is already decided, so the conversation is about the app.

**Step 2 — Create the app (when the thinking layer is done).**
Once concept + features + data model + decisions are settled, write the app from the template —
`dotnet new install Perezosoft.Platform.Templates::<version>`, then `dotnet new perezosoft-app -n <AppName>`
(ADR-034: an app is not a copy of this repo; the platform comes as the `Perezosoft.Platform.*` packages). Make it a
repo and add the conceptualization docs to its `docs/`. The feed token, the CI access and the rest are
`docs/NEW_APP_GUIDE.md` Phases 0 and 2.

**Step 3 — Brand + build (in Claude Code).**
Point Claude Code at the app. The platform (auth, tenancy, billing, jobs, notifications, GDPR,
admin, deploy pipeline, …) is already built — the first session brands the app and writes its own
docs; after that it's your feature slices, with per-epic user stories written into
`docs/stories/` as they're built.

**The expected first Claude Code prompt** (copy, fill the brackets, attach the logo):

> We're starting a new app on the Perezosoft platform, from the conceptualization docs already in `docs/`.
> The app is **[AppName]**; the tenant's app-facing label is **[Team / Workspace / Household / …]**.
> Here is the logo: **[file]**.
>
> 1. **Brand it end to end per the platform's `docs/REBRANDING.md`** — the brand assets (derive the
>    icons/favicon/OG image from this logo), the **colour palette derived from the logo** (the `:root`
>    tokens in `src/Shared.Ui/wwwroot/css/app.css`, the Android system bars), the **email brand** in
>    `AppComposition.ConfigurePlatform` (`options.Brand`: tagline, colours, PNG logo — don't skip the
>    mails), the product copy in every `.resx`, and the tenant label in the pages (the API keeps
>    `household`).
> 2. **Write the app's own docs** — `CLAUDE.md` (golden rules, conventions) and the `docs/` from the
>    conceptualization docs.
> 3. **Verify** per the checklist: `git grep -i appbrand` returns nothing, the app builds and
>    runs with the new brand, and a test OTP email arrives with the new logo/colours/name.
>
> Then propose the first feature epic from `docs/FEATURES.md` (the `Notes` sample slice gets
> deleted when the first real feature lands).

## Local ports

The three apps run side by side on one machine, each on its own block, so a stack never answers for another. This
table is the same in all three repos. A repo states its block ONCE, in `local-ports.props`: code reads it through the
generated `LocalPorts` class, `pwsh tools/ports.ps1 -Apply` (`dotnet platform ports -Apply`) writes it into the files that cannot read MSBuild (compose
defaults, launch profiles, appsettings, `.env.example`, the Postman environment, this row), and `LocalPortsTests` fails
on any file that disagrees, on a port number in code, and if two apps share a port.
API http is the port the Android emulator uses; the app container is `docker compose --profile app up`.

| App | Postgres | Mailpit SMTP | Mailpit UI | API https | API http | Web https | Web http | app container |
|---|---|---|---|---|---|---|---|---|
| **perezosoft-platform** (this repo) | 5433 | 1025 | 8025 | 7160 | 5238 | 7008 | 5169 | 8080 |
| y-el-vuelto | 5434 | 1026 | 8026 | 7260 | 5338 | 7108 | 5269 | 8180 |
| jigger-jot | 5435 | 1027 | 8027 | 7360 | 5438 | 7208 | 5369 | 8280 |

## Reading order

For a newcomer, motivation first, mechanics last:

1. [`docs/OVERVIEW.md`](docs/OVERVIEW.md) — what the platform is and why it saves months; no codebase knowledge assumed.
2. [`docs/PROJECT_BRIEF.md`](docs/PROJECT_BRIEF.md) — what *this* app is for, and (as important) the OUT list.
3. [`docs/NEW_APP_GUIDE.md`](docs/NEW_APP_GUIDE.md) — the end-to-end path from idea to production, phase by phase.
4. [`docs/DECISIONS.md`](docs/DECISIONS.md) — the "why" behind every settled choice; read before disagreeing with any of them.
5. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — the shape in diagrams: projects, seams, subsystems.
6. [`docs/DATA_MODEL.md`](docs/DATA_MODEL.md) — entities, ER diagrams, invariants, lifecycles.
7. [`docs/FLOWS.md`](docs/FLOWS.md) — the call stacks that matter, traced through the code.
8. [`docs/WAYS_OF_WORKING.md`](docs/WAYS_OF_WORKING.md) — how work actually lands: slices, stories, TDD, PR conventions.
9. [`CONTRIBUTING.md`](CONTRIBUTING.md) — the frozen quality bar and the machine-enforced rules behind it.

## What's constant vs. per-project

- **Constant (don't re-decide):** the stack, the clean-API-boundary + RCL architecture,
  multi-tenancy (Tenant ≠ User, tenant-scoped data, per-user preferences), the doc/ADR method,
  the "latest stable, never previews" version policy, and MAUI Blazor Hybrid for non-web clients.
- **Per-project (designed fresh):** the concept, features, data model entities, domain-specific
  derived rules, the tenant's real-world label, scope, seed data, and hosting.

## Important caution

**Do not copy app-specific data-model decisions between projects.** Things like single-table
inheritance, snapshot-vs-reference semantics, or any particular derived rule are designed for one
app's domain and can quietly mislead another. Only the items listed as "constant" carry forward.
Re-verify tool/library versions at the start of every project — this platform ages.
