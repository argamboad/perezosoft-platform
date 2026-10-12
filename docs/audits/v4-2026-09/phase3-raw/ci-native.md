# CI/deploy scripts + native shell logic — v4 Phase 3, Part A (wrong-result hunt) + Part B (test specs)
SHA: 8c8ed4a · BASE: f52be3d · 2026-09-23 · read-only; nothing tracked was modified, no forge write was made.
Builds on Phase 1 (DEP-13..27, NAT-12..22, UX-8/9, S0-G6) — those are NOT re-filed; where a probe sharpens one it is cited.
Probes live in `scratchpad/phase3/probes/` (p1_push_diagnosis.sh, p2_already_green.sh + p2b_already_green.py, p3_classifier.sh,
p4_shards.sh, p5_slowest.py, p7_exists.proj, p8_stderr_stop.ps1, p10_*.md) and were executed with Git Bash / python / dotnet msbuild /
pwsh 7.6 / Windows PowerShell 5.1 on this host. `jq` is absent on the host, so the deploy.yml jq was re-implemented 1:1 in python (p2b);
the real field shapes came from a read-only GET of `/api/v1/repos/argamboad/perezosoft-platform/actions/tasks` (status strings are
`"success"`, matrix names are `e2e (1)` / `native-build (ubuntu-latest, net10.0-android)`, PR runs carry the PR **head** sha with
`head_branch: "#18"`, 12 tasks per develop run, `total_count` 613).

## SUMMARY
- **Part A: 16 findings — High 1 · Medium 3 · Low 6 · Info 6** (LB-DEP-1..11, LB-NAT-1..5). 11 are probe-confirmed (Certain),
  the rest Likely/Suspected with the runtime dependency named.
- Headline: **LB-DEP-2 (High, Certain)** — the `changes` classifier reads `git diff --name-only` with git's default path quoting, so a
  code file with a non-ASCII name (`src/Api/Features/Añadir.cs` → `"src/Api/Features/A\303\261adir.cs"`) matches neither `^src/`
  nor `^docs/` and the push runs **no gate at all** (code=false, native=false, docs=false). Real for the Spanish downstream apps.
  **LB-DEP-3 (Medium)** — the already-green deploy's `-ge 2`/`-ge 3` leg floors are literals; a grown matrix with one red leg deploys.
  **LB-DEP-4 (Medium)** — all four native boot-probe greps (`…providers.*200`) match the elapsed-ms field, so a 404/500 provider
  probe with `200` in its timing is a green smoke. **LB-DEP-1 (Medium)** — the push-refusal diagnosis reports a failed diagnostic fetch
  (bad `DEPLOY_MIRROR_REPO`, 401, branch absent) as "IS an ancestor → ruleset/token".
- The native shell logic (theme.js ↔ SystemBarThemeSync ↔ AndroidSystemBarTheme, bfcache-guard.js, smoke.js) is sound on every path
  the brief asked about; the first-render/interop ordering question is answered "safe" with the mechanism (LB-NAT-4) so Phase 4 need
  not re-chase it. The MSBuild guards have two real gaps (LB-NAT-1/2).
- **Part B: 11 DEP specs + 6 NAT specs**, headed by **TB-DEP-1, the shell-logic harness seam** (`tests/ci-logic/` fixtures run from
  `EnforcementGateTests` via `Process` on the Linux build-test leg) — the class of bug in this report exists because every one of
  these verdicts is computed in bash/jq/PowerShell/python/JS that the C# suite mirrors in regex or never touches.
- Rules proposed: **R123d–R130d**. Rule-conflict candidates: 4 (refinements of R98, R75-on-Forgejo, DEP-21/UX-9 count, NAT-13 path claim).

## Part A findings

### LB-DEP-1 · Medium · Certain (p1 cases D, E) · `.forgejo/scripts/push-to-github.sh:45–51`
- **Triggering input:** the push is refused AND the diagnostic `git fetch … refs/heads/$BRANCH` itself fails — `DEPLOY_MIRROR_REPO`
  mistyped or carrying a trailing `.git` (URL becomes `…/name.git.git`), a revoked/expired `DEPLOY_MIRROR_TOKEN` (401), or the branch not
  existing on the mirror yet (first `main` deploy of a new app) while a ruleset blocks branch creation.
- **Wrong output:** the `if fetch && ! merge-base` guard is false when the fetch fails, so the **else** branch runs and prints
  `"…and its $BRANCH IS an ancestor of this commit, so it is not a fast-forward problem — … A ruleset or a token permission is the
  usual cause (… Workflows: Read and write …)"`. The operator is sent to widen a token scope when the repo name is wrong.
- **Probe:** A (FF) → pushed; B (mirror ahead) → non-FF message ✓; C (pre-receive refusal, ancestor) → ruleset message ✓;
  **D (repo path unreachable) → "IS an ancestor" ✗; E (branch absent + refusal) → "IS an ancestor" ✗.**
- **Correct:** three-way — fetch failed → `"could not read GitHub's $BRANCH — check DEPLOY_MIRROR_REPO (owner/name, no .git) and that
  the token can read the repo"`; else the two existing verdicts. Trailing-`.git` GitHub behaviour is Suspected (not probed); the message
  path is Certain.
- **Fix:** capture the fetch status separately: `if ! git … fetch …; then <config/auth message>; elif ! git merge-base …; then <non-FF>;
  else <ruleset>; fi`; strip a trailing `.git` from `MIRROR_REPO` defensively.
- **Gate:** TB-DEP-2 (harness runs the real script against local bare repos, cases A–E).

### LB-DEP-2 · **High** · Certain (p3 case 4 vs 12) · `.forgejo/workflows/ci.yml:404` and `.github/workflows/ci.yml:314`
- **Triggering input:** any changed file whose path has a byte ≥ 0x80 — `src/Api/Features/Añadir.cs`, `src/Shared.Ui/Pages/Categoría.razor`,
  `tests/E2E.Tests/JornadaCompraTests.cs` with an accent. Nothing in the repo today; the downstream apps are Spanish-named products.
- **Wrong output:** `git diff --name-only` (core.quotePath default `true`) prints `"src/Api/Features/A\303\261adir.cs"` — leading `"`.
  `^(src/|tests/|…)` does not match, `^docs/` does not match → `code=false native=false docs=false` → build-test, e2e, docker, license
  and both native builds are **skipped** for a code change; the merge run is "green" with 3 jobs. The `.md` strip also misses (`.md"`).
- **Probe:** case 4 → `code=false native=false docs=false diff=["src/Api/Features/A\303\261adir.cs"]`;
  case 12 (`core.quotePath=false`) → `code=true native=true`. Cases 1, 9 (rename), 10 (delete), 13 (space in name) classify correctly.
- **Correct:** code=true, native=true.
- **Fix:** `files=$(git -c core.quotePath=false diff --name-only "$BASE_SHA" "${{ github.sha }}")` in BOTH copies (or `-z` + `tr '\0' '\n'`).
- **Gate:** TB-DEP-5 (harness fixture through real git); cheap C# half: `ForgejoCiParityTests` asserts the literal
  `git -c core.quotePath=false diff --name-only` in both copies. The existing `MarkdownAnywhere_IsNeverCodeOrNative` cannot see this — it
  mirrors the regex in C#, never runs git.

### LB-DEP-3 · Medium · Certain (p2b cases B, C) · `.forgejo/workflows/deploy.yml:68–73`
- **Triggering input:** the e2e matrix grows to 4 shards (or a 3rd native-build leg is added) and one leg is red on the merge run; the
  operator dispatches "Deploy (already green)".
- **Wrong output:** `shards` counts distinct **successful** `e2e (` names = 3 → `-ge 3` holds → **DEPLOY** with a red shard. Same for
  `native-build (` with `-ge 2`. The comment says "counting them is what stops a half-run from looking green" — the count is a literal
  copied from `ci.yml`, bound to nothing; `ForgejoCiParityTests.AlreadyGreenDeploy_…` asserts only that `native-build (` appears.
- **Probe:** A → DEPLOY; **B (e2e (4) failure) → DEPLOY ✗; C (ios leg failure) → DEPLOY ✗**; F/G (renamed jobs) → REFUSE ✓;
  H (empty list) → REFUSE ✓; M (docs-only run, gates skipped) → REFUSE ✓; D/E/J (re-run red / red smoke / re-run in progress) → DEPLOY —
  these are **DEP-19** (filed), J is its "no recency" variant.
- **Correct:** refuse when any task whose name starts with the prefix is not `success`, and when the distinct success count ≠ the matrix
  length declared in `ci.yml` (parsed, not typed).
- **Fix:** `bad=$(jq -r 'select(.name|startswith("e2e (")) | select(.status != "success") | .name' …)` → refuse if non-empty; derive
  the expected count in the workflow from `ci.yml` (`grep -oE 'shard: \[[^]]+\]'`) or assert it in the parity test.
- **Gate:** TB-DEP-3 (+ TB-DEP-10 for DEP-19).

### LB-DEP-4 · Medium · Certain (p6) · Windows probe `.forgejo/workflows/ci.yml:751` / `.github/workflows/ci.yml:624`; Apple probe `.forgejo:601` / `.github:493`
- **Triggering input:** the provider probe returns anything but 200 (route regression, 500 on startup, auth misconfig) and the
  request-log line's elapsed-ms field contains `200` — `… - 404 - application/problem+json 200.1234ms`, `… - 500 - … 3.2001ms` — or the
  probe hits a sibling route (`/api/auth/providers-beta`). ASP.NET's line is `Request finished HTTP/1.1 GET <url> - <status> - <ctype> <ms>ms`;
  a cold first request in the 200–209 ms band is ordinary.
- **Wrong output:** `Request finished.*GET.*/api/auth/providers.*200` counts it → `$ok=$true` / `ok=1` → the native canary passes while
  Blazor's login page got a 404 from the API.
- **Probe:** 5 fixture lines — current pattern matches 4 (incl. the 404, the 500 and the sibling route); the strict pattern
  `Request finished [^ ]+ GET [^ ]*/api/auth/providers - 200 - ` matches only the genuine 200.
- **Fix:** the strict pattern in all four sites (bash `grep -cE`, PowerShell `Select-String -Pattern`).
- **Gate:** TB-DEP-6 (harness fixture api.log; C# gate asserting `providers - 200 - ` in the four sites via `ForgejoCiParityTests`).

### LB-DEP-5 · Low · Certain (p4 tail + confirmation run) · `.forgejo/workflows/ci.yml:1096–1097` / `.github/workflows/ci.yml:943–945`
- **Trigger:** `--list-tests` yields nothing (the E2E build is stale/broken, or the adapter changes its indentation).
- **Wrong output:** the step began with `set -e`; `total=$(echo "$names" | grep -c .)` — `grep -c` exits 1 on zero matches, the assignment
  fails, the shell exits **before** the `::error::only $total journeys listed — the --list-tests parse broke` line. Verdict is still red
  (correct direction) but the diagnosis the author wrote never prints; the job log ends at a silent `exit 1`.
- **Fix:** `total=$(echo "$names" | grep -c . || true)`.
- **Gate:** TB-DEP-7.

### LB-DEP-6 · Low · Certain (p5) · `.forgejo/workflows/ci.yml:1117–1139` / `.github/workflows/ci.yml:961–989` (Slowest journeys)
- **Trigger:** a `.trx` with zero `UnitTestResult` elements (the filter matched nothing — reachable if a journey name ever contains a
  filter metachar; today the sed restricts names to `[A-Za-z0-9_]`, so dormant).
- **Wrong output:** `slow/total*100` → `ZeroDivisionError` → the `if: always()` step exits 1 → **the job goes red from the reporting
  step**, contradicting "never fails the job"; the traceback hides that nothing ran. Probe: `0 journeys, 0.0 min …` then the exception, exit 1.
- **Also (Suspected):** `boots = trx.count("[blazor-boot] attempt")` over-counts a fatal boot by one — `BlazorBoot.cs:79` puts `{note}`
  (the attempt-3 line) into the exception message, which the trx stores again in `<ErrorInfo>`; whether `TestContext.Progress` lines
  also land in `<StdOut>` (NUnit adapter) would double every count. Feeds UX-9's threshold (TB-DEP-11).
- **Fix:** `if not rows: print("no journeys in the trx"); raise SystemExit`; count the deduplicated set (already computed on the next line).
- **Gate:** TB-DEP-7.

### LB-DEP-7 · Low · Certain (p10) · `.github/scripts/qa-runlog-append-only.sh:37`
- **Trigger:** the §16 table's alignment row uses colon alignment (`|:--------|:-------|…`) — Markdown-valid, and what most table
  formatters emit. Dormant: the committed plan uses `|---------|` (verified, lines 6/17 of the §16 block).
- **Wrong output:** the separator check is `caseId ~ /^-+$/`; `:---` fails it, `tester=$5=":-------"` is non-blank → the alignment row is
  treated as an **executed** row and must survive verbatim. A later reformat of the row (`|:---|` → `|---|`, or a column widened)
  → `::error::Executed QA run-log row rewritten or deleted: |:--------|…` and exit 1 — a false red on a doc-only PR. Probe: realigned
  head → exit 1; blank row filled in → exit 0 ✓.
- **Fix:** `caseId ~ /^:?-+:?$/`.
- **Gate:** TB-DEP-8.

### LB-DEP-8 · Low · Certain (parse probe under 5.1 + p8) · `tools/protect-branches.ps1`, `tools/publish-native.ps1` (whole files)
- **Trigger:** running either from Windows PowerShell 5.1 — `docs/NEW_APP_GUIDE.md:81` says `./tools/protect-branches.ps1 …` with no
  `pwsh` prefix (DEPLOYMENT §9 does say `pwsh`).
- **Wrong output (two layers):** (1) both files are BOM-less UTF-8 with em dashes **inside double-quoted strings**
  (`protect-branches.ps1:23,39,41`, `publish-native.ps1:45,63,67`); 5.1 decodes them as cp1252, where `—`'s last byte is `”`, a string
  delimiter → **8 and 7 parse errors** ("Unexpected token 'no'/'the'") — loud, so no silent wrong result today. (2) If the dashes were
  ever ASCII-fied without a version pin, p8 shows every redirect shape the scripts use (`2>$null` at :23, `2>&1` at :36 and
  publish-native :61) **terminates** under 5.1 with `$ErrorActionPreference='Stop'` on the first stderr byte from `gh`/`apksigner`
  (pwsh 7.6: all three continue). For protect-branches that turns "no such branch, skipped" into a crash; for publish-native the APK is
  copied, then the script dies before the verdict lines. Neither file has `#Requires -Version 7`.
- **Fix:** `#Requires -Version 7.0` as line 1 of both; save with BOM or keep strings ASCII; NEW_APP_GUIDE:81 → `pwsh ./tools/…`.
- **Gate:** TB-DEP-9. (DEP-13's exit-0-on-failure at `protect-branches.ps1:39` stands; the pwsh harness in TB-DEP-9 covers it too.)

### LB-DEP-9 · Info · Certain (read) · `.forgejo/workflows/ci.yml:395–397` / `.github/workflows/ci.yml:305–309`
- The fail-open branch writes `code=true native=true docs=false`. No job consumes `docs` in either copy today (grep: only `code`/`native`
  are read), so it is harmless — but a future job gated on `needs.changes.outputs.docs == 'true'` (a doc-currency gate is a natural
  candidate) would be **skipped exactly on the unknown-base path whose purpose is "run everything"**. Fix: `docs=true` on fail-open.
  Gate: TB-DEP-4 fixture "fail-open ⇒ every output true".

### LB-DEP-10 · Info · Certain (p3 case 3) · both `changes` (`grep -vE '\.md$'`)
- `README.MD` / `Notes.Md` are not stripped → `src/Api/README.MD` classifies as code+native (over-run: ~30 billed minutes, never a
  skipped gate). Fix: `grep -viE '\.md$'`. Gate: TB-DEP-4 positive/negative table.

### LB-DEP-11 · Low · Suspected · `.forgejo/workflows/ci.yml:190–197` (`QA run-log is append-only` step)
- The step runs on a depth-1 checkout and does `git fetch --quiet origin "$BASE_SHA"`; on failure it prints "Diff base unavailable …
  skipping" and exits 0. Fetching a bare SHA needs `uploadpack.allowAnySHA1InWant`/reachable-SHA on the server; Gitea/Forgejo set it in
  their git config, but if the desk runner's origin URL, the container's git version, or the server setting differs, **R75's guard is
  silently vacuous on every Forgejo run** (the same fail-open covers a genuine force push). The job-logs endpoint (`/actions/jobs/{id}/logs`)
  returned 404 on this instance, so the "intact" line could not be read back. Fix: fetch with `--depth=1` and distinguish
  "server refused the SHA" (`::warning::`) from "SHA unknown to the server"; Monday schedule asserts the guard ran once.
  Gate: TB-DEP-8 (runtime evidence step).

### LB-NAT-1 · Low · Certain (p7) · `src/Maui/Perezosoft.Maui.csproj:132, 145, 160`
- **(a) The `Exists()` question answered:** with `LOCALAPPDATA` empty, `Exists('$(LOCALAPPDATA)\Xamarin\Mono for Android\debug.keystore')`
  resolves to the **drive root** (`C:\Xamarin\Mono for Android\debug.keystore` on Windows; `/Xamarin/…` on Unix) — never a repo-relative
  directory, so it cannot false-match; it is simply false, and the debug-keystore fallback goes inactive on macOS/Linux. That is the
  mechanism of NAT-13 (filed), now Certain rather than Likely.
- **(b) Fresh:** the three Release-only guards key on different predicates — signing fallback `== 'Release'` (:132), ApiBaseUrl guard
  `== 'Release'` (:145), HTTPS-only network config `!= 'Debug'` (:160). A custom configuration (`-c Staging`, common downstream) gets the
  cleartext-forbidding network config but **no refusal for a missing `ApiBaseUrl`** → the app compiles in the Debug fallback
  `http://localhost:5238` (MauiProgram's Debug default) under a config that forbids cleartext → every API call fails at launch, and no
  build error said why. Fix: one predicate `'$(Configuration)' != 'Debug'` for all three.
- **Gate:** TB-NAT-3 (the p7 pattern: evaluation-only `dotnet msbuild` of an importable `.targets`, no workload needed).

### LB-NAT-2 · Low · Certain (p7) · `src/Maui/Perezosoft.Maui.csproj:145`
- `-p:ApiBaseUrl=` (empty) fires the guard ✓; `-p:ApiBaseUrl=" "` (whitespace — a CI variable that exists but is blank, quoted) passes
  `'$(ApiBaseUrl)' != ''`, is baked into `AssemblyMetadata`, and `MauiProgram.ReleaseApiBaseUrl` builds a `Uri` from a space → launch crash.
  NAT-18 already covers `example.com`/`http://`; this is the whitespace member of the same negative set. Fix: `'$(ApiBaseUrl.Trim())' == ''`
  plus the NAT-18 `https://` + origin-only checks in one target. Gate: TB-NAT-3 table.

### LB-NAT-3 · Info · Suspected · `src/Shared.Ui/wwwroot/js/theme.js:30–32`
- `media.addEventListener('change', …)` runs **before** `window.appTheme = {…}` (line 32). On a WebView whose `MediaQueryList` is not an
  `EventTarget` (Chromium < 76 — Android 7 stock WebView; Safari < 14, excluded by the iOS 15 floor) the IIFE throws at :30, `appTheme`
  is never defined, the stored theme is never applied, and every `appTheme.set/watch` interop call throws (caught in SystemBarThemeSync,
  surfaced elsewhere as an unhandled JS interop error). Whether Blazor WebView itself runs on such a WebView is unknown → Info.
  Fix: define `window.appTheme` first, then `(media.addEventListener ? media.addEventListener('change', h) : media.addListener(h))`.
  Gate: TB-NAT-4 (Node stub test).

### LB-NAT-4 · Info · Certain (read) · `SystemBarThemeSync.razor`, `theme.js`, `MainLayout.razor:17`, both `index.html:11` — **no bug; the ordering question closed**
- Can `watch` fire before the JS module loads on an Android cold start? **No:** theme.js is a synchronous `<script>` in `<head>` before the
  first stylesheet in both hosts, so `window.appTheme` exists before Blazor's runtime starts; the call sits in `try/catch` regardless.
- `unwatch` on dispose vs a second instance: only `MainLayout` hosts the component and it is the only layout (`src/Shared.Ui/Layout/`), so
  two instances never coexist; even on a layout swap Blazor disposes the old subtree inside the render batch and runs the new one's
  `OnAfterRenderAsync` after it, so `unwatch` precedes `watch` in the interop queue. NAT-19's single slot remains the only latent hazard.
- light/dark/system mapping: `resolved()` and `_theme = theme == "dark" ? "dark" : "light"` agree; `Ground` flips repaint only when
  `_theme` is known and `Ground != _paintedGround` (bUnit-covered).
- **Open (needs a device):** `MainActivity` handles `ConfigChanges.UiMode` itself (no recreate), so a night-mode flip under "system"
  depends on the WebView re-evaluating `prefers-color-scheme` from the configuration change. Likely true on current WebViews; unprobed.

### LB-NAT-5 · Info · Certain (read) · `tests/native-smoke-android/smoke.js:58–125`
- Exit codes are sound: the whole run is one promise chain with `.catch(… process.exit(1))`; Node ≥ 15 turns an unhandled rejection into
  a crash (exit 1), so "unhandled rejection → exit 0" cannot happen. The one boot retry catches **any** error from `bootToLogin` (including a
  60 s `webView()` attach timeout after a genuine G7 crash), which is the documented intent (both attempts must fail). Two Info notes:
  `device.close()` throwing after a passed smoke → exit 1 (false red); the `goto` fallback (:116) is NAT-20. Refactor seam in TB-NAT-6.

## Part B specs

### TB-DEP-1 — the shell-logic test seam (the class-level fix)
- **Layout:** `tests/ci-logic/run.sh` (bash, needs `jq`, `python3`, `pwsh`) + `tests/ci-logic/fixtures/**`. Each case is a directory:
  `input/` (a fixture `git diff` list, `jobs.json`, `api.log`, `list-tests.txt`, `e2e.trx`, `qa-base.md`/`qa-head.md`, a bare-repo builder)
  and `expected` (the verdict line).
- **Extraction, not duplication:** the runner pulls each verdict block out of the workflow by anchor so the tests exercise the committed text:
  classifier = the `run:` body of step `id: diff` from `codefiles=` to the `>> "$GITHUB_OUTPUT"` block, with `git diff --name-only …`
  replaced by `cat "$FIXTURE"` through a shell function `git() { … }`; already-green = `deploy.yml` lines from `green() {` to the
  `refusing to deploy` `fi`; probe greps = the `probes()`/`Probes` function lines; shard = from `names=$(dotnet test …` (stubbed by a
  function) to `filter=`; Slowest = the `python3 - <<'PY' … PY` heredoc; push diagnosis and qa-runlog = the real scripts. Anchors are
  asserted present in BOTH `ci.yml` copies (R80 already guarantees the bodies are equal — `ForgejoCiParityTests.ForgejoCopy_ClassifiesChangesLikeGitHub`
  can be reused for the classifier).
- **Hook:** `EnforcementGateTests.CiShellLogic_PassesItsFixtures` → `Process.Start("bash", "tests/ci-logic/run.sh")`, asserting exit 0
  and echoing the runner's TAP-style output on failure. `Skip` (with a reason) when `bash`/`jq` are absent (Windows dev boxes), but a
  second assertion runs on the Linux `build-test` leg only (`RUNNER_OS == Linux`) and **fails if the runner was skipped there** — so CI
  can never quietly lose the harness. PowerShell cases run under `pwsh` (present on ubuntu-latest and the desk image).
- **Verdict-step inventory:** the test lists the step names that print `::error::` in a `run:` block and asserts each has a fixture
  directory (or an explicit allowlist entry with a reason) — R123d's "no untested verdict" clause.
- **Initial fixture set:** p1 A–E, p2 A–M, p3 1–13 + fail-open, p4 (12 names, 3 shards, the `Delta_Journey` prefix pair, empty list),
  p5 (0 rows; 3 rows; one with two `[blazor-boot] attempt` lines + a giving-up message), p6 (5 lines), p10 (3 heads).

### TB-DEP-2 — push-to-github.sh three-way diagnosis (LB-DEP-1)
Add `MIRROR_URL="${MIRROR_URL:-https://github.com/$MIRROR_REPO.git}"` so the harness can point the script at a local bare repo without
faking DNS. Cases: A fast-forward → exit 0 "Pushed"; B mirror ahead → exit 1, message contains "not an ancestor"; C pre-receive `exit 1`
with the mirror an ancestor → "read git's message above … ruleset or a token"; D unreachable repo → new "could not read GitHub's
develop" message and **not** "IS an ancestor"; E branch absent + refusal → same as D; F `MIRROR_REPO=owner/name.git` → normalised
(assert the URL printed by `git push` has one `.git`). Env guards: HOOK unset → exit 0 notice; HOOK set, token unset → exit 1.

### TB-DEP-3 — already-green matrix completeness (LB-DEP-3)
Harness: fixtures A (deploy), B/C (refuse, naming the red leg), F/G/H/M (refuse). Parity test:
`AlreadyGreen_LegFloors_MatchTheMatrices` parses `shard: [1, 2, 3]` in `ci.yml` (Forgejo copy) and the `native-build` matrix `include`
count, and asserts `deploy.yml` contains `-ge <n>` for each (or, after the fix, contains the `status != "success"` refusal per prefix and
no numeric floor at all).

### TB-DEP-4 — DEP-15 classifier positives + fail-open (LB-DEP-9/10, NAT-16, S0-G6)
Extend `EnforcementGateTests.MarkdownAnywhere_IsNeverCodeOrNative` (C# mirror, cheap) with `Assert.True(Code(...))` for
`.forgejo/scripts/push-to-github.sh`, `.forgejo/workflows/deploy.yml`, `.forgejo/workflows/postman-sync.yml`, `.github/workflows/postman-sync.yml`,
`.github/forbidden-licenses.json`, `.dockerignore`, `docs/DEPLOYMENT.md`(R97 choice), `tools/publish-native.ps1`; `Assert.True(Native(...))` for
`Directory.Build.props`, `tools/publish-native.ps1`, `tests/native-smoke-android/smoke.js`; `Assert.False(Code("src/x/README.MD"))` after the
`-i` fix. R97 reflective half: regex-extract every `Read("…")`/`Path.Combine(RepoRoot(), …)` literal in `tests/Api.Tests/**/*.cs` and assert
`Code(path)`. Harness: fixture "fail-open" asserts all three outputs `true`.

### TB-DEP-5 — non-ASCII path (LB-DEP-2)
Harness fixture `classifier/non-ascii/`: builder creates a temp repo, commits `src/Api/Features/Añadir.cs` and `docs/Guía.md`, runs the
extracted block against **real** `git diff` (quotePath default) → expected `code=true native=true docs=true`. C# half:
`ForgejoCiParityTests` asserts `git -c core.quotePath=false diff --name-only` (or `--name-only -z`) in both copies.

### TB-DEP-6 — provider-probe grep (LB-DEP-4)
Harness fixture `smoke-probe/api.log` = the five p6 lines; extracted bash `probes()` → expect 1; extracted PowerShell `Probes` under
`pwsh` → expect 1. C# half: the four sites contain `providers - 200 - ` (regex over both `ci.yml`).

### TB-DEP-7 — shard step + Slowest journeys (LB-DEP-5/6, DEP-22, R113)
Harness: (a) 12-name list → three disjoint shards, each filter term anchored after the DEP-22 fix (`FullyQualifiedName~.Alpha_Journey(`
or `Name=`), the `Delta_Journey`/`Delta_Journey_Extended` pair lands in one shard only; (b) empty list → exit 1 AND the
`::error::only 0 journeys listed` line present; (c) a list containing an `[Explicit]` journey (name from a marker file) is excluded from
every shard (R113); (d) Slowest: 0-row trx → exit 0 with "no journeys"; 3-row trx → table; boot-line trx → `Blazor boot retries: 2`
(deduplicated) and, once UX-9's threshold lands, exit 1 when `> N` (N in one place, both copies — parity-asserted).

### TB-DEP-8 — qa-runlog guard (LB-DEP-7/11)
Harness: p10 base with (i) realigned separator → exit 0; (ii) executed row deleted → exit 1 naming it; (iii) executed row edited → exit 1;
(iv) blank row filled → exit 0; (v) a row re-ordered → exit 0. Runtime evidence for LB-DEP-11: the step writes `guard=ran|skipped` to
`$GITHUB_OUTPUT`; `qa-artifacts` fails on a `pull_request` event when `guard=skipped` AND `github.event.pull_request.base.sha` was set
(a PR base is always reachable — a skip there is a fetch problem, not a force push).

### TB-DEP-9 — operator PowerShell contract (LB-DEP-8, DEP-13's exit code)
`EnforcementGateTests.ToolsScripts_RequirePwsh7_AndFailLoud`: every `tools/*.ps1` starts with `#Requires -Version 7`, is ASCII or
BOM-prefixed, contains `$ErrorActionPreference = 'Stop'`, and has no `return` on a failure path without a preceding `$script:failed = $true`
/ final `exit 1`. Harness (pwsh): put a fake `gh` function/script first on PATH; `protect-branches.ps1 -Repo x/y` with a fake that
returns 1 on PUT → exit code 1 and output "FAILED"; branch missing → "skipped", exit 0; `publish-native.ps1` with a fake `dotnet` and a
fake `apksigner.bat` printing `v2 … false` → exit 1 (NAT-13), `true` → exit 0.

### TB-DEP-10 — DEP-19 red-smoke refusal + newest task (asked for by Phase 1)
Harness fixtures on the extracted `deploy.yml` block: D (build-test success then failure) → REFUSE naming build-test; E (selected
`native-smoke-android` failure) → REFUSE; J (`changes` running beside an old success) → REFUSE ("a run is still in progress"); K (smoke
`skipped`) → DEPLOY. Implementation hint that makes all four pass at once: group by `.name`, take the max `.id`, require `success` for
gates, and `success|skipped` for `native-smoke-*`.

### TB-DEP-11 — UX-8/9 BlazorBoot retry semantics with a fake page
Seam: `BlazorBoot.BootAsync(Func<Task> navigate, Func<Task> reload, Func<Task<string>> outcome, ConcurrentQueue<string> log, string what)`
— the `IPage` overloads become one-line adapters, so no Playwright fake is needed. Tests (`tests/E2E.Tests/BlazorBootTests.cs`, plain
NUnit, `[Category("Unit")]` so the shard filter keeps it out of the browser lanes, or in `Api.Tests` if E2E.Tests must stay browser-only):
(1) verdicts `loading → ok` → `navigate` once, `reload` never; (2) `fetch failed: … ERR_NETWORK_CHANGED` then `ok` → `navigate` **once**,
`reload` once (UX-8: the single-use magic-link URL is never re-spent — assert the original URL's navigate count == 1); (3) `banner at 47%`
with **no** `requestfailed …/_framework/` line after `seen` → throws immediately, no reload (UX-9: our own startup exception is not a
network death); (4) `banner` **with** a failed `_framework` fetch → reload; (5) three network deaths → `InvalidOperationException` whose
message contains the console and exactly one `[blazor-boot] attempt 3/3` (LB-DEP-6b: count the notes, not the exception);
(6) run-level budget: a static counter (reset per fixture) — the 4th dead boot in a run throws even if the reload would succeed (UX-9
"per-shard threshold"), with the number in one constant that the CI Slowest step reads from the trx and both copies print.
The grep gate R109 (`page.GotoAsync(`/`ReloadAsync(` only inside `BlazorBoot.cs`) goes in `EnforcementGateTests`.

### TB-NAT-1 — NAT-12b: MinIO presigned + `Authorization` (asked for by Phase 1)
In `S3FileStorageMinioTests` (fixture exists, 5 green): store a blob, `GetSignedUrl` (GET, 5 min), then (a) plain `HttpClient` → 200 +
bytes; (b) same URL with `Authorization: Bearer eyJ…` → assert **≠ 200** and body contains `InvalidArgument`/"Only one auth mechanism"
(documents that the launcher must not attach the bearer; if MinIO tolerates it, the test records that S3 proper does not and the
assertion becomes a `[Fact(Skip=…)]` with the S3 doc link — the leak half of NAT-12 stands regardless). (c) Ui.Tests: `BearerScopedHandler`
— relative URL → header; `https://<api-origin>/x` → header; `https://minio.example/x?X-Amz-Signature=…` → **no** header; `ShareFileDownloadLauncher`
uses the plain client. Arch test: `AuthenticationHeaderValue("Bearer"` appears only in that handler.

### TB-NAT-2 — NAT-13: keystore resolution on macOS/Linux (p7 pattern)
Move the Release guards into `src/Maui/ReleaseGuards.targets` (imported by the csproj). `EnforcementGateTests.ReleaseGuards_ResolveAKeystoreOnEveryHost`
writes a probe `.proj` importing that file and runs `dotnet msbuild -nologo -t:Probe -p:Configuration=Release` with
(a) `-p:LOCALAPPDATA=` and `-p:HOME=<tmp>` where `<tmp>/.local/share/Xamarin/Mono for Android/debug.keystore` exists → `AndroidKeyStore=true`,
`AndroidSigningKeyStore` = that path; (b) both empty and no file → the target `RequireReleaseKeystore` errors ("no keystore resolved —
supply AndroidSigning* or install the debug keystore"); (c) `-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=x` → untouched.
The macOS path (`$HOME/.local/share/Xamarin/Mono for Android/debug.keystore`) is the Xamarin default and must be verified once on the
MacBook (QA §13b line). `publish-native.ps1`: throws unless `apksigner verify` prints `v2 … true` **or** `v3 … true` (TB-DEP-9 fake).

### TB-NAT-3 — NAT-18 + LB-NAT-1b/2 guard negatives (evaluation-only, no workload)
Same probe project; table of `-p:Configuration`/`-p:ApiBaseUrl` → expectation: `Release,""` refuse · `Release," "` refuse ·
`Release,example.com` refuse (no scheme) · `Release,http://api.x` refuse (all TFMs) · `Release,https://api.x/v1` refuse (origin only — the
runtime never supported a path segment, MauiProgram:32/161/177) · `Release,https://api.x` accept · `Release,https://api.x/` accept
(PR #230 trailing slash) · `Staging,""` refuse (LB-NAT-1b) · `Debug,""` accept. Assert `AssemblyMetadata` carries the trimmed value.
Also assert the three guard predicates are the same string (`'$(Configuration)' != 'Debug'`).

### TB-NAT-4 — theme.js ↔ SystemBarThemeSync (LB-NAT-3, NAT-19, first-render ordering)
bUnit additions to `SystemBarThemeSyncTests`: (a) `JSInterop.SetupVoid("appTheme.watch")` throws → no exception escapes, `Applied` empty;
(b) render instance 1, dispose it, render instance 2 → invocation order is `watch, unwatch, watch` and the second `watch` is not followed
by an `unwatch`; (c) `Ground` toggled before any `OnThemeApplied` → nothing painted, then the first `OnThemeApplied` paints with the
**current** Ground. Node test (`tests/js-logic/theme.test.js`, `node --test`, hooked like the harness): load theme.js with stubs for
`window`, `document.documentElement`, `matchMedia` (controllable `matches` + listener set), `localStorage`; assert stored `"dark"` →
`data-bs-theme=dark` before any call; `set("system")` + a `change` event → attribute flips and the watcher receives it; `unwatch` stops
notifications; `watch` reports immediately; a `matchMedia` stub **without** `addEventListener` (only `addListener`) still defines
`window.appTheme` (LB-NAT-3); storage throwing → falls back to the OS scheme. Optional: `unwatch(ref)` no-ops for a stale ref (NAT-19).

### TB-NAT-5 — bfcache-guard behaviour (R110)
Node test: dispatch `new PageTransitionEvent('pageshow', {persisted: true})` with `location.reload` stubbed → called once;
`persisted: false` → not called. E2E (browser proof, Chromium needs `--enable-features=BackForwardCache` in the launch args — verify
Playwright's default disables it and set it for this journey only): sign in → sign out → `GoBackAsync()` → URL is `/login`, no
`data-testid=sign-out` attached. If headless Chromium cannot be made to serve a bfcache restore, record that and keep the unit test as the gate.

### TB-NAT-6 — smoke.js retry policy (LB-NAT-5, NAT-20)
Refactor: export `{ bootToLogin, run }` and guard the top-level IIFE with `if (require.main === module)`. `node --test` with a fake
`device` (`webView()` rejecting once then resolving a fake page with `getByTestId().waitFor()`): (1) attempt 1 fails → `am force-stop`
and `monkey` shell calls in that order, attempt 2 attaches; (2) both fail → `run` rejects with the attempt-2 error; (3) the household
link path succeeds → `goto` never called; (4) link click fails → `goto('https://0.0.0.1/household')` once **and** `::warning::` emitted
(NAT-20); (5) `device.close()` rejecting after success → still exit 0 (log the error).

## Harness readiness
- **Hosts:** what can run this today — `build-test` on `ubuntu-latest` (bash, jq, python3, pwsh, node present on the hosted image and in
  the Forgejo `ci-image`). Windows dev boxes: no `jq` (confirmed here), Git Bash present, pwsh 7 present, `powershell` 5.1 present;
  the harness must `Skip` cleanly there but assert-present on Linux.
- **Existing seams to reuse:** `ForgejoCiParityTests.Classifier()`/`Jobs()` (workflow text extraction by regex), `EnforcementGateTests`'
  `Code/Native` C# mirror (keep for cheap positives; it cannot see git quoting or `set -e`), `ForgejoCiParityTests` `Read()` root finder,
  `S3FileStorageMinioTests` (Testcontainers MinIO, quay-pinned), `SystemBarThemeSyncTests` (bUnit + `JSInterop` recorder),
  `tests/native-smoke-android/package.json` (node + playwright-core already `npm ci`'d in the Android leg — a `node --test` script slot is free).
- **Seams that must be created:** `BlazorBoot` delegate overload (TB-DEP-11); `smoke.js` module export (TB-NAT-6); `MIRROR_URL` override
  in `push-to-github.sh` (TB-DEP-2); `ReleaseGuards.targets` extraction (TB-NAT-2/3) — without it evaluating `Maui.csproj` needs the MAUI
  workload, which `build-test` does not install.
- **Evidence gaps:** Forgejo's job-logs endpoint is 404 on this instance, so "did the qa-runlog guard actually run" (LB-DEP-11) needs the
  `$GITHUB_OUTPUT` marker in TB-DEP-8; the MacBook was asleep for the whole delta (Apple legs skipped) — TB-NAT-2's macOS keystore path is
  a §13b line item; the Android night-mode flip (LB-NAT-4 open) is a device item (QA-AND).
- **Probability notes for triage:** LB-DEP-2 needs one non-ASCII filename (none today; likely downstream); LB-DEP-4 needs a non-200 probe
  whose ms field contains `200` (per-line ≈ 0.1–1 % plus the 200–209 ms band on a cold API — over a year of Monday smokes, expect one);
  LB-DEP-3 needs a matrix change (the e2e matrix changed twice in this delta alone).

## Candidate rules
- **R123d [machine]** — Every CI step that computes a verdict in shell/jq/PowerShell/python/JS (classifier, already-green, shard split,
  smoke-probe grep, deploy-smoke, qa-runlog, push diagnosis, Slowest journeys) has a fixture case in `tests/ci-logic/` that runs the
  **committed text** (extracted by anchor) on the Linux `build-test` leg; the harness runner is asserted present there and the
  verdict-step inventory (steps printing `::error::`) equals the fixture directory set. — `EnforcementGateTests.CiShellLogic_PassesItsFixtures`
  + TB-DEP-1. — LB-DEP-1, 3, 4, 5, 6, 7, 9, 10 (and the mechanism behind DEP-19/22).
- **R124d [machine]** — Path classification is byte-safe: every `git diff --name-only` in a workflow is invoked with
  `-c core.quotePath=false` (or `-z`), asserted in both copies by `ForgejoCiParityTests`, with a non-ASCII fixture in the harness. — LB-DEP-2.
- **R125d [machine]** — Matrix completeness is derived, never typed: the already-green deploy refuses when any task with a matrix job's
  prefix is not `success`, and the expected leg count (if kept) is parsed from `ci.yml`'s matrix by the parity test. Refines R98's
  "newest task per job" (which alone still passes a grown matrix with one red leg — p2b case B/C). — LB-DEP-3, DEP-19.
- **R126d [machine]** — A log-grep verdict matches the status **field**: the native probe pattern is `providers - 200 - ` in all four
  sites (both copies, both shells) and the harness holds the negative lines (elapsed-ms containing 200, sibling route, 404/500). — LB-DEP-4.
- **R127d [machine]** — Operator PowerShell under `tools/` starts with `#Requires -Version 7.0`, is ASCII or BOM-prefixed UTF-8, sets
  `$ErrorActionPreference = 'Stop'`, and exits non-zero on every failure path; a pwsh harness with fake `gh`/`dotnet`/`apksigner` proves the
  exit codes. — LB-DEP-8, DEP-13 (exit code), NAT-13 (verify verdict).
- **R128d [machine]** — Release-only MSBuild guards share ONE predicate (`'$(Configuration)' != 'Debug'`), validate values (trimmed
  non-empty, `https://`, origin-only), resolve a keystore on every host or error, and live in an importable `.targets` evaluated by a
  workload-free probe project in `EnforcementGateTests`. Machine half of R67/R103 for the guards themselves. — LB-NAT-1, LB-NAT-2, NAT-18, NAT-13.
- **R129d [machine]** — Each `wwwroot/js` bootstrap file defines its public object before registering listeners, guards feature-gated
  APIs, and has a `node --test` stub test of its state machine (theme set/watch/unwatch/change; bfcache pageshow). — LB-NAT-3, NAT-19, R110.
- **R130d [machine]** — The classifier's fail-open branch sets **every** output to its permissive value (`docs=true` included), fixture-asserted. — LB-DEP-9.

## RULE_CONFLICTS candidates
1. **R98 (Phase 1) — "takes the newest task per job"** is necessary but not sufficient: p2b cases B/C show a grown matrix with one red
   leg still deploys under newest-per-job. Proposed refinement (R125d): "no non-success task with the prefix" + a derived count. Not a
   contradiction; a strengthening.
2. **R75 (v3) — QA run-log append-only guard "on every PR"** may be vacuous on Forgejo (LB-DEP-11): the guard exits 0 on any fetch failure
   and the instance exposes no job-log API to check. If Phase 4/5 cannot produce a Forgejo run log showing "all executed base rows intact",
   R75's enforcement claim should be marked unverified for the primary forge until TB-DEP-8's marker lands.
3. **DEP-21/UX-9 (Phase 1) — "Boot retries counted, never thresholded"**: the count itself is over-stated (the giving-up exception
   message repeats the attempt line; Progress lines may duplicate it) — a threshold built on `trx.count` would trip early. Threshold on
   the deduplicated set (already computed by the step). Refinement of the R109 wording.
4. **NAT-13 (Phase 1) — "Probe platform keystore paths"** is confirmed as the right fix, with one precision: the `Exists()` with an empty
   `$(LOCALAPPDATA)` resolves to the drive/filesystem root (p7), so it cannot false-match a repo path; the risk is only the silent
   inactivity Phase 1 described. No conflict; Phase 1's "Likely" can be raised to Certain for the mechanism.

## Out-of-area observations
- `deploy-smoke.sh`: no wrong result found. An API that reports no `.commit` (build arg unset on Render) times out red after 12.5 min with
  "version never matched" — correct direction; a `::warning::` when `.commit` is empty on the first poll would save the wait (Info).
- `tests/E2E.Tests/BlazorBoot.cs:112`: the console queue is trimmed to 300 entries by dequeue, but `seen` is an index into the untrimmed
  count, so after trimming `log.Skip(seen)` skips too many lines and a genuine `requestfailed …/_framework/` can be missed → the verdict
  degrades to "still loading after 60 s" (slower, still retried). Info; fold into TB-DEP-11's seam.
