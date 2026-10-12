# v4 Delta Audit — Phase 3: Logic bugs (Part A) & test-completeness (Part B)

> Reads Phases 1–2 + the candidate rules. Four parallel auditors (auth/client session, billing/jobs, CI-script/native
> logic — with executed probes, cross-area test specs); raw reports under `phase3-raw/`, probe scripts under
> `phase3-raw/probes/`. Date 2026-09-23.

## SUMMARY
- **SHA: `8c8ed4a746c57a6df8bac5f31195f011dc0758c4`** — identical to Phases 1–2 (verified).
- **Part A — 42 distinct wrong-RESULT bugs the green 909-test suite cannot see: High 3 · Medium 16 · Low 15 ·
  Info 8** (LB-AUTH-4..7, LB-UI-11..16, LB-BILL-19..27, LB-JOBS-1..8, LB-DEP-1..11, LB-NAT-1..5; LB-BILL-19 ≡
  LB-AUTH-5, counted once). 11 CI findings are **probe-confirmed** (the shell/jq/MSBuild/PowerShell logic was
  extracted and executed). The synthesizer re-verified LB-AUTH-5 and LB-AUTH-4 in the code before headlining.
- **Headlines:** (1) **LB-AUTH-5 / LB-BILL-19 (High, Certain)** — invitation accept dissolves the joiner's empty
  tenant-of-one with a raw `Tenants.Remove`, not `ITenantDissolutionService`: no contributor runs, so a live Stripe
  subscription is never cancelled and API keys (which **still authenticate** — `ApiKeyService` never checks the
  tenant exists), webhook secrets, usage counters and audit rows are orphaned. v3's LB-TEN-1 fix wired the
  contributors into one of three dissolve flows; the canary proves entity→contributor registration, not call sites.
  (2) **LB-JOBS-1 (High, Likely)** — the webhook client follows 301/302/303 by re-issuing the POST as a body-less
  GET; a 200 from the redirect target is recorded `Success=true` and the outbox flips to `Sent` although the signed
  event was never delivered (silent false success — a different wrong result from Phase 1's JOBS-9 SSRF hop).
  (3) **LB-DEP-2 (High, Certain, probe)** — both `changes` classifiers read `git diff --name-only` with default path
  quoting; a code file with a non-ASCII name (`Añadir.cs`) matches neither `^src/` nor `^docs/` → `code=false
  native=false` → **no gate runs** for the push. None in the repo today; the downstream apps are Spanish-named.
  (4) **Logout is not final under the keep-alive** (LB-AUTH-4 + LB-UI-12, Medium): logout resolves its user via the
  live-token lookup, so a just-rotated or expired token revokes nothing and answers 200; a refresh in flight then
  re-accepts the session. (5) **The client has no session epoch and reads impersonation from an expiry-gated
  claim** (LB-UI-11/13): an expired impersonation token is silently renewed into the STAFF identity mid-page by the
  bearer handler, so the admin's next write lands in their own tenant.
- **Part B — 159 test specs** (TB-AUTH-13..30, TB-UI-17..28 + 40..71, TB-BILL-28..40, TB-JOBS-1..27, TB-TEN-10..28,
  TB-ADM-7..11, TB-OBS-1..7, TB-DOC-1..11, TB-DEP-1..11, TB-NAT-1..6), tenancy negatives first; **≈20 fail today by
  reading** (a code gap, not only a suite gap) and 5 are decision-gated. Every Phase 1 ask has a spec.
- **Harness readiness:** server strong for sequential paths (v3 verdict holds); **the same two v3 seams are still
  missing** — a shared concurrency runner and a **DB-fault injector** (blocks every fault-path spec: TB-BILL-33,
  TB-JOBS-7/22, TB-AUTH-30). New seams named: `ServiceHarness` contributors/dissolution overload, DNS-resolver seam
  on `OutboundUrlGuard`, clock-offset seam in the bUnit chassis, `TestJwt` `withNotBefore:false`, RCL HTTP stub with
  3xx/timeout, recording repository doubles, two-tenant seed helper, migration-on-data walk, gate-off E2E lane,
  **a shell-logic harness (`tests/ci-logic/`)** for the verdict blocks the C# suite only mirrors in regex, a
  `BlazorBoot` delegate seam, `smoke.js` module export, `ReleaseGuards.targets` extraction. The only scheduled-job
  test uses `NoopEmailSender`, which hides a real two-commit atomicity defect (LB-BILL-24).
- **Rules added: R123–R151** (29; appended to `FOUNDATION_RULES.md`). **Conflicts logged: C11–C24.**

---

## Part A — wrong-result findings
Confidence: Certain (code-evident / probe-executed) · Likely · Suspected. Full triggering input → wrong output →
correct → fix → test in the raw report named per block.

### Auth / session — `phase3-raw/auth-client.md`
| ID | Sev · Conf | Where | Wrong result | Fix → test/rule |
|----|-----------|-------|--------------|-----------------|
| **LB-AUTH-5** (≡ LB-BILL-19) | **High** · Certain | `TenantInvitationService.cs:249–250` → `TenantRepository.DeleteTenantAsync` | Solo owner accepts an invitation → old tenant removed with a raw `Remove`; only memberships/invitations cascade (no FK from ApiKey/WebhookSubscription/UsageCounter/Subscription/AuditEvent — ADR-003); plumbing contributors return `HasData=false` by design so the guard passes; **no `billing.cancel` enqueued** (Stripe keeps charging), orphaned API key still mints `ApiKeyAuthResult(TenantId=dead)`, encrypted webhook secrets never erased | Inject `ITenantDissolutionService`, `DissolveAsync(oldTenantId)`; delete `DeleteTenantAsync` → TB-AUTH-20/21, TB-BILL-28 → **R123** |
| **LB-AUTH-4** | Med · Certain | `AuthController.cs:236–241` | Logout resolves the user with `ValidateRefreshTokenAsync` (live only): a just-rotated (grace) or expired token → `null` → revokes nothing, cookie deleted, **200**; the in-flight refresh's `Set-Cookie`/store save restores the session (web + native); an expired token also skips "all devices" | Resolve via `InspectRefreshTokenAsync`; revoke the family whenever `Token is not null` → TB-AUTH-18/19 → **R124** |
| LB-AUTH-7 (≡ LB-BILL-20) | Med · Certain | `QuotaService.cs:31–32` ← `GetPendingForTenantAsync:25–29` | Expired pending invitations reserve seats forever (no `ExpiresAt` predicate; nothing writes `Expired`) → spurious `402 seat_limit_reached` for new invites AND for a legitimate joiner's accept; with billing off the copy says "household full" permanently | `&& i.ExpiresAt > now` → TB-AUTH-23 / TB-BILL-29 → **R127** |
| LB-AUTH-6 (≡ LB-BILL-25) | Low · Certain | repo `:52` (`>= now`) vs service `:181–184` (`<= now`) vs entity | Three validity predicates; at `now == ExpiresAt` the green-list gate admits (founds a tenant-of-one) and the accept refuses one call later — a one-instant inversion of GATES-2 | One `TenantInvitation.IsValidAt(now)` → TB-AUTH-22 → **R127** |
| **LB-UI-13** | Med · Certain | `AuthService.cs:588,697–706,803–805` | `IsImpersonating` reads an expiry-gated claim; at minute 15 the guard no longer sees impersonation → `RefreshAsync` → the **staff** token accepted silently; pre-#11 the next request 401'd visibly, post-#11 the bearer handler renews before sending → the admin's request from the target's page runs in the staff's own tenant | Impersonation as a state field; expiry raises `IdentityChanged` → TB-UI-24 → **R125** |
| LB-UI-11 | Med · Likely | `AuthService.cs:595–603,641–654` | `CancelRenewal` cannot cancel a `PostAsync` in flight; `AcceptTokensAsync` unguarded → a renewal completing during `BeginImpersonation` overwrites it (impersonation ends the instant it starts, header stale) | Session epoch → TB-UI-22 → **R125** |
| LB-UI-12 | Med · Likely | `AuthService.cs:618–668` | Client half of LB-AUTH-4: a refresh completing after `LogoutAsync` re-sets the token, re-arms the timer, raises `SignedIn`, re-writes the prefs ADM-9 wiped, re-populates the native store | Same epoch → TB-UI-23 → **R125** |
| LB-UI-14 | Med · Certain (mech.) | `AuthService.cs:709–722,818–829` vs `JwtValidation.cs:23` (`ClockSkew=Zero`) | Deadlines from the JWT `exp` on the **device** clock; `expires_in` returned and ignored; neither handler retries on 401. Slow clock ⇒ a per-hour window of 401s; fast clock ≥ lifetime ⇒ permanent anonymous shell + **30 s rotation loop** (2 880 live-successor chains/day) | Server-relative deadline from `expires_in`; one-shot refresh-and-retry on 401 → TB-UI-25/26 → **R126** |
| LB-UI-15 | Low · Likely | `AuthService.cs:714–715` vs `JwtTokenService.cs:69–74` | "lifetime/4 cap" computed from `nbf`, which production JWTs never carry → cap inert; ≤2-min tokens fall into the 30 s loop | Same as LB-UI-14 (or emit `iat`) → TB-UI-27 |
| LB-UI-16 | Low · Suspected | `Join.razor:112–117` + `AuthService.cs:158–176` | Post-accept refresh coalesces onto a timer renewal started before the accept committed → a JWT scoped to the dissolved tenant-of-one for up to an hour | `TryRefreshAsync(force:true)` → TB-UI-28 |

Verified not-bugs: signup gate under an unset/foreign ambient tenant admits correctly (memberships not scoped/RLS'd; TB-AUTH-24 pins it); accept seat re-check is seat-neutral (no TOCTOU); `WouldAbandonData` ambient-independent; issue→mark ordering crash-safe; grace successor `UserId` equality IS checked (`RefreshTokenService.cs:150`); `ReuseGraceSeconds ≤ 0` strict; legacy revoked row with null link → `Reuse`.

### Billing / jobs — `phase3-raw/billing-jobs.md`
| ID | Sev · Conf | Where | Wrong result | Fix → test/rule |
|----|-----------|-------|--------------|-----------------|
| **LB-JOBS-1** | **High** · Likely | `Infrastructure/ServiceCollectionExtensions.cs:87`, `WebhookOutboxHandler.cs:54`, `WebhookService.cs:129` | `AllowAutoRedirect=true`; .NET re-issues a POST answered by 301/302/303 as a **body-less GET**; `success = status < 300` on the final response → `Success=true`, outbox `Sent`, **event never delivered**, nothing retries; replay reproduces it; a 404/405 on the hop ⇒ 5 retries of a GET that cannot succeed | `AllowAutoRedirect=false`; any 3xx = failure → TB-JOBS-1 → **R130** (supersedes R94) |
| LB-JOBS-2 | Med · Likely | `OutboundUrlGuard.cs:28–40` + `WebhookSender.cs:35` | Guard resolves and vets, then `SocketsHttpHandler` **resolves again** and connects to whatever it gets — the TTL-0 rebinding window the docstring says is closed | `ConnectCallback` dialing the vetted address → TB-JOBS-2 (needs resolver seam) → **R130** |
| LB-JOBS-3 | Med · Certain | `WebhookSender.cs:24–25` → handler → `OutboxProcessor.cs:121–134` | A guard refusal / parse failure is a **deterministic** failure retried 5× (five delivery rows carrying the SSRF verdict, five DNS lookups against an attacker resolver, then dead-letter) | `OutboxPermanentFailureException` → dead-letter on first attempt → TB-JOBS-3 → **R130** (+ R57 amendment, C15) |
| LB-JOBS-4 | Med · Certain (arith.) | `IEmailSender.cs:36–38,61–66`, `SmtpEmailSender.cs:74` | Cap measured in **raw** bytes vs a relay limit on the **encoded** message: 10 MiB raw ⇒ ≈13.7 MiB on the wire (base64 76-char lines) ⇒ relay refuses at `DATA` ⇒ 5 retries ⇒ dead-letter — the loop the guard promises to prevent; inline images and attachment **count** uncapped | Measure the built `MimeMessage`, or derive `MaxTotalBytes` (≈7.3 MiB) and cap count → TB-JOBS-4 → **R132** |
| **LB-JOBS-7** | Med · Likely | `OutboxProcessor.cs:89–138`, `OutboxDispatcher.cs:22–41` | v3's LB-BILL-2 fix moved bookkeeping to a second transaction **with no fallback**: if it throws (the common disconnect case), the row stays `Pending` with no attempt recorded → re-claimed every 5 s, side effect (SMTP send, signed POST, broadcast) **re-executes for as long as the fault lasts**, never nearing `MaxAttempts`. Also: a successful handler whose commit fails discards its success row; `ProcessedAt` never set on `DeadLettered` | Bump `AttemptCount`/`NextAttemptAt` in the **claim** statement; terminal timestamp → TB-JOBS-7/8 → **R131** (extends R57) |
| LB-BILL-21 | Med · Likely | `StripeBillingProvider.cs:80–92` | `SubscriptionItem.CurrentPeriodEnd` is a non-nullable `DateTime` in Stripe.net 52.1.0 (reflected); a payload without it yields `0001-01-01` → paid tenant resolves **Free**, "renews on 0001-01-01" email, lapse nudge every 6 h | Treat `default` as null + Warning → TB-BILL-30 → **R128** |
| LB-BILL-22 | Med · Certain | `StripeBillingProvider.cs:81–88` | Unmapped price id → projection `free/active` with live Stripe ids: entitlements Free while Stripe charges; staff cannot comp (409 provider-managed); "Your Free plan is active" email; no log | Acknowledge-not-apply + Error log → TB-BILL-31 → **R128** |
| LB-BILL-23 | Med · Certain | `BillingWebhookHandler.cs:53–56,92–105` | A signed event for a **dissolved** tenant (its own cancel emits `subscription.deleted`) enters the tenant unconditionally, finds no projection and **inserts a new one** — Stripe ids persist forever outside every erasure path | Existence check after the inbox claim → TB-BILL-32 → **R129** |
| LB-BILL-24 | Low · Likely | `SubscriptionLapseSweepJob.cs:44–51` + `OutboxEmailSender.cs:31–34` | "notification + stamp commit together" — no transaction; the email sender's `SaveChangesAsync` autocommits the notification before the stamp; a fault on the stamp ⇒ nudge **every 6 h**; atomicity depends on the recipient's email preference; the test's `NoopEmailSender` hides it | One transaction per tenant → TB-BILL-33 (fault seam) → **R133** |
| LB-BILL-26 | Low · Certain (latent) | `QuotaService.cs:49–50` | `now.ToString("yyyy-MM")` is culture-sensitive: under `th-TH`/`ar-SA` the period key differs → one counter per calendar, cap multiplied. Not wired today (EN/ES); exactly what LOCALIZATION/FLAVORS adds | `InvariantCulture` → TB-BILL-35 → **R134** |
| LB-BILL-27 | Info · Certain | `StripeBillingProvider.cs:61–71` | Webhook leg unconstrained by R64: a test-mode `whsec_` in prod accepts test events carrying any `tenant_id`; `Event.Livemode` never read | `Livemode != ExpectLiveKey ⇒ ignore` → TB-BILL-36 → **R128** |
| LB-JOBS-5 | Low · Certain | `WebhookService.cs:89–98,161–174` | Replay of a delivery whose subscription was deleted/disabled → 202, handler returns silently, outbox `Sent`, no row — accepted replay that did nothing; deleted subscriptions' delivery bodies survive until dissolve | Join the subscription before enqueue; tombstone deliveries on delete → TB-JOBS-5 |
| LB-JOBS-8 | Low · Suspected | `OutboxProcessor.cs:122,146,148` | `Truncate` slices UTF-16 code units → a lone surrogate at index 999 → Npgsql strict UTF-8 throws inside the bookkeeping write → LB-JOBS-7's loop; Phase 1's R96 copies the same `Truncate` to two more sites; `Backoff` overflows `TimeSpan` at attempt ≥ 47 (live once `OutboxOptions` is config-bound) | Rune-safe shared helper; clamp exponent → TB-JOBS-9 → **R135** |
| LB-JOBS-6 | Info · Certain | `WebhookService.cs:120–127` | Sync test-send 10 s timeout ⇒ `delivered:false` while the receiver may have accepted — inherent; guard for the implementer: Phase 1's JOBS-6 fix must keep the `when (ct.IsCancellationRequested)` filter or every slow endpoint becomes a 500 | TB-JOBS-6 both branches |

Verified correct (do not re-hunt): activation/dunning idempotency under redelivery (inbox `ON CONFLICT DO NOTHING`
inside the handler tx); broadcast fan-out rows roll back with the claim tx; sweep runs each nudge under
`EnterTenant(sub.TenantId)`; seat re-check seat-neutral; `SeatLimit==null` unlimited / `CurrentPeriodEnd==null` never
lapses consistent everywhere; `TryConsumeAsync` conditional update + 23505-only retry correct; **no currency/rounding
arithmetic exists**; `DateTimeOffset` handling UTC-safe; `EmailOutboxPayload.Attachments` back-compat proven.

### CI scripts / native shell — `phase3-raw/ci-native.md` (probe-executed)
| ID | Sev · Conf | Where | Wrong result | Fix → test/rule |
|----|-----------|-------|--------------|-----------------|
| **LB-DEP-2** | **High** · Certain (p3) | `.forgejo/…/ci.yml:404`, `.github/…/ci.yml:314` | `git diff --name-only` with `core.quotePath=true` prints `"src/Api/Features/A\303\261adir.cs"` → matches nothing → `code=false native=false docs=false` → every gate skipped, merge run "green" with 3 jobs; `.md` strip also misses. Probe: quotePath=false classifies correctly | `git -c core.quotePath=false diff` in both copies → TB-DEP-5 → **R137** |
| LB-DEP-3 | Med · Certain (p2b) | `deploy.yml:68–73` | `-ge 2`/`-ge 3` leg floors are literals; a grown matrix (e2e shard 4, a 3rd native leg) with one red leg **deploys**; Phase 1's R98 "newest task" alone still passes it | Refuse on any non-success task with the prefix; derive counts from `ci.yml` → TB-DEP-3/10 → **R138** (refines R98) |
| LB-DEP-4 | Med · Certain (p6) | Windows + Apple boot probes, both copies (4 sites) | `Request finished.*GET.*/api/auth/providers.*200` matches the **elapsed-ms** field: a 404/500 with `200.1234ms`, or a sibling route, is a green smoke | `providers - 200 - ` in all four → TB-DEP-6 → **R139** |
| LB-DEP-1 | Med · Certain (p1) | `push-to-github.sh:45–51` | When the diagnostic fetch itself fails (mistyped `DEPLOY_MIRROR_REPO`, `.git.git`, 401, branch absent) the `else` branch prints "IS an ancestor → ruleset/token" — the operator widens a token scope when the repo name is wrong | Three-way diagnosis; strip `.git` → TB-DEP-2 |
| LB-DEP-5 | Low · Certain | shard step (both) | `set -e` + `grep -c` exiting 1 on zero matches → the shell exits **before** the `::error::only $total journeys listed` diagnosis prints | `\|\| true` → TB-DEP-7 |
| LB-DEP-6 | Low · Certain (p5) | Slowest-journeys step (both) | Zero `UnitTestResult` rows → `ZeroDivisionError` → the `always()` reporting step **reddens the job it says it never fails**; `boots = trx.count(...)` over-counts (the giving-up message repeats the attempt line) | Guard + count the deduplicated set → TB-DEP-7 (feeds R109's threshold, C19) |
| LB-DEP-7 | Low · Certain (p10) | `qa-runlog-append-only.sh:37` | A colon-aligned separator row (`|:---|`) is treated as an executed row → a later reformat is a false red on a docs PR (dormant: plan uses `|---|`) | `/^:?-+:?$/` → TB-DEP-8 |
| LB-DEP-8 | Low · Certain | `tools/*.ps1` | BOM-less UTF-8 em dashes inside strings → 8/7 parse errors under Windows PowerShell 5.1 (NEW_APP_GUIDE:81 omits `pwsh`); if ASCII-fied, `2>$null`/`2>&1` under `$ErrorActionPreference='Stop'` terminates on the first stderr byte from `gh`/`apksigner` (pwsh 7 continues) | `#Requires -Version 7.0`; BOM/ASCII → TB-DEP-9 → **R140** |
| LB-DEP-11 | Low · Suspected | `.forgejo/…/ci.yml:190–197` | QA run-log guard fetches a bare SHA on a depth-1 checkout and exits 0 on failure → R75's guard may be **silently vacuous on every Forgejo run**; job-logs API is 404 on this instance so it cannot be read back | `guard=ran|skipped` output marker; fail on PR events when skipped → TB-DEP-8 (C18) |
| LB-DEP-9 | Info · Certain | fail-open branch (both) | Sets `docs=false` on fail-open — a future docs-gated job would be skipped exactly on the "run everything" path | `docs=true` → **R143** |
| LB-DEP-10 | Info · Certain (p3) | `grep -vE '\.md$'` | `README.MD`/`.Md` not stripped → over-run only | `-i` |
| LB-NAT-1 | Low · Certain (p7) | `Maui.csproj:132,145,160` | (a) empty `LOCALAPPDATA` → `Exists()` resolves to the drive root (NAT-13's mechanism, now Certain); (b) three Release guards use **different predicates** (`== 'Release'` ×2, `!= 'Debug'`) → `-c Staging` gets the cleartext-forbidding config but **no** API-base refusal → localhost base under no-cleartext ⇒ every call fails at launch, no build error | One predicate `!= 'Debug'`; `.targets` extraction → TB-NAT-2/3 → **R141** |
| LB-NAT-2 | Low · Certain (p7) | `Maui.csproj:145` | `-p:ApiBaseUrl=" "` passes `!= ''` → `new Uri(" ")` launch crash | `Trim()` + `https://` + origin-only → TB-NAT-3 → **R141** |
| LB-NAT-3 | Info · Suspected | `theme.js:30–32` | Media listener registered before `window.appTheme` is defined → on a WebView whose `MediaQueryList` is not an `EventTarget` the IIFE throws and every interop call fails | Define first; `addListener` fallback → TB-NAT-4 → **R142** |
| LB-NAT-4 | Info · Certain | `SystemBarThemeSync` ↔ `theme.js` | **No bug** — first-render/interop ordering proven safe (synchronous head script, single layout, dispose-before-OnAfterRender); open device item: night-mode flip under `ConfigChanges.UiMode` | QA-AND device line |
| LB-NAT-5 | Info · Certain | `smoke.js:58–125` | Exit codes sound (promise chain `.catch → exit 1`; Node ≥15 crashes on unhandled rejection); `device.close()` throwing after a pass → false red | TB-NAT-6 |

---

## Part B — test-completeness (specs, not code)
159 specs. Per-spec arrange/act/assert, layer, pin and today's status are in the raw reports; this section is the index
and the verdicts. Layer key: U unit · S service (`ServiceHarness`/`PostgresFixture`) · I integration (`IntegrationTestFactory`) ·
B bUnit (`ComponentTestBase`) · A arch/gate · E E2E · H shell-logic harness.

### The Critical class — tenancy-isolation negatives (TB-TEN-10..28 + siblings)
- **Name-only negatives found:** `Replay_UnknownOrOtherTenant_ReturnsFalse` and `SendTest_UnknownSubscription…` never seed a
  second tenant (a random GUID is "unknown", not "other tenant") — vacuous on the half their names claim. → TB-TEN-16/17
  (HTTP 404-not-403 for a foreign delivery id; a foreign subscription id never receives a **signed** ping) + **R146** (two-tenant
  seed helper, arch-scanned).
- **Signup gate:** admits, never joins (TB-TEN-10); reads only the inviting tenants — recording `ITenantRepository` (TB-TEN-11);
  never consulted for an existing account — no oracle (TB-TEN-12); order-independent, refusal text names no tenant (TB-TEN-13);
  the new cross-tenant hatch under the **RLS role** sees every tenant and its untagged twin sees none (TB-TEN-14); writer↔gate
  normalization parity (TB-TEN-15); foreign ambient tenant still admits (TB-AUTH-24); RLS-role accept counts the invitation
  tenant's seats (TB-AUTH-25); `WouldAbandonData` ambient-independent (TB-AUTH-26).
- **Outbox lifecycle (JOBS-2):** dissolve wipes the tenant's rows, other tenant intact (TB-TEN-20 ❌); email enqueues stamp
  `TenantId`, broadcast is the only allowlisted tenant-less origin (TB-TEN-21 ❌, TB-ADM-11 ❌); erasure scrubs emails addressed
  to the user incl. attachment bytes (TB-TEN-22 ❌); export contains no payload/body/error (TB-TEN-23).
- **Accept-path dissolve (LB-AUTH-5):** wipes plumbing rows and the key no longer authenticates (TB-AUTH-20 ❌, TB-BILL-28 ❌);
  arch: `DeleteTenantAsync` has no caller outside the dissolution sequence (TB-AUTH-21 ❌).
- **Rotation link:** `RefreshTokens` not an RLS table, still erased, link honoured only within a user (TB-TEN-24 ✅ — the
  `UserId` check verified at `RefreshTokenService.cs:150`); predecessor whose successor row was **deleted** by the cleanup job →
  `Reuse`, no crash (TB-TEN-25); migration Up/Down **on linked rows** (TB-TEN-27 — the existing Down test walks an empty schema).
- **Webhook for a dissolved tenant creates no projection** (TB-BILL-32 ❌); **grace successor of another user → Reuse** (TB-AUTH-27 ✅).
- **Features endpoint exposes exactly `{billing}`** (TB-TEN-26); OTLP carries no tenant data (stated).

### Admin / impersonation × delta (TB-ADM-7..11)
Impersonation token refused on every ADR-021 write as a theory (TB-ADM-7); comp-while-off (TB-ADM-8 ⚖ BILL-2); staff tenant
detail while off (TB-ADM-9 ⚖ BILL-1); **webhook test/replay under impersonation are neither `RequireStaffAsync`-gated nor
audited** (TB-ADM-10 ❌ — a scope hole in R45/R50 for minimal-API writes, C21); broadcast the only tenant-less origin (TB-ADM-11).

### Auth server (TB-AUTH-13..30) and client session (TB-UI-17..28)
Third presentation inside the grace ⇒ 401 + revoke-all (TB-AUTH-13 ❌, R81); attacker-first ordering documented (TB-AUTH-14);
list normalization (TB-AUTH-15 ❌); invitee founds a tenant-of-one (TB-AUTH-16 ⚖ AUTH-8); refused OAuth deletes the external cookie
(TB-AUTH-17 ❌); **logout with the just-rotated / expired token still revokes** (TB-AUTH-18/19 ❌); expiry boundary (TB-AUTH-22 ❌);
expired invites don't reserve seats (TB-AUTH-23 ❌); erased user → 401 mints nothing (TB-AUTH-28); concurrent accepts at the cap
(TB-AUTH-29); revoke-all between inspect and issue (TB-AUTH-30 — **blocked**, fault seam). Client: hung refresh times out inside the
grace (TB-UI-17 ❌, R107); 403-with-HTML keeps the token (TB-UI-18 ❌, R108); unreachable through expiry then 401 ⇒ one `SignedOut`
(TB-UI-19 ❌, R84); mid-session `Rejected` ⇒ `/login` (TB-UI-20 ❌, R111); theme saved during a renewal survives (TB-UI-21 ❌);
**refresh in flight across impersonation / logout is discarded** (TB-UI-22/23 ❌, R125); **impersonation expiry never renews into
the staff identity** (TB-UI-24 ❌, R125); device-clock skew slow/fast (TB-UI-25/26 ❌ — clock-offset seam); server-shaped token
without `nbf` (TB-UI-27); Join's post-accept refresh is its own (TB-UI-28).

### Billing / jobs (TB-BILL-28..40, TB-JOBS-1..27)
Provider mapping theories over signed fixtures — missing period end ⇒ null, unmapped price ⇒ ignored+logged, livemode mismatch
(TB-BILL-30/31/36 ❌); lapse sweep notify+stamp in one tx with the **real** outbox sender (TB-BILL-33, fault seam); invariant period
key under `th-TH` (TB-BILL-35 ❌); gate-off × granting projection / admin comp / lapse sweep / **route table** (TB-BILL-37..40, R86);
**a gate-off E2E lane** (R147). Webhooks: 302 recorded as failure, one request only, public and private hop (TB-JOBS-1/17 ❌);
vetted-address pinning (TB-JOBS-2, resolver seam); guard refusal dead-letters on attempt 1 (TB-JOBS-3 ❌); **built message with a
10 MiB attachment fits the relay limit** (TB-JOBS-4 ❌ today ≈13.7 MiB); replay of a deleted subscription refused (TB-JOBS-5);
timeout vs caller cancellation (TB-JOBS-6); bookkeeping write fails ⇒ attempt still counted (TB-JOBS-7, fault seam); dead rows
carry a terminal timestamp (TB-JOBS-8 ❌); rune-safe truncate + clamped backoff (TB-JOBS-9); dissolve wipes outbox rows + payload
scrub + retention job (TB-JOBS-10/11 ❌); broadcast audit row before scrub (TB-JOBS-12, C14); malformed media type / filename /
1100-char error / cancelled token (TB-JOBS-13..16 ❌); log-template PII gate (TB-JOBS-18 ❌ on 8 sites); OTLP protocol from one
value (TB-JOBS-19); rate limits on test/replay (TB-JOBS-25); email enqueue inside an ambient tx does not commit (TB-JOBS-26).

### RCL pages still E2E-only (TB-UI-40..65) — the TOOL-7 set
`Household` seat-limit copy after a failed probe (TB-UI-40 ❌), invite 409/other/load errors, role matrix theory, export launches the
absolute URL (TB-UI-45 — the contract NAT-12 depends on), leave/transfer/dissolve copy; `Join` redirect not blocked behind a hung
probe (TB-UI-47 🔍); `Login` query-error table exhaustive, **403 mapped by body not status** (TB-UI-49 ⚖ R112), 429s, MFA
challenge entry; `Billing` bounded refetch loop + dispose, off ⇒ no billing request, probe-unreachable does not bounce (TB-UI-54 ❌);
`AppHeader` re-probes after a failed cold probe (TB-UI-56 ❌), admin link follows impersonation, dispose unsubscribes;
`AdminConsole` comp buttons follow the gate (TB-UI-60 ⚖), 409/204/confirm/forbidden branches; `NotificationBell` poll lifecycle
(TB-UI-65 — **blocked**: `PeriodicTimer`/`UtcNow` not clock-injected).

### E2E (TB-UI-66..71)
Billing-off lane (TB-UI-66 — blocked: both CIs run ONE API with `Billing__Enabled=true`); signup refused OTP + magic link (TB-UI-67);
invitation-owner rule two-step (TB-UI-68); keep-alive past the hour via Playwright `Page.Clock` (TB-UI-69 🔍); sign-out then Back
lands on `/login` with bfcache enabled (TB-UI-70 🔍 launch flag); theme save with a renewal in flight survives reload (TB-UI-71).
Not E2E: the Forgejo deploy drill (operator, QA §1.5), webhook replay (no UI, HOOKS-3 open).

### Config / doc / CI-shape gates (TB-DOC-1..11, TB-DEP-1..11, TB-NAT-1..6)
`[Explicit]` excluded from sharding + derived suite size (TB-DOC-1 ❌); a gate-off lane per deployment gate (TB-DOC-2 ❌); Postman
gate/refusal floor (TB-DOC-3 ❌); reflective posture (TB-DOC-4 ❌); test-id contract (TB-DOC-5); compiled-in limits (TB-DOC-6 ❌);
every path an arch test reads is `code` (TB-DOC-7 ❌); doc-map `docs/**` (TB-DOC-8 ❌); migrations Down on seeded rows (TB-DOC-9 ❌);
rule-ids cited are final (TB-DOC-10 ❌); allowlisted `TenantId` entities ship a two-tenant test via the helper (TB-DOC-11 ❌).
**TB-DEP-1 — the shell-logic harness** (`tests/ci-logic/`: verdict blocks extracted by anchor and run with fixtures from
`EnforcementGateTests` via `Process`, asserted present on the Linux leg) then TB-DEP-2..11 (push diagnosis A–F, already-green A–M
incl. red leg / red smoke / newest task, classifier positives + fail-open + non-ASCII through **real git**, provider-probe negatives,
shard/Slowest, qa-runlog, PowerShell contract with fake `gh`/`apksigner`, **BlazorBoot via a delegate seam** — retry reloads the landed
URL, `banner` without a failed `_framework` fetch throws, per-run budget). TB-NAT-1..6: MinIO presigned + bearer (settles NAT-12b),
keystore resolution on every host via a workload-free `.targets` probe (NAT-13), guard negatives table (NAT-18/LB-NAT-1/2), theme.js
`node --test`, bfcache unit + browser proof, `smoke.js` retry policy.

## Harness readiness
| Seam | Status | Blocks |
|------|--------|--------|
| Concurrency runner | still none shared (v3 gap 1); five files hand-roll `Task.WhenAll` | none directly; every new slice copies an idiom |
| **DB-fault injector** | **still none** (v3 gap 2); four private `Throwing*` doubles | TB-BILL-33, TB-JOBS-7/22, TB-AUTH-30, TB-OBS-7 — every fault-path spec |
| Injected clock — server | ✅ (`RefreshTokenService`, sweep, processor, cleanup, gate); `IntegrationTestFactory` on the real clock (SQL ageing works) | — |
| Injected clock — RCL | partial: `AuthService` ✅; `NotificationBell` ✗ (`PeriodicTimer`, `UtcNow` ×3); `Billing.razor` via a `[Parameter]` | TB-UI-65 → **R148** |
| Clock-offset seam (device vs server) | ✗ `ComponentTestBase.Time` at real now; `TestJwt` stamps `exp` from real now, always sets `nbf` | TB-UI-25/26/27 |
| Impersonation client helper | ✅ closed since v3 (`CreateImpersonatingClientFor`) | — |
| Gate-off `IntegrationTestFactory` | ✅ boots gate-off by default | — |
| Gate-off **E2E lane** | ✗ one API per e2e job, `Billing__Enabled=true`, no `Signup__*` | TB-UI-66/67/68 → **R147** |
| RCL HTTP stub | no 3xx-with-Location, no cancellation-honouring delay (`OnGated` ignores the token — `HttpClient.Timeout` can never fire) | UX-6 proof, TB-UI-47 |
| Recording repository/gate doubles | ✗ | TB-TEN-11/12 → **R151** |
| Two-tenant seed helper | ✗ every file hand-rolls | TB-DOC-11 → **R146** |
| `ServiceHarness` contributors + dissolution overload | ✗ hard-codes `[]` — why the accept-path dissolve was never exercised | TB-AUTH-20, TB-BILL-28 |
| DNS-resolver seam on `OutboundUrlGuard` | ✗ | TB-JOBS-2 |
| `IDbContextFactory` fixture | ✅ added in the delta | — |
| Migration-on-data walk | ✗ Down test walks an empty schema | TB-TEN-27, TB-DOC-9 → **R150** |
| **Shell-logic harness** | ✗ verdicts live only in YAML/scripts; C# mirrors regexes, never runs git/jq/pwsh | TB-DEP-1..10 → **R136** |
| `BlazorBoot` delegate seam / `smoke.js` export / `ReleaseGuards.targets` | ✗ | TB-DEP-11, TB-NAT-6, TB-NAT-2/3 |
| Playwright clock / bfcache flag | ✗ never used | TB-UI-69/70 |

**Verdict:** a new server slice can write every sequential tenancy negative today; **no slice can write a fault-path test**
without the injector v3 already asked for; the RCL chassis is real but the five heaviest pages are untested because nobody
wrote the tests (not because they cannot be); the E2E suite cannot prove either deployment gate in its shipped state; the
CI verdict logic is untested by construction until the shell harness exists.

## TDD invariants (rules output — appended to `FOUNDATION_RULES.md` as R144–R151)
Coupled client+server changes ship one joint-invariant test · a new data-bearing column/table ships its lifecycle spec
(dissolve/erase/export) in the same PR · every `OtherTenant` test seeds two tenants through the shared helper · a gate-off
E2E lane per deployment-config gate · `[Explicit]` excluded from sharding, suite size derived · RCL components take the
clock they schedule with · test-id contract · every migration ships a Down-on-data walk · pre-auth cross-tenant reads are
proven narrow with a recording double. Standing (v3 R7/R69/R99): no production code without the failing test; a slice
ships happy-path + permission-denied + **two-tenant** isolation; per-branch AND per-error-path coverage (the delta's
`catch` blocks in `AdminConsole`/`Household`/`Login` are the untested majority); QA plan + PDFs in the same PR (held).
