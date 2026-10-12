# v4 Delta Audit — Phase 5: Consolidation gate

> The only reconciler and the only gate to implementation. Reads every report's `## SUMMARY`, then
> `FOUNDATION_RULES.md` (candidates R81–R158) and `RULE_CONFLICTS.md` (C1–C29) in full. Date 2026-09-23.

## 1. Integrity check
| Phase | File | SHA stated | Matches |
|-------|------|-----------|---------|
| 1 | `AUDIT_REPORT.md` | `8c8ed4a746c57a6df8bac5f31195f011dc0758c4` | ✅ |
| 2 | `AUDIT_RECONCILIATION.md` | same (audit-doc commits on top do not change the code SHA) | ✅ |
| 3 | `LOGIC_AND_TEST_REPORT.md` | same | ✅ |
| 4 | `ADVERSARIAL_REPORT.md` | same; throwaway branch `audit/v4-2026-09-phase4-throwaway` @ `7c52cc4`, never merged | ✅ |
| 7 | (course reconcile, run early — reconciles docs with the same SHA) | same | ✅ |

**Gate: PASSES.** All phases audited one commit. Phases 1–4 wrote no production code (Phase 4's code lives only on
the throwaway branch). Phase 7 edited `docs/tutorial/**` only — teaching material, not production code.

## 2. Conflict resolutions (C1–C29)
Precedence: (a) security/tenancy/auth invariants from the adversarial pass outrank all; (b) a fix that would violate
a higher-precedence rule is rejected/replaced; (c) machine-enforceable beats review-enforced when equivalent;
(d) foundation-genericity beats slice convenience.

| # | Resolution | Effect on the final ruleset |
|---|-----------|-----------------------------|
| C1 | v3 T36 mis-marked: `allowBackup="true"` never landed. **Correct the v3 tracker with a dated note; fix under NAT-17 with the R105 posture gate.** | R105 final; v3 `IMPLEMENTATION_TRACKER.md` gets an erratum line (B9). |
| C2 | v3 T35 mis-marked: `Directory.Build.props` never in the native regex. Same treatment. | R106 final. |
| C3 | R67 stays [machine] in text; **R103 + R141 are its machine halves**. Until B7 lands, the v3.0 file marks R67 "enforcement pending (R103)". | R67 amended (status note). |
| C4 | R53 gains an enumerated-exception clause; **R122 (reflective posture with a named exception list) is the mechanism; R85 prevents the next silent exception.** Not an overturn. | R53 amended; R122, R85 final. |
| C5 | R63 amended: "…on forges that honour `permissions:`; on forges that ignore it, R98 applies". Precedence (a). | R63 amended; R98 final. |
| C6 | R51 not overturned. **R81 (one-shot grace) restores the bounded invariant** — adopted as the default decision (see §4 #1); the ADR-002 addendum is amended to state the trade is symmetric. If the owner rejects R81, R51's text becomes conditional. | R81 final; ADR-002 amendment task. |
| C7 | R68's gate is **widened** (ordering + `lib` byte-identity) rather than the text narrowed. Precedence (c). | enforcement-backlog item under R68. |
| C8 | **`FOUNDATION_RULES_v3.md` states the exact final range; the R77–R79 reservation is retired** (LOCALCI-1 superseded here; R79's intent is R114; LOCALCI-2's tripwires take fresh numbers when built); `ArchitectureTests.cs:111,153` cite R43/R44; R116 gates it. | header + R116 final. |
| C9 | R75 text amended: "…C# reads; deploy-time YAML keys are a DEPLOYMENT §10 review item (R100)". | R75 amended. |
| C10 | The narrowed GAP-1 Stripe guard is recorded inside R86: the route-table proof is the precondition for relaxing the fail-fast. | R86 final (absorbs R156). |
| C11 | R43 amended: "…and every dissolve call site uses the service" — **R123** is the machine half. Precedence (a). | R43 amended; R123 final. |
| C12 | ADR-002 (2026-09-22) and ADR-014 sentences amended; **R125** adopted (impersonation as state; expiry raises `IdentityChanged`). Precedence (a). | R125 final; ADR amendment task. |
| C13 | ADR-002 (2026-09-18) addendum amended; **R124** states the logout leg. Precedence (a). | R124 final. |
| C14 | R90 allowlists nothing; **the broadcast handler writes a system-scope audit row before the scrub applies** (TB-JOBS-12). Attribution (R52) and erasure (R90) both hold. | R90 final with the audit-row precondition in its text. |
| C15 | R57 amended: "advances **durably** (claim-time) and distinguishes permanent from transient failures" — R131 + R130 are the mechanisms. | R57 amended; R130, R131 final. |
| C16 | **R94 merged into R130** (number retired). | R94 retired. |
| C17 | **R138 refines R98** (no non-success task with the prefix + derived count); R98 keeps its token/protection clauses. | both final. |
| C18 | R75's run-log clause marked **unverified on Forgejo** until TB-DEP-8's `guard=ran/skipped` marker lands (B1). | R75 amended (status note). |
| C19 | R109's threshold counts the **deduplicated** attempt set. | R109 text adjusted. |
| C20 | R70 amended with a per-page floor: every `Pages/*.razor` has ≥1 bUnit test. Precedence (c). | R70 amended. |
| C21 | **Decision: audit operational writes under impersonation** (webhook test/replay write an audit row with `impersonated_by`) rather than exempt them — precedence (a) and R52's intent; recorded as R50's minimal-API clause. | R50 amended (scope: `MapGroup` endpoints too). |
| C22 | Gate-state proof is owned by bUnit (both states) + **one gate-off E2E lane (R147)**; R73 is not satisfied by a gate-on suite. | R73 amended; R147 final. |
| C23 | v3 R84-cand stays review-only in text; **R146** is its machine half. | R146 final. |
| C24 | The injected-clock gate is widened to `src/Shared.Ui` (**R148**). | R148 final; R15 note. |
| C25 | NAT-13's mechanism raised to Certain; fix unchanged (R141). | — |
| C26 | ADR-004 re-amended with the measured **list** of touchpoints; **R158** gates the checklist against the gates. | R158 final; ADR-004 task. |
| C27 | **R122 lands** (reflective posture); R53's text is not weakened. Precedence (c). | as C4. |
| C28 | **R155** adds the startup route-uniqueness check; R36 keeps the CI scan. | R155 final; R36 note. |
| C29 | Phase 2's build claim restated as "0 C# warnings"; **R152** promotes MSB3568 and gates resx duplicates. | R152 final; `AUDIT_RECONCILIATION.md` wording note (kept as-is with this erratum). |

**Two irreconcilable Critical invariants?** None. No Critical was filed; every High has a fix that violates no
surviving rule.

## 3. Cross-check — every proposed fix against the full ruleset
| Fix | Checked against | Verdict |
|-----|-----------------|---------|
| R123 (accept dissolves via `ITenantDissolutionService`) | R44 (dissolve enters the target tenant) ✓ the service already does; R39 (no `QueryAllTenants`+`ExecuteUpdate`) ✓ unchanged; ADR-029 (single membership) ✓ | OK |
| R81 (one-shot grace) | ADR-002 addendum's two-tab race ✓ (each tab presents once); R107 (client timeout inside grace) ✓ required together; R51 ✓ restored | OK |
| R124 (logout revokes any known token) | R48 (single-use atomic consume) ✓ orthogonal; R45 (staff gate) ✓ | OK |
| R125/R126 (epoch, server-relative lifetime, retry-on-401) | R71 (no reconcile while impersonating) ✓ strengthened; R72 (deep-link preservation) ✓ untouched; R45 ✓ | OK |
| R90/R91 (outbox scrub, stamping, contributor, retention) | R52 attribution — **conflict C14 resolved by the audit-row precondition**; R43 canary extension ✓; R57 ✓ | OK |
| R130 (`AllowAutoRedirect=false`, pinned connect, 3xx = failure, refusal permanent) | R3/R76 SSRF ✓ (strengthens); R57 amended ✓; HOOKS-2 replay semantics ✓ (a 3xx row is a failed delivery the owner can replay) | OK |
| R131 (claim-time accounting) | R57 amended ✓; R58 (23505-only quota recovery) ✓ unrelated; at-least-once contract (ADR-007) ✓ preserved | OK |
| R86 (route-table gate incl. admin writes) + BILL-1/2 default decision | ADR-021 enumerated admin writes ✓ (gated by name, not removed); R64 ✓; ADR-027 amended to the decided semantics | OK |
| R98 (persist-credentials off, branch protection, script exit 1) | R80 parity ✓ (the Forgejo copy diverges by allowlisted lines); R63 amended ✓; `qa-artifacts` fetch step allowlisted with its own token ✓ | OK |
| R102 (bearer scoped to API origin) | R68 host parity ✓ (shared handler in Shared.Ui serves both); FILES-3 presigned URLs ✓ | OK |
| R103/R141 (Release leg + guard predicates) | R60 native-paths ✓ (R106 adds the script); R61 pins ✓ (workload set reused); R80 parity ✓ (job in both copies) | OK |
| R152 (MSB3568 as error) | R72 resx parity ✓; MAUI MSB3073 excluded by scoping ✓ | OK |
| R147 (gate-off E2E lane) | R80 parity ✓; R56/R63 deploy gating ✓ (lane is a non-deploy job gated on `code`) | OK |
| R114/R115 (course gates) | LOCALCI-3 "never gate docs checks on code" ✓ (runs beside `qa-artifacts`) | OK |
| R155 (startup route check) | R36 ✓ complementary; boot-time cost negligible; fails closed | OK |

No proposed fix re-introduces another phase's flagged problem. **Step 2 passes.**

## 4. Human decisions — attached to batches, with the default adopted for planning
The plan below assumes the **recommended** answer; the batch lists the alternative. Any override changes only the
named tasks.
| # | Decision | Recommended default | Batch |
|---|----------|--------------------|-------|
| 1 | Grace one-shot (R81)? | **Yes** — closes unbounded replay, keeps the two-tab race; addendum states the symmetric trade | B4 |
| 2 | Absolute session lifetime (AUTH-4)? | **Ship an optional `RefreshToken:AbsoluteLifetimeDays` (default 0 = off)** — a knob, not a posture change | B4 |
| 3 | Invitee tenant-of-one (AUTH-8)? | **Document as accepted** in ADR-027 (the "admits nobody new" argument holds); no deferral of creation | B9 |
| 4 | Rate limit `/api/auth/refresh`? | **Yes, per-IP fixed window** (cheap; amplification remains without it even after R81) | B4 |
| 5 | Comp-while-off (BILL-2) & gate-off semantics for provider projections (BILL-1)? | **Gate the admin comp/revert with the billing surface (404 while off)**; keep `ResolvePlanKey` gate-blind (no silent downgrade) **+ a startup warning** when off with provider-managed rows; truth-up ADR-027 | B6 |
| 6 | Outbox retention + attachment storage (JOBS-2)? | **Scrub `Payload` on terminal, purge after `Outbox:RetentionDays=30`; keep inline attachments** (bounded); revisit `IFileStorage` keys when a downstream needs >10 MiB | B3 |
| 7 | Tenant-visible failure detail (JOBS-1)? DPA note (OBS-1)? `AllowAutoRedirect=false` (JOBS-9)? | **Coded reason; add the data-class note to DEPLOYMENT §observability; `AllowAutoRedirect=false` (guard already forces https)** | B5 |
| 8 | Forgejo token write probed? GitHub role? Branch protection home? Maui lockfile on the desk? Already-green vs red smoke? | **Treat the token as write (probe optional); GitHub = mirror — knob-gate its Apple legs; protection on BOTH forges (Forgejo now, GitHub when Pro/public); commit per-host Maui lockfiles; red selected smoke refuses** | B2 |
| 9 | Native: v1-only on a Mac (NAT-13), MinIO presigned+bearer (NAT-12b), keep "debug key signs Release"? | **R141 errors when no keystore resolves (safer default); TB-NAT-1/2 settle the two facts** | B7 |
| 10 | Client: shorten refresh timeout vs widen grace (UX-6)? bfcache reload for signed-in users (UX-10)? E2E count definition? | **Client timeout 20 s (safer); keep the reload, record it in ADR/QA-SEC-03; "34 browser journeys (+1 explicit native smoke)"** | B4/B8/B9 |
| 11 | Docs: R77–R79 fate; course teaches Forgejo?; `docs/tutorial/` in the doc map; Postman description floor enforced? | **Retire the reservation; yes — 8.3 §5 already landed in Phase 7; yes — add the row + the reconcile rule; yes (R119)** | B9 |
| 12 | Slice under a platform-gated prefix ever allowed (ADV-P4)? Pre-auth 500 worth a startup check? MSB3568 scope? | **Forbidden (R86 by prefix); yes (R155, cheap); scoped to Shared.Ui/Infrastructure resx** | B1/B6 |
| 13 | Audit HOOKS operational writes under impersonation (C21)? | **Yes** (precedence (a)) | B5 |

## 5. Outputs
- `FOUNDATION_RULES_v3.md` — the final consolidated ruleset (binding once B10 is green).
- `AUDIT_TASKS.md` — ten one-session batches, keystone first / enforcement last, done-when + verify per task.
- `IMPLEMENTATION_TRACKER.md` — task-granular status board for the discuss-then-implement loop.
- Phase 6 (`QA_TEST_PLAN.md` + PDFs) follows this gate; Phase 7 (course) landed early on this branch (commits
  `aa5e990`…`9d84a1a`) because it depends only on the audited SHA, not on remediation decisions.

**Report-only until the plan is approved.** Then implement test-first, one change at a time, each verified against
the final rules; update `CONTRIBUTING.md`'s Definition of Solid to v3.0 in B10.
