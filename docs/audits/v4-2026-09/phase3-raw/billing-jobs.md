# BILLING GATE + QUOTAS + OUTBOX/EMAIL/WEBHOOKS/NOTIFY — v4 Phase 3 (Part A logic bugs · Part B specs)
SHA: 8c8ed4a · BASE: f52be3d · Read-only; nothing executed except two reflection probes on the pinned Stripe.net 52.1.0 assembly.

## SUMMARY
- **Part A: 17 new wrong-RESULT findings — High 2 · Medium 8 · Low 5 · Info 2** (Certain 10 · Likely 6 · Suspected 1).
  `LB-BILL-19..27`, `LB-JOBS-1..8`. None re-files a Phase-1 BILL-*/JOBS-*/OBS-* item or a v3 LB-BILL-1..18 / LB-TEN-1..2 item
  (grep of v2/v3/v4 audit docs for each mechanism: no prior adjudication).
- **Headline (money + tenancy):** **LB-BILL-19** — a solo owner who accepts an invitation dissolves their old tenant through
  `ITenantRepository.DeleteTenantAsync`, **not** `ITenantDissolutionService`: no contributor runs, so a live Stripe subscription is
  never cancelled and the `Subscription`/`ApiKey`/`WebhookSubscription`/`UsageCounter`/`AuditEvent` rows are orphaned (no FK,
  ADR-003). v3's LB-TEN-1 fix (T5) wired the contributors into `DissolveAsync` only; this second dissolve path was never named.
  **LB-JOBS-1** — the webhook `HttpClient` follows 301/302/303 by re-issuing the POST as a body-less **GET**; a 200 from the redirect
  target is recorded `Success=true` and the outbox flips to `Sent` although the signed event was never delivered (JOBS-9's redirect
  concern, but a *silent false success*, not the SSRF hop).
- **Quotas:** expired pending invitations reserve seats forever (LB-BILL-20, Certain); the usage period key is culture-sensitive
  (LB-BILL-26, latent). **Provider mapping:** an unmapped Stripe price projects a paying tenant as `free/active` (LB-BILL-22);
  `SubscriptionItem.CurrentPeriodEnd` is a non-nullable `DateTime` (reflected) so a payload without it yields 0001-01-01 ⇒ paid
  tenant lapses to Free + "Subscription expired" nudge (LB-BILL-21). **Tenancy:** a webhook for a dissolved tenant re-creates an
  orphan projection with the Stripe ids (LB-BILL-23). **Outbox:** a guard refusal / parse failure is retried 5× (LB-JOBS-3), the
  10 MiB cap is measured in raw bytes against a wire-size relay limit (LB-JOBS-4), attempt accounting has no fallback when its own
  transaction fails (LB-JOBS-7), DNS TOCTOU between guard and connect (LB-JOBS-2).
- **Part B: 40 specs** — `TB-BILL-28..40`, `TB-JOBS-1..27` — incl. every Phase-1 ask (BILL-1/2/3/5, JOBS-2/3/4/5/6/9, OBS-1/3,
  gate-off E2E lane). **Harness:** 31 of 40 are writable today on `ServiceHarness`/`PostgresFixture`/`IntegrationTestFactory`
  with test-local stubs; **3 missing seams** block the rest: a DB-fault injector (interceptor hook on `CreateTestContext`), a DNS
  resolver seam on `OutboundUrlGuard`, and a `ServiceHarness` overload that takes `ITenantDataContributor`s (test-only).
- **Rules proposed:** R123b–R132b. **Conflicts logged:** R90 (payload scrub) vs ADM-6 (broadcast payload = attribution record);
  R57's "accounting advances" vs LB-JOBS-3/7 (accounting advances but the *retry* is wrong / the accounting write can itself fail).
- **Brief questions answered** (§ "Answers to the brief" at the end): the 10 MiB cap is per-email total, raw bytes, `>` so exactly
  10 MiB passes, inline images excluded, `Payload` is unbounded `text`; backoff is 10 s·2^(n−1) for n=1..4 (10/20/40/80 s), dead when
  `AttemptCount >= 5` (after the 5th failure), overflow at n ≥ 47, `ProcessedAt` is **not** set on Dead; replay creates NEW rows via
  the handler, there is no attempt column at all; activation notice on a redelivered event cannot fire (inbox dedup); no currency or
  rounding arithmetic exists anywhere (confirmed).

## Part A findings
Format: ID · confidence · severity · file:line · triggering input → wrong output → correct → fix → test that catches it.
Line numbers are from the working tree at 8c8ed4a.

### LB-BILL-19 · Certain · **High** · `src/Api/Services/TenantInvitationService.cs:203-208,249-250` + `src/Infrastructure/Repositories/TenantRepository.cs:83-89`
**Trigger:** user U is the sole member/owner of tenant A, A has a `Subscription` with `StripeSubscriptionId` (active Stripe plan, or a
staff comp), no Notes; U accepts an invitation to tenant B.
**Wrong output:** `AcceptAsync` computes `dissolveOld = true` (`soloOwner && !TenantHasDataAsync`, `:204-208`; every non-Notes contributor
returns `HasDataAsync => false` by design — `BillingDataContributor.cs:22-23`, `ApiKeyDataContributor.cs:22-23`,
`UsageCounterDataContributor.cs:20-21`, `WebhookDataContributor.cs:24-25`) and then runs
```csharp
if (dissolveOld)
    await tenants.DeleteTenantAsync(oldTenantId, cancellationToken);   // :249-250
```
`DeleteTenantAsync` (`TenantRepository.cs:83-89`) removes the `Tenant` row only; the FK cascade covers `TenantInvitations`/`TenantMemberships`
alone (`AppDbContextModelSnapshot.cs:789-802` — the only two `HasOne<Tenant>` in the model). **No contributor runs:** no `billing.cancel`
outbox message (Stripe keeps charging U's card for a tenant that no longer exists), the `Subscription` row with `StripeCustomerId`/
`StripeSubscriptionId` is orphaned, as are `ApiKeys` (hashed credentials), `WebhookSubscriptions` (encrypted secrets + URLs),
`WebhookDeliveries` (bodies), `UsageCounters`, `AuditEvents`, and the `TenantInvitations` it *did* cascade were never counted.
`git log -S` shows `bb7f9ed` (foundation) — pre-existing, but `TenantInvitationService.cs` is in the delta and the path was never adjudicated
(v3 LB-TEN-1 / T3 / T5 name `TenantDissolutionService` + Leave + Erase only; `grep DeleteTenantAsync docs/audits` → only a coverage XML).
`AcceptAsync` is the **only** caller of `DeleteTenantAsync` (`grep -rn "DeleteTenantAsync(" src` → 1 call site); `LeaveAsync:211` and
`AccountErasureService:74` both go through `dissolution.DissolveAsync`.
**Correct:** the same sequence as `LeaveAsync` — `DissolveAsync(oldTenantId)` inside the accept transaction (contributor wipes + core
teardown under `EnterTenant`), so the Stripe cancel is enqueued and no row survives.
**Fix:** inject `ITenantDissolutionService` into `TenantInvitationService` (it already takes `IEnumerable<ITenantDataContributor>` for the
`HasDataAsync` half) and replace `:250` with `await dissolution.DissolveAsync(oldTenantId, ct)`; delete `ITenantRepository.DeleteTenantAsync`
so the bypass cannot recur.
**Test:** TB-BILL-28. **Gate:** R123b — arch test: zero callers of `DeleteTenantAsync` (or the method is gone); the R43 tenant-axis canary
gains an accept-path leg. Violates R43's spirit (per-tenant erasure completeness) — HIGH-value per the brief.

### LB-BILL-20 · Certain · Medium · `src/Infrastructure/Repositories/TenantInvitationRepository.cs:25-29` + `src/Api/Services/QuotaService.cs:29-33`
**Trigger:** Free tenant (cap 5), 2 members, 3 invitations created 8+ days ago (`LifespanDays` default 7) never accepted.
**Wrong output:** `GetPendingForTenantAsync` filters `Status == Pending` only:
```csharp
.Where(i => i.TenantId == tenantId && i.Status == InvitationStatuses.Pending)   // :27 — no ExpiresAt predicate
```
Nothing ever writes `InvitationStatuses.Expired` (`DATA_MODEL.md:360` "exists as a constant but is never written"; `ExpiredTokenCleanupJob.cs:21-22`
sweeps LoginTokens/RefreshTokens only). So `SeatUsage.Used = members + pending = 5` (`QuotaService.cs:31-33`): a new invite →
`CanAddSeatsAsync(1)` false → **402 `seat_limit_reached`** (`HouseholdInvitationsController.cs:41`); a valid 4th invite's accept →
`CanAddSeatsAsync(0)` false (6 > 5) → **402 for a legitimate joiner** (`TenantInvitationService.cs:220-224`); `GET /api/billing` reports
`5 of 5` (`BillingController.cs:41`). The owner's only recovery is to find and revoke the lapsed invites by hand (the roster shows them with a
past date, `Household.razor:148`). With billing off the copy says "household full" with no upgrade — permanently.
**Correct:** `DATA_MODEL.md:109-110` / `FEATURES.md:114`: `is_valid = status == pending AND !is_expired` is the derived rule; a lapsed invitation
reserves nothing.
**Fix:** seat counting reads valid invitations: `GetPendingForTenantAsync(tenantId, now)` with `&& i.ExpiresAt > now` (pass `clock` — the
repository already receives `now` for `GetValidByEmailAcrossTenantsAsync:46-54`), or count via `TenantInvitation.IsValidAt(now)`.
`GetPendingByEmailAsync:31-40` (the refresh-in-place dedup) must stay expiry-blind so a lapsed invite is refreshed rather than duplicated —
it is exempt from the quota anyway (`:104-106`).
**Test:** TB-BILL-29. **Gate:** R124b.

### LB-BILL-21 · Likely (type Certain, exposure deployment-dependent) · Medium · `src/Infrastructure/Billing/StripeBillingProvider.cs:80-92`
**Trigger:** any `customer.subscription.*` event whose `items.data[0]` lacks `current_period_end` — an endpoint/account pinned to an API
version before the field moved onto the item (the code's own comment `:80` records the move), or a Stripe fixture/`stripe trigger` on an
older version.
**Wrong output:** reflected on the pinned assembly: `Stripe.SubscriptionItem.CurrentPeriodEnd : System.DateTime` (non-nullable) and
`Stripe.Subscription` has **no** `CurrentPeriodEnd` in 52.1.0. So
```csharp
CurrentPeriodEnd: item?.CurrentPeriodEnd is { } end ? new DateTimeOffset(end, TimeSpan.Zero) : null,   // :91
```
is non-null whenever an item exists, and a missing field deserializes to `default(DateTime)` → `CurrentPeriodEnd = 0001-01-01T00:00Z`.
Downstream: `ResolvePlanKey` (`EntitlementService.cs:30-31`, `CurrentPeriodEnd > now` false) → the **active, paid** tenant resolves to Free;
`BillingNotifications.Activated` (`BillingNotifier.cs:53`) emails "It renews on 0001-01-01."; within 6 h `SubscriptionLapseSweepJob:34-38`
(`CurrentPeriodEnd < now`) sends "Subscription expired — resubscribe"; `LapseNotifiedAt` re-arms every renewal.
**Correct:** an absent period end is *unknown* → `null` (which the model already treats as "no lapse").
**Fix:** `item is { CurrentPeriodEnd: var end } && end != default ? new DateTimeOffset(end, TimeSpan.Zero) : null`, plus a Warning log.
**Test:** TB-BILL-30 (a signed fixture without the field; `Stripe.EventUtility.GenerateSigHeader`/`ComputeSignature` is available for the test).
**Gate:** R125b.

### LB-BILL-22 · Certain (code) / Likely (impact) · Medium · `src/Infrastructure/Billing/StripeBillingProvider.cs:81-88`
**Trigger:** a subscription whose price id is not in `Billing:Stripe:Prices` — a price rotated in the Dashboard, a currency/interval variant,
a coupon-created price, a second product.
**Wrong output:**
```csharp
var planKey = item?.Price?.Id is { } priceId ? _settings.PlanForPrice(priceId) : null;   // :82
    PlanKey: planKey ?? PlanKeys.Free,            // :87
    Status: MapStatus(subscription.Status),       // :88  → "active"
```
The projection becomes `free/active` with live Stripe ids: entitlements Free while Stripe charges; `IsProviderManaged` true so staff cannot
comp around it (409 `provider_managed`, `AdminController.cs:135`); the activation notice reads "Your **Free** plan is active — thank you."
(`BillingNotifier.cs:52-54`, `char.ToUpper("free")`); no log, no alert — the tenant's support ticket is the detector. Pre-existing (`be56861`),
unadjudicated (`grep PlanForPrice docs/audits` → coverage XML only).
**Correct:** an unmapped price is a configuration fault, not "Free": acknowledge and **do not apply** (return `null` → `Ignored`) and log at
Error with the price id; or apply status only and keep the previous `PlanKey`.
**Fix:** `if (planKey is null) { logger.LogError(...); return null; }` (the provider has no logger today — add one).
**Test:** TB-BILL-31. **Gate:** R125b.

### LB-BILL-23 · Certain · Medium · `src/Api/Services/BillingWebhookHandler.cs:53-56,92-105` + `src/Api/Services/BillingDataContributor.cs:25-48`
**Trigger:** tenant T with a Stripe subscription is dissolved (leave/erase — or LB-BILL-19's path). `WipeAsync` deletes the projection and
enqueues `billing.cancel`; the provider cancels and Stripe emits `customer.subscription.deleted` (and `updated`) carrying *our*
`metadata.tenant_id = T`.
**Wrong output:** the handler enters T unconditionally — `using (tenantContext.EnterTenant(evt.TenantId))` `:53` — finds no projection
(`:80`, it was wiped) and **inserts a new one** (`:92-105`) `{Status=canceled, StripeCustomerId, StripeSubscriptionId, TenantId=T}`; the inbox
claim + row commit. There is no FK to `Tenants` (snapshot) and no contributor will ever run for T again, so the dissolved tenant's Stripe
identifiers persist indefinitely, outside export/erasure. Under RLS the entered GUC makes the insert legal. The same applies to a stale
`updated` event racing the dissolve, and to a crafted `tenant_id` set by a Dashboard operator (tenants cannot set metadata; test-mode events
are signed with a different endpoint secret — see LB-BILL-27).
**Correct:** a signature-authenticated event naming a tenant that does not exist is acknowledged (`Ignored`) and never creates rows.
**Fix:** after the inbox claim, `if (await tenants.GetByIdAsync(evt.TenantId) is null) { log Warning; commit claim; return Ignored; }`.
**Test:** TB-BILL-32. **Gate:** R126b (a machine rule: every `EnterTenant(x)` where `x` comes off the wire is preceded by an existence check).

### LB-BILL-24 · Likely · Low · `src/Api/Services/SubscriptionLapseSweepJob.cs:44-51` + `src/Infrastructure/Email/OutboxEmailSender.cs:31-34`
**Trigger:** owner with email channel on (the default), a lapsed projection, and a transient DB fault after the notification is written.
**Wrong output:** the comment `:50` says "notification + stamp commit together" but the job opens **no transaction**. `NotifyOwnerAsync` →
`NotificationService.NotifyAsync:101` → `OutboxEmailSender.SendAsync:34` **`await db.SaveChangesAsync`** — on the job's shared context this is
an autocommit: the in-app row + the email outbox row are durable *before* `sub.LapseNotifiedAt = now; SaveChangesAsync` (`:48-50`). If the
second write fails, the `catch` at `:53` logs and moves on; `LapseNotifiedAt` stays null → the owner is nudged (in-app + email) **every 6 h**
until a stamp write succeeds. With the email channel off there is no intermediate save and the pair *is* atomic — the atomicity depends on
the recipient's preference. `SubscriptionLapseSweepJobTests.cs:148` wires `NoopEmailSender`, so the two-commit shape is invisible to the suite.
**Correct:** notify + stamp in one transaction per tenant (as `BillingWebhookHandler:41-66` does), or stamp first and notify second.
**Fix:** wrap the per-tenant block in `await using var tx = await unitOfWork.BeginTransactionAsync(ct); … await tx.CommitAsync(ct);`.
**Test:** TB-BILL-33 (needs the DB-fault seam for the fault half; the two-commit *shape* is provable with a `SaveChanges`-counting
interceptor — same seam). **Gate:** R130b.

### LB-BILL-25 · Certain · Low · `TenantInvitationService.cs:181-184` vs `TenantInvitationRepository.cs:52` vs `src/Core/Entities/TenantInvitation.cs:30`
**Trigger:** clock exactly at `invitation.ExpiresAt` (fake clock).
**Wrong output:** three different boundaries for one derived rule: accept says invalid (`invitation.ExpiresAt <= now`), the GATES-2 gate says
valid (`i.ExpiresAt >= now`), the entity says not expired (`now > ExpiresAt`). Sequence at the instant with the green list on: a non-listed
invitee is **admitted** by the signup gate on the strength of the invitation (`SignupGate` → `GetValidByEmailAcrossTenantsAsync`), an account
**and their own tenant** are founded (`CreateUserWithTenantAsync`), then `AcceptAsync` returns `InvalidToken` → a person the green list would
have refused now owns a household. One instant wide, but it is a real inversion of GATES-2's "the list decides who may found a household".
**Correct:** one predicate — `TenantInvitation.IsValidAt(now)` — used by all three.
**Fix:** `invitation.IsValidAt(now)` in `AcceptAsync`; `i.ExpiresAt > now` in the repository (matching `IsExpiredAt`).
**Test:** TB-BILL-34.

### LB-BILL-26 · Certain (framework) · Low (latent) · `src/Api/Services/QuotaService.cs:49-50`
**Trigger:** a request culture whose default calendar is not Gregorian (`th-TH` → Buddhist, `ar-SA` → Umm al-Qura) — not wired today
(EN/ES only) but exactly what the Flavors/Localization roadmap adds.
**Wrong output:** `var period = now.ToString("yyyy-MM");` formats with `CultureInfo.CurrentCulture`: under `th-TH` September 2026 keys as
`2569-09`, under `ar-SA` as `1448-03`; other cultures key `2026-09`. The tenant gets **one counter per calendar** → the monthly cap is
multiplied by the number of calendars its users browse in; `TryConsume_NewMonth/YearRollover` tests run under the test host's invariant-ish
culture and cannot see it.
**Correct:** an invariant, calendar-independent key.
**Fix:** `now.ToString("yyyy-MM", CultureInfo.InvariantCulture)`.
**Test:** TB-BILL-35. **Gate:** R131b (grep gate for `ToString("yyyy` without `InvariantCulture` in `src/**`).

### LB-BILL-27 · Certain · Info · `src/Infrastructure/Billing/StripeBillingProvider.cs:61-71` (mode parity of the *webhook* leg)
R64 pins the API key's mode (`ExpectLiveKey`) but the webhook secret is unconstrained and `Event.Livemode` (reflected: present) is never read: a
prod deployment configured with a **test-mode** endpoint's `whsec_` accepts test-mode events (signed correctly) that carry any `tenant_id`
the operator types in the Dashboard → real projections flipped by test events. Fix: `if (stripeEvent.Livemode != settings.ExpectLiveKey) return null;`
(acknowledge, ignore). Test: TB-BILL-36. Not a bug in the shipped default (fake provider) — filed so the R64 rule text can name both legs.

### LB-JOBS-1 · Likely · **High** · `src/Infrastructure/ServiceCollectionExtensions.cs:87` + `src/Infrastructure/Webhooks/WebhookOutboxHandler.cs:47,54` + `src/Api/Services/WebhookService.cs:122,129`
**Trigger:** a receiver that answers the signed POST with **301/302/303** (canonical-host or trailing-slash redirect, an http→https upgrade
on a subdomain, a proxy's unauthenticated 302 to a login page, a CDN's 302 during maintenance).
**Wrong output:** the client is registered with only a timeout — `AddHttpClient<IWebhookSender, WebhookSender>(c => c.Timeout = 10 s)` —
so `SocketsHttpHandler.AllowAutoRedirect` is `true`. Per .NET's redirect rules the handler re-issues a POST answered by 301/302/303 as a
**GET with no content** (307/308 keep the POST). `WebhookSender.SendAsync:35-36` returns the *final* status; `success = status is >= 200 and < 300`
(`WebhookOutboxHandler:54`, `WebhookService:129`). A 200 from the redirect target ⇒ `WebhookDelivery {Success=true, StatusCode=200}` and the
outbox message flips to `Sent` — **the event body was never delivered** and nothing will retry it. A 404/405 there ⇒ five retries of a
GET that can never succeed. The delivery log (HOOKS-2) reports the false success to the owner; replay reproduces it.
**Correct:** no auto-redirect; any 3xx is a non-2xx failure (owner sees `status_code: 302` and fixes the URL).
**Fix:** `.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })` — the same line JOBS-9 needs; the
`< 300` check then does the right thing. Builds on JOBS-9 (different wrong result: JOBS-9 = signed body to a private host; this = silent
false success to a public one).
**Test:** TB-JOBS-1. **Gate:** R127b (extends R94).

### LB-JOBS-2 · Likely · Medium · `src/Infrastructure/Http/OutboundUrlGuard.cs:12-13,28-40` + `src/Infrastructure/Webhooks/WebhookSender.cs:23-35`
**Trigger:** a subscription URL whose authoritative resolver answers a public address on one lookup and `10.0.0.5` / `169.254.169.254` on the
next (TTL 0 rebinding — the attack the docstring names).
**Wrong output:** the guard resolves and vets (`Dns.GetHostAddressesAsync(uri.Host)` `:33`), returns `true`, then `httpClient.SendAsync`
(`:35`) **resolves the host a second time** inside `SocketsHttpHandler` and connects to whatever it gets. The comment "resolving at call time
so DNS rebinding to an internal host is caught (GAP-2)" is true only for a rebind that completed *before* the call; the two-lookup window is
the rebinding attack itself. `OutboundUrlGuardTests` cover pre-resolved private literals/hosts only.
**Correct:** connect to the address the guard vetted.
**Fix:** on the same `SocketsHttpHandler`, a `ConnectCallback` that dials the vetted `IPAddress` (keep the `Host` header/SNI) — or have
`IOutboundUrlGuard` return the vetted addresses and the sender pass them; reject on any private address as today.
**Test:** TB-JOBS-2 — needs a resolver seam (see Harness readiness). **Gate:** R127b.

### LB-JOBS-3 · Certain · Medium · `src/Infrastructure/Webhooks/WebhookSender.cs:24-25` → `WebhookOutboxHandler.cs:49-52,83-90` → `src/Infrastructure/Outbox/OutboxProcessor.cs:121-134`
**Trigger:** a subscription whose host now resolves to a private range (or is unresolvable), or an email payload whose `To`/`MediaType`
fails `MailboxAddress.Parse`/`ContentType.Parse` at dispatch (JOBS-3's enqueue-side gap, from the dispatch side).
**Wrong output:** the refusal `InvalidOperationException("Refusing to send a webhook to a disallowed URL.")` is caught as a *transport*
failure (`transportError = ex.Message`), a failure row is written, the handler rethrows, and the processor schedules **four more attempts**
(10/20/40/80 s): five delivery rows carrying the SSRF verdict (JOBS-1), five DNS lookups against an attacker-chosen resolver, then dead-letter
with `LastError` = the refusal. A policy refusal is deterministic — retrying it is wrong, and the extra lookups widen LB-JOBS-2's window.
The email handler has the same shape for parse failures (5 attempts with no SMTP contact).
**Correct:** permanent failures dead-letter (or complete as a recorded no-op) on the first attempt; only transport/5xx/timeout retry.
**Fix:** an `OutboxPermanentFailureException` (Core) that `RecordFailedAttemptAsync` maps straight to `DeadLettered` (`AttemptCount = MaxAttempts`);
`WebhookSender` throws it for a guard refusal; `EmailOutboxHandler` wraps `FormatException`/`ParseException`.
**Test:** TB-JOBS-3. **Gate:** R127b (refusal = permanent) + amendment to R57 (see RULE_CONFLICTS).

### LB-JOBS-4 · Likely (relay semantics external) / Certain (arithmetic) · Medium · `src/Core/Abstractions/IEmailSender.cs:36-38,61-66` + `src/Infrastructure/Email/SmtpEmailSender.cs:74`
**Trigger:** one attachment of exactly `MaxTotalBytes` (10 485 760 raw bytes) — the boundary the suite pins as *accepted*
(`Validate_TotalExactlyAtTheLimit_IsAccepted`, `SendAsync_AttachmentsExactlyAtTheLimit_AreEnqueued`).
**Wrong output:** `Validate` sums `a.Content.LongLength` (raw) and accepts `total <= MaxTotalBytes` (`:64` uses `>`). The relay limit the
constant cites (`:36` "10 MiB, the transactional relay's (Brevo) limit") is a limit on the **encoded message**: MimeKit emits base64 in 76-char
lines, so 10 MiB raw ⇒ ≈ 13.7 MiB on the wire, plus the CID logo and HTML. The relay refuses at `DATA` (5xx size), `SmtpEmailSender:42-46`
wraps it in `EmailSendException`, and the message retries 5× over ~2.5 min and dead-letters — precisely the loop the guard promises to
prevent ("an oversize mail never sits in the outbox", ADR-007 amendment). Inline-image bytes and the attachment *count* are uncapped
(10 000 zero-byte attachments pass). The JSON row is ≈ 13.3 MiB (`Payload` is unbounded `text`, `OutboxMessageConfiguration.cs:14` — no truncation,
TOAST handles it; JOBS-2 covers the storage/lifetime side).
**Correct:** the cap is enforced in the unit the external limit uses — encoded/wire bytes of the built message.
**Fix:** either measure the built `MimeMessage` (`WriteTo` a counting stream) before enqueue and cap that at the relay limit, or set
`MaxTotalBytes` to the largest raw size whose encoding fits (≈ 7.3 MiB for a 10 MB wire cap) and document the derivation; add a count cap.
**Test:** TB-JOBS-4. **Gate:** R129b.

### LB-JOBS-5 · Certain · Low · `src/Api/Services/WebhookService.cs:89-98,161-174` + `WebhookOutboxHandler.cs:36-39`
**Trigger:** owner deletes a subscription (or it is disabled), then replays one of its past deliveries (the delivery id is in the log they
loaded earlier; rows have no FK to the subscription and `DeleteAsync` removes only the subscription).
**Wrong output:** `ReplayAsync` finds the row (`deliveries.Query()` by id + tenant), enqueues, returns `true` → **202 Accepted**. The handler
loads the subscription, finds it missing/disabled and `return`s (`:38-39`) → the outbox marks `Sent`; **no delivery row** is written. The owner
sees 202 and then nothing in the log — an accepted replay that silently did nothing. Also: a deleted subscription's deliveries (with bodies)
survive until dissolve.
**Correct:** replay of a delivery whose subscription is gone/disabled is refused (404/409), or records a "skipped: subscription disabled" row.
**Fix:** in `ReplayAsync`, join the subscription (`subscriptions.Query().AnyAsync(s => s.Id == delivery.SubscriptionId && s.DisabledAt == null)`)
before enqueueing; on `DeleteAsync`, delete (or tombstone) its deliveries.
**Test:** TB-JOBS-5.

### LB-JOBS-6 · Certain · Info · `src/Api/Services/WebhookService.cs:120-127` + `ServiceCollectionExtensions.cs:87`
The sync test-send's 10 s `HttpClient.Timeout` surfaces as `TaskCanceledException` (an `OperationCanceledException`) → caught by the generic
`catch` → `Success=false`, `Error="The request was canceled due to the configured HttpClient.Timeout…"`, owner sees `delivered:false` — while a
slow receiver may have processed the ping. That is inherent to HTTP and acceptable; the point is that Phase 1's JOBS-6 fix **must** be
`catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` (as R-text says): a bare rethrow would turn
every slow endpoint into a 500 for the owner. Pinned by TB-JOBS-6 (both branches). No new bug.

### LB-JOBS-7 · Likely · Medium · `src/Infrastructure/Outbox/OutboxProcessor.cs:89-99,107-138` + `OutboxDispatcher.cs:22-41`
**Trigger:** a transient DB fault (connection reset, failover, statement timeout) that kills the handler transaction's commit **and** the
immediately following bookkeeping transaction on the same `AppDbContext`/connection — the common case for a real disconnect.
**Wrong output:** v3's LB-BILL-2 fix moved `AttemptCount++`/backoff into a second transaction (`RecordFailedAttemptAsync`) but that
transaction has no fallback: if it throws, the exception propagates out of `ProcessNextAsync` → `OutboxDispatcher:37-41` logs and sleeps
`PollInterval` (5 s) → the row is still `Pending` with `NextAttemptAt <= now` and **no attempt recorded** → re-claimed and the side effect
(SMTP send, signed POST, broadcast fan-out) re-executes every 5 s for as long as the fault lasts, never approaching `MaxAttempts`. Two
adjacent wrong results in the same block: (a) a **successful** handler whose commit fails (`:85-86`) discards the success delivery row staged
at `WebhookOutboxHandler:79` while the receiver did accept — the log then shows one fewer attempt than happened (the retry's row is the first
visible one); (b) `ProcessedAt` is never set for `DeadLettered` (`:125`), so any retention/purge keyed on it (R90 draft) misses dead rows.
**Correct:** attempt accounting must be at least as durable as the claim itself.
**Fix:** bump in the claim: `UPDATE "OutboxMessages" SET "AttemptCount" = "AttemptCount"+1, "NextAttemptAt" = {now + lease} WHERE "Id" = (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *`
— a crash anywhere after the claim already counts; dead-letter when `AttemptCount >= MaxAttempts` at claim time; on success reset
`NextAttemptAt`. Set `ProcessedAt` on dead-letter too (or add `TerminalAt`).
**Test:** TB-JOBS-7 (needs the DB-fault seam), TB-JOBS-8 (ProcessedAt on dead). **Gate:** R128b (extends R57).

### LB-JOBS-8 · Suspected · Low · `src/Infrastructure/Outbox/OutboxProcessor.cs:122,146,148`
(a) `Truncate(cause.Message, 1000)` = `s[..1000]` slices UTF-16 code units: if chars 999–1000 are a surrogate pair (an emoji/astral char in
an exception message that quotes a URL or payload), the cut leaves a lone surrogate; Npgsql encodes text with a **strict** UTF-8 encoder
(`throwOnInvalidBytes: true`) → `EncoderFallbackException` inside `RecordFailedAttemptAsync` → LB-JOBS-7's no-accounting loop. Phase-1's R96
copies this `Truncate` to two more sites. (b) `Backoff(attempt) = BackoffBase * Math.Pow(2, attempt-1)` overflows `TimeSpan` (`OverflowException`)
at `attempt >= 47` with the 10 s base — unreachable while `MaxAttempts` is the hard-coded 5, live the day `OutboxOptions` is bound from config
("bind from configuration if/when", `OutboxOptions.cs:4-5`).
**Fix:** rune-safe truncation (`EnumerateRunes`/`Rune` boundary) in one shared helper; clamp the exponent (`Math.Min(attempt-1, 30)`).
**Test:** TB-JOBS-9. **Gate:** R132b (refines R96).

### Verified-correct paths (so Phase 5 does not re-hunt them)
- **Activation/dunning idempotency:** a redelivered `customer.subscription.updated` never reaches `MaybeNotifyActivationAsync` — `EfInbox` claims
  `(stripe, EventId)` with `INSERT … ON CONFLICT DO NOTHING` inside the handler transaction (`EfInbox.cs:20-26`, `BillingWebhookHandler.cs:41-48`);
  a *distinct* same-status event has `IsGranting(previousStatus)` true → silent. Concurrent redeliveries serialize on the unique index.
- **`AdminBroadcastOutboxHandler` idempotency claim holds:** `NotifyAsync` runs on the processor's scoped `AppDbContext`, so `OutboxEmailSender`'s
  `SaveChangesAsync` is a flush inside the claim transaction, not a commit; a crash before commit rolls every per-user row back. Per-user rows
  (`Notification`, `NotificationPreference`) carry no `TenantId`, so the system scope is the correct scope (ADR-C2). Attribution lives only in the
  payload — see RULE_CONFLICTS (R90).
- **Sweep scope:** each nudge runs under `EnterTenant(sub.TenantId)` (`:44`); `QueryAllTenants()`'s tag sets the RLS bypass GUC for the scan only.
- **Seat re-check semantics at accept** (`CanAdd(0)`, seat-neutral, `:211-224`) and `SeatUsage.CanAdd`/`AtLimit` boundaries are correct;
  `SeatLimit == null` = unlimited and `CurrentPeriodEnd == null` = never lapses are consistent across entitlement, quota, sweep and comp.
- **`TryConsumeAsync`**: `amount <= 0` allows without tracking, `amount > limit` refuses, the conditional `ExecuteUpdate` + 23505-only retry
  (LB-BILL-3 fix) is correct; `Count + amount <= limit` cannot overflow at realistic caps.
- **No currency/rounding arithmetic anywhere** — amounts never leave Stripe; `BillingSummaryResponse` carries plan/status/period/seats only.
- **`DateTimeOffset` handling:** Stripe.net's Unix converters yield `DateTime(Kind=Utc)`; `new DateTimeOffset(end, TimeSpan.Zero)` is valid for
  Utc and Unspecified kinds (the `default` case in LB-BILL-21 does not throw — it silently mis-values).
- **`EmailOutboxPayload.Attachments` back-compat** (nullable, defaulted) verified by `HandleAsync_PayloadEnqueuedBeforeAttachmentsExisted_…`.

## Part B specs
Layer key: **U** unit (`Core.Tests`/pure) · **S** service on `PostgresFixture`+`ServiceHarness` · **I** `IntegrationTestFactory` (real pipeline)
· **A** arch/gate test · **E** E2E. "Pins" = finding/rule the spec fails-for today.

### TB-BILL (from 28)
- **TB-BILL-28** `Accept_SoloOwnerWithStripeSubscription_DissolvesThroughContributors` · **S** · Arrange: tenant A (1 owner) with
  `Subscription{StripeSubscriptionId="sub_1"}`, an `ApiKey`, a `WebhookSubscription`; tenant B with a pending invite for A's owner. Act:
  `InvitationService(contributors: [Billing, ApiKey, Webhook, UsageCounter]).AcceptAsync`. Assert: `Joined`; `OutboxMessages` has one
  `billing.cancel` for `sub_1`; 0 rows in `Subscriptions/ApiKeys/WebhookSubscriptions` for A; `Tenants` has no A. Pins LB-BILL-19 / R123b.
  Needs the `ServiceHarness` contributors overload (test-only seam).
- **TB-BILL-29** `Seats_ExpiredPendingInvites_DoNotReserveSeats` · **S** · Free tenant, 2 members, `cap−2` invites with `ExpiresAt = now−1d`
  (seed rows directly), `FakeTimeProvider`. Assert `GetSeatUsageAsync().Used == 2`, `CanAddSeatsAsync(1)` true, and `AcceptAsync` of a fresh
  valid invite → `Joined`. Pins LB-BILL-20 / R124b. Sibling: `Invite_RefreshOfExpiredPending_ReusesRow_NotASeat`.
- **TB-BILL-30** `ParseWebhookEvent_ItemWithoutCurrentPeriodEnd_YieldsNullPeriod` · **U** (`StripeBillingProviderTests`) · a
  `customer.subscription.updated` JSON whose item omits `current_period_end`, signed with `EventUtility.GenerateSigHeader(payload, secret)`.
  Assert `CurrentPeriodEnd is null`, not `0001-01-01`. Pins LB-BILL-21 / R125b.
- **TB-BILL-31** `ParseWebhookEvent_UnmappedPrice_IsIgnoredNotFree` · **U** · price `price_unknown` not in `Prices`. Assert `null` (or, if the
  status-only design is chosen, `PlanKey` absent/previous) and an Error log. Pins LB-BILL-22 / R125b.
- **TB-BILL-32** `Webhook_ForUnknownTenant_IsAcknowledged_AndCreatesNoProjection` · **S** (`BillingWebhookHandlerTests` harness) ·
  `Event(tenant: Guid.CreateVersion7(), status: canceled)` with no `Tenant` row. Assert `Ignored`, `Subscriptions` count 0, inbox row exists.
  Then: dissolve a real tenant via `TenantDissolutionService`, deliver its `deleted` event → 0 rows. Pins LB-BILL-23 / R126b.
- **TB-BILL-33** `LapseSweep_NotificationAndStamp_AreOneTransaction` · **S** · wire the REAL `OutboxEmailSender(new EfOutbox(db, clock), db)`
  into `NotificationService` (not `NoopEmailSender`); inject a fault on the 2nd `SaveChanges` (seam). Assert either both the notification
  and `LapseNotifiedAt` exist or neither; second sweep after the fault clears ⇒ exactly one notification. Pins LB-BILL-24 / R130b.
- **TB-BILL-34** `InvitationExpiry_AtTheInstant_SignupGateAndAcceptAgree` · **S** · `FakeTimeProvider` at exactly `ExpiresAt`; assert
  `SignupGate.IsAllowedAsync(email)` and `AcceptAsync` give the same verdict, and `TenantInvitation.IsValidAt(now)` matches both. Pins LB-BILL-25.
- **TB-BILL-35** `TryConsume_PeriodKey_IsCalendarIndependent` · **U** (make the period key a testable `internal static`) · run under
  `CultureInfo("th-TH")` and `"ar-SA"`; assert `"2026-09"`. Pins LB-BILL-26 / R131b.
- **TB-BILL-36** `ParseWebhookEvent_LivemodeMismatch_IsIgnored` · **U** · `livemode:false` with `ExpectLiveKey=true` ⇒ `null`. Pins LB-BILL-27.
- **TB-BILL-37** `GateOff_GrantingProjection_StillResolvesPro_AndWarnsAtStartup` · **I** (`BillingGateTests`, factory boots gate-off) · seed
  `Subscription{pro, active, StripeSubscriptionId}` → `IEntitlementService.HasAsync(ProFeature)` true (pins whichever BILL-1 semantics the human
  picks — write the assertion for the chosen one) and a startup Warning log line exists when `!Enabled && any provider row`. Pins BILL-1.
- **TB-BILL-38** `GateOff_AdminComp_<Gated404|DocumentedAllowed>` · **I** · staff `PUT /api/admin/tenants/{id}/subscription {plan_key:pro}` with
  the gate off ⇒ 404 (decision a) or 200 + `AdminConsole` bUnit shows the read-only state (decision b). Pins BILL-2 / R86.
- **TB-BILL-39** `GateOff_LapseSweep_DoesNotNudge` · **S** · `BillingSettings{Enabled=false}` passed to the job (add the ctor param); lapsed
  row ⇒ 0 notifications, stamp untouched. Pins BILL-5.
- **TB-BILL-40** `GateOff_RouteTable_HasNoBillingEndpoint` · **A/I** · `factory.Services.GetRequiredService<EndpointDataSource>().Endpoints`:
  none with `RoutePattern.RawText` starting `api/billing`, none whose `ControllerActionDescriptor.ControllerTypeInfo.Namespace` contains
  `Billing`; run for PUBAPI/HOOKS prefixes too. Pins BILL-3 / R86.
- **Gate-off E2E lane** (Phase-1 ask; not numbered as a test): a 4th `e2e` matrix entry in BOTH `ci.yml` copies with `Billing__Enabled: "false"`
  running `BillingGateJourneyTests` (header has no Billing link; `/billing` bounces; invite at cap shows the no-upgrade copy — QA-GATE-01..03).
  R80 parity keeps the two copies together.

### TB-JOBS (from 1)
- **TB-JOBS-1** `Handler_302_IsRecordedAsFailure_AndNeverFollowed` · **S** (`WebhookDeliveryLogTests`) · a `RedirectingStubHandler` returning
  `302 Location: https://public.example/other` and recording every request. Assert exactly **one** request (POST, body present),
  `delivery.Success == false`, `StatusCode == 302`, outbox `Pending` (retry) — and the same for `SendTestAsync` (`delivered:false, status_code:302`).
  Fails today (a 2nd GET is issued; with a 200 stub on the hop the row says success). Pins LB-JOBS-1 / R127b.
- **TB-JOBS-2** `Sender_ConnectsToTheVettedAddress_NotASecondResolution` · **U** · resolver seam returns `[93.184.216.34]` on the 1st call and
  `[10.0.0.5]` on the 2nd; assert the connect callback dials `93.184.216.34` (or the send is refused). Pins LB-JOBS-2 / R127b. **Needs seam.**
- **TB-JOBS-3** `GuardRefusal_DeadLettersOnFirstAttempt_OneDeliveryRow` · **S** · `DenyAllUrlGuard`; run the real `OutboxProcessor` twice.
  Assert 1 delivery row, message `DeadLettered` after pass 1, `AttemptCount == MaxAttempts` (or a `PermanentFailure` marker). Sibling
  `EmailHandler_UnparseableTo_DeadLettersImmediately`. Pins LB-JOBS-3.
- **TB-JOBS-4** `BuiltMessage_WithMaxTotalBytesAttachment_FitsTheRelayLimit` · **U** (`SmtpMessageBuilderTests`) · one 10 MiB attachment +
  the CID logo; `message.WriteTo(countingStream)`; assert `length <= 10 * 1024 * 1024`. **Fails today** (~13.7 MiB). Plus
  `Validate_ManyEmptyAttachments_IsRejected` (count cap) and `Validate_InlineImages_CountTowardTheLimit`. Pins LB-JOBS-4 / R129b.
- **TB-JOBS-5** `Replay_OfDeletedOrDisabledSubscription_IsRefused` · **S** · seed delivery, delete the subscription, `ReplayAsync` ⇒ `false`
  (or a recorded skipped row); no outbox message. Pins LB-JOBS-5.
- **TB-JOBS-6** `SendTest_HttpClientTimeout_IsDeliveredFalse_NotAThrow` + `SendTest_CallerCancellation_Propagates_NoRow` · **S** · a stub that
  delays past a 50 ms client timeout vs a pre-cancelled `ct`. Pins JOBS-6 (both branches) / LB-JOBS-6.
- **TB-JOBS-7** `Processor_BookkeepingWriteFails_StillCountsTheAttempt` · **S** · fault the 2nd transaction's `SaveChanges` (seam); assert the
  row's `AttemptCount` advanced (claim-time bump) and the handler is not re-invoked within `Backoff(1)`. Pins LB-JOBS-7 / R128b. **Needs seam.**
- **TB-JOBS-8** `DeadLettered_HasATerminalTimestamp` · **S** · after `MaxAttempts` failures assert `ProcessedAt`/`TerminalAt` not null. Pins
  LB-JOBS-7(b); a precondition for R90's purge spec.
- **TB-JOBS-9** `Truncate_IsRuneSafe` (`U`, 999 ASCII + "😀" + more ⇒ no lone surrogate, length ≤ 1000) + `Backoff_ClampsAtLargeAttempts`
  (`attempt=60` does not throw). Pins LB-JOBS-8 / R132b.
- **TB-JOBS-10** `Dissolve_WipesTheTenantsOutboxRows` · **S** · enqueue an email (stamped with `tenantId` after the JOBS-2 fix) and a webhook
  for T; `DissolveAsync(T)` ⇒ `OutboxMessages WHERE TenantId = T` empty, other tenant's rows intact. Pins JOBS-2 / R91. (Today: email rows
  have no `TenantId` — the spec first asserts `OutboxEmailSender` stamps it: `SendAsync_StampsTheCurrentTenant`.)
- **TB-JOBS-11** `Processor_OnSent_ScrubsPayload` + `Processor_OnDead_ScrubsPayload` + `OutboxRetentionJob_PurgesTerminalRowsOlderThanRetention`
  · **S** · Pins JOBS-2 / R90. Note the R90 conflict (broadcast attribution) — the scrub spec must first assert an audit row exists for
  `admin.broadcast` (`TB-JOBS-12`).
- **TB-JOBS-12** `AdminBroadcast_WritesASystemScopeAuditRow_BeforePayloadScrub` · **S** · Pins the R90/ADM-6 conflict resolution.
- **TB-JOBS-13** `SendAsync_MalformedMediaType_IsRejected_AndNothingIsEnqueued` · **U+S** theory over `"pdf"`, `"application/pdf; charset="`,
  `"a b/c"`, `"text/plain\r\nX: 1"` ⇒ `ArgumentException`, 0 outbox rows. Pins JOBS-3 / R92.
- **TB-JOBS-14** `Validate_FileName_IsReducedToASafeBasename` · **U** · `"../../etc/passwd"` ⇒ `passwd`; `"C:\\x\\y.pdf"` ⇒ `y.pdf`;
  control chars rejected; 256+ chars rejected. Pins JOBS-4 / R92.
- **TB-JOBS-15** `Handler_1100CharError_IsTruncatedAndPersisted` · **S** · `ThrowingHandler` with a 1100-char message (and a variant with an
  astral char at index 999) ⇒ delivery row exists, `Error.Length <= 1000`; same for `SendTestAsync`. Pins JOBS-5 / R96 + LB-JOBS-8.
- **TB-JOBS-16** `Handler_PreCancelledToken_Propagates_AndWritesNoRow` · **S** · Pins JOBS-6.
- **TB-JOBS-17** `Handler_302ToPrivateHost_IsNotFollowed` · **S** · same `RedirectingStubHandler` as TB-JOBS-1 with `Location: http://169.254.169.254/`;
  assert one request only. Pins JOBS-9 / R94 (shares the fix with LB-JOBS-1).
- **TB-JOBS-18** `LogTemplates_NeverCarryPii` · **A** · regex over `src/**` for `\{(Email|To|Token|Code|Otp|Secret|Password|Body)\}` inside
  `Log(Trace|Debug|Information|Warning|Error|Critical)\(`; comment allowlist. Fails today on the 8 OBS-1 sites. Pins OBS-1 / R93.
- **TB-JOBS-19** `OtlpProtocol_ResolvedFromTheSameValueThePathUses` · **U** (`TelemetryLogsExportTests`) · `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`
  in `IConfiguration` only ⇒ resolved `OtlpExporterOptions.Protocol == HttpProtobuf` for traces/metrics/logs. Pins OBS-3 / R95.
- **TB-JOBS-20** `ConfigCatalog_CapturesFlatIndexerReads` · **A** · Pins OBS-3 / R95.
- **TB-JOBS-21** `DbContextFactory_ResolvesInsideAScope` · **I** · Pins JOBS-10.
- **TB-JOBS-22** `Handler_SuccessThenCommitFault_RetryProducesOneSuccessRow_AndLogsTheGap` · **S** · seam; documents LB-JOBS-7(a).
- **TB-JOBS-23** `Enqueue_WebhookFromSystemScope_HasNoTenant_FansOutNothing` · **S** · `WebhookPublisher` under a null tenant ⇒ 0 rows (pins the
  scope answer for R91's arch scan).
- **TB-JOBS-24** `Sweep_NudgesUnderTheSubscriptionsTenant_NotTheCallersAmbient` · **S** · two lapsed tenants, ambient = a third ⇒ each owner
  gets exactly one notification (pins the EnterTenant scope; extends `OwnerlessLapsedTenant_…`).
- **TB-JOBS-25** `WebhookTest_And_Replay_AreRateLimited` · **I** · 11th POST in a minute ⇒ 429. Pins JOBS-7.
- **TB-JOBS-26** `OutboxEmail_SendAsync_InsideAnAmbientTransaction_DoesNotCommit` · **S** · begin tx, `SendAsync`, roll back ⇒ 0 rows (pins the
  flush-vs-commit distinction LB-BILL-24 depends on).
- **TB-JOBS-27** `Notify_SecurityKind_FromSystemScope_ReachesBothChannels` · **S** · `AdminBroadcastOutboxHandler` under a null tenant with a user
  whose prefs are both off and kind `security.*` ⇒ in-app + email (R46 under the broadcast scope).

## Harness readiness
**Reusable today (no new seams):** `PostgresFixture` (+`CreateTestContext(ICurrentTenant)` so `EnterTenant` drives the filter),
`ServiceHarness.InvitationService()/QuotaService()`, `TestCurrentTenant` (implements `ITenantContext`), `FakeTimeProvider`
(`Microsoft.Extensions.Time.Testing`, already used by the sweep/processor tests), `Fixture.CreateContextFactory()` for the out-of-band
recorder, `FakeBillingProvider` (any `BillingWebhookEvent` JSON with the literal signature), `RecordingHandler`/`ThrowingHandler`/
`StagesBadRowHandler` (`OutboxProcessorTests`), `StubHandler(HttpStatusCode)`/`ThrowingHandler`/`AllowAllUrlGuard` (`WebhookDeliveryLogTests`,
class-private — promote to `tests/Api.Tests/Infrastructure`), `RecordingEmailSender` (`OutboxEmailTests`), `IntegrationTestFactory` +
`WithWebHostBuilder(b => b.UseSetting("Billing:Enabled", …))` (gate on/off boot proven by `BillingGateTests`), `EndpointDataSource` from
`factory.Services`, Stripe.net's `EventUtility.GenerateSigHeader` for signed fixtures.
**Writable now:** TB-BILL-29, 30, 31, 32, 34, 35, 36, 37, 38, 39, 40; TB-JOBS-1, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21,
23, 24, 25, 26, 27 (31 of 40). TB-JOBS-1/17 need only a `RedirectingStubHandler` (status + `Location` + request log) — test-local.
**Missing seams (block the rest):**
1. **DB-fault injector** — an `IInterceptor` hook on `PostgresFixture.CreateTestContext` (`.AddInterceptors(faultOnNthSaveChanges)` /
   `IDbCommandInterceptor` throwing `NpgsqlException` on a chosen command). Blocks TB-BILL-33, TB-JOBS-7, TB-JOBS-22 — the same seam v3 named
   (#2) and never built; still the single biggest gap for fault-path specs in this area.
2. **DNS resolver seam** on `OutboundUrlGuard` (`Func<string, CancellationToken, Task<IPAddress[]>>` or an `IDnsResolver`) and, for the pinning
   fix, a `SocketsHttpHandler` factory the test can observe. Blocks TB-JOBS-2. Production seam (small).
3. **`ServiceHarness` overload taking `IEnumerable<ITenantDataContributor>` (+ `ITenantDissolutionService`)** for `InvitationService()` — today
   it hard-codes `[]` (`ServiceHarness.cs:73`), which is exactly why the accept-path dissolve has never been exercised with a contributor.
   Blocks TB-BILL-28. Test-only.
4. (Nice-to-have) an `IEmailSender` fake that **throws `EmailSendException`** for LB-JOBS-3's email sibling — trivial, test-local.
5. (Nice-to-have) `BillingSettings` injectable into `SubscriptionLapseSweepJob` — production ctor change that TB-BILL-39 needs (the fix itself).
**Harness verdict in one line:** the v3 assessment "server-side harness strong" still holds for *sequential happy/negative* paths; every
fault-path spec in this area (5 of the 9 blocked) waits on the same DB-fault interceptor, and the scheduled-job test's `NoopEmailSender`
actively hides the two-commit shape (R130b makes the real outbox sender mandatory there).

## Candidate rules
- **R123b-cand — [machine]** — Every tenant-deleting code path goes through `ITenantDissolutionService`; `ITenantRepository.DeleteTenantAsync`
  is removed (or an arch test asserts zero callers outside `TenantDissolutionService`/`WipeDataAsync`), and the R43 tenant-axis canary includes
  the invitation-accept solo-dissolve leg (seed a Stripe-backed projection, accept, assert cancel enqueued + 0 rows). — `ArchitectureTests`
  caller scan + TB-BILL-28. — LB-BILL-19.
- **R124b-cand — [machine]** — A "pending invitation" read that feeds a decision (seat count, accept re-check, listing to the owner) uses the
  derived validity predicate (`IsValidAt(now)` / `ExpiresAt > now`); the seat-usage test seeds an expired pending invite and asserts it does
  not count. — `QuotaServiceTests` + `TenantInvitationRepository` contract test. — LB-BILL-20, LB-BILL-25.
- **R125b-cand — [machine]** — Provider-mapped fields fail safe **and loud**: an unmapped price, an absent/`default` period end, or a livemode
  mismatch never becomes a projection value; the event is acknowledged-not-applied with an Error/Warning log naming the field.
  — `StripeBillingProviderTests` theories over signed fixtures. — LB-BILL-21, LB-BILL-22, LB-BILL-27.
- **R126b-cand — [machine]** — `ITenantContext.EnterTenant(x)` where `x` originates off the wire (provider metadata, a token's payload) is
  preceded by a tenant-existence check in the same transaction; an event for an unknown tenant creates no rows. — `BillingWebhookHandlerTests`
  + arch scan of `EnterTenant(` call sites for a preceding `GetByIdAsync`/`ExistsAsync`. — LB-BILL-23.
- **R127b-cand — [machine]** — Outbound HTTP to user-supplied URLs: `AllowAutoRedirect = false`, the connection is made to the address the
  guard vetted (connect-callback pinning), any 3xx is a failed delivery, and a guard refusal is a **permanent** failure that never retries.
  Supersedes/extends R94. — `ArchitectureTests` scan of the `IWebhookSender` registration + TB-JOBS-1/2/3/17. — LB-JOBS-1, LB-JOBS-2, LB-JOBS-3, JOBS-9.
- **R128b-cand — [machine]** — Outbox attempt accounting is durable independent of the handler and of any second transaction: the attempt
  counter and next-attempt time advance in the **claim** statement, dead-lettering is decided at claim time, and terminal rows carry a
  terminal timestamp. Extends R57. — `OutboxProcessorTests` with the fault seam (TB-JOBS-7/8). — LB-JOBS-7.
- **R129b-cand — [machine]** — A limit that mirrors an external cap is checked in the external cap's unit: the email size test builds the
  MIME message and measures wire bytes; `MaxTotalBytes` documents its derivation. — `SmtpMessageBuilderTests` (TB-JOBS-4). — LB-JOBS-4.
- **R130b-cand — [machine]** — A scheduled job that notifies and records that it notified does both in one explicit transaction; scheduled-job
  and notifier tests wire the real outbox-backed `IEmailSender`, never a no-op sender. — `SubscriptionLapseSweepJobTests` (TB-BILL-33) + a
  grep gate: no `NoopEmailSender` in `tests/**/*Job*Tests.cs`. — LB-BILL-24.
- **R131b-cand — [machine]** — Persisted date keys are formatted with `CultureInfo.InvariantCulture` (grep gate: `ToString("yyyy` /
  `ToString("MM` without `Invariant` in `src/**`). — `EnforcementGateTests` + TB-BILL-35. — LB-BILL-26.
- **R132b-cand — [machine]** — One shared, rune-safe `Truncate` helper serves every `HasMaxLength` free-text write (refines R96); exponential
  backoff exponents are clamped. — grep gate for `[..` applied to `.Message`; TB-JOBS-9. — LB-JOBS-8.

## RULE_CONFLICTS candidates
- **R90 (scrub `Payload` on `Sent`/`Dead`) vs ADM-6 / R52 attribution.** `AdminController.AnnounceAll:277-289` and
  `AdminBroadcastOutboxHandler.cs:8-11` designate the `admin.broadcast` outbox payload (title, body, `StaffUserId`) as *the* attribution record
  for the largest-blast-radius admin write — there is no audit row. R90 as drafted erases it the moment the fan-out succeeds. Resolution
  proposal: the broadcast handler writes a system-scope audit row (`admin.announce_all.delivered {staff, user_count}`) before the scrub applies,
  and R90's scrub allowlists nothing. Not overturning R90; flagging for Phase 5.
- **R57 ("attempt/dead-letter bookkeeping advances on any completion failure") is satisfied by the code yet two wrong results remain**:
  (i) the bookkeeping transaction itself can fail with no fallback (LB-JOBS-7), (ii) deterministic refusals are retried (LB-JOBS-3). Propose
  amending R57's text to "advances *durably* (claim-time) and distinguishes permanent from transient failures" rather than adding a rule.
- **R94 as drafted (Phase 1) fixes the SSRF hop but not the false-success (LB-JOBS-1) or the TOCTOU (LB-JOBS-2)**; R127b subsumes it — the
  synthesizer should merge, not keep both.
- **R92 (enqueue-time validation as strict as dispatch parsing)** covers `MediaType`/`To`/`FileName` but LB-JOBS-4 shows the *size* half is
  measured in the wrong unit; R129b is its sibling, not a conflict.
- **v3 harness verdict ("server-side harness strong")** — still true for sequential paths; the `NoopEmailSender` in the only scheduled-job test
  hides a real atomicity defect (LB-BILL-24). Suggest the Phase-5 harness section record "fault seam still missing; job tests must use the
  real outbox sender".
- **JOBS-6's fix text must keep the `when (cancellationToken.IsCancellationRequested)` filter** (it does); a reviewer simplifying it to a bare
  rethrow would turn `HttpClient.Timeout` into a 500 for the owner (LB-JOBS-6). Not a conflict; a guard for the implementer.

## Answers to the brief's explicit questions (evidence-backed, for the synthesizer)
- **10 MiB cap:** per-email **total** across attachments (`IEmailSender.cs:61-66` sums `Content.LongLength`), **raw bytes**, `total > MaxTotalBytes`
  ⇒ exactly 10 MiB passes; inline images and attachment count are not counted; base64 is not counted (LB-JOBS-4). `Payload` is `text` with no
  `HasMaxLength` (`OutboxMessageConfiguration.cs:14`) — no truncation; a 10 MiB attachment is ≈13.3 MiB of JSON per row (TOAST-backed, fine
  for Postgres; JOBS-2 covers lifetime/storage).
- **Backoff arithmetic:** `BackoffBase * 2^(attempt−1)` with base 10 s ⇒ retries at 10/20/40/80 s after failures 1–4; dead-letter when
  `AttemptCount >= MaxAttempts` (5) i.e. after the **5th** failure (≈2.5 min total); `TimeSpan` overflow at attempt ≥ 47 (latent, LB-JOBS-8);
  `ProcessedAt` is **not** set on `DeadLettered` (`OutboxProcessor.cs:125` sets only `Status`), so a purge must key Dead rows on `NextAttemptAt`/`CreatedAt`
  or the code must gain a terminal timestamp (TB-JOBS-8).
- **`EfOutbox.EnqueueAsync` tenant stamping:** only what the caller passes (`EfOutbox.cs:21`); no interceptor (not `ITenantScoped`). Email: none
  (JOBS-2); webhook: `currentTenant.TenantId` (null under system scope); billing cancel: the dissolved tenant id; broadcast: null.
- **Replay/delivery rows:** replay enqueues a new outbox message with the same `EventId`; the handler writes a **new** `WebhookDelivery` per
  dispatch attempt (never mutates); there is **no attempt number column** — rows are distinguished only by `CreatedAt` (ordering
  `ListDeliveriesAsync:154-158`). The out-of-band failure row is durable before the throw; if the processor's bookkeeping then fails
  (LB-JOBS-7) rows > attempts; if a *success* commit fails, rows < attempts. A replay's row is indistinguishable from an original retry.
- **Idempotency on redelivery:** email — duplicate send accepted by design; webhook — duplicate POST + a second success row (receiver dedups
  by `X-Webhook-Id`); billing webhook — inbox dedup makes a redelivered `customer.subscription.updated` a `Duplicate` before any notice;
  broadcast — rows roll back with the claim tx (idempotent). A delivery row inserted twice for one attempt cannot happen (one `Add` per path).
- **Nulls/defaults:** `SeatLimit == null` ⇒ unlimited (`SeatUsage.CanAdd`); `CurrentPeriodEnd == null` ⇒ never lapses (entitlement, sweep, comp
  agree); `OccurredAt` default only via the fake provider (first event applies, later defaults are "older" and dropped — test-only); Stripe
  always sets `Created`. `user_ids: []` ⇒ nobody (LB-ADM-2 held).
- **Time zones:** all comparisons are `DateTimeOffset` in UTC; Stripe.net's converters give `DateTime(Kind=Utc)`; `new DateTimeOffset(end, Zero)`
  is valid for Utc/Unspecified — the `default(DateTime)` case (LB-BILL-21) is silent, not a throw.
- **Currency/rounding:** none anywhere (confirmed by grep: no `decimal`, no amounts in Core/Api billing types).
- **Tenancy scope:** `EnterTenant(evt.TenantId)` trusts provider metadata that only the operator (Dashboard) can set; test-mode events need
  the test endpoint's secret (LB-BILL-27); the missing existence check is LB-BILL-23. Sweep: per-tenant `EnterTenant` (correct). Broadcast:
  system scope over per-user tables (correct); attribution only in the payload (R90 conflict).
- **Sync test-send timeout:** `TaskCanceledException` from the 10 s client timeout ⇒ `delivered:false` row while the receiver may have
  accepted — inherent; the JOBS-6 fix must not rethrow it (LB-JOBS-6).
