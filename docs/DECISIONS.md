# Decisions (ADRs)

> Every decision the user did not make explicitly. Newest last. Keep each one short.
> Format below. Status: accepted | superseded by ADR-xxx.

## ADR-001 — Single-operator content platform: no tenancy, no public registration
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: The starter assumes a multi-tenant SaaS (self-registration creates an organization). Kosova Radar is one public feed curated by one editorial team; the V1 PDF has users with roles but no organizations.
- Options: keep a single "Kosova Radar" tenant · drop tenancy entirely.
- Decision: Drop tenancy. No entity implements `ITenantOwned`; public read endpoints are anonymous; admin endpoints use roles `Admin`/`Reviewer`. The first Admin is created from bootstrap env vars; Admins create further users. Platform REQs P01/P03/P04/P10 rewritten accordingly (P01 bootstrap, P03 admin endpoints, P04 role check → 403).
- Consequences: Simpler model and queries. If V4 adds merchant accounts, tenancy is introduced then for merchant-owned data only.

## ADR-002 — PostgreSQL instead of SQL Server
- Date: 2026-10-03 · Status: accepted (human may override before approval) · Task: spec
- Context: The V1 PDF says "Use SQL Server with EF Core migrations". The factory's fixed tooling (locked acceptance `AppFactory` with Testcontainers PostgreSQL, `docker-compose` db, Playwright servers, verify.sh) is built on PostgreSQL; the project rules allow stack deviations only when a requirement cannot be met.
- Options: SQL Server (needs human changes to scripts/ and the acceptance harness; heavier container ≥ 2 GB RAM; licensing above Express limits) · PostgreSQL (default stack).
- Decision: PostgreSQL 17 via EF Core (Npgsql). No requirement depends on SQL Server features. Code stays provider-neutral (no raw SQL outside migrations), so a later switch is a provider + migrations change.
- Consequences: Cheaper hosting (fits a small VPS), full factory gates work. If the client insists on SQL Server, decide **before** approving the spec.

## ADR-003 — Project names `App.*` instead of `KosovaRadar.*`
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: The PDF suggests `KosovaRadar.Api` etc.; the factory's Dockerfile, CI and verify.sh depend on fixed `App.*` names and `web/`.
- Decision: Keep `App.*` and `web/`. Product name lives in docs and UI. Docs from PDF §3 (`docs/architecture`, `docs/api`, `docs/sources`) are covered by design.md and the release docs.

## ADR-004 — Background jobs with a `BackgroundService` scheduler, not Hangfire
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: PDF §7 lists six jobs (fetch, processing, verification, dedup, expiration, health) with retries, idempotency, structured logs; roadmap allows "Hangfire or similar". V1 runs one instance.
- Options: Hangfire (PostgreSQL storage, dashboard) · Quartz.NET · `BackgroundService` + `JobExecution` table.
- Decision: One scheduler `BackgroundService` that runs due source fetches (each run executes fetch → parse → normalize → AI → validate → dedup synchronously per source) plus an `ExpirationJob` timer; source health is derived on read. Idempotency comes from fingerprints/content hashes; retries from the HTTP resilience handler plus the next due run; history in `JobExecution`.
- Consequences: No extra dependency or dashboard to secure. Moving to Hangfire is needed when running multiple instances or fan-out queues (V2).

## ADR-005 — Every publish needs human approval in V1
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: Roadmap V1 §7: "initially keep human approval as default"; auto-publish belongs to V2.
- Decision: Validation outcome `Valid` only marks a candidate as ready; status stays `Pending` until a Reviewer/Admin approves. Confidence and AI metadata are stored so V2 thresholds can be calibrated on real data.

## ADR-006 — Generic adapters first; site-specific adapters only for cleared sources
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: PDF §6 asks for 2–3 concrete adapters on stable public pages, while §2 and roadmap §9 require compliance with each source's access rules. Which sites the client may use (permissions, ToS) is a business/legal decision we cannot make.
- Decision: Build `JsonFeed` (Radar feed format, also usable by partners) and `Rss` adapters — stable, standards-based, robots-compliant — plus a `Fixture` adapter for development/tests. An iCalendar adapter is deferred until the client names an iCal source (spec review: Kosovo venues mostly publish web pages or RSS). Site-specific HTML adapters are a later task that starts when the client names the sources and confirms permission (it will be recorded as a blocker in the build phase).
- Consequences: V1 can ingest real data from any source that publishes RSS or Radar JSON on day one; the PDF's launch gate "one production-like ingestion verified" depends on the client's source list; HTML scrapers are added per source with saved-fixture contract tests.

## ADR-007 — AI behind `IAiProvider`: Claude Haiku 4.5 in production, deterministic Fake in dev/test
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: PDF §8 requires a vendor-neutral abstraction, strict JSON, server-side validation, audit of model/request/confidence, cost limits; AI must never be the authority.
- Decision: Providers `Anthropic` (Messages API, model from config, default `claude-haiku-4-5-20251001` for cost), `Fake` (documented deterministic rules in design.md §6, Development/Testing only) and `None`. AI fills only null fields; explicit source values win; every call stored in `AiCall`; daily limit. Without an API key production runs with `None` and AI-dependent candidates go to `NeedsReview`.
- Consequences: Acceptance tests are deterministic; production works without AI; switching vendor is one class.

## ADR-008 — SEO for a SPA via server-side meta injection
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: Roadmap asks for SEO-friendly, shareable detail pages; the fixed stack is a Vite SPA (no SSR framework).
- Options: Next.js SSR (stack deviation) · prerendering service · API serves `index.html` with per-item `<title>`, Open Graph and JSON-LD.
- Decision: Meta injection for `/events/{id}` and `/deals/{id}`, plus `/sitemap.xml` and `/robots.txt` from the API.
- Consequences: Correct link previews and indexable metadata; body content still client-rendered (Google renders JS). Full SSR can be revisited if organic search becomes a key channel.

## ADR-009 — Albanian-only UI; Europe/Pristina presentation time
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: Audience is Kosovo; roadmap uses Albanian labels ("Sot, Evente, Oferta"). Multi-language is V5.
- Decision: All UI strings in Albanian, kept in one strings module per feature to ease later i18n. Store UTC, render Kosovo local time via the IANA zone `Europe/Belgrade` (there is no `Europe/Pristina` id).

## ADR-010 — Expiry and freshness rules
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: "Expired items must not appear as active" needs an end time, but many sources give only a start time; "never invent facts".
- Decision: Event effective end = `EndsAt` when present and ≥ `StartsAt`, else `StartsAt + 6 h` (internal only, never displayed). Deals expire at `ValidUntil`; deals without it drop out of lists after 7 days without re-verification. Freshness `fresh` ≤ 48 h since last verification, else `stale` with "Kontrolluar së fundi më …". Public queries filter by time so expiry is immediate; a 5-minute job also persists `Expired`.

## ADR-011 — Images are linked, not re-hosted
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: Re-hosting source images raises copyright and storage questions; PDF lists Media but no upload flow.
- Decision: Store only https `imageUrl` from the source; render lazily with `referrerpolicy="no-referrer"` and a neutral placeholder when absent. Media entity deferred.

## ADR-012 — Search with a normalized `SearchText` column
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: Users type "e" for "ë"; V1 volume is small (thousands of items).
- Options: PostgreSQL `unaccent` + full-text · external search engine · app-computed normalized column + `ILIKE` with a trigram index.
- Decision: App-computed `SearchText` (diacritics folded in C#, same function as dedup), queried with `ILIKE`, `pg_trgm` GIN index added if the load test needs it.

## ADR-013 — Tags, notifications and public accounts deferred
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: PDF domain lists Tag/Media and roadmap core domains include Notifications, but no V1 acceptance criterion needs them.
- Decision: Not built in V1 (see requirements §2 Out). Source failures surface on the admin dashboard instead of alerts.

## ADR-014 — Deterministic test hooks (`X-Test-Now`, `/api/_test/throw`)
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: Black-box acceptance tests must cover time rules (stale after 48 h, expiry, 7-day deal staleness) and the 500 path (REQ-P09), while Playwright runs parallel workers against a shared, persistent database.
- Options: (a) per-request `X-Test-Now` header + throw endpoint gated by config · (b) global settable clock endpoint · (c) tag time rules `[manual]`, cover them only with integration tests.
- Decision: (a). Each request carries its own time, so parallel tests never interfere; AC5/AC6 of the PDF stay automated acceptance tests. Gated by `TestHooks__Enabled` (Development/Testing only); startup fails if enabled in Production; the scheduler and expiration job always use the real clock.
- Consequences: A few lines in `IClock` and one middleware; an integration test proves the hooks are inert when disabled; security-reviewer checks it in T001.

## ADR-015 — Manual fetch waits for a running scheduled run
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: The scheduler can start a run of the same source just before an admin clicks "Merr tani"; a 409 would be a confusing race for users and tests.
- Decision: Manual fetch waits up to 30 s on the per-source lock (`SemaphoreSlim.WaitAsync(timeout)`), then runs; 409 only if still locked. Scheduled runs skip a locked source.

## ADR-016 — Login limiter counts failed attempts only
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: The starter's REQ-P06 counts all attempts; acceptance suites log in as the same admin from 127.0.0.1 many times per minute and would get 429s.
- Decision: Count failed attempts per (client IP, normalized email), 10 per minute. Brute-force protection is unchanged; legitimate repeated logins are not blocked.

## ADR-017 — Server-side sessions (in-memory ticket store); 8 h sliding cookie
- Date: 2026-10-03 · Status: accepted · Task: T001
- Context: REQ-P11 requires ending the session; clearing the browser cookie leaves replayed copies valid. Rotating the security stamp on logout would end all of the user's sessions, which breaks parallel Playwright workers sharing the bootstrap admin (reviewer findings).
- Options: stamp rotation on logout · `ITicketStore` in memory · `ITicketStore` in a database table.
- Decision: Cookie `SessionStore` = in-memory `ITicketStore`; logout removes only that session, replayed copies get 401, other sessions stay valid. Stamp rotation remains the mechanism for deactivation (REQ-003). Cookie lifetime 8 h sliding.
- Consequences: Memory grows by one entry per login (≤ 8 h each), fine for a small team. Sessions end on restart/redeploy (users sign in again) — acceptable for a single instance (ADR-004). Moving to multiple instances needs a database-backed store.

## ADR-018 — Per-email failed-login cap across all IPs
- Date: 2026-10-03 · Status: accepted · Task: T002
- Context: The REQ-P06 limiter is keyed on (client IP, email); an attacker rotating IPs could guess one admin password without limit (security-review finding).
- Options: Identity lockout · per-email cap across IPs · CAPTCHA.
- Decision: In addition to REQ-P06, more than 50 failed logins for one email from any IPs within an hour block logins for that email until the hour ends. In memory, real clock.
- Consequences: An attacker can deliberately lock an admin out for up to an hour; acceptable for V1 (other Admins can still sign in; restart clears it). Failed logins are logged as warnings for monitoring.

## ADR-019 — Per-IP failed-login cap; Data Protection keys unencrypted at rest
- Date: 2026-10-03 · Status: accepted · Task: T002
- Context: Every failed login costs a PBKDF2 hash (also for unknown emails, to avoid a timing oracle); one client rotating random emails was never throttled and could saturate the CPU (security-review finding). Separately, Data Protection keys sit unencrypted in PostgreSQL.
- Decision: A third window counts failed logins per client IP across all emails: more than `Login:MaxFailuresPerIpPerMinute` (Production 30, Development/Testing 1000 because all test traffic comes from one IP) → 429 with `Retry-After`. Keys stay unencrypted: cookies carry only a reference to a server-side session (ADR-017), so a leaked key cannot forge a session; revisit (ProtectKeysWithCertificate) when tokens or protected payloads are added.
- Consequences: Several users behind one NAT share the per-IP budget for failures only (successes never count). Database backups must be treated as sensitive (RUNBOOK, T043).

## ADR-020 — Punctuation separates words in normalized text
- Date: 2026-10-03 · Status: accepted · Task: T010
- Context: design.md §4 said "punctuation removed"; removing it would turn "Fushë-Kosovë" into "fushekosove", which no longer matches "Fushë Kosovë", and "Rock-Nata" into one word for title similarity.
- Decision: Punctuation is a word separator in `TextNormalizer.Fold` (used for city matching, duplicate similarity and search). design.md §4 updated accordingly.
- Consequences: Hyphenated and spaced spellings match; similarity works on the visible words.


## ADR-021 — Audit entries join the action's transaction; email snapshot; wall-clock time
- Date: 2026-10-03 · Status: accepted · Task: T011
- Context: `IAuditWriter` must record the acting user and `{ before, after }` for every audited action (REQ-072). `AuditEntryDto` needs `userEmail`, but Application cannot see the Identity user table; the `X-Test-Now` hook would otherwise shift `At` away from the real time of the action.
- Decision: `IAuditWriter.Record` only adds the entry to the DbContext, so the caller's own `SaveChangesAsync` commits action and audit together. The actor comes from `ICurrentUser` (claims of the request; throws without a user). `AuditLog` stores a `UserEmail` snapshot (design.md §4 updated). `At` uses `TimeProvider`, not `IClock`. `Id` is a UUIDv7, used only as a tie-breaker for equal `At` (ordered to the millisecond, not strictly monotonic). Password-like properties (any name containing "password") are dropped when serializing, whatever the caller passes. Shared `PagedResult<T>`/`Paging.Parse` implement the `Page<T>` contract for all lists.
- Consequences: Use cases must call `Record` before their single `SaveChangesAsync`. A use case with several saves must not audit between them. Entries keep the email of the time even if accounts change.

## ADR-022 — Password policy is length only; user create/deactivate are atomic with their audit entry
- Date: 2026-10-03 · Status: accepted · Task: T012
- Context: REQ-001 accepts any password of at least 12 characters, but Identity defaults also demand a digit, upper/lower case and a symbol, so a valid 12-character password could be refused.
- Decision: Identity keeps only `RequiredLength = 12` (applies to the bootstrap admin too); the API also caps passwords at 128 characters. `UserAdmin` (Infrastructure) creates user + role + audit entry in one transaction (the audit entry is saved by the `SaveChanges` inside `CreateAsync`); deactivation sets `IsActive = false` and rotates the security stamp in one save together with its audit entry. Deactivating an already inactive user is a silent 204 (no audit entry).
- Consequences: Weak-but-long passwords are accepted; operators are expected to use a password manager. No password change exists in V1 (design.md §3).

## ADR-023 — Source adapter = fetch + parse; run saved at the end; transitional lookup rows
- Date: 2026-10-04 · Status: accepted · Task: T016
- Context: design.md §2 listed `FetchAsync`/`ParseAsync`/`NormalizeAsync`, but every format parses straight into normalized candidates (`RadarFeedParser`, T015), so a separate normalize step would only wrap the same list. Rows 2-4 of the lookup table need published items and decisions that arrive with T028/T029.
- Decision: `ISourceAdapter` has `CanHandle`, `FetchAsync`, `ParseAsync` (returns `FeedParseResult`: normalized candidates + per-item failures; `ErrorCode` for an unusable payload); design.md §2 updated. A failed fetch throws `SourceFetchException(errorCode, message)`. `IngestionService` saves candidates, the run and the source bookkeeping in one `SaveChanges` at the end, after an initial save of the `Running` run; a crash marks the run `Failed`/`Interrupted` with a direct update. City/category slugs are resolved in the run (`ReferenceResolver`) after the content hash is computed. Until T028, an item whose fingerprint belongs to a candidate that is not open (person-rejected, published, merged) is counted `unchanged`; until T017 every new candidate is `Pending`/`NeedsReview` with no issues. A source no adapter can handle (JsonFeed/Rss until T023/T025, a fixture when fixtures are disallowed) fails the run with `Unreachable`. `JobExecution` times use the wall clock (like the audit log); `fetchedAt` and the source's `lastFetchedAt`/`lastSuccessAt` use `IClock`.
- Consequences: T023/T025 add adapters only; T028 replaces the "left alone" branch with rows 2-4; T017 adds validation after the store step. The run does not retry a lost `xmin` race on a candidate edited during the run (the run fails as `Interrupted`).


## ADR-024 — ExpirationJob only flips items with an end; undated stale deals are expired by reading
- Date: 2026-10-04 · Status: accepted · Task: T020
- Context: design.md §2 says the job expires "published items past their effective end". A deal without `validUntil` has no end; REQ-087 hides it after 7 days without verification, and it can become active again when re-verified.
- Decision: `ExpirationRunner` sets `Expired` only on events past their effective end and deals past `validUntil`. Undated stale deals stay `Published` and are hidden/reported `expired` by the time-based filters. Date query parameters accept years 1900-2200 (anything else is a 400 on that parameter); `page`/`pageSize` are bound as strings so non-numeric values are reported by name. The integration test host does not run `ExpirationJob` (tests call the runner directly).
- Consequences: no job row is written for runs that expire nothing.

## ADR-025 — The SSRF guard cannot be switched off; one deadline per fetch; options validated at startup
- Date: 2026-10-04 · Status: accepted · Task: T023
- Context: design.md §2 listed `Ingestion__BlockPrivateNetworks` (true everywhere), but nothing needs the guard off: tests use stub handlers and the REQ-020 acceptance tests expect it on.
- Decision: the guard in `GuardedConnect` is unconditional and the key is removed from design.md. Only global-unicast IPv6 (2000::/3, minus 6to4, Teredo and documentation) and non-reserved IPv4 are public. `SourceHttpFetcher` owns one deadline (`Ingestion__HttpTimeoutSeconds`) for retries and body reading; the resilience handler only retries (`Ingestion__HttpRetries`; 5xx, 408, 429, network errors, but never a blocked address, an unknown host or a TLS failure). Defaults 2 / 20 (Testing 0 / 5); a timeout below 1 s or negative retries stop the startup. The body is read to 5 MB of decoded bytes, honouring the declared charset (UTF-8 fallback); a corrupt compressed body is `MalformedPayload`. `Site__BaseUrl` is required outside Development/Testing (crawler User-Agent). RSS sources are fetched through the guard now; their parsing (T025) fails the run as `MalformedPayload` until then.
- Consequences: any port is allowed (admin-only URLs); cookies are off. Connect tries addresses one by one without a per-address timeout, so a black-holed address eats the shared deadline.

## ADR-026 — robots.txt and per-host spacing live in the `sources` client pipeline
- Date: 2026-10-04 · Status: accepted · Task: T024
- Context: requirements §6 demand robots.txt compliance and one request per host every 10 s; design.md §6 describes the robots outcomes.
- Decision: `RobotsChecker` (called by the HTTP adapters before the feed fetch) fetches `{origin}/robots.txt` through the same `SourceHttpFetcher` and caches the parsed rules per origin for 24 h (`RobotsCache`, answers only, never failures). `RobotsRules` follows RFC 9309: our product token `KosovaRadarBot` group else `*`, longest pattern wins, Allow wins ties, `*` and `$`. 2xx parses, other 4xx allows all, 5xx/429/unresolved 3xx → `RobotsDisallowed` (429 added to the design's "5xx": it means "come back later"), network failures keep `Unreachable`/`Timeout`/`BlockedAddress`. `HostThrottle` + `HostThrottleHandler` (innermost handler of the client, so retries are spaced as well) reserve one slot per host authority; the handler reports its wait to the fetcher, which extends its deadline so the politeness delay never causes a `Timeout`. `Ingestion__PerHostDelaySeconds` (10; Testing 0) is validated at startup (≥ 0). Security review: parsing is bounded (first 500 KiB, lines ≤ 4 KB, ≤ 2000 rules per group, one flag per group instead of lists so repeated `User-agent` lines cannot multiply rules); rules and URL are compared percent-decoded; a robots.txt that is too large or undecodable is `RobotsDisallowed`; a request whose queue for the host is already longer than 3 delays fails with `Timeout` instead of waiting (nothing reserved).
- Consequences: a first fetch of a host takes `PerHostDelaySeconds` longer (robots, then the feed); a manual fetch in Development takes ~10 s. Redirects to another host are neither throttled nor robots-checked (only the configured URL is). The delay is in memory, so it restarts with the process; slots are per process.

## ADR-027 — RSS/Atom: evidence is re-serialized XML; the prolog encoding is not honoured; relative links fail the item
- Date: 2026-10-04 · Status: accepted · Task: T025
- Context: `SourceHttpFetcher` decodes the body to a string by the HTTP charset (UTF-8 fallback) before `RssFeedParser` sees it.
- Decision: evidence is `XElement.ToString()` of the item (outer XML, not the original bytes); items whose link is not absolute http(s) fail `InvalidField:url`; entity-decoded NUL/lone surrogates fail the item (`InvalidField:title|text`); text is capped at 20,000 chars; regexes have a 200 ms timeout (item fails `InvalidField:text`); depth > 32 → `MalformedPayload`; items beyond 1000 → one `TooManyItems` failure.
- Consequences: a feed that declares a non-UTF-8 encoding only in its XML prolog and sends no charset header shows mojibake in non-ASCII letters. Fix later by passing bytes to the parser if a named source needs it (also needs the code-pages provider for windows-1252).

## ADR-028 — AI step: fill-only-null with server-side validation; failures never block ingestion
- Date: 2026-10-04 · Status: accepted · Task: T026
- Context: REQ-040..043 and design.md §6 define the AI contract; open points were what counts as an invalid answer, what "enabled" means, where the call ledger is saved and what a reviewer edit does with AI-origin issues.
- Decision: `AiEnricher` (Application) runs for items that are stored or updated in place (never for unchanged ones) and has text. Order per item: resolve slugs → AI → rules validation → AI issues. `AiOutputValidator` rejects the whole answer (`InvalidOutput`, issue `AiUnavailable`) for: confidence outside 0..1/NaN, a `categorySlug` that does not exist for the kind (design: "slugs exist"), prices < 0 or > 1,000,000, currency not `^[A-Z]{3}$`, dates outside 1900..2199, text > 300 chars or with control characters. `AiFieldMerger` fills null fields only (events: startsAt, endsAt, city, venue, priceText, priceAmount; deals: merchant, priceText, oldPrice, newPrice, validFrom, validUntil; both: city, currency) and reports `ConflictingData` per field for instants, resolved city slugs and decimal amounts only; free text is never compared. The currency is taken from the AI only if no value conflicted. A city the AI names but nobody knows is stored as `cityText` without slug (then `UnknownCity`). Failure → `AiUnavailable`, fields untouched, `ai` null; kinds: provider exception (`ProviderError`), `Ai__TimeoutSeconds` (15) (`Timeout`), schema violation (`InvalidOutput`), daily limit (no call, no `AiCall` row). Without an enabled provider (`None`) and with text, `AiUnavailable` is added only when `MissingStartsAt`/`UnknownCity`/`MissingPrice` shows a gap the AI could have filled. `Ai__Provider=Anthropic` stops startup until T027; `Fake` outside Development/Testing stops startup. The daily count is read once per run (UTC day, wall clock) and incremented locally; Each `AiCall` row is saved at once through `IAiLedger` (own scope and context, failures logged only), so a run that crashes later still has its calls counted (review: a cost cap must not depend on the run succeeding); the AI answer is also rejected for lone surrogates/NUL (they would crash the save), and the provider call has a hard `WaitAsync` deadline besides the cooperative token. `UnknownCity` triggers the "no provider" `AiUnavailable` only when the source gave no city text. An in-place update replaces the candidate's AI data and suggested category with the new result (null when the new content has no AI output). A reviewer edit re-runs the rules only: AI-origin issues are not carried over, otherwise nothing a person does could clear `AiUnavailable`.
- Consequences: the daily limit is approximate under concurrent runs (each run starts from the stored count); a run has no cap on AI calls of its own (up to 1000 items × `Ai__TimeoutSeconds` while holding the source lock; revisit with T027 if needed); the `ai.suggestedFields` list is per last processing; T027 only has to implement `IAiProvider` and register it in `AddAi`.

## ADR-029 — Anthropic provider over plain HTTP with a JSON-schema structured output
- Date: 2026-10-04 · Status: accepted · Task: T027
- Context: T027 needs the production `IAiProvider`. The task's DoD asks for unit tests with a stubbed HTTP handler, and design.md §6 fixes the contract (structured output, "unknown = null", server-side validation).
- Decision: `AnthropicAiProvider` (Infrastructure) posts to `https://api.anthropic.com/v1/messages` through `IHttpClientFactory` (client `anthropic`: fixed host, `AllowAutoRedirect=false` so the key header cannot follow a redirect, `UseProxy=false`, no cookies, no retries: a retry is a second paid call and the enricher already owns timeout and daily limit). Plain `HttpClient` instead of the Anthropic NuGet SDK keeps the dependency list short and the handler stub-able. Request: `model` from `Ai__Model`, `max_tokens` 1024, a system prompt that states the text is untrusted data and "unknown = null", the feed text only inside `<item_text>` in the user turn, and `output_config.format = json_schema` per kind (event or deal fields; every field required but nullable; `categorySlug` is an enum of the kind's slugs plus null). No `thinking`, no sampling parameters (not valid on every model). The key is sent per request in `x-api-key`, never logged. Response handling: non-200 → `HttpRequestException` with the status number only (`ProviderError`); `stop_reason` other than `end_turn`/`stop_sequence` (refusal, `max_tokens`), no text block, bad JSON, a wrong-typed field, a non-date string in a date field, or a response over 1 MB → `AiInvalidOutputException` with constant messages (never the answer, the text or the body). A date without a UTC offset is treated as not stated (parsing it would assume the server's time zone); ranges, lengths, slugs and confidence stay with `AiOutputValidator` (REQ-041). Usage tokens and the message id go to `AiCall`. `Ai__Provider=Anthropic` without `Ai__ApiKey` stops startup (explicit misconfiguration); an empty provider without a key still means `None`.
- Consequences: a 429 or 5xx fails that item's AI step (`AiUnavailable`) and is not retried within the run; the next fetch of the item tries again. The live smoke test (`LiveAnthropicFact`) runs only with `Ai__ApiKey` in the environment; without it, it is recorded in BLOCKERS.md (B-001). The old default model id in design.md/.env.example stays configurable via `Ai__Model`.
- Review follow-up (T027): the schema uses `anyOf` for nullable fields and the category (type arrays are not documented for the structured-output subset); the feed text cannot close its `<item_text>` delimiter (`</item_text` is neutralised); dates are parsed as strict ISO 8601; the API key is trimmed and refused with control characters. Not done on purpose: no per-run circuit breaker after 401/402/403/429 — failed calls count against `Ai__DailyCallLimit` (500) and cost nothing, so the exposure is bounded; revisit if the live test or production shows runs holding the source lock with doomed calls. The limit counts calls, not tokens: a larger `Ai__Model` multiplies the bill.

## ADR-030 — Duplicate check, published-item lookup and approval of changed items
- Date: 2026-10-04 · Status: accepted · Task: T028
- Context: REQ-050..053 and 068 need lookup rows 2-4, the duplicate rules of design.md §4 and an approval that updates an existing item.
- Decision: rows 3-4 find the item through `ItemSource.Fingerprint`; "same content" means the same `ContentHash` as the latest Published/Merged candidate of that fingerprint. A kind change (fingerprint ignores the kind) is treated as a new, unlinked item. `lastVerifiedAt` is set by a targeted `ExecuteUpdate` after the loop, so a concurrent reviewer edit cannot fail the run. `DuplicateDetector` (Application) runs after validation (skipped once rejected), against published events/deals in SQL and one snapshot of pending candidates per run (Fields is jsonb, compared in memory); it is also re-run on a reviewer edit together with `ChangedSinceApproval`, so edits do not drop those issues. Approving a candidate with `matchedItemId` overwrites all item fields from the candidate (nulls included: the reviewer saw them), keeps id, `PrimarySourceId`, `Fingerprint`, `PublishedAt`, revives an expired item, refuses a withdrawn or missing one (400), and updates the existing `ItemSource` row instead of adding one.
- Consequences: the duplicate flag is one-directional (only the later candidate) and not cleared when the referenced item changes; per-item cost is one query plus a scan of pending candidates (fine at current size). Row 1 order means a feed that reverts to person-rejected content while a changed candidate is pending updates that candidate in place.

## ADR-031 — Review UI: source options endpoint for reviewers, read-only fields until T037
- Date: 2026-10-04 · Status: accepted · Task: T036
- Context: the queue's "Burimi" filter needs source names, but `/api/admin/sources` is Admin-only and reviewers use the queue (REQ-112 runs as a reviewer).
- Decision: new `GET /api/admin/candidates/sources` (Reviewer policy) returns `{ id, name }[]`. The candidate page renders the normalized fields inside a disabled `fieldset` until T037 adds saving (otherwise "Aprovo" could publish values different from what the form shows); "Ruaj ndryshimet" and "Bashko" are disabled for the same reason. The "Çmimi" input (`priceAmount`) is shown for events only: deals carry `oldPrice`/`newPrice`, and `priceText` is shown for both.
- Consequences: T037 must enable the fieldset and the two buttons.

## ADR-032 — Published items UI: "Ndrysho" is a link
- Date: 2026-10-04 · Status: accepted · Task: T038
- Context: design.md §7 listed "Ndrysho" among the row buttons, but it only navigates to the edit page.
- Decision: "Ndrysho" is a link (role `link`); "Skado" and "Tërhiq" stay buttons. design.md §7 updated. "Tërhiq" acts on one click like the API (idempotent, no confirmation step); revisit if mis-taps are reported.
- Consequences: tests address "Ndrysho" as a link.

## ADR-033 — Synthetic fixtures: no duplicate-of-published / changed-time cases
- Date: 2026-10-05 · Status: accepted · Task: T041 (review of T040)
- Context: design.md §9 listed "duplicate title" and "changed time" among the 24 fixture items, but duplicates are detected against published items or other sources, and change detection needs a published item; a single inactive fixture source seeded into an empty DB cannot produce either.
- Decision: the fixture keeps two same-title items in different cities instead; design.md §9 updated. Both cases remain covered by DuplicateAndChangeTests.
- Consequences: dev data does not demo `PossibleDuplicate`/`ChangedSinceApproval`; approve some fixtures and re-fetch to see them.

## ADR-034 — Gemini is the default AI provider (supersedes the provider choice of ADR-007)
- Date: 2026-10-08 · Status: accepted · Task: spec amendment v2 (T046)
- Context: the client brief "Automated Data Collection Agent" (docs/briefs/data-collection-agent.md) asks for Gemini to minimise AI cost; the client supplied a Gemini API key.
- Decision: add `GeminiAiProvider` behind `IAiProvider` (design.md §6) and make it the provider inferred when `Ai__ApiKey` is set; default model `gemini-3.5-flash-lite` (configurable). The Anthropic provider stays as `Ai__Provider=Anthropic`. Everything else of ADR-007 holds: fill-only-null, server-side validation, `AiCall` audit, daily limit, Fake in Development/Testing. New `Ai__MinIntervalMs` spaces calls for free-tier rate limits.
- Consequences: the key lives only in the environment (`.env`, git-ignored); `scripts/ai-smoke.sh` runs the live smoke test. Switching back is a config change.

## ADR-035 — HTML sources: deterministic site parsers first, AI only for gaps (supersedes ADR-006 in part)
- Date: 2026-10-08 · Status: accepted · Task: spec amendment v2 (T042, T044, T045)
- Context: the client named Prishtina.com (https://prishtina.com/events) and Ka' Po Dalim (https://kapodalim.com/ks) as the V1 sources (brief §V1 Sources) and asked for per-source adapters, a cleaning step before AI, no AI on unchanged content, sources disabled by default and no circumvention of access controls (brief §1, §2, §7, §16). Both robots.txt files allow `/` (checked 2026-10-08). Both pages are server-rendered.
- Decision: new source type `Html` with a required `adapter` key. `HtmlSourceAdapter` owns fetching (guarded client, robots, per-host delay) and cleaning; one `IHtmlSiteParser` per site maps a listing to raw fields deterministically. The cleaned listing text is the evidence; it is sent to the AI only when the start time, city or category could not be determined, so most listings cost no AI call. Only the configured page is fetched (no detail pages); listings beyond `Ingestion__HtmlHorizonDays` (30) are skipped to keep the review queue manageable. Both sources are seeded inactive (`Seed__KnownSources`). Parsers are pinned by saved-fixture contract tests; zero listings on a 2xx page fails the run (`MalformedPayload`) so layout changes surface on the dashboard.
- Consequences: no descriptions/images unless the listing shows them (the public page links to the original, REQ-022/REQ-030). A third site = one parser + fixtures + adapter key. The operator remains responsible for the sites' terms of use; sources stay inactive until an Admin enables them.

## ADR-036 — AI retry for unchanged candidates (resolves B-003, first part)
- Date: 2026-10-08 · Status: accepted · Task: T047
- Context: an open candidate whose AI call failed (`AiUnavailable`, e.g. a 429 on the free tier) was never re-enriched, because unchanged content skips processing (lookup row 1).
- Decision: on a later fetch with the same content hash, an open candidate (`Pending`, or auto-rejected) that still has `AiUnavailable` is re-sent to the AI when a provider is enabled, the daily limit allows it and it has fewer than 3 `AiCall` rows; validation and the duplicate check re-run; the run still counts it `unchanged` (REQ-015). No admin button in V1.
- Consequences: bounded extra cost (≤ 3 calls per candidate); the reviewer sees the result on the next run.

## ADR-037 — Confirmation dialogs for destructive admin actions (resolves B-003, second part; supersedes the "no confirmation" note of ADR-032)
- Date: 2026-10-08 · Status: accepted · Task: T047
- Context: "Skado", "Tërhiq" and "Çaktivizo" acted on one click; "Tërhiq" is permanent (withdrawn items never resurface). The locked UI acceptance tests do not click these buttons (only `web/e2e/flows` do), so a confirmation step does not touch the locked spec.
- Decision: each opens an in-page confirmation dialog (role `dialog`, not `window.confirm`) with "Po, skado" / "Po, tërhiq" / "Po, çaktivizo" and "Anulo"; focus moves into the dialog and returns to the trigger on cancel. The API is unchanged.
- Consequences: one extra click; the flows in `web/e2e/flows` are updated.

## ADR-038 — Bulk approve of valid candidates in the review queue
- Date: 2026-10-08 · Status: accepted · Task: T048
- Context: every publish needs human approval in V1 (ADR-005, confirmed by the client 2026-10-08), and the two HTML sources produce dozens of candidates per run.
- Decision: the review queue offers checkboxes on pending `Valid` rows and "Aprovo të zgjedhurat", which calls the existing approve endpoint per candidate (same rules, audit and 409 handling) and reports successes and failures. No new API. Release review round 2: only selected rows still shown as pending `Valid` are sent, and each candidate is read again first; one whose status, validation or `fetchedAt` changed since the list was loaded (a run updated it) is skipped and counted as failed. A 401 stops the loop and signs out. A change between that read and the approve call is not caught (a server-side precondition would need an API change).
- Consequences: approval stays a human decision; reviewing a full run takes minutes instead of one page per item.

## ADR-039 — Safety limits of the HTML adapter
- Date: 2026-10-08 · Status: accepted · Task: T042
- Context: the security review of T042 measured AngleSharp parsing time as quadratic in nesting depth (500 KB of nested `div` took 91 s) and a 5 MB page can hold ~200k listings, each of which may cost an AI call.
- Decision: `HtmlSourceAdapter` parses with a 5 s deadline (a timeout is `MalformedPayload`) and gives removing non-content, finding and mapping a second 5 s deadline, refuses a page nested deeper than 512 levels (checked iteratively right after parsing: AngleSharp's `TextContent`/`Clone` recurse per level and a 60 KB page with 20,000 nested elements overflowed the stack, which kills the process; release review round 2), refuses a page with more than 20,000 removable elements (scripts, ads, hidden; AngleSharp removal is quadratic in their number, release review round 2), reads at most 1000 listings (the rest is one `TooManyItems` failure, as for RSS), caps the evidence text at 16,000 characters (stricter than the 64 KB item limit of design.md §6; cutting stops early, so cost does not grow with the listing), rejects item links with credentials or longer than the 200-character `externalId` column, and turns any exception of a site parser into a per-listing failure `InvalidListing`.
- Consequences: a hostile or broken page fails one run instead of stalling the scheduler. Relative links resolve against the configured URL, not a redirect target (known limitation). The seeder identifies the known sources by URL only.

## ADR-040 — Shared AI extraction contract and process-wide call spacing
- Date: 2026-10-08 · Status: accepted · Task: T046
- Context: Gemini and Anthropic need the same prompt, schema and strict answer parsing; the Gemini free tier needs spaced calls (`Ai__MinIntervalMs`).
- Decision: `AiExtractionContract` holds prompt, schema (the provider supplies only the "type or null" spelling) and answer parsing; providers only add their envelope. `ThrottledAiProvider` decorates a remote provider and spaces call starts. Its wait counts against `Ai__TimeoutSeconds` (the enricher owns the timeout), so start-up requires `Ai__MinIntervalMs < Ai__TimeoutSeconds * 1000`.
- Consequences: many concurrent manual runs behind a long interval can still time out; the scheduler is sequential. A model name of the other vendor is refused at start-up so an old `.env` fails loudly.

## ADR-041 — Migrations run at start-up in every environment
- Date: 2026-10-08 · Status: accepted · Task: T043 (recorded in the release review)
- Context: design.md §9 said Production migrates in a separate deploy step, but T043 found Production never migrated and made `Program.cs` migrate at every start; no ADR recorded it.
- Decision: the app applies pending EF Core migrations at start-up in every environment. Single instance only (RUNBOOK), so no migration lock is needed.
- Consequences: a deploy is `up -d --build`; a failing migration stops the app before it serves traffic (restore from the nightly dump if needed). Two app containers must never start against the same database at once.

## ADR-042 — HTML listing identity, content hash and processing deadline
- Date: 2026-10-08 · Status: accepted · Task: release review (T045/T042 follow-up)
- Context: Ka' Po Dalim links a running series to today's occurrence (`/event/{slug}?date=YYYY-MM-DD`) and its poster shows that day, so identity and content changed every day: one new candidate per day and multiple public copies after bulk approve. The security review measured a hostile Ka' Po Dalim page stalling the scheduler for over an hour (listing discovery was quadratic and only the parse had a deadline).
- Decision: the Ka' Po Dalim identity and `url` are the canonical `/event/{slug}` without the query; a day range (from the date line, else from the slug's `-D1[-month]-D2-month-YYYY`) gives `startsAt` on the first day (with the shown time) and `endsAt` on the last day 23:59. The content hash of HTML listings covers the fields, the AI text and the URL but not the evidence (display-only card text such as the poster day). Listings whose category is shown but not mapped go to the AI like unknown ones. Cleaning, finding and mapping listings run under one deadline equal to the parse timeout (5 s; expiry → `MalformedPayload`); site parsers receive it as a `CancellationToken` and must stay linear in the page size.
- Amendment (release review round 2): the AI text still carried the poster day, date block and weekday, so a series with a TBA time or an unmapped category changed every day. The AI text is now read from the listing with occurrence-only parts left out: the site parser may supply the text of an element and its subtree (`IHtmlSiteParser.OccurrenceFreeText`, default none; Ka' Po Dalim: the poster `time` → its descriptor only, `.event-date-block` → nothing, the weekday before the first "·" of the date line, the position "02 / " of a featured pick). The walk is iterative and works on the original DOM: cloning the listing recursed once per nesting level and a 15 KB page with 5,000 nested elements overflowed the stack (security re-check). "Teknologji/Biznes" maps to `edukim` like Prishtina.com's "Tech & Business".
- Consequences: an unchanged series is `unchanged` on every fetch; the stored evidence is the text first seen until the fields change. A page that needs more than 5 s after parsing fails one run.

## ADR-043 — Manual fetch runs to completion when the request is aborted
- Date: 2026-10-08 · Status: accepted · Task: release review
- Context: "Merr tani" passed `RequestAborted` to the run; an HTML page with AI spacing (about 4 s per call on the Gemini free tier) can take minutes, so a 60 s proxy timeout cancelled the run after the AI calls were made and paid, and nothing was saved.
- Decision: the manual fetch endpoint runs with `ApplicationStopping` instead of the request token; the response contract (200 with the run summary, 409 when busy) is unchanged. RUNBOOK asks for a proxy read timeout of at least 300 s.
- Consequences: a client that gives up still gets the result in the source's run history; only shutdown interrupts a run (recorded as `Interrupted`).


## ADR-039 — Auto-publish per source (supersedes ADR-005 for sources that enable it)
- Date: 2026-10-10 · Status: accepted · Task: T049
- Context: the client wants the events of Prishtina.com and Ka' Po Dalim published without manual approval (2026-10-10), as in brief §6, while keeping a check on dates and expiry.
- Decision: `Source.AutoPublish` (default false). After validation and the duplicate check, a new candidate with validation `Valid` from such a source is approved by the system through the same code path as a person (Venue, ItemSource, `lastVerifiedAt`, audit `candidate.autoPublish` with user "system"). A changed published item whose candidate's only review issue is `ChangedSinceApproval` is applied the same way. Everything else (`UnknownCity`, `PossibleDuplicate`, `AiUnavailable`, `Expired`, …) stays in the review queue. Expiry is unchanged: `ExpirationJob` every 5 minutes plus time filtering on every public query. Sources without the flag behave exactly as before (all v1/v2 acceptance tests).
- Consequences: wrong data from a source can appear publicly without review; mitigations are the validation rules, the review queue for anything doubtful, and "Tërhiq" on the published list. Confidence thresholds (brief §6) are not used: validation outcome is the gate.
- Guards added in the T049 review (security-reviewer + reviewer): never for a listing a person rejected (a changed version is reviewed again); a change is applied only to an item the same source published (a merge is not consent to edit); the item link must be on the source's own host (no credentials in it); a candidate whose start, end, city or prices were filled by the AI waits for a person (prompt injection); an event may run at most 14 days, a daily deal must start within 24 h and not have ended (the 3-day window of ADR-042 bounds only the start); at most `AutoPublish__MaxPerRun` (100) items per run, the rest stays pending; `Ingestion__EventHorizonDays` 1–30, `AutoPublish__MinDealDiscountPercent` 1–100; the seeding of known sources is audited as "system". The audit entry carries `sourceId` and `matchedItemId`.
- Accepted limits (T049): a person's edit of a published item is overwritten when its source changes it again (withdraw to stop that); a reviewer, the expiration job or a parallel run changing a staged item or creating the same venue during a run fails that run with a concurrency error, which does not count as a source failure and is repeated by the next run (the paid AI calls of the failed run are repeated too); image URLs are not restricted to the source's host (CSP `img-src https:` and `referrerpolicy=no-referrer` are the protection).

## ADR-040 — Date-only events
- Date: 2026-10-10 · Status: accepted · Task: T050
- Context: 35 of 78 Prishtina.com listings and Ka' Po Dalim "ORARI TBA" listings show a date but no time; with `MissingStartsAt` they could never be auto-published, and inventing a time is forbidden (REQ-030).
- Decision: feed field / candidate field `startDate`; such events are valid, published at local 00:00 with `StartTimeKnown = false`, shown with the date only, and active until the end of that local day (REQ-025).
- Consequences: one new event column + DTO field; the "today" lists include date-only events of today for the whole day.

## ADR-041 — Local run uses its own database
- Date: 2026-10-10 · Status: accepted · Task: spec amendment v3
- Context: the E2E suites start the API in Development against the database `app`, so the localhost site the client looks at filled up with test and synthetic events.
- Decision: `scripts/dev.sh` uses the database `kosovaradar_local` (created by the migrations on first start), turns synthetic fixtures off and seeds the two real sources active with auto-publish. Tests keep using `app`.
- Consequences: the local site shows only data from the real sources; deleting the volume or the database resets it.

## ADR-042 — Only current content is published automatically (3-day event window, featured deals)
- Date: 2026-10-10 · Status: accepted · Task: T049
- Context: the client wants Kosova Radar to differ from Prishtina.com: quick information about what happens soon, not a full calendar; deals only when they are daily and a big discount (≥ 35 %), which is what users care about (client, 2026-10-10).
- Decision: `Ingestion__EventHorizonDays` = 3 (replaces the 30-day `Ingestion__HtmlHorizonDays` of ADR-035) for `Html` and auto-publish sources: events starting on a local date more than 3 days after the fetch date are skipped and picked up by a later fetch (every 3 h) once inside the window; ongoing events (started, not ended) are kept. Auto-publish of deals requires a daily deal (`validUntil` ≤ 24 h after `validFrom`/fetch) and `discountPercent` ≥ `AutoPublish__MinDealDiscountPercent` (35); other valid deals stay `Pending` for a person. Sources without auto-publish keep the v1 behaviour.
- Consequences: the public lists show at most ~3 days ahead from these sources; with a 3-hour poll an event appears 3 days (± 3 h) before it starts and disappears when it ends (ExpirationJob every 5 min).

## ADR-043 — No final release phase
- Date: 2026-10-10 · Status: accepted · Task: —
- Context: the release skill (whole-codebase review, hardening pass, exploratory QA with Opus) used most of the client's usage limit; T090 (hardening) and two release review rounds are already done.
- Decision: the client dropped T091 and the final release phase. Every task keeps its own checks (acceptance/unit/integration tests, `verify --quick`, `reviewer`, `security-reviewer` for 🔒 tasks).
- Consequences: no final whole-app QA pass or RELEASE.md refresh; run `./scripts/verify.sh` (full) before a real deployment.

## ADR-044 — Public UI follows the client's Lovable design
- Date: 2026-10-13 · Status: accepted · Task: T052
- Context: the client designed the public page in Lovable (project "Kosova Pulse": dark graphite theme, electric lime and coral, Manrope, animated radar mark, "in focus" event, day switcher, cards with live countdowns) and asked to use that design with the real data. The Lovable app uses TanStack Start, shadcn/ui and hard-coded sample items.
- Decision: port the look and the components into `web/src/features/public` (no Lovable runtime, no shadcn/Radix): tokens redefined under `.theme-radar` on the public layout only (the admin keeps the light theme), `@fontsource-variable/manrope` replaces Inter for the whole app, `lucide-react` for icons, "in focus" = today's first event with a picture, countdowns from `startsAt`/`endsAt` (end of the Pristina day when there is no end) and deal `validUntil`. The home tablist stays "Sot"/"Evente"/"Oferta" (REQ-100) styled as the day switcher, with "Nesër"/"Pasnesër" filters inside "Evente". Not taken over: sample data and pictures, the bookmark list "Të ruajturat" (not in the requirements), the detail modal (detail pages stay for links, SEO and REQ tests).
- Consequences: the public pages are dark; main bundle +~8 kB gzip (lucide icons, countdown). Design changes made later in Lovable must be ported by hand.

## ADR-045 — Shop deals from Foleja and Gjirafa Mall as "today's deals"
- Date: 2026-10-14 · Status: accepted · Task: spec amendment v4 (T053–T055)
- Context: the client wants deals from Gjirafa Mall and Foleja like the event sources. Neither shop states an end date for its offers (checked 2026-10-14), so the daily-and-≥35 % rule (ADR-042) would publish nothing. The client chose "today's deal": a product listed on the shop's deals page with ≥ 35 % off is today's deal while it stays listed.
- Decision: deal items without `validUntil` from auto-publish sources are listing deals (REQ-045): daily for REQ-044, active while a fetch listed them within `Deals__ListingStaleHours` (6 h; poll every 3 h), inactive as soon as a fetch shows < 35 %. Two HTML deal parsers (`foleja`, `gjirafaMall`) seeded like the event sources; parsers declare their kind; server-rendered pagination up to 5 pages. Robots.txt of both shops allows the listing pages. Feasibility is checked first in each parser task: no headless browser, no private APIs, nothing robots.txt disallows; a 403/bot challenge or JavaScript-only listing becomes a BLOCKER, not a workaround.
- Consequences: deals carry no city (online shops) and show "Oferta e sotme"; prices change often, so a deal can appear and disappear within a day. Shop terms of use stay the operator's responsibility; the sources can be deactivated in Admin → Burimet.

### ADR-045 amendment — as built (T053)
- Date: 2026-10-14 · Task: T053
- A listing deal is an undated deal of an auto-publish source with at least `AutoPublish__MinDealDiscountPercent` off (`DealOptions.IsListingDeal`); a person-approved undated deal with a small discount keeps the 7-day window and is not shown as "Oferta e sotme". `Deal.PrimarySource` (navigation, no schema change) lets the SQL twin `ActiveItems.Deal` read `Source.AutoPublish`; the DTOs carry `isTodaysDeal` (design.md §5).
- Ending a listing deal: a fetch that lists the item with both prices and a discount below the minimum moves its `LastVerifiedAt` to the start of the stale window (`Status` stays `Published`). Not `Expired`: that status is a person's "Skado", and the system never republishes an expired undated deal (`CandidateApproval` refuses it for auto-publish, the change waits for a person). When the discount returns, the same-hash path or the auto-published change on the same item makes it active again. A missing price never ends a deal.
- `ExpirationJob` does not touch listing deals that merely stop being listed (the time filter hides them; they reappear on the first fetch that lists them). Keep `Deals__ListingStaleHours` (4–168) above twice the poll interval of the shops (3 h); it is global, so a source polled less often than that must not auto-publish undated deals.
- Pagination: `IHtmlSiteParser.NextPage` reads the page as fetched (cleaning removes `nav`); `HtmlSourceAdapter.FetchAsync` follows only absolute, credential-free links on the same scheme/host/port, fragment stripped, never twice, robots.txt checked, up to `Ingestion__HtmlMaxPages` (1–10); a later page that fails or has no listings ends the listing and keeps the earlier pages. Each page resolves its links against its own URL. The 1000-listing cap is per run, not per page. Shop deals never reach the AI (cost; a missing price is a review issue). `ShopPrice.Parse` reads "1.299,00 €".
- Known limits, for the human to decide: (1) REQ-044 leaves every valid deal below 35 % `Pending`, so a shop page makes the review queue as long as its sale list (candidates beyond `AutoPublish__MaxPerRun` also stay pending); an amendment could skip new listing deals below the threshold and count them only in `received`. (2) The duplicate check compares city-less deals with all city-less deals and is one query per listing; product variants may be flagged `PossibleDuplicate` — T054 measures it with the real fixture. (3) The `foleja`/`gjirafaMall` parsers are placeholders until T054/T055 (runs fail `MalformedPayload`); Gjirafa Mall is seeded with the site root, and seeding keys on `baseUrl`, so T055 must handle the seeded row.

### ADR-045 amendment — Foleja as built (T054)
- The deals page is a landing page of product sliders; `?p=N` returns the same page, so the parser has no pagination (the category pages paginate through `/navigation/{id}?p=N`, not followed: one source URL). Sliders repeat boxes, so listings are de-duplicated by product path.
- Identity is the product path `/{SKU}/{slug}` (= `url` = `externalId`), the SKU may carry a variant suffix (`YLL-200005503.1`). Absolute links to another host are not product links.
- Reads only Shopware's own classes (`product-box`, `product-name`, `list-price-*`); the badge is read as the first percentage inside `.list-price-percentage`.

### ADR-045 amendment — Gjirafa Mall as built (T055)
- Date: 2026-10-14 · Task: T055
- Feasibility: the bot User-Agent gets 200 from `https://gjirafamall.com/`, robots.txt (`User-agent: *`) disallows only admin/cart/checkout/account/`/search?` paths. The home page's "Oferta speciale" and recommended products are skeletons filled by JavaScript, so they are not usable (no headless browser, no private API). `/oferta-speciale` is a server-rendered category page that lists the products; the dated campaign page `/app-deals-august-2026` is server-rendered and paginated too, but a monthly slug would break silently, so it was not chosen.
- Decision: source URL `https://gjirafamall.com/oferta-speciale`; `GjirafaMallDealsParser` lists only boxes with an old price (the page mixes regular-price products, which are not deals) and has no pagination. A source seeded by T053 on the site root is moved to the new URL by the seeder (a one-off exception to "seeding never touches rows"; that row could only fail).
- Known limits: the page is small (21 boxes, 10 on sale, 5 ≥ 35 % on the capture day) and its content is a shop-side curation we cannot see the rules of; if the shop empties or renames the category the run fails `MalformedPayload` (or lists nothing), which the source health shows. The slider the shop's home page labels "Oferta speciale" may hold other products that only a JavaScript client can read.

## ADR-046 — Public UI follows the client's design concept (supersedes ADR-044)
- Date: 2026-10-15 · Status: accepted · Task: T056
- Context: the client sent three design images (desktop concept, brand logo, phone concept) and asked for that look —
  white background, the "Kosova Radar · Digital tracking solutions" logo, modern boxes/buttons, more animation — with
  the phone version matching the concept but on white, without the bottom tab bar and without the profile avatar of
  the image. The dark Lovable theme of ADR-044 is dropped.
- Decision: one light theme for the whole app (no `.theme-radar` scope): white surfaces, `--color-brand` #10b981 with
  teal/mint for gradients and graphics, `--color-accent` #047857 wherever text or a filled button is involved (AA),
  violet for events, rose/cyan/orange/violet for discount badges by size. The logo is re-drawn as an inline SVG
  (`Logo.tsx`): transparent, it follows the text colour, and its radar waves, signal blip and footer float animate.
  Home = hero (eyebrow, h1, lead, search that opens /search, city chips, "Zbulo tani") with today's featured event and
  the radar overlay beside it, a four-item feature band, then the feed: the required "Sot/Evente/Oferta" tablist
  (REQ-100) over grouped sections ("Oferta të nxehta", "Evente aktuale") with their own headings and "Shiko të gjitha"
  links, and a closing CTA banner. Taken from the concept only what the product has: no weather badge, no profile
  menu, no bottom navigation, no price alerts or scam warnings; the header's city dropdown became the hero's city
  chips, which drive the existing `city` filter.
- Consequences: the admin inherits the same light tokens (green instead of blue). Bundle +2 kB gzip (Caveat subset for
  the hero's handwritten line). Cards keep every fact the acceptance tests require (full Pristina date-time, validity,
  source link, freshness, discount), so the locked suites are untouched.

## ADR-047 — Public UI is ported from the client's Lovable design (supersedes ADR-046)
- Date: 2026-10-15 · Status: accepted · Task: T057
- Context: the client did not like the interpretation of the concept images (T056) and pointed at the design they had
  just built themselves in Lovable ("Kosova Pulse", commit 9a8c4e58): white editorial surfaces, the official logo on
  its dark plate, a city photo behind the hero, a radar on the right, a benefit band, a day switcher with a sliding
  indicator, filter pills, bookmarks and compact four-column cards.
- Decision: port that design's CSS and markup one to one into `web/src` (class names kept: `page-width`,
  `discovery`, `benefit-band`, `day-switcher`, `feed-card`, `card-photo`, `countdown`, …) and connect it to the real
  API instead of the sample feed. Theme tokens are their oklch values (emerald `--color-accent`, berry events, rose
  discounts, amber sun). Differences forced by the product: the locked tablist (REQ-100) takes the day-switcher
  position and keeps the names "Sot"/"Evente"/"Oferta" (sub-labels are `aria-hidden`), the filter pills become the
  day filter inside the "Evente" tab, the weather widget becomes the city picker with today's date (no weather
  service), card titles are links to the detail pages instead of a modal, and the demo disclaimer is gone. The hero
  photo is today's first event picture, blurred as a backdrop, because the design's city photo is not ours to copy.
  "Të ruajturat" is implemented for real with `localStorage` (`saved.ts`), filtering the loaded feed.
- Consequences: the layout now lives mostly in component classes in `index.css` rather than utility classes; that is
  deliberate so the client's design can be re-synced. The admin keeps the same tokens (emerald instead of blue).
  Caveat and the SVG logo lockup of T056 are gone; `Logo.tsx` keeps only the mark used for placeholders and the
  favicon.

## ADR-048 — Public UI v3: brand from the logo, layout from the reference
- Date: 2026-10-15 · Status: accepted · Task: T058 (supersedes the look of ADR-044/046/047)
- Context: the client supplied `docs/prompts/kosova-radar-redesign-v2.md` with a reference layout
  (`docs/design/reference-layout.png`), the official logo files in `web/public/brand` and a fixed palette taken from
  the logo. The earlier Lovable-based look had no relation to the brand colours.
- Decision: the public pages follow the reference structure and the logo's identity. Tokens live in `index.css`:
  the `@theme` block keeps the admin's light theme and adds the brand palette; `.theme-radar` (the public layout)
  switches to warm paper #FAF8F5, charcoal #121214 bands and the orange gradient. Text on orange is always ink
  (7.0:1); white on orange is never used. Category badges use eight soft tints with dark text (6.3:1 and above).
  Plus Jakarta Sans replaces Manrope for the whole app. The header is solid charcoal at every scroll position.
- Deviations from the prompt, and why: (1) the header city select keeps the accessible name "Qyteti" but is hidden on
  `/events` and `/deals`, whose own filter owns that name — two would break the locked REQ-102 selector; (2) the tab
  panel uses a keyed fade instead of `AnimatePresence` cross-fade, because keeping the outgoing panel mounted shows
  each item twice and breaks the strict locators of REQ-100; (3) the home list keeps the reference's rows, while
  `/events` and `/deals` use the card grid; (4) the "Të ruajturat" bookmarks of ADR-047 are gone — the reference has
  no place for them.
- Consequences: `docs/design.md` §7 now allows a menu button below 1024 px on the public header (it still exposes
  every link, with Escape, a focus trap and focus return). The admin keeps its own light theme and only changes font.

## ADR-049 — Home: "Sot në Kosovë" (events only) and the deals panel replace the three tabs
- Date: 2026-10-16 · Status: accepted · Task: spec amendment v5 (T059)
- Context: the client wants the home page calmer and more premium: below the hero and categories only today's events, as large cards under the heading "Sot në Kosovë"; deals stay in the "Ofertat më të mira" panel beside the hero; full lists live on /events and /deals.
- Decision: REQ-100 amended (tabs "Sot"/"Evente"/"Oferta" removed); its acceptance test now checks the two labelled regions. The redesign itself (T059) follows `docs/prompts/kosova-radar-redesign-v3.md` and `docs/design/reference-v3.png`, with the logo recoloured to the new palette (`web/public/brand/*-violet*`).
- Consequences: "Nesër"/"Pasnesër" browsing moves to /events; the home page needs fewer requests.

## ADR-050 — Public UI v4: the reference palette, premium cards, four new libraries
- Date: 2026-10-16 · Status: accepted · Task: T059 (supersedes the look of ADR-048)
- Context: `docs/prompts/kosova-radar-redesign-v3.md` asked for a warmer, premium public site built on
  `docs/design/reference-v3.png`, with the card quality of the client's furniture shop and the logo recoloured to the
  new palette (`web/public/brand/*-violet*`).
- Decision: the palette of the reference lives in `.theme-radar` (paper #F3F6FA, white surfaces, ink #0B1B3F, violet
  #6D4AF6, gradient #5B3FE8 → #2F5BEA with white text, hero #03142E, headline gradient only on the hero); every pair
  in use is listed with its measured contrast in `index.css`. Each category keeps one pastel icon tile (eight pairs,
  4.5:1 and up) reused on chips, card badges and placeholders. Cards follow the shop's product cards — rounded card
  with a border, media frame with `object-cover`, hover lift and a 500 ms image zoom — extended with the light sweep,
  tap scale and staggered reveal the prompt asks for; product pictures are shown whole (`object-contain`) on their
  tile. Libraries added: Embla (swipeable deals panel and category chips below the breakpoint), Radix Dialog (menu
  sheet with its own focus trap, Escape and focus return), clsx + tailwind-merge (`cn`). Native `<select>` elements
  stay, restyled; the header's city select is labelled "Zgjidh qytetin".
- Deviations: the header city select is still hidden on `/events` and `/deals` — Playwright's `getByLabel('Qyteti')`
  matches substrings, so "Zgjidh qytetin" would collide with the locked filter label there. The client's furniture
  shop was read at `C:/furniture project` (the path in the prompt, `C:/TiaraHomeFurniture`, is an empty folder).
- Consequences: the main bundle grows to ~190 kB gzip (Radix + Embla). jsdom needs `matchMedia` and `ResizeObserver`
  stubs in `web/src/test/setup.ts` for Embla.

## ADR-051 — Brand colour refinement: lavender surfaces and the purple–blue gradient
- Date: 2026-10-16 · Status: accepted · Task: T060 (refines the palette of ADR-050; layout unchanged)
- Context: the client asked for a warmer, more premium colour system built on the logo's own gradient, without
  rebuilding the site: lavender page, white cards, navy type, one reusable purple→blue gradient used sparingly.
- Decision: `.theme-radar` now carries paper #F7F5FF (with a very light lavender-to-white wash under the header),
  white surfaces, soft #F1EEFC, ink #172554, muted #5F6E85, line #E8E5F5 and accent #7C3AED, plus the brand tokens
  `--color-violet-bright` #A855F7, `--color-blue` #2563EB and `--color-cyan` #06B6D4 (decorative). Two gradient
  tokens: `--brand-gradient` (110deg #A855F7 → #7C3AED → #2563EB) for decoration — the active nav indicator, small
  accents — and `--brand-gradient-solid` (#9333EA → #7C3AED → #2563EB) for anything carrying white text: primary
  buttons, discount badges, the selected category chip and the "Sot në Kosovë" heading tile.
- Accessibility: white on #A855F7 is only 4.0:1, so white text never sits on the lighter gradient; the solid variant
  measures 5.4:1 … 5.2:1. The brief's secondary text #64748B is 4.4:1 on this paper, so the token is the same slate
  one step darker (#5F6E85, 4.8:1 on paper, 5.2:1 on white, 4.5:1 on the soft tint). Both choices are written into
  the contrast comment in `index.css`.
- Logo: the artwork is never redrawn. The lockup is built from the two original files — the K is that mark's shape
  used as a CSS mask filled with `--k-gradient` (blue #18A8FF → violet → pink #F0459B, the colours the client sent),
  and the wordmark is the logo file with the mark cropped out by a background window (x 358–1619 of 1636), so
  "Kosova" keeps its navy and "Radar" its gradient. The brand line sits directly under the wordmark and now reads
  "Më afër gjërave që të interesojnë"; the hero lead is the client's sentence about events, deals and what is on.
- Consequences: only tokens and a handful of class usages changed; no component, route or data change. Category
  tiles keep their own colours so event types stay distinguishable.
