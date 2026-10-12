# v4 Remediation — Task-Granular Implementation Tracker

> **Truthed up 2026-10-06:** every task row now states where it landed. The live record during the remediation was the GitHub issues (platform #238–#306, one per task, in milestones v4 B1–B10 and v4 Later, all closed); this file had stopped at the B-batch rows. Two items left the audit on purpose: the test-id contract (R149 → Arch A12, #371) and jigger-jot's shared-or-household catalog (→ Arch A4, #364).

> Mirrors `AUDIT_TASKS.md` (T1–T69). **High-severity items are worked first from `HIGH_SEVERITY_TRACKER.md`** (H1–H9). **Workflow: discuss each task → implement it (test-first) → verify → next.**
> Nothing is implemented before its discussion and before the plan is approved (`PHASE5_GATE.md` §5).
> Conventions (repo memory): branch off `develop` (never `main`); one batch ≈ one branch/PR unless decided otherwise;
> commit per task; **do not `git push` until confirmed**; GitHub is the only forge since ADR-030 (2026-10-02); TDD — the failing test
> lands before the production code; QA plan / Postman / docs / the course lesson that quotes the changed code updated
> in the same task. Re-verify each finding still reproduces on current `develop` before fixing.
>
> Status legend: ⬜ not started · 🔵 discussing · 🟡 in progress · ✅ done · ⏸️ deferred/decision-pending · 🚫 closed as moot.

## Decisions (PHASE5_GATE §4) — defaults adopted unless overridden
| # | Decision | Default | Status |
|---|----------|---------|--------|
| 1 | Grace one-shot (R81) | Yes | ⏸️ awaiting owner |
| 2 | Absolute session lifetime | Optional knob, default off | ⏸️ |
| 3 | Invitee tenant-of-one | Document as accepted | ⏸️ |
| 4 | Rate limit `/api/auth/refresh` | Yes, per-IP | ⏸️ |
| 5 | Comp-while-off / gate-off semantics | Gate comp with billing; gate-blind resolve + startup warning | ⏸️ |
| 6 | Outbox retention / attachment storage | Scrub + purge at 30 d; keep inline | ⏸️ |
| 7 | Failure detail / DPA note / redirects | Coded reason; note; `AllowAutoRedirect=false` | ⏸️ |
| 8 | Forge token / GitHub role / protection / Maui lockfile / red smoke | Treat as write; mirror + knob; both forges; per-host lockfiles; refuse | ⏸️ |
| 9 | Native keystore default / NAT-12b / NAT-13 | Error when no keystore; TB-NAT-1/2 settle | ⏸️ |
| 10 | Client timeout vs grace / bfcache / E2E count | Client 20 s; keep reload; "34 (+1)" | ⏸️ |
| 11 | R77–R79 / course Forgejo / doc map / Postman floor | Retire; yes (landed); yes; yes | ⏸️ |
| 12 | Slice under gated prefix / startup route check / MSB3568 scope | Forbidden; yes; scoped | ⏸️ |
| 13 | Audit HOOKS operational writes under impersonation | Yes | ⏸️ |

## B1 — Keystone gates
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T1 | LB-DEP-2 | High | byte-safe `git diff` in both classifiers + parity fact | R137 | no | ✅ H3 — platform #19 + both downstream (2026-09-24) |
| T2 | DEP-15, LB-DEP-9/10, NAT-16 | Med | classifier `code=`/`native=` widening, fail-open permissive, `-i`, reflective read-path test | R97, R106, R143 | no | ✅ platform #239 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T3 | LB-DEP-4 | Med | status-field provider-probe grep ×4 | R139 | no | ✅ platform #240 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T4 | LB-DEP-3, DEP-19 | Med | already-green: non-success refusal, newest task, derived counts | R138 | no | ✅ platform #241 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T5 | ADV-P4-12 | Low | boundary-matched namespace scan | R157 | no | ✅ platform #242 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T6 | ADV-P4-10, OBS-3, BILL-4 | Low | config-catalog reflection over `SectionName`, flat reads, single read site | R153, R95, R87 | no | ✅ platform #243 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T7 | S0-G8, AUTH-11 | Low | reflective posture gate with named exceptions | R122 | no | ✅ platform #244 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T8 | LB-DEP-1/5/6/7/11, DEP-22 | Med | shell-logic harness `tests/ci-logic/` + the six script fixes | R136 | no | ✅ platform #245 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T9 | ADV-P4-11 | Med | resx duplicate gate + MSB3568 as error | R152 | no | ✅ platform #246 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T10 | vacuous OtherTenant tests | High | `TwoTenants.SeedAsync` + arch scan | R146 | no | ✅ H4 — platform #20 + both downstream (2026-09-24) |
| T11 | TR-15 | Med | rule-id hygiene gate + test comment fixes | R116 | no | ✅ 2026-10-03 (v4 Later): platform #248; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T12 | UX-15 | Info | `[Explicit]` excluded from shards; suite size asserted | R113 | no | ✅ platform #249 (B1) + both downstream — landed before the 2026-10-02 GitHub move |
| T13 | ADV-P4-13 | Low | startup route-uniqueness check | R155 | yes | ✅ platform #250 (B1) + both downstream — landed before the 2026-10-02 GitHub move |

## B2 — Forge trust boundary
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T14 | DEP-13 | High | Forgejo branch protection + script exit codes (operator run on 3 repos) | R98, R140 | no | ✅ H1 — platform #19; protection applied on all 3 Forgejo repos (2026-09-24) |
| T15 | DEP-14, DEP-27 | High | `persist-credentials: false`; protection check in `changes`; consistent `permissions:` | R98 | no | 🟡 H2 — persist-credentials + protection check merged (#19, 2026-09-24); DEP-27 consistent `permissions:` still open → ✅ the rest landed with B1 (platform #252); the protection check is moot since ADR-030/031 (no branch protection on GitHub Free, by decision 2026-10-02) |
| T16 | DEP-18 | Med | image-pin gate over all four surfaces; `postgres:17.x` | R99 | no | ✅ 2026-10-06 (v4 Later): platform #253 via PR #372; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T17 | DEP-17, DEP-26 | Med | GitHub Apple legs behind a knob; comment truth-up; PAT bypass note | R101 | no | 🚫 closed as moot 2026-10-06 — platform #254: the GitHub-as-mirror knob belonged to the Forgejo era; ADR-030/031 made GitHub the forge |
| T18 | DEP-20 | Med | per-host MAUI lockfiles + `--locked-mode` on the desk; ADR-028 residual risk | R61-adj | no | 🚫 closed as moot 2026-10-06 — platform #255: the desk runner's locked-mode leg went with the Forgejo runners (ADR-030/031) |
| T19 | DEP-16/25/24 | Low | runbook rollback + numbers + parity summary + playbook step ② | R100 | no | ✅ 2026-10-06 (v4 Later): platform #256 via PR #372; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T20 | DEP-23 | Low | CI image sha256 pin + sign check (server folder) | R63-adj | no | 🚫 closed as moot 2026-10-06 — platform #257: the server-folder CI image was retired with Forgejo (ADR-030/031) |

## B3 — Tenancy / erasure completeness
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T21 | LB-AUTH-5 ≡ LB-BILL-19 | High | accept dissolves via the service; delete `DeleteTenantAsync`; harness overload | R123 | yes | ✅ H5 — platform #20 + both downstream (2026-09-24) |
| T22 | JOBS-2 (stamping) | High | email enqueues stamp `TenantId`; `OutboxDataContributor`; canary read | R91 | yes | ✅ platform #259 (B3) + both downstream — landed before the 2026-10-02 GitHub move |
| T23 | JOBS-2 (retention), C14 | High | payload scrub + terminal stamp + retention job + broadcast audit row + user erasure scrub | R90 | yes | ✅ platform #260 (B3) + both downstream — landed before the 2026-10-02 GitHub move |
| T24 | LB-BILL-23 | Med | webhook tenant-existence check | R129 | yes | ✅ platform #261 (B6) + both downstream — landed before the 2026-10-02 GitHub move |
| T25 | TB-TEN-10..15/23/24/25 | Med | signup-gate isolation negatives + recording doubles | R151, R146 | no | ✅ 2026-10-03 (v4 Later): platform #262; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T26 | TB-TEN-27 | Med | migration Down-on-data walk | R150 | no | ✅ 2026-10-03 (v4 Later): platform #263; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T27 | R145 | Med | canary over nullable-`TenantId` tables | R145 | no | ✅ 2026-10-03 (v4 Later): platform #264; downstream vuelto #196 / jigger-jot #171 (2026-10-06) — jigger-jot's shared-or-household catalog is excluded from the gate and goes upstream as Arch A4 (#364, jigger-jot #165) |

## B4 — Auth / session
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T28 | AUTH-1, AUTH-13 | Med | one-shot grace + Warning + ADR-002 addendum | R81 | yes | ✅ platform #265 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T29 | LB-AUTH-4 | Med | logout via inspect; revoke any known token | R124 | yes | ✅ platform #266 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T30 | UX-6/7/12 | Med | refresh timeout + pinned invariant; rejected-by-body; capped backoff | R107, R108, R144 | yes | ✅ platform #267 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T31 | LB-UI-11/12/13 | Med | session epoch; impersonation as state; ADR-002/014 amendments | R125 | yes | ✅ platform #268 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T32 | LB-UI-14/15 | Med | server-relative lifetime; retry-on-401; clock-offset seam | R126 | yes | ✅ platform #269 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T33 | AUTH-5, UX-11 | Low | session-held layout gate; `/login` on sign-out | R84, R111 | yes | ✅ platform #270 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T34 | LB-AUTH-6/7 | Med | one invitation validity predicate; expired invites free seats | R127 | yes | ✅ platform #271 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T35 | AUTH-7 | Low | list settings normalized at bind | R82 | yes | ✅ platform #272 (B4) + both downstream — landed before the 2026-10-02 GitHub move |
| T36 | AUTH-12, UX-17, LB-UI-16, AUTH-4, AUTH-1 (limiter) | Low | external cookie; pref shadow; forced refresh; absolute-lifetime knob; refresh limiter | — | yes | ✅ platform #273 (B4) + both downstream — landed before the 2026-10-02 GitHub move |

## B5 — Jobs / email / webhooks / observability
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T37 | LB-JOBS-1/2/3, JOBS-9 | High | no redirects; pinned connect; 3xx = failure; permanent failures dead-letter | R130 | yes | ✅ platform #274 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T38 | LB-JOBS-7/8 | Med | claim-time accounting; terminal stamp; rune-safe truncate; clamped backoff | R131, R135 | yes | ✅ platform #275 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T39 | JOBS-1/5/6 | Med | enumerated error codes; truncation; cancellation filter | R89, R96 | yes | ✅ platform #276 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T40 | JOBS-3/4, LB-JOBS-4 | Med | strict attachment validation; wire-size cap; count cap | R92, R132 | yes | ✅ platform #277 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T41 | OBS-1 | Med | `{Email}` → `{UserId}` ×8; PII template gate; DPA note | R93 | yes | ✅ platform #278 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T42 | OBS-2/3, JOBS-10 | Low | protocol from one value; unreachable-collector warning; DI scope assertion | R95 | yes | ✅ platform #279 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T43 | LB-BILL-24, BILL-5 | Low | sweep transaction; gate-aware; real outbox sender in job tests | R133 | yes | ✅ platform #280 (B5) + both downstream — landed before the 2026-10-02 GitHub move |
| T44 | LB-BILL-26, JOBS-7, C21 | Low | invariant period key; webhook write limiter; audit rows under impersonation | R134, R50 | yes | ✅ platform #281 (B5) + both downstream — landed before the 2026-10-02 GitHub move |

## B6 — Billing gate
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T45 | BILL-2/3, ADV-P4-6 | Med | route-table gate by prefix; admin comp gated; console follows features | R86 | yes | ✅ platform #282 (B6) + both downstream — landed before the 2026-10-02 GitHub move |
| T46 | BILL-1, LB-BILL-21/22/27 | Med | gate-blind resolve + startup warning + ADR-027 truth-up; provider mapping fails safe+loud | R128 | yes | ✅ platform #283 (B6) + both downstream — landed before the 2026-10-02 GitHub move |
| T47 | BILL-6/7, UX-13 | Low | stale "3" sweep + doc-grep gate; feature re-probe | R88 | yes | ✅ 2026-10-04 (v4 Later D): platform #284; both downstream the same day |
| T48 | BILL-10 | Med | gate-off E2E lane in both copies + lane-per-gate assertion | R147 | no | ✅ 2026-10-06 (v4 Later): platform #285 via PR #372; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |

## B7 — Native
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T49 | NAT-12 | Med | `BearerScopedHandler`; plain client for downloads; MinIO presigned+bearer test | R102 | yes | ✅ platform #286 (B7) + both downstream — landed before the 2026-10-02 GitHub move |
| T50 | NAT-13/14/18, LB-NAT-1/2 | Med | `ReleaseGuards.targets` + probe; Release CI leg; script verify | R103, R141 | no | ✅ platform #287 (B7) + both downstream — landed before the 2026-10-02 GitHub move |
| T51 | NAT-17, NAT-15, NAT-22 | Low | manifest posture gate; signing hygiene; docs; icon ground gate | R105, R104 | no | ✅ 2026-10-03 (v4 Later): platform #288; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T52 | LB-DEP-8 | Low | PowerShell contract + pwsh fixtures | R140 | no | ✅ 2026-10-03 (v4 Later): platform #289; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T53 | LB-NAT-3, NAT-19/20/21 | Info | theme.js ordering + node tests; smoke.js export; parity docs | R142 | yes (js) | ✅ 2026-10-03 (v4 Later): platform #290; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |

## B8 — Harness seams, client/E2E correctness, RCL coverage
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T54 | v3 gap 2 | High (seam) | DB-fault injector + concurrency runner; land blocked fault specs | R7 | no | ✅ platform #291 (B8) + both downstream — landed before the 2026-10-02 GitHub move |
| T55 | UX-8/9, DEP-21 | Med | `BlazorBoot` seam; landed-URL reload; verdict classes; budget; threshold | R109 | no | ✅ platform #292 (B8) + both downstream — landed before the 2026-10-02 GitHub move |
| T56 | TOOL-7, C20 | Med | bUnit page tests ×26 + per-page floor + HTTP stub 3xx/timeout | R70, R112 | yes | ✅ 2026-10-03 (v4 Later): platform #293; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T57 | TB-UI-65, TOOL-8 | Med | RCL clock injection; `AuthService` refactor | R148 | yes | ✅ 2026-10-06 (v4 Later): clock injection + AuthService split (1,158 → 586 lines, eight files beside it) — platform #294 via PR #372; the <500 figure dropped by the owner; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T58 | UX-10, TB-UI-69..71 | Low | bfcache/keep-alive/theme E2E journeys | R110 | no | ✅ 2026-10-03 (v4 Later): platform #295; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T59 | TB-DOC-5, ADV-P4-14 | Low | test-id contract; per-gate harness seam; all-gates-on Postman parity | R149, R154 | no | ✅ 2026-10-03 (v4 Later): per-gate harness seam + all-gates-on Postman parity — platform #296; downstream vuelto #196 / jigger-jot #171 (2026-10-06). The test-id contract (R149) is Arch A12 (#371) |

## B9 — Docs, ADRs, runbooks, QA, course
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T60 | TR-13/14 | High (docs) | deploy-trigger wording ×3 + grep gate | R117 | no | 🟡 TR-13 fixed in Phase 6; TR-14 (H9) merged #20 with a Postman-wording gate (2026-09-24); the broad `auto-deploys` grep gate is still open → ✅ closed with B9 (platform #297) |
| T61 | TR-19, TR-12.. | High (docs) | **course reconcile ✅ landed in Phase 7** (`aa5e990`…`9d84a1a`); coverage CI step + quote sweep pending | R114, R115 | no | ✅ platform #298 (B9) + both downstream — landed before the 2026-10-02 GitHub move |
| T62 | TR-15 (docs) | Med | v2 header erratum → v3 | R116 | no | ✅ 2026-10-03 (v4 Later): platform #299; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T63 | TR-20/21/24/25/26/27, OBS-4, NAT-21, DEP-25, AUTH-10 | Med/Low | FLOWS/ARCHITECTURE; Postman descriptions + code parity; limits block; doc-map rows + gate; story statuses | R121, R83, R119, R120, R118 | no | ✅ 2026-10-04 (v4 Later D): FLOWS signup branches + auth error codes; ARCHITECTURE SignupGate, §11 observability, §12 class index; Postman descriptions name every error code, the signup refusal and the `Billing:Enabled` 404; `.env.example` limits; doc-map rows + course-reconcile rule; ADR-025/026 stubs; story truth-ups. Gates: `PostmanParityDescriptionTests` (R83, R119), `CompiledInLimits_AreListedInTheEnvExample` (R120), `ArchitectureAndFlows_NameTheClassesAndTheAuthErrorCodes` (R121), `ClaudeMdDocMap_ListsEveryDoc` + `ClaudeMd_CarriesTheCourseReconcileRule` (R118). NAT-21, the `localci.md` status line and DEP-25's parity-test summary were already settled (the last by ADR-030 removing the file) |
| T64 | C6/C12/C13/C26, UX-18, AUTH-8, C1/C2 | Med | ADR amendments (002/004/014/022/027/028); WAYS_OF_WORKING checklist + gate; v3 tracker errata | R85, R158 | no | ✅ platform #301 (B9) + both downstream — landed before the 2026-10-02 GitHub move |
| T65 | TOOL-5/6 | Low | xunit.v3 migration story; adapter bump | R66-adj | no | ✅ 2026-10-04 (v4 Later D): `docs/stories/test-toolchain.md` (epic TOOLS; TOOLS-1 xunit.v3 planned) + `NUnit3TestAdapter` 6.3.0 — `--deprecated` clean for `E2E.Tests` |
| T66 | Phase 6 | — | QA plan v4 cases + PDFs + counts | R75 | no | ✅ Phase 6 (§14d, 156→173 cases) |

## B10 — Enforcement close-out
| # | Finding(s) | Sev | Task | Rule | Core? | Status |
|---|-----------|-----|------|------|-------|--------|
| T67 | all `[machine]` | — | promote every remaining machine rule to a standing check | all | no | ✅ 2026-10-03 (v4 Later): platform #304; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T68 | R7 mandate | — | CONTRIBUTING + CLAUDE.md → v3.0 | R7 | no | ✅ 2026-10-03 (v4 Later): platform #305; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |
| T69 | re-drill | — | nine corrected gates fire by name on a throwaway branch | — | no | ✅ 2026-10-03 (v4 Later): platform #306; downstream vuelto #196 / jigger-jot #171 (2026-10-06) |

---
**69 tasks; 13 decisions pending (defaults recorded above).** Every Phase 1–5 finding is represented (see the
traceability table in `AUDIT_TASKS.md`).
