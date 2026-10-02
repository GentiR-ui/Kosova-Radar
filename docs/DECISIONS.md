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
- Decision: All UI strings in Albanian, kept in one strings module per feature to ease later i18n. Store UTC, render Europe/Pristina.

## ADR-010 — Expiry and freshness rules
- Date: 2026-10-03 · Status: accepted · Task: spec
- Context: "Expired items must not appear as active" needs an end time, but many sources give only a start time; "never invent facts".
- Decision: Event effective end = `EndsAt` ?? `StartsAt + 6 h` (internal only, never displayed). Deals expire at `ValidUntil`; deals without it drop out of lists after 7 days without re-verification. Freshness `fresh` ≤ 48 h since last verification, else `stale` with "Kontrolluar së fundi më …". Public queries filter by time so expiry is immediate; a 5-minute job also persists `Expired`.

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
