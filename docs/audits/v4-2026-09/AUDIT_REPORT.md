# v4 Delta Audit — Phase 1: Comprehensive static audit

> **Run:** `docs/audits/v4-2026-09/` · **Suite:** `docs/audits/AUDIT_SUITE.md` (delta mode) · **Date:** 2026-09-23
> **Trigger:** a new wave of platform work since v3 settled — the Forgejo primary-forge migration (LOCALCI-4,
> ADR-028: a structural change to the deploy/CI trust boundary), the pre-launch gates (GATES-1/2, ADR-027), the
> refresh-token grace + session keep-alive (ADR-002 addenda), JOBS-4 attachments, OTLP logs export, the sloth
> rebrand, and ~30 fix/doc PRs. **Method:** eight parallel read-only auditors (Step 0 + seven areas), each
> raw report committed verbatim under `phase1-raw/`; the load-bearing findings (AUTH-1, JOBS-2, NAT-12, DEP-13)
> were re-verified in the code / live API by the synthesizer before headlining.

## SUMMARY

- **Commit SHA under audit: `8c8ed4a746c57a6df8bac5f31195f011dc0758c4`** (`develop`, 2026-09-23). **BASE (prior
  settled run): `f52be3d`** (PR #193, v3 comprehensive truth-up, 2026-07-28). Delta surface = 50 first-parent
  merges (GitHub #194–#235, Forgejo #2–#18) — 291 files, +14,896/−1,442.
- **Step 0 — prior remediations:** **60 Held · 0 Regressed · 1 Superseded (T35) · 1 Held-partial (T36:
  `allowBackup="true"` was never flipped — a v3 adjudication error, not a delta regression).** The gate-file delta is
  purely additive; every v3 machine gate was re-read for non-vacuity after the delta and all fire on the right
  paths. Five pre-existing gate-scope gaps filed (S0-G6..G10).
- **Fresh findings: 99 — Critical 0 · High 6 · Medium 27 · Low 45 · Info 21.** The six Highs split 3 code /
  3 docs-currency: **JOBS-2** (attachment bytes + recipient PII persist forever in the outbox, outside every
  erasure path), **DEP-13** (no branch protection on either forge — verified live), **DEP-14** (Forgejo's
  repo-write job token persisted into every job's workspace by `actions/checkout`), and **TR-12/13/14** (the
  course and three operator docs still teach/describe the GitHub auto-deploy pipeline ADR-028 replaced).
- **Rules added (candidate block, `FOUNDATION_RULES.md`): R81–R122** (42 candidates; Phase 5 consolidates).
- **Conflicts logged (`RULE_CONFLICTS.md`): C1–C9** — none overturns a prior rule; two are v3 tracker
  mis-marks (T35/T36), two are rules whose text outgrew their gate (R67, R68), one is a deliberate R53
  exception never written into the rules file (`SignupSettings`), one is R63 vs a forge that ignores
  `permissions:`, one is R51's family-revocation intent vs the grace addendum, one is the R80/R77–R79
  numbering ambiguity, one is R75 wording vs scope.
- **Tools (Phase 2, run first for scoping):** build clean (0 warnings; the only "error" is the R67 guard refusing
  a Release MAUI build without an API base — the gate working), **909/909 unit+integration tests green**
  (Api 767 incl. RLS + Testcontainers gates executed, Core 62, Ui 80), Forgejo CI green for this SHA incl. the
  3 E2E shards, 0 vulnerable packages, 0 forbidden licenses. Details in `AUDIT_RECONCILIATION.md`.

### Foundation-readiness verdict
**READY WITH FIXES.** The tenancy/RLS/auth core held every v3 guarantee under a 50-PR delta with zero
regressions, and the delta's own gates (Forgejo parity, billing gate, config posture, image pins, bfcache) are
real and firing. What slipped is at the edges the v3 rules did not cover: **data lifecycle in the outbox** (a
new data class rode an old table), **the trust boundary of the self-hosted forge** (protection + token scope
were assumed, not configured), **the security posture of two coupled client/server changes** (grace + keep-alive
are each individually documented but their joint invariants are unpinned), and **doc/course currency**
(review-only practices lost under maintenance, exactly as the suite predicts). None of it requires a structural
change; all of it is gate-able.

### Top-10 (ordered for remediation: keystone/security first)
| # | ID | Sev | One line | Why first |
|---|----|-----|----------|-----------|
| 1 | **DEP-13** | High | `develop`/`main` unprotected on Forgejo (live `[]`) and GitHub (private free plan → 403); `protect-branches.ps1` is GitHub-only and exits 0 on failure | Every other gate fails open on a rewritten base; cheapest fix, largest blast radius |
| 2 | **DEP-14** | High (Likely) | Forgejo job token is repo-write for push/same-repo-PR; copy dropped `permissions:`; `actions/checkout@v5` persists it in every job incl. the unlocked MAUI restore on the host-mode desk runner | With #1 there is nothing between third-party build code and `git push origin HEAD:develop` |
| 3 | **JOBS-2** | High | ≤10 MiB attachment bytes (base64) + `To`/body live forever in `OutboxMessages.Payload`; email enqueues carry no `TenantId`; no purge, no scrub, no dissolve/erasure contributor | Per-user AND per-tenant erasure completeness (R12/R13/R43 spirit) is broken for a new data class; storage cost on the free tier |
| 4 | **AUTH-1** | Medium | The 60 s refresh-reuse grace is unbounded (N replays each mint a chain) and symmetric (thief-first ordering also silences the family revocation) | R51's only theft signal is now conditional; ADR-002 addendum documents half the trade |
| 5 | **UX-6 + UX-7** | Medium | Client keep-alive and server grace are coupled by an unpinned invariant (refresh timeout 100 s + retry 30 s > grace 60 s ⇒ a lost-response retry trips revoke-all); any 400/403 is read as "the server said no" and deletes the native token | The pair was shipped to cure "the web keeps forgetting me" and can reproduce it |
| 6 | **NAT-12** | Medium | Native bearer handler attaches the JWT to every request; the share launcher fetches the (S3-presigned) export URL with that client → token to a third-party origin + Likely S3 400 | Structural: any downstream slice fetching an absolute URL leaks the token |
| 7 | **JOBS-1 / OBS-1** | Medium | Raw `ex.Message` persisted into `WebhookDelivery.Error` and returned to the tenant (R16 violated by a slice whose comments cite GAP-3); `{Email}` in 8 log templates now exported to a third-party collector with `ParseStateValues` | Both are review-only rules that did not hold under maintenance → need machine halves |
| 8 | **BILL-1 / BILL-2** | Medium | "Billing off ⇒ everyone Free" is false: `ResolvePlanKey` is gate-blind and the ungated admin comp write mints Pro projections while off; ADR-027's accepted-consequence paragraph is contradicted | Decision-gated (feature or leak) — must be adjudicated before the vuelto/jigger-jot ports |
| 9 | **NAT-14 / NAT-13** | Medium | R67 is [machine] with no CI execution path (every MAUI CI build is Debug); the #213 release-signing fallback is Windows-only and its verify is print-only | The delta doubled never-exercised Release-only MSBuild logic |
| 10 | **TR-12/16/17/18/19 + TR-13/14** | High/Med (docs) | Course: 8.3 has zero Forgejo content, 2.2/3.4/4.5/0.2/2.3 quote removed code, COVERAGE.md stale (says 0 unmapped, tree has 1); QA §1 + Postman README still say a develop merge auto-deploys | The user asked for course currency; the suite's Phase 7 rule ("reconcile on the branch that changes code") was never written into the repo |

---

## Delta surface

First-parent merges `f52be3d..8c8ed4a` (50): Forgejo #18/#17 e2e boot retry · #16 e2e shards · #15 slowest
journeys · #14 deploy-already-green · #13 runner docs · #12 android smoke + refused-push message · #11 session
keep-alive · #10 refresh reuse grace · #9 mac probe · #8 e2e after builds · #7 sloth rebrand · #6 apple artifacts
· #5 knobs → repo variables · #4 permissions key · #3 dispatch deploy (LOCALCI-4) · #2 coverage map encoding ·
GitHub #235 email attachments (JOBS-4) · #234 ADR-029 · #233 theme save race · #232 protect branches · #231
android system bars · #230 native api base slash · #229 markdown never code · #228 render gate vars · #227
pre-launch gates (GATES-1/2) · #226 LOCALCI-3 trigger diet · #225–#221/#217/#216 docs · #224 apk name · #223
sideload traps · #222 brand tokens · #220 SDK 10.0.401 · #219 gitignore env · #218 e2e apt · #215/#214 chores ·
#213 android release signing · #212 otel logs export · #211 stripe return pages · #210/#207 native smoke · #209
deploy smoke superseded · #208 otlp http paths · #206 postman-sync arg · #205/#204 course PDF · #203 bfcache guard
· #202 qa artifacts · #201–#199 claude/* (send-test) · #198 postman ping · #197 xcode 26.6 · #196 SDK 10.0.400 ·
#195 ADR-024 · #194 webhook delivery-log failures. Files: `phase1-raw/../BRIEF` list — 291 files (72 tutorial,
31 Api.Tests, 25 Shared.Ui, 25 Api, 14 Infrastructure, 13 Maui, 12 Ui.Tests, 12 E2E, 12 Web, 3 Forgejo workflows).

## Step 0 — prior-remediation status (v3 T1–T62)

Full table with evidence per task: `phase1-raw/step0.md`. Verdict: **60 Held · 0 Regressed · 1 Superseded · 1
Held-partial.** Highlights:
- **Superseded — T35** (NAT-4): the filtered `dotnet test` assertion in the Windows smoke was replaced by an explicit
  boot-probe verdict (`if (-not $ok) { exit 1 }`); e2e shards assert non-vacuity. Intent preserved.
- **Held-partial — T36** (NAT-9b): `AndroidManifest.xml:3` still `android:allowBackup="true"`; never changed since
  its introduction; the T36-closing commit did not touch it. Re-filed as NAT-17 + conflict C1.
- Every brief-named target checks out: `BillingSettings`/`SignupSettings` in `ConfigPostureTests`;
  `FeaturesController` inside the R36/R76 scan dirs and auto-covered by the reflective R74 gate; `OtlpEndpoints` is
  a URL helper (no `Map*`); the rotation-link migration touches user-scoped `RefreshTokens` and the RLS gate derives
  its table list from `RlsDdl.TenantTables(model)` (correctly out of scope, not skipped); both `.forgejo/workflows/*`
  are inside the pin-parity + R61 + LOCALCI-3 gates; `bfcache-guard.js` in both hosts; SDK 10.0.401 agrees across
  every pin source.

| ID | Sev | Gate-scope gap (pre-existing; the delta did not close it) |
|----|-----|---|
| S0-G6 | Low | R60: the `native=` classifier omits `Directory\.Build\.props` in BOTH copies (rule names it; v3 T35 claimed it) — a `Directory.Build.props` change skips native builds. = NAT-16 |
| S0-G7 | Low | R68: only the RCL script SET is gated; "theme.js before first stylesheet" + "lib trees byte-identical" clauses ungated (hold today) |
| S0-G8 | Low | R53: `ConfigPostureTests` is a hand-curated list; nothing reflects over `*Settings` with `bool Enabled` |
| S0-G9 | Low | R61: prose pin sources (TECH_STACK/CLAUDE.md/DEPLOYMENT) ungated (agree today) |
| S0-G10 | Info | R36: route-prefix scan is directory-scoped; a `Map*` under a new dir is invisible to the collision check |

## Template-readiness (first-class)

- **Slice contract holds.** A `Features/<X>/` slice still touches only slice code for every delta horizontal: the
  signup gate sits at the single founding choke point; `IEmailSender.SendAsync` gained an *optional* parameter;
  OTLP/keep-alive/bfcache/system bars are host-level. No new core→slice dependency except the hand-listed
  `BillingGateConvention.GatedControllers` (MVC-only; a minimal-API `MapGroup("/api/billing…")` is neither removed
  nor detected — BILL-3).
- **New burdens a slice/downstream inherits (fix in the template, not per slice):** bearer-on-every-request
  (NAT-12); attachment media-type/filename validation is blankness-only (JOBS-3/4); "identifiers only in log state"
  is doc-only and now vendor-facing (OBS-1); every page reading a config gate copies the `_billingEnabled` pattern
  and must `StubFeatures()` in tests (UX template note); E2E copies the Free seat number (BILL-6).
- **Review-only good practices the delta introduced — what clones lose:** `CancellationToken` by name to
  `IEmailSender` (no analyzer, no exemplar); Postman description content (R74 checks presence only — TR-21);
  FLOWS/ARCHITECTURE currency (TR-20); course reconcile-on-code-change (memory-only — TR-24); deploy-trigger wording
  (TR-13/14); DEPLOYMENT §10 variable catalogue; compiled-in limits block (TR-25); Forgejo secrets/variables + branch
  protection on both forges (DEP-13); the CI image pins (outside the tree); signing-material hygiene (NAT-15);
  `build_assets.py` regeneration (NAT-22).
- **Gate-enforced ✓ (clones keep):** Forgejo parity (11 facts), billing controllers gated, bfcache loaded by both
  hosts, Android chrome tokens, image pins (fixtures+compose), config catalogue, doc-map top-level, QA count, Postman
  presence, resx parity, already-green deploy shape, R70 chassis (every delta component is bUnit-covered).

---

## Findings by area
Severity → Confidence → location → what → fix → gate. Full evidence (quoted code, line numbers, runtime probes)
is in the raw report named per area. IDs continue v3's scheme.

### AUTH (`phase1-raw/auth.md`) — Med 1 · Low 8 · Info 4
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **AUTH-1** | Med · Certain (Likely exploit) | `RefreshTokenService.cs:124–153`, `AuthController.cs:187–204` | Grace replay mints a NEW independent chain and writes nothing: **unbounded** (N replays in 60 s), **symmetric** (attacker-first ordering also classified benign), **schedulable** (#11's fixed cadence); `/refresh` unlimited | One-shot grace per rotated token (stamp/link on the grace path), Warning log + count, amend ADR-002 addendum; `RefreshReplayTests` 3rd presentation ⇒ 401 + revoke-all → **R81** |
| AUTH-2 | Low · Suspected | `AuthController.cs:170–194` | Inspect→issue→mark = 4 round-trips, no fence; a revoke-all between inspect and INSERT leaves the new token live; grace widens the input set | Conditional `MarkRotated WHERE IsRevoked=false` + rollback on 0 rows; review item |
| AUTH-4 | Low · decision | `AuthService.cs:650,724–738`, `RefreshTokenService.cs:93` | No absolute session lifetime: sliding expiry + idle-tab renewal = signed in indefinitely while open | Optional `RefreshToken:AbsoluteLifetimeDays` — **human decision** |
| AUTH-5 | Low · Suspected/Certain | `AuthService.cs:656–668`, `MainLayout.razor:27` | Past `exp` under an unreachable server the layout flips to the anonymous shell while the session is held; a later 401 skips `SignedOut` (ADM-9 pref wipe) | Gate layout on session-held; derive `wasAuthenticated` from `_sessionHeld`; `SessionKeepAliveTests` crossing expiry → **R84** |
| AUTH-6 | Low · Certain | `Web/Program.cs:47–48` | Resume-renewal path is native-only; web never fires `Notify()` | 5-line `visibilitychange` interop or document the exception |
| AUTH-7 | Low · Certain | `SignupSettings.cs:31–39` | Green list bound raw (no trim/blank-drop/`@domain`); a stray space or `[""]` locks the deployment (fails closed) | Normalize at bind; `SignupGateTests` theory → **R82** |
| AUTH-8 | Low · Certain | `UserService.cs:174–215`, ADR-027 | Invited-but-unlisted addressee founds a tenant-of-one at sign-in (never-accept / revoked / `WouldAbandonData`) — a variant ADR-027 does not list | Extend ADR-027 accepted-consequences (or defer creation) — **human decision** |
| AUTH-9 | Low · Certain | Postman OTP-verify / magic-link / OAuth callbacks | `403 signup_not_allowed` + `?error=signup_not_allowed` undocumented (0 hits) | Add to 4 descriptions → **R83** |
| AUTH-12 | Low · Suspected | `AuthController.cs:111–138`, `NativeAuthController.cs:91–105` | Refused-signup catch skips the external-scheme `SignOutAsync`; carrier cookie lingers | `finally` sign-out; assert cookie deleted |
| AUTH-3 | Info | `RefreshTokenService.cs:158` | `RevokeRefreshTokenAsync`/`RevokeAsync` dead after `MarkRotatedAsync` | Delete or keep as primitive |
| AUTH-10 | Info | `gates.md:127–129` | Doc names `QueryAllTenants()`; code uses the tagged `IgnoreQueryFilters` hatch | One-line correction |
| AUTH-11 | Info | `FOUNDATION_RULES_v2.md` R53 | Empty-list-⇒-open is a pinned, argued exception R53's text does not record | Amend R53 → conflict C4, **R85** |
| AUTH-13 | Info | `AuthController.cs:148–150` | Docstring says replay is "audit-logged"; code `LogWarning`s only | Fold into AUTH-1's observability |

Verified clean: single choke point (all four minting paths + E2E), owner-at-creation semantics, OTP consumed
before the gate, migration correct (nullable, no FK by design, PK lookup), RLS gate classifies it correctly,
logout/revoke-all never stamp `RotatedAt`, keep-alive clock-injected and never renews signed-out/impersonating,
#230 leaves no open redirect.

### BILLING (`phase1-raw/billing.md`) — Med 2 · Low 5 · Info 3
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **BILL-1** | Med · Certain | `EntitlementService.cs:24–33`, `Program.cs:203`, ADR-027 §1 | "Off ⇒ every tenant Free" is false: `ResolvePlanKey` is gate-blind; a granting projection keeps Pro while the webhook that could correct it is 404 (cancelled Pro stays Pro; comps never lapse; Stripe keeps charging) | Truth-up docs/log to "every tenant *without a granting projection*"; startup warning when off + provider-managed rows; `BillingGateTests` pin — **human decision** on semantics |
| **BILL-2** | Med · Certain | `AdminController.cs:113–203`, `AdminConsole.razor:132–137`, `ArchitectureTests.cs:235–253` | Admin comp/revert under `api/admin` are ungated and invisible to `BillingControllers_AreAllGated`; staff can mint Pro with billing off, contradicting "nobody holds ProFeature" | Gate them (or document comp-while-off + drive console buttons from `/api/features`) — **human decision** → **R86** |
| BILL-3 | Low · Certain | `BillingGateConvention.cs:31–35` | Removal by 2-type hard-coded list; honesty test only sees type-level `[Route("api/billing…")]` — minimal-API groups, action-level absolute templates, differently-prefixed controllers are blind spots; the Stripe fail-fast was relaxed on this list | Runtime `EndpointDataSource` assertion with the gate off → **R86** |
| BILL-4 | Low · Certain | `Program.cs:37–41` vs `Infrastructure/ServiceCollectionExtensions.cs:141` | Gate read at two sites by two mechanisms, no shared key | One constant / pass the bound bool → **R87** |
| BILL-5 | Low · Certain | `SubscriptionLapseSweepJob.cs:41–47`, `BillingNotifier.cs:52–54` | Sweep runs unconditionally and emits "Resubscribe from the billing page" + `url:/billing` while off | Early return on `!Enabled` |
| BILL-6 | Low · Certain | QA plan `:1174,1940–1944`, `billing.md:258,434,458`, lesson 5.3:300, `AcceptSeatQuotaTests.cs:94`, both PDFs | Seven stale "3"s after the 3→5 seat re-tune; QA-ADV-15's drill no longer reproduces at the cap; E2E copies `FreePlanSeatLimit = 5` in two files | Sweep; "cap − 1" phrasing; regen PDFs → **R88** |
| BILL-7 | Low · Likely | `AppHeader.razor:75–79`, `AuthService.cs:573–585` | Header probes `/api/features` once; a failed cold-start probe hides the Billing link all session | Re-probe on identity change; bUnit test |
| BILL-8 | Info | `BillingWebhookHandler.cs:122–134` | Activation notice verified R56–R59 clean; docstring overstates "after a lapse" (lapsed keeps `active`) | Docstring |
| BILL-9 | Info | `FeaturesController` | Anonymous single-bool, in Postman, R36-scanned, no cache/rate-limit — harmless | none |
| BILL-10 | Info | tests | Present: posture, 404-vs-401, gate-on restores, provider matrix, bUnit both states. Missing: gate-off browser journey (both CIs run E2E with `Billing__Enabled=true`), comp-while-off, sweep-while-off, route-table assertion | Phase 3 specs |

### JOBS / EMAIL / WEBHOOKS / OBSERVABILITY (`phase1-raw/jobs-obs.md`) — High 1 · Med 4 · Low 6 · Info 3
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **JOBS-2** | **High** · Certain | `OutboxEmailSender.cs:29`, `OutboxMessage.cs`, absence in Dissolution/Export/Erasure services, `ExpiredTokenCleanupJob` | Attachment bytes (≤10 MiB → ~13 MiB base64 JSON) written into `OutboxMessages.Payload`; email enqueues pass no `TenantId`; `Sent` rows never scrubbed/purged (no reader of `OutboxStatus.Sent`); no contributor touches the outbox; `async-jobs.md:261` "audit when wiring dissolve" never done. Recipient email + body + tenant documents persist forever outside every erasure path; R43's canary cannot reach `TenantId`-less rows | Scrub `Payload` on `Sent`/`Dead`; scheduled retention purge (`Outbox:RetentionDays`); stamp `TenantId` on email enqueues; `OutboxDataContributor` wipes pending rows on dissolve → **R90, R91**. **Human decisions:** retention window; move attachments to `IFileStorage` keys now? |
| **JOBS-1** | Med · Certain | `WebhookOutboxHandler.cs:51,73`, `WebhookService.cs:126,140`, `WebhookEndpoints.cs:118` | Raw `ex.Message` persisted into `WebhookDelivery.Error` and returned verbatim by `GET /api/webhooks/{id}/deliveries` — v2 **R16** violated; the code's own GAP-3 comments name the tenant-readable row as the "safe" place. Latent before #194 (failed rows were discarded), live now | Enumerated reason codes; `ex.Message` to server log only; string-scan gate → **R89** |
| **JOBS-3** | Med · Certain (runtime-verified, MimeKit 4.17.0) | `IEmailSender.cs:46–67` vs `SmtpEmailSender.cs:74` | `Validate` checks `MediaType` for blankness only; `ContentType.Parse` throws on `"pdf"`, `"application/pdf; charset="`, CRLF → 5 attempts → dead-letter — the loop ADR-007's amendment says the guard prevents | Strict `token/token` grammar in `Validate` → **R92** |
| **JOBS-9** | Med · Likely | `Infrastructure/ServiceCollectionExtensions.cs:87`, `WebhookSender.cs:24–36` | Webhook `HttpClient` keeps `AllowAutoRedirect=true`; guard checks the configured URL only — a 302 to a private/metadata host is followed with the signed body (pre-existing line; new caller `SendTestAsync` in the delta; never adjudicated) | `AllowAutoRedirect=false`; 302 stub test → **R94**. **Human decision:** acceptable for tenant endpoints? |
| **OBS-1** | Med · Certain | `TelemetryExtensions.cs:60–69` + 8 `Log*` sites (`JwtTokenService.cs:76` on every issue, `UserService.cs` ×5, both OAuth callbacks, invitation) | `ParseStateValues=true` exports `{Email}` as an indexed attribute to the OTLP collector (docs point at Grafana Cloud); no scrubbing exists; `observability.md:40` "no PII beyond identifiers" is false | `{UserId}` in those lines; DEPLOYMENT data-class note; log-template gate → **R93** |
| JOBS-4 | Low · Certain | `SmtpEmailSender.cs:74`, `IEmailSender.cs:55` | `FileName` unsanitised: `../../etc/passwd` lands verbatim in `Content-Disposition` (CRLF is RFC-2231-encoded — verified, no header injection) | Reject control chars, `Path.GetFileName`, cap length → **R92** |
| JOBS-5 | Low · Likely | `WebhookOutboxHandler.cs:73`, `WebhookService.cs:140` | `Error` not truncated to its 1000-char column → `DbUpdateException` loses the failure row again | Truncate at write site → **R96** |
| JOBS-6 | Low · Certain | `WebhookService.cs:124–127`, `WebhookOutboxHandler.cs:49–52` | `catch (Exception)` records cancellation as a failed delivery and burns an attempt | Rethrow `OperationCanceledException` |
| JOBS-7 | Low · Certain | `WebhookEndpoints.cs:44–66` | test/replay POSTs unlimited; each now inserts a row + outbound POST at API speed | Per-tenant fixed-window policy |
| OBS-3 | Low · Certain | `TelemetryExtensions.cs:32`, `DocAndConfigSyncTests.cs:54,65` | `configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]` flat read invisible to the config-catalog gate; app decides path from IConfiguration while SDK reads protocol from process env | Set `o.Protocol` from the same value; regex extension → **R95** |
| OBS-2 | Low · Likely | `TelemetryExtensions.cs:90–113` | Unreachable collector ⇒ silent bounded drops, no ILogger signal, no test asserts per-signal endpoint wiring | Document; SDK self-diagnostics; per-signal options test |
| JOBS-8 | Info | — | By-name `CancellationToken` cannot be positional (won't compile) — no gate needed; R46/R55/R57 verified preserved | none |
| JOBS-10 | Info | `ServiceCollectionExtensions.cs:45–51` | `IDbContextFactory` scoped registration untested in DI | One harness assertion |
| OBS-4 | Info | `observability.md:76–77`, `:2026-09-05` | Package list omits `Instrumentation.Runtime`; `/v1/logs` unmentioned | Doc |

### DEPLOY / CI / SUPPLY-CHAIN (`phase1-raw/deploy-ci.md`) — High 2 · Med 6 · Low 5 · Info 2
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **DEP-13** | **High** · Certain (live) | `tools/protect-branches.ps1:22–39`; Forgejo `/branch_protections` = `[]` on all three repos; GitHub private → 403 | Neither forge protects `develop`/`main`; the script is GitHub-only, cannot apply on a private free plan, and swallows failure with exit 0; `production` env has no reviewer (v3 DEP-7 still unmet) | Forgejo half of the script + `exit 1`; CI step reads `/branch_protections` and fails when unlisted → **R98**. **Human decision:** where protection lives |
| **DEP-14** | **High** · Likely | `.forgejo/workflows/ci.yml:60–63` + 14× `actions/checkout@v5`; `deploy.yml:11–12` | Forgejo's job token is repo-write for push/same-repo-PR (docs); the copy dropped `permissions:` (R63) on an unverified "cannot write"; checkout persists the token in every workspace; jobs run unlocked MAUI restore, `npm ci`, brew, Playwright apt | `persist-credentials: false` everywhere except the one `git fetch` step; probe the write claim → **R98**. **Human decision:** was write probed? |
| DEP-15 | Med · Certain | both `ci.yml` `code=` regex | Classifier omits `push-to-github.sh`, `deploy.yml`, both `postman-sync.yml`, `forbidden-licenses.json`, `.dockerignore` — a PR adding `--force` to the push script skips `build-test` (and the R80 test guarding it) | Widen `code=` (or docs-allowlist inversion); test that every path an arch test reads is `Code(...)` → **R97** |
| DEP-16 | Med · Certain | `DEPLOYMENT.md:508–510` vs `push-to-github.sh:38–53` | Documented rollback ("dispatch on an older commit") is refused by the FF-only push + ref check; wrong advice mid-incident | Rewrite: revert + deploy, or Render Redeploy → **R100** |
| DEP-17 | Med · Likely | `.github/workflows/ci.yml:270,342–347` | Every Forgejo deploy re-runs GitHub's full pipeline incl. 2× macOS Apple builds (10×) on a repo that is private again; comments still say "public"; quota exhaustion silently ends GitHub's smoke | Knob-gate GitHub's Apple legs / drop `develop` from its push trigger — **human decision** → **R101** |
| DEP-18 | Med · Certain | `EnforcementGateTests.cs:209–254` | Image-pin gate covers fixtures + compose only; workflow `services:`, `docker run`, Dockerfile uncovered; `postgres:17` floats (major tag), no digests | Widen to all four surfaces; require major.minor/digest → **R99** |
| DEP-19 | Med · Certain | `deploy.yml:59–79` | Already-green ignores a red selected `native-smoke-*` (ci.yml's own deploy blocks on it) and accepts any historical success (no recency) | Refuse on red smoke; newest task per job → **R98**. **Human decision** |
| DEP-20 | Med · Certain (exposure) | `SETUP.md:451`, ADR-028 | Host-mode desk runner executes the lockfile-less MAUI restore for every PR in the maintainer's interactive session (credential stores); Linux runners are privileged DinD; not stated in ADR-028 | Per-host Maui lockfiles + `--locked-mode`, or low-privilege session; record residual risk — **human decision** |
| DEP-21 | Low · Likely | `BlazorBoot.cs:22,63–81`, ci `:1120–1128` | Boot retries counted, never thresholded; `banner` verdict retried like a network death | = UX-9 → **R109** |
| DEP-22 | Low · Certain | `.forgejo/workflows/ci.yml:1091–1108` | Shard filter substring match (prefix names run twice; wasted minutes, never skipped) | Anchor the filter |
| DEP-23 | Low · Certain | Forgejo `ci-image/Dockerfile:50–55` (outside tree), `DEFAULT_ACTIONS_URL: github` | Unverified `dotnet-install.sh`, `--skip-sign-check`, mutable action tags, floating brew/apt on the self-hosted side | Pin by sha256; SHA-pin actions once the parity regex allows it — review item in CI.md |
| DEP-24 | Low · Certain | PR #220 | Bump-together step ② (regenerate lockfiles) skipped; harmless because WASM packs are `VersionOverride`-pinned and `--locked-mode` is the real gate | Reword step ② |
| DEP-25 | Low · Certain | `ForgejoCiParityTests.cs:10`, `localci.md:600–665`, `DEPLOYMENT.md:452–457` | Doc drift: "deploy/* branches" in the test summary, knob counts (4 vs 5), runner capacities, token scopes | Truth-up → **R100** |
| DEP-26 | Info | `DEPLOYMENT.md:452–457`, `push-to-github.sh:34–37` | Mirror token = admin PAT with Contents+Workflows RW; design depends on bypassing GitHub protection if it ever exists | Record in §10 |
| DEP-27 | Info | three Forgejo workflows | `permissions:` kept in one, argued away in one, silently absent in one | Resolve via conflict C5 |

Verified correct: push is plain FF (non-FF diagnosed); both deploy paths ref-check; prod never on push;
already-green reads real task rows and refuses docs-only runs; `.md` stripping anchored; `secret-scan`/
`qa-artifacts` code-ungated in both copies; all SDK/runtime/workload pins agree incl. the CI image; e2e lanes each own
a dockerd.

### NATIVE (`phase1-raw/native.md`) — Med 3 · Low 4 · Info 4
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **NAT-12** | Med · Certain (leak) / Likely (S3 400) | `NativeAuthHeaderHandler.cs:17–19`, `ShareFileDownloadLauncher.cs:16`, `MauiProgram.cs:108–112` | Bearer attached to every request on the default client; the share launcher fetches the S3-presigned export URL with it → JWT to a third-party origin; SigV4 presigned + `Authorization` ⇒ S3 `InvalidArgument`; now also spends a refresh rotation | Scope the header to the API origin (shared `BearerScopedHandler` in Shared.Ui, both hosts) or a plain client for the launcher → **R102** |
| **NAT-13** | Med · Likely | `Maui.csproj:132–138`, `publish-native.ps1:52–67` | #213's Release signing fallback is Windows-only (`$(LOCALAPPDATA)`); on a Mac the v1-only trap returns; the script's `apksigner verify` is print-only and says "Send THIS file" in green; SDK probe misses the VS-installed SDK | Probe platform keystore paths; `<Error>` when none resolved; script throws unless v2/v3 true → **R103** |
| **NAT-14** | Med · Certain | both `ci.yml` (every MAUI build `-c Debug`) | R67 [machine] has no CI execution path; no test references `RequireApiBaseUrlInRelease`/`network_security_config_release`/`AndroidSigning*`; the delta doubled never-exercised Release-only logic | Dispatch/Monday `native-release-android` leg with `apksigner verify` + negative case → **R103** (= R67's machine half) |
| NAT-15 | Low · Certain | `DEPLOYMENT.md:405–406`, `NEW_APP_GUIDE.md:284–286`, `.gitignore` | Passwords as `-p:` CLI literals in §9 (NEW_APP_GUIDE says env); script has no signing params; `.gitignore` lacks `*.jks/*.keystore/*.p12/*.pfx`; `jarsigner -verify` advice contradicts v2 requirement; reverse-upgrade trap undocumented. No secret committed | Script params + env passwords; gitignore patterns; reconcile docs → **R104** |
| NAT-16 | Low · Certain | both `ci.yml` native regex | = S0-G6: `Directory\.Build\.props` missing; `tools/publish-native.ps1` matches nothing | Add; positive assertion per R60 class → **R106** |
| NAT-17 | Low · Certain | `AndroidManifest.xml:3` | `allowBackup="true"` — v3 T36 marked ✅, never landed (conflict C1) | `false` or backup rules; manifest posture gate → **R105** |
| NAT-18 | Low · Certain | `MauiProgram.cs:32,161,177`, `csproj:144–147` | API base is origin-only (path segment never supported) and nothing says/checks it; Release guard accepts `example.com` (launch crash) or `http://` on non-Android TFMs | Tighten guard to `https://` + no path; state "origin only" → **R103** negative case |
| NAT-19 | Info | `theme.js:18,41–42` | Single global watcher slot | `unwatch(ref)` |
| NAT-20 | Info | `smoke.js:104–118` | `goto` fallback masks in-app-navigation regressions on a green run | `::warning::` / fail on schedule |
| NAT-21 | Info | `NATIVE_PARITY.md`, `native.md` | Not updated for #231/#203/#213 | One dated line each |
| NAT-22 | Info | `Maui.csproj:74,77` vs `build_assets.py:30` | `#6b8a72` icon ground duplicated with no gate | Assert equality in `NativeChromeGateTests` |

Verified clean: `DebugFileSessionStore` still `#if MACCATALYST && DEBUG` at file + DI, fixed path, 0700/0600
atomic; loopback OAuth state/127.0.0.1/random port/timeout; entitlements Debug ⊂ Release, swap scoped; system bars
null-safe, main-thread, `UiMode` handled, off-Android no-op tested; smokes non-vacuous; index.html parity; brand
swap complete — **the email `logo.png` WAS updated** (28,557 → 10,004 bytes), all `build_assets.py` rasters in the delta.

### CLIENT (`phase1-raw/client.md`) — Med 4 · Low 5 · Info 4
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **UX-6** | Med · Likely | `AuthService.cs:64,199,761`, `Web/Program.cs:35`, `MauiProgram.cs:160`, `appsettings.json:26` | Unpinned invariant `refresh timeout + RenewRetryDelay ≤ ReuseGraceSeconds`: default 100 s timeout + 30 s retry ⇒ a lost-response retry presents the rotated token at ~130 s > 60 s ⇒ `Reuse` ⇒ revoke-all of every session (the symptom the pair was meant to cure); native retries from the stale stored token and the 401 deletes it | Short refresh timeout (≈20 s) + cross-project constant test → **R107**. **Human decision:** shorten client or widen grace (client is safer) |
| **UX-7** | Med · Certain | `AuthService.cs:202–208` | Any 401/400/403 = `Rejected` without reading the body; an infrastructure 403/400 (WAF, proxy limits) deletes the native refresh token, contradicting the addendum's "only the server saying no" | 401 → Rejected; 400/403 only with parsable `ErrorResponse` → **R108** |
| **UX-8** | Med · Certain | `BlazorBoot.cs:58,63–81`, `MagicLinkJourneyTests.cs:32` | Dead-boot retry re-issues the ORIGINAL navigation — a single-use magic-link verify URL is re-spent → `invalid_link` → unexplained red journey | Retry with `ReloadAsync()` of the landed URL → **R109** |
| **UX-9** | Med · Certain | `BlazorBoot.cs:22–23,71–79`, ci `:1135–1139` | Reloads bounded per navigation but never budgeted per run; `banner` (our own startup exception) retried like a network death → a 1-in-3 startup crash passes | Retry only network verdicts; fail on `banner` without a failed `_framework` fetch; per-shard threshold → **R109** (= DEP-21) |
| UX-10 | Low · Certain | `BfcacheGuardTests.cs:16–22`, `bfcache-guard.js:8–10` | Security control gated by substring presence only; no E2E for QA-SEC-03; reload-on-every-restore trade-off lives in a JS comment | E2E `SignOut_ThenBack_LandsOnLogin` (Playwright bfcache flag — Suspected) → **R110** |
| UX-11 | Low · Likely | `MainLayout.razor:128–151`, `AuthService.cs:656–668` | Mid-session `Rejected` clears prefs but never navigates to `/login`; protected page renders chromeless with every call 401ing; newly reachable via timer/request/resume | Navigate from `OnSignedOut` → **R111** |
| UX-12 | Low · Certain | `AuthService.cs:697–706,749–768` | Fixed 30 s retry forever on `Unreachable` (no backoff/cap) + one refresh per request once expired | Capped exponential backoff |
| UX-13 | Low · Certain | `AppHeader.razor:74–78`, `Billing.razor:137–141` | Serial probes, never re-probed (= BILL-7); `/billing*` bounces silently to `/` | `WhenAll` + re-read on `SignedIn`; optional toast |
| UX-17 | Low · Likely | `AuthService.cs:641–654,797–801`, `ThemeSwitcher.razor:64–71` | #233's fix is a shadow value wiped on EVERY `AcceptTokensAsync`; a keep-alive renewal in flight during the PUT reinstates the old claim on next native reload | Forget only on clear / different `sub`; gated-refresh bUnit test |
| UX-14 | Info | `Login.razor:196–206,351–358` vs `AuthErrorCopy.cs` | Web OTP maps ANY 403 to "private testing" by status alone; three mapping tables for one code. Enumeration verdict: not an oracle (refusal only after a correct code/identity) | Route through `AuthErrorCopy` → **R112** |
| UX-15 | Info | ci `:989,1094–1100`, `e2e.md:11`, QA `:168`, `CLAUDE.md:174` | 35 `[Test]` = 34 journeys + 1 `[Explicit]` native smoke; docs disagree (34 vs 35); shard filter assigns the explicit test by name (kept out only by NUnit strict mode — Suspected) | Exclude the category; derive the count → **R113**. **Human decision:** count definition |
| UX-16 | Info | `AppStrings.resx:86–87` | Cosmetic line break; six new keys EN+ES complete, no `MarkupString`, no hardcoded strings | Re-join |
| UX-18 | Info (R72 review violation) | `DECISIONS.md` vs `ThemeSwitcher.razor:67–70`, `PreferenceSyncClaimTests.cs:11–23` | #233's two accepted client-side races recorded only in code/test comments; no ADR-022 amendment (ADR-028 did get its BlazorBoot addendum) | One ADR-022 amendment paragraph |

Verified clean: keep-alive `TimeProvider` seam + `FakeTimeProvider` chassis, timers disposed on clear/impersonate/
re-arm, v3 T45c in-flight-cache fix preserved, impersonation never renewed, three paths coalesce in one instance;
per-shard services are per-job (no shared DB/Mailpit); no `[Parallelizable]`; every delta component bUnit-covered
(R70 satisfied).

### DOCS ↔ CODE / TEMPLATE / RULE HYGIENE (`phase1-raw/docs-template.md`) — High 3 · Med 7 · Low 8
| ID | Sev · Conf | Where | What | Fix / gate |
|----|-----------|-------|------|-----------|
| **TR-12** | High (docs) · Certain | lessons 8.3, 1.6, 1.4 | 8.3 has **zero** Forgejo/ADR-028/R80/dispatch/FF content although COVERAGE buckets `.forgejo/*` + `push-to-github.sh` into it; `ForgejoCiParityTests` bucketed to 1.4 (silent); LOCALCI-3 classifier taught nowhere | Phase 7: 8.3 "your own forge" section, 1.6 classifier paragraph, re-bucket → **R114/R115** |
| **TR-13** | High (docs) · Certain | `QA_TEST_PLAN.md:126–130` | §1 still says a develop merge auto-deploys staging — false under ADR-028 (dispatch-only) | Rewrite §1 → **R117** |
| **TR-14** | High (docs) · Certain | `docs/postman/README.md:28–39` | Sync secrets documented as GitHub-only; `.forgejo/workflows/postman-sync.yml` needs them on Forgejo | README + CLAUDE.md Postman block → **R117** |
| **TR-15** | Med · Certain | `FOUNDATION_RULES_v2.md:6,169–174,185`, `CONTRIBUTING.md:66–68`, `ArchitectureTests.cs:111,153` | Header/footer say "R36–R76 (final)" but R80 was appended; R77–R79 "reserved" while `R77-cand/R79-cand/R80-cand` name DIFFERENT v3 candidates (→R65/R66/R63) and localci.md defines them a third way (LOCALCI-1 superseded here); CONTRIBUTING omits R80; test comments cite candidate ids R82/R83/R86 (finals R43/R44; "R86" wrong even as a candidate) | v2.1 bump; CONTRIBUTING; fix the two comments; decide R77–R79 → conflict C8, **R116** |
| TR-16 | Med · Certain | lesson 2.2:7–14,118,138–146 | Teaches strict reuse-as-theft and quotes the REMOVED controller branch; 0 hits for grace/`RotatedAt`/`ReplacedByTokenId` | Phase 7 → **R115** |
| TR-17 | Med · Certain | lesson 3.4:99–119,266 | Quotes deleted `_refreshInFlight`/`TryRefreshAsync` model and old `theme.js`; orphans `bfcache-guard.js`, `SessionKeepAliveTests`, `BfcacheGuardTests`, `SystemBarThemeSyncTests` | Phase 7 → **R115** |
| TR-18 | Med · Certain | lesson 4.5:92 | Quotes 3-arg `ApplyExporter`; `OtlpEndpoints` bucketed but never named | Phase 7 |
| TR-19 | Med · Certain | `COVERAGE.md:662–670`, totals | Committed map stale vs generator: says 901/0 unmapped; generator 903/1 (+`deploy.yml`, `BlazorBoot.cs` unmapped) — the unbuilt R79 failure mode | Regenerate; CI gate → **R114** |
| TR-20 | Med · Certain | `FLOWS.md:160–200`, `ARCHITECTURE.md:118–182,321–335` | No `403 signup_not_allowed` branch; no `SignupGate` class; no observability section at all | Diagram updates → **R121** |
| TR-21 | Med · Certain | Postman folders 1, 6 | OTP-verify/magic-link/native-exchange never mention the signup refusal; Billing folder never mentions `Billing:Enabled ⇒ 404` (R74 checks presence only) | One sentence each → **R119** |
| TR-22 | Low · Certain | lesson 0.2:92 | Quotes `mailpit:latest` — now rejected by the R63 image-pin gate | Re-quote |
| TR-23 | Low · Certain | lesson 2.3:98 | One stale SMTP line; by-name `CancellationToken` convention absent | Re-quote |
| TR-24 | Low · Certain | `CLAUDE.md` doc map | Top-level docs all mapped; gaps outside the gate's `TopDirectoryOnly` scope: entire `docs/tutorial/`, `docs/qa-runs/`, `stories/apple-signin.md`; the course-reconcile practice exists only in the maintainer's memory; E2E count ambiguity | Doc-map rows + "Read before you act" bullet; widen gate → **R118** |
| TR-25 | Low · Certain | `.env.example:142–148` | Every IConfiguration key documented; compiled-in block misses `MaxTotalBytes`, `RenewLead`/`RenewRetryDelay`/`MaxRenewalWait`/`StartupRetryDelays` | Five bullets → **R120** |
| TR-26 | Low · Certain | `deploy.md:121` | DEPLOY-2 body still "🚧 Local half done" vs its own header ✅ | Fix |
| TR-27 | Low · Certain | `DECISIONS.md`, `localci.md:7`, `flavors.md:8` | No pointer for draft ADR-025/026; localci.md leading status "PLANNED" | Stubs; status line |
| TR-28 | Low · Certain | `QA_TEST_PLAN.md` | Phase 6 feed: no manual case for the Forgejo deploy drill / already-green / refused push, OTLP logs, #233/#230 regressions; everything else from the delta has a case; `check_qa_artifacts.py` green | Phase 6 |
| TR-29 | Low · Certain | lessons A.1, A.2, 7.4, 9.1, 5.1, 5.3, 3.6 | Remaining orphans (`AndroidSystemBarTheme`, `NativeChromeGate`, #230, #194 failed rows, `build_assets.py`, `/api/features`, billing-off copy, lanes/dead-boot) | Phase 7 checklist |

---

## Cross-cutting observations
1. **Review-only rules do not survive maintenance** — R16 (JOBS-1), R72 (UX-18), R67 (NAT-14), the R79
   tripwire (TR-19), the course-reconcile rule (TR-12..18) all slipped in the same 8 weeks. Every one has a cheap
   machine half proposed in the candidate block. This is the run's systemic finding.
2. **Coupled changes need joint invariants.** #10 + #11 (grace + keep-alive) are each correct against their own
   tests and jointly unpinned (AUTH-1, UX-6, UX-7, UX-17). A "cross-project constant" test class is the cheapest
   seam (R107).
3. **New data class on an old table** (JOBS-2) is the pattern v3's tenant-axis canary cannot see because the row
   has no `TenantId`. R91 extends the canary to nullable-TenantId tables that carry per-tenant content.
4. **The forge migration moved a trust boundary without moving its controls** (DEP-13/14/20). ADR-028 documents
   mechanics, not consequences. R98 is the forge-side equivalent of R63.

## Unknowns needing a human decision (collected; Phase 5 attaches them to batches)
1. **Grace one-shot (R81)?** Closes unbounded replay; the attacker-first ordering can only be closed by giving up the grace — the addendum should state the trade is symmetric. (AUTH-1)
2. **Absolute session lifetime** — ship a cap or accept "signed in while open"? (AUTH-4)
3. **Invitee tenant-of-one** — document as cosmetic or defer household creation? (AUTH-8)
4. **`/api/auth/refresh` rate limit** — add or rely on R81? (AUTH-1)
5. **Comp-while-off: feature or leak?** (BILL-2) and **what `Billing:Enabled=false` means for provider-managed projections** (BILL-1: gate-blind + warning / fail-closed / refuse-to-boot).
6. **Outbox retention window + scrub scope; move attachments to `IFileStorage` keys now?** (JOBS-2)
7. **Tenant-visible failure detail** — coded reason vs none (JOBS-1); **DPA note for exported logs** (OBS-1); **`AllowAutoRedirect=false` acceptable?** (JOBS-9)
8. **Was the Forgejo job token's write actually probed?** (DEP-14 severity) · **GitHub's role** (mirror vs full CI + macOS billing, DEP-17) · **where branch protection lives** (DEP-13) · **Maui lockfile on the desk** (DEP-20) · **already-green vs red smoke** (DEP-19)
9. **Native:** confirm v1-only-without-keystore on a Mac (NAT-13); MinIO presigned + Authorization behaviour (NAT-12b); keep "debug key signs Release" as the template default?
10. **Client:** shorten refresh timeout (client) or widen grace (server)? (UX-6) · bfcache reload for signed-in users? (UX-10) · E2E count definition (UX-15/TR-24)
11. **Docs:** R77–R79 fate; should the course teach Forgejo (sidebar vs section); `docs/tutorial/` in the doc map; Postman description floor enforce-or-not.
