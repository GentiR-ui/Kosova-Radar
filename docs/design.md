# Design — Kosova Radar (V1 private beta)

> HOW the product is built: the contracts the implementation must follow.
> The acceptance tests are written against sections 2–7, so keep them precise.
> Change after approval only together with an ADR in docs/DECISIONS.md.

## 1. Stack

Default stack (see docs/stack/dotnet-react.md). Deviations need an ADR.

| Concern | Choice |
|---|---|
| Frontend | React + TypeScript (Vite), TanStack Query, React Router, Tailwind |
| Backend | ASP.NET Core 10, minimal APIs; OpenAPI document at `/openapi/v1.json` in Development and Testing only |
| Architecture | Clean/Onion, 4 projects: Domain ← Application ← Infrastructure ← Api (names `App.*`, ADR-003) |
| Data | EF Core + PostgreSQL (ADR-002; the source PDF named SQL Server) |
| Auth | ASP.NET Core Identity, cookie auth (same origin, SameSite=Strict, HttpOnly, Secure), roles `Admin`, `Reviewer` |
| Multi-tenancy | **None** — single-operator content platform (ADR-001). No entity implements `ITenantOwned`. |
| Background jobs | `BackgroundService` scheduler + `JobExecution` table in PostgreSQL (ADR-004) |
| Outbound HTTP | `IHttpClientFactory` named client `sources` + `Microsoft.Extensions.Http.Resilience`, SSRF guard (§6) |
| AI | `IAiProvider` abstraction: `Gemini` (Gemini API `generateContent`, JSON-schema output; default, ADR-034), `Anthropic` (Claude Messages API, structured JSON output), `Fake` (Development/Testing), `None` (ADR-007, ADR-034) |
| Cache | none until measured need |
| Tests | xUnit (unit, integration, API acceptance) + Playwright (UI acceptance) |
| Delivery | One Docker image (API + SPA), GitHub Actions CI |

**Deviations:** ADR-001 (no tenancy, no registration), ADR-002 (PostgreSQL instead of SQL Server), ADR-003 (project names), ADR-004 (no Hangfire), ADR-014 (test hooks).

## 2. Architecture rules

- Domain: entities, value objects, domain rules (validation rules, effective end, freshness, discount, title similarity, slug/diacritic normalization). No framework references.
- Application: use cases, DTOs, request validation, pipeline orchestration (`IngestionService`), interfaces: `IAppDbContext`, `IClock`, `ISourceAdapter`, `ISourceFetcher`, `IAiProvider`, `IAuditWriter`.
- Infrastructure: `AppDbContext`, migrations, Identity, source adapters, HTTP fetcher (robots, SSRF guard, per-host throttle), AI providers, scheduler `BackgroundService`s.
- Api: endpoint mapping only, auth policies, problem details, rate limiting, test hooks middleware, SEO HTML/sitemap/robots, SPA hosting.
- No MediatR/CQRS/repositories over EF.

### Ingestion pipeline (one run per source)

```
Scheduler tick ─► due active sources ─► run (JobExecution)          manual "fetch now" ─► same run
  FetchAsync (adapter, via ISourceFetcher)      failure → run failed (error code), source bookkeeping, next source
  ParseAsync  → RawItem[]  (per-item failures → counted "failed")
  NormalizeAsync → CandidateFields (null when unknown) + Evidence
  Fingerprint = sha256(sourceId + (externalId ?? url)); item with neither → counted "failed"
  ContentHash = sha256(normalized fields as parsed + evidence); HTML listings: fields + AI text + url, without the evidence (ADR-042)
  Lookup by fingerprint, in this order (first match wins):
    1. pending or auto-rejected candidate: same hash → unchanged (AI retry only, ADR-036); other hash → update in place, re-run AI + validation (REQ-053)
    2. candidate rejected by a person with the same content hash → unchanged
    3. withdrawn item → unchanged (never resurfaces)
    4. published or expired item (incl. items it was merged into via ItemSource):
         same hash → item.lastVerifiedAt = fetchedAt, unchanged (REQ-052)
         other hash → new candidate, issue ChangedSinceApproval, matchedItemId (REQ-051)
    5. none → new candidate
  AI step: whenever `text` is non-empty and a provider is enabled (REQ-040..043)
  Validation → issues → Valid | NeedsReview | Rejected → status Pending | Rejected     (REQ-028..039)
  Duplicate check (§4)                                                                  (REQ-050)
  Horizon (Html and auto-publish sources): events starting > EventHorizonDays local days ahead are skipped before lookup (REQ-026)
  Auto-publish (source.AutoPublish): new candidate `Valid` (deals: daily and discount ≥ 35 %, REQ-044) → approve as the system;
    ChangedSinceApproval as the only review issue → approve onto the same item       (REQ-023/024, ADR-039)
Counters: received = items in the payload; created = new candidates (incl. auto-rejected and ChangedSinceApproval);
          updated = candidates updated in place; unchanged = as above; failed = per-item failures.
```

- `ISourceAdapter { bool CanHandle(Source); Task<FetchResult> FetchAsync(Source, ct); Task<FeedParseResult> ParseAsync(Source, FetchResult, ct); }` — returns normalized candidates (plus per-item failures), never entities; a whole-fetch failure is a `SourceFetchException` with a run error code (ADR-023).
- Adapters: `FixtureSourceAdapter`, `JsonFeedSourceAdapter`, `RssSourceAdapter` and `HtmlSourceAdapter` (type `Html`). `HtmlSourceAdapter` does the fetching and page cleaning and delegates the site-specific part to an `IHtmlSiteParser` chosen by `Source.Adapter` (`prishtinaEvents` → `PrishtinaEventsParser`, `kapodalim` → `KapodalimParser`, `foleja` → `FolejaDealsParser`, `gjirafaMall` → `GjirafaMallDealsParser`). Adding a site = one `IHtmlSiteParser`, its adapter key, saved fixtures and contract tests; `IngestionService` and the other pipeline steps do not change (ADR-035).
- Runs of the same source never overlap: a per-source `SemaphoreSlim`. Scheduled runs skip a locked source; manual runs wait up to 30 s, then 409.
- Retry policy: HTTP resilience handler (`Ingestion__HttpRetries` retries with exponential backoff, `Ingestion__HttpTimeoutSeconds` total timeout). A failed run is retried at the next due time; from the 3rd consecutive failure the source is `failing` and shows on the dashboard.
- On startup, `JobExecution` rows left in `Running` are marked `failed` with error code `Interrupted`.
- `ExpirationJob` (every 5 min, real clock): sets `Status = Expired` on published items past their effective end. Public queries additionally filter by time, so expiry is visible immediately (REQ-086). Listing deals (REQ-045) are not expired by the job when they merely stop being listed: they are inactive by the time filter and active again on the first fetch that lists them; a fetch that lists one below the minimum discount moves its `LastVerifiedAt` back to the start of the stale window, so it is inactive at once while its status stays `Published` (ADR-045).
- Source health is derived on read: `disabled` if inactive; else `never` if never fetched; `healthy` if the last run succeeded; `degraded` if it failed with < 3 consecutive failures; `failing` otherwise.

### Configuration (environment variables; `.env.example` documents them)

| Key | Production default | Development | Testing |
|---|---|---|---|
| `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | required when no user exists | `admin@kosovaradar.local` / `Radar-admin-dev-2026` | same as Development |
| `Ingestion__SchedulerTickSeconds` | 60 | 10 | 2 |
| `Ingestion__AllowFixtureSources` | false | true | true |
| `Ingestion__PerHostDelaySeconds` | 10 | 10 | 0 |
| `Ingestion__HttpRetries` / `Ingestion__HttpTimeoutSeconds` | 2 / 20 | 2 / 20 | 0 / 5 |
| `Ai__Provider` | `None` (`Gemini` when `Ai__ApiKey` set; explicit `Gemini`/`Anthropic` without a key stops startup) | `Fake` (environment variables `Ai__Provider=Gemini` + `Ai__ApiKey` override it for local trials, `scripts/dev.sh`) | `Fake` |
| `Ai__Model` | per provider: `gemini-3.5-flash-lite` (Gemini), `claude-haiku-4-5-20251001` (Anthropic) | same | — |
| `Ai__MinIntervalMs` | 0 (minimum spacing between two AI calls, process-wide; set ~4000 on the Gemini free tier) | 0 | 0 |
| `Ingestion__EventHorizonDays` (replaces `Ingestion__HtmlHorizonDays`) | 3 (events of `Html` and auto-publish sources starting on a local date more than N days after the fetch date are skipped, REQ-026) | 3 | 3 |
| `AutoPublish__MinDealDiscountPercent` | 35 (REQ-044) | 35 | 35 |
| `Deals__ListingStaleHours` | 6 (REQ-045) | 6 | 6 |
| `Ingestion__HtmlMaxPages` | 5 (pagination pages per HTML run, §6) | 5 | 5 |
| `Seed__KnownSources` | true (creates the Prishtina.com and Ka' Po Dalim sources, §6) | true | false |
| `Seed__KnownSourcesActive` / `Seed__KnownSourcesAutoPublish` | false / false (values given to newly seeded sources; `scripts/dev.sh` sets both true) | false / false | false / false |
| `Ai__DailyCallLimit` | 500 | 100000 | 100000 |
| `Ai__ApiKey` / `Ai__TimeoutSeconds` | — / 15 | — / 15 | — / 15 |
| `RateLimit__PublicPerMinute` | 300 | 0 (disabled) | 0 (disabled) |
| `Site__BaseUrl` | required (e.g. `https://kosovaradar.com`) | `http://localhost:5173` | `https://localhost` |
| `Seed__SyntheticFixtures` | false | true | false |
| `TestHooks__Enabled` | false (true → startup fails) | true | true |
| `Proxy__KnownProxies` | reverse proxy IPs | — | — |

Development/Testing values live in `appsettings.Development.json` / `appsettings.Testing.json` (dev-only credentials; Production requires its own bootstrap env vars).

### Test hooks (ADR-014)

When `TestHooks__Enabled=true`:
1. Request header `X-Test-Now: <ISO-8601 UTC>` makes `IClock.UtcNow` return that instant for the whole request, including a manual fetch run started by the request (`fetchedAt`, `lastFetchedAt`, `lastSuccessAt`, `lastVerifiedAt`, validation "now"), approve, edit, expire, and public queries (active/expired/freshness filtering). An invalid header value → 400 on `X-Test-Now`.
2. `GET /api/_test/throw` throws an unhandled exception with message `TEST-SECRET-EXCEPTION` (REQ-P09).

When disabled, the header is ignored and `/api/_test/**` responds 404. The scheduler and `ExpirationJob` always use the real clock.

### Testing recipe (contract for acceptance tests)

- Tests share one database and a running scheduler; never assume an empty database. Every title created by a test contains a unique token (e.g. `t` + 8 hex chars), which also keeps title similarity below 0.8 against other tests' data.
- Tests that assert run counts or time-shifted data create sources with `isActive: false` and use `POST /fetch`. Health tests (REQ-016/019) create the source with `isActive: false` and `pollIntervalMinutes: 1440`, run the needed `POST /fetch` calls **without** `X-Test-Now`, then `PUT` the source with `isActive: true` and read `health` (the scheduler will not fetch it again for 24 h because `lastFetchedAt` is recent). REQ-010 asserts `health: "never"` on the `POST` response of an active source. Only scheduler tests (REQ-012/017) rely on scheduled runs: `pollIntervalMinutes: 1440`, poll `GET /api/admin/sources/{id}` for up to 15 s until `recentRuns[0].trigger == "schedule"`; the REQ-017 failing source uses a malformed fixture payload. The REQ-013 test waits 3 ticks (6 s in Testing).
- Failure triggers: `MalformedPayload` = fixture payload `not json`; `Unreachable` = `https://kosovaradar-test.invalid/feed.json`; `BlockedAddress` = `http://127.0.0.1/`, `http://localhost/`, `http://169.254.169.254/`, `http://[::1]/`, `http://10.0.0.1/`. `Timeout`, `HttpStatus`, `RobotsDisallowed` are integration-tested with a stub handler.
- Time: use `X-Test-Now` (e.g. fetch 3 days in the past for `stale`, fetch before an event's end and query after it for expiry). For "today" in Pristina use start times between 00:01 and 23:58 local.
- AI: the Fake provider rules in §6.
- UI tests reach their own items through query-string filters (`/events?search=<token>`), never by list position.

## 3. Auth, authorization & HTTP security

- No public registration. On startup, if the user table is empty, create an active `Admin` from `Bootstrap__AdminEmail`/`Bootstrap__AdminPassword` (REQ-P01). In Production, missing bootstrap values with an empty user table → startup fails with a clear log message.
- **Development & Testing bootstrap admin: `admin@kosovaradar.local` / `Radar-admin-dev-2026`.**
- Roles: `Admin` (everything), `Reviewer` (dashboard, candidates, published items, audit). Admin-only: sources, users.
- Inactive users cannot log in (generic 401). `SecurityStampValidator` interval = 0, so deactivation (which updates the security stamp) ends sessions on the next request (REQ-003). No password change in V1; the runbook rotates the bootstrap admin by creating a personal Admin and deactivating the bootstrap one.
- Policies: `Reviewer` = role Admin or Reviewer; `Admin` = role Admin. Public endpoints are `AllowAnonymous` by design (read-only published content).
- Login limiter (REQ-P06): counts failed attempts per (client IP, normalized email) in a 1-minute fixed window; at > 10 failures, further attempts for that key → 429 until the window ends. All environments.
- Public limiter: fixed window per client IP on anonymous `/api/**`, `RateLimit__PublicPerMinute` (0 = off).
- Behind the production reverse proxy: `UseForwardedHeaders` (X-Forwarded-For/Proto) with `KnownProxies` from `Proxy__KnownProxies`, first in the pipeline.
- Headers on every response (REQ-P08): `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Content-Security-Policy: default-src 'self'; img-src 'self' https: data:; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'`; plus HSTS in Production. (JSON-LD data blocks are not affected by `script-src`.)
- Concurrency: `Candidate`, `Event`, `Deal` use PostgreSQL `xmin` as concurrency token; a concurrent change → 409.

## 4. Domain model

| Entity | Key fields | Notes |
|---|---|---|
| AppUser | Id, Email, Role (`Admin`/`Reviewer`), IsActive, CreatedAt | Identity user |
| City | Id, Slug, Name, Aliases[] | seeded: 38 Kosovo municipalities (below); unique Slug |
| Category | Id, Slug, Name, Kind (`Event`/`Deal`) | seeded (below); unique (Slug, Kind) |
| Venue | Id, Name, NormalizedName, CityId | found-or-created on publish by (NormalizedName, CityId) |
| Source | Id, Name, Type (`Fixture`/`JsonFeed`/`Rss`/`Html`), BaseUrl?, Adapter? (`prishtinaEvents`/`kapodalim`/`gjirafaMall`/`foleja`; `Html` only, else null), FixturePayload?, DefaultKind (`Event`/`Deal`, for Rss), IsActive, IsUnreliable, AutoPublish (default false), PollIntervalMinutes, LastFetchedAt?, LastSuccessAt?, LastErrorAt?, LastError?, ConsecutiveFailures, CreatedAt | |
| Candidate | Id, Kind, SourceId, ExternalId?, SourceUrl (as fetched, immutable), Fingerprint, ContentHash, Fields (jsonb `CandidateFields`; `fields.url` is the editable URL used on publish), Evidence (text ≤ 64 KB), Status (`Pending`/`Rejected`/`Published`/`Merged`), Validation (`Valid`/`NeedsReview`/`Rejected`), Issues (jsonb), SuggestedCategoryId?, DuplicateOfKind?, DuplicateOfId?, DuplicateOfIsCandidate?, MatchedItemId?, PublishedItemId?, RejectionReason?, RejectedByUserId?, IsSynthetic, Ai* (Model, RequestId, Confidence, ProcessedAt, SuggestedFields[]), FetchedAt, CreatedAt, UpdatedAt | indexes: Fingerprint, (Status, Validation), SourceId |
| Event | Id, Title, Description?, VenueId?, CityId, CategoryId?, StartsAt, StartTimeKnown (false = date only; StartsAt is then local 00:00 of that date, ADR-040), EndsAt?, PriceText?, PriceAmount?, Currency?, ImageUrl?, Status (`Published`/`Expired`/`Withdrawn`), PrimarySourceId, SourceUrl, Fingerprint, IsSynthetic, SearchText, LastVerifiedAt, PublishedAt, CreatedAt, UpdatedAt | indexes: StartsAt, CityId, CategoryId, Status, PrimarySourceId, Fingerprint |
| Deal | Id, Title, Description?, MerchantName?, CityId?, CategoryId?, OldPrice?, NewPrice?, PriceText?, Currency?, DiscountPercent?, ValidFrom?, ValidUntil?, ImageUrl?, Status, PrimarySourceId, SourceUrl, Fingerprint, IsSynthetic, SearchText, LastVerifiedAt, PublishedAt, CreatedAt, UpdatedAt | indexes: ValidUntil, Status, PrimarySourceId, Fingerprint |
| ItemSource | Id, ItemKind, ItemId, SourceId, SourceUrl, Fingerprint, CandidateId, AddedAt | all sources of a published item (merge adds rows) |
| AuditLog | Id (UUIDv7), UserId, UserEmail (snapshot), Action, EntityType, EntityId, Changes (jsonb `{ before, after }`), At (wall clock) | append-only; written in the same `SaveChanges` as the action (ADR-021) |
| JobExecution | Id, JobName (`SourceFetch`/`Expiration`), SourceId?, Trigger (`Schedule`/`Manual`), StartedAt, FinishedAt?, Status (`Running`/`Succeeded`/`Failed`), Received, Created, Updated, Unchanged, Failed, ErrorCode?, Error?, CorrelationId | run history |
| AiCall | Id, CandidateId, Provider, Model, RequestId?, Success, ErrorCode?, Confidence?, InputTokens?, OutputTokens?, StartedAt, DurationMs | cost/audit |

Money `decimal(18,2)`, currency ISO code (never assumed). Times `DateTimeOffset` UTC. "Europe/Pristina" in this spec = IANA zone `Europe/Belgrade` (no `Europe/Pristina` id exists) in both .NET `TimeZoneInfo` and browser `Intl`.

**Domain rules**

- **Effective end** of an event = `EndsAt` when present and ≥ `StartsAt`, else `StartsAt + 6 h` when the start time is known, else the end of the start date in Europe/Pristina (next local midnight; REQ-025, ADR-040) (internal, never displayed; ADR-010).
- **Date-only events** (ADR-040): candidate field `startDate` (local `YYYY-MM-DD`) with `startsAt` null. No `MissingStartsAt`; the horizon, `Expired` and duplicate rules use the date (local 00:00; duplicates compare dates as before). On publish `StartsAt` = local 00:00 of `startDate`, `StartTimeKnown = false`; a later known time sets `StartTimeKnown = true`. The UI shows only the date ("gjatë ditës" instead of a time).
- **Active event**: `Status = Published` and effective end > now. **Active deal**: `Status = Published` and (`ValidUntil` > now, or `ValidUntil` is null and `LastVerifiedAt` > now − 7 days; for a **listing deal** — no `ValidUntil`, its primary source has auto-publish and its discount is at least `AutoPublish__MinDealDiscountPercent` — `LastVerifiedAt` > now − `Deals__ListingStaleHours` (6 h), REQ-045, ADR-045). A published deal that is not active is shown as `expired` on detail.
- **Freshness**: `fresh` if `LastVerifiedAt` ≥ now − 48 h, else `stale`.
- **Discount**: `round((old − new) / old × 100)` (midpoint away from zero), only when both prices are present and old > new.
- **Normalization for matching/search**: lower-case, Albanian/Latin diacritics folded (ë→e, ç→c, č/ć→c, š→s, ž→z, đ→d), punctuation treated as a word separator ("Fushë-Kosovë" = "Fushë Kosovë"), whitespace collapsed (ADR-020). `SearchText` = normalized title + description + venue name.
- **Title similarity**: Jaccard similarity of normalized word sets (words ≥ 2 chars); threshold 0.8.
- **City matching**: normalized city text equals a city's normalized name, slug or alias.
- **Duplicate rules (REQ-050)**: compared against published items (status Published) of any source except the candidate's own `matchedItemId`, and against `Pending` candidates of other sources; same kind; events need equal non-null `citySlug` and equal Europe/Pristina start date; deals need equal `citySlug` (both null counts as equal); title similarity ≥ 0.8. First match by highest similarity is referenced.

**Validation issue codes** (`severity`): `MissingTitle` (rejected), `InvalidUrl` (rejected), `Expired` (rejected), `MissingStartsAt` (review), `InvalidDateRange` (review), `UnknownCity` (review), `MissingPrice` (review), `PriceInconsistent` (review), `ConflictingData` (review), `AiUnavailable` (review), `PossibleDuplicate` (review), `ChangedSinceApproval` (review), `SourceUnreliable` (review), `InvalidImageUrl` (info — image set to null). Any rejected → `Rejected`; else any review → `NeedsReview`; else `Valid`. Auto-rejected candidates (`RejectedByUserId = null`) stay editable; re-validation may make them `Pending`. Rejected by a person is final (REQ-067).

**Seeded cities** (slug — name — aliases): prishtine — Prishtinë — Pristina, Prishtina, Priština · prizren — Prizren · peje — Pejë — Peja, Peć · gjakove — Gjakovë — Gjakova, Đakovica · mitrovice — Mitrovicë — Mitrovica · mitrovice-veriore — Mitrovicë e Veriut — North Mitrovica · ferizaj — Ferizaj — Uroševac · gjilan — Gjilan — Gnjilane · podujeve — Podujevë — Podujeva · vushtrri — Vushtrri · suhareke — Suharekë — Suhareka · rahovec — Rahovec · drenas — Drenas — Gllogoc · lipjan — Lipjan · malisheve — Malishevë · kamenice — Kamenicë · viti — Viti · decan — Deçan · istog — Istog · kline — Klinë · skenderaj — Skenderaj · dragash — Dragash · fushe-kosove — Fushë Kosovë · kacanik — Kaçanik · shtime — Shtime · obiliq — Obiliq · novoberde — Novobërdë · gracanice — Graçanicë · hani-i-elezit — Hani i Elezit · junik — Junik · kllokot — Kllokot · mamushe — Mamushë · partesh — Partesh · ranillug — Ranillug · shterpce — Shtërpcë · zubin-potok — Zubin Potok · zvecan — Zveçan · leposaviq — Leposaviq.

**Seeded categories** — Event: `muzike` Muzikë & koncerte · `teater` Teatër & film · `art` Art & ekspozita · `sport` Sport · `familje` Familje & fëmijë · `ushqim` Ushqim & pije · `nate` Jeta e natës · `edukim` Konferenca & edukim. Deal: `teknologji` Teknologji · `ushqim` Ushqim & pije · `mode` Modë · `shtepi` Shtëpi · `sherbime` Shërbime · `udhetime` Udhëtime.

## 5. API contracts

All endpoints are under `/api` unless stated, JSON, camelCase. **Enums are camelCase strings** (`needsReview`, `jsonFeed`, `admin`); **issue codes and run error codes are PascalCase strings** exactly as listed (`MissingTitle`, `BlockedAddress`).
Errors are RFC 9457 problem details. Validation errors: 400 with `errors: { field: [messages] }` (query parameters use their parameter name).
All ids are UUIDs; a malformed or unknown id in the path → 404; an id in a request body → 400 on that field. Lists: `?page=1&pageSize=20` (1–100) → `Page<T> = { items: T[], page, pageSize, totalCount }`; a page beyond the last → 200 with empty `items`.
Times are ISO-8601 UTC (`2026-10-03T18:00:00Z`); `from`/`to` are `yyyy-MM-dd` dates interpreted in Europe/Pristina.

### Platform

| Method | Path | Auth | Request | Responses | Req |
|---|---|---|---|---|---|
| POST | /api/auth/login | anonymous; > 10 failed attempts/min per (client IP, email) → 429 | `{ email, password }` | 200 + auth cookie, body `MeDto`; 400 when `email` or `password` is missing or empty (not counted as a failed attempt); 401 generic (also for inactive users); 429 | P01, P02, P05, P06 |
| POST | /api/auth/logout | user | — | 204, cookie cleared | P11 |
| GET | /api/me | user | — | 200 `MeDto`; 401 | P03 |
| GET | /health | anonymous | — | 200 healthy; 503 unhealthy | P07 |
| GET | /api/_test/throw | anonymous; only with `TestHooks__Enabled` (else 404) | — | 500 problem details | P09 |

`MeDto = { userId: string, email: string, role: "admin" | "reviewer" }`

### Public (anonymous — published content only)

| Method | Path | Request | Responses | Req |
|---|---|---|---|---|
| GET | /api/events | `?city=&category=&from=&to=&search=&page=&pageSize=` | 200 `Page<EventSummaryDto>`; 400 | 080–082, 084, 086 |
| GET | /api/events/{id} | — | 200 `EventDetailDto`; 404 | 085, 086 |
| GET | /api/deals | `?city=&category=&search=&page=&pageSize=` | 200 `Page<DealSummaryDto>`; 400 | 083, 084, 086, 087 |
| GET | /api/deals/{id} | — | 200 `DealDetailDto`; 404 | 079, 085, 086 |
| GET | /api/search | `?q=&page=&pageSize=` (q ≥ 2 chars after trim) | 200 `Page<SearchResultDto>`; 400 on `q` | 089 |
| GET | /api/cities | — | 200 `CityDto[]` (ordered by slug) | 088 |
| GET | /api/categories | `?kind=event\|deal` | 200 `CategoryDto[]` (ordered by slug); 400 on unknown kind | 088 |

```
CityDto       = { slug, name }
CategoryDto   = { slug, name, kind: "event" | "deal" }
SourceRefDto  = { name, url }
EventSummaryDto = { id, title, startsAt, startTimeKnown, endsAt|null, city: CityDto, venueName|null, category: CategoryDto|null,
                    priceText|null, priceAmount|null, currency|null, imageUrl|null,
                    sourceName, sourceUrl, lastVerifiedAt, freshness: "fresh"|"stale",
                    status: "active"|"expired", isSynthetic }
EventDetailDto  = EventSummaryDto + { description|null, sources: SourceRefDto[] }   // primary source first
DealSummaryDto  = { id, title, merchantName|null, city: CityDto|null, category: CategoryDto|null,
                    oldPrice|null, newPrice|null, priceText|null, currency|null, discountPercent|null,
                    validFrom|null, validUntil|null, imageUrl|null,
                    sourceName, sourceUrl, lastVerifiedAt, freshness, status: "active"|"expired", isSynthetic,
                    isTodaysDeal /* no validUntil and the primary source auto-publishes: listing deal, REQ-045; the UI says "Oferta e sotme" */ }
DealDetailDto   = DealSummaryDto + { description|null, sources: SourceRefDto[] }
SearchResultDto = { kind: "event"|"deal", id, title, date|null /* event startsAt or deal validUntil */,
                    cityName|null, sourceName }
```

`sourceName`/`sourceUrl` = primary source name and the published `fields.url`.
Ordering: events by `startsAt` asc then `id`; deals by `validUntil` asc nulls last then `id`; search: events (by startsAt) then deals.
Filters: `from` = start on/after 00:00 Pristina of that date; `to` = start before 24:00 Pristina of that date.

### SEO (outside `/api`, anonymous)

| Method | Path | Responses | Req |
|---|---|---|---|
| GET | /events/{id}, /deals/{id} | 200 `text/html`: the SPA `index.html` (or a minimal built-in shell when no build is present) with `<title>{title} · Kosova Radar</title>`, `<meta name="description">`, `og:title`, `og:description` (description truncated to 200 chars, else "{dd.MM.yyyy, HH:mm} · {city}" for events / "{merchant} · Vlen deri më {date}" for deals, omitting a null part and its "·"), `og:url` (`Site__BaseUrl` + path), `og:image` only when https; events also `<script type="application/ld+json">` schema.org `Event` (name, startDate, endDate?, location: Place {name: venueName ?? city name, address.addressLocality: city name}, url). Synthetic items add `<meta name="robots" content="noindex">`. Unknown/non-public id → the plain shell with status 404. Encoding: `<title>`/`<meta content>` via `HtmlEncoder.Default`; JSON-LD via System.Text.Json default encoder (escapes `<`, `>`, `&`, `'`, so `</script>` cannot appear). | 090 |
| GET | /sitemap.xml | 200 `application/xml`, `<urlset>` with `{base}/`, `{base}/events`, `{base}/deals` and one `<url><loc>{base}/events/{id}` or `{base}/deals/{id}` per active published item (synthetic items cannot exist in Production because fixture sources are disallowed there) | 091 |
| GET | /robots.txt | 200 `text/plain`: `User-agent: *`, `Disallow: /admin`, `Disallow: /api/admin`, `Sitemap: {Site__BaseUrl}/sitemap.xml` | 092 |

### Admin — sources (policy `Admin`)

| Method | Path | Request | Responses | Req |
|---|---|---|---|---|
| GET | /api/admin/sources | — | 200 `SourceDto[]` (by name) | 013, 016, 019, 073 |
| GET | /api/admin/sources/{id} | — | 200 `SourceDetailDto` (incl. last 10 runs, newest first); 404 | 016, 019 |
| POST | /api/admin/sources | `SourceRequest` | 201 `SourceDto` (Location header); 400 | 010, 011 |
| PUT | /api/admin/sources/{id} | `SourceRequest` | 200 `SourceDto`; 400; 404 | 011, 013, 073 |
| POST | /api/admin/sources/{id}/fetch | — | 200 `RunDto` (also when the run failed: `status: "failed"`); works for active and inactive sources and ignores the poll interval; waits up to 30 s for a run in progress, then 409; 404 | 014–021 |

```
SourceRequest = { name (1–120), type: "fixture"|"jsonFeed"|"rss"|"html",
                  adapter: "prishtinaEvents"|"kapodalim"|"gjirafaMall"|"foleja"|null (required for html; ignored and stored null for other types),
                  baseUrl|null (absolute http/https, ≤ 2000; required unless fixture),
                  fixturePayload|null (string ≤ 1 MB; required for fixture; not validated as JSON on save),
                  defaultKind: "event"|"deal" (default "event"; used by rss),
                  pollIntervalMinutes (fixture: 1–1440; others: 15–1440),
                  isActive (bool), isUnreliable (bool), autoPublish (bool, default false) }
SourceDto = { id, name, type, baseUrl|null, adapter|null, defaultKind, isActive, isUnreliable, autoPublish, pollIntervalMinutes,
              health: "never"|"healthy"|"degraded"|"failing"|"disabled",
              lastFetchedAt|null, lastSuccessAt|null, lastErrorAt|null, lastError|null, consecutiveFailures }
SourceDetailDto = SourceDto + { fixturePayload|null, recentRuns: RunDto[] }
RunDto = { id, sourceId, trigger: "schedule"|"manual", status: "running"|"succeeded"|"failed", startedAt, finishedAt|null,
           received, created, updated, unchanged, failed, errorCode|null, error|null }
```

`type: "fixture"` when `Ingestion__AllowFixtureSources=false` → 400 on `type`. `type: "html"` without a known `adapter` → 400 on `adapter`; html poll interval 15–1440.
Run error codes: `Unreachable`, `Timeout`, `HttpStatus` (non-2xx), `MalformedPayload`, `BlockedAddress` (REQ-020), `RobotsDisallowed`, `TooLarge` (> 5 MB), `Interrupted`.

### Admin — candidates (policy `Reviewer`)

| Method | Path | Request | Responses | Req |
|---|---|---|---|---|
| GET | /api/admin/candidates | `?status=pending\|rejected\|published\|merged&validation=valid\|needsReview\|rejected&kind=event\|deal&sourceId=&page=&pageSize=` | 200 `Page<CandidateSummaryDto>` (newest `fetchedAt` first, then id); 400 | 060 |
| GET | /api/admin/candidates/sources | — | 200 `{ id, name }[]` (by name; feeds the queue's "Burimi" filter, because `/api/admin/sources` is Admin-only) | 060, 112 |
| GET | /api/admin/candidates/{id} | — | 200 `CandidateDto`; 404 | 061 |
| PUT | /api/admin/candidates/{id} | `CandidateFieldsRequest` | 200 `CandidateDto` (re-validated); 400; 404; 409 | 065, 067 |
| POST | /api/admin/candidates/{id}/approve | — | 200 `{ kind, itemId }`; 400 (missing fields named, or `validation` when rejected); 404; 409 | 062, 063, 067, 068 |
| POST | /api/admin/candidates/{id}/reject | `{ reason }` (1–500 after trim) | 200 `CandidateDto`; 400 on `reason`; 404; 409 | 064, 067, 074 |
| POST | /api/admin/candidates/{id}/merge | `{ targetId }` (published item of the same kind) | 200 `CandidateDto`; 400 on `targetId`; 404; 409 | 066, 067, 075 |

```
IssueDto = { code, severity: "rejected"|"review"|"info", field|null, message }
CandidateFields = { title|null, description|null, url|null, imageUrl|null, cityText|null, citySlug|null,
                    venueName|null, categoryText|null, categorySlug|null,
                    startsAt|null, startDate|null /* "YYYY-MM-DD", only while startsAt is null (ADR-040), 1900-2200 */,
                    endsAt|null, priceText|null, priceAmount|null, currency|null,
                    merchantName|null, oldPrice|null, newPrice|null, statedDiscountPercent|null,
                    discountPercent|null /* computed, read-only */, validFrom|null, validUntil|null }
CandidateSummaryDto = { id, kind, title|null, status, validation, issues: IssueDto[], sourceId, sourceName,
                        fetchedAt, isSynthetic }
CandidateDto = CandidateSummaryDto + { externalId|null, sourceUrl, fields: CandidateFields, evidence: string,
               suggestedCategory: CategoryDto|null,
               ai: { model, requestId|null, confidence|null, processedAt, suggestedFields: string[] } | null,
               duplicateOf: { kind, id, title, isCandidate } | null, matchedItemId|null,
               publishedItemId|null, rejectionReason|null }
CandidateFieldsRequest = CandidateFields without discountPercent/cityText/categoryText.
  Limits: title 1–300, description ≤ 10 000, other text ≤ 300, url/imageUrl absolute http(s) ≤ 2000,
  prices 0–1 000 000, currency ^[A-Z]{3}$, citySlug/categorySlug must exist (else 400 on that field).
```

Merge: item `lastVerifiedAt` = max(item.lastVerifiedAt, candidate.fetchedAt).
Approve: category = `fields.categorySlug ?? suggestedCategory.slug`. Required — 400 keys: `title`, `url` (all kinds); `citySlug`, `startsAt` (events; a `startDate` satisfies it); `newPrice` when a deal has neither `newPrice` nor `priceText`. Validation `Rejected` → 400 on `validation`.

### Admin — published items (policy `Reviewer`)

| Method | Path | Request | Responses | Req |
|---|---|---|---|---|
| GET | /api/admin/items | `?kind=event\|deal&status=published\|expired\|withdrawn&search=&page=&pageSize=` | 200 `Page<AdminItemDto>` | 069–071 |
| GET | /api/admin/events/{id} · /api/admin/deals/{id} | — | 200 `EventDetailDto`/`DealDetailDto` + `{ adminStatus: "published"\|"expired"\|"withdrawn" }`; 404 | 069 |
| PUT | /api/admin/events/{id} · /api/admin/deals/{id} | `CandidateFieldsRequest` (same required rules as approve) | 200; 400; 404; 409 concurrent change | 069 |
| POST | /api/admin/events/{id}/expire · /api/admin/deals/{id}/expire | — | 204 (idempotent); 404; 409 if withdrawn | 070 |
| POST | /api/admin/events/{id}/withdraw · /api/admin/deals/{id}/withdraw | — | 204 (idempotent); 404 | 071 |

`AdminItemDto = { kind, id, title, adminStatus, cityName|null, date|null, sourceName, lastVerifiedAt }`

### Admin — dashboard, audit, users

| Method | Path | Auth | Request | Responses | Req |
|---|---|---|---|---|---|
| GET | /api/admin/dashboard | Reviewer | — | 200 `{ pendingCandidates, needsReviewCandidates, activeEvents, activeDeals, expiredLast7Days, failingSources, degradedSources, aiCallsToday, aiErrorsToday }` | P10 |
| GET | /api/admin/audit | Reviewer | `?entityId=&page=&pageSize=` | 200 `Page<AuditEntryDto>` newest first | 072 |
| GET | /api/admin/users | Admin | — | 200 `UserDto[]` (by email) | 001 |
| POST | /api/admin/users | Admin | `{ email, password (≥ 12), role: "admin"\|"reviewer" }` | 201 `UserDto`; 400; 409 email taken | 001, 002 |
| POST | /api/admin/users/{id}/deactivate | Admin | — | 204; 404; 400 when deactivating yourself | 003, 004 |

```
AuditEntryDto = { id, userId, userEmail, action, entityType, entityId, at,
                  changes: { before: object|null, after: object|null } }   // camelCase, never passwords
  actions: candidate.approve, candidate.autoPublish (user "system", ADR-039), candidate.reject, candidate.edit, candidate.merge, item.edit, item.expire,
           item.withdraw, source.create, source.update, source.fetch (manual only), user.create, user.deactivate
  entityType: "candidate" | "event" | "deal" | "source" | "user"
UserDto = { id, email, role: "admin"|"reviewer", isActive, createdAt }
```

## 6. Feed formats, fetching and AI

### Radar JSON feed (types `fixture` and `jsonFeed`)

```json
{ "items": [ {
    "externalId": "string ≤ 200 (optional; identity falls back to url)",
    "kind": "event | deal",
    "title": "string", "description": "string", "text": "unstructured text for AI extraction",
    "url": "source page URL", "imageUrl": "https URL",
    "city": "free text", "venue": "free text", "category": "category slug or free text",
    "startsAt": "ISO-8601 with offset", "endsAt": "ISO-8601 with offset",
    "startDate": "YYYY-MM-DD (events only; used when the time is unknown and startsAt is absent)",
    "priceText": "string", "price": 12.5, "currency": "EUR",   (price: events only)
    "merchant": "string", "oldPrice": 100, "newPrice": 80, "discountPercent": 20,
    "validFrom": "ISO-8601 with offset", "validUntil": "ISO-8601 with offset"
} ] }
```

- All fields except `kind` are optional. Payload not JSON or without an `items` array → run fails `MalformedPayload`.
- Per-item failures (counted `failed`, other items processed — REQ-018): item is not an object; `kind` missing/unknown; a field of the wrong JSON type or an unparsable date; neither `externalId` nor `url`; raw item > 64 KB.
- Mapping: `city` → `cityText` (resolved to `citySlug` by city matching); `category` → `categoryText` (resolved to `categorySlug` if it equals a slug or name of the kind); `venue` → `venueName`; `price` → `priceAmount` (events only); `discountPercent` → `statedDiscountPercent`; `merchant` → `merchantName`; `url` → `fields.url` and `sourceUrl`.
- Evidence = the item's raw JSON text exactly as in the payload.
- `fixture` sources read `fixturePayload` (no network) and mark everything `isSynthetic`.

### RSS 2.0 / Atom (`rss`)

Each `<item>`/`<entry>` → candidate of the source's `defaultKind`; `externalId` = guid/id ?? link; `title`; `url` = link;
`text` = description/summary/content with HTML stripped (the AI fills start time, city, venue, prices). Evidence = the item/entry outer XML.
Parsed with `DtdProcessing=Prohibit`, `XmlResolver=null`, max 5 MB. Without AI, missing required fields surface as their issues plus `AiUnavailable`.

An iCalendar adapter is deferred (ADR-006). Site-specific HTML parsers are added per source (ADR-035, below).

### HTML pages (`html`, ADR-035)

`HtmlSourceAdapter` fetches `baseUrl` with the guarded `sources` client (robots.txt, per-host delay, 5 MB limit and the same run error codes), parses it with AngleSharp, removes `script`, `style`, `noscript`, `template`, `nav`, `header`, `footer`, `form`, `iframe`, ad containers and hidden elements (`[hidden]`, inline `display:none`/`visibility:hidden`; more than 20,000 such elements, or any element nested deeper than 512 levels → `MalformedPayload`, ADR-039), and asks the site parser for the listing elements. One listing = one raw item (REQ-022):
- `externalId` = `url` = the absolute event page URL (relative links resolved against the page URL). A listing without a link → per-item failure.
- Evidence = the listing's cleaned text (whitespace collapsed, ≤ 16,000 characters, ADR-039) followed by a line `URL: <url>` — never the whole page.
- The parser maps what the listing shows, deterministically: title, start date/time (Europe/Pristina local → instant with offset), end, venue, city, category text, price text, image (an `<img>` inside the listing, absolute http(s)) and description when shown; for deal parsers: title, old/new price (EUR, decimal comma accepted), stated discount, image, merchant = the shop's name, category text. Anything not shown stays null (REQ-030). Each parser declares its kind (event or deal).
- `text` (AI input) = the evidence text, set only when the start (`startsAt` or `startDate`), the city or the category could not be determined deterministically; otherwise empty so no AI call is made (cost, ADR-035). A site parser may leave out what only names the day's occurrence of a running series (`IHtmlSiteParser.OccurrenceFreeText`; Ka' Po Dalim: poster day, date block, weekday, featured pick position), so the text and the content hash stay the same every day (ADR-042). The AI step itself is unchanged (fill-only-null, REQ-040..043).
- Listings whose start date is more than `Ingestion__EventHorizonDays` (3) local days after the fetch date are skipped and counted only in `received` (REQ-026). Past listings are parsed as usual; validation rejects them as `Expired`.
- At most 1000 listings per page; the rest is one `TooManyItems` failure (ADR-039).
- No detail pages and no JavaScript execution. A parser may follow the listing's own server-rendered pagination links (same host, robots-checked, per-host delay) up to `Ingestion__HtmlMaxPages` (5) pages per run; "load more" buttons that need JavaScript are not followed.
- A 2xx page on which the parser finds no listing → run fails `MalformedPayload` (the site layout probably changed).

Site parsers — each with saved HTML fixtures in `tests/App.UnitTests/Fixtures/Html/` (captured once from the live page with the bot User-Agent, file name with the capture date) and contract tests asserting the parsed items. Cards are located by stable signals (event-link URL pattern first, then the text inside the link/card), never by generated CSS class names:
- `prishtinaEvents` — https://prishtina.com/events (robots.txt allows `/`). Server-rendered, grouped by day ("Today", "Tomorrow", weekday headings). Each event card is one link `/events/{slug}-{YYYY-MM-DD}-{6 hex}` showing: title (may end with "+ N more", kept), date ("Fri, 11 Sep"), optional festival label (e.g. "PriFest", ignored), category ("Culture & Arts", "Music", "Clubs", "Food & Drinks", "Tech & Business", "Other"), time ("14:30", optional) and venue. Start date from the URL (year-safe), time from the card; no time → `startsAt` null (`text` set). City = Prishtinë (the site covers Prishtina only). Category: Music → `muzike`; Clubs → `nate`; Food & Drinks → `ushqim`; Tech & Business → `edukim`; Culture & Arts → `teater` when the card text contains film/kino/theat/teat, else `art`; Other → null.
- `kapodalim` — https://kapodalim.com/ks (robots.txt allows `/`). Server-rendered "N'RADAR" cards linking to `/event/{slug}`: city badge ("PRISHTINË / 2026KPD-…"), date ("22 SHTATOR" or a range "14 SHTATOR … 25 SHTATOR"), time ("18:00", "18:00-21:00", "SEPTEMBER 22 AT 15:00"; "ORARI TBA"/"… TBA" → null), entry line (→ `priceText` unless it says the price is not published, e.g. "Hyrja s'u publikua", "Kostoja nuk është publikuar"), title, venue and city, category tag. Albanian month names (janar … dhjetor); year from the slug's trailing `-YYYY`, else from the event code (`2026KPD-…`), else the next occurrence after the fetch date. Date range → `startsAt` = first day (with the time when given), `endsAt` = last day 23:59 local. City via city matching on the badge/city text. Category: Koncert/Muzikë/Live → `muzike`; Teatër/Film/Kino → `teater`; Art/Ekspozitë → `art`; Sport → `sport`; Familje/Fëmijë → `familje`; Ushqim/Pije/Food → `ushqim`; Party/Natë/DJ/Club → `nate`; Edukim/Konferencë/Workshop/Kurs/Teknologji/Biznes → `edukim`; others (e.g. Komunitet, Lojëra) → null. If the page server-renders only the first N cards behind "Shiko më shumë", V1 takes those N. As built (T045): the start date is the `datetime` attribute of the poster's `time` element (fallback: a `YYYY-MM-DD` in the slug), which makes the slug-year/next-occurrence rules unnecessary; a card without any such date has no start. The grid `article` wins over a featured card for the same event; the saved page renders 11 events (the "78" total loads on demand). Identity and `url` are the canonical `/event/{slug}` without the occurrence query `?date=`; a day range from the date line or, else, the slug (`-7-16-tetor-2026`) sets `startsAt` to its first day and `endsAt` to its last day 23:59, so a running series keeps the same fields every day (ADR-042).

Deal parsers (ADR-045) — online shops list products on sale without an end date; they are **listing deals** (REQ-045): the parser leaves `validUntil` null, the item is published only when its discount is ≥ 35 % (REQ-044), stays active while the next fetches (every 3 h) still list it, and ends when it is no longer listed for 6 h or its discount drops below 35 %. The content hash of a listing deal covers title, prices, discount, image and URL only (no fetch-time values), so an unchanged product only refreshes `lastVerifiedAt`. Feasibility first: capture the page with the bot User-Agent; if it answers 403/429/a bot challenge, or the products are not in the server HTML, do not work around it (no headless browser, no private API, nothing robots.txt disallows): record a BLOCKER with what was seen.
- `foleja` — https://www.foleja.com/foleja-Deals/ (Shopware 6; robots.txt disallows only account/checkout/search paths). Product boxes with name, old price ("89,00 €"), new price, badge "-29%", image and link `/{SKU}/{Slug}` (SKU may have a variant suffix, "YLL-200005503.1"). As built (T054, captured 2026-10-14 with the bot User-Agent: 200, server-rendered): the page is a landing page of product sliders, not a paginated listing — `?p=N` returns the same page, so there is no `NextPage`; the sliders repeat boxes (207 boxes, 122 distinct products, 58 at ≥ 35 %), de-duplicated by path. The current price is a whole part ("€ 1,341") plus a separate decimal element ("30"); a box without a list price has no old price or badge. `url` (path) is the identity and `externalId`; `currency` EUR. Merchant "Foleja".
- `gjirafaMall` — https://gjirafamall.com/oferta-speciale (nopCommerce; robots.txt for `*` disallows only admin/cart/checkout/account/`/search?` paths). As built (T055, captured 2026-10-14 with the bot User-Agent: 200, server-rendered): the home page's "Oferta speciale" slider and "recommended" grid are skeleton boxes filled by JavaScript and are not used; the category page `/oferta-speciale` lists the products in the HTML. A listing is a `product-item` with `data-productid` and an `.old-price` in its prices block (the page also lists products at the regular price: skipped, they are not deals); the badge ("-38%") is the prices block's own `div`; identity = the product link path `/{slug}` (= `externalId`), prices "99.50 €" (point decimal, `ShopPrice`), `currency` EUR, merchant "Gjirafa Mall". The saved page has 20 products, 10 with an old price, 5 at ≥ 35 %. No `NextPage`: "Shfaq më shumë" needs JavaScript and `?pagenumber=2` returns no products on this page (the dated campaign page `/app-deals-august-2026` paginates but is a monthly campaign slug, so it is not used).

Seeding (`Seed__KnownSources`): at startup, for each known adapter a source is created when no source has its `baseUrl` — "Prishtina.com — Evente" (`html`, `prishtinaEvents`, https://prishtina.com/events), "Ka' Po Dalim" (`html`, `kapodalim`, https://kapodalim.com/ks), "Foleja — Deals" (`html`, `foleja`, https://www.foleja.com/foleja-Deals/, kind deal) and "Gjirafa Mall — Oferta" (`html`, `gjirafaMall`, https://gjirafamall.com/oferta-speciale, kind deal) — with `isActive` = `Seed__KnownSourcesActive`, `autoPublish` = `Seed__KnownSourcesAutoPublish` (both false by default), poll interval 180 and `isUnreliable: false`. Existing rows are never changed by seeding, with one exception: a `gjirafaMall` row still on the site root `https://gjirafamall.com` (seeded by T053, when its parser was a placeholder) is moved to the listing URL and keeps its name and settings. Parsers set `startDate` (not `startsAt`) when a listing shows a date but no time (REQ-025); `text` is then set only when the city or the category is unknown.


### HTTP fetching rules

- User-Agent `KosovaRadarBot/1.0 (+{Site__BaseUrl}/bot)`; `UseProxy=false`; max 5 redirects; response limit 5 MB (`TooLarge`).
- SSRF guard (REQ-020) runs in `SocketsHttpHandler.ConnectCallback` on the IP actually connected (defeats DNS rebinding; covers redirects). Blocked: loopback, 0.0.0.0/8, RFC 1918, 100.64/10, 169.254/16, 192.0.0/24, 198.18/15, multicast/broadcast, `::`, `::1`, fc00::/7, fe80::/10, and IPv4-mapped/compatible IPv6 forms of these.
- robots.txt is fetched through the same guarded client (cached 24 h). The guard is checked first, so a blocked host yields `BlockedAddress`. Robots network failure → `Unreachable`/`Timeout`; robots 4xx (not 429) → allow all; robots 5xx, 429 or a matching `Disallow` for our agent or `*` → `RobotsDisallowed`.
- One request per host (scheme-less authority, so a port counts) per `Ingestion__PerHostDelaySeconds`: a handler innermost in the `sources` client reserves the next slot, so robots.txt fetches and retries are spaced too; the wait is not counted against `Ingestion__HttpTimeoutSeconds`. Only the configured URL is checked against robots.txt and throttled; a redirect to another host is followed without either (the SSRF guard still applies).

### AI provider contract

```
IAiProvider.ExtractAsync(AiExtractionRequest { kind, text, knownFields, categories[] }, ct)
  → AiExtractionResult { model, requestId, confidence (0–1), fields: { startsAt?, endsAt?, city?, venue?, priceText?,
                         priceAmount?, currency?, merchant?, oldPrice?, newPrice?, validFrom?, validUntil? },
                         categorySlug?, isKosovoRelevant?, summary? }
```

- Called whenever `text` is non-empty and a provider is enabled. The Anthropic provider uses structured outputs (JSON schema) and the prompt states "unknown = null"; every response is still validated server-side against the schema and domain rules (dates parse, prices ≥ 0, slugs exist). Any violation → `AiUnavailable` (REQ-041).
- AI fills only null fields (REQ-040); `ai.suggestedFields` uses `CandidateFields` key names (a city filled by AI lists `cityText` and `citySlug`); category goes only to `suggestedCategory` (REQ-043).
- Conflicts (REQ-042): `startsAt`/`endsAt` compared as instants, city by resolved slug, prices by decimal equality; source value kept.
- Every call is stored in `AiCall`. Timeout 15 s. Daily limit `Ai__DailyCallLimit` counted from `AiCall` rows of the current UTC day. Calls are spaced by at least `Ai__MinIntervalMs` (process-wide).
- **Gemini provider** (ADR-034): `POST https://generativelanguage.googleapis.com/v1beta/models/{Ai__Model}:generateContent` with the key in the `x-goog-api-key` header (never in the URL, logs or exceptions), the same prompt and extraction schema as the Anthropic provider (nullable fields as `"type": ["…", "null"]`, category as an enum of the kind's slugs plus null), temperature 0. JSON text from `candidates[0].content.parts[0].text`; `responseId` → request id; `usageMetadata.promptTokenCount` / `candidatesTokenCount` → tokens. As built (T046): `generationConfig` = `{ temperature: 0, maxOutputTokens: 4096, responseMimeType: "application/json", responseJsonSchema }` (4096 because thinking tokens of newer Gemini models may count against the limit) and `systemInstruction.parts[0].text`, pinned by a unit test (the newer Interactions API uses `response_format`; `generateContent` still takes the fields above). The model name goes into the URL path, so `Ai__Model` is limited to letters, digits, `.`, `-`, `_` at start-up. The answer is the first non-thought part that has text. `finishReason` other than `STOP`, a `promptFeedback.blockReason`, thought parts only or no text → `AiUnavailable`. `Ai__MinIntervalMs` > 0 wraps the remote provider in `ThrottledAiProvider` (spacing between call starts, process-wide). HTTP 429/5xx, timeout, a blocked/empty candidate, non-JSON or schema-violating output → `AiUnavailable` (REQ-041). `GeminiAiProviderTests.Live_smoke_test_against_the_real_API` runs only when `Ai__ApiKey` is set (`scripts/ai-smoke.sh`).
- **Retry (ADR-036):** an open candidate (`Pending`, or `Rejected` without a reviewer) whose content is unchanged on a later fetch but still has `AiUnavailable` is sent to the AI again when a provider is enabled, the daily limit allows it and it has fewer than 3 `AiCall` rows; then validation and the duplicate check re-run. It still counts as `unchanged`.

**Fake provider (Development/Testing, deterministic — acceptance tests rely on it):**
- If `text` contains `#ai-fail` → throws (provider failure). If it contains `#ai-invalid` → returns output that violates the schema.
- `startsAt` = first ISO-8601 date-time with offset in `text` (e.g. `2026-10-20T20:00:00+02:00`), else null.
- Price = first match of `(\d+(?:[.,]\d{1,2})?)\s?€` (decimal comma accepted), else null. Events: fills `priceAmount` + `currency: "EUR"`; deals: fills `newPrice` + `currency: "EUR"` (never `priceAmount`).
- `city` = seeded city name, slug or alias appearing as whole words in the folded `text`; the longest match wins (e.g. "Mitrovicë e Veriut" over "Mitrovicë"), then the earliest position; else null.
- `categorySlug`: event kind — `muzike` if text contains "koncert" or "muzik", `teater` if "teat" or "film", `sport` if "ndeshje" or "sport"; deal kind — `teknologji` if "laptop" or "telefon"; else null.
- `model = "fake-1"`, `requestId = "fake-" + guid`, `confidence = 0.9`.

## 7. UI map

All UI text is Albanian (ADR-009). Times shown in Europe/Pristina, format `dd.MM.yyyy, HH:mm`.
Every list has a loading state (`role="status"`, "Duke u ngarkuar…"), an empty state ("Nuk u gjet asnjë rezultat")
and an error state ("Diçka shkoi keq." + button "Provo sërish"). Cards are `<article>` elements whose heading is the item title.
List pages keep filters in the query string (`/?search=`, `/events?city=&category=&from=&to=&search=`, `/deals?city=&category=&search=`, `/search?q=`)
and show a button "Shfaq më shumë" while more pages exist. Admin navigation stays visible at 360 px; the public header
keeps the logo, the search link and a menu button below 1024 px, whose sheet holds the same links (ADR-048).

**Visual direction (ADR-048).** Light warm paper with charcoal header, hero and promo surfaces, and the logo's orange as
the single accent (gradient fills carry ink text, never white). One typeface (Plus Jakarta Sans) for public and admin;
tabular numerals for prices, discounts, dates and countdowns. Home follows the reference layout: hero + "Ofertat më të
mira" panel, category row, tablist over the event/deal rows, promo card. Cards are 16 px radius with a 16:10 media
frame (a category-tinted tile with its icon when an item has no picture) and lift on hover. Motion is opacity/transform
only, 200–500 ms, and stops under `prefers-reduced-motion`; the LIVE dot is the only loop.
Titles, descriptions and evidence are rendered as text (never `dangerouslySetInnerHTML`); `href` only for http(s) URLs.

### Public

| Route | Purpose | Key elements (accessible names used by tests) | Req |
|---|---|---|---|
| (all public) | layout | header link "Kosova Radar" (home); nav links "Evente", "Oferta", "Kërko" | — |
| / | feed | `h1` "Çfarë ka sot në Kosovë"; search input "Kërko" (filters both tabs); tablist with tabs "Sot", "Evente", "Oferta"; the selected tab panel lists cards | 100, 104, 108 |
| (cards and detail) | events with `startTimeKnown: false` | show the date and "gjatë ditës" instead of a time (ADR-040) | 025 |
| /events | event list | `h1` "Evente"; selects "Qyteti" (option "Të gjitha qytetet" + city names), "Kategoria"; date inputs "Nga data", "Deri më"; search input "Kërko në evente"; event cards | 101, 102, 104, 107, 108 |
| /events/:id | event detail | `h1` title; date/time text; city and venue; price or "Çmimi nuk dihet" when unknown; link "Burimi: {sourceName}" (external, `rel="noopener noreferrer"`, new tab); freshness text; banner "Ky event ka përfunduar" when expired; badge "Të dhëna testuese" when synthetic | 101, 103, 107 |
| /deals | deal list | `h1` "Oferta"; selects "Qyteti", "Kategoria"; search input "Kërko në oferta"; deal cards | 101, 104, 106, 107, 108 |
| /deals/:id | deal detail | `h1` title; merchant; old price (`<s>`), new price, discount text "-{n}%"; validity "Vlen deri më {date}" or "Afati nuk dihet"; link "Burimi: {sourceName}"; freshness; banner "Kjo ofertë ka skaduar" when expired | 101, 103, 106, 107 |
| /search | unified search | `h1` "Kërkim"; search input (`type="search"`) "Kërko"; button "Kërko"; result list items with a kind label "Event" or "Ofertë" and a link with the title | 105 |

Card content: title (link to detail), Pristina date/time (events) or "Vlen deri më …"/"Afati nuk dihet" (deals), city, link "Burimi: {sourceName}", freshness text
"Verifikuar së fundmi" (fresh) or "Kontrolluar së fundi më {dd.MM.yyyy}" (stale), badge "Të dhëna testuese" for synthetic items.
Images: lazy-loaded, explicit width/height, `referrerpolicy="no-referrer"`; no image → neutral placeholder (no invented imagery).

### Admin (all under /admin; not indexed)

| Route | Purpose | Key elements (accessible names used by tests) | Req |
|---|---|---|---|
| /admin/login | sign in | `h1` "Hyrje për administratorë"; inputs "Email", "Fjalëkalimi"; button "Hyr"; errors "Email ose fjalëkalim i pasaktë" (401) and "Shumë përpjekje. Provo pas një minute." (429) | P02, P10 |
| (all admin) | layout | nav links "Paneli", "Shqyrtimi", "Të publikuarat", "Burimet" (Admin only), "Përdoruesit" (Admin only); button "Dil" | P11 |
| /admin | dashboard | `h1` "Paneli"; stats labelled "Në pritje për shqyrtim", "Kërkojnë vëmendje", "Evente aktive", "Oferta aktive", "Burime me probleme" | P10 |
| /admin/review | candidate queue | `h1` "Radha e shqyrtimit"; selects "Statusi" (default "Në pritje"; options "Të gjitha", "Në pritje", "Refuzuar", "Publikuar", "Bashkuar"), "Validimi", "Lloji", "Burimi"; table with column headers "Titulli", "Burimi", "Validimi", "Statusi"; rows with a link named by candidate title (or "(pa titull)"), validation badge ("Valid", "Kërkon shqyrtim", "Refuzuar") and status cell ("Në pritje", "Refuzuar", "Publikuar", "Bashkuar"); filters in the query string; pending `Valid` rows have a checkbox "Zgjidh {title}" and the page a button "Aprovo të zgjedhurat" that approves the selected candidates one by one through the approve endpoint (a candidate whose status, validation or `fetchedAt` changed since the list was loaded is skipped and counted as failed) and then shows "{n} u aprovuan." or, when some fail, "{n} u aprovuan, {m} dështuan (mund të jenë ndryshuar nga dikush tjetër). Rifreskoni listën." (ADR-038) | 060, 112 |
| /admin/items/:id | candidate review | `h1` candidate title or "(pa titull)"; region "Evidenca nga burimi" (evidence text + link "Hap burimin"); region "Fushat e normalizuara" (side by side ≥ 1024 px, stacked below) with inputs "Titulli", "Përshkrimi", "Lidhja e burimit", "Qyteti", "Vendi", "Kategoria", "Fillon më", "Përfundon më", "Çmimi (tekst)", "Çmimi", "Monedha", and for deals "Tregtari", "Çmimi i vjetër", "Çmimi i ri", "Vlen nga", "Vlen deri"; list "Problemet e validimit"; buttons "Ruaj ndryshimet", "Aprovo", "Refuzo", "Bashko"; reject dialog with textarea "Arsyeja" and button "Konfirmo refuzimin"; merge dialog with the suggested duplicate and button "Bashko me këtë"; after approve status message "U publikua" and link "Shiko faqen publike" | 110–112 |
| /admin/published | published items | `h1` "Të publikuarat"; filters "Lloji", "Statusi"; rows with link "Ndrysho" (to the edit page, ADR-032) and buttons "Skado", "Tërhiq"; each opens a confirmation dialog (role `dialog`) with buttons "Po, skado" / "Po, tërhiq" and "Anulo" (ADR-037) | 069–071 |
| /admin/published/:kind/:id | edit published item | same field labels as candidate review; button "Ruaj ndryshimet" | 069 |
| /admin/sources | sources | `h1` "Burimet"; button "Shto burim" opens form with "Emri", "Lloji", "URL", "Të dhënat testuese (JSON)", "Lloji i paracaktuar", "Intervali (minuta)", checkbox "Publiko automatikisht" (default unchecked; ADR-039), select "Adapteri" (options "Prishtina.com — evente", "Ka' Po Dalim"; shown only when "Lloji" is "Faqe HTML" (value `html`)); checkboxes "Aktiv" (default unchecked), "I pabesueshëm"; "Intervali (minuta)" defaults to 60; button "Ruaj burimin"; table rows: name, health badge ("Pa u marrë ende", "Në rregull", "Me probleme", "Dështon", "Joaktiv"), last success, last error, buttons "Merr tani" and "Ndrysho"; after "Merr tani" a status message "Marrja përfundoi: {created} të reja, {updated} të përditësuara, {unchanged} pa ndryshim, {failed} të dështuara", "Marrja dështoi: {errorCode}" or (409) "Një marrje është në vazhdim. Provo pas pak." | 113 |
| /admin/users | users | `h1` "Përdoruesit"; form inputs "Email", "Fjalëkalimi", select "Roli" ("Administrator", "Shqyrtues"); button "Krijo përdoruesin"; rows with button "Çaktivizo", which opens a confirmation dialog with buttons "Po, çaktivizo" and "Anulo" (ADR-037) | 001–004 |

Reviewer role: Admin-only links hidden; direct navigation shows "Nuk keni leje për këtë faqe".

## 8. Integrations

| Service | Purpose | Status |
|---|---|---|
| Google Gemini API (`generateContent`, JSON schema) | extraction/classification (REQ-040..043), default provider (ADR-034) | behind `IAiProvider`; `Ai__ApiKey` from the environment (`.env`, never committed); Fake in dev/test |
| Anthropic Claude API (Messages, structured outputs) | alternative provider (`Ai__Provider=Anthropic`) | built behind `IAiProvider` |
| Public source sites | content | generic Radar JSON / RSS adapters; HTML parsers for Prishtina.com and Ka' Po Dalim (ADR-035), seeded inactive |
| Payments | — | not in V1 |
| Email | — | not in V1 (source failures surface on the dashboard) |

## 9. Non-functional notes

- Logging: structured; scopes `CorrelationId`, `RunId`, `SourceId`; never log passwords, cookies, API keys or full payloads.
- Metrics (V1 = queryable from `JobExecution`, `AiCall`, items; the dashboard shows the counts in §5): fetch success rate, run duration, AI error rate, candidate→publish rate, stale count; API latency from request logging.
- Migrations: EF Core migrations in Infrastructure; applied automatically at start-up in every environment, single instance (ADR-041).
- Seed: cities and categories in every environment (idempotent); synthetic fixture source "Fixture — Shembuj sintetikë" (inactive; the seed runs one fetch of it at startup when it has never been fetched) with 24 items covering missing time, missing price, same title in two cities, expired event, invalid URL, non-Kosovo location, conflicting data, AI failure — only when `Seed__SyntheticFixtures=true`.
- Frontend: route-level code splitting (admin bundle lazy-loaded), one self-hosted variable font (Manrope) for the whole app, UI transitions ≤ 600 ms, decorative loops (radar sweep, logo ping, live dot) stopped under `prefers-reduced-motion`; the public pages are ported from the client's own Lovable design (white surfaces, emerald identity, berry events, rose discounts; ADR-047) and the admin uses the same tokens. Bookmarks ("Të ruajturat") live in `localStorage`, never on the server.
- Config: environment variables only; keys documented in `.env.example`.
