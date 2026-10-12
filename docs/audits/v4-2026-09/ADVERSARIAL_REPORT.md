# v4 Delta Audit — Phase 4: Adversarial Pass (build-a-slice)

SHA: 8c8ed4a · throwaway branch: audit/v4-2026-09-phase4-throwaway @ 7c52cc4

> Worktree `perezosoft-phase4`, never merged, never pushed. Two commits on top of the pinned code SHA:
> `7f8a5ac` (the `Reports` slice + 17 adversarial tests) and `7c52cc4` (the careless `Reports2` stub, left red on
> purpose so the gate reaction is reproducible). Reads Phases 1–2 (`AUDIT_REPORT.md`, `AUDIT_RECONCILIATION.md`),
> the candidate `FOUNDATION_RULES.md` (R81–R122) and v3's `ADVERSARIAL_REPORT.md` first; v3's proven results
> (`Projects`) are not repeated — only the gates it fixed are re-verified. Findings continue at **ADV-P4-6**.

## SUMMARY

- **Built (test-first, copying `Notes`):** `Report : ITenantScoped` in Core, `Features/Reports/` (endpoints via
  `MapTenantFeatureGroup`, handler, models, `ITenantDataContributor`, `ReportsSettings` config gate
  `Reports:Enabled`), `IEntityTypeConfiguration`, a `dotnet ef migrations add` migration with the RLS policy
  hand-appended, Program.cs wiring. The slice deliberately exercises every delta horizontal: an
  `IEmailSender` **attachment** endpoint (JOBS-4), an `IWebhookPublisher` endpoint (HOOKS), a **billing-prefixed
  minimal-API group** `/api/billing/reports` (BILL-3 probe), a `{Email}` log template (OBS-1), a `TenantId` bound
  from the request body and a raw `db.Reports` read (tenancy attacks). 17 tests under
  `tests/Api.Tests/Features/Reports/` — all green on the slice alone.
- **Forced core edits: 7 hand-edits to existing platform/test files + 1 regenerated snapshot** (+2 voluntary,
  +2 additive files in shared projects). v3 measured 3; ADR-004 (T58) says "~6". The contract grew by two members
  since v3 — both are v3's own remediation gates (the tenant-axis canary list in `ArchitectureTests`, the Postman
  collection) and neither is in ADR-004 or the WAYS_OF_WORKING checklist. **Claim 3 fails, and the documented
  number is wrong again.**
- **Gates fired (by name), bare slice:** `EveryEntity_IsDocumentedInDataModel`,
  `EveryTenantOwnedEntity_IsWiredIntoTenantDissolution`, `RlsMigrationGateTests.EveryTenantScopedTable_…` (v3
  keystone RLS-1 fix re-verified alive), `PostmanParityTests` (only once the slice is ON in the harness).
  **Did not fire although they should have:** `ConfigKeys_ReadInCode_AreDocumented` (blind to a colon-less
  `const string SectionName = "Reports"` — **ADV-P4-10**), `ConfigPostureTests` (hand-curated, S0-G8 confirmed),
  anything for `{Email}` (OBS-1 confirmed: zero resistance).
- **Gates fired, second stub slice (`Reports2`):** `RouteGroupPrefixes_AreUnique` (v3 ADV-P4-1 fix re-verified),
  `FeatureSlices_DoNotBypassTheTenantFilter` (`IgnoreQueryFilters` caught), `EveryEntity_IsDocumentedInDataModel`,
  `EveryTenantOwnedEntity_IsWiredIntoTenantDissolution`, `RlsMigrationGateTests` (policy-less migration caught —
  the v3 tautology is gone), `FeatureFolders_DoNotReferenceEachOthersNamespaces` (**false positive**, ADV-P4-12).
  **Not caught by anything:** the duplicate i18n key `Reports_Title` (**ADV-P4-11** — MSB3568 is a *warning*
  "ignored" under the warnings-as-errors build; `ResourceParityTests` green); Postman parity (the colliding path
  is already documented once). The collision itself manifests LIVE as **500 `AmbiguousMatchException` before
  authorization** (ADV-P4-13).
- **Delta findings reproduced end-to-end on a real slice (all Certain):** BILL-3 (billing-prefixed minimal-API
  group answers 200 while `/api/billing` is 404 — **ADV-P4-6**), JOBS-2 (dissolve leaves the tenant's document +
  recipient in `OutboxMessages`, `TenantId` null; export omits it — **ADV-P4-7**), JOBS-3/4 (`"pdf"` enqueues then
  explodes at MIME build; `../../x.pdf` verbatim into the MIME part — **ADV-P4-8**), JOBS-9 sharpened (302 →
  followed as **GET** to an unguarded host carrying the HMAC headers, the redirect target's 200 reported as a
  successful delivery; 307 → POST with the signed tenant body — **ADV-P4-9**).
- **Suite:** BEFORE Reports2 — Core 62/62 · **Api 784/784** · Ui 80/80 (E2E not run: no host). AFTER Reports2 —
  Core 62/62 · **Api 775 passed / 9 failed** (6 gates + 1 gate self-test collateral + 2 slice tests exposing the
  live 500) · Ui 80/80.
- **Claims:** 1 Holds · 2 Holds · 3 **Fails** · 4 Holds conditionally (routes/tables gated; **i18n fails**) ·
  5 Holds conditionally · 6 **Fails** · 7 Holds conditionally · 8 Holds conditionally.
- **Rules proposed:** R123p–R131p (9 candidates, all machine-enforceable). Conflicts: C-P4-1..4.

---

## Forced core edits (log)

Building `Reports` **by the intended mechanism only** (copy `Notes`, follow the WAYS_OF_WORKING checklist) touched
these EXISTING files outside `src/Api/Features/Reports/`:

| # | File | Edit | Forced by | In ADR-004 (T58) list? |
|---|------|------|-----------|------------------------|
| 1 | `src/Infrastructure/Persistence/AppDbContext.cs` | `DbSet<Report> Reports` | Exemplar convention (+ my raw-DbSet attack needs it) | yes |
| 2 | `src/Api/Program.cs` | bind `ReportsSettings`, `AddScoped<ReportsHandler>`, `AddScoped<ITenantDataContributor, ReportsDataContributor>`, `if (Enabled) app.MapReports()` | R8 (only Program may reference `Features.*`) | yes |
| 3 | `…/Migrations/AppDbContextModelSnapshot.cs` | regenerated by `dotnet ef` | inherent | yes (migration) |
| 4 | `…/Migrations/20260923204006_AddReportsThrowaway.cs` | **hand-append** ENABLE/FORCE RLS + policy (+ `Down`) | `RlsMigrationGateTests` fired (v3 TR-4/T58) | yes |
| 5 | `docs/DATA_MODEL.md` | `### Report` entry | `EveryEntity_IsDocumentedInDataModel` fired | **no** |
| 6 | `tests/Api.Tests/ArchitectureTests.cs` | add `nameof(Report)` to the `handled` set of the tenant-axis canary | `EveryTenantOwnedEntity_IsWiredIntoTenantDissolution` fired (v3 T3/R43) | **no** |
| 7 | `docs/postman/Perezosoft.postman_collection.json` | folder "11 · Reports" with 8 requests | `PostmanParityTests` fired — **only because the slice was switched ON in the harness** (see ADV-P4-14) | **no** |
| v1 | `.env.example` | `#Reports__Enabled=false` | **voluntary** — the config-catalog gate did NOT fire (ADV-P4-10) | (implied by R20) |
| v2 | `src/Shared.Ui/Resources/AppStrings.resx` + `.es.resx` | `Reports_Title` EN+ES | **voluntary** (parity gate fires only for EN-only) | yes (step 8) |
| a1 | `src/Core/Entities/Report.cs` | new file in Core | inherent to the layering | yes |
| a2 | `src/Infrastructure/Persistence/Configurations/ReportConfiguration.cs` | new file | exemplar | yes |
| t1 | `tests/Api.Tests/Features/Reports/ReportsGateEnv.cs` | `[ModuleInitializer]` setting `Reports__Enabled=true` process-wide | the shared harness has **no per-slice gate seam** (ADV-P4-14) | n/a |

**Count:** 7 forced hand-edits (#1–#7) + 1 regenerated file (#3 counted above) = **7 existing files edited**, vs
v3's 3 and ADR-004's "~6". WAYS_OF_WORKING step 7 ("fixture reset → add the table to the truncate list") is
**obsolete** — `PostgresFixture.ResetAsync` derives tables from the model (v2 TR-3) and nothing was edited.
Steps that exist in the code but not the checklist: DATA_MODEL entry, canary `handled` list, Postman folder.
A slice also **cannot declare its own `Permission`** — the enum + `RolePermissions` live in Core (ADR-009 coarse
capabilities); the slice reused `Permission.ExportData` for its owner-only route (ADV-P4-18, inherent, noted).

## Attacks and results

All tests live in `tests/Api.Tests/Features/Reports/` (branch `7f8a5ac`); quoted outcomes are from the runs.

### Tenancy (inherited walls)
| Attack | Test | Observed |
|--------|------|----------|
| Read another tenant's report by id; delete it; publish it | `ReportsHttpTests.CrossTenant_ReadAndDeleteById_Are404_AndTheRowSurvives` | 404 / 404 / 404 for B; A still 200; B's `/raw` list empty. **Held.** |
| Raw `db.Reports` without `IRepository`/`Where(TenantId)` | `ReportsSliceTests.Reports_AreVisibleOnlyToTheirTenant_ViaRepository_AndViaRawDbSet` | Global filter is on the model, not the repository — B sees nothing either way. **Held.** |
| Bind `TenantId` from the request body (handler honours `request.TenantId ?? tenantId`) | `ReportsSliceTests.BindingTenantIdFromTheRequestBody_IsRefusedByTheStampingInterceptor` | `InvalidOperationException` "Refusing to added a Report for tenant … while the current tenant is …"; 0 rows. **Held** (message grammar: "to added", ADV-P4-20 cosmetic). |
| `IgnoreQueryFilters` composed in a slice handler | `Reports2Handler` + `ArchitectureTests.FeatureSlices_DoNotBypassTheTenantFilter` | "Reports2Handler.cs: IgnoreQueryFilters — use IRepository<T>.QueryAllTenants() in a *DataContributor". **Caught** — by the *test* run, not `dotnet build` (the solution built clean). |
| RLS as the runtime role on the migrated `Reports` table | `ReportsHttpTests.RlsBackstop_OnTheMigratedReportsTable_FailsClosed_ForTheRuntimeRole` | `SELECT count(*)` without GUC → **0** (fail-closed); with `SET LOCAL app.tenant_id` → 1. **Held — from the migration, not a harness back-fill** (`IntegrationFactory_DoesNotBackfillRlsPolicies` green). |
| Policy-less migration (`Reports2`) | `RlsMigrationGateTests` | "Reports2: ROW LEVEL SECURITY not enabled / not FORCEd / policy 'rls_tenant_isolation' missing". **Caught** (v3 RLS-1 keystone fix verified alive on a fresh slice). |
| Forged JWT (wrong signing key, valid `tenant_id`) | `ReportsHttpTests.ForgedToken_WrongSigningKey_Is401` | 401. **Held.** (Note: a *validly signed* `tenant_id` is trusted without a per-request membership re-check — `HttpCurrentTenant` reads the claim; pre-existing ADR-002/003 design, not a slice finding.) |

### Auth / authz (inherited without re-implementation)
| Attack | Test | Observed |
|--------|------|----------|
| Unauthenticated on every route incl. `/api/billing/reports/usage` | `Unauthenticated_Is401_OnEveryRoute` | 401 ×3 — the slice wrote **no** guard. **Held.** |
| Wrong role (member) on the owner-only email route | `WrongRole_Member_Is403_OnTheEmailRoute_Owner_Is202` | 403 (`PermissionDeniedResponse`) / owner 202. **Held** via `.RequirePermission`. |
| Route collision live (after `Reports2`) | same two tests, AFTER run | **500 `InternalServerError`** for the anonymous AND the forged-token caller — `AmbiguousMatchException` is thrown by endpoint selection **before** authorization (ADV-P4-13). |

### The v4 delta boundaries
| Attack | Test | Observed |
|--------|------|----------|
| Attachment media type `"pdf"` | `Attachment_MalformedMediaType_pdf_IsEnqueued_ThenExplodesAtDispatch` | Enqueued (outbox row, `MediaType == "pdf"`); `SmtpEmailSender.BuildMessage` throws on that payload → the retry-until-dead-letter loop JOBS-3 describes. **Confirmed live (ADV-P4-8).** |
| Filename `../../x.pdf` | `Attachment_TraversalFileName_IsEnqueuedVerbatim_AndLandsInContentDisposition` | Payload `FileName == "../../x.pdf"`; `MimePart.FileName == "../../x.pdf"`. No basename reduction anywhere (JOBS-4). **Confirmed.** |
| Attachment exactly `MaxTotalBytes` / +1 | `Attachment_ExactlyMaxTotalBytes_IsEnqueued_ButOneMore_IsRejected` | Exact → enqueued (1 row); +1 → `ArgumentException(ParamName="attachments")`, 0 extra rows. **Held** — the slice inherited the limit without re-implementing it. |
| Webhook subscription URL that **302s** to the metadata stand-in | `ReportsWebhookRedirectTests.WebhookRedirect_IsFollowedToAnUnguardedHost(302,"GET","")` | The app's own `IWebhookSender` `HttpClient` followed it: the unguarded host received a **GET** carrying `X-Webhook-Signature`/event/id headers; the guard was asked **once** (`["http://127.0.0.1:{hookPort}/hook"]`); `SendAsync` returned **200** = the redirect target's answer, recorded as a successful delivery. **Confirmed (ADV-P4-9, JOBS-9).** |
| …that **307s** | same theory, `(307,"POST","{\"secret\":\"tenant-doc\"}")` | Method + the **signed tenant body** arrive at the unguarded host. **Confirmed.** |
| Tenant dissolve after enqueuing an email with an attachment | `DissolveAfterEnqueue_LeavesTheTenantsDocumentInTheOutbox_WithNoTenantId` | Tenant + `Reports` rows gone; **1 `OutboxMessages` row survives** containing the base64 document and `owner@doomed.test`; `TenantId == null` (email enqueues pass none) — no contributor could even address it. **Confirmed (ADV-P4-7, JOBS-2).** |
| GDPR tenant export after the enqueue | `TenantExport_DoesNotContainThePendingOutboxEmail` | Export contains the report body; **does not** contain the queued recipient/copy — as expected, but it means the outbox is outside export *and* erasure. **Confirmed.** |
| `OutboxDataContributor`-less dissolve | (same as above — there is no such contributor in the tree) | see JOBS-2. |
| Billing-gated sub-surface via minimal API, `Billing:Enabled` unset | `BillingPrefixedMinimalApiGroup_IsReachable_WhileBillingIsOff` | `GET /api/billing` → **404** (controller removed); `GET /api/billing/reports/usage` → **200** with a body. `BillingControllers_AreAllGated` green (it only sees controllers). **BILL-3 proven live (ADV-P4-6).** |
| `{Email}` in a log template | `ReportsHandler.EmailAsync` (`"Emailed report {ReportId} to {Email}"`) | Compiled, 0 warnings, every gate green in the 784/784 run. **Nothing stops it (OBS-1 confirmed).** |
| `ReportsSettings` posture (S0-G8) | `ConfigPostureTests` (unchanged) | Green with no line added — the new gate's default is unpinned. **Confirmed.** |
| Webhook publish through the platform publisher | `Publish_FansOutThroughTheRealPublisher_ToTheTenantsSubscriptionOnly` | 1 outbox row for the tenant's subscription, none for the other tenant's; `TenantId` stamped (webhook enqueues DO stamp it — the asymmetry with email is the JOBS-2 root). **Held.** |

## Gate reaction to the second slice

`Reports2` (commit `7c52cc4`): `Report2 : ITenantScoped` + `DbSet` + `MapTenantFeatureGroup("/api/reports")` +
`IgnoreQueryFilters()` in its handler + `dotnet ef` migration with **no** RLS DDL + **no** contributor + no
DATA_MODEL/Postman + duplicate `Reports_Title` in both resx files.

| Concern | Gate | Fired? | Quoted |
|---------|------|--------|--------|
| Route prefix reuse (R36) | `ArchitectureTests.RouteGroupPrefixes_AreUnique` | **Yes** | "Route prefixes must be unique …: /api/reports" |
| `IgnoreQueryFilters` in a slice (R5/R38) | `FeatureSlices_DoNotBypassTheTenantFilter` | **Yes** | "Reports2Handler.cs: IgnoreQueryFilters …" |
| Entity undocumented (R23) | `EveryEntity_IsDocumentedInDataModel` | **Yes** | "Entities missing from DATA_MODEL.md: Report2" |
| No contributor (R43 canary) | `EveryTenantOwnedEntity_IsWiredIntoTenantDissolution` | **Yes** | "… orphan on dissolve: Report2" |
| Policy-less migration (ADR-020) | `RlsMigrationGateTests` | **Yes** | "Reports2: … policy 'rls_tenant_isolation' missing" |
| Migrations misorder | `MigrationsTests` / startup `Migrate()` | No (correct) | `20260923204006` < `20260924002425`; both applied; the RLS probe ran on the migrated schema. Compose fine. |
| Table collision (R35) | `TenantScopedEntities_MapToDistinctTables` | No (correct) | distinct tables. |
| i18n key clash (`Reports_Title` twice, EN and ES) | `ResourceParityTests` | **No — silent** | Build: `warning MSB3568: Duplicate resource name "Reports_Title" is not allowed, ignored` in BOTH files; build "succeeded" under warnings-as-errors (MSBuild warnings are outside `TreatWarningsAsErrors`); parity green (both sets equal). First value silently wins. **ADV-P4-11.** |
| Postman parity (R74) | `PostmanParityTests` | **No** | `GET /api/reports` is already documented by the `Reports` folder — a second slice on a documented path is invisible. |
| Contributor registration (R43 DI half) | `ContributorRegistrationTests` | No (correct) | no contributor *class* exists to be unregistered; the canary is the one that fires. |
| Slice namespace isolation (R7) | `FeatureFolders_DoNotReferenceEachOthersNamespaces` | **Yes — false positive** | "Reports2Endpoints.cs, Reports2Handler.cs": the gate does `text.Contains("Perezosoft.Api.Features.Reports")`, which matches `Reports2`'s OWN namespace. **ADV-P4-12.** |
| Gate self-test | `RlsMigrationGateBitesTests.Gate_IsClean_…` | Collateral | expects exactly the one dropped policy; now also lists Reports2's three violations. Non-tautology proof, just noisy (ADV-P4-19). |
| Runtime | `ReportsHttpTests` ×2 | Collateral | `GET /api/reports` → **500** pre-auth (ADV-P4-13). |

**Suite totals.** BEFORE: Core 62/62 · Api 784/784 · Ui 80/80. AFTER: Core 62/62 · Api **775/784** (9 red: the 6
gate rows marked Yes above + `RlsMigrationGateBitesTests` + the 2 collision-500 slice tests) · Ui 80/80. E2E not
run (no host) — out of scope per brief.

## Assurance-claims table

| # | Claim | Evidence | Support | Verdict |
|---|-------|----------|---------|---------|
| 1 | No slice can access another tenant's data, even written carelessly | EF filter (repo AND raw DbSet), stamping interceptor (body-bound `TenantId`), RLS from the migration (runtime role fail-closed), RLS gate fires on a policy-less table, `IgnoreQueryFilters` scan fires | Supported | **Holds** (the v3 conditionality — "if the author remembers the policy" — is now gate-enforced and was re-proven to fire) |
| 2 | Every slice route inherits auth/authz without re-implementing it | 401 on every route incl. the billing-prefixed one; 403 via `.RequirePermission`; slice wrote zero guard code. Caveat: a prefix collision yields 500 *before* auth | Supported | **Holds** (collision 500 is a separate finding) |
| 3 | A slice can be added touching only slice code — zero core edits | 7 existing files hand-edited (+1 regenerated, +2 voluntary, +2 additive). Two members appeared since v3 (canary list, Postman) and are undocumented in ADR-004/WAYS_OF_WORKING; step 7 of the checklist is obsolete | Contradicted | **Fails** — and the documented "~6" is wrong (measured 7 forced, 9 practical) |
| 4 | A slice's migrations/config/i18n cannot collide with core or another slice | Routes: collision **caught** (R36 fixed); tables: gated; migrations: compose; config: `Reports` section documented only voluntarily (ADV-P4-10); **i18n: collides silently, no gate, warning swallowed** (ADV-P4-11) | Partially contradicted | **Holds conditionally / fails for i18n** |
| 5 | Cross-tenant paths (`EnterTenant`, impersonation, export/erasure) preserve isolation | Contributor wipe/export scoped to the target tenant; export excludes other tenants (existing suite + slice contributor tests); impersonation untouched by the delta. Completeness (not isolation) is broken by the outbox (claim 6) | Supported for isolation | **Holds conditionally** — isolation yes, completeness no |
| 6 | A slice cannot leak a tenant document or PII through the outbox/logs/webhook-error surfaces | Outbox: document + recipient survive dissolve, invisible to export (ADV-P4-7). Logs: `{Email}` passes everything (OBS-1). Webhook error surface: raw `ex.Message` persisted/returned (JOBS-1, Phase 1 — not re-tested here) | Contradicted | **Fails** |
| 7 | A config-gated surface a slice adds is removed by the gate | The slice's OWN gate works (off ⇒ its 8 routes absent from the route table — the Postman gate saw none until the env switch). But a slice surface under a *platform* gated prefix (`/api/billing/…`) is NOT removed by that platform gate (ADV-P4-6) | Partially contradicted | **Holds conditionally** — own gate yes; borrowed gated prefix no |
| 8 | A slice that emails/webhooks inherits the validation + SSRF guard without re-implementing it | Inherited: size cap, blank checks, guard on the configured URL, tenant-scoped fan-out. Inherited *as-is*: media-type/filename validation too weak (ADV-P4-8); guard bypassed by any 3xx and the redirect target's status recorded as delivery success (ADV-P4-9) | Supported (inheritance) / Contradicted (sufficiency) | **Holds conditionally** — the slice inherits exactly the platform's gaps |

## Findings (ADV-P4-6 …)

| ID | Sev · Conf | What (test) | Relates to |
|----|-----------|-------------|------------|
| **ADV-P4-6** | Med · Certain | Billing-prefixed minimal-API group reachable (200) with `Billing:Enabled` unset while `/api/billing` is 404; `BillingControllers_AreAllGated` sees only controllers (`BillingPrefixedMinimalApiGroup_IsReachable_WhileBillingIsOff`) | BILL-3 proven live → R86 must be route-table based (**R127p** sharpens it) |
| **ADV-P4-7** | High · Certain | Dissolve leaves the tenant's attachment (base64 document) + recipient in `OutboxMessages` with `TenantId = null`; export omits it (`DissolveAfterEnqueue_…`, `TenantExport_DoesNotContainThePendingOutboxEmail`) | JOBS-2 proven live → R90/R91 confirmed necessary |
| **ADV-P4-8** | Med · Certain | `"pdf"` media type enqueued then throws at `BuildMessage`; `../../x.pdf` lands verbatim in the MIME part (`Attachment_MalformedMediaType_…`, `Attachment_TraversalFileName_…`) | JOBS-3/4 proven live → R92 confirmed |
| **ADV-P4-9** | Med · Certain | The registered webhook client follows 302 (as GET, headers kept) and 307 (POST, signed body kept) to a host the guard never sees; the redirect target's 2xx is recorded as a successful delivery (`WebhookRedirect_IsFollowedToAnUnguardedHost`) | JOBS-9 proven live + a new half: **3xx must be a failure, not success** → **R129p** |
| **ADV-P4-10** | Low · Certain (NEW gate blind spot) | `ConfigKeys_ReadInCode_AreDocumented` never saw the new `Reports` section: the read is `GetSection(ReportsSettings.SectionName)` with `const string SectionName = "Reports"` — the `constKey` regex requires a colon, `sectionOnly` requires a literal. `WebhooksSettings`/`PublicApiSettings` pass only because their keys happen to be documented | R20 gap → **R124p** (extends R95) |
| **ADV-P4-11** | Med · Certain (NEW) | Cross-slice i18n key collision is silent: MSB3568 is a warning ("ignored") that the warnings-as-errors build does not promote; `ResourceParityTests` compares key SETS so duplicates vanish; the first value silently wins. WAYS_OF_WORKING step 8's "namespace your keys" is review-only | v3 §4 composability obs, now proven → **R123p** |
| **ADV-P4-12** | Low · Certain (NEW gate bug) | `FeatureFolders_DoNotReferenceEachOthersNamespaces` uses an unbounded `Contains("Perezosoft.Api.Features.<Y>")` — a slice whose name is a prefix of another (`Reports`/`Reports2`, `Order`/`Orders`) is a false positive and blocks legitimate naming | R7 gate → **R130p** |
| **ADV-P4-13** | Low · Certain (NEW) | A prefix collision boots fine and manifests per request as **500 before authorization** (anonymous and forged callers both observe it) — R36 is CI-only; nothing checks the route table at startup | R36 → **R126p** |
| **ADV-P4-14** | Low · Certain (NEW harness gap) | A config-gated slice (R53 default-off) is invisible to `PostmanParityTests` and cannot be integration-tested in the shared harness without a process-wide env var (`ReportsGateEnv`); the harness has no per-gate seam and parity enumerates only what is mapped at default posture — PUBAPI/HOOKS have the same blind spot by design | R74/R53 → **R125p** |
| ADV-P4-15 | Info (confirms S0-G8) | `ConfigPostureTests` did not force a line for `ReportsSettings` | R122 confirmed needed |
| ADV-P4-16 | Info (confirms OBS-1) | `{Email}` template passes build + every gate | R93 confirmed needed |
| **ADV-P4-17** | Low · Certain (contract drift) | Measured touchpoints 7 forced (+2 voluntary) vs ADR-004 T58 "~6"; DATA_MODEL, canary list and Postman are forced by gates but absent from the checklist; checklist step 7 (fixture reset) is obsolete | ADR-004 / WAYS_OF_WORKING → **R131p**, conflict C-P4-1 |
| ADV-P4-18 | Info (inherent) | A slice cannot declare its own `Permission` — enum + `RolePermissions` are Core (ADR-009 coarse capabilities) | document in the checklist |
| ADV-P4-19 | Info | `RlsMigrationGateBitesTests` self-test breaks whenever *any* table lacks a policy (asserts the drop-one violation list is exactly one) — noisy, not wrong | make it assert "contains" |
| ADV-P4-20 | Info (cosmetic) | Stamping-interceptor message reads "Refusing to added a Report" (`EntityState` lower-cased verbatim) | wording |

## Candidate rules

- **R123p [machine]** — Resource keys are unique per resx: a gate parses every `*.resx` and fails on a duplicate
  `data name`; MSB3568 is promoted to an error (`<MSBuildWarningsAsErrors>MSB3568</MSBuildWarningsAsErrors>` in
  `Directory.Build.props` — scoped, since a blanket `MSBuildTreatWarningsAsErrors` would trip on the MAUI
  `adb reverse` MSB3073 seen in this build); keys in the shared bundle carry a `<Feature>_` prefix
  (`ResourceParityTests` asserts the prefix set ⊆ known feature folders + platform prefixes). — ADV-P4-11.
- **R124p [machine]** — The config-catalog gate reflects over every `*Settings` class exposing `SectionName`
  (and captures single-segment `const string … = "<Section>"` literals) and requires `<Section>__…` in
  `.env.example`; a `GetSection(<Ident>.SectionName)` read counts as a read of that section. Extends R95.
  — ADV-P4-10.
- **R125p [machine]** — The integration harness exposes a per-gate seam (`IntegrationTestFactory.WithGates(...)`
  or `UseSetting("<Section>:Enabled")` per test class), and `PostmanParityTests` enumerates the route table with
  **every** reflected `*Settings.Enabled` set to true, so config-gated surfaces (platform and slice) are documented;
  a `[ModuleInitializer]` env switch in tests is banned by grep. — ADV-P4-14 (extends R74/R122).
- **R126p [machine]** — Route uniqueness is enforced at **startup**, not only in CI: an `IStartupFilter` (or a
  post-`Build()` check) enumerates `EndpointDataSource` and throws on duplicate (HTTP method, route pattern) pairs,
  so a collision never reaches a request as a pre-auth 500. R36's CI scan stays as the early signal. — ADV-P4-13.
- **R127p [machine]** — Gated-prefix ownership: with a gate bound from empty config, no endpoint of ANY kind
  (controller, minimal-API group, standalone `Map*`) whose `RoutePattern.RawText` starts with that gate's prefix
  (`/api/billing`, `/api/webhooks`, `/api/apikeys`, `/api/public`) exists in `EndpointDataSource`; a slice may not
  map under a platform-gated prefix. This is R86 stated by **prefix**, not by controller type. — ADV-P4-6.
- **R128p [machine]** — (Confirmation, no new number needed if R90/R91 land) Every `IEmailSender.SendAsync` from a
  tenant context stamps `TenantId` on the outbox row, and dissolve/erasure wipe pending rows; the R43 canary reads
  `OutboxMessages WHERE TenantId = t` after dissolve. — ADV-P4-7 (= R91's test, now with a slice-driven repro).
- **R129p [machine]** — Outbound webhook/HTTP senders set `AllowAutoRedirect = false` **and** treat any 3xx as a
  failed delivery (never the redirect target's status): `WebhookDeliveryTests` 302/307 stub asserts no second
  request and a non-2xx result. Sharpens R94. — ADV-P4-9.
- **R130p [machine]** — Source-scan gates that match a namespace or prefix use a boundary
  (`Perezosoft\.Api\.Features\.<Y>(?![A-Za-z0-9_])`), with a self-test containing the `X`/`X2` pair. — ADV-P4-12.
- **R131p [machine]** — The add-a-slice checklist is gate-verified: `DocAndConfigSyncTests` asserts
  `docs/WAYS_OF_WORKING.md` names every artifact a gate forces (`DATA_MODEL.md`, the tenant-axis `handled` list,
  the Postman collection, the RLS migration append, `.env.example` for a gated slice) and ADR-004's touchpoint count
  equals the checklist's step count; the obsolete fixture-reset step is removed. — ADV-P4-17.

## RULE_CONFLICTS candidates

- **C-P4-1** — ADR-004 (T58 amendment: "~6-touchpoint contract") and WAYS_OF_WORKING's 8-step checklist vs the
  measured 7 forced edits: two gate-forced members (canary list, Postman) are missing from both; step 7 (fixture
  reset) has been dead since v2 TR-3. The ADR should be re-amended with the measured list, not a number.
- **C-P4-2** — R53 ("every config-gated feature is closed under empty configuration, pinned") vs the hand-curated
  `ConfigPostureTests`: a slice's gate is unpinned with a green suite. Either R122 lands (reflective) or R53's text
  must say "pinned only when listed".
- **C-P4-3** — R36 ("route prefixes are unique") is a CI scan; the runtime accepts the collision and serves a
  pre-auth 500. The rule's wording implies a structural impossibility it does not provide (R126p).
- **C-P4-4** — The "0 warnings under warnings-as-errors" build claim (Phase 2, R-build hygiene) covers C# warnings
  only; MSB3568 (duplicate resource) and MSB3073 (adb reverse) pass. R72 (resx parity) is silent on duplicates while
  WAYS_OF_WORKING step 8 asserts keys "collide across slices" as a known hazard — known, unguarded.

## Unknowns

1. **Should a slice ever be allowed under a platform-gated prefix?** R127p forbids it; the alternative is making
   the billing gate prefix-based and letting slices ride it. Human decision (ties to BILL-2/BILL-3 adjudication).
2. **Pre-auth 500 on ambiguity** — operational severity: is an unauthenticated route-collision fingerprint worth
   a startup-time check (R126p), or is CI's R36 enough? (Every real collision would be caught before merge.)
3. **Harness seam design** — per-test `UseSetting` (works for `Billing:Enabled`, proven by `BillingGateTests`) vs
   a process-wide "all gates on" Postman-parity boot; the latter also documents PUBAPI/HOOKS for the first time.
4. **MSB3568 promotion scope** — confirm `MSBuildWarningsAsErrors=MSB3568` does not break the MAUI/Web resource
   pipelines (only Shared.Ui + Infrastructure carry resx today).
5. **Tenant-claim trust** — `HttpCurrentTenant` trusts a validly-signed `tenant_id` with no per-request membership
   re-check; the forged-key attack fails, but a token minted before a membership change stays valid until expiry.
   Pre-existing (ADR-002/003); flagged here only because the slice inherits it. Not re-filed.
6. JOBS-1 (raw `ex.Message` on the tenant-readable delivery row) was not re-driven through the slice — Phase 1's
   Certain stands; claim 6's verdict does not depend on it.

---
*Phase 4 complete. The slice, the stub and their tests remain on `audit/v4-2026-09-phase4-throwaway` (`7c52cc4`)
and must never be merged; only this report and the R123p–R131p candidates graduate to Phase 5.*
