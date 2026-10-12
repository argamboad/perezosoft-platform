# v4 Delta Audit — Phase 2: Tool reconciliation

> Reads Phase 1 (`AUDIT_REPORT.md`) + the candidate `FOUNDATION_RULES.md`. Raw tool output distilled under
> `tooling/`. Date 2026-09-23.

## SUMMARY
- **Commit SHA: `8c8ed4a746c57a6df8bac5f31195f011dc0758c4`** — identical to Phase 1 (verified; the Phase 1 docs
  commit `4bf370a` sits on top and does not change the code SHA under analysis).
- **Build:** `dotnet build Perezosoft.slnx -c Release` (warnings-as-errors) — **0 warnings**; the only errors are
  the v3 R67 guard (`RequireApiBaseUrlInRelease`) refusing the Android/Windows MAUI Release legs without
  `-p:ApiBaseUrl` — the gate firing as designed. Re-run with `-p:ApiBaseUrl=https://audit.invalid` for
  `net10.0-android`: 0 warnings, 0 errors (3 min 06 s).
- **Tests:** **909/909 green** — Api 767 (incl. `RlsMigrationGate` ×2, MinIO/Testcontainers ×5, `EnforcementGate`
  ×12, `ForgejoCiParity` ×11, `ConfigPosture` ×7, `PostmanParity`, `Dissolve*` ×15 — all executed against Docker
  29.8), Core 62, Ui 80. E2E 0/34 locally (`ERR_CONNECTION_REFUSED`: no live host in this run) — **verified
  instead by the Forgejo CI status of this SHA: `success`, incl. `e2e (1)(2)(3)`, `build-test`, `docker-build`,
  `license-scan`, `native-build` Windows + Android** (`tooling/ci-status.txt`). Apple legs skipped (Mac asleep /
  `CI_MACOS_RUNNER` unset) — a green develop run is not a green Apple build.
- **Coverage (cobertura):** Api **79.7 %** line / 67.7 % branch (v3: 75.6 %); Core 94.1 %; Infrastructure 98.9 %
  (migrations inflate); **Shared.Ui RCL 37.0 %** line via `Ui.Tests` (v3: **1.2 %**, E2E-only — the R70 bUnit
  chassis is real and used by every delta component).
- **Supply chain:** **0 vulnerable** packages (all 10 projects, transitive); **deprecated: xunit 2.9.3 "Legacy"
  (xunit.v3 exists) in the three xunit projects + `Microsoft.ApplicationInsights 2.22.0` "Other" transitive in
  E2E.Tests** — both hygiene, not security; **licenses: 0 forbidden** on the Api and Web graphs (Api graph: 102
  MIT · 30 Microsoft · 24 Apache-2.0 · 5 PostgreSQL); 46 direct pins (CPM), 68 direct+transitive in Api; 9
  lockfiles, `--locked-mode` restore in CI for Api + Web (Maui excluded by design — DEP-20). Outdated: the .NET
  10.0.11 → 10.0.12 package line (deliberately held to the `aspnet:10.0.11` runtime image, R61), OpenTelemetry
  1.16 → 1.19, MAUI 10.0.20 → 10.0.110, NUnit/xunit/Playwright minors.
- **Complexity proxy** (decision points per file, `tooling/complexity-proxy.txt`): `Shared.Ui/Auth/AuthService.cs`
  **131 / 882 LOC** (was the delta's most-touched file: +284 lines for keep-alive), then `Household.razor` 70,
  `Login.razor` 64, `AdminConsole.razor` 54, `AdminController.cs` 52, `AuthController.cs` 47. Duplicate 6-line
  windows across files: **0**.
- **Reconciliation:** 99 Phase 1 findings → **97 Confirmed/consistent, 0 Contradicted, 2 refined** (DEP-24
  downgraded to Info: `--locked-mode` is the real gate and held; OBS-2 stays Low/Likely — SDK behaviour not
  executed). **Tool-only findings: TOOL-5..TOOL-9** (5). No Phase 1 rule removed. Enforcement backlog: 31 items.

---

## Tool run detail
| Tool | Result | File |
|------|--------|------|
| `dotnet build -c Release` (WAE) | 0 warnings; R67 guard errors only (expected); Android Release with API base: clean | `tooling/build.txt` |
| `dotnet test -c Release --collect:"XPlat Code Coverage"` | Core 62 ✓ · Ui 80 ✓ · Api 767 ✓ · E2E 0/34 (env) | `tooling/test-run-summary.txt` |
| Forgejo commit status (API, read-only) | `success` — 12 jobs success, 7 skipped by design | `tooling/ci-status.txt` |
| Coverage | per-assembly line/branch | `tooling/coverage-summary.txt` |
| `dotnet list package --vulnerable --include-transitive` | 0 | `tooling/vulnerable-packages.txt` |
| `dotnet list package --deprecated --include-transitive` | xunit 2.x Legacy ×3 projects; ApplicationInsights 2.22.0 (E2E, transitive) | `tooling/deprecated-packages.txt` |
| `dotnet list package --outdated` | .NET 10.0.12 line, OTel 1.19, MAUI 10.0.110, test minors | `tooling/outdated-packages.txt` |
| `dotnet-project-licenses` (CI step reproduced, Api + Web graphs) | exit 0 both; no GPL/AGPL/LGPL/SSPL | `tooling/license-scan.txt` |
| Complexity/duplication proxy (python, `src/**`) | top-25 by decision points; 0 cross-file 6-line duplicates | `tooling/complexity-proxy.txt` |

## Reconciliation of Phase 1 findings

### Confirmed (tools corroborate; severity unchanged unless noted)
- **Every gate the Step 0 table names exists and passed in the run** (`api.trx`): T1 `IntegrationFactory_DoesNotBackfillRlsPolicies` + `RlsMigrationGate*`, T2 `RouteGroupPrefixes_AreUnique`, T3 tenant-axis canary, T27 `ConfigPostureTests` (7), T29 `SdkPin_HasOneSource…`, T30/R80 `ForgejoCiParityTests` (11), T55 `PostmanParityTests`, T60 enforcement close-out (12 `EnforcementGateTests`). Step 0's "Held" verdicts are therefore tool-backed, not just read.
- **JOBS-2 (High)** — confirmed by structure: no test in the 909 references `OutboxStatus.Sent` cleanup, no scheduled-job test beyond `ExpiredTokenCleanupJob`/`SubscriptionLapseSweepJob`; `OutboxEmailTests` never asserts `TenantId`. Coverage of `OutboxProcessor.cs` is high (the bookkeeping is tested) — the gap is a *missing feature*, not untested code. Severity holds.
- **JOBS-3** — the auditor's runtime probe against the pinned MimeKit 4.17.0 is reproducible (`ContentType.Parse("pdf")` throws); `EmailAttachmentTests` (Core, 62 total) has no malformed-media-type theory. Confirmed Certain.
- **JOBS-1** — `WebhookDeliveryLogTests` passes with `ThrowingHandler` and asserts a non-null `Error`, i.e. the suite *pins* the raw-message behaviour. Confirmed; the fix must change the assertion (R89).
- **OBS-1** — `TelemetryLogsExportTests` asserts registration only; nothing scans templates. Confirmed.
- **AUTH-1** — `RefreshReplayTests` (4) pin exactly two presentations; the third-presentation and attacker-first
  cases are absent — confirmed test-completeness gap alongside the logic finding (Phase 3 spec).
- **UX-6 / UX-7** — `SessionKeepAliveTests` (14) run on `FakeTimeProvider`; none sets a hung-refresh timeout or a 403-with-HTML body; the RCL's 37 % coverage is concentrated in `AuthService` (keep-alive) — the branch the finding targets (`PostAsync` timeout) is the untested `catch`. Confirmed.
- **NAT-14** — `grep RequireApiBaseUrlInRelease|AndroidSigning|network_security_config_release tests/ .github .forgejo` → 0 outside the csproj; every CI MAUI build is `-c Debug`. The local Release build succeeded only because this run supplied the property. Confirmed Certain.
- **DEP-13** — live API re-queried by the synthesizer: `[]` on all three repos; GitHub `private: true`. Confirmed Certain.
- **DEP-14** — not executable here (a write probe would be a state change on the live forge). Stays **Likely**; the human decision stands.
- **DEP-18** — `EveryContainerImage_IsPinned_NotFloating` passed while `postgres:17` floats in 4 workflow sites → the gate's scope, not its logic, is the gap. Confirmed.
- **BILL-1/2** — `BillingGateTests` (gate-off 404s) pass; nothing exercises `EntitlementService` with a granting projection under the gate, and `AdminControllerTests` never sets `Billing:Enabled=false`. Confirmed.
- **TR-19** — `gen_coverage.py` re-run: `903 files … 1 unmapped (tests/E2E.Tests/BlazorBoot.cs)`; committed map says 901/0. Confirmed Certain (tree restored).
- **TR-12/16/17/18/22/23** — the removed-line sweep is reproducible (`git diff f52be3d..HEAD -- src | grep ^-` ∩ lesson fenced blocks). Confirmed.

### Refined by tools
- **DEP-24** (bump-together step ② skipped) — `--locked-mode` restore ran green in CI for this SHA and the WASM packs are `VersionOverride`-pinned; **downgrade Low → Info** (playbook wording only).
- **UX-15** — `--list-tests` count reproduced: 35 `[Test]` incl. one `[Explicit]`; the run's three shards summed to 34 executed journeys in CI. Confirmed as filed (Info; doc definition needed).

### Contradicted / false positive
- **None.** No Phase 1 finding was disproved by a tool; no candidate rule is removed.

### Unresolvable by tools (static vs tools; marked for Phase 5)
- **DEP-14** (token write scope) — needs a deliberate, authorized write probe on the forge (out of audit scope).
- **NAT-12(b)** (MinIO vs presigned + `Authorization`) — the MinIO fixture exists (`S3FileStorageMinioTests`, 5 green) but no test sends a bearer to a presigned URL; a Phase 3 spec can settle it cheaply.
- **NAT-13** (v1-only APK on a Mac without a keystore) — no Mac in this run (Apple legs skipped).
- **OBS-2** (drop semantics under an unreachable collector) — SDK-default reasoning; a Phase 3 spec with an unroutable endpoint can settle it.

## Tool-only findings (missed by static)
| ID | Sev | Finding | Rule |
|----|-----|---------|------|
| **TOOL-5** | Low | **xunit 2.9.3 is deprecated ("Legacy") across `Api.Tests`, `Core.Tests`, `Ui.Tests`**; xunit.v3 is the maintained line. Not a CVE; migration is a test-infrastructure task (analyzers, `[Fact]` semantics unchanged, `IAsyncLifetime` → `IAsyncLifetime`/`ValueTask`). Plan it before a future SDK major forces it. | R66-adj (maintained deps) |
| **TOOL-6** | Info | `Microsoft.ApplicationInsights 2.22.0` (deprecated "Other") reaches `E2E.Tests` transitively (NUnit adapter/Playwright chain). Test-only, no runtime exposure; drops with the NUnit/adapter bump in `outdated`. | — |
| **TOOL-7** | Medium (test-completeness) | **`Shared.Ui` at 37 % line / 38.6 % branch** — the bUnit chassis exists but coverage concentrates in `AuthService`/switchers; `Household.razor` (70 decision points), `Login.razor` (64), `AdminConsole.razor` (54), `NotificationBell` (41), `Billing.razor` (37) remain E2E-only. That is where UX-11/UX-13/BILL-7 live. Phase 3 Part B target. | R70 (extend) |
| **TOOL-8** | Low (debt) | **`AuthService.cs` is the complexity hotspot at 131 decision points / 882 LOC** after #11 (+284) and #233 — three renewal paths, impersonation, remembered prefs, native/web branches in one class. Refactor seam: extract the keep-alive scheduler (`RenewalScheduler`) and the preference shadow; both are already clock-injected. | debt |
| **TOOL-9** | Info | Coverage per run is inflated for Infrastructure (98.9 %) by generated migrations; Api branch coverage 67.7 % — the auth/admin branch gap v3 TOOL-3 named is narrower (v3: 63.5 %) but not closed. | — |

## Supply-chain depth (inherited by every clone)
- **Load-bearing single-maintainer deps:** `Otp.NET` (documented, confined — R66/T50 held); **MailKit/MimeKit**
  (jstedfast — single-maintainer, very active; behind `IEmailSender`, confined to `Infrastructure/Email/` by R-rule);
  **DotNetEnv** (small, dev-only loader; behind `Program.cs`); `bunit` (test-only). No new single-maintainer runtime
  dep entered in the delta (OpenTelemetry, AWSSDK, Npgsql are foundation-backed).
- **License compatibility with commercial use:** all permissive (MIT/Apache-2.0/BSD/PostgreSQL/Microsoft); the
  forbidden list (GPL/AGPL/LGPL/SSPL) is enforced on Api + Web graphs in CI; Maui graph unscanned (v3 TOOL-1 residual,
  needs the workload on a scanning leg — note for the R103 Release leg, which has it).
- **Lockfile integrity:** 9 `packages.lock.json`, `--locked-mode` in `build-test` for Api + Web on both forges;
  **Maui has no lockfile** (host-conditional TFMs) and now restores on the maintainer's desk session (DEP-20) — the
  one open supply-chain exposure of the run.
- **Transitive bloat:** Api graph 68 packages (direct + transitive) — lean. `outdated` shows nothing floating past
  the pinned lines; the .NET 10.0.12 package line is held back deliberately to match the runtime image (R61).
- **Self-hosted side (outside the tree):** the CI image installs the SDK via an unverified `dotnet-install.sh` and
  `--skip-sign-check`; actions resolve from mutable `@vN` tags (DEP-23). Review item for the server folder's CI.md.

## Enforcement backlog (ordered; each = one arch test / analyzer / props rule / CI step; mapped to its rule)
Keystone first (gates other gates depend on), then security, then correctness, then hygiene.
1. **R98** — `ForgejoCiParityTests`: every `actions/checkout` in `.forgejo/workflows/*` has `persist-credentials: false` (allowlist the `qa-artifacts` fetch step); `deploy.yml` contains a `native-smoke-` failure check and newest-task selection; `protect-branches.ps1` contains `exit 1` and a Forgejo half. + CI step: read `/branch_protections`, fail when `develop`/`main` unlisted. (DEP-13/14/19)
2. **R97** — `EnforcementGateTests`: extract every relative path literal read by `tests/Api.Tests/**` and assert `Code(path)`; add `.forgejo/scripts/`, `deploy.yml`, both `postman-sync.yml`, `forbidden-licenses.json`, `.dockerignore` to `code=` in BOTH copies. (DEP-15)
3. **R99** — widen `EveryContainerImage_IsPinned_NotFloating` to workflow `services:`/`docker run` + Dockerfile `FROM`; require major.minor or digest. (DEP-18)
4. **R91** — extend the R43 canary: after `DissolveAsync`, `OutboxMessages WHERE TenantId = t` empty; arch scan that email `EnqueueAsync(` passes a tenant id. (JOBS-2)
5. **R90** — `OutboxProcessorTests`: payload scrubbed on `Sent`/`Dead`; `ScheduledJobs_Include("outbox-retention")`. (JOBS-2)
6. **R81** — `RefreshReplayTests`: third presentation inside the window ⇒ 401 + revoke-all; attacker-first ordering documented. (AUTH-1)
7. **R107** — `ConfigPostureTests` cross-project constant: `AuthService.RefreshTimeout + RenewRetryDelay < RefreshToken:ReuseGraceSeconds`. (UX-6)
8. **R108** — `SessionKeepAliveTests` theory status × body; retry-count backoff test. (UX-7, UX-12)
9. **R102** — `Ui.Tests` `BearerScopedHandler` (foreign absolute URL ⇒ no header); arch ban on `AuthenticationHeaderValue("Bearer"` elsewhere. (NAT-12)
10. **R89** — `EnforcementGateTests` scan `Error\s*=.*\.Message`; `WebhookDeliveryLogTests` enumerated set. (JOBS-1)
11. **R93** — `EnforcementGateTests.LogTemplates_NeverCarryPii`. (OBS-1)
12. **R92** — `EmailAttachmentTests` malformed media-type/filename theories; `OutboxEmailTests` zero rows. (JOBS-3/4)
13. **R94** — `WebhookDeliveryTests` 302 stub + arch scan `AllowAutoRedirect = false`. (JOBS-9)
14. **R86** — `EndpointDataSource` gate-off assertion over BILLING/PUBAPI/HOOKS; admin billing writes gated or exempted by name. (BILL-2/3)
15. **R103** — `native-release-android` job in both copies (dispatch + Monday) with `apksigner verify`; negative case for the guard; `EnforcementGateTests` asserts the job exists. (NAT-13/14/18)
16. **R105** — `NativeChromeGateTests` manifest posture (`allowBackup="false"`, cleartext off, no debuggable). (NAT-17)
17. **R104** — `.gitignore` signing patterns asserted; doc-grep for `-p:AndroidSigning*Pass=`. (NAT-15)
18. **R106** — native classifier positives per R60 class incl. `Directory.Build.props`. (NAT-16/S0-G6)
19. **R109** — grep gate: no `page.GotoAsync(`/`ReloadAsync(` outside `BlazorBoot.cs`; retry reloads landed URL; Slowest-journeys threshold in both copies. (UX-8/9, DEP-21)
20. **R111** — bUnit `MainLayout`: `Rejected` on a protected route ⇒ `/login`; layout gates on session-held. (UX-11, AUTH-5)
21. **R84** — `SessionKeepAliveTests` crossing `exp` under outage (test-name manifest). (AUTH-5)
22. **R122** — reflective `ConfigPostureTests` over `*Settings.Enabled` with the named R53 exceptions. (S0-G8, AUTH-11)
23. **R82** — reflective list-normalization theory. (AUTH-7)
24. **R87** — `DocAndConfigSyncTests`: no raw `GetValue("<Section>:Enabled")` where a `*Settings` exists. (BILL-4)
25. **R95** — config-catalog regex captures `configuration["ALL_CAPS"]`; `TelemetryLogsExportTests` asserts `Protocol`. (OBS-3)
26. **R96** — `.Message` → `HasMaxLength` property without `Truncate(`. (JOBS-5)
27. **R112** — arch grep `"signup_not_allowed" =>` outside `AuthErrorCopy`. (UX-14)
28. **R113** — shard filter excludes `[Explicit]`; `e2e.md` suite size asserted. (UX-15)
29. **R114** — CI step `gen_coverage.py` + `git diff --exit-code` (never code-gated, beside `qa-artifacts`). (TR-19)
30. **R115** — `gen_coverage.py` quote-currency sweep + orphan check. (TR-12/16/17/18/22/23/29)
31. **R116 / R118 / R119 / R120 / R121 / R83 / R88** — doc-sync gate extensions (rule-id hygiene, doc-map `docs/**`, Postman gate floor + error-code parity, compiled-in limits, diagram currency, catalog literals). (TR-15/21/24/25/20, AUTH-9, BILL-6)
- **Backlog under existing rules (no new number):** R68 ordering + `lib` byte-identity (S0-G7); R61 prose sources (S0-G9); R36 scan of any `Map*` outside the three dirs (S0-G10); R65 Maui graph on the R103 leg.

## Deference record
No Phase 1 rule removed or weakened. Two Phase 1 severities refined (DEP-24 → Info; none raised). Conflicts C1–C10
stand as logged.
