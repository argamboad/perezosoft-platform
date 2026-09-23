# FOUNDATION_RULES — v4 candidate block (Phases 1–4; Phase 5 consolidates into `FOUNDATION_RULES_v3.md`)

> **Status: CANDIDATE.** Binding rules remain `docs/audits/v3-2026-07/FOUNDATION_RULES_v2.md` (R1–R35 via v1.0,
> R36–R76 + R80) until Phase 5 emits the v3.0 final. Numbers here are **provisional**: they continue after R80 and
> skip the reserved R77–R79 (their fate is conflict C8). Each entry: **[machine]** / **[review]** · text ·
> enforcement mechanism · findings subsumed. Later phases APPEND (R123+) or FLAG for revision; nothing here
> overturns a prior rule (disagreements → `RULE_CONFLICTS.md`).
>
> SHA under audit: `8c8ed4a` (all phases).

## Auth / session (Phase 1)
- **R81 [machine]** — A rotated-out refresh token is honoured at most **once** inside the reuse grace; a second
  presentation, or any presentation after the grace, is `Reuse` and revokes the family; the grace outcome is
  logged at Warning with a per-user count. — `tests/Api.Tests/Integration/RefreshReplayTests.cs` (third presentation
  inside the window ⇒ 401 + revoke-all) + `TokenServiceTests` (grace-used token ⇒ `Reuse`). — AUTH-1, AUTH-13.
- **R82 [machine]** — Every list-typed allow/deny setting (`string[]` on a `*Settings` class) is normalized at
  bind (trim, case-fold where matching is case-insensitive, blanks dropped, leading `@` stripped from domains) and
  exposed only in normalized form. — Reflective theory in `ConfigPostureTests` over every list-typed settings
  property with `" X "`, `""`, `"@d"`. — AUTH-7.
- **R83 [machine]** — Every `new ErrorResponse("<code>"` literal and every `?error=<code>` redirect literal
  reachable from a controller action appears in that request's Postman description. — Extend
  `PostmanParityTests` from path parity to code parity (allowlist redirect-only flows by name). — AUTH-9, TR-21
  (with R119).
- **R84 [review→machine]** — A client session-continuity change ships a fake-clock test that crosses the access
  token's `exp` under an unreachable server and asserts (i) no `SignedOut` until the server rejects, (ii) exactly
  one `SignedOut` when it does, (iii) no `/login` bounce on a later renewal. — Test-name manifest check in
  `SessionKeepAliveTests` (R31's machine-half pattern). — AUTH-5.
- **R85 [review]** — A deliberate exception to a numbered R-rule is written into `FOUNDATION_RULES` (rule text +
  the pinning test's name) in the same PR as the ADR that argues it. — PR-template checkbox; the audit's truth-up
  diffs ADR "Accepted/Rejected" blocks against the rules file. — AUTH-11 (R53/`SignupSettings`), UX-18 (R72).

## Billing / config gates (Phase 1)
- **R86 [machine]** — A config-gated HTTP surface is proven absent by the **route table**, not by an attribute
  list: with the gate bound from empty config, enumerate `EndpointDataSource` and assert no endpoint's
  `RoutePattern.RawText` matches the gated prefix and no endpoint's handler type lives in the gated namespace —
  applied to BILLING, PUBAPI and HOOKS alike; ADR-021's admin billing writes are gated or explicitly exempted by
  name with an ADR reference. — Extend `ArchitectureTests.BillingControllers_AreAllGated` (or new
  `GatedSurfaceTests`; the harness already boots gate-off). — BILL-2, BILL-3.
- **R87 [machine]** — A config gate has exactly one read site: the bound `*Settings` object or a single shared
  key constant; no second `GetValue("<Section>:Enabled")` in `src/**`. — `DocAndConfigSyncTests` regex over `src/`
  flags raw reads of a key whose section owns a `*Settings` class. — BILL-4.
- **R88 [review→machine]** — Catalog numbers (seat/usage caps) are *read* from `PlanCatalog` wherever a test or
  doc asserts them; E2E projects that cannot reference Core pin them behind one named constant with a
  cross-reference comment; prose QA cases express seat counts relative to the cap ("cap − 1"), never as literals.
  — `DocAndConfigSyncTests` grep of `docs/**/*.md` + `tests/E2E.Tests/**` for `Free ?\(\d\)|\d of \d used|cap \d`
  against the catalog value. — BILL-6.

## Jobs / email / webhooks / observability (Phase 1)
- **R89 [machine]** — Persisted diagnostics that any `*Response.From` maps to a client (`WebhookDelivery.Error`,
  future `*.Error/LastError`) hold an enumerated reason code, never `Exception.Message`. This is the machine half
  of v2 **R16**. — `EnforcementGateTests` string-scan `Error\s*=.*\.Message` in `src/**` against entity types
  referenced by a `Response.From`; `WebhookDeliveryLogTests` asserts the enumerated set. — JOBS-1.
- **R90 [machine]** — Outbox payload lifecycle: `Payload` is scrubbed when a row leaves `Pending`
  (`Sent`/`Dead`), and a registered `IScheduledJob` purges terminal rows older than `Outbox:RetentionDays`
  (documented in `.env.example`). — `OutboxProcessorTests` (payload empty after success/dead-letter) + scheduled-job
  test + `ScheduledJobs_Include("outbox-retention")`. — JOBS-2 (retention half).
- **R91 [machine]** — Every outbox enqueue carrying per-user or per-tenant content stamps `TenantId`, and tenant
  dissolve wipes that tenant's pending rows (an `OutboxDataContributor`); the R43 tenant-axis canary extends to
  nullable-`TenantId` tables that carry tenant content. — After `DissolveAsync`, `OutboxMessages WHERE TenantId = t`
  is empty; arch scan that `EnqueueAsync(` callers in `Email/` and `Services/` pass a tenant id. — JOBS-2 (erasure
  half).
- **R92 [machine]** — Enqueue-time validation is at least as strict as dispatch-time parsing: every payload field
  a handler parses (`MediaType` → `ContentType.Parse`, `To` → `MailboxAddress.Parse`, `FileName` → MIME parameter)
  is validated with the same grammar before the row is written; file names are reduced to a safe basename with no
  control characters and a length cap. — `EmailAttachmentTests` + `OutboxEmailTests` malformed-value theories
  asserting zero rows. — JOBS-3, JOBS-4.
- **R93 [machine]** — Log state carries identifiers only: message-template placeholders `{Email}`, `{To}`,
  `{Token}`, `{Code}`, `{Otp}`, `{Secret}`, `{Password}`, `{Body}` are banned in `src/**` (comment allowlist).
  — New `EnforcementGateTests.LogTemplates_NeverCarryPii`. — OBS-1.
- **R94 [machine]** — An `HttpClient` that targets user-supplied URLs sets `AllowAutoRedirect = false` (or
  re-guards each hop through `IOutboundUrlGuard`). Transport half of R3/R76. — `ArchitectureTests` scan of the
  `IWebhookSender` registration + a 302 stub test in `WebhookDeliveryTests`. — JOBS-9.
- **R95 [machine]** — The config-catalog gate also captures flat indexer reads (`configuration["ALL_CAPS_NAME"]`)
  and the SDK-owned `OTEL_*` variables the code consults; the app sets the exporter protocol from the same value it
  reads. — Regex extension in `DocAndConfigSyncTests`; `TelemetryLogsExportTests` asserts the resolved
  `OtlpExporterOptions.Protocol`. — OBS-3.
- **R96 [machine]** — Free-text diagnostics written to a `HasMaxLength` column are truncated at the write site.
  — Scan for assignments from `.Message` into properties configured with `HasMaxLength` without `Truncate(`.
  — JOBS-5.

## Deploy / CI / supply-chain (Phase 1)
- **R97 [machine]** — The change classifier is safe-by-default: every repo path an arch test or CI gate reads
  (`.forgejo/**`, `.github/workflows/**`, `.github/scripts/**`, `.github/forbidden-licenses.json`,
  `.dockerignore`, `docs/DEPLOYMENT.md`) is `code`; a path counts as docs only when it matches an explicit
  allowlist. — `EnforcementGateTests.MarkdownAnywhere_IsNeverCodeOrNative` positives + a test extracting every
  `Read("…")`/`File.ReadAllText(...)` literal in `tests/Api.Tests` and asserting `Code(path)`. — DEP-15.
- **R98 [machine]** — On a forge that ignores `permissions:`, the job token is treated as **write**: every
  `actions/checkout` sets `persist-credentials: false` unless allowlisted with a reason; no step passes the token
  to third-party code; `develop`/`main` carry branch protection on that forge (a `changes`-job step reads
  `/branch_protections` and fails when unlisted); the branch-protection script exits non-zero on any failure and
  covers both forges; the already-green deploy refuses a SHA with any red `native-smoke-*` task and takes the newest
  task per job. Forge-side equivalent of R63. — `ForgejoCiParityTests` (checkout scan, deploy.yml assertions,
  script `exit 1`) + a branch-protection step in `.forgejo/workflows/ci.yml`. — DEP-13, DEP-14, DEP-19, DEP-27.
- **R99 [machine]** — Every container image reference — Testcontainers builders, compose, workflow
  `services:`/`docker run`, Dockerfile `FROM` — carries at least a `major.minor` tag or a digest; the gate scans
  all four surfaces on both forges. — `EnforcementGateTests.EveryContainerImage_IsPinned_NotFloating` widened.
  — DEP-18.
- **R100 [review]** — An operator procedure in `DEPLOYMENT.md` is executable by the scripts it names (a
  deploy/rollback sentence is re-read against `push-to-github.sh` and the dispatch `if:` whenever either changes);
  numbers in story files (knob counts, runner capacities, token scopes) are stated once, in the runbook.
  — PR-template checkbox + `ForgejoKnobs_AreTheDocumentedFour` also reading the story file. — DEP-16, DEP-25.
- **R101 [review]** — A deploy push to a mirror must not silently buy paid runner minutes: the mirror's Apple
  legs and scheduled smokes sit behind a `vars.` knob while the mirror is a mirror, and every "repo is public/free"
  cost claim in a workflow comment is re-verified when visibility changes. — Review at deploy-config PRs; the knob
  is listable in the parity test. — DEP-17.

## Native (Phase 1)
- **R102 [machine]** — Both hosts attach `Authorization: Bearer` only to requests whose target origin equals
  the API base origin; third-party absolute URLs (signed downloads) go through a plain client. — Shared
  `BearerScopedHandler` in `src/Shared.Ui` covered by `tests/Ui.Tests` (absolute foreign URL ⇒ no header; relative
  and same-origin ⇒ header) + `ArchitectureTests` banning `AuthenticationHeaderValue("Bearer"` outside it.
  — NAT-12.
- **R103 [machine]** — CI exercises an Android **Release** build (dispatch input + the Monday schedule, like the
  smokes): `-c Release -p:ApiBaseUrl=https://release.invalid -p:AndroidPackageFormat=apk`, asserting `apksigner
  verify` reports v2/v3 = true, `RequireApiBaseUrlInRelease` fires without the property (and rejects a non-`https`
  or path-bearing base), and the Release APK carries the HTTPS-only network config; `publish-native.ps1` itself
  fails unless v2/v3 are verified; a Release build that resolves no keystore on any host errors. This is the missing
  machine half of **R67**. — New job in both workflow copies (R80 parity) + `EnforcementGateTests` asserting its
  presence. — NAT-13, NAT-14, NAT-18.
- **R104 [machine]** — Signing material is never in git: `.gitignore` lists `*.jks`, `*.keystore`, `*.p12`,
  `*.pfx`, `*.mobileprovision`, `*.cer`, `out/`; passwords come from env, never `-p:` literals in docs or scripts.
  — `EnforcementGateTests` asserts the gitignore patterns; a doc-grep rejects `-p:AndroidSigning(Key|Store)Pass=`
  outside a "do not" sentence. — NAT-15.
- **R105 [machine]** — Android shell posture gate: manifest has `allowBackup="false"` (or a backup-rules file),
  `networkSecurityConfig` set, no `usesCleartextTraffic="true"`, no `debuggable`; the Release network config is
  `cleartextTrafficPermitted="false"`. — Regex assertions in `NativeChromeGateTests` (rename `NativeShellGateTests`).
  — NAT-17; hardens R67's cleartext half.
- **R106 [machine]** — The `native` classifier regex is asserted positively per R60 file class (`src/`,
  `tests/E2E.Tests/`, `tests/native-smoke-android/`, `Directory.Build.props`, `Directory.Packages.props`,
  `global.json`, both `ci.yml`, and any script a native CI leg runs). — Extend
  `EnforcementGateTests.MarkdownAnywhere_IsNeverCodeOrNative` with one `Assert.True(Native(...))` per class.
  — NAT-16, S0-G6.

## Client (Phase 1)
- **R107 [machine]** — The client's refresh call carries an explicit timeout `T` and retry pause `D` with
  `T + D < RefreshToken:ReuseGraceSeconds` (server default), so a lost-response retry always lands inside the grace.
  — `ConfigPostureTests` cross-reads `AuthService.RefreshTimeout`/`RenewRetryDelay` and the `appsettings.json`
  default; `SessionKeepAliveTests` proves the timeout fires. — UX-6.
- **R108 [machine]** — "The server said no" means 401, or 400/403 whose body parses to an `ErrorResponse` with
  a known code; every other status/exception is `Unreachable` and keeps the stored credential; `Unreachable`
  retries back off exponentially with a cap. — `SessionKeepAliveTests` theory over status × body + a retry-count
  test. — UX-7, UX-12.
- **R109 [machine]** — E2E full navigations go through `BlazorBoot` (grep gate: no `page.GotoAsync(`/
  `ReloadAsync(` outside `BlazorBoot.cs`/`NativeSmokeTests.cs`); a boot retry reloads the LANDED url, never
  re-issues the original navigation; only network verdicts (`fetch failed`, `still loading`) retry — a `banner`
  with no failed `_framework` fetch fails immediately with the console; retries are budgeted per shard.
  — `EnforcementGateTests` grep + the Slowest-journeys step threshold (both copies, R80). — UX-8, UX-9, DEP-21.
- **R110 [review]** — A `wwwroot/js` file introduced as a security control ships a behaviour test (E2E with the
  relevant browser feature enabled); a substring test is a presence gate and is named as such. — UX-10.
- **R111 [machine]** — A `SignedOut` transition while on a protected route navigates to `/login` from ONE place
  (`MainLayout.OnSignedOut`); the layout gates on session-held, not token expiry. — bUnit `MainLayout` test.
  — UX-11, AUTH-5.
- **R112 [machine]** — Server auth error codes map to copy in one table (`AuthErrorCopy`) consumed by every
  surface (query string, web status+body, native body); no inline code-literal switches in `.razor`.
  — `SignupRefusedCopyTests` + arch grep for `"signup_not_allowed" =>` outside `AuthErrorCopy`. — UX-14.
- **R113 [machine]** — `--list-tests`-driven sharding excludes `[Explicit]` fixtures, and the "suite size" in
  `docs/stories/e2e.md` is asserted equal to the non-explicit `[Test]` count (R75 extension). — UX-15.

## Docs / course / rule hygiene (Phase 1)
- **R114 [machine]** — The committed `docs/tutorial/COVERAGE.md` equals `gen_coverage.py`'s output and reports 0
  unmapped. Promotes localci.md's unbuilt R79. — CI step (`qa-artifacts`-style, never code-gated) running the
  generator + `git diff --exit-code`, or an `EnforcementGateTests` fact shelling out to it. — TR-19.
- **R115 [machine]** — Lesson code quotes are current and no bucketed file is an orphan: fenced-code lines in
  `docs/tutorial/lessons/*.md` that quote an identifier from a file the lesson owns per COVERAGE appear verbatim in
  the tree (unless tagged `<!-- historical -->`), and every bucketed file's basename appears in its lesson's prose.
  — Extend `docs/tutorial/gen_coverage.py` (quote sweep + orphan check). — TR-12, TR-16, TR-17, TR-18, TR-22,
  TR-23, TR-29.
- **R116 [machine]** — Rule-id hygiene: every `R\d{2,3}` cited in `tests/**/*.cs` (outside a `-cand` token) is
  defined as a final rule; the binding file's header range covers every `**R\d+ [` present; `-cand` ids never appear
  in test comments. — `EnforcementGateTests` fact. — TR-15.
- **R117 [review]** — Deploy-trigger wording names the forge: any doc that describes *when* a deploy happens (QA
  §1, `docs/postman/README.md`, README, lessons 8.3/1.6, CLAUDE.md Postman block) says "Forgejo dispatch
  `deploy=…`"; a grep gate on `auto-deploys` outside DEPLOYMENT §10 is the cheap machine half. — TR-13, TR-14.
- **R118 [machine]** — The doc-map gate covers `docs/**/*.md` minus an explicit allowlist (`audits/**`,
  `tutorial/lessons/**`, `stories/_EXAMPLE_epic.md`) and requires a row for each of `docs/tutorial/`,
  `docs/qa-runs/`; CLAUDE.md's "Read before you act" carries the course-reconcile rule. — Widen
  `ClaudeMdDocMap_ListsEveryTopLevelDoc`. — TR-24.
- **R119 [machine]** — Postman gate/refusal description floor: requests routed to a gated controller mention the
  gate key (`Billing:Enabled`); requests whose action can throw `SignupNotAllowedException` mention
  `signup_not_allowed`. — Extend `PostmanParityTests`. — TR-21 (with R83).
- **R120 [machine]** — Compiled-in limits are listed: every `public const`/`public static readonly`
  numeric/`TimeSpan` field in `src/Core` + `src/Shared.Ui/Auth` whose name ends in
  `Bytes|Lead|Delay|Delays|Wait|Limit|Max*` appears by name in `.env.example`'s "Not configurable" block.
  — Extend `DocAndConfigSyncTests`. — TR-25.
- **R121 [machine]** — Diagram currency: `docs/ARCHITECTURE.md` names every public class in `src/Api/Services`,
  `src/Api/Configuration`, `src/Api/Observability` (or an allowlist); `docs/FLOWS.md` names every distinct
  `ErrorResponse("<code>")` literal reachable from `AuthController`. — Extend `DocAndConfigSyncTests`. — TR-20.

## Gate-scope closures from Step 0 (Phase 1)
- **R122 [machine]** — The empty-config posture gate is reflective: every `*Settings` class exposing `bool
  Enabled` (and every non-Settings gate keyed on a nullable endpoint/URL) is enumerated by reflection and asserted
  closed under empty configuration, with the enumerated R53 exceptions (`SignupSettings`: closed state is
  *non-empty*; posture pinned as emptiness) listed by name in the test. — `ConfigPostureTests` reflective theory.
  — S0-G8, AUTH-11.
- *(S0-G7, S0-G9, S0-G10 are recorded as enforcement-backlog items under R68, R61 and R36 respectively — Phase 2
  lists them; no new rule.)*
