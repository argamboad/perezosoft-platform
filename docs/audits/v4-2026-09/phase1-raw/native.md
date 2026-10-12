# NATIVE (MAUI shells) — v4 Phase 1 raw report
SHA: 8c8ed4a · BASE: f52be3d

## SUMMARY
Counts: Critical 0 · High 0 · Medium 3 (NAT-12, NAT-13, NAT-14) · Low 4 (NAT-15, NAT-16, NAT-17, NAT-18) · Info 4 (NAT-19..22).
Headline: (1) the native Bearer handler (re-touched by #11) still attaches the JWT to EVERY request on the default client, and the native download launcher uses that client for the S3 presigned URL — token to a third-party origin and a Likely 400 from S3; (2) #213's Release signing fallback is Windows-only and its verification is print-only, so the exact v1-only trap it fixed returns silently on a Mac; (3) R67 is declared [machine] but nothing in CI ever runs an Android Release build, so both Release-only csproj blocks (API base guard + signing) have zero standing coverage.
Rules proposed: R81n (Bearer scoped to the API origin), R82n (CI Release leg = R67's missing gate + apksigner v2 assert), R83n (signing material gitignored + gated), R84n (Android manifest posture gate incl. `allowBackup=false`), R85n (native classifier regex positively asserted per R60 file class).
Answers to the nine brief questions are inline under each finding and in "Delta surface examined" (items verified clean are listed there, not as findings).

## Delta surface examined
PRs: #231, #230, #213, #224, #223, #197, #7, #11 (its native half), #203 (bfcache guard), LOCALCI-3/#229 (classifier regex), plus the Android-smoke commits (be9b3c9 …).
Files read in full: `src/Maui/Perezosoft.Maui.csproj`, `tools/publish-native.ps1`, `.gitignore`, `src/Maui/MauiProgram.cs`, `src/Maui/Auth/{NativeAuthHeaderHandler,DebugFileSessionStore,LoopbackOAuthInitiator,WebAuthenticatorOAuthInitiator}.cs`, `src/Maui/ShareFileDownloadLauncher.cs`, `src/Maui/Platforms/Android/{AndroidSystemBarTheme.cs,MainActivity.cs,AndroidManifest.xml,Resources/values/colors.xml,Resources/xml/network_security_config.xml,network_security_config_release.xml}`, `src/Maui/Platforms/MacCatalyst/Entitlements{,.Debug}.plist`, `src/Maui/wwwroot/index.html`, `src/Web/wwwroot/index.html`, `src/Web/Http/AuthHeaderHandler.cs`, `src/Shared.Ui/ISystemBarTheme.cs`, `src/Shared.Ui/Components/SystemBarThemeSync.razor`, `src/Shared.Ui/wwwroot/js/{theme.js,bfcache-guard.js}`, `src/Shared.Ui/Auth/AuthService.cs` (RefreshAsync / GetFreshAccessTokenAsync), `src/Shared.Ui/Layout/MainLayout.razor` (delta), `tests/Ui.Tests/SystemBarThemeSyncTests.cs`, `tests/Api.Tests/{NativeChromeGateTests,EnforcementGateTests,BfcacheGuardTests}.cs`, `tests/native-smoke-android/smoke.js`, `tests/E2E.Tests/NativeSmokeTests.cs`, `docs/brand/build_assets.py`, `.github/workflows/ci.yml` + `.forgejo/workflows/ci.yml` (classifier + native jobs), `docs/DEPLOYMENT.md` §9, `docs/NEW_APP_GUIDE.md` Phase 9, deltas of `docs/{NATIVE_PARITY,MOBILE_TESTING}.md`, `docs/stories/native.md`, `docs/REBRANDING.md` grep, `docs/QA_TEST_PLAN.md` grep, v3 `AUDIT_REPORT.md` NAT-8..11 rows + `IMPLEMENTATION_TRACKER.md` T33/T35/T36.

Verified clean (no finding):
- **Q2 `DebugFileSessionStore`**: still `#if MACCATALYST && DEBUG` at both the file (`DebugFileSessionStore.cs:1`) and the DI branch (`MauiProgram.cs:130-136`); only the doc comment changed in the delta. Path is a fixed constant (`Path.Combine(ApplicationData, "perezosoft-debug-session")`, `:18-20`) — nothing configurable, no traversal surface. 0700 dir + atomic 0600 create via `FileStreamOptions.UnixCreateMode` kept (`:42`, `:48-56`). Caveat: no test asserts the `#if`; it is compiled only on the Apple leg (see NAT-14 / out-of-area).
- **Q5 initiators**: Loopback keeps per-flow `state` (32 random bytes, `LoopbackOAuthInitiator.cs:31`), constant-time compare (`:90-93`), binds `http://127.0.0.1:{port}/` only (`:27`), random port via `TcpListener(IPAddress.Loopback, 0)` (`:95-107`), 5-min timeout (`:23`, `:55`). The only delta is `apiBaseUrl.TrimEnd('/')` (`:47`; `WebAuthenticatorOAuthInitiator.cs:26`). API-side redirect allow-list unchanged: `NativeLogin`/`NativeCallback` both reject with `IsAllowedNativeRedirect` → `NativeRedirectPolicy.IsAllowed(redirect, appSettings.NativeCallbackScheme)` (`NativeAuthController.cs:38,68,144-145`); the controller's delta is only the `signup_not_allowed` redirect (`:102-105`). No CancellationToken on the loopback wait (pre-existing, not delta).
- **Q6 entitlements**: `Entitlements.Debug.plist` changed comment-only (NATIVE-9 → ADR-024 wording). Debug plist = `com.apple.security.network.client` only (`:14-17`); Release plist = sandbox + network.client + `keychain-access-groups` (`Entitlements.plist:5-20`). The swap is scoped `maccatalyst And '$(Configuration)' == 'Debug'` (`csproj:68-70`), so the Debug set (a strict subset, unsandboxed) cannot leak into Release, and Release cannot lose the keychain group. iOS ships no Entitlements.plist (implicit keychain) — unchanged.
- **Q4 system bars**: null-safe (`activity?.Window is not { } window` → return, `Platform.CurrentActivity` may be null, `AndroidSystemBarTheme.cs:24,63-65`); marshalled via `MainThread.InvokeOnMainThreadAsync`; the JS `watch` is issued in `OnAfterRenderAsync(firstRender)` (`SystemBarThemeSync.razor:29-37`) i.e. after the WebView is live; system-theme changes under "system" reach the bar through `media.addEventListener('change', …)` (`theme.js:30`) and the Activity declares `ConfigChanges.UiMode` (`MainActivity.cs:10`) so a night-mode toggle does not recreate it; the boot state is painted from `UiMode.NightMask` in `OnCreate` (`:28-29`). Off-Android: registration is `#if ANDROID` only (`MauiProgram.cs:102-106`) and `OnTheWeb_ItStaysOutOfTheWay` proves zero JS calls when no `ISystemBarTheme` is registered. `NativeChromeGateTests` holds the constants to `app.css` tokens.
- **Q7 smokes**: `smoke.js` retries the BOOT phase once, and attempt 2 failing throws → `process.exit(1)` (`:70-79`, `:125`); the roster assertion is strict (`members !== 1` → throw, `:119-120`). `NativeSmokeTests.cs` delta is timeouts only. See NAT-20 (Info) for the goto fallback.
- **Q8 host parity**: both `index.html` reference the same RCL set `theme.js` (in `<head>` before the first stylesheet in both), `qrcode-generator.min.js`, `mfa-qr.js`, `bfcache-guard.js`; `HostIndexHtml_ReferenceTheIdenticalRclScriptSet` (R68) + `BfcacheGuardTests` pin it. WebView safety of the guard: see NAT-21 (Info).
- **Q9 brand (#7)**: `git diff --stat f52be3d..HEAD -- src/Infrastructure/Email/Assets` → `logo.png | Bin 28557 -> 10004 bytes` — **the email logo WAS updated**. Every raster `build_assets.py` writes (§3 list: `*_1024/*_1520`, `favicon.{ico,png}`, `apple_touch_180`, `icon-192/512/maskable`, `og_image`, `Email/Assets/logo.png`, the five `docs/brand/*.png`) shows in the delta stat; the SVG-only assets (`favicon.svg`, `appiconfg.svg`, `splash.svg`, `brand/*.svg`) were hand-swapped. All in-tree references (`AppHeader.razor:13`, `Home.razor:13,26`, `Login.razor:18`, `app.css:70`, `Web/index.html:14-20`, `BrandedEmail.cs:58,174`) resolve to files that exist; no stale raster name found. Residual: NAT-22 (Info).

## Findings

### NAT-12 · Medium · `src/Maui/Auth/NativeAuthHeaderHandler.cs:17-19` (+ `src/Maui/ShareFileDownloadLauncher.cs:16`, `src/Maui/MauiProgram.cs:108-112`)
**What.** The native Bearer handler attaches the access token to every request that goes through the default `HttpClient`, with no check that the request targets the API origin. The native download launcher deliberately uses that client for the signed download URL — which, with `Files:Provider=S3` (FILES-3), is an S3/MinIO presigned URL on a third-party host.
**Why it matters.** (a) The tenant-scoped access JWT is sent to a non-API origin (credential leak to whoever terminates that host — provider, CDN, a mis-set `PublicBaseUrl`). (b) A SigV4 presigned GET that also carries an `Authorization` header is rejected by AWS S3 with `InvalidArgument: Only one auth mechanism allowed` → the native household export download fails whenever S3 storage is on. (c) The delta made this worse in kind: the handler now calls `GetFreshAccessTokenAsync()` first, so a third-party fetch can also spend a refresh rotation.
**Evidence.**
```csharp
// NativeAuthHeaderHandler.cs:17-19
var token = await auth.GetFreshAccessTokenAsync();
if (!string.IsNullOrEmpty(token))
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
// ShareFileDownloadLauncher.cs:16
using var response = await http.GetAsync(url);
// MauiProgram.cs:109-110 (comment)  "Uses the default (Bearer) client registered below: the signed URL itself
// needs no auth, but absolute URLs bypass BaseAddress so the same client serves both."
// S3FileStorage.cs:73-75  GetDownloadUrlAsync → s3.GetPreSignedURL(...)   (third-party host)
// Household.razor:426,439  _exportUrl = new Uri(Http.BaseAddress!, u) … DownloadLauncher.LaunchAsync(_exportUrl, …)
```
The web path is unaffected (`BrowserFileDownloadLauncher` navigates; no header). `src/Web/Http/AuthHeaderHandler.cs:13-15` has the identical unscoped shape but no current absolute-URL caller.
**Fix (one line).** Attach the header only when `request.RequestUri` is null/relative or its scheme+host+port equal the API base origin (or give `ShareFileDownloadLauncher` a plain `HttpClient`).
**Gate.** Move the logic into a Shared.Ui `BearerScopedHandler` used by both hosts and test it in `tests/Ui.Tests` with `TestHttpHandler` (absolute third-party URL ⇒ no `Authorization`); arch test in `tests/Api.Tests/ArchitectureTests.cs` banning `new AuthenticationHeaderValue("Bearer"` outside that class.
**Confidence.** Certain for the leak (code path), Likely for the S3 400 (AWS-documented; MinIO not verified here — cannot run). Not filed in v3 (grep of the four v3 reports for the launcher / "Bearer" finds nothing); the handler is delta code (#11).

### NAT-13 · Medium · `src/Maui/Perezosoft.Maui.csproj:132-138` + `tools/publish-native.ps1:52-67`
**What.** #213's "Release always signs through apksigner" relies on a Windows-only path, and the script's signature check is print-only.
**Why it matters.** On macOS/Linux (`$(LOCALAPPDATA)` empty) the `Exists('\Xamarin\Mono for Android\debug.keystore')` condition is false, so `AndroidKeyStore` is never set and the build takes the very jarsigner/v1-only route the PR fixed (per the csproj's own claim). The script then still finds a `*-Signed.apk`, prints "(no apksigner or JDK found — signature NOT verified)" in yellow and "Send THIS file" in green — the silent-sideload failure returns with a green message. Even on Windows, `apksigner verify` output is only piped through `Select-String`; a `v2 … false` line never fails the script, contrary to DEPLOYMENT §9 ("if you do not see it, do not ship").
**Evidence.**
```xml
<!-- csproj:132 -->
<PropertyGroup Condition="'$(Configuration)' == 'Release' And '$(AndroidKeyStore)' == '' And Exists('$(LOCALAPPDATA)\Xamarin\Mono for Android\debug.keystore')">
```
```powershell
# publish-native.ps1:58-64
$bt = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Android/Sdk/build-tools") -Directory -ErrorAction SilentlyContinue | ...
if ($bt -and ($env:JAVA_HOME -or (Get-Command java -ErrorAction SilentlyContinue))) {
    & (Join-Path $bt.FullName "apksigner.bat") verify --verbose $apk 2>&1 | Select-String "Verified using v[123] "
} else {
    Write-Host "(no apksigner or JDK found — signature NOT verified)" -ForegroundColor Yellow
}
```
Also: the SDK probe looks only under `%LOCALAPPDATA%\Android\Sdk` (Android Studio's), while the VS-installed SDK lives under Program Files (x86) — on a VS-only box verification is skipped too.
**Fix (one line).** In the csproj, probe the platform debug-keystore locations (`$(HOME)/.local/share/Xamarin/Mono for Android/debug.keystore`, `$(AndroidDebugKeyStore)`) and `<Error>` a Release build that resolved no keystore; in the script, parse the verify output and `throw` unless `v2 … true` or `v3 … true`, and locate `apksigner` via `$(AndroidSdkDirectory)`/`ANDROID_HOME` before the LocalAppData guess.
**Gate.** None today — see NAT-14; the R82n leg would run `apksigner verify` in CI.
**Confidence.** Likely (the "v1-only without a keystore" behaviour is the PR author's empirical claim; I cannot run an Android Release build here). The Windows-only condition and the non-failing verify are Certain.

### NAT-14 · Medium · `.github/workflows/ci.yml:250,384,714` / `.forgejo/workflows/ci.yml:304,482,856` vs `csproj:132-164` — **R67 has no standing gate**
**What.** R67 [machine] ("Native Release builds fail when dev wiring survives") is enforced only by the csproj targets themselves (`RequireApiBaseUrlInRelease`, the Release-only network-config swap, and now #213's signing block). Every MAUI build CI performs is `-c Debug`; no test references `RequireApiBaseUrlInRelease`, `network_security_config_release` or `AndroidSigning*` (grep over `tests/`, both workflows, `tools/` — zero hits outside node binaries).
**Why it matters.** A regression in any Release-only block (a typo in the `Condition`, an `AndroidPackageFormat`/RID interaction, the NAT-13 host gap) is invisible until a human sideloads a phone. The delta doubled the amount of never-exercised Release-only MSBuild logic.
**Evidence.** `ci.yml:250 dotnet build src/Maui/Perezosoft.Maui.csproj -f ${{ matrix.tfm }} -c Debug …`; `:714 … -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true`; `IMPLEMENTATION_TRACKER.md:72` T33 marks R67 ✅ with "gate: yes" — the gate is the csproj `<Error>`, which CI never reaches.
**Fix (one line).** Add a dispatch/Monday `native-release-android` leg: `dotnet publish -f net10.0-android -c Release -p:ApiBaseUrl=https://release.invalid -p:AndroidPackageFormat=apk`, then `apksigner verify --print-certs` asserting v2/v3 true, plus a negative step proving `-c Release` without `ApiBaseUrl` fails.
**Gate.** New CI job in both workflow copies (R80 parity) + `EnforcementGateTests` asserting the job exists and is on the dispatch input list.
**Confidence.** Certain.

### NAT-15 · Low · `docs/DEPLOYMENT.md:405-406`, `docs/NEW_APP_GUIDE.md:284-286`, `tools/publish-native.ps1:16-24`, `.gitignore`
**What.** Signing-material handling is inconsistent and unguarded. (a) DEPLOYMENT §9 tells the operator to pass `-p:AndroidSigningKeyPass=… -p:AndroidSigningStorePass=…` on the command line (shell history, process list, any `-bl` binlog), while NEW_APP_GUIDE says "the four signing props from env". (b) `publish-native.ps1` has no signing parameters, so a store build must bypass the script — and with it the signed-APK pick and the verify step it exists for. (c) `.gitignore` covers `out/` but not `*.jks`, `*.keystore`, `*.p12`, `*.pfx`, `*.mobileprovision`; `.gitleaks.toml` has no keystore rule (binary) — a `release.jks` generated in the repo root per Phase 9 step 1 commits silently. (d) NEW_APP_GUIDE Phase 9 says "assert the artifact with `jarsigner -verify`" — jarsigner passes a v1-only APK that Android 11+ refuses, contradicting DEPLOYMENT §9's v2 requirement. (e) The Release-signed-with-debug-key default documents the forward case ("a sideload upgrades over one") but not the reverse trap: a store-key build cannot install over a debug-key sideload (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`) without an uninstall.
**Answers to Q1.** No keystore or real password is committed — the only literals are the public Android debug defaults (`androiddebugkey`/`android`, `csproj:135-137`). Signing is conditioned on `'$(Configuration)' == 'Release'` only; a Debug build takes the SDK default debug key and cannot pick up the release key unless the operator passes `AndroidKeyStore=true` + `AndroidSigning*` explicitly. Passing only the four `AndroidSigning*` props (as §9 instructs) works by accident: the fallback block sets `AndroidKeyStore=true` and each `Condition="'$(AndroidSigningX)' == ''"` yields to the supplied value — but only on a Windows host where the debug keystore exists (NAT-13); elsewhere the store props are ignored.
**Fix (one line).** Script params `-KeyStore/-KeyAlias` with passwords read from `ANDROID_SIGNING_STORE_PASS`/`ANDROID_SIGNING_KEY_PASS` env; add the gitignore patterns; reconcile the two docs to `apksigner`.
**Gate.** `EnforcementGateTests`: assert `.gitignore` contains each signing-material pattern (R83n).
**Confidence.** Certain.

### NAT-16 · Low · `.github/workflows/ci.yml:329` / `.forgejo/workflows/ci.yml:425` — R60 regex vs rule text
**What.** The `native` classifier regex lacks `Directory\.Build\.props`, which R60 names explicitly and v3 T35 recorded as added ("native-paths regex adds `Directory.Build.props`", `IMPLEMENTATION_TRACKER.md:74`). The regex was rewritten into the `changes` job in the delta (LOCALCI-3, #229) still without it; the BASE regex (`git show f52be3d:.github/workflows/ci.yml:268`) did not have it either. `tools/publish-native.ps1` matches neither `code` nor `native` — tolerable only because CI never runs it (NAT-14).
**Why it matters.** `Directory.Build.props` sets `TreatWarningsAsErrors`/`Nullable` for every project including the MAUI shells; a change there rebuilds Api/Web (`code`) but skips the Apple build + native smokes, exactly the compile-rot R60 exists to catch.
**Evidence.** `native=$(match "$codefiles" '^(src/|tests/E2E\.Tests/|tests/native-smoke-android/|Directory\.Packages\.props|\.github/workflows/ci\.yml|global\.json)')` (`ci.yml:329`); Forgejo copy adds only `\.forgejo/workflows/ci\.yml`.
**Fix (one line).** Add `Directory\.Build\.props` (and `tools/` once a Release leg consumes the script) to both copies.
**Gate.** Extend `EnforcementGateTests.MarkdownAnywhere_IsNeverCodeOrNative` with `Assert.True(Native("Directory.Build.props"))` and one positive per R60 file class (R85n).
**Confidence.** Certain.

### NAT-17 · Low · `src/Maui/Platforms/Android/AndroidManifest.xml:3` — re-opened v3 NAT-9(b); adjudication error, not new code
**What.** `android:allowBackup="true"` is still in the manifest. v3 T36 (`IMPLEMENTATION_TRACKER.md:75`) records "`allowBackup=false`" as ✅; `git log -S allowBackup -- AndroidManifest.xml` shows a single commit (862fdf7, the original introduction as `true`) — it was never flipped. The manifest is not in the delta file list.
**Why it matters.** Auto/cloud backup includes app data unless excluded: the `PreferencesOAuthResumeStore` marker + stashed callback (NATIVE-12), the culture preference and the WebView's `localStorage` (theme) are backed up and restored onto another device; SecureStorage's Keystore-wrapped blob restores unreadable, so the refresh token itself is not exposed — hence Low, matching v3's own rating.
**Evidence.** `<application android:allowBackup="true" android:icon="@mipmap/appicon" … android:networkSecurityConfig="@xml/network_security_config">`.
**Fix (one line).** `android:allowBackup="false"` (or a `fullBackupContent` rule excluding prefs + WebView data).
**Gate.** Regex assertion in `tests/Api.Tests/NativeChromeGateTests.cs` (R84n manifest posture gate).
**Confidence.** Certain. Filed here because the tracker says fixed and the tree says not; the synthesizer may prefer to keep it under RULE_CONFLICTS only.

### NAT-18 · Low · `src/Maui/MauiProgram.cs:32,161,177` + `csproj:144-147` — API base is an origin, and nothing says or checks it (Q3)
**What.** `ApiBaseUrl => RawApiBaseUrl.TrimEnd('/')` now yields a `BaseAddress` without a trailing slash. That is harmless for the shipped call pattern — all 62 RCL API paths are root-absolute `"/api/…"` (0 relative `"api/…"`), and `AuthService` posts `"/api/auth/refresh"` — but it means a base with a path segment (`https://host/app`) was never supported: `new Uri(base, "/api/x")` discards the path in both the old and new code. Nothing documents "origin only", and the Release guard accepts any non-empty string: `-p:ApiBaseUrl=example.com` passes the csproj `<Error>` and crashes at first DI resolve (`new Uri("example.com")` → `UriFormatException`), and an `http://` Release base is blocked only on Android (network config); Windows/iOS/macCatalyst would ship cleartext.
**Answer to Q3 (header scoping).** Not scoped — see NAT-12.
**Evidence.** `private static string ApiBaseUrl => RawApiBaseUrl.TrimEnd('/');` (`:32`); `new HttpClient { BaseAddress = new Uri(ApiBaseUrl) }` (`:161`, `:177`); `Condition="'$(Configuration)' == 'Release' And '$(ApiBaseUrl)' == ''"` (`csproj:145`).
**Fix (one line).** Tighten the csproj guard to `!$(ApiBaseUrl.StartsWith('https://'))` (plus "no path" — `$(ApiBaseUrl.TrimEnd('/'))` having no `/` after the authority), and state "origin only" in DEPLOYMENT §9.
**Gate.** Covered by the R82n Release leg (negative case) or a csproj-text assertion in `NativeChromeGateTests`.
**Confidence.** Certain.

### NAT-19 · Info · `src/Shared.Ui/wwwroot/js/theme.js:18,41-42`
**What.** One global watcher slot: `watch` overwrites, `unwatch` nulls unconditionally. A second `SystemBarThemeSync` instance (a downstream page adding its own) would silence the layout's on dispose. Harmless today (single instance in `MainLayout.razor`).
**Fix.** `unwatch: function (ref) { if (watcher === ref) watcher = null; }` and pass `_self` from `DisposeAsync`. **Gate:** extend `ThemeJs_TellsAWatcherEveryThemeItApplies`. **Confidence:** Certain.

### NAT-20 · Info · `tests/native-smoke-android/smoke.js:104-118`
**What.** The Household step's `page.goto` fallback (added in the delta) turns an in-app-navigation regression (broken `nav-household` link or hamburger) into a `console.error` on a green run. Boot and roster assertions remain non-vacuous (attempt-2 failure throws; `members !== 1` throws).
**Fix.** Emit `::warning::` and fail unless `NATIVE_SMOKE_ALLOW_GOTO_FALLBACK=1` (or count fallbacks and fail in the Monday scheduled run). **Confidence:** Certain.

### NAT-21 · Info · `src/Shared.Ui/wwwroot/js/bfcache-guard.js:8-10` + `docs/NATIVE_PARITY.md`
**What.** The guard reloads on `pageshow.persisted`. In the BlazorWebView single-document app there is no history navigation that could restore from bfcache (the Android back key is intercepted natively, NATIVE-4 G3; WKWebView back-forward gestures are off by default), so it is a no-op there — but that is a comment-level claim only (`bfcache-guard.js:6-7`), and NATIVE_PARITY.md, the document the comment points at, does not mention the guard, `SystemBarThemeSync`, or the #231 status-bar change (its "Safe areas / status bar" row still ends at the 2026-07-07 fix; the delta touched that row only cosmetically). `docs/stories/native.md` has no entry for the system-bar port (#231) or the release-signing fix (#213); the QA plan's 2026-09-16 note and REBRANDING §4 do cover the bars.
**Fix.** One dated line each in NATIVE_PARITY (status bar row; bfcache guard = browser-only) and native.md. **Confidence:** Certain (doc currency).

### NAT-22 · Info · `src/Maui/Perezosoft.Maui.csproj:74,77` vs `docs/brand/build_assets.py:30`
**What.** The icon/splash ground `#6b8a72` (= `app.css --brand-accent`) is hand-duplicated in the two `Color=` attributes and in `ICON_GROUND`, with no gate tying them together (NativeChromeGateTests covers `colors.xml`/`SystemBarColors` only). A downstream rebrand that changes the token but not the csproj ships a mismatched launcher icon. `build_assets.py` also needs network (Google Fonts) and a local Chromium — fine for a maintainer tool, worth one line in REBRANDING §3.
**Fix.** Assert the three values equal `--brand-accent` in `NativeChromeGateTests`. **Confidence:** Certain.

## Template-readiness notes
- **`ISystemBarTheme` seam (good practice, gate-backed).** RCL → host contract with a no-op default; a downstream app only edits `SystemBarColors` and `colors.xml`, and `NativeChromeGateTests` fails when they drift from `app.css`. No new core→slice dependency. Burden: none per slice.
- **Bearer-on-every-request is structural (NAT-12).** Any downstream slice that fetches a third-party absolute URL through the injected `HttpClient` (report PDFs on S3, a provider webhook test, an image proxy) leaks the JWT. The fix belongs in the template (scoped handler), not in each slice.
- **"Debug key signs Release" is a template convenience with no guard.** It removes a first-sideload trap but lets a downstream app call a debug-key-signed APK a release; a downstream must consciously replace it (Phase 9). Recommend the template fail a Release without an explicitly resolved keystore, or at minimum name the reverse-upgrade trap (NAT-15e).
- **Release-only MSBuild logic is doc-only assurance (NAT-14).** Both R67 blocks and the signing block are exercised by humans only; a downstream inherits the same blind spot.
- **`build_assets.py` is a doc-only good practice**: no gate proves the committed rasters match the SVG sources (a rebrand that swaps the SVGs and forgets the script leaves stale PNGs, e.g. the email logo REBRANDING warns about). A CI job that runs the script and `git diff --exit-code` would be heavy (Chromium + fonts); acceptable as a §3 checklist item.
- **Signing-material hygiene is doc-only (NAT-15).** The gitignore/gitleaks pair does not cover keystores.

## Candidate rules
- **R81n-cand — [machine]** — Both hosts attach `Authorization: Bearer` only to requests whose target origin equals the API base origin; a plain client (no auth handler) is what third-party absolute URLs (signed downloads) use. — Enforcement: shared `BearerScopedHandler` in `src/Shared.Ui` covered by `tests/Ui.Tests` (absolute foreign URL ⇒ no header; relative and same-origin ⇒ header) + `ArchitectureTests` banning `AuthenticationHeaderValue("Bearer"` outside it. — Subsumes NAT-12.
- **R82n-cand — [machine]** — CI exercises an Android **Release** build (dispatch input + the Monday schedule, like the smokes): `-c Release -p:ApiBaseUrl=https://release.invalid -p:AndroidPackageFormat=apk`, asserting `apksigner verify` reports v2/v3 = true, `RequireApiBaseUrlInRelease` fires without the property, and the Release APK carries the HTTPS-only network config. `publish-native.ps1` itself fails when v2/v3 are not both verified. — Enforcement: new job in both workflow copies (R80 parity), `EnforcementGateTests` asserting its presence. This is the missing machine half of R67. — Subsumes NAT-13, NAT-14, NAT-18 (negative case).
- **R83n-cand — [machine]** — Signing material is never in git: `.gitignore` lists `*.jks`, `*.keystore`, `*.p12`, `*.pfx`, `*.mobileprovision`, `*.cer`, `out/`; passwords come from env, never `-p:` literals in docs or scripts. — Enforcement: `EnforcementGateTests` asserts the gitignore patterns; a doc-grep gate rejects `-p:AndroidSigning(Key|Store)Pass=` outside a "do not" sentence. — Subsumes NAT-15.
- **R84n-cand — [machine]** — Android shell posture gate: manifest has `allowBackup="false"` (or a backup-rules file), `networkSecurityConfig` set, no `usesCleartextTraffic="true"`, no `debuggable`; the Release network config is `base-config cleartextTrafficPermitted="false"`. — Enforcement: regex assertions in `NativeChromeGateTests` (rename to `NativeShellGateTests`). — Subsumes NAT-17; hardens R67's cleartext half.
- **R85n-cand — [machine]** — The `native` classifier regex is asserted positively per R60 file class (`src/`, `tests/E2E.Tests/`, `tests/native-smoke-android/`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, both `ci.yml`, and any script a native CI leg runs). — Enforcement: extend `EnforcementGateTests.MarkdownAnywhere_IsNeverCodeOrNative` with one `Assert.True(Native(...))` per class. — Subsumes NAT-16.

## RULE_CONFLICTS candidates
- **v3 T36 (NAT-9b) marked ✅ but not implemented**: `AndroidManifest.xml:3` still `allowBackup="true"`; never changed since 862fdf7. Tracker/AUDIT_RECONCILIATION should be corrected (NAT-17).
- **v3 T35 (R60) marked ✅ "regex adds `Directory.Build.props`"** — neither the BASE nor HEAD regex contains it (NAT-16).
- **R67 is labelled [machine] with no test or CI execution path**: its only enforcement is a csproj `<Error>` reached solely by a human Release build. Either relabel [review] or add R82n.
- **R68 text is broader than its gate**: the rule says "theme.js before the first stylesheet" and "the two vendored `wwwroot/lib` trees are byte-identical"; `HostIndexHtml_ReferenceTheIdenticalRclScriptSet` checks script-set equality only (no ordering, no lib comparison — grep for `bootstrap.min.css`/`byte-identical` in `tests/` finds nothing). Both clauses hold today by inspection. Pre-existing; noted for the synthesizer.

## Out-of-area observations
- AUTH: `AuthService.GetFreshAccessTokenAsync()` ignores the request's `CancellationToken`; both hosts' handlers block up to the auth client's timeout on a hung refresh (`AuthService.cs:697-705`).
- AUTH/WEB: `src/Web/Http/AuthHeaderHandler.cs:13-15` shares NAT-12's unscoped shape (no current absolute-URL caller on web).
- CI (LOCALCI-4 #9): an asleep MacBook skips the Apple jobs with a warning, so `#if MACCATALYST && DEBUG` code (`DebugFileSessionStore`) and the Catalyst entitlement swap compile nowhere on those runs — compile rot in the only Debug-Catalyst path is invisible until the Mac wakes.
- FILES: `LocalFileStorageSettings`' `PublicBaseUrl` decides whether the local-disk signed URL is same-origin; a mis-set value would trigger NAT-12 even without S3.
- UI: `MainLayout.OnResumed` is `async void` (guarded by try/catch) — fine; `SessionKeepAliveTests:254` covers the Notify path.

## Unknowns needing a human decision
1. Confirm on a Mac that `dotnet publish -c Release` for `net10.0-android` without `AndroidKeyStore=true` yields a v1-only APK (the csproj comment's claim) — decides NAT-13's severity (Medium as filed; High if the store-key path is also skipped there).
2. Confirm MinIO's behaviour for presigned GET + `Authorization` header (AWS S3 rejects; MinIO unverified) — decides whether NAT-12(b) is a live break on the platform's own S3 test fixture.
3. Policy: should the template keep "debug key signs Release" as the default, or should a Release build fail unless a keystore is explicitly resolved (safer, one more step for a first sideload)?
4. Whether to keep NAT-17 as a numbered finding (live manifest posture) or fold it into RULE_CONFLICTS as a tracker correction only.
