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
| AI | `IAiProvider` abstraction: `Anthropic` (Claude Messages API, structured JSON output), `Fake` (Development/Testing), `None` (ADR-007) |
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
  ContentHash = sha256(normalized fields as parsed + evidence)
  Lookup by fingerprint (first match wins):
    pending or auto-rejected candidate: same hash → unchanged; other hash → update in place, re-run AI + validation (REQ-053)
    candidate rejected by a person:     same hash → unchanged; other hash → new candidate
    published or expired item (incl. items it was merged into via ItemSource):
                                        same hash → item.lastVerifiedAt = fetchedAt, unchanged (REQ-052)
                                        other hash → new candidate, issue ChangedSinceApproval, matchedItemId (REQ-051)
    withdrawn item:                     → unchanged (never resurfaces)
    none                                → new candidate
  AI step: whenever `text` is non-empty and a provider is enabled (REQ-040..043)
  Validation → issues → Valid | NeedsReview | Rejected → status Pending | Rejected     (REQ-028..039)
  Duplicate check (§4)                                                                  (REQ-050)
Counters: received = items in the payload; created = new candidates (incl. auto-rejected and ChangedSinceApproval);
          updated = candidates updated in place; unchanged = as above; failed = per-item failures.
```

- `ISourceAdapter { string Type; bool CanHandle(Source); Task<FetchResult> FetchAsync(Source, ct); Task<ParseResult> ParseAsync(FetchResult, ct); Task<IReadOnlyList<NormalizedCandidate>> NormalizeAsync(ParseResult, ct); }` — returns normalized candidates, never entities.
- Runs of the same source never overlap: a per-source `SemaphoreSlim`. Scheduled runs skip a locked source; manual runs wait up to 30 s, then 409.
- Retry policy: HTTP resilience handler (`Ingestion__HttpRetries` retries with exponential backoff, `Ingestion__HttpTimeoutSeconds` total timeout). A failed run is retried at the next due time; from the 3rd consecutive failure the source is `failing` and shows on the dashboard.
- On startup, `JobExecution` rows left in `Running` are marked `failed` with error code `Interrupted`.
- `ExpirationJob` (every 5 min, real clock): sets `Status = Expired` on published items past their effective end. Public queries additionally filter by time, so expiry is visible immediately (REQ-086).
- Source health is derived on read: `disabled` if inactive; else `never` if never fetched; `healthy` if the last run succeeded; `degraded` if it failed with < 3 consecutive failures; `failing` otherwise.

### Configuration (environment variables; `.env.example` documents them)

| Key | Production default | Development | Testing |
|---|---|---|---|
| `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | required when no user exists | `admin@kosovaradar.local` / `Radar-admin-dev-2026` | same as Development |
| `Ingestion__SchedulerTickSeconds` | 60 | 10 | 2 |
| `Ingestion__AllowFixtureSources` | false | true | true |
| `Ingestion__BlockPrivateNetworks` | true | true | true |
| `Ingestion__PerHostDelaySeconds` | 10 | 10 | 0 |
| `Ingestion__HttpRetries` / `Ingestion__HttpTimeoutSeconds` | 2 / 20 | 2 / 20 | 0 / 5 |
| `Ai__Provider` | `None` (`Anthropic` when `Ai__ApiKey` set) | `Fake` | `Fake` |
| `Ai__Model` | `claude-haiku-4-5-20251001` | — | — |
| `Ai__DailyCallLimit` | 500 | 100000 | 100000 |
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
- Tests that assert run counts, health sequences or time-shifted data create sources with `isActive: false` and use `POST /fetch`. Only scheduler tests (REQ-012/017) create active sources: `pollIntervalMinutes: 1440`, then poll `GET /api/admin/sources/{id}` for up to 15 s until `recentRuns[0].trigger == "schedule"`. The REQ-013 test waits 3 ticks (6 s in Testing).
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
- Headers on every response (REQ-P08): `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Content-Security-Policy: default-src 'self'; img-src 'self' https: data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'`; plus HSTS in Production. (JSON-LD data blocks are not affected by `script-src`.)
- Concurrency: `Candidate`, `Event`, `Deal` use PostgreSQL `xmin` as concurrency token; a concurrent change → 409.

## 4. Domain model

| Entity | Key fields | Notes |
|---|---|---|
| AppUser | Id, Email, Role (`Admin`/`Reviewer`), IsActive, CreatedAt | Identity user |
| City | Id, Slug, Name, Aliases[] | seeded: 38 Kosovo municipalities (below); unique Slug |
| Category | Id, Slug, Name, Kind (`Event`/`Deal`) | seeded (below); unique (Slug, Kind) |
| Venue | Id, Name, NormalizedName, CityId | found-or-created on publish by (NormalizedName, CityId) |
| Source | Id, Name, Type (`Fixture`/`JsonFeed`/`Rss`), BaseUrl?, FixturePayload?, DefaultKind (`Event`/`Deal`, for Rss), IsActive, IsUnreliable, PollIntervalMinutes, LastFetchedAt?, LastSuccessAt?, LastErrorAt?, LastError?, ConsecutiveFailures, CreatedAt | |
| Candidate | Id, Kind, SourceId, ExternalId?, SourceUrl (as fetched, immutable), Fingerprint, ContentHash, Fields (jsonb `CandidateFields`; `fields.url` is the editable URL used on publish), Evidence (text ≤ 64 KB), Status (`Pending`/`Rejected`/`Published`/`Merged`), Validation (`Valid`/`NeedsReview`/`Rejected`), Issues (jsonb), SuggestedCategoryId?, DuplicateOfKind?, DuplicateOfId?, DuplicateOfIsCandidate?, MatchedItemId?, PublishedItemId?, RejectionReason?, RejectedByUserId?, IsSynthetic, Ai* (Model, RequestId, Confidence, ProcessedAt, SuggestedFields[]), FetchedAt, CreatedAt, UpdatedAt | indexes: Fingerprint, (Status, Validation), SourceId |
| Event | Id, Title, Description?, VenueId?, CityId, CategoryId?, StartsAt, EndsAt?, PriceText?, PriceAmount?, Currency?, ImageUrl?, Status (`Published`/`Expired`/`Withdrawn`), PrimarySourceId, SourceUrl, Fingerprint, IsSynthetic, SearchText, LastVerifiedAt, PublishedAt, CreatedAt, UpdatedAt | indexes: StartsAt, CityId, CategoryId, Status, PrimarySourceId, Fingerprint |
| Deal | Id, Title, Description?, MerchantName?, CityId?, CategoryId?, OldPrice?, NewPrice?, PriceText?, Currency?, DiscountPercent?, ValidFrom?, ValidUntil?, ImageUrl?, Status, PrimarySourceId, SourceUrl, Fingerprint, IsSynthetic, SearchText, LastVerifiedAt, PublishedAt, CreatedAt, UpdatedAt | indexes: ValidUntil, Status, PrimarySourceId, Fingerprint |
| ItemSource | Id, ItemKind, ItemId, SourceId, SourceUrl, Fingerprint, CandidateId, AddedAt | all sources of a published item (merge adds rows) |
| AuditLog | Id, UserId, Action, EntityType, EntityId, Changes (jsonb `{ before, after }`), At | append-only |
| JobExecution | Id, JobName (`SourceFetch`/`Expiration`), SourceId?, Trigger (`Schedule`/`Manual`), StartedAt, FinishedAt?, Status (`Running`/`Succeeded`/`Failed`), Received, Created, Updated, Unchanged, Failed, ErrorCode?, Error?, CorrelationId | run history |
| AiCall | Id, CandidateId, Provider, Model, RequestId?, Success, ErrorCode?, Confidence?, InputTokens?, OutputTokens?, StartedAt, DurationMs | cost/audit |

Money `decimal(18,2)`, currency ISO code (never assumed). Times `DateTimeOffset` UTC.

**Domain rules**

- **Effective end** of an event = `EndsAt` ?? `StartsAt + 6 h` (internal, never displayed; ADR-010).
- **Active event**: `Status = Published` and effective end > now. **Active deal**: `Status = Published` and (`ValidUntil` > now, or `ValidUntil` is null and `LastVerifiedAt` > now − 7 days). A published deal that is not active is shown as `expired` on detail.
- **Freshness**: `fresh` if `LastVerifiedAt` ≥ now − 48 h, else `stale`.
- **Discount**: `round((old − new) / old × 100)` (midpoint away from zero), only when both prices are present and old > new.
- **Normalization for matching/search**: lower-case, Albanian/Latin diacritics folded (ë→e, ç→c, č/ć→c, š→s, ž→z, đ→d), punctuation removed, whitespace collapsed. `SearchText` = normalized title + description + venue name.
- **Title similarity**: Jaccard similarity of normalized word sets (words ≥ 2 chars); threshold 0.8.
- **City matching**: normalized city text equals a city's normalized name, slug or alias.
- **Duplicate rules (REQ-050)**: compared against published items (status Published) of any source except the candidate's own `matchedItemId`, and against `Pending` candidates of other sources; same kind; events need equal non-null `citySlug` and equal Europe/Pristina start date; deals need equal `citySlug` (both null counts as equal); title similarity ≥ 0.8. First match by highest similarity is referenced.

**Validation issue codes** (`severity`): `MissingTitle` (rejected), `InvalidUrl` (rejected), `Expired` (rejected), `MissingStartsAt` (review), `InvalidDateRange` (review), `UnknownCity` (review), `MissingPrice` (review), `PriceInconsistent` (review), `ConflictingData` (review), `AiUnavailable` (review), `PossibleDuplicate` (review), `ChangedSinceApproval` (review), `SourceUnreliable` (review), `InvalidImageUrl` (info — image set to null). Any rejected → `Rejected`; else any review → `NeedsReview`; else `Valid`. Auto-rejected candidates (`RejectedByUserId = null`) stay editable; re-validation may make them `Pending`. Rejected by a person is final (REQ-067).

**Seeded cities** (slug — name — aliases): prishtine — Prishtinë — Pristina, Prishtina, Priština · prizren — Prizren · peje — Pejë — Peja, Peć · gjakove — Gjakovë — Gjakova, Đakovica · mitrovice — Mitrovicë — Mitrovica · mitrovice-veriore — Mitrovicë e Veriut — North Mitrovica · ferizaj — Ferizaj — Uroševac · gjilan — Gjilan — Gnjilane · podujeve — Podujevë — Podujeva · vushtrri — Vushtrri · suhareke — Suharekë — Suhareka · rahovec — Rahovec · drenas — Drenas — Gllogoc · lipjan — Lipjan · malisheve — Malishevë · kamenice — Kamenicë · viti — Viti · decan — Deçan · istog — Istog · kline — Klinë · skenderaj — Skenderaj · dragash — Dragash · fushe-kosove — Fushë Kosovë · kacanik — Kaçanik · shtime — Shtime · obiliq — Obiliq · novoberde — Novobërdë · gracanice — Graçanicë · hani-i-elezit — Hani i Elezit · junik — Junik · kllokot — Kllokot · mamushe — Mamushë · partesh — Partesh · ranillug — Ranillug · shterpce — Shtërpcë · zubin-potok — Zubin Potok · zvecan — Zveçan · leposaviq — Leposaviq.

**Seeded categories** — Event: `muzike` Muzikë & koncerte · `teater` Teatër & film · `art` Art & ekspozita · `sport` Sport · `familje` Familje & fëmijë · `ushqim` Ushqim & pije · `nate` Jeta e natës · `edukim` Konferenca & edukim. Deal: `teknologji` Teknologji · `ushqim` Ushqim & pije · `mode` Modë · `shtepi` Shtëpi · `sherbime` Shërbime · `udhetime` Udhëtime.

## 5. API contracts

All endpoints are under `/api` unless stated, JSON, camelCase. **Enums are camelCase strings** (`needsReview`, `jsonFeed`, `admin`); **issue codes and run error codes are PascalCase strings** exactly as listed (`MissingTitle`, `BlockedAddress`).
Errors are RFC 9457 problem details. Validation errors: 400 with `errors: { field: [messages] }` (query parameters use their parameter name).
All ids are UUIDs; malformed or unknown id → 404. Lists: `?page=1&pageSize=20` (1–100) → `Page<T> = { items: T[], page, pageSize, totalCount }`; a page beyond the last → 200 with empty `items`.
Times are ISO-8601 UTC (`2026-10-03T18:00:00Z`); `from`/`to` are `yyyy-MM-dd` dates interpreted in Europe/Pristina.

### Platform

| Method | Path | Auth | Request | Responses | Req |
|---|---|---|---|---|---|
| POST | /api/auth/login | anonymous; > 10 failed attempts/min per (client IP, email) → 429 | `{ email, password }` | 200 + auth cookie, body `MeDto`; 401 generic (also for inactive users); 429 | P01, P02, P06 |
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
EventSummaryDto = { id, title, startsAt, endsAt|null, city: CityDto, venueName|null, category: CategoryDto|null,
                    priceText|null, priceAmount|null, currency|null, imageUrl|null,
                    sourceName, sourceUrl, lastVerifiedAt, freshness: "fresh"|"stale",
                    status: "active"|"expired", isSynthetic }
EventDetailDto  = EventSummaryDto + { description|null, sources: SourceRefDto[] }   // primary source first
DealSummaryDto  = { id, title, merchantName|null, city: CityDto|null, category: CategoryDto|null,
                    oldPrice|null, newPrice|null, priceText|null, currency|null, discountPercent|null,
                    validFrom|null, validUntil|null, imageUrl|null,
                    sourceName, sourceUrl, lastVerifiedAt, freshness, status: "active"|"expired", isSynthetic }
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
| GET | /events/{id}, /deals/{id} | 200 `text/html`: the SPA `index.html` (or a minimal built-in shell when no build is present) with `<title>{title} · Kosova Radar</title>`, `<meta name="description">`, `og:title`, `og:description` (description truncated to 200 chars, else "{dd.MM.yyyy, HH:mm} · {city}" for events / "{merchant} · Vlen deri më {date}" for deals), `og:url` (`Site__BaseUrl` + path), `og:image` only when https; events also `<script type="application/ld+json">` schema.org `Event` (name, startDate, endDate?, location: Place {name: venueName ?? city name, address.addressLocality: city name}, url). Synthetic items add `<meta name="robots" content="noindex">`. Unknown/non-public id → the plain shell with status 404. Encoding: `<title>`/`<meta content>` via `HtmlEncoder.Default`; JSON-LD via System.Text.Json default encoder (escapes `<`, `>`, `&`, `'`, so `</script>` cannot appear). | 090 |
| GET | /sitemap.xml | 200 `application/xml`, `<urlset>` with `{base}/`, `{base}/events`, `{base}/deals` and one `<url><loc>{base}/events/{id}` or `{base}/deals/{id}` per active, non-synthetic published item | 091 |
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
SourceRequest = { name (1–120), type: "fixture"|"jsonFeed"|"rss",
                  baseUrl|null (absolute http/https, ≤ 2000; required unless fixture),
                  fixturePayload|null (string ≤ 1 MB; required for fixture; not validated as JSON on save),
                  defaultKind: "event"|"deal" (default "event"; used by rss),
                  pollIntervalMinutes (fixture: 1–1440; others: 15–1440),
                  isActive (bool), isUnreliable (bool) }
SourceDto = { id, name, type, baseUrl|null, defaultKind, isActive, isUnreliable, pollIntervalMinutes,
              health: "never"|"healthy"|"degraded"|"failing"|"disabled",
              lastFetchedAt|null, lastSuccessAt|null, lastErrorAt|null, lastError|null, consecutiveFailures }
SourceDetailDto = SourceDto + { fixturePayload|null, recentRuns: RunDto[] }
RunDto = { id, sourceId, trigger: "schedule"|"manual", status: "running"|"succeeded"|"failed", startedAt, finishedAt|null,
           received, created, updated, unchanged, failed, errorCode|null, error|null }
```

`type: "fixture"` when `Ingestion__AllowFixtureSources=false` → 400 on `type`.
Run error codes: `Unreachable`, `Timeout`, `HttpStatus` (non-2xx), `MalformedPayload`, `BlockedAddress` (REQ-020), `RobotsDisallowed`, `TooLarge` (> 5 MB), `Interrupted`.

### Admin — candidates (policy `Reviewer`)

| Method | Path | Request | Responses | Req |
|---|---|---|---|---|
| GET | /api/admin/candidates | `?status=pending\|rejected\|published\|merged&validation=valid\|needsReview\|rejected&kind=event\|deal&sourceId=&page=&pageSize=` | 200 `Page<CandidateSummaryDto>` (newest `fetchedAt` first, then id); 400 | 060 |
| GET | /api/admin/candidates/{id} | — | 200 `CandidateDto`; 404 | 061 |
| PUT | /api/admin/candidates/{id} | `CandidateFieldsRequest` | 200 `CandidateDto` (re-validated); 400; 404; 409 | 065, 067 |
| POST | /api/admin/candidates/{id}/approve | — | 200 `{ kind, itemId }`; 400 (missing fields named, or `validation` when rejected); 404; 409 | 062, 063, 067, 068 |
| POST | /api/admin/candidates/{id}/reject | `{ reason }` (1–500 after trim) | 200 `CandidateDto`; 400 on `reason`; 404; 409 | 064, 067, 074 |
| POST | /api/admin/candidates/{id}/merge | `{ targetId }` (published item of the same kind) | 200 `CandidateDto`; 400 on `targetId`; 404; 409 | 066, 067, 075 |

```
IssueDto = { code, severity: "rejected"|"review"|"info", field|null, message }
CandidateFields = { title|null, description|null, url|null, imageUrl|null, cityText|null, citySlug|null,
                    venueName|null, categoryText|null, categorySlug|null,
                    startsAt|null, endsAt|null, priceText|null, priceAmount|null, currency|null,
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

Approve: category = `fields.categorySlug ?? suggestedCategory.slug`. Required — 400 keys: `title`, `url` (all kinds); `citySlug`, `startsAt` (events); `newPrice` when a deal has neither `newPrice` nor `priceText`. Validation `Rejected` → 400 on `validation`.

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
  actions: candidate.approve, candidate.reject, candidate.edit, candidate.merge, item.edit, item.expire,
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
    "priceText": "string", "price": 12.5, "currency": "EUR",
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

Site-specific adapters and an iCalendar adapter are added only for sources the client names and clears (ADR-006).

### HTTP fetching rules

- User-Agent `KosovaRadarBot/1.0 (+{Site__BaseUrl}/bot)`; `UseProxy=false`; max 5 redirects; response limit 5 MB (`TooLarge`).
- SSRF guard (REQ-020) runs in `SocketsHttpHandler.ConnectCallback` on the IP actually connected (defeats DNS rebinding; covers redirects). Blocked: loopback, 0.0.0.0/8, RFC 1918, 100.64/10, 169.254/16, 192.0.0/24, 198.18/15, multicast/broadcast, `::`, `::1`, fc00::/7, fe80::/10, and IPv4-mapped/compatible IPv6 forms of these.
- robots.txt is fetched through the same guarded client (cached 24 h). The guard is checked first, so a blocked host yields `BlockedAddress`. Robots network failure → `Unreachable`/`Timeout`; robots 4xx → allow all; robots 5xx or a matching `Disallow` for our agent or `*` → `RobotsDisallowed`.
- One request per host per `Ingestion__PerHostDelaySeconds`.

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
- Every call is stored in `AiCall`. Timeout 15 s. Daily limit `Ai__DailyCallLimit` counted from `AiCall` rows of the current UTC day.

**Fake provider (Development/Testing, deterministic — acceptance tests rely on it):**
- If `text` contains `#ai-fail` → throws (provider failure). If it contains `#ai-invalid` → returns output that violates the schema.
- `startsAt` = first ISO-8601 date-time with offset in `text` (e.g. `2026-10-20T20:00:00+02:00`), else null.
- `priceAmount` and `currency: "EUR"` = first match of `(\d+(?:[.,]\d{1,2})?)\s?€` (decimal comma accepted), else null. For deals the same value fills `newPrice`.
- `city` = seeded city name, slug or alias appearing as whole words in the folded `text`; the longest match wins (e.g. "Mitrovicë e Veriut" over "Mitrovicë"), then the earliest position; else null.
- `categorySlug`: event kind — `muzike` if text contains "koncert" or "muzik", `teater` if "teat" or "film", `sport` if "ndeshje" or "sport"; deal kind — `teknologji` if "laptop" or "telefon"; else null.
- `model = "fake-1"`, `requestId = "fake-" + guid`, `confidence = 0.9`.

## 7. UI map

All UI text is Albanian (ADR-009). Times shown in Europe/Pristina, format `dd.MM.yyyy, HH:mm`.
Every list has a loading state (`role="status"`, "Duke u ngarkuar…"), an empty state ("Nuk u gjet asnjë rezultat")
and an error state ("Diçka shkoi keq." + button "Provo sërish"). Cards are `<article>` elements whose heading is the item title.
List pages keep filters in the query string (`/?search=`, `/events?city=&category=&from=&to=&search=`, `/deals?city=&category=&search=`, `/search?q=`)
and show a button "Shfaq më shumë" while more pages exist. Public and admin navigation stay visible at 360 px (no hamburger menu).
Titles, descriptions and evidence are rendered as text (never `dangerouslySetInnerHTML`); `href` only for http(s) URLs.

### Public

| Route | Purpose | Key elements (accessible names used by tests) | Req |
|---|---|---|---|
| (all public) | layout | header link "Kosova Radar" (home); nav links "Evente", "Oferta", "Kërko" | — |
| / | feed | `h1` "Çfarë ka sot në Kosovë"; search input "Kërko" (filters both tabs); tablist with tabs "Sot", "Evente", "Oferta"; the selected tab panel lists cards | 100, 104, 108 |
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
| /admin/review | candidate queue | `h1` "Radha e shqyrtimit"; selects "Statusi" (default "Në pritje"; options "Të gjitha", "Në pritje", "Refuzuar", "Publikuar", "Bashkuar"), "Validimi", "Lloji", "Burimi"; table with column headers "Titulli", "Burimi", "Validimi", "Statusi"; rows with a link named by candidate title (or "(pa titull)"), validation badge ("Valid", "Kërkon shqyrtim", "Refuzuar") and status cell ("Në pritje", "Refuzuar", "Publikuar", "Bashkuar"); filters in the query string | 060, 112 |
| /admin/items/:id | candidate review | `h1` candidate title or "(pa titull)"; region "Evidenca nga burimi" (evidence text + link "Hap burimin"); region "Fushat e normalizuara" (side by side ≥ 1024 px, stacked below) with inputs "Titulli", "Përshkrimi", "Lidhja e burimit", "Qyteti", "Vendi", "Kategoria", "Fillon më", "Përfundon më", "Çmimi (tekst)", "Çmimi", "Monedha", and for deals "Tregtari", "Çmimi i vjetër", "Çmimi i ri", "Vlen nga", "Vlen deri"; list "Problemet e validimit"; buttons "Ruaj ndryshimet", "Aprovo", "Refuzo", "Bashko"; reject dialog with textarea "Arsyeja" and button "Konfirmo refuzimin"; merge dialog with the suggested duplicate and button "Bashko me këtë"; after approve status message "U publikua" and link "Shiko faqen publike" | 110–112 |
| /admin/published | published items | `h1` "Të publikuarat"; filters "Lloji", "Statusi"; rows with buttons "Ndrysho", "Skado", "Tërhiq" | 069–071 |
| /admin/published/:kind/:id | edit published item | same field labels as candidate review; button "Ruaj ndryshimet" | 069 |
| /admin/sources | sources | `h1` "Burimet"; button "Shto burim" opens form with "Emri", "Lloji", "URL", "Të dhënat testuese (JSON)", "Lloji i paracaktuar", "Intervali (minuta)", checkboxes "Aktiv", "I pabesueshëm", button "Ruaj burimin"; table rows: name, health badge ("Pa u marrë ende", "Në rregull", "Me probleme", "Dështon", "Joaktiv"), last success, last error, buttons "Merr tani" and "Ndrysho"; after "Merr tani" a status message "Marrja përfundoi: {created} të reja, {updated} të përditësuara, {unchanged} pa ndryshim, {failed} të dështuara", "Marrja dështoi: {errorCode}" or (409) "Një marrje është në vazhdim. Provo pas pak." | 113 |
| /admin/users | users | `h1` "Përdoruesit"; form inputs "Email", "Fjalëkalimi", select "Roli" ("Administrator", "Shqyrtues"); button "Krijo përdoruesin"; rows with button "Çaktivizo" | 001–004 |

Reviewer role: Admin-only links hidden; direct navigation shows "Nuk keni leje për këtë faqe".

## 8. Integrations

| Service | Purpose | Status |
|---|---|---|
| Anthropic Claude API (Messages, structured outputs) | extraction/classification (REQ-040..043) | built behind `IAiProvider`; needs `Ai__ApiKey` for production; Fake in dev/test |
| Public source sites (Radar JSON / RSS) | content | generic adapters; concrete sources named and cleared by the client (ADR-006) |
| Payments | — | not in V1 |
| Email | — | not in V1 (source failures surface on the dashboard) |

## 9. Non-functional notes

- Logging: structured; scopes `CorrelationId`, `RunId`, `SourceId`; never log passwords, cookies, API keys or full payloads.
- Metrics (V1 = queryable from `JobExecution`, `AiCall`, items; the dashboard shows the counts in §5): fetch success rate, run duration, AI error rate, candidate→publish rate, stale count; API latency from request logging.
- Migrations: EF Core migrations in Infrastructure; applied automatically in Development/Testing, via a deploy step in Production.
- Seed: cities and categories in every environment (idempotent); synthetic fixture source "Fixture — Shembuj sintetikë" (inactive; the seed runs one fetch of it at startup when it has never been fetched) with 24 items covering missing time, missing price, duplicate title, changed time, expired event, invalid URL, non-Kosovo location, conflicting data, AI failure — only when `Seed__SyntheticFixtures=true`.
- Frontend: route-level code splitting (admin bundle lazy-loaded), system font stack plus one subset display font, animations ≤ 300 ms, `prefers-reduced-motion` respected, calm editorial visual language (generous whitespace, strong typographic hierarchy).
- Config: environment variables only; keys documented in `.env.example`.
