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
