# Test-completeness (Phase 3, Part B — cross-cutting delta view) — v4 raw report
SHA: 8c8ed4a · BASE: f52be3d · Date 2026-09-23 · Read-only (no tracked file touched)

Scope: the delta as a whole minus the three sibling Part-A areas (auth/client, billing/jobs, CI/native own
their own specs). Owned here: **tenancy negatives for every delta surface**, **admin/impersonation interplay
with the new features**, **the RCL pages that are still E2E-only (TOOL-7)**, **the rotation-link migration**,
**the E2E suite**, **the config/doc gates**, and the **harness-readiness** verdict. Prefixes as assigned:
`TB-TEN-` from 10, `TB-ADM-` from 7, `TB-UI-` from 40 (17–39 left to the auth/client sibling), `TB-OBS-` from 1,
`TB-DOC-` from 1; candidate rules `R123t…`. No `TB-MIG-` prefix was allocated — the two migration specs sit
under `TB-TEN-27/28` (the migration touches `RefreshTokens`, whose RLS classification is a tenancy fact).

## SUMMARY

- **Specs: 72** — tenancy negatives **19** (TB-TEN-10..28, incl. 2 migration) · admin/impersonation **5**
  (TB-ADM-7..11) · RCL pages **26** (TB-UI-40..65) · E2E journeys **6** (TB-UI-66..71) · jobs/observability
  cross-cut **7** (TB-OBS-1..7) · config/doc gates **11** (TB-DOC-1..11). Of these, **13 fail today by
  reading** (a gap in the code, not only in the suite) and **5 are decision-gated** (they pin whichever way the
  human decides BILL-1/BILL-2/R112/UX-13/AUTH-8).
- **Headline 1 — the cross-tenant negatives the delta added are name-only.**
  `Replay_UnknownOrOtherTenant_ReturnsFalse` (`WebhookDeliveryLogTests.cs:232`) never seeds another tenant's row
  (a random GUID is "unknown", not "other tenant"); `SendTest_UnknownSubscription…` likewise. Nothing at the HTTP
  layer proves 404-not-403 for a foreign delivery id, and nothing proves a foreign subscription id never receives
  a *signed* ping. The SignupGate suite (13 tests, `SignupGateTests.cs`) is the delta's best isolation suite and
  still lacks the two negatives that matter: "admits, never joins" and "reads only the inviting tenant".
- **Headline 2 — a new data class rode an old table and no lifecycle test noticed** (JOBS-2): no test asserts
  `TenantId` on an email enqueue, no dissolve/erasure test touches `OutboxMessages`, and the tenant-axis canary
  (`EveryTenantOwnedEntity_IsWiredIntoTenantDissolution`) cannot see a nullable-`TenantId` table. TB-TEN-20/21/22
  are the R91 pins; all three fail today.
- **Headline 3 — the RCL chassis (R70) is real; the five TOOL-7 pages remain E2E-only because nobody wrote the
  page tests, not because they cannot be written.** Every spec in TB-UI-40..65 is writable today on
  `ComponentTestBase` except three, blocked by two seams: no clock on `NotificationBell` (`PeriodicTimer(60 s)`,
  `DateTimeOffset.UtcNow` ×3) and no cancellation-honouring "timing-out" stub in `TestHttpHandler`.
- **Harness verdict:** server side strong (v3's four gaps: impersonation client helper ✓ closed;
  `IDbContextFactory` fixture ✓ added; concurrency runner and DB-fault seam still ad hoc — five `Task.WhenAll`
  files, four private `Throwing*` doubles). Client side: chassis ✓, clock partial (AuthService ✓, Bell ✗, Billing
  page by `[Parameter]`), HTTP stub lacks 3xx/timeout, no gate-off E2E lane, BlazorBoot/shard logic is
  shell-private. Named per spec below.
- **Rules proposed:** R123t–R131t (9): joint-invariant test for coupled changes; lifecycle spec per new
  data-bearing column/table; two-tenant seed for every "OtherTenant" test; gate-off lane per deployment gate;
  `[Explicit]` excluded from sharding; RCL clock seam; test-id contract; migration Down on data; recording
  doubles for pre-auth cross-tenant reads.

## Part B — spec catalogue

Format: **ID** `TestName` — layer — *arrange / act / assert* — **pins** — status today (✅ passes by reading ·
❌ fails by reading · ⚖ decision-gated · 🔍 suspected). File = where it belongs.

### Critical — tenancy & isolation negatives (TB-TEN-10..28) — FIRST

**Signup gate (GATES-2) — `SignupGate.cs`, `TenantInvitationRepository.cs:42–55`, `UserService.cs:176–181`**

- **TB-TEN-10** `SignupGate_Admits_NeverJoins` — integration, `SignupGateTests` — *tenant A owned by
  green-listed `friend@`; pending invite for `x@`; act `GetOrCreateByEmailAsync("x@")`; assert: `x` holds exactly
  one membership, role Owner, in a tenant ≠ A; A's member count unchanged; the invitation row is still
  `Pending` with its `TokenHash` intact* — **pins** the one-line rule the code comments state ("the gate only
  admits; joining is the token's job") and makes AUTH-8's tenant-of-one an asserted fact rather than a surprise.
  ✅ (by reading; not asserted anywhere today — `InvitedInto_…_MaySignUp` checks only the email).
- **TB-TEN-11** `SignupGate_ReadsOnlyTheInvitingTenant_NeverAnyOther` — unit, `SignupGateTests` — *wrap
  `ITenantRepository` in a recording decorator; invitations for `v@` from tenants A (listed owner) and B (unlisted
  owner); a third tenant C holds no invitation; act `IsAllowedAsync("v@")`; assert `GetMemberDetailsAsync` was
  called only with {A, B} (any order), never C, and stops after the first listed owner* — **pins** that the
  pre-auth hatch (`IgnoreQueryFilters().TagWith(RlsTags.CrossTenant)`) widens exactly to the invitation rows and
  nothing else; a hostile owner cannot turn an invitation into a read of the invitee's other households. ✅.
  Harness: needs a recording `ITenantRepository` decorator — `ServiceHarness` wires the real one (see Harness §).
- **TB-TEN-12** `SignupGate_IsNeverConsultedForAnExistingAccount_NoOracle` — unit — *recording `ISignupGate`;
  `early@` exists; restricted list excludes it; act `GetOrCreateByEmailAsync("early@")` and `GetOrCreateUserAsync`
  (OAuth path); assert 0 gate calls and 0 `LogInformation("Refused signup…")`* — **pins** the enumeration
  argument in `gates.md`: inviting `victim@` reveals nothing about whether `victim@` already has an account
  (refusal and admission are decided identically for both). ✅ — the existing
  `ExistingAccount_SignsIn_EvenWhenNotGreenListed` proves the outcome, not the absence of the call.
- **TB-TEN-13** `SignupGate_TwoInvitations_AdmitsOnTheListedOwner_RegardlessOfOrder` — integration — *invites
  from A (listed) and B (unlisted) for the same address, seeded B-then-A and A-then-B; assert admitted both
  times; assert `SignupNotAllowedException.Message` and the refusal log for a third, unadmitted address contain
  no tenant id or owner email* — **pins** order independence and that refusal text never names a tenant. ✅.
- **TB-TEN-14** `GetValidByEmailAcrossTenants_UnderTheRlsRole_SeesEveryTenant_AndTheUntaggedTwinSeesNone` —
  integration, RLS role (`RlsTestSetup`, pattern of `RlsBackstopTests`) — *ambient tenant C (or none); invites
  for `v@` in A and B; act the tagged repository method ⇒ 2 rows; act the same LINQ without `TagWith` as C ⇒ 0
  rows* — **pins** that the backstop is bypassed only by the sanctioned tag on this new hatch (ADR-020 RLS-7),
  and that the tag is *needed* (a future refactor dropping it would silently return 0 and refuse every invitee).
  ✅ for the tagged half; the untagged twin documents the fail-closed default.
- **TB-TEN-15** `InvitationEmail_NormalizationParity_WriterAndGateAgree` — integration — *create the
  invitation through `TenantInvitationService` with `"  Mixed@Case.COM "`; act the gate with `"mixed@case.com"`,
  `" MIXED@case.com"` and `"mixed@case.com.evil"`; assert admitted, admitted, refused* — **pins** the
  repository comment "InvitedEmail is stored normalized, so normalize in C#" as a two-sided contract (a seeded
  mixed-case row, as `SeedPendingInviteAsync` could write, would be unreachable by the gate). ✅ by reading; the
  existing suite seeds lowercase only.

**Webhook delivery log / replay / send-test (HOOKS-2, #194/#199–#201) — `WebhookService.cs:100–174`,
`WebhookEndpoints.cs:46–64`**

- **TB-TEN-16** `Replay_OfAnotherTenantsDeliveryId_Is404_AndEnqueuesNothing` — integration HTTP,
  `IntegrationTestFactory` — *owner A seeds a delivery row (via `SendTestAsync` against a stub or direct insert);
  owner B `POST /api/webhooks/deliveries/{A.deliveryId}/replay`; assert 404 (not 403), `OutboxMessages` of type
  `webhook` count 0; the same call with a random id is byte-identical (status + body)* — **pins** enumeration
  neutrality at the HTTP layer (v7 GUIDs are time-ordered) and R2 isolation. ✅ by reading
  (`ReplayAsync` filters `d.TenantId == tenantId`) — the existing `Replay_UnknownOrOtherTenant_ReturnsFalse` is
  **vacuous on "OtherTenant"**: it never seeds a second tenant.
- **TB-TEN-17** `SendTest_OnAnotherTenantsSubscription_Is404_AndNeverSignsAPingToTheirUrl` — service test,
  `WebhookDeliveryLogTests` — *subscription in A (URL `https://a.example/hook`, secret S); B calls
  `SendTestAsync(A.subId)`; assert null, `WebhookDelivery` count 0, recording `IWebhookSender` called 0 times*
  — **pins** that a foreign subscription id can never make the platform emit a request signed with A's secret to
  A's endpoint (a replay/DoS primitive). ✅ by reading (`GetAsync` is tenant-scoped) — untested.
- **TB-TEN-18** `ListDeliveries_ForeignSubscription_Is200Empty_IndistinguishableFromUnknown` — service +
  HTTP — *rows for sub S in A and B; B lists S ⇒ only B's; C (no rows) lists S ⇒ `[]` with 200; a random id ⇒ `[]`
  200* — **pins** the existing `ListDeliveries_IsTenantScoped` outcome and adds the neutrality half. ✅.
- **TB-TEN-19** `WebhookOutboxHandler_RefusesAPayloadWhoseSubscriptionIsNotInTheMessagesTenant` — unit,
  `WebhookDeliveryTests` — *outbox row `TenantId = A`, payload `SubscriptionId = B.sub`; dispatch; assert no send,
  one failed delivery row (or dead-letter) attributed to A, never to B* — **pins** that the replay path
  (`ReplayAsync` re-enqueues `delivery.SubscriptionId` under the caller's tenant) can never be turned into a send
  on another tenant's subscription if a delivery row is ever mis-stamped. 🔍 handler behaviour not read here.

**Outbox lifecycle across tenant/user erasure (JOBS-2 / JOBS-4) — `OutboxEmailSender.cs:29`,
`TenantDissolutionService.cs:41–46`, `AccountErasureService.cs:88–95`**

- **TB-TEN-20** `Dissolve_WipesTheTenantsOutboxRows_OtherTenantsIntact` — integration, extends
  `TenantTeardownContributorTests` — *pending `webhook` rows with `TenantId = A` and `= B`; dissolve A under
  `EnterTenant(A)`; assert 0 rows for A, B's rows intact* — **pins** R91's dissolve half. ❌ **fails today**: no
  contributor touches `OutboxMessages` (grep of `ITenantDataContributor` implementors: ApiKey, Billing, Notes,
  UsageCounter, Webhook, Audit — none outbox).
- **TB-TEN-21** `TenantScopedEmailEnqueues_StampTenantId_SystemBroadcastsAreTheOnlyException` — integration,
  `OutboxEmailTests` + `NotificationFanOutTests` — *send an invitation from A and a notification email to a member
  of A; assert both `email` rows carry `TenantId = A`; the admin `announce-all` broadcast's per-user emails are
  the allowlisted `TenantId = null` origin (or carry the recipient's tenant — decide)* — **pins** R91's stamping
  half so TB-TEN-20 can reach email rows at all. ❌ **fails today**: `OutboxEmailSender.SendAsync` calls
  `outbox.EnqueueAsync(MessageType, payload, cancellationToken: …)` with no tenant id.
- **TB-TEN-22** `Erasure_RemovesOrScrubsOutboxEmailsAddressedToTheUser` — integration, `AccountErasureTests`
  — *pending + sent `email` rows with `To = user.Email` (one with a 1 MiB attachment); erase the user; assert no
  row's `Payload` contains the address or the attachment bytes* — **pins** per-user erasure completeness (R12
  spirit) for the new data class. ❌ **fails today** (`AccountErasureService` deletes RefreshTokens/Logins/
  LoginTokens/Users only).
- **TB-TEN-23** `Export_ContainsNoOutboxPayload_NoDeliveryBody_NoDeliveryError` — integration,
  `TenantExportTests` — *A has an attachment email in flight, a failed delivery whose `Error` holds
  `"No such host is known: internal-hook.corp"`; export A; assert the bundle JSON contains none of: the base64
  attachment, the recipient address from the outbox, the delivery `Body`, the `Error` string* — **pins** the
  secret-free export rule (ADR-011) on the two fields the delta made tenant-visible/persisted. ✅ for
  body/error by reading (`WebhookDataContributor.ExportAsync` projects metadata only); the outbox half is ✅ only
  because the export ignores the outbox entirely — assert it explicitly so a future "include pending emails"
  contributor cannot leak attachment bytes.

**The rotation-link column vs RLS/erasure — `RlsDdl.TenantTables`, `RefreshTokenRepository.cs:40–66`,
`ExpiredTokenCleanupJob.cs:19–24`**

- **TB-TEN-24** `RefreshTokens_IsNotAnRlsTable_IsErased_AndTheRotationLinkNeverCrossesUsers` — arch +
  service — *(a) `RlsDdl.TenantTables(model)` does not contain `"RefreshTokens"` and the delta migration adds no
  policy DDL; (b) `EveryUserKeyedEntity_IsWiredIntoAccountErasure` still covers `RefreshToken` (assert by
  reflection, not by name); (c) user U1's rotated token has `ReplacedByTokenId` pointing at U2's live token
  (hand-written row); `Inspect(U1.old)` ⇒ `Reuse`, never `RotatedWithinGrace`* — **pins** the user-scoped
  classification of the new column and that the soft link is honoured only within a user. (a)(b) ✅ by reading;
  (c) 🔍 — `Inspect` reads the successor by id (`RefreshTokenConfiguration.cs:18–20` comment); a UserId equality
  check was not confirmed in the lines read. **Auth sibling please cross-check.**
- **TB-TEN-25** `Predecessor_WhoseSuccessorRowWasDeleted_IsReuse_NotGrace_NotCrash` — service,
  `TokenServiceTests` — *rotate; advance the clock past the successor's expiry; run `ExpiredTokenCleanupJob`
  (deletes `ExpiresAt < now`, `ExpiredTokenCleanupJob.cs:23`); present the predecessor within its own grace (or
  with a long grace) ⇒ `Reuse` and revoke-all; no exception on the missing successor* — **pins** the
  deliberate no-FK design of the migration under the job that motivated it. ✅ likely (existing
  `…ButSuccessorExpired_IsReuse` covers *expired-but-present*); the *deleted* variant is the one the migration
  comment argues for and nobody runs.
- **TB-TEN-26** `Features_ExposesOnlyTheDocumentedKeys` — integration, `FeaturesEndpointTests` — *GET
  `/api/features` anonymous; assert the JSON object has exactly the key set `{billing}`* — **pins** that the
  anonymous probe cannot grow a staff list, green-list echo or tenant count by accident. ✅. (No other tenancy
  spec exists for this endpoint — it is anonymous and carries no tenant data; stated as required.)
  `OtlpEndpoints` — no tenant data, no spec (its one remaining hole is TB-OBS-1). Announcement fan-out with the
  renamed `cancellationToken:` argument — **no spec needed**: a positional token in the 5th slot no longer
  compiles (`IEmailSender.SendAsync` gained `attachments` there); the compiler is the gate (JOBS-8).

**Migration `20260919010912_AddRefreshTokenRotationLink` (no `TB-MIG` prefix allocated)**

- **TB-TEN-27** `AddRefreshTokenRotationLink_UpThenDown_OnLinkedRows_KeepsTheRowsAndDropsBothColumns` —
  migration test beside `MigrationsTests` — *migrate to the migration before it; insert two refresh-token rows;
  apply Up; set `RotatedAt`/`ReplacedByTokenId` on row 1; apply Down; assert both rows survive with `IsRevoked`
  unchanged and `information_schema.columns` lists neither column; apply Up again ⇒ both null (the link is lost
  on rollback, by design — assert and document); a second raw `Up` is refused by the migrations history (EF), not
  by the migration body — assert `__EFMigrationsHistory` has the id once* — **pins** "Down on a row with a
  rotation link" and the one-way data loss. ✅ by reading (`DropColumn` ×2 is data-agnostic); the existing
  `Migrations_Down_RevertCleanly_ToEmptySchema` runs on an **empty** schema and so never exercises Down on data.
- **TB-TEN-28** `Snapshot_MatchesModel_ForTheRotationLink` — exists as
  `Migrations_ApplyCleanly_AndModelHasNoPendingChanges` ✅ — listed to show the pair is complete; no new test.

### Critical — admin / impersonation interplay with the delta (TB-ADM-7..11)

- **TB-ADM-7** `ImpersonationToken_IsRefusedOnEveryAdminWrite` — integration theory, `AdminControllerTests`,
  using the (now existing) `CreateImpersonatingClientFor(staff, target)` — *rows: `PUT tenants/{id}/subscription`,
  `DELETE tenants/{id}/subscription`, `POST tenants/{id}/announce`, `POST announce-all`, `POST users/{id}/mfa-reset`,
  `POST users/{id}/impersonate`; each ⇒ 403 and no side effect (no `Subscription` row, no outbox row, no audit
  row)* — **pins** R45 over the enumerated ADR-021 writes; today one row exists
  (`ImpersonationToken_AtStaffGate_Returns403_EvenWhenTargetIsStaff`, a single read). ✅ by reading (all share
  `RequireStaffAsync`) — the theory makes the next admin write inherit the check by construction.
- **TB-ADM-8** `Staff_Comp_WhileBillingIsOff` — integration on the factory's **default (gate-off)** boot —
  *staff `PUT tenants/{A}/subscription {plan_key:"pro"}`; assert ⚖ either 404 (R86: gated by name with the
  billing surface) **or** 200 + a startup/response marker that the comp is honoured while off (BILL-2 "feature")*
  — **pins** the BILL-2 decision. ⚖ decision-gated; today it is 200 (ungated) and untested under gate-off
  (`AdminControllerTests` never sets `Billing:Enabled=false` — Phase 2 confirmed).
- **TB-ADM-9** `Staff_TenantDetail_WhileBillingIsOff_ReportsTheDecidedPlan_ForACompedTenant` — integration —
  *gate off; A holds a comped `active`/`pro` projection; staff `GET tenants/{A}`; assert the plan the BILL-1
  decision names (Free if fail-closed; Pro-with-warning if gate-blind)* — **pins** BILL-1 on the one screen that
  shows staff the truth. ⚖.
- **TB-ADM-10** `WebhookSendTestAndReplay_UnderImpersonation_AreAttributedToTheActingStaff` — integration —
  *impersonating client for A's owner; `POST /api/webhooks/{id}/test` and `…/replay`; assert an audit row exists
  with `impersonated_by = staff` (R50 family, `ImpersonatedWrite_RecordsActingStaff_OnTheAuditRow` pattern)* —
  **pins** that the two tenant-visible writes the delta added (#194/#199–#201) do not escape the attribution rule
  because they are minimal-API endpoints rather than controller actions. ❌ **fails today**: neither
  `SendTestAsync` nor `ReplayAsync` writes an audit row at all — a delivery row is not an audit row (no actor).
  Decision: audit them, or exempt "operational plumbing" by name in the rules file (R85 pattern).
- **TB-ADM-11** `AnnounceAll_Broadcast_IsTheOnlyTenantlessEmailOrigin` — arch/integration companion to
  TB-TEN-21 — *run the `AdminBroadcastHandler` through the real processor; assert its email rows are the only
  `email` rows with `TenantId IS NULL` after a mixed workload (invitation, notification, broadcast)* — **pins**
  the allowlist that makes R91 enforceable instead of "every row stamped" (which the broadcast cannot satisfy
  without a per-recipient tenant lookup). ❌ today (every email row is tenant-less).

### High — RCL pages on the bUnit chassis (TB-UI-40..65) — the TOOL-7 set

All render on `ComponentTestBase` (`SignInAsync`, `StubFeatures`, `TestHttpHandler`, `FakeTimeProvider`,
`FakeFileDownloadLauncher`). "Branch" cites the `.razor` line.

**`Household.razor` (70 decision points; delta touched lines 238–260, 329–335)**

- **TB-UI-40** `Household_SeatLimitCopy_AfterAFailedProbe_DoesNotOfferOrDenyOnStaleState` — *`/api/features`
  `OnUnreachable` at render, then `On(...)` billing=true; invite ⇒ 402; assert the copy chosen is the billing-on
  copy (re-probed) or a neutral copy — never `Household_ErrSeatLimitNoBilling` on a billing-on deployment* —
  pins BILL-7/UX-13 for this page (the field is read once in `OnInitializedAsync`:260; `IsBillingEnabledAsync`
  returns false uncached on exception). ❌ today the stale `false` wins.
- **TB-UI-41** `Household_Invite_409_ShowsAlreadyMember` (line 326) · **TB-UI-42**
  `Household_Invite_OtherFailure_ShowsSendError` (337) · **TB-UI-43** `Household_Load_Failure_ShowsErrLoad`
  (279–281) — three E2E-only error branches; each: stub, click, assert the `FakeStringLocalizer` key. ✅.
- **TB-UI-44** `Household_Controls_FollowTheRoleMatrix` — theory over `role ∈ {owner, admin, member}` from the
  `/api/household` payload — *assert rename/invite/remove present for owner+admin, promote/demote owner-only,
  transfer/dissolve owner-only (lines 249–251)* — pins the client mirror of ADR-009; E2E covers one role only
  (`Roster_Is_PermissionAware_For_NonOwner`). ✅.
- **TB-UI-45** `Household_Export_LaunchesTheAbsoluteUrl_ThroughTheLauncher` — *`POST …/export` ⇒
  `{download_url:"/api/files/abc"}`; assert `DownloadLauncher.Launched[0].Url == "http://localhost/api/files/abc"`
  (line 426 joins `Http.BaseAddress`); an S3-style absolute `https://s3…` URL passes through unchanged* — pins
  the URL contract NAT-12 depends on (the launcher must not receive a relative path; the native launcher must
  not send a bearer to it — the header half is the native sibling's). ✅.
- **TB-UI-46** `Household_Leave_Transfer_Dissolve_FailureCopy` — theory over the three error keys (456, 473,
  490) + success navigations. ✅.

**`Join.razor` (delta 50–80)**

- **TB-UI-47** `Join_Anonymous_RedirectsToLogin_EvenWhenTheFeatureProbeIsSlowOrDown` — *`/api/features`
  `OnGated` (never answers) or `OnUnreachable`; render anonymous `/join?token=x`; assert the login redirect is
  not blocked behind the probe (line 77 awaits `IsBillingEnabledAsync()` **before** the `IsAuthenticated` check)*
  — pins that a gate probe never delays sign-in. 🔍 by reading the redirect waits for the probe; with
  `OnUnreachable` it proceeds (exception ⇒ false); with a hung probe it hangs — the spec decides which is
  acceptable (recommend: probe after the auth check).

**`Login.razor` (64 decision points; delta 198–204, 352–355)**

- **TB-UI-48** `Login_QueryError_Table_IsExhaustive` — theory over `external_failed`, `email_unverified`,
  `invalid_link`, `signup_not_allowed`, `""`, `"unknown_code"` ⇒ the six copy keys incl. the catch-all
  (`Login_ErrSomethingWrong`). Exists for one row (`LoginPage_ReadsTheRefusalFromTheQueryString`). ✅.
- **TB-UI-49** `Login_WebOtp_403_MapsBySignupBody_NotByStatusAlone` — *OTP verify ⇒ 403 with
  `{"error":"signup_not_allowed"}` ⇒ `Login_ErrSignupNotAllowed`; 403 with `{"error":"other"}` or an HTML body ⇒
  the generic copy* — pins R112 (UX-14). ⚖ today line 355 maps **any** 403 to the signup copy; the spec fails
  until R112 lands and is the test that lands it.
- **TB-UI-50** `Login_SendLink_And_SendCode_429_ShowTooManyRequests` (272, 290) · **TB-UI-51**
  `Login_MfaChallenge_EntersFromQuery_AndFromOtpResult` (242–245, 314–319) — E2E-only branches today. ✅.

**`Billing.razor` (delta: return pages, cancelled state, refetch loop, gate bounce)**

- **TB-UI-52** `Billing_ReturnSuccess_WhenTheWebhookNeverLands_StopsAfterTwoRefetches_AndDisposeCancels` —
  *`OnSequence` summary always Free; `RefreshDelayMs=1`; assert exactly 3 `GET /api/billing` (initial + 2);
  dispose mid-delay ⇒ no further request, no `ObjectDisposedException` surfaced (lines 150–158)* — pins the loop
  bound the delta introduced (existing test covers the flip-happens path only). ✅.
- **TB-UI-53** `Billing_Off_BouncesBeforeAnyBillingRequest` — extend `BillingPage_RedirectsHome_WhenBillingIsOff`
  with `Assert.DoesNotContain(Http.Requests, r => r.RequestUri.AbsolutePath.StartsWith("/api/billing"))`. ✅.
- **TB-UI-54** `Billing_ProbeUnreachable_DoesNotBounceHome_OnABillingOnDeployment` — *`/api/features`
  `OnUnreachable`; render `/billing`; assert no `NavigateTo("/")` and a retry/neutral state* — pins UX-13/BILL-7
  for the page (line 134 bounces on `false`, which is also the transient-failure answer). ❌ today it bounces.
- **TB-UI-55** `Billing_CancelReturnPage_ShowsBanner_AndNoRefetchLoop` — exists
  (`…Cancel_ShowsTheHonestBanner_AndChangesNothing`) ✅ — listed for completeness of the return-page pair.

**`AppHeader.razor` (delta: `_billingEnabled`, `OnIdentityChanged`, dispose)**

- **TB-UI-56** `AppHeader_BillingLink_ReprobesAfterAFailedColdProbe` — *probe unreachable at render, then
  `SignInAsync()` (fires `SignedIn`) with the probe answering true; assert `nav-billing` appears* — pins BILL-7.
  ❌ today: `_billingEnabled` is set once in `OnInitializedAsync` (line ~70) and never re-read.
- **TB-UI-57** `AppHeader_AdminLink_FollowsImpersonation_EnterAndStop` — *sign in as staff (`/api/admin/me` ⇒
  `is_staff:true`); assert Admin link; `Auth.BeginImpersonation(TestJwt.Build(impersonatedBy:"staff"))` ⇒ link
  gone, badge shows the impersonated name; `StopImpersonationAsync` ⇒ link back* — pins QA-ADMIN-03's fix at the
  component level; `ImpersonationIdentityTests` proves only the event on `AuthService`. ✅.
- **TB-UI-58** `AppHeader_IdentityChanged_StaffProbeFailure_KeepsThePriorHeader` (catch at line ~95) ·
  **TB-UI-59** `AppHeader_Dispose_Unsubscribes_NoRenderAfterDispose` — *dispose, then `BeginImpersonation` ⇒ no
  exception, no render* — pins the leak/`InvokeAsync`-after-dispose branch. ✅.

**`AdminConsole.razor` (54 decision points; E2E drives announce + forbidden only —
`AdminConsolePage.cs` has no comp/impersonate locators)**

- **TB-UI-60** `AdminConsole_CompButtons_FollowTheBillingGate` — *`StubFeatures(billing:false)`; select a Free,
  non-provider tenant; assert ⚖ `admin-comp-pro` hidden (if BILL-2 → gated) or shown with the "comp while off"
  wording (if feature)* — pins the UI half of TB-ADM-8. ⚖ today the buttons ignore the gate (lines 130–142).
- **TB-UI-61** `AdminConsole_Comp_409_ShowsSubscriptionError_AndKeepsDetail` (338) · **TB-UI-62**
  `AdminConsole_Revert_204_ClearsThePlan_AndIsIdempotent` (353–358) · **TB-UI-63**
  `AdminConsole_Impersonate_ConfirmCancelled_DoesNothing_ConfirmAccepted_BeginsImpersonation` (278–291; bUnit
  `JSInterop.Setup<bool>("confirm", …)` — the base is Loose, so set the two returns explicitly) ·
  **TB-UI-64** `AdminConsole_NonStaff_RendersForbidden_WithoutListingTenants` (E2E has it; the bUnit twin costs
  one stub) — ✅ all writable today.

**`NotificationBell.razor` (41 decision points; delta-untouched but TOOL-7-named)**

- **TB-UI-65** `Bell_Poll_StopsOnSignOut_ResumesOnSignIn_AndKeepsTheLastCountOnATransientError` — *advance the
  clock 60 s ⇒ one `unread-count` GET; sign out ⇒ no further GET; sign in ⇒ resumes; `OnUnreachable` ⇒ count
  unchanged (line 133)* — **blocked**: `PeriodicTimer(TimeSpan.FromSeconds(60))` (line 117) and
  `DateTimeOffset.UtcNow` (162, 179, 227) are not clock-injected; the `Ago` buckets are covered today only by
  fixing `CreatedAt` relative to real now (`Bell_RendersRelativeAge_PerBucket`). Mutation-set specs
  (mark-read revert 167, delete 191, clear-read `?read=true` 206, clear-all `?read=false` 218 — verified against
  `NotificationsController.cs:77–90`) are writable now; v3 TB-UI-9/10 already cover double-decrement and
  request shape.

### E2E journeys the delta added or changed (TB-UI-66..71)

- **TB-UI-66** `BillingOff_Lane_NoLinkNoRouteNoUpgradeCopy` — *API started with `Billing__Enabled` unset; owner
  signs in; assert no `nav-billing`; `/billing` lands on `/`; curl `/api/billing` ⇒ 404; invite past the cap ⇒
  `Household_ErrSeatLimitNoBilling` copy* — the only end-to-end proof that GATES-1 ships closed. **Blocked by
  the harness**: both CI copies start ONE API per e2e job with `Billing__Enabled: "true"` (`.forgejo/…ci.yml:1062`,
  `.github/…ci.yml:910`); needs a gate-off lane (see Harness §, R126t). BILL-10 named this gap.
- **TB-UI-67** `SignupRefused_Otp_ShowsPrivateTesting_MagicLink_LandsOnLoginWithError` — *lane with
  `Signup__AllowedEmails__0=allowed@example.com`; stranger OTP ⇒ `login-otp-error` = private-testing copy (and
  NOT the wrong-code copy); stranger magic link ⇒ `/login?error=signup_not_allowed`; OAuth via
  `TestExternalAuthHandler` if the lane wires it* — same lane as TB-UI-66 (both gates in their non-default
  state). Blocked likewise.
- **TB-UI-68** `InvitationOwnerRule_ListedOwnerInvitesStranger_StrangerJoins_UnlistedOwnersInviteIsRefused` —
  same lane; drives `SignupGateTests`' two central cases through the browser incl. the accept-by-token step
  (TB-TEN-10's "never joins" becomes visible as a two-step journey).
- **TB-UI-69** `KeepAlive_PastTheAccessTokenExpiry_SessionSurvives_OneRefreshSeen` — *Playwright
  `Page.Clock.InstallAsync()` + `FastForwardAsync(61 min)` (Playwright ≥ 1.45); assert still on `/household`,
  exactly one `/api/auth/refresh` request after the jump, no `/login`* — pins #11 end-to-end. 🔍 the WASM
  `Task.Delay(delay, Time)` (AuthService.cs:685) maps to JS `setTimeout`, which `page.clock` controls; server
  refresh tokens live 30 d so the server side needs nothing. Suspected until tried.
- **TB-UI-70** `SignOut_ThenBack_LandsOnLogin_NotTheCachedHousehold` — *Chromium launched with
  `--enable-features=BackForwardCache`; sign out; `GoBackAsync()`; assert `/login` rendered and
  `bfcache-guard.js` fired a reload (console line or `performance.getEntriesByType('navigation')[0].type ==
  'back_forward'` + reload)* — R110's behaviour test for QA-SEC-03 (UX-10). 🔍 headless bfcache eligibility.
- **TB-UI-71** `ThemeSave_WithARenewalInFlight_SurvivesReload` — *`page.RouteAsync("**/api/auth/refresh")`
  delaying the response; change theme; reload before the refresh completes; assert the new theme after reload* —
  the E2E twin of `PreferenceSyncClaimTests` for #233 + UX-17. Feasible today.
- **Not E2E, stated as required:** webhook replay (no HOOKS UI exists — HOOKS-3 open); the Forgejo deploy drill /
  already-green / refused push are **operator** cases (QA §1.5 + TR-28), not browser journeys; the `[Explicit]`
  native smoke is a CI-shape fact → TB-DOC-1.

### Jobs / observability cross-cut (TB-OBS-1..7)

- **TB-OBS-1** `OtlpEndpoints_ForSignal_PreservesQueryAndUserInfo` — unit — *`https://u:tok@host/otlp?x=1`
  + http/protobuf ⇒ `https://u:tok@host/otlp/v1/logs?x=1`* — pins that `UriBuilder { Path = … }` keeps the
  credential and query an operator may have put in the URL (the 12 existing cases use bare paths). ✅ by reading.
- **TB-OBS-2** `Telemetry_UnreachableCollector_NeverBlocksARequest_AndSaysSoOnce` — integration factory with
  `OpenTelemetry:Otlp:Endpoint=http://127.0.0.1:9` — *100 requests to `/health`; assert p95 < 200 ms and one
  Warning log naming the endpoint* — settles OBS-2 (Phase 2 "unresolvable by tools"). 🔍 the warning does not
  exist today (silent drops) — the spec is the fix's test.
- **TB-OBS-3** `LogTemplates_NeverCarryPii` — R93 gate as written in the rules file; today 8 `{Email}` sites
  fail it. ❌.
- **TB-OBS-4** `OutboxProcessor_DeadLetterAndSent_ScrubThePayload` — R90; `OutboxProcessorTests` already drives
  backoff on the injected clock (`ProcessDue_FailingHandler_BackoffGrowsExponentially` ✅) — add: after `Sent`
  and after dead-letter, `Payload` is empty/`"{}"` and `LastError` is truncated. ❌ today (no scrub exists).
- **TB-OBS-5** `SubscriptionLapseSweep_WhileBillingIsOff_NotifiesNobody` — BILL-5; the job is clock-injected
  (`SubscriptionLapseSweepJob.cs:22`) and has 5 tests, none under gate-off. ❌ by reading (no gate read).
- **TB-OBS-6** `WebhookDelivery_Error_IsAnEnumeratedReasonCode` — R89/JOBS-1 — **the existing
  `SendTest_RecordsDelivery_OnTransportFailure` asserts `!string.IsNullOrEmpty(delivery.Error)` and so pins the
  raw `ex.Message`**; the spec replaces that assertion with membership in `{dns, timeout, tls, connection,
  http_<status>}`. ❌ (Phase 2 confirmed).
- **TB-OBS-7** `WebhookDelivery_Error_IsTruncatedToTheColumn` — 2 000-char message ⇒ row saved, `Error.Length
  ≤ 1000` (JOBS-5/R96). ❌ by reading (no `Truncate` at the write sites).

### Config / doc gates (TB-DOC-1..11)

- **TB-DOC-1** `E2eShards_ExcludeExplicitFixtures_AndTheSuiteCountMatchesE2eMd` — `ForgejoCiParityTests` +
  `EnforcementGateTests` — *parse both `ci.yml`; assert the `--list-tests` pipeline filters
  `TestCategory!=NativeSmoke` (or the shard `awk` reads a name list that excludes `[Explicit]` methods); assert
  `docs/stories/e2e.md` "Current suite size: N" == count of `[Test]`/`[TestCase]` methods in non-`[Explicit]`
  fixtures (34)* — R113. ❌ today: the filter is `FullyQualifiedName~<name>` over every listed name incl.
  `Native_App_Boots_SignsIn_And_LoadsHousehold` (`ci.yml:1094–1107`); NUnit runs an `[Explicit]` test when a
  filter names it (UX-15 "Suspected" — the spec settles it by asserting the CI shape, not NUnit's behaviour).
- **TB-DOC-2** `EveryDeploymentGate_HasAnE2eLaneInItsShippedDefault` — `EnforcementGateTests` — *for each
  `*Settings` with `bool Enabled` or a list-typed allow list (`Billing`, `Signup`, `PublicApi`, `Webhooks`), a CI
  job/matrix entry exists whose env leaves it at the shipped default and runs ≥1 journey tagged
  `[Category("Gate:<Section>")]`* — the machine half of R126t. ❌ (no such lane).
- **TB-DOC-3** `Postman_DescriptionsMention_TheGateKey_AndSignupRefusal` — R119/R83 extension of
  `EveryMappedApiEndpoint_IsDocumentedInThePostmanCollection` (presence-only today). ❌ (TR-21: 0 hits).
- **TB-DOC-4** `ConfigPosture_IsReflective_OverEverySettingsEnabled` — R122; the 7 facts in `ConfigPostureTests`
  are hand-listed. ❌ by design today (S0-G8).
- **TB-DOC-5** `EveryDeltaTestId_IsReferencedByATestOrAQaCase` — *collect `data-testid="…"` literals in
  `src/Shared.Ui/**/*.razor`; assert each appears in `tests/**` or `docs/QA_TEST_PLAN.md`* — the test-id contract
  FLAVORS SPEC-4 will need anyway; today `billing-ended`, `billing-renews`, `admin-comp-pro`,
  `admin-revert-free`, `nav-household` are referenced by bUnit tests, `billing-checkout-cancel` by one, and the
  QA plan by prose — the gate would show which ids are dead. 🔍 count not computed here.
- **TB-DOC-6** `CompiledInLimits_AreListedInEnvExample` — R120 — *`Billing.razor RefreshDelayMs=3000`,
  `NotificationBell PeriodicTimer 60 s`, `BlazorBoot MaxAttempts=3 / BootTimeout=60 s`, `EmailAttachment
  MaxTotalBytes`, `AuthService RenewLead/RenewRetryDelay/MaxRenewalWait/StartupRetryDelays`* — ❌ (TR-25 lists
  the five missing today; this adds the two RCL page constants).
- **TB-DOC-7** `EveryPathAnArchTestReads_IsClassifiedCode` — R97 (`push-to-github.sh`, `deploy.yml`, both
  `postman-sync.yml`, `forbidden-licenses.json`, `.dockerignore`). ❌ (DEP-15).
- **TB-DOC-8** `DocMap_CoversDocsTree_MinusAllowlist` — R118 (`docs/tutorial/`, `docs/qa-runs/`). ❌ (TR-24).
- **TB-DOC-9** `MigrationsDown_RunsAgainstSeededRows_NotAnEmptySchema` — generalises TB-TEN-27: the existing
  `Migrations_Down_RevertCleanly_ToEmptySchema` seeds nothing; the spec seeds one row per entity (the tenant-axis
  canary's seeding could be reused) before walking Down. ❌ (empty schema today).
- **TB-DOC-10** `RuleIds_CitedInTests_AreFinalRules` — R116 (test comments cite `R82/R83/R86` candidates as if
  final — `ArchitectureTests.cs:111,153`). ❌ (TR-15).
- **TB-DOC-11** `AllowlistedTenantIdEntities_ShipATwoTenantIsolationTest` — *for each name in
  `EveryEntityWithATenantId_IsScopedOrAllowlisted`'s `allow` set (`TenantMembership`, `WebhookDelivery` —
  `ArchitectureTests.cs:189`), a test exists whose body calls the shared two-tenant seed helper (R128t) and whose
  name contains `OtherTenant|CrossTenant|IsTenantScoped`* — the machine half of v3's R84-cand, made non-vacuous
  by requiring the helper call rather than the name (the lesson of `Replay_UnknownOrOtherTenant_ReturnsFalse`).
  ❌ (no helper; `ListDeliveries_IsTenantScoped` passes on the name half only).

## Harness readiness

Does the shared harness let a new slice write each spec class out of the box? Per seam, with the specs it
blocks:

| Seam | Status at 8c8ed4a | Evidence | Blocks |
|---|---|---|---|
| Concurrency runner | **Still none shared** (v3 gap 1 open). Five files hand-roll `Task.WhenAll` (`AcceptSeatQuotaTests`, `QuotaServiceTests`, `InboxTests`, `OutboxProcessorTests`, `PasswordlessConcurrencyTests`) | grep | none of this report's specs (the concurrency specs are the sibling areas'); a new slice copies one of five idioms |
| DB-fault seam | **Still none shared** (v3 gap 2 open). Four private doubles: `ThrowingUsageRepository`, `ThrowingHandler` ×2, `ThrowingJob` | `QuotaServiceTests.cs:250`, `OutboxProcessorTests.cs:238`, `WebhookDeliveryLogTests.cs:325`, `ScheduledJobsHostTests.cs:95` | TB-OBS-7 (needs the column-length fault path), TB-TEN-19 partially |
| Injected clock — server | ✅ `RefreshTokenService` (`TimeProvider clock`, :74), `SubscriptionLapseSweepJob` (:22), `OutboxProcessor` (:22, backoff tested on the fake clock), `ExpiredTokenCleanupJob`, `SignupGate`; gate `ServerServices_UseInjectedClock_NotAmbientUtcNow` holds | read | — |
| Injected clock — RCL | **Partial.** `AuthService` ✓ (`Task.Delay(delay, Time)`, `FakeTimeProvider` in `ComponentTestBase`); `NotificationBell` ✗ (`PeriodicTimer(60 s)`, `DateTimeOffset.UtcNow` ×3); `Billing.razor` ✗ (`Task.Delay(RefreshDelayMs)` — tests shorten a `[Parameter]` instead of advancing a clock); `MainLayout` n/a; the clock gate explicitly excludes `Shared.Ui` | `NotificationBell.razor:117,162,179,227`, `Billing.razor:117,152` | **TB-UI-65**; TB-UI-52 works only via the parameter |
| Impersonation-token client helper | ✅ **closed since v3** — `IntegrationTestFactory.CreateImpersonatingClientFor(staff, target)` (:176) | read | TB-ADM-7/10 ready |
| Gate-off `IntegrationTestFactory` | ✅ the factory boots **gate-off by default**; gate-on via `WithWebHostBuilder(b => b.UseSetting("Billing:Enabled","true"))` (`BillingGateTests.cs:22`) | read | TB-ADM-8/9, TB-OBS-5 ready |
| Gate-off **E2E** lane | ✗ one API per e2e job, `Billing__Enabled: "true"` hard-coded, no `Signup__*` | both `ci.yml` | **TB-UI-66/67/68** |
| RCL HTTP stub | `On / OnUnreachable / OnSequence / OnGated`; **no 3xx-with-Location** (redirect-following assertions), **no cancellation-honouring delay** — `OnGated` ignores the token, so `HttpClient.Timeout` can never fire (UX-6's timeout branch is unreachable in bUnit); status-with-HTML-body is achievable via `On(status, json:"<html>")` | `TestHttpHandler.cs:150–162` | UX-6/R107 proof (auth sibling), TB-UI-47's hung-probe variant |
| RCL JS doubles | bUnit `JSInterop` Loose; `confirm` returns default(false) unless set up | `ComponentTestBase.cs:59` | TB-UI-63 needs an explicit `Setup<bool>("confirm")` — cheap |
| Recording repository doubles | ✗ `ServiceHarness` wires the real repositories only; no decorator pattern for "was this cross-tenant read attempted" | `ServiceHarness.cs:28–34` | **TB-TEN-11/12** need a recording `ITenantRepository`/`ISignupGate` (≈20 lines each) |
| Two-tenant seed helper | ✗ every file hand-rolls (`SeedHouseholdAsync`, `SeedDeliveryAsync`, `SeedUserAsync`…) | `SignupGateTests.cs:213–259`, `WebhookDeliveryLogTests.cs:250+` | TB-DOC-11's enforceability; every `OtherTenant` spec re-implements it |
| `IDbContextFactory` fixture | ✅ **added in the delta** (`PostgresFixture.CreateContextFactory`) — out-of-band writers (delivery recorder) testable | diff | — |
| RLS runtime-role harness | ✅ (`RlsTestSetup`, `RlsBackstopTests` pattern) | read | TB-TEN-14 ready |
| Migration-on-data harness | ✗ `Migrations_Down_RevertCleanly_ToEmptySchema` walks an empty schema; no "migrate to N−1, seed, Up, Down" helper | `MigrationsTests.cs:44` | **TB-TEN-27, TB-DOC-9** |
| Shell-logic seam (CI) | ✗ `BlazorBoot.WatchBootAsync` verdicts are private static and need an `IPage`; the shard `sed/awk` and the Slowest-journeys python live only in YAML — testable solely by re-parsing YAML in `ForgejoCiParityTests` | `BlazorBoot.cs:84–107`, `ci.yml:1094–1140` | UX-9's verdict-classification test (CI sibling); TB-DOC-1 is doable via YAML parsing |
| Playwright clock / bfcache | ✗ never used; `Page.Clock` needs Playwright ≥ 1.45 (pinned version not checked here); bfcache needs a launch flag | `E2ETestBase.cs` | **TB-UI-69, TB-UI-70** (both 🔍) |

**Verdict:** a new *server* slice can write every tenancy negative in this report today except the ones that
need a recording double or a seeded migration walk; a new *client* screen inherits a real chassis (R70 held —
every delta component is bUnit-covered) but will copy the Bell's un-injected timer unless R129t lands. The
E2E suite cannot prove either deployment gate in its shipped state until a gate-off lane exists.

## Candidate rules (TDD invariants) — R123t..R131t

Machine unless marked. Suffix `t` = this report; the synthesizer renumbers.

- **R123t [machine] — Coupled client+server changes ship one joint-invariant test.** When a PR changes a
  server timing/security constant and a client that depends on it (grace ↔ refresh timeout/retry; keep-alive ↔
  `exp`; gate ↔ page bounce), it adds a test that reads BOTH constants (cross-project read of `appsettings.json`
  defaults + the `Shared.Ui` constants) and asserts the inequality/relationship in prose. — `ConfigPostureTests`
  cross-project fact + a PR-template line. — subsumes UX-6/UX-7/AUTH-1 joint gap, R107's mechanism generalised.
- **R124t [machine] — A new data-bearing column or table ships its lifecycle spec in the same PR:** dissolve
  (tenant axis), erasure (user axis), export exclusion, and — for nullable-`TenantId` tables — the stamping rule
  and the allowlisted tenant-less origins by name. — Extend `EveryTenantOwnedEntity_IsWiredIntoTenantDissolution`
  to any entity with a `TenantId` (nullable included) and require a test named `<Entity>_Lifecycle_*` per
  migration that adds a column holding user/tenant content (attachments, bodies, recipients). — TB-TEN-20/21/22/23,
  JOBS-2; extends R43/R91.
- **R125t [machine] — Every `OtherTenant`/`CrossTenant`/`IsTenantScoped` test seeds two tenants through the
  shared helper.** A test whose name claims a cross-tenant negative must call `TwoTenants.SeedAsync` (new,
  `tests/Api.Tests/Infrastructure`) — a name with a random-GUID "unknown" arrange is rejected by an arch scan.
  — `EnforcementGateTests` scan of test bodies for the helper call. — TB-TEN-16/17, TB-DOC-11; closes the
  vacuous `Replay_UnknownOrOtherTenant_ReturnsFalse` class.
- **R126t [machine] — A gate-off E2E lane exists for every deployment-config gate** (`Billing:Enabled`,
  `Signup:*`, `PublicApi`, `Webhooks`): a CI matrix entry runs the API in the gate's shipped default and executes
  the `[Category("Gate:<Section>")]` journeys; the lane is present in both workflow copies (R80). —
  `ForgejoCiParityTests` + `EnforcementGateTests` (TB-DOC-2). — TB-UI-66/67/68, BILL-10.
- **R127t [machine] — `[Explicit]` fixtures are excluded from sharding by category, and the suite size is
  derived, not typed.** The shard list is filtered by `TestCategory != NativeSmoke` (or the Explicit attribute),
  and `docs/stories/e2e.md`'s "Current suite size" is asserted against the non-explicit `[Test]` count. —
  TB-DOC-1; = R113 with the exclusion made a CI-shape assertion.
- **R128t [machine] — RCL components take the clock they schedule with.** Any `.razor` or `Shared.Ui` class
  using `PeriodicTimer`, `Task.Delay`, `DateTimeOffset.UtcNow` or `DateTime.UtcNow` injects `TimeProvider` (the
  `ComponentTestBase` `FakeTimeProvider` then drives it); the server clock gate is widened to `src/Shared.Ui`
  with a Loose-mode allowlist emptied within one wave. — Extend `ServerServices_UseInjectedClock_NotAmbientUtcNow`.
  — TB-UI-65, TB-UI-52's parameter workaround; TOOL-7/TOOL-8.
- **R129t [machine] — Test-id contract.** Every `data-testid` literal in `src/Shared.Ui` is referenced by at
  least one test (`tests/**`) or QA case; a test id referenced by a test must exist in a `.razor`. —
  `EnforcementGateTests` (TB-DOC-5). — FLAVORS SPEC-4 precursor; makes the TOOL-7 pages' coverage visible.
- **R130t [machine] — Every migration ships a Down-on-data walk.** The migrations test seeds one row per
  entity at migration N−1, applies N, mutates the new columns, applies Down and asserts row survival; a
  column-dropping Down states its data loss in the migration's XML doc. — Extend `MigrationsTests` (TB-TEN-27,
  TB-DOC-9). — closes "Down on a row with a rotation link".
- **R131t [review→machine] — Pre-auth cross-tenant reads are proven narrow with a recording double.** A
  repository method that bypasses the tenant filter before the caller has a tenant (`GetValidByEmailAcrossTenantsAsync`,
  `GetByTokenHashAsync`) ships a test with a recording decorator asserting which tenants were subsequently
  touched. — `ServiceHarness` gains `Recording<T>` decorators; arch scan pairs each `TagWith(RlsTags.CrossTenant)`
  site with a test naming it. — TB-TEN-11/12; extends the `TenantHatchGuard` from "where" to "how far".

TDD invariants restated for the delta (unchanged from v3 R99/R69 but now with the delta's evidence): no
production code without the failing test at the right layer; a slice is not "done" without happy-path +
permission-denied + **two-tenant** isolation; every new public method tested per branch **and per error path**
(the delta's `catch` blocks in `AdminConsole`/`Household`/`Login` are the untested majority); a test whose name
promises a negative must arrange the negative (R125t); QA plan + PDFs in the same PR (held — `check_qa_artifacts`
green).

## RULE_CONFLICTS candidates

- **C-t1 — R70 says the chassis exists; TOOL-7 shows the chassis is unused for the five heaviest pages.** No
  overturn: R70's text ("a test project exercises `src/Shared.Ui` `.razor` components") is satisfied by one
  component. Proposal for Phase 5: add a per-page floor (every `Pages/*.razor` has ≥1 bUnit test, or a
  per-file branch-coverage floor) rather than a project-level existence check.
- **C-t2 — R45 ("every endpoint gated by `RequireStaffAsync`… rejects `impersonated_by`") vs the minimal-API
  webhook writes.** TB-ADM-10 shows the delta's tenant-visible writes (`test`, `replay`) are neither
  `RequireStaffAsync`-gated nor audited, so R45/R50's attribution rule has no hook there. Not a contradiction —
  a scope hole: the rules speak of controllers; `WebhookEndpoints` is a `MapGroup`. Phase 5: state whether
  operational writes are exempt by name (R85 pattern) or must audit.
- **C-t3 — R73 (E2E is the confidence layer for RCL) vs R126t.** The E2E suite runs with both gates in their
  NON-default state, so "E2E-only confidence" for the gate pages is confidence in the state the platform does
  not ship. Phase 5 should say which layer owns gate-state proof (bUnit both states ✓ + one gate-off lane, per
  R126t) so R73 is not read as satisfied by a gate-on suite.
- **C-t4 — v3 R84-cand ("entity with `TenantId` but not `ITenantScoped` ships a dedicated cross-tenant test")
  was adjudicated review-only; `Replay_UnknownOrOtherTenant_ReturnsFalse` shows the name-level reading of
  "dedicated test" passes vacuously.** Not overturned; R125t is the machine half the adjudication lacked.
- **C-t5 — R15/R57 (injected clocks) exclude `Shared.Ui` by test scope (`ServerServices_UseInjectedClock…`
  comment: "the WASM client (no injected clock) … outside this scan").** The delta gave `AuthService` a
  `TimeProvider`, so the exclusion's premise is gone. Flag for Phase 5 to widen (R128t), not a contradiction.

## Out-of-area observations (one line each)
- Auth sibling: TB-TEN-24(c) — confirm `Inspect` checks the successor's `UserId` equals the predecessor's before
  granting the grace; the lines read do not show it.
- CI sibling: `ci.yml:1097` sanity floor `total ≥ 10` and the `awk NR % 3` split are the only guards on shard
  completeness; a name-collision (`Foo` ⊂ `FooBar`) runs a journey twice (DEP-22) — a parity fact could assert
  the anchored filter.
- Billing sibling: `AdminControllerTests` never boots gate-off; `Staff_CompForTenantA_LeavesTenantBUntouched`
  is the only two-tenant comp test and is gate-on.

## Unknowns needing a human decision
1. BILL-2 (comp while off): TB-ADM-8 + TB-UI-60 pin either answer — pick one.
2. BILL-1 (gate-blind vs fail-closed plan resolution): TB-ADM-9.
3. Audit or exempt the HOOKS operational writes under impersonation (TB-ADM-10 / C-t2).
4. R112 (403 mapped by body): TB-UI-49 is the landing test.
5. Broadcast emails: stamp the recipient's tenant or allowlist the tenant-less origin (TB-TEN-21 / TB-ADM-11).
6. Where the gate-off E2E lane runs (a 4th Forgejo port lane vs a per-test API restart) — R126t's cost.
