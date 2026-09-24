# v4 Remediation — High-severity tracker (first wave)

> The High findings from `AUDIT_REPORT.md` (Phase 1), `LOGIC_AND_TEST_REPORT.md` (Phase 3) and
> `ADVERSARIAL_REPORT.md` (Phase 4), pulled out of the full plan (`AUDIT_TASKS.md`, T-numbers kept) so they can be
> worked first. Everything else stays in `IMPLEMENTATION_TRACKER.md`.
>
> **Re-verified 2026-09-24:** Forgejo `develop` is still `8c8ed4a` (the audited commit) and `src/`, `tests/`,
> `.forgejo/`, `.github/` are unchanged since the audit — every open item below still reproduces.
>
> **Workflow (unchanged from v3):** discuss → failing test first → fix → verify → mark done. One branch per wave off
> `develop`, never `main`; commit per task; **no `git push` until confirmed**; both forges reconciled after merge; the
> QA case named per item flips from Blocked to Pass in the same PR; the course lesson that quotes changed code is
> reconciled in the same PR.
>
> Status: ⬜ not started · 🔵 discussing · 🟡 in progress · ✅ done · ⏸️ waiting on a decision.

## Summary

| # | Task | Finding(s) | One line | Wave | Decision needed | Status |
|---|------|-----------|----------|------|-----------------|--------|
| H1 | T14 | DEP-13 | No branch protection on either forge; the protection script fails silently | W1 forge | #8 ✅ approved 2026-09-24 | ✅ merged 2026-09-24 (platform #19); protection applied on all 3 Forgejo repos |
| H2 | T15 | DEP-14 | Forgejo's write-capable job token is left in every CI workspace | W1 forge | #8 ✅ | ✅ merged 2026-09-24 (platform #19, y-el-vuelto #10, jigger-jot #7) |
| H3 | T1 | LB-DEP-2 | A non-ASCII file name makes the change classifier skip every gate | W1 forge | — | ✅ merged 2026-09-24 (platform #19, y-el-vuelto #10, jigger-jot #7) |
| H4 | T10 | vacuous "OtherTenant" tests | Two cross-tenant tests never seed a second tenant; no shared two-tenant helper | W2 tenancy | — | ✅ merged 2026-09-24 (platform #20, y-el-vuelto #11, jigger-jot #8) |
| H5 | T21 | LB-AUTH-5 ≡ LB-BILL-19 | Accepting an invitation deletes the old household without its contributors | W2 tenancy | — | ✅ merged 2026-09-24 (platform #20, y-el-vuelto #11, jigger-jot #8) |
| H6 | T22 | JOBS-2, ADV-P4-7 | Email outbox rows carry no tenant id and no dissolve path removes them | W2 tenancy | #6 (retention/storage) | ⏸️ |
| H7 | T23 | JOBS-2, C14 | Sent/dead outbox rows keep attachments and addresses forever | W2 tenancy | #6 | ⏸️ |
| H8 | T37 | LB-JOBS-1, LB-JOBS-2, LB-JOBS-3, JOBS-9, ADV-P4-9 | Webhook client follows redirects and records the redirect target's 200 as delivered | W3 webhooks | #7 (`AllowAutoRedirect=false`) | ⏸️ |
| H9 | T60 | TR-14 | Postman README documents the sync secrets as GitHub-only | W4 docs | — | ✅ merged 2026-09-24 (platform #20, y-el-vuelto #11, jigger-jot #8) |
| — | T60 | TR-13 | QA §1 said a develop merge auto-deploys | — | — | ✅ Phase 6 (`a9b4026`) |
| — | T61 | TR-12 | Course taught the GitHub-only pipeline; coverage map stale | — | — | ✅ Phase 7 (`aa5e990`…`9d84a1a`); the CI gate for it (R114) is Medium, stays in the main tracker |

**Decisions that unblock this wave** (full text in `PHASE5_GATE.md` §4; recommended default in brackets):
- **#6** outbox retention and attachment storage [scrub payload on Sent/Dead, purge after 30 days, keep attachments inline] — blocks H6, H7.
- **#7** webhook redirects [`AllowAutoRedirect=false`; any 3xx is a failed delivery] — blocks H8.
- **#8** forge trust [treat the token as write; protect `develop`/`main` on Forgejo now, GitHub when Pro or public] — blocks H1, H2.

## Waves (one branch each, in this order)

| Wave | Branch | Items | Why this order |
|------|--------|-------|----------------|
| W1 | `fix/v4-high-forge` | H1, H2, H3 | Every other gate fails open on a rewritten base; protect the branches and CI before landing anything else |
| W2 | `fix/v4-high-tenancy` | H4 → H5 → H6 → H7 | H4's two-tenant helper is what H5–H7's isolation tests use. H4 + H5 (with H9) landed 2026-09-24 as `fix/v4-high-h4-h5-h9`; H6 + H7 wait on #6 |
| W3 | `fix/v4-high-webhooks` | H8 | Independent; one registration change plus the permanent-failure path |
| W4 | `docs/v4-high-docs` | H9 | Docs only — rode with H4 + H5 (`fix/v4-high-h4-h5-h9`), merged 2026-09-24 |

---

## W1 — Forge trust boundary

> **2026-09-24:** implemented on `fix/v4-high-forge` in all three repos (platform: `b787fab` H3, `50ca4e9` H1,
> `90aec2f` H2; downstream: H3 + H2 ported, H1 is the platform script run against them). Forgejo protection applied
> to `develop` + `main` on perezosoft-platform, y-el-vuelto and jigger-jot (push: argamboad; required checks
> `CI / {changes,secret-scan,qa-artifacts,build-test,license-scan,docker-build,native-build,e2e} *`; admins bound).
> **Merged 2026-09-24** — platform #19, y-el-vuelto #10, jigger-jot #7; every job green on the PRs except one jigger-jot
> e2e shard. That shard, and one e2e shard in each post-merge `develop` run on platform and jigger-jot, died in
> `playwright install --with-deps` on an Ubuntu mirror mid-sync (apt size mismatch), before any test ran — unrelated to
> W1; fixed on `fix/e2e-apt-mirror-retry` in all three repos (retry with back-off). QA-DEP-04 can be re-run (expected Pass).

### H1 · T14 · DEP-13 — protect `develop` and `main` · ✅
- **Problem.** Forgejo `GET /repos/argamboad/{platform,y-el-vuelto,jigger-jot}/branch_protections` returns `[]`
  (re-checked 2026-09-24). GitHub protection needs Pro or a public repo; `tools/protect-branches.ps1` is GitHub-only and
  prints "FAILED" but exits 0.
- **Test first.** `EnforcementGateTests.ToolsScripts_RequirePwsh7_AndFailLoud` (script has `#Requires -Version 7.0`,
  `exit 1` on failure, a Forgejo half); pwsh fixture with a fake `gh`/API that fails → exit code 1.
- **Fix.** Forgejo half of the script (`POST /branch_protections` for `develop`/`main`: no force-push, no deletion,
  required status contexts = the gate job names); `exit 1` on any failure; document the GitHub limitation in
  DEPLOYMENT §10. **Operator step, confirm before running:** apply it to all three Forgejo repos.
- **Done when.** `branch_protections` lists both branches on all three repos; the script fails loud on a fake error.
- **Verify.** `curl …/branch_protections` (WSL, token file) · `dotnet test tests/Api.Tests --filter ToolsScripts`.
- **QA flips.** QA-DEP-04 Blocked → Pass. **Rules:** R98, R140.

### H2 · T15 · DEP-14 — stop leaving the write token in the workspace · ✅
- **Problem.** `.forgejo/workflows/ci.yml` has 14 `actions/checkout@v5` steps and 0 `persist-credentials`; Forgejo
  ignores `permissions:` and hands same-repo runs a write token; jobs run third-party code (unlocked MAUI restore on
  the desk runner, npm, brew).
- **Test first.** `ForgejoCiParityTests`: every checkout in `.forgejo/workflows/*` sets `persist-credentials: false`
  unless allowlisted with a reason (only the `qa-artifacts` fetch step).
- **Fix.** Add `persist-credentials: false`; give the `qa-artifacts` fetch its token through its own `env:`; add a
  `changes`-job step that reads `/branch_protections` and fails when `develop`/`main` are missing (depends on H1);
  make `permissions:` handling consistent across the three Forgejo workflows; replace `deploy.yml`'s "cannot write"
  sentence with a probe result or delete it.
- **Done when.** Parity test green; a Forgejo run on an unprotected branch fails in `changes`.
- **Verify.** `dotnet test tests/Api.Tests --filter ForgejoCiParityTests` + one Forgejo run.
- **Rules:** R98 (and the R63 amendment, conflict C5).

### H3 · T1 · LB-DEP-2 — byte-safe change classification · ✅
- **Problem.** Both classifiers run `git diff --name-only` (`.forgejo/workflows/ci.yml:409`,
  `.github/workflows/ci.yml:314`) with git's default path quoting; `src/Api/Features/Añadir.cs` prints as
  `"src/Api/Features/A\303\261adir.cs"` → `code=false native=false docs=false` → no gate runs. Probe:
  `phase3-raw/probes/p3_classifier.sh` cases 4 vs 12.
- **Test first.** `ForgejoCiParityTests` asserts `git -c core.quotePath=false diff --name-only` in both copies (fails
  today). The real-git fixture joins the shell harness later (T8, Medium).
- **Fix.** Add `-c core.quotePath=false` in both workflow copies.
- **Done when.** Parity fact green; probe case 4 classifies `code=true native=true`.
- **Verify.** `dotnet test tests/Api.Tests --filter ForgejoCiParityTests` · `bash docs/audits/v4-2026-09/phase3-raw/probes/p3_classifier.sh`.
- **Rules:** R137.

## W2 — Tenancy and erasure completeness

### H4 · T10 — a real two-tenant seed for every cross-tenant test · ✅
- **Problem.** `WebhookDeliveryLogTests.Replay_UnknownOrOtherTenant_ReturnsFalse` and `SendTest_UnknownSubscription…`
  use a random id ("unknown"), never another tenant's row; every file hand-rolls its seeding.
- **Test first.** Arch scan: a test whose name contains `OtherTenant|CrossTenant|IsTenantScoped` must call
  `TwoTenants.SeedAsync` (fails on the two tests today).
- **Fix.** `tests/Api.Tests/Infrastructure/TwoTenants.cs`; rewrite the two tests to seed tenant B's delivery and
  subscription (404 not 403; no signed ping to B's URL — TB-TEN-16/17).
- **Done when.** Scan green; both tests seed and assert a real second tenant.
- **Verify.** `dotnet test tests/Api.Tests --filter "TwoTenant|WebhookDeliveryLog"`.
- **Rules:** R146.

### H5 · T21 · LB-AUTH-5 ≡ LB-BILL-19 — accept dissolves through the dissolution service · ✅
- **Problem.** `TenantInvitationService.cs:250` calls `tenants.DeleteTenantAsync(oldTenantId)` (a raw
  `Tenants.Remove`). No contributor runs: no `billing.cancel` (Stripe keeps charging), and `ApiKeys`,
  `WebhookSubscriptions`, `UsageCounters`, `Subscription`, `AuditEvents` are orphaned. `ApiKeyService.AuthenticateAsync`
  finds the key by hash across tenants, so an orphaned key still authenticates.
- **Test first.** `Tenancy/AcceptDissolveTests.Accept_DissolvingAnEmptyTenantOfOne_WipesPlumbingRows` (TB-AUTH-20:
  seed API key + webhook subscription + usage counter + Stripe-backed projection; accept; assert 0 rows, a
  `billing.cancel` outbox row, key → no auth) and the arch scan `DeleteTenantAsync_IsCalledOnlyByTheDissolutionSequence`
  (TB-AUTH-21). Needs a `ServiceHarness.InvitationService` overload that takes contributors + the dissolution service.
- **Fix.** Inject `ITenantDissolutionService`; replace line 250 with `DissolveAsync(oldTenantId)`; delete
  `ITenantRepository.DeleteTenantAsync`. Consider (review) an `ApiKeyService` tenant-existence check as defence in depth.
- **Done when.** Both tests green; no caller of `DeleteTenantAsync` remains.
- **Verify.** `dotnet test tests/Api.Tests --filter "AcceptDissolve|DeleteTenantAsync"`.
- **QA flips.** QA-ADV-25. **Rules:** R123 (amends R43, conflict C11). **Course:** lesson on dissolve/invitations if it quotes line 250.

### H6 · T22 · JOBS-2 (stamping + dissolve) · ⏸️ decision #6
- **Problem.** `OutboxEmailSender.cs:29` enqueues with no tenant id; no contributor touches `OutboxMessages`; the
  tenant-axis canary cannot see a nullable-`TenantId` table. Phase 4 showed a dissolved tenant's document and recipient
  surviving in the outbox.
- **Test first.** TB-TEN-21 `TenantScopedEmailEnqueues_StampTenantId…` (broadcast is the one allowlisted tenant-less
  origin, by name); TB-TEN-20 / TB-JOBS-10 `Dissolve_WipesTheTenantsOutboxRows_OtherTenantsIntact` (uses H4's helper).
- **Fix.** Stamp `TenantId` from `ICurrentTenant` in `OutboxEmailSender`; add `OutboxDataContributor` (wipe pending rows,
  export nothing); extend the canary to nullable-`TenantId` tables carrying tenant content.
- **Done when.** Dissolve leaves 0 outbox rows for the tenant; the other tenant's rows are intact.
- **Verify.** `dotnet test tests/Api.Tests --filter "Outbox|Dissolve_Wipes|EveryTenantOwnedEntity"`.
- **QA flips.** QA-ADV-26 (with H7). **Rules:** R91, R145.

### H7 · T23 · JOBS-2 (retention + scrub) + C14 · ⏸️ decision #6
- **Problem.** `OutboxProcessor` flips rows to `Sent` and keeps `Payload` (up to ~13 MiB of base64 per attachment
  mail); nothing purges `Sent`/`Dead`; `ProcessedAt` is not set on dead-letter; account erasure leaves emails addressed
  to the user. The `admin.broadcast` payload is today's only attribution record for announce-all, so a scrub would erase it.
- **Test first.** TB-JOBS-11 (payload scrubbed on Sent and on Dead; retention job purges terminal rows older than
  `Outbox:RetentionDays`), TB-JOBS-8 (dead rows carry a terminal timestamp), TB-JOBS-12 (broadcast writes a system-scope
  audit row before the scrub), TB-TEN-22 (erasure removes emails addressed to the user, incl. attachment bytes).
- **Fix.** Scrub on terminal status; terminal timestamp; `OutboxRetentionJob` (default 30 days, documented in
  `.env.example`); broadcast audit row; erasure scrub.
- **Done when.** The four tests green; `.env.example` gate green.
- **Verify.** `dotnet test tests/Api.Tests --filter "Scrub|Retention|Broadcast_Writes|Erasure"`.
- **QA flips.** QA-ADV-26. **Rules:** R90 (amended by C14). **Course:** lesson 4.2 (outbox) — reconcile.

## W3 — Webhooks

### H8 · T37 · LB-JOBS-1/2/3, JOBS-9, ADV-P4-9 — no redirects, pinned connect, permanent refusals · ⏸️ decision #7
- **Problem.** `Infrastructure/ServiceCollectionExtensions.cs:87` registers the webhook client with only a timeout, so
  it follows redirects: a 301/302/303 becomes a body-less GET to a host the SSRF guard never checked, and that host's 200
  is recorded as a successful delivery (the event is never delivered, nothing retries); a 307 sends the signed body on.
  The guard resolves DNS once and the handler resolves again (rebinding window). A guard refusal is retried five times.
- **Test first.** TB-JOBS-1 `Handler_302_IsRecordedAsFailure_AndNeverFollowed` (one request only; `Success=false`,
  `StatusCode=302`; same for send-test); TB-JOBS-17 (302 to `169.254.169.254` not followed); TB-JOBS-3
  `GuardRefusal_DeadLettersOnFirstAttempt_OneDeliveryRow`; TB-JOBS-2 pinning (needs a DNS-resolver seam on
  `OutboundUrlGuard`); arch scan that the `IWebhookSender` registration sets `AllowAutoRedirect = false`.
- **Fix.** `ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = … })`
  dialing the vetted address; `OutboxPermanentFailureException` mapped straight to dead-letter for guard refusals and
  parse failures.
- **Done when.** The four tests green; a replay of a 302 delivery shows `status_code: 302`.
- **Verify.** `dotnet test tests/Api.Tests --filter "Handler_302|GuardRefusal|Sender_Connects|WebhookDelivery"`.
- **QA flips.** QA-ADV-30. **Rules:** R130 (supersedes R94), R57 amendment (C15). **Course:** lesson 7.4 — reconcile.

## W4 — Docs

### H9 · T60 (TR-14) — Postman sync documented for Forgejo · ✅
- **Problem.** `docs/postman/README.md` tells the operator to set `POSTMAN_API_KEY` / `POSTMAN_WORKSPACE_ID` on
  GitHub only; `.forgejo/workflows/postman-sync.yml` reads them on Forgejo, where `develop` changes now land. CLAUDE.md's
  Postman block says "on every develop change" without naming the forge.
- **Test first.** `EnforcementGateTests` grep: `auto-deploys` / GitHub-only sync wording outside DEPLOYMENT §10 fails.
- **Fix.** README: set the secret and variable on Forgejo (primary) and GitHub (runs at deploy time); CLAUDE.md line
  "on every Forgejo `develop` change".
- **Done when.** Gate green. **Verify.** `dotnet test tests/Api.Tests --filter DeployTriggerWording`. **Rules:** R117.

---
**3 open Highs — H6, H7, H8 (H1–H5 and H9 closed 2026-09-24; 2 more closed in Phases 6–7). All three wait on a decision:
#6 unblocks H6 and H7, #7 unblocks H8.**
