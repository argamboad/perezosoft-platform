# JOBS / EMAIL / WEBHOOKS / OBSERVABILITY — v4 Phase 1 raw report
SHA: 8c8ed4a · BASE: f52be3d

## SUMMARY
Counts: Critical 0 · High 1 · Medium 4 · Low 5 · Info 3 (13 findings; JOBS-1..9, OBS-1..4).

Headline: (1) JOBS-4's attachments ride a non-tenant-scoped outbox table that nothing ever purges or erases, so
up to 10 MiB of tenant documents plus the recipient's email persist forever, outside every erasure/dissolve
path (JOBS-2, High). (2) #194 makes the webhook delivery log's `Error` column real for failures — and it is raw
`ex.Message`, returned verbatim to the tenant by `GET /api/webhooks/{id}/deliveries`, contradicting the very
GAP-3 comment above it and v2's R16 (JOBS-1, Medium). (3) #212 exports every log record's state values to the
OTLP collector; eight existing log lines carry user email addresses as state, and no scrubbing exists (OBS-1,
Medium). Also verified at runtime (MimeKit 4.17.0): the pre-enqueue guard lets a malformed media type through,
which then dead-letters at dispatch — exactly the loop the slice says it prevents (JOBS-3, Medium).

Rules proposed: R81j (no `ex.Message` in tenant-visible persisted diagnostics — machine half of R16), R82j
(outbox payload scrub + retention purge), R83j (tenant-stamped enqueue + dissolve wipe of pending rows), R84j
(enqueue-time validation as strict as dispatch-time parsing), R85j (log state = identifiers only), R86j
(no auto-redirect on user-URL HttpClients), R87j (config gate captures flat indexer reads), R88j (truncate
free-text diagnostics to column length at the write site).

## Delta surface examined
PRs: #235 (email attachments), #194 (webhook delivery-log failures) + the 2026-08-28 send-test change
(landed via the `claude/*` PRs #199–#201), #208 (OTLP http signal paths), #212 (logs export + runtime metrics).
Files read in full: `src/Core/Abstractions/IEmailSender.cs`, `src/Infrastructure/Email/{OutboxEmailSender,
EmailOutboxHandler,SmtpEmailSender}.cs`, `src/Infrastructure/Outbox/{EfOutbox,OutboxProcessor,OutboxDispatcher,
OutboxOptions}.cs`, `src/Core/Entities/{OutboxMessage,WebhookDelivery}.cs`, `src/Core/Abstractions/IOutbox.cs`,
`src/Infrastructure/Persistence/Configurations/{OutboxMessage,WebhookDelivery}Configuration.cs`,
`src/Infrastructure/Webhooks/{WebhookOutboxHandler,WebhookSender}.cs`, `src/Infrastructure/Http/OutboundUrlGuard.cs`,
`src/Api/Services/{WebhookService,NotificationService,WebhookDataContributor}.cs`, `src/Api/Endpoints/WebhookEndpoints.cs`,
`src/Api/Observability/{OtlpEndpoints,TelemetryExtensions,RequestLoggingScopeMiddleware}.cs`, `src/Api/Configuration/RateLimiting.cs`
(policy list), `src/Infrastructure/Scheduling/ExpiredTokenCleanupJob.cs`, `src/Infrastructure/ServiceCollectionExtensions.cs` (delta + email/webhook registrations),
`src/Api/Program.cs` (logging block). Tests: `tests/Api.Tests/Email/SmtpMessageBuilderTests.cs`, `tests/Core.Tests/EmailAttachmentTests.cs`,
`tests/Api.Tests/Outbox/OutboxEmailTests.cs` (delta), `tests/Api.Tests/Webhooks/WebhookDeliveryLogTests.cs` (full) + `WebhookDeliveryTests.cs`/`WebhookSubscriptionServiceTests.cs` (delta),
`tests/Api.Tests/Observability/{OtlpEndpointsTests,TelemetryLogsExportTests}.cs`, `tests/Api.Tests/Notify/NotificationFanOutTests.cs` (delta),
`tests/Api.Tests/DocAndConfigSyncTests.cs` (config gate regexes), `tests/Api.Tests/Infrastructure/PostgresFixture.cs` (delta), `EnforcementGateTests`/`ArchitectureTests` delta (grep).
Docs: `.env.example` delta, `docs/stories/{async-jobs,hooks,observability}.md` deltas, `docs/DECISIONS.md` ADR-007 (2026-09-16) + ADR-008 (2026-09-07) amendments,
`docs/DATA_MODEL.md` outbox sections, `docs/DEPLOYMENT.md:269`, `docs/QA_TEST_PLAN.md` (grep), v2/v3 audit docs (grep for prior adjudication of outbox retention, GAP-3, redirects — only LB-TEN-2 touches this area).
Runtime probe (read-only, scratchpad console app against MailKit 4.17.0): `ContentType.Parse` and `BodyBuilder.Attachments.Add` behaviour — results quoted in JOBS-3/JOBS-4.

## Findings

### JOBS-2 · High · `src/Infrastructure/Email/OutboxEmailSender.cs:29`, `src/Core/Entities/OutboxMessage.cs:10-13`, (absence in) `src/Api/Services/{TenantDissolutionService,TenantExportService,AccountErasureService}.cs`, `src/Infrastructure/Scheduling/ExpiredTokenCleanupJob.cs`
**What.** Attachment bytes (≤ 10 MiB, base64 ⇒ ~13.3 MiB of JSON) are written into `OutboxMessages.Payload`, a row that (a) carries no `TenantId` for emails, (b) is never purged after `Sent`, and (c) is touched by no erasure/dissolve/export contributor.
**Evidence.**
- `OutboxEmailSender.cs:29`: `await outbox.EnqueueAsync(MessageType, payload, cancellationToken: cancellationToken);` — no `tenantId` argument (the `IOutbox` overload has one; the webhook publisher passes it, email does not).
- `OutboxMessage.cs`: "Deliberately NOT `ITenantScoped` … The optional `TenantId` is context for handlers, not a scoping key." No FK, no filter, no RLS policy.
- `OutboxProcessor.cs:78-79` sets `Status = Sent; ProcessedAt = now;` and keeps `Payload` intact. `grep -rn "OutboxStatus.Sent\|ProcessedAt" src` finds no reader other than the processor — no purge, no scrub.
- `ExpiredTokenCleanupJob.RunAsync` deletes only `LoginTokens`/`RefreshTokens`; the only other `IScheduledJob` is `SubscriptionLapseSweepJob`.
- `grep -n -i outbox` over `TenantDissolutionService.cs`, `TenantExportService.cs`, `AccountErasureService.cs` → zero hits. `docs/stories/async-jobs.md:261-263` still says "pending outbox rows for a dissolving tenant should be drained or cancelled … Audit this when wiring tenant dissolve." — never done.
- ADR-007 amendment 2026-09-16: "The attachment bytes travel **inside the outbox payload** … rather than via `IFileStorage` + a key … That is only acceptable because the size is bounded" — bounded per row, unbounded in count and in lifetime.
**Why it matters.** After account erasure the user's address (`To`), the email body (magic-link URLs, invitation copy) and now arbitrary tenant documents ("a generated PDF report" is the stated use case) survive indefinitely in a table outside every erasure path — a per-user AND per-tenant erasure-completeness gap (R12/R13 spirit; R43's tenant-axis canary is scoped to `TenantId`-carrying entities, and email rows never get a `TenantId`, so the canary cannot reach them). Storage: every attachment mail leaves ~13 MiB of text in Postgres forever; on Neon free tier that is a few hundred mails to the quota. The dispatcher `SELECT *`s the whole row on each of up to 5 attempts.
**Pre-existing portion.** Bodies/addresses were already retained (not filed in v3 — grep of the v3 reports finds no outbox-retention finding); #235 changes the data class from "a link" to "a tenant document", which is why it is filed now.
**Fix (one line).** Scrub `Payload` to `""` when flipping to `Sent`, add a scheduled purge of `Sent`/`Dead` rows older than N days, stamp `TenantId` on email enqueues and wipe pending rows in a dissolve contributor.
**Gate.** New test in `tests/Api.Tests/Outbox/OutboxProcessorTests` (payload empty after success); a scheduled-job test; extend `EveryTenantScopedEntity_IsWiredIntoDissolve`-style canary to assert `OutboxMessages WHERE TenantId = dissolved` is empty after `DissolveAsync`.
**Confidence.** Certain (code facts); Likely (privacy impact magnitude depends on downstream usage).

### JOBS-1 · Medium · `src/Infrastructure/Webhooks/WebhookOutboxHandler.cs:51,73`, `src/Api/Services/WebhookService.cs:126,140`, `src/Api/Endpoints/WebhookEndpoints.cs:118`
**What.** Raw exception text is persisted into `WebhookDelivery.Error` and returned verbatim to the tenant.
**Evidence.**
- Handler: `transportError = ex.Message; // network/timeout — no HTTP status` (l.51) → `Error = success ? null : transportError ?? $"HTTP {status}",` (l.73), now durably written via `auditDb` (l.83-86).
- Service (send-test): `transportError = ex.Message;` (l.126) → `Error = … transportError … // kept server-side; never a secret` (l.140).
- Endpoint: `WebhookDeliveryResponse.From` → `Error = d.Error,` (l.118), served by `GET /api/webhooks/{id}/deliveries` to the owner.
- The code's own comments assert the opposite: `WebhookEndpoints.cs:56` "Don't leak internal DNS/connection detail to the tenant (GAP-3) — it stays in the delivery row." — the delivery row is the tenant-readable surface.
**Rule.** v2 **R16 [review]** — "Client-facing error bodies never contain raw exception text (`ex.Message`); detail stays in server logs. *(GAP-3)*". Before #194 the failure row was discarded by the processor rollback, so the leak was latent; #194 (async path) and the send-test change (sync path) make it live. Content leaked: `HttpRequestException`/`SocketException` messages (resolved IP + port, "No such host is known", the 10 s timeout text), and `"Refusing to send a webhook to a disallowed URL."` (reveals the SSRF guard verdict, i.e. that the tenant's hostname resolved to a private range on the server's resolver).
**Fix (one line).** Store an enumerated reason (`transport_error` / `timeout` / `url_refused` / `http_{status}`) in `Error`; keep `ex.Message` in the processor's `LastError`/server log only.
**Gate.** `tests/Api.Tests/Webhooks/WebhookDeliveryLogTests.cs` — assert `delivery.Error` ∈ the enumerated set on the `ThrowingHandler` cases; an `EnforcementGateTests` string-scan that no entity mapped by a `*Response.From` is assigned from `.Message`.
**Confidence.** Certain.

### JOBS-3 · Medium · `src/Core/Abstractions/IEmailSender.cs:46-67` vs `src/Infrastructure/Email/SmtpEmailSender.cs:74`
**What.** The pre-enqueue guard only checks `IsNullOrWhiteSpace(a.MediaType)` (l.57); dispatch calls `ContentType.Parse(attachment.MediaType)` (l.74), which throws on anything that is not a well-formed `type/subtype`.
**Evidence (runtime, MailKit/MimeKit 4.17.0, scratch probe).**
```
Parse('pdf'):                        THROWS ParseException: Expected '/' at position 3
Parse('application/pdf; charset='):  THROWS ParseException: Incomplete parameter at offset 17
Parse('a b/c'):                      THROWS ParseException: Expected '/' at position 2
Parse("text/plain\r\nX-Evil: 1"):    THROWS ParseException: Expected ';' at position 12
```
So `SendAsync(…, attachments: [new("r.pdf", bytes, "pdf")])` passes `Validate`, is enqueued, and every dispatch throws `ParseException` inside `BuildMessage` → 5 attempts at 10/20/40/80 s → dead-lettered with `LastError = "Expected '/' at position 3"`. ADR-007 amendment: "checked … **before enqueueing** … (an oversize mail would otherwise sit in the outbox failing until it dead-letters)" — the same loop exists for the media type. `EmailInlineImage.MediaType` is likewise unparsed (pre-existing; platform-supplied constants, low risk).
**Why it matters.** A downstream caller passing a file extension or a `Content-Type` header copied with parameters burns the retry budget and produces a dead letter with no operator-visible cause at call time; the slice's stated guarantee is false for one of the three validated fields.
**Fix (one line).** In `EmailAttachment.Validate`, require a strict RFC 2045 `token/token` (regex; Core cannot reference MimeKit) — or move the guard to Infrastructure and use `ContentType.TryParse`.
**Gate.** `tests/Core.Tests/EmailAttachmentTests.cs` theory with `"pdf"`, `"application/pdf; charset="`, `"a b/c"`, `"text/plain\r\nX: 1"`; `OutboxEmailTests` asserting zero rows.
**Confidence.** Certain (verified against the pinned MimeKit version).

### JOBS-9 · Medium · `src/Infrastructure/ServiceCollectionExtensions.cs:87`, `src/Infrastructure/Webhooks/WebhookSender.cs:24-36`
**What.** The webhook `HttpClient` is registered with only a timeout — `services.AddHttpClient<IWebhookSender, WebhookSender>(c => c.Timeout = TimeSpan.FromSeconds(10));` — so `SocketsHttpHandler.AllowAutoRedirect` stays at its default (`true`). `WebhookSender.SendAsync` re-runs `IOutboundUrlGuard` on the *configured* URL only (l.24-25); a public https endpoint that answers `302 Location: http://169.254.169.254/…` or `https://10.0.0.5/…` is followed by the handler, with the signed body and headers, and the redirect target is never guarded.
**Delta relation.** Line 87 is unchanged in the delta (pre-existing), but the delta adds a second production caller through the same client (`WebhookSubscriptionService.SendTestAsync`, request-scoped, owner-triggered on demand) and the replay path re-enters it. Not adjudicated in v2/v3 (grep: no "redirect" finding). Filed because the SSRF machine half (R76: "outbound-to-user-URL requests route through `IOutboundUrlGuard`") is satisfied in letter but bypassed by the transport.
**Fix (one line).** `.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })` on the registration (the guard already forces https outside Development, so a legitimate http→https redirect cannot occur).
**Gate.** `tests/Api.Tests/Webhooks/WebhookDeliveryTests.cs` — stub returning 302 with an internal `Location`: assert the sender returns 302 and never issues a second request; `ArchitectureTests` string-scan that the `IWebhookSender` client sets `AllowAutoRedirect = false`.
**Confidence.** Likely (default documented; not exercised against a live redirect here).

### OBS-1 · Medium · `src/Api/Observability/TelemetryExtensions.cs:60-69` + eight `Log*` call sites
**What.** #212 adds `.WithLogging(logging => ApplyExporter(…))` (l.60) and `IncludeFormattedMessage = true; IncludeScopes = true; ParseStateValues = true;` (l.66-68). With `ParseStateValues`, every message-template placeholder becomes a named attribute on the exported record. The codebase logs user email addresses as state in eight places:
```
src/Api/Controllers/AuthController.cs:126        "OAuth callback successful for user: {Email} via {Provider}"
src/Api/Controllers/NativeAuthController.cs:95   "Native OAuth callback successful for {Email} via {Provider}"
src/Api/Services/JwtTokenService.cs:76           "JWT issued for user {Email} (id: {UserId})"     ← every token issue/refresh
src/Api/Services/TenantInvitationService.cs:282  "Failed to send invitation email to {Email}"
src/Api/Services/UserService.cs:86,96,110,135,165  "{Email}" on lookup / merge refusal / link / create
```
No redaction/scrubbing processor exists (`grep -rn -i "redact\|scrub" src` → none). `docs/stories/observability.md:40` claims "**No secrets/PII** beyond identifiers in log state"; that was already untrue for the console, but until #212 those lines stayed on the host's stdout (Render). Now they ship — with `tenant_id`/`user_id` scope and trace ids — to whatever `OpenTelemetry__Otlp__Endpoint` names, which per `DEPLOYMENT.md:269` and `.env.example` is Grafana Cloud with an `Authorization: Basic` header.
**Why it matters.** Email is personal data; exporting it as an indexed attribute to a third-party log store on every JWT issue creates a processor relationship and a retention surface that no doc, ADR or QA case mentions; the claim in the story is now falsified by a machine-checkable fact.
**Secondary.** `OutboxProcessor.RecordFailedAttemptAsync` logs the full exception; an `EmailSendException`'s inner MailKit exception can carry the SMTP reply (`550 5.1.1 <user@x>: Recipient address rejected`) — same channel.
**Fix (one line).** Replace `{Email}` with `{UserId}` (or a stable hash) in those eight lines; state the exported-log data class in `DEPLOYMENT.md` § observability.
**Gate.** New `EnforcementGateTests.LogTemplates_NeverCarryEmailAddresses`: scan `Log(Information|Warning|Error|Debug|Trace)\(` templates in `src/**` for `{Email}|{To}|{Token}|{Code}|{Secret}|{Password}|{Body}` placeholders (allowlist by comment).
**Confidence.** Certain (code); vendor exposure Likely (depends on the operator setting the endpoint, which the docs instruct).

### JOBS-4 · Low · `src/Infrastructure/Email/SmtpEmailSender.cs:74`, `src/Core/Abstractions/IEmailSender.cs:55`
**What.** `FileName` is only checked for blankness; it is passed verbatim to `builder.Attachments.Add(attachment.FileName, …)`.
**Evidence (runtime probe, MimeKit 4.17.0, Windows host).**
```
name=[../../etc/passwd] -> FileName=[../../etc/passwd]
   Content-Disposition: attachment; filename="../../etc/passwd"
name=[a\r\nContent-Type: text/html\r\n\r\n<b>x</b>.pdf] -> Content-Disposition: attachment; filename*0*=iso-8859-1''a%0D%0AContent-Type%3A%20text%2Fhtml%0D%0A…
name=[C:\Windows\x.pdf] -> FileName=[x.pdf]
```
So: path traversal segments pass through unchanged into the MIME `filename` parameter; CRLF is neutralised by MimeKit's RFC 2231 encoding (header injection is **not** possible — verified); the Windows drive path was reduced on this Windows host, which will not happen on the Linux runtime where `\` is not a separator.
**Why it matters.** Mail clients generally sanitise on save, so exploitation is receiver-dependent (Low); but the slice advertises "validated before enqueue" and a downstream app that builds the name from user input (a note title) gets no help.
**Fix (one line).** In `Validate`, reject control chars and reduce to `Path.GetFileName` after replacing `\` with `/`; cap length (e.g. 255).
**Gate.** `tests/Core.Tests/EmailAttachmentTests.cs`.
**Confidence.** Certain for the traversal pass-through and CRLF encoding; Likely for the Linux backslash case.

### JOBS-5 · Low · `src/Infrastructure/Webhooks/WebhookOutboxHandler.cs:73`, `src/Api/Services/WebhookService.cs:140`, `src/Infrastructure/Persistence/Configurations/WebhookDeliveryConfiguration.cs:14`
**What.** `Error` is `HasMaxLength(1000)` but neither write site truncates `ex.Message`; contrast `OutboxProcessor.cs:122`: `message.LastError = Truncate(cause.Message, 1000);`.
**Why it matters.** A >1000-char message (nested `HttpRequestException` → `SocketException` chains, or a future exception with the response body) makes `auditDb.SaveChangesAsync` fail with `DbUpdateException` (22001) — the failure row is lost again (the exact blindness #194 fixed) and the processor records the wrong cause; on the sync path the owner gets a 500 instead of `delivered:false`.
**Fix (one line).** Truncate to 1000 at both write sites (or share `OutboxProcessor.Truncate`).
**Gate.** `WebhookDeliveryLogTests` with a `ThrowingHandler` whose message is 1100 chars.
**Confidence.** Likely (needs a long message; not observed).

### JOBS-6 · Low · `src/Api/Services/WebhookService.cs:124-127`, `src/Infrastructure/Webhooks/WebhookOutboxHandler.cs:49-52`
**What.** `catch (Exception ex)` also catches `OperationCanceledException`. On host shutdown (dispatcher `stoppingToken`) or a client abort (send-test `ct`) the cancellation is recorded as a failed delivery row with `Error = "The operation was canceled."`, and on the async path an attempt is burned and the message backs off. `SmtpEmailSender.cs:38-41` gets this right (`catch (OperationCanceledException) { throw; }`); the delta copies the webhook pattern into a second place.
**Fix (one line).** Add `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` before the generic catch in both.
**Gate.** Unit test with a pre-cancelled token → no `WebhookDelivery` row, `OperationCanceledException` propagates.
**Confidence.** Certain (code); low impact.

### JOBS-7 · Low · `src/Api/Endpoints/WebhookEndpoints.cs:44-58,64-66`, `src/Api/Configuration/RateLimiting.cs`
**What.** `POST /{id}/test` and `POST /deliveries/{id}/replay` have no rate-limit policy (`RateLimiting.cs` defines only Passwordless/PasswordlessVerify/PublicApi; `grep RequireRateLimiting|EnableRateLimiting` finds none on webhooks). The delta makes every test-send also `INSERT` a `WebhookDelivery` row with the body, so an owner can grow the table and drive outbound POSTs to a third-party URL at API speed (the platform as a signed-POST amplifier). `hooks.md` lists per-subscription rate limiting as out of scope (known), so this is filed only for the new persistence cost.
**Fix (one line).** Apply a per-tenant fixed-window policy to the two POSTs (e.g. 10/min).
**Gate.** `EnforcementGateTests`: every `MapPost` under `/api/webhooks` that triggers an outbound call carries a policy.
**Confidence.** Certain.

### OBS-3 · Low · `src/Api/Observability/TelemetryExtensions.cs:32`, `tests/Api.Tests/DocAndConfigSyncTests.cs:54,65`
**What.** `var otlpProtocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];` is a flat, colon-less indexer read. The config-catalog gate's regexes capture dotted literals only (`access`: `…(?::[A-Za-z0-9]+)+`) and raw `Environment.GetEnvironmentVariable(...)` reads (`envRead`); this read shape is invisible to `ConfigKeys_ReadInCode_AreDocumented`. The key *is* documented in `.env.example` today (R75 satisfied by discipline, not by the gate).
**Second half.** The app decides whether to append `/v1/{signal}` from `IConfiguration`, while the SDK decides the wire protocol from the *process environment* (`OtlpExporterOptions` reads `OTEL_EXPORTER_OTLP_PROTOCOL` itself; the code never sets `o.Protocol`). Under DotNetEnv (`Env.Load()` sets process env vars) and Render they agree; a value placed in `appsettings*.json`/user-secrets makes the app append the http path while the SDK still speaks gRPC to it — the same silent 404 the fix was for, from the other side.
**Fix (one line).** Set `o.Protocol = OtlpExportProtocol.HttpProtobuf` from the same `otlpProtocol` value so one source drives both; extend the gate with a flat `configuration\["([A-Z][A-Z0-9_]+)"\]` alternative.
**Gate.** `DocAndConfigSyncTests` (regex extension); `TelemetryLogsExportTests` asserting the resolved `OtlpExporterOptions.Protocol`.
**R53 posture.** Verified closed under empty config: no endpoint ⇒ no exporter (`WithoutAnEndpoint_TheAppStillBoots_AndLogsStillWork`).
**Confidence.** Certain.

### OBS-2 · Low · `src/Api/Observability/TelemetryExtensions.cs:90-113` (exporter wiring), SDK defaults (OpenTelemetry 1.16.0)
**What happens when the collector is unreachable.** Startup cannot fail on it (the exporter only builds options; connection is lazy — Certain from code). Steady state per SDK defaults: traces and logs go through `Batch*ExportProcessor` (MaxQueueSize 2048, drop-on-overflow, 10 s export timeout, 5 s schedule) and metrics through `PeriodicExportingMetricReader` (60 s) — memory is bounded (~2×2048 records + one metrics snapshot) and records are dropped silently; the OTLP exporter's in-memory retry is opt-in (`OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY`), so a transient outage loses data rather than growing memory. Failed exports are reported only through the SDK's `EventSource`, not `ILogger`, so the operator sees nothing in the app log or in Grafana — the exact "silence" #208 fixed for the path case remains for the network case. Not run here → **Suspected/Likely**; no test covers it (`TelemetryLogsExportTests` checks registration only, and nothing asserts the per-signal `o.Endpoint` wiring — a regression to `new Uri(otlpEndpoint)` would keep `OtlpEndpointsTests` green).
**Fix (one line).** Document the drop semantics in `DEPLOYMENT.md` §observability and enable SDK self-diagnostics (`OTEL_DIAGNOSTICS.json`) or a `/health` contributor that reports the last export failure; add a test resolving the named `OtlpExporterOptions` per signal.
**Confidence.** Likely (SDK-default reasoning); Suspected for exact queue numbers on 1.16.0.

### JOBS-8 · Info · `IEmailSender.SendAsync` "pass `cancellationToken` by name" (JOBS-4)
Not a gate gap: a positional token no longer compiles (no implicit conversion `CancellationToken` → `IReadOnlyList<EmailAttachment>?`), which the ADR-007 amendment states correctly. All four production call sites verified named (`AuthController.cs:276,327`, `NotificationService.cs:101`, `TenantInvitationService.cs:276`). The `NotificationService` delta is exactly this one-token rename; **R46 holds** (`forceAll = NotificationKinds.IsSecurity(kind)` still forces both channels regardless of prefs, l.73-98) and **R55** is untouched (it governs the admin bulk selectors, not this service). `NotificationFanOutTests` delta only adapts the fake's signature. **R57**: the larger payload changes nothing in `OutboxProcessor` (bookkeeping still runs in its own transaction on any failure; `FailedDelivery_SurvivesTheProcessorRollback_AndRetries` + `DeadLetteredDelivery_KeepsOneRowPerAttempt` re-prove it end-to-end for webhooks). Inbox dedup (`IInbox`) is not used on the email path by design (at-least-once accepted, `EmailOutboxHandler` docstring).

### JOBS-10 · Info · `src/Infrastructure/ServiceCollectionExtensions.cs:45-51` (`AddDbContextFactory<AppDbContext>(…, ServiceLifetime.Scoped)` after `AddDbContext`)
Both registrations `TryAdd` `DbContextOptions<AppDbContext>` as Scoped, so the pair is consistent and the scoped factory resolves the dispatcher scope's tenant-less `ICurrentTenant` (system context; `WebhookDelivery` has no RLS policy anyway). The DI wiring itself is untested: `WebhookDeliveryLogTests` hand-roll `FixtureContextFactory`; in production a resolution failure would surface as a per-pass `OutboxDispatcher` error log, not a boot failure. Suggest one `ServiceHarness` assertion that `IDbContextFactory<AppDbContext>` resolves inside a scope. Suspected (not run).

### OBS-4 · Info · docs currency
`docs/stories/observability.md:76-77` package list omits `OpenTelemetry.Instrumentation.Runtime` (added by #212); its 2026-09-05 fix paragraph names `/v1/traces` / `/v1/metrics` only (code also appends `/v1/logs`); `DEPLOYMENT.md:269` is current. No QA case exercises OTLP/logs export (operator-only; acceptable) and none covers JOBS-4 (internal seam; acceptable, no Postman impact — confirmed the send-test response shape is unchanged so `PostmanParityTests` is unaffected). `async-jobs.md` JOBS-4 "Out of scope: per-type allow-lists" is honest but JOBS-3 shows the *format* check (not the allow-list) is what is missing.

## Template-readiness notes
- **New burden on slices (JOBS-3/4).** A downstream feature that emails a document must itself guarantee a parseable media type and a safe file name; the platform guard only checks blankness. Fixing R84j in the platform removes the burden.
- **New platform seam (HOOKS-2 fix).** "Out-of-band write through `IDbContextFactory<AppDbContext>` when a handler must persist across the processor rollback" is a reusable pattern documented only in `hooks.md` and a code comment; `ARCHITECTURE.md`/`FLOWS.md` outbox sequence should show the two commit paths, or the next handler author will stage the failure row on the shared context again (the 2026-08-24 bug class).
- **Doc-only good practice (OBS-1).** "identifiers only in log state" is asserted in `observability.md` and the scope middleware docstring but enforced nowhere; with export on, every downstream `LogInformation("… {Email}")` becomes vendor-side PII. Needs the R85j gate before the template is cloned again.
- **No new core→slice dependency.** `EmailAttachment` lives in Core; `Features/**` untouched.
- **Retention (JOBS-2)** is a template-level omission every clone inherits: no purge job, no dissolve hook for the outbox.

## Candidate rules
- **R81j-cand — [machine]** — Persisted diagnostics that any `*Response.From` maps to a client (`WebhookDelivery.Error`, future `*.Error/LastError`) hold an enumerated reason code, never `Exception.Message`. Enforcement: `EnforcementGateTests` string-scan `Error\s*=.*\.Message` in `src/**` against the set of entity types referenced by a `Response.From`, + `WebhookDeliveryLogTests` asserting the enumerated set. Subsumes JOBS-1 (machine half of R16).
- **R82j-cand — [machine]** — Outbox payload lifecycle: `Payload` is scrubbed when a row leaves `Pending`, and a registered `IScheduledJob` purges `Sent`/`Dead` rows older than a configured retention (`Outbox:RetentionDays`, documented in `.env.example`). Enforcement: `OutboxProcessor` test (payload empty after success/dead-letter) + scheduled-job test + `ScheduledJobs_Include("outbox-retention")`. Subsumes JOBS-2 (retention half).
- **R83j-cand — [machine]** — Every outbox enqueue that carries per-user or per-tenant content stamps `TenantId`, and tenant dissolve wipes that tenant's pending rows (an `OutboxDataContributor`). Enforcement: extend the R43 canary — after `DissolveAsync`, `OutboxMessages WHERE TenantId = t` is empty; arch scan that `EnqueueAsync(` callers in `Email/` and `Services/` pass a tenant id. Subsumes JOBS-2 (erasure half).
- **R84j-cand — [machine]** — Enqueue-time validation is at least as strict as dispatch-time parsing: every payload field a handler parses (`MediaType` → `ContentType.Parse`, `To` → `MailboxAddress.Parse`, `FileName` → MIME parameter) is validated with the same grammar before the row is written. Enforcement: `EmailAttachmentTests` + `OutboxEmailTests` malformed-value theories asserting zero rows. Subsumes JOBS-3, JOBS-4.
- **R85j-cand — [machine]** — Log state carries identifiers only: message-template placeholders `{Email}`, `{To}`, `{Token}`, `{Code}`, `{Otp}`, `{Secret}`, `{Password}`, `{Body}` are banned in `src/**` (comment allowlist). Enforcement: new `EnforcementGateTests.LogTemplates_NeverCarryPii`. Subsumes OBS-1.
- **R86j-cand — [machine]** — An `HttpClient` that targets user-supplied URLs sets `AllowAutoRedirect = false` (or re-guards each hop). Enforcement: `ArchitectureTests` scan of the `IWebhookSender` registration + a 302 stub test in `WebhookDeliveryTests`. Subsumes JOBS-9 (transport half of R3/R76).
- **R87j-cand — [machine]** — The config-catalog gate also captures flat indexer reads (`configuration["ALL_CAPS_NAME"]`) and the SDK-owned `OTEL_*` variables the code consults. Enforcement: regex extension in `DocAndConfigSyncTests`. Subsumes OBS-3 (gate half).
- **R88j-cand — [machine]** — Free-text diagnostics written to a `HasMaxLength` column are truncated at the write site. Enforcement: scan for assignments from `.Message` into properties configured with `HasMaxLength` without a `Truncate(`. Subsumes JOBS-5.

## RULE_CONFLICTS candidates
- None overturned. Observation: **R16 is [review]-only** and was violated by a slice whose comments cite GAP-3 by name in three places — evidence that the review half does not hold under maintenance; R81j is its machine half.

## Out-of-area observations
- `src/Api/Services/JwtTokenService.cs:76` logs `{Email}` on every token issue/refresh — auth area, but it is the highest-volume contributor to OBS-1.
- `src/Infrastructure/ServiceCollectionExtensions.cs:136-149` (GATES-1 fake-provider registration when `Billing:Enabled` is false) — billing area; not reviewed here.
- `tests/Ui.Tests/Infrastructure/Fakes.cs` and `tests/E2E.Tests` email fakes changed signature for JOBS-4 — UI/E2E area; compile-checked only.
- `docs/QA_TEST_PLAN.md:2169` QA-API-06 (delivery log + replay) should gain a step asserting `error` is a coded value once JOBS-1 is fixed.

## Unknowns needing a human decision
1. Retention window for `Sent`/`Dead` outbox rows and whether `To`/`Subject` should survive the payload scrub for support (JOBS-2).
2. Whether tenants should see any failure detail in the delivery log at all — coded reason vs `null` (JOBS-1); Stripe shows a coded reason.
3. Whether exported logs to a third-party collector need a data-processing note in `DEPLOYMENT.md` before the next downstream clone goes live (OBS-1).
4. Whether `AllowAutoRedirect = false` is acceptable for tenant endpoints (some receivers 308 to a canonical host); the guard already forces https, so http→https upgrades are moot (JOBS-9).
5. Whether the platform should adopt the storage-key design (attachments via `IFileStorage`, tenant-scoped key in the payload) now rather than "past the cap", since the retention/erasure problem is what the inline design created (JOBS-2).
