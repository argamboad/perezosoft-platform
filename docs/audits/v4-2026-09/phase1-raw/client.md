# CLIENT (Shared.Ui RCL + Web host + component/E2E tests) — v4 Phase 1 raw report
SHA: 8c8ed4a · BASE: f52be3d

## SUMMARY
Counts: Critical 0 · High 0 · Medium 4 (UX-6, UX-7, UX-8, UX-9) · Low 5 (UX-10, UX-11, UX-12, UX-13, UX-17) · Info 4 (UX-14, UX-15, UX-16, UX-18).

Headline: (1) The session keep-alive (#11) and the server's 60 s refresh-reuse grace (#10) are coupled by an
UNENFORCED timing invariant — the refresh `HttpClient` has the default 100 s timeout on both hosts, so a
lost-response refresh retried after `RenewRetryDelay` (30 s) lands ~130 s after the rotation, outside the grace,
and trips the theft response that revokes every session (the symptom the pair was meant to cure). (2) The client
reads any 400/403 as "the server said no" without looking at the body, so an infrastructure 403/400 (WAF, proxy,
cookie-too-large) deletes the native refresh token — contradicting the ADR-002 addendum's own wording. (3)
`BlazorBoot` retries re-issue the ORIGINAL navigation (a single-use magic link is re-spent) and boot reloads are
counted but never budgeted, so a flaky boot regression in our own startup code is absorbed silently. The #233
theme/locale "remembered value" approach — an accepted client-side race — is recorded only in code/test comments,
not as an ADR-022 amendment (R72 [review] violated). Everything else in the delta (billing-off UI, refused-signup
copy, i18n parity, system-bar sync, brand tokens) checks out; 6 new resx keys are EN/ES complete, no
`MarkupString`, no hardcoded strings introduced.

Rules proposed: R81c, R82c, R83c, R84c, R85c, R86c, R87c.

## Delta surface examined
PRs #11, #10 (client side + the coupled controller path), #203, #233, #231, #227, #222, #7, #16, #17, #18, #15.
Files read in full or by delta: `src/Shared.Ui/Auth/AuthService.cs` (full), `src/Web/Http/AuthHeaderHandler.cs`,
`src/Maui/Auth/NativeAuthHeaderHandler.cs`, `src/Shared.Ui/Layout/MainLayout.razor` (full + delta),
`src/Shared.Ui/Components/{ThemeSwitcher,LanguageSwitcher,SystemBarThemeSync,AppHeader}.razor` (+ `.css`),
`src/Shared.Ui/ISystemBarTheme.cs`, `src/Shared.Ui/AppResumeNotifier.cs`, `src/Shared.Ui/Auth/AuthErrorCopy.cs`,
`src/Shared.Ui/Pages/{Billing,Household,Join,Login}.razor` (delta + surrounding code),
`src/Shared.Ui/Resources/AppStrings.resx` + `.es.resx` (delta), `src/Shared.Ui/wwwroot/js/{bfcache-guard,theme}.js`,
`src/Shared.Ui/wwwroot/css/app.css` (delta), `src/Web/wwwroot/index.html`, `src/Maui/wwwroot/index.html`,
`src/Web/Program.cs` (DI + cache middleware), `src/Maui/MauiProgram.cs` (delta + AuthService DI),
`src/Maui/Platforms/Android/{AndroidSystemBarTheme,MainActivity}.cs`, `src/Api/Controllers/FeaturesController.cs`,
`src/Api/Controllers/AuthController.cs` (refresh delta), `src/Api/Services/RefreshTokenService.cs` (grace),
`src/Api/Program.cs` L252–282, `tests/Ui.Tests/{SessionKeepAliveTests,PreferenceSyncClaimTests,SystemBarThemeSyncTests,
BillingGateUiTests,SeatLimitCopyTests,SignupRefusedCopyTests,NotifyBillingTests}.cs`,
`tests/Ui.Tests/Infrastructure/{ComponentTestBase,TestHttpHandler,Fakes,TestJwt}.cs`,
`tests/Api.Tests/{BfcacheGuardTests,EnforcementGateTests,ArchitectureTests,ResourceParityTests}.cs`,
`tests/Api.Tests/Integration/FeaturesEndpointTests.cs`, `tests/E2E.Tests/{BlazorBoot,E2ETestBase,README}` + every
E2E delta, `.forgejo/workflows/ci.yml` e2e job (L975–1160) + `.github/workflows/ci.yml` e2e job head,
`docs/DECISIONS.md` ADR-002 addenda (L157–200) + ADR-028 addendum (L1519+), `docs/QA_TEST_PLAN.md` L164–172 + QA-SEC-03,
`docs/stories/e2e.md` L11, `docs/audits/v3-2026-07/FOUNDATION_RULES_v2.md` R62/R68/R70–R73/R80.

## Findings

### UX-6 · Medium · `src/Shared.Ui/Auth/AuthService.cs:64,199,761` + `src/Web/Program.cs:35-36` + `src/Maui/MauiProgram.cs:160` + `src/Api/appsettings.json:26`
**What.** The client keep-alive and the server reuse grace are coupled by a timing invariant nobody pins:
`refresh-call timeout + RenewRetryDelay ≤ RefreshToken:ReuseGraceSeconds`. Neither host sets a timeout on the
refresh `HttpClient` (default 100 s); the grace is 60 s.
**Why it matters.** The lost-response case the grace was added for (ADR-002 addendum 2026-09-18: "a refresh whose
response is lost") now has a client that retries it — but late. Sequence: server rotates T0→S1 at t≈0, response
lost; client's `PostAsync` hangs until the 100 s default timeout → `catch` → `Unreachable` (L228–233); timer
re-arms at `RenewRetryDelay` = 30 s (L761–762) → presents T0 at t≈130 s; server: revoked, `RotatedAt` 130 s old >
60 s → `Reuse` → `RevokeAllUserTokensAsync` (AuthController L175–176) → **every session of the user, web and
native, is revoked** — the exact "the web keeps forgetting me" symptom. Native is worse: the retry comes from the
stored T0 (the store was never updated), and the eventual 401 deletes it.
**Evidence.**
- `AuthService.cs:64` `public static readonly TimeSpan RenewRetryDelay = TimeSpan.FromSeconds(30);`
- `AuthService.cs:199` `response = await httpClient.PostAsync("/api/auth/refresh", null);` — no `CancellationToken`, no per-call timeout.
- `src/Web/Program.cs:35` `builder.Services.AddHttpClient("ApiAuth", client => client.BaseAddress = new Uri(apiBase))` — no `Timeout`; `grep -rn "Timeout\s*=" src/Web src/Maui src/Shared.Ui` finds only the OAuth loopback initiator.
- `src/Maui/MauiProgram.cs:160` `var authClient = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };` — default 100 s.
- `RefreshTokenService.cs:145` `if (now - rotatedAt > TimeSpan.FromSeconds(settings.ReuseGraceSeconds)) return false;`
- Startup path is fine by luck (`StartupRetryDelays[0]` = 2 s) only when the failure is fast (5xx); a hung request still burns 100 s first.
**Fix (one line).** Give the refresh call its own short timeout (`CancellationTokenSource(TimeSpan.FromSeconds(20))`
on `PostAsync`) and pin `RefreshTimeout + RenewRetryDelay < ReuseGraceSeconds` in a test.
**Gate.** New cross-project constant test in `tests/Api.Tests/Configuration/ConfigPostureTests.cs` (reads
`AuthService.RefreshTimeout`/`RenewRetryDelay` from the RCL and the `appsettings.json` default); behaviour test in
`tests/Ui.Tests/SessionKeepAliveTests.cs` (a hung refresh times out within N s).
**Confidence.** Likely (runtime path not executed here; every step is read from code; the 100 s default is .NET's documented `HttpClient.Timeout`).

### UX-7 · Medium · `src/Shared.Ui/Auth/AuthService.cs:202-208`
**What.** `RefreshOutcome.Rejected` is assigned to any 401/400/403 status without inspecting the body, so a 400 or
403 produced by infrastructure in front of the API is read as the server's verdict.
**Why it matters.** The addendum's promise is "only the server rejecting the refresh token ends a session"
(AuthService.cs:35–37). The API's own refusals on this route are always 401 with `ErrorResponse` JSON
(`invalid_refresh_token`, `user_not_found`; AuthController L179, L185); a 403 from a WAF/CDN challenge or a
maintenance page, or a 400 from a proxy header/cookie limit, is not a ruling — but on native it runs
`ClearSessionAsync` → `sessionStore.ClearAsync()` (L664–665) and the 30-day token is gone for good.
**Evidence.**
```
202  if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
203  {
204      // The server looked at the token and refused it (revoked, expired, unknown): it is dead.
206      await ClearSessionAsync();
207      return RefreshOutcome.Rejected;
```
Contrast the non-success branch right below (L209–215) which is careful about "a proxy's 502". The tests
(`SessionKeepAliveTests.Native_Rejected_ClearsTheStoredRefreshToken`) only cover 401 with a JSON body.
**Fix.** 401 → Rejected; 400/403 → Rejected only when the body parses to `ErrorResponse` with a known `error`
code, otherwise Unreachable.
**Gate.** `tests/Ui.Tests/SessionKeepAliveTests.cs` — theory `(403, "<html>…")` keeps the stored token; `(403, {"error":"…"})` clears it.
**Confidence.** Certain on the code; Likely on the operational exposure (Render's proxy behaviour not observed).

### UX-8 · Medium · `tests/E2E.Tests/BlazorBoot.cs:58,63-81` + `tests/E2E.Tests/MagicLinkJourneyTests.cs:32,48,55`
**What.** `BlazorBoot.GotoAsync(page, url)` retries a dead boot by re-issuing `page.GotoAsync(url)` with the
ORIGINAL url. For a single-use navigation the retry re-spends the token.
**Why it matters.** The magic-link journeys navigate to `/api/auth/magic-link/verify?token=…`; the server
consumes the token and redirects to `/auth-callback`. If THAT boot dies (ERR_NETWORK_CHANGED, banner), attempt 2
hits the verify endpoint again → `?error=invalid_link` → the app boots fine ("ok" verdict) → the journey fails
on `sign-out` visibility with no `[blazor-boot]` explanation of the real cause. The retry converts a reported
boot flake into an unexplained red journey — the opposite of the helper's purpose. Same shape for any future
OAuth-callback or one-time-code navigation.
**Evidence.**
```
58  public static Task GotoAsync(IPage page, string url) => BootAsync(page, () => page.GotoAsync(url), url);
66  for (var attempt = 1; ; attempt++) { … await navigate(); …
```
`MagicLinkJourneyTests.cs:32` `await BlazorBoot.GotoAsync(Page, link);` (link = the verify URL).
**Fix.** Retry with `page.ReloadAsync()` of the landed URL after the first attempt (or take a `retryWith` delegate; default = reload current URL).
**Gate.** None machine today; add a unit test of `BlazorBoot` with a fake `IPage` is heavy — a review rule (R83c) + README note.
**Confidence.** Certain on mechanism; the failure needs a dead boot at exactly that navigation (rare, but the PR was motivated by exactly such deaths).

### UX-9 · Medium · `tests/E2E.Tests/BlazorBoot.cs:22-23,71-79` + `.forgejo/workflows/ci.yml:1135-1139`
**What.** Boot reloads are bounded (3 per navigation, 60 s each) and reported, but never budgeted per run, and the
"banner" verdict is not distinguished from the "framework fetch failed" verdict when deciding to retry.
**Why it matters.** A regression in OUR startup code that throws intermittently (a race in `MainLayout.OnInitializedAsync`,
a JS interop failure in `theme.js`/`SystemBarThemeSync` first render) shows Blazor's banner → verdict `banner at NN%`
→ reloaded up to twice on EVERY navigation of EVERY journey, silently absorbed as long as it passes 1-in-3. The
Slowest-journeys step counts and prints (`boots = trx.count("[blazor-boot] attempt")`) and "never fails the job".
Worst case per navigation before failing: 3 × 60 s.
**Evidence.** `BlazorBoot.cs:71` `if (verdict == "ok") return;` — any non-ok verdict retries; `ci.yml:1136-1137`
`print(f"Blazor boot retries: {boots}" …)` with no threshold; the `pageerror:` lines needed to tell "our exception"
from "network" are already captured (L48) but unused in the decision.
**Fix.** Retry only on `fetch failed:`/`still loading` verdicts (network); on `banner` without any failed
`_framework` fetch since `seen`, fail immediately with the console; fail the step above N retries per shard.
**Gate.** `.forgejo/workflows/ci.yml` + `.github/workflows/ci.yml` Slowest-journeys step (R80 parity holds both).
**Confidence.** Certain.

### UX-10 · Low · `tests/Api.Tests/BfcacheGuardTests.cs:16-22` + `src/Shared.Ui/wwwroot/js/bfcache-guard.js:8-10`
**What.** The QA-SEC-03 guard's gate is a substring test: `Contains("pageshow")`, `Contains(".persisted")`,
`Contains("location.reload")`. A file reading `if (!e.persisted) location.reload()` passes. No E2E exercises Back
after sign-out (`grep GoBackAsync|pageshow|bfcache tests/E2E.Tests` → none); QA-SEC-03 stays manual.
**Why it matters.** This is a security control (stale authenticated view on a shared device) whose only machine
check is presence, not behaviour — the "vacuous test" pattern v3 T60 was closing. Also noted for the record: the
SPA shell is `Cache-Control: no-cache` (Program.cs:273), which does not inhibit bfcache, so the script is the sole
defence — correct that it exists; R62 is untouched and there is no header interaction to worry about. The guard
also reloads on EVERY persisted restore, signed-in or not (losing in-progress form state) — an accepted trade-off,
but it lives in a JS comment, not the ADR/QA plan. Native: MAUI loads it for parity; BlazorWebView never fires a
persisted `pageshow`, so it is inert there (comment in the file is accurate).
**Evidence.** `bfcache-guard.js:8-10`
```
window.addEventListener('pageshow', function (e) { if (e.persisted) window.location.reload(); });
```
`BfcacheGuardTests.cs:19-21` three `Assert.Contains` calls.
**Fix.** An E2E `SignOut_ThenBack_LandsOnLogin`; Playwright's Chromium ships `--disable-back-forward-cache` by
default, so the fixture must launch with `IgnoreDefaultArgs = ["--disable-back-forward-cache"]` for the test to
be non-vacuous (Suspected on that flag detail — verify against the installed Playwright version).
**Gate.** `tests/E2E.Tests` (new journey); keep `BfcacheGuardTests` as the presence gate but rename it so.
**Confidence.** Certain on the vacuity; Suspected on the Playwright flag.

### UX-11 · Low · `src/Shared.Ui/Layout/MainLayout.razor:128-136,140-151` + `AuthService.cs:656-668`
**What.** A mid-session `Rejected` (revoke-all elsewhere, the UX-6 theft response, a staff MFA reset, logout in
another tab) clears the session and raises `SignedOut`; `MainLayout.OnSignedOut` only clears device prefs, and
nothing navigates to `/login` — the `/login` bounce lives solely in `OnInitializedAsync` (L112–115).
**Why it matters.** The next render flips the layout to the anonymous branch (`else if (!Auth.IsAuthenticated) @Body`)
so the user sees the CURRENT protected page chromeless, with every call 401ing, until they reload. #11 makes this
reachable from three new paths (timer, per-request, resume — `OnResumed` even calls `StateHasChanged()` right
after, L146–147, guaranteeing the chromeless render). Before the delta the only mid-session refresh was the cold
start, so this was practically unreachable.
**Evidence.** `MainLayout.razor:128-136` `OnSignedOut` body is two `ClearAsync` calls; `AuthService.cs:666-667`
`if (wasAuthenticated) SignedOut?.Invoke();`.
**Fix.** In `OnSignedOut`, when the current path is not in `AnonymousPaths`, `Nav.NavigateTo("/login", forceLoad: true)`.
**Gate.** `tests/Ui.Tests/SessionKeepAliveTests.cs` — render `MainLayout`, stub refresh 401, advance past expiry, assert `nav.Uri` ends `/login`.
**Confidence.** Likely (render-flip reasoning from the razor; not executed).

### UX-12 · Low · `src/Shared.Ui/Auth/AuthService.cs:697-706,749-768`
**What.** Answer to "infinite retry? bounded?": mid-session the timer retries `Unreachable` at a FIXED 30 s forever
(no backoff, no cap: L761–762 `ScheduleRenewal(RenewRetryDelay)`), and the per-request path (`GetFreshAccessTokenAsync`)
adds one refresh attempt per API call once the token is inside the lead/expired, with no memory of the last failure.
**Why it matters.** With the server down and the app open, a 30 s wake-up loop indefinitely (background phone app,
until the OS kills it) plus one extra refresh per user click; on WASM background tabs are timer-throttled so the
effect is bounded by the browser, on native it is not. Not a correctness bug — the design note says "to be tried
again" — but unbounded fixed-rate retry is the pattern R-rules elsewhere avoid (outbox backoff).
**Evidence.** L761–762; L702–705 `if (token is not null && RenewalDue(token) > TimeSpan.Zero) return token; await RefreshAsync();`.
**Fix.** Exponential backoff capped (30 s → 5 min) for the timer; skip the per-request refresh when the last
`Unreachable` was < 10 s ago.
**Gate.** `tests/Ui.Tests/SessionKeepAliveTests.cs` (advance through several failures, count refreshes).
**Confidence.** Certain.

### UX-13 · Low · `src/Shared.Ui/Components/AppHeader.razor:74-78` + `AuthService.cs:573-585` + `Pages/Billing.razor:137-141`
**What.** Billing-off UX details (answers to Q4). The client learns the gate via anonymous `GET /api/features`
(`FeaturesController`, `[AllowAnonymous]`), cached for the app lifetime on success only; `_billingEnabled` starts
`false` so there is no link flash — but `AppHeader.OnInitializedAsync` awaits `IsStaffAsync()` THEN
`IsBillingEnabledAsync()` serially, and the long-lived header never re-probes: one transient probe failure at
startup hides the billing link for the whole session on a billing-ON deployment. `/billing`, `/billing/success`,
`/billing/cancel` all bounce silently to `/` (no message) — a bookmarked `/billing` just "does nothing".
Copy: `Household_ErrSeatLimitNoBilling`, `Join_FullBodyNoBilling` chosen by `_billingEnabled`; `Billing_EndedOn`
("Upgrade any time…") renders only on the gated page → no upgrade offer reachable when off. Cache across sign-in/
tenant switch: intentional (deployment config), correct.
**Evidence.** `AppHeader.razor:76-77` two sequential awaits; `AuthService.cs:583` `return false; // don't cache transient failures`
(true for the service, but the header caches the `false` it got).
**Fix.** `await Task.WhenAll(...)` in the header and re-read the flag on `SignedIn`/`IdentityChanged`; optionally a one-line toast on the `/billing` bounce.
**Gate.** `tests/Ui.Tests/BillingGateUiTests.cs` — features probe 502 first, 200 later → link appears after `SignedIn`.
**Confidence.** Certain.

### UX-14 · Info · `src/Shared.Ui/Pages/Login.razor:196-206,351-358` vs `src/Shared.Ui/Auth/AuthErrorCopy.cs:17-22`
**What.** Two mapping tables for the same server codes. Web OTP verify maps ANY 403 to `Login_ErrSignupNotAllowed`
by status alone (L357 `System.Net.HttpStatusCode.Forbidden => Localizer["Login_ErrSignupNotAllowed"]`) while the
native path reads the body's `error` through `AuthErrorCopy.OtpErrorKey`; the query-string `?error=` switch is a
third inline table. A proxy/WAF 403 on the web reads as "private testing".
**Q5 verdict (enumeration).** The server decides; the client renders only from the server's code or a known query
value, unknown `?error=` falls to the generic line — no raw echo (R73 respected). The refusal fires only after a
CORRECT code / valid link / OAuth identity, so it reveals green-list membership only to someone who already
controls the mailbox/identity — inherent to the feature, not an oracle for account existence. OK.
**Fix.** Route the web 403 through `OtpErrorAsync(res)` (it already reads the body and `AuthErrorCopy` handles `signup_not_allowed`); fold the query switch into `AuthErrorCopy`.
**Gate.** `tests/Ui.Tests/SignupRefusedCopyTests.cs` — 403 with `{"error":"other"}` → generic copy.
**Confidence.** Certain.

### UX-15 · Info · `.forgejo/workflows/ci.yml:989,1094-1100` + `docs/stories/e2e.md:11` + `docs/QA_TEST_PLAN.md:168` + `CLAUDE.md:174`
**What.** Suite count: `[Test]` = 35 = 34 browser journeys + 1 `[Explicit] [Category("NativeSmoke")]`
(`NativeSmokeTests`). ci.yml comments and CLAUDE.md's FLAVORS row say "35 journeys"; `e2e.md` and the QA plan say
34. The shard script's `--list-tests` counts the explicit method in `$total` and assigns it to a shard by name
(`FullyQualifiedName~…`); it does not run today (runs are green — the NUnit adapter's Strict explicit mode is the
only thing keeping it out, Suspected), but a future adapter setting or `[Category]` filter would launch the native
smoke inside the browser job. Substring collisions among the 35 names: none (checked).
**Fix.** Add `& Category!=NativeSmoke` to the shard filter (or drop it from `names`); make the doc count derived (R75 extension).
**Gate.** `tests/Api.Tests/EnforcementGateTests.cs` (R75) — assert `e2e.md` "Current suite size" == count of non-explicit `[Test]`.
**Confidence.** Certain on counts; Suspected on adapter semantics.

### UX-16 · Info · `src/Shared.Ui/Resources/AppStrings.resx:86-87`
**What.** `Login_ErrInvalidLink` gained a line break between `<data …>` and `<value>` — whitespace outside
`<value>`, so the string is unchanged (verified: the ES file and `ResourceParityTests` compare keys, not format).
Cosmetic; the only key in the file formatted that way. i18n answer (Q6): six new keys — `Login_ErrSignupNotAllowed`,
`Billing_EndedOn`, `Billing_CheckoutSuccess`, `Billing_CheckoutCancelled`, `Join_FullBodyNoBilling`,
`Household_ErrSeatLimitNoBilling` — all present in EN and ES; `ResourceParityTests.EveryNeutralKey_HasATranslation_AndNoOrphans`
holds them; `grep MarkupString src/Shared.Ui` → none; the changed .razor files introduce no literal user text (the
only literals found — "Perezosoft" brand text, "Loading…", `aria-label="Toggle navigation"` — predate the delta).
**Fix.** Re-join the two lines. **Gate.** none needed. **Confidence.** Certain.

### UX-17 · Low · `src/Shared.Ui/Auth/AuthService.cs:641-654,797-801` + `Components/ThemeSwitcher.razor:64-71`
**What.** Q3 answer + a residual race. The #233 race: the reconcile trusts the in-memory token's `theme`/`locale`
claim; native keeps that token across a WebView reload, so a saved preference was reverted to the claim. The fix is
neither single-flight nor versioned: it is a shadow value (`RememberTheme/Locale`) read ahead of the claim and
wiped by `ForgetRememberedPreferences()` on EVERY `AcceptTokensAsync` (L647). With keep-alive, renewals are now
routine: a renewal in flight when the PUT commits mints a token with the OLD claim, then wipes the NEW remembered
value → the next native reload reconciles to the old theme/locale again (the symptom #233 fixed) until the next
token. Narrow (one in-flight refresh vs one PUT), but the keep-alive made in-flight refreshes ~hourly per session.
PREFS-1 invariants still hold: "system" is stored verbatim (ThemeSwitcher PUTs the raw value; `theme.js` maps
unknowns to `system`; reconcile compares strings), and reconcile-on-every-sign-in is intact — note that a LATE
renewal after expiry now also raises `SignedIn` (L652–653, `!wasAuthenticated`) and triggers an extra idempotent
reconcile mid-session (harmless; contradicts the event's doc comment "NOT raised on mid-session token rotation").
**Fix.** Forget only on `ClearSessionAsync`/a different `sub`, or keep the remembered value when the new token's claim differs and `iat` predates the PUT.
**Gate.** `tests/Ui.Tests/PreferenceSyncClaimTests.cs` — `Http.OnGated` the refresh, change theme, release the gate, assert `Auth.Theme == "light"`.
**Confidence.** Likely.

### UX-18 · Info (R72 [review] violation — cite) · `docs/DECISIONS.md` (no ADR-022 amendment) vs `Components/ThemeSwitcher.razor:67-70`, `LanguageSwitcher.razor:54-58`, `tests/Ui.Tests/PreferenceSyncClaimTests.cs:11-23`
**What.** R72: "Any accepted client-side race is an ADR amendment, not a commit-message aside." #233 records two
accepted client-side races — the abandoned refresh-after-save that rotated the cookie under a reload and signed
users out (JiggerJot run 35128350182), and the chosen "remembered, not refreshed" shadow — only in code comments
and the test's `<summary>`. `grep -n "Remember\|2026-09-16\|rotated the web" docs/DECISIONS.md` finds no ADR-022
amendment (L523 is JOBS-4). ADR-028 DID get its BlazorBoot addendum (L1519), so the process was followed for #17/#18
and skipped for #233.
**Fix.** One ADR-022 amendment paragraph (2026-09-16) describing the shadow and why a refresh is wrong there.
**Gate.** review (R72). **Confidence.** Certain.

## Answers to the brief's questions not already a finding
- **Q1 trigger/seam/leaks.** Renew triggers: (a) timer at `exp − min(1 min, lifetime/4)`, floored at 30 s, capped 1 day
  (L709–738); (b) before every request via both bearer handlers (`GetFreshAccessTokenAsync`); (c) `AppResumeNotifier`
  → `MainLayout.OnResumed` (MAUI wires `Resumed` AND `Activated`, App.xaml.cs:24–25; web never fires it). Clock seam:
  `TimeProvider` ctor param, `FakeTimeProvider` in the chassis (`ComponentTestBase.Time`), timers via
  `Time.CreateTimer` — fully testable, and the tests do drive it. Disposal: `ScheduleRenewal` disposes the previous
  timer under `_refreshGate`; `ClearSessionAsync`/`BeginImpersonation` → `CancelRenewal`; `_refreshInFlight` still
  cached only when running (v3 T45c preserved, L167–173) and cleared in `finally` — no stale-task regression.
  Impersonation: timer cancelled, `GetFreshAccessTokenAsync` returns the impersonation token untouched, timer
  re-armed by the refresh in `StopImpersonationAsync`. Hidden tab/bfcache: JS timers are throttled/suspended; the
  per-request net covers it; a bfcache restore reloads the page (fresh AuthService) — no fight.
- **Q1 double rotation.** Within one AuthService the three paths share `_refreshInFlight` — no double rotation.
  Across web tabs (separate WASM instances, one cookie jar) collisions are now ~hourly-per-tab rather than
  per-boot, so the client fix RAISES the base rate of the benign race the server grace absorbs; the server fix
  alone leaves the daily sign-in. Neither alone is correct; together they are, EXCEPT for the lost-response timing
  (UX-6). The grace path issues a second live chain and revokes nothing (AuthController L188–200) — the shared
  jar keeps the last Set-Cookie; the orphan chain expires unused. Consistent.
- **Q2 native.** Loaded in `src/Maui/wwwroot/index.html:35` for R68 parity; inert in BlazorWebView. R68's
  `HostIndexHtml_ReferenceTheIdenticalRclScriptSet` holds the set equal; `BfcacheGuardTests` pins presence per host.
- **Q7 sharding.** Each shard is its own job with its OWN `services:` Postgres + Mailpit (per-job containers) on a
  lane that takes one job at a time → no shared DB/mail state across shards; within a shard NUnit runs fixtures
  sequentially (no `[Parallelizable]` anywhere) → no journey interference. Cost: three stack boots. Unique-email
  helpers make journeys DB-independent anyway.

## Template-readiness notes
- **New core→slice burden (documented):** any page reading a config gate must `StubFeatures()` in its bUnit test or
  it is silently redirected home (ComponentTestBase L62–69 says so). A new gate = new key on `FeaturesResponse` +
  new `IsXxxEnabledAsync` (flat/additive, fine) — but each page copies the same `_billingEnabled` field/await
  pattern (Household, Join, Billing, AppHeader): a `<FeatureGate Name="billing">` component would remove the copy.
- **Rebrand burden:** `SystemBarColors` constants must equal `app.css` tokens (`NativeChromeGateTests` enforces;
  REBRANDING §4 lists it) — machine-held, good. The `app.css` Bootstrap-derived token restatements
  (`--bs-primary-text-emphasis/bg-subtle/border-subtle`, the checkbox/switch SVG with a hardcoded `%23465d4d`)
  are a second place a rebrand must touch; the switch-knob data-URI colour is NOT token-driven (`app.css` delta,
  `fill='%23465d4d'`) — a rebrand that changes `--bs-primary` leaves a sage focus knob. Doc-only practice today.
- **E2E convention:** every full navigation must go through `BlazorBoot` (BasePage does; a downstream journey
  calling `page.GotoAsync` directly bypasses it silently) — needs a grep gate (R83c).
- **Chassis:** `FakeTimeProvider`, `OnUnreachable`, `OnSequence`, `FakeFileDownloadLauncher` are real R70 chassis
  additions; new components/behaviours in the delta are ALL bUnit-covered (keep-alive 14 tests, prefs 4,
  system-bar 4, gates 3+4+3, billing return pages 3) — R70 satisfied; the two E2E-only gaps are QA-SEC-03 (UX-10)
  and the boot-retry behaviour.

## Candidate rules
- **R81c — [machine] —** The client's refresh call carries an explicit timeout `T` and retry pause `D` with
  `T + D < RefreshToken:ReuseGraceSeconds` (server default), so a lost-response retry is always inside the grace.
  Enforcement: `ConfigPostureTests` cross-reads `AuthService.RefreshTimeout`/`RenewRetryDelay` and the
  appsettings default; `SessionKeepAliveTests` proves the timeout fires. Subsumes UX-6.
- **R82c — [machine] —** "The server said no" means 401, or 400/403 whose body is a parsable `ErrorResponse`
  with a known code; every other status/exception is Unreachable and keeps the stored credential.
  Enforcement: `SessionKeepAliveTests` theory over status × body. Subsumes UX-7.
- **R83c — [machine] —** E2E full navigations go through `BlazorBoot` (grep gate: no `page.GotoAsync(`/`ReloadAsync(`
  outside `BlazorBoot.cs`/`NativeSmokeTests.cs`); a boot retry reloads the LANDED url, never re-issues the original
  navigation; retries are budgeted per shard and only network verdicts retry. Enforcement: `EnforcementGateTests`
  grep + the Slowest-journeys step threshold (both workflow copies, R80). Subsumes UX-8, UX-9.
- **R84c — [review] —** A `wwwroot/js` file introduced as a security control ships a behaviour test (E2E with the
  relevant browser feature enabled) — a substring test is a presence gate and is named as such. Subsumes UX-10.
- **R85c — [machine] —** A `SignedOut` transition while on a protected route navigates to `/login` from ONE place
  (`MainLayout.OnSignedOut`). Enforcement: bUnit `MainLayout` test. Subsumes UX-11.
- **R86c — [machine] —** Server auth error codes map to copy in one table (`AuthErrorCopy`) consumed by every
  surface (query string, web status+body, native body); no inline code-literal switches in `.razor`.
  Enforcement: `SignupRefusedCopyTests` + an arch grep for `"signup_not_allowed" =>` outside `AuthErrorCopy`. Subsumes UX-14.
- **R87c — [machine] —** `--list-tests`-driven sharding excludes `[Explicit]` fixtures, and the "suite size" in
  `docs/stories/e2e.md` is asserted equal to the non-explicit `[Test]` count (R75 extension). Subsumes UX-15.

## RULE_CONFLICTS candidates
- None overturned. Note only that R72's "ADR amendment" clause is [review] and was skipped for #233 (UX-18) while
  followed for #17/#18 — evidence that a machine half (a test that every `// (found downstream, <date>)` comment in
  `src/Shared.Ui` has a matching `docs/DECISIONS.md` date) would be cheap.

## Out-of-area observations
- Server: `AuthController` refresh grace issues a fresh session with the presented token's `Provider` and revokes
  nothing — the audit log should record the grace path per user (currently Information log only); `RefreshReplayTests` cover it.
- Server: `SignupNotAllowedException` on magic-link verify spends the token before refusing (comment says fine) — a
  green-listed-later user must request a new link; worth a QA plan line.
- CI: the e2e job comment "35 journeys" and `docs/QA_TEST_PLAN.md` "34 journeys" disagree (UX-15) — R75 doc-sync area.
- MAUI: `window.Activated` also fires `Notify()` (App.xaml.cs:25) — on desktop every focus change now calls
  `GetFreshAccessTokenAsync` (cheap when not due) and `Billing.OnAppResumed` refetches `/api/billing` on each
  alt-tab while on that page.

## Unknowns needing a human decision
1. Accept UX-6 by shortening the refresh timeout (client change) or by widening the grace (server change, weakens
   the theft trade-off)? The client change is the safer one.
2. Should a bfcache restore for a SIGNED-IN user reload (current) or be left alone (keeps form state)? Either way,
   record it in ADR/QA-SEC-03.
3. Playwright bfcache flag (`--disable-back-forward-cache`) — confirm against the pinned Playwright version before
   writing the QA-SEC-03 journey.
4. Whether the NUnit adapter's explicit handling is relied upon deliberately (UX-15) or the shard filter should exclude the category explicitly.
