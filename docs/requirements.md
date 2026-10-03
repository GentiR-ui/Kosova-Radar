# Requirements — Kosova Radar (V1 private beta)

> WHAT the product must do. Every requirement is one testable EARS sentence with an id.
> LOCKED after `scripts/approve-spec.sh`; only the human can amend it.
> If a requirement can't be turned into a failing test, it is a wish — rewrite it or tag it `[manual]`.
> Source documents: `Kosova_Radar_V1_Claude_Implementation_Spec.pdf` (authoritative for V1) and
> `Kosova_Radar_Master_Roadmap.pdf` (vision, later versions). Deviations from them are ADRs in docs/DECISIONS.md.

## 1. Product

- **Problem:** People in Kosovo learn about events and deals from scattered venue pages, feeds and word of mouth; the information is often incomplete or out of date, and nobody shows which items are still valid and where they come from.
- **Target users:** (a) the public in Kosovo looking for what is on today, this week and which deals are worth it; (b) the Kosova Radar editorial team (Admin, Reviewer) who curate the feed.
- **Value proposition:** One trustworthy feed of Kosovo events and deals in which every item links to its source, shows when it was last verified, and disappears automatically when it has ended — collected automatically, approved by a human.
- **Success signal for the market test:** During a 4-week controlled beta: at least 3 active sources ingested on schedule with ≥ 95 % successful fetches, ≥ 150 published items, zero expired items shown as active, and the beta client's reviewers handle the daily queue in under 30 minutes.

## 2. Scope

**In (V1):**
1. Source registry with scheduled, rate-limited, fault-isolated fetching (generic Radar JSON feed and RSS/Atom adapters, plus a synthetic fixture adapter for development and tests).
2. Ingestion pipeline: parse → normalize → optional AI extraction/classification → validation → duplicate detection → candidate.
3. Human review: approve, reject with reason, edit, merge duplicates, with the source evidence beside the extracted fields; every admin action audited.
4. Public feed (Albanian UI, mobile first): events and deals lists with city/category/date/search filters, detail pages, unified search, source link and freshness label on every item; expired items leave active lists automatically.
5. Admin: dashboard, review queue, source health and configuration, published-item management, user management (Admin/Reviewer).
6. SEO basics for shareable detail pages (server-side title/Open Graph/JSON-LD), sitemap and robots.txt.

**Out (explicitly not now):** public user accounts, saved items, notifications/alerts (email, push), automatic publishing without human approval, price-drop monitoring, merchant/venue portal and self-submission, social-media scraping, image re-hosting, tags, iCalendar adapter, password change/reset, English or Serbian UI, PWA/native apps, multi-country support, Hangfire/queues, multi-instance deployment, site-specific scrapers for sources the client has not yet named and cleared (ADR-006).

## 3. EARS patterns

| Pattern | Template |
|---|---|
| Event | WHEN <trigger> THE SYSTEM SHALL <response> |
| State | WHILE <state> THE SYSTEM SHALL <response> |
| Unwanted | IF <bad condition> THEN THE SYSTEM SHALL <response> |
| Optional | WHERE <feature is enabled> THE SYSTEM SHALL <response> |
| Ubiquitous | THE SYSTEM SHALL <response> |

**Naming rule:** values in backticks name enum members; on the wire they are camelCase (`Pending` → `pending`, `NeedsReview` → `needsReview`, `JsonFeed` → `jsonFeed`, `Admin` → `admin`). Validation issue codes and run error codes are PascalCase strings exactly as written (`MissingTitle`, `BlockedAddress`). "Now" means the clock defined in design.md §2 (Test hooks). "Europe/Pristina time" means Kosovo local time, implemented with the IANA zone `Europe/Belgrade` (Kosovo has no IANA zone of its own; same rules).

## 4. Platform requirements

> Adapted from the starter's multi-tenant baseline: Kosova Radar is a single-operator content platform with
> anonymous public read access and role-based admin access (ADR-001). There is no public registration.

- **REQ-P01** WHEN the application starts and no user exists THE SYSTEM SHALL create an active user with role `Admin` from the bootstrap configuration, so that this user can sign in (in Development and Testing with the credentials listed in design.md §3).
- **REQ-P02** IF login credentials are invalid THEN THE SYSTEM SHALL respond 401 with the same generic message whether or not the email exists.
- **REQ-P03** IF an unauthenticated request targets any `/api/admin/**` endpoint or `/api/me` THEN THE SYSTEM SHALL respond 401.
- **REQ-P04** IF a signed-in `Reviewer` calls an Admin-only endpoint (sources and users, design.md §5) THEN THE SYSTEM SHALL respond 403 and SHALL leave the data unchanged.
- **REQ-P05** IF a request body or query fails validation THEN THE SYSTEM SHALL respond 400 with RFC 9457 problem details that name each invalid field.
- **REQ-P06** IF more than 10 failed login attempts for the same email arrive from the same client IP within one minute THEN THE SYSTEM SHALL respond 429 to further login attempts for that email from that IP until the minute has passed.
- **REQ-P07** WHEN `GET /health` is called and the database is reachable THE SYSTEM SHALL respond 200.
- **REQ-P08** THE SYSTEM SHALL send the headers `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin` and the `Content-Security-Policy` given in design.md §3 on every response.
- **REQ-P09** IF an unhandled error occurs THEN THE SYSTEM SHALL respond 500 with problem details that contain no stack trace or internal exception text.
- **REQ-P10** WHEN an admin signs in through the web app at `/admin/login` THE SYSTEM SHALL show the admin dashboard with the heading "Paneli", on desktop and on a phone-sized screen.
- **REQ-P11** WHEN a signed-in user logs out THE SYSTEM SHALL end the session so that subsequent `/api/me` and `/api/admin/**` requests respond 401.

## 5. Product requirements

### Users and roles

- **REQ-001** WHEN an Admin creates a user with a unique email, a password of at least 12 characters and the role `Admin` or `Reviewer` THE SYSTEM SHALL create the active user so that they can sign in with that role.
- **REQ-002** IF an Admin creates a user whose email already exists THEN THE SYSTEM SHALL respond 409 and SHALL NOT create a second user.
- **REQ-003** WHEN an Admin deactivates a user THE SYSTEM SHALL reject that user's subsequent logins with the generic 401 and SHALL make that user's existing session respond 401 on its next `/api/admin/**` request.
- **REQ-004** IF an Admin tries to deactivate their own account THEN THE SYSTEM SHALL respond 400 and SHALL leave the account active.

### Sources and fetching

- **REQ-010** WHEN an Admin creates a source with a name, a type (`Fixture`, `JsonFeed`, `Rss`), a poll interval in range and the type's location (`fixturePayload` for `Fixture`, an absolute http(s) `baseUrl` otherwise) THE SYSTEM SHALL save it and return it with health `never` if it is active.
- **REQ-011** IF a source is created or updated with a missing name, an unknown type, a poll interval outside the allowed range of its type (design.md §5), or a missing/invalid location for its type THEN THE SYSTEM SHALL respond 400 naming each invalid field.
- **REQ-012** WHILE a source is active THE SYSTEM SHALL fetch it automatically whenever it has never been fetched or its last fetch is at least its poll interval old, without any manual trigger.
- **REQ-013** WHILE a source is inactive THE SYSTEM SHALL NOT fetch it automatically and SHALL report its health as `disabled`.
- **REQ-014** WHEN an Admin triggers "fetch now" for a source (active or inactive) THE SYSTEM SHALL fetch and process it immediately and return a run summary with the counts `received`, `created`, `updated`, `unchanged` and `failed` as defined in design.md §2.
- **REQ-015** WHEN a source is fetched again and an item's content is unchanged THE SYSTEM SHALL NOT create another candidate for it and SHALL count it as `unchanged`.
- **REQ-016** IF fetching a source fails (for example unreachable host, malformed payload or blocked address) THEN THE SYSTEM SHALL record the run as failed with an error code, set the source's `lastErrorAt` and error message, increase its consecutive-failure count and, while the source is active, report health `degraded` (or `failing` from the third consecutive failure).
- **REQ-017** IF one source fails during fetching THEN THE SYSTEM SHALL still fetch and process every other due source, and SHALL keep serving the public API.
- **REQ-018** IF a single item inside an otherwise valid feed cannot be parsed THEN THE SYSTEM SHALL count it as `failed` and SHALL still process the other items of that feed.
- **REQ-019** WHEN a fetch of a source succeeds THE SYSTEM SHALL set its `lastSuccessAt`, reset its consecutive-failure count to 0 and, while the source is active, report health `healthy`.
- **REQ-020** IF a source's URL resolves to a loopback, private, link-local or otherwise non-public network address THEN THE SYSTEM SHALL NOT send the request and SHALL fail the run with error code `BlockedAddress`.
- **REQ-021** WHERE a source is of type `Fixture` THE SYSTEM SHALL mark every candidate and published item from it as synthetic (`isSynthetic: true`).

### Normalization and validation

- **REQ-027** IF a deal candidate has neither a new price nor a price text THEN THE SYSTEM SHALL add issue `MissingPrice` and set validation `NeedsReview`.
- **REQ-028** WHEN validation of a candidate completes THE SYSTEM SHALL set its status to `Pending` (awaiting a person) unless the validation is `Rejected`, in which case it SHALL set status `Rejected` without a reviewer.
- **REQ-029** IF an event's `endsAt` is before its `startsAt` or a deal's `validUntil` is before its `validFrom` THEN THE SYSTEM SHALL set validation `NeedsReview` with issue `InvalidDateRange`.
- **REQ-030** WHEN a fetched item is normalized THE SYSTEM SHALL keep every field the source does not provide as null (no default price, time, city, venue or image is ever invented) and SHALL store the original item text as source evidence.
- **REQ-031** IF a candidate has no title or its URL is not an absolute http(s) URL THEN THE SYSTEM SHALL set its validation to `Rejected` with issue `MissingTitle` or `InvalidUrl`.
- **REQ-032** IF an event candidate has already ended (its effective end, design.md §4, is in the past) or a deal candidate's `validUntil` is in the past THEN THE SYSTEM SHALL set its validation to `Rejected` with issue `Expired`.
- **REQ-033** IF an event candidate has no start time THEN THE SYSTEM SHALL set its validation to `NeedsReview` with issue `MissingStartsAt`.
- **REQ-034** IF an event candidate has no city, or any candidate's stated city does not match a Kosovo municipality or one of its known aliases (design.md §4), THEN THE SYSTEM SHALL set its validation to `NeedsReview` with issue `UnknownCity` and SHALL leave its resolved city (`citySlug`) null.
- **REQ-035** IF a deal candidate has a negative price or an old price that is not greater than its new price THEN THE SYSTEM SHALL add issue `PriceInconsistent` and set validation `NeedsReview`.
- **REQ-036** WHEN a deal has both prices and the old price is greater than the new price THE SYSTEM SHALL set `discountPercent` = round((old − new) / old × 100), midpoint away from zero.
- **REQ-037** WHEN a candidate has no issue of severity `rejected` or `review` THE SYSTEM SHALL set its validation to `Valid`.
- **REQ-038** WHERE a source is marked unreliable THE SYSTEM SHALL add issue `SourceUnreliable` and set validation `NeedsReview` for every new candidate from it.
- **REQ-039** IF a deal's stated discount differs from the computed `discountPercent` by more than 1 percentage point THEN THE SYSTEM SHALL add issue `ConflictingData` and set validation `NeedsReview`.

### AI extraction and classification

- **REQ-040** WHEN a candidate has unstructured `text` and the AI provider is enabled THE SYSTEM SHALL use the AI only to fill fields that are still null, list the AI-filled field names in `ai.suggestedFields`, and record the model name, request id, confidence and processing timestamp (`ai.processedAt`) on the candidate.
- **REQ-041** IF the AI provider fails, times out, returns output that does not match the extraction schema, or the daily AI call limit is reached THEN THE SYSTEM SHALL set the candidate's validation to `NeedsReview` with issue `AiUnavailable` and SHALL leave every field the source did not provide null.
- **REQ-042** IF an AI-extracted value differs from a value the source states explicitly THEN THE SYSTEM SHALL keep the source's value, add issue `ConflictingData` and set validation `NeedsReview`.
- **REQ-043** WHEN the AI suggests a category that exists for the candidate's kind THE SYSTEM SHALL set it as the candidate's `suggestedCategory` without changing a category the source states.

### Duplicates and changes

- **REQ-050** IF a new candidate matches, under the duplicate rules of design.md §4 (same kind, same city, for events the same Europe/Pristina start date, normalized title similarity ≥ 0.8), a published item (any source) or a pending candidate from a different source THEN THE SYSTEM SHALL add issue `PossibleDuplicate`, reference it in `duplicateOf`, and set validation `NeedsReview`.
- **REQ-051** WHEN a source item that was already published is fetched again with changed content THE SYSTEM SHALL create a candidate linked to the published item (`matchedItemId`) with issue `ChangedSinceApproval`, and SHALL leave the public item unchanged until that candidate is approved.
- **REQ-052** WHEN a source item that was already published is fetched again with unchanged content THE SYSTEM SHALL update the published item's `lastVerifiedAt` to the fetch time.
- **REQ-053** WHEN a still-pending candidate's source item is fetched again with changed content THE SYSTEM SHALL update that candidate in place (no second candidate) and re-run validation.

### Review and publishing

- **REQ-060** WHEN a Reviewer or Admin lists candidates THE SYSTEM SHALL return them paginated, filterable by status, validation, kind and source, each with its validation issues.
- **REQ-061** WHEN a Reviewer or Admin opens a candidate THE SYSTEM SHALL return its normalized fields together with the unchanged source evidence, source name and source URL.
- **REQ-062** WHEN a Reviewer or Admin approves a pending candidate whose required fields are present (title and URL; for events also a known city and a start time; for deals also a new price or a price text) THE SYSTEM SHALL publish it as an event or deal visible in the public API, with `lastVerifiedAt` set to the candidate's fetch time, and return the published item id.
- **REQ-063** IF a candidate without its required fields, or with validation `Rejected`, is approved THEN THE SYSTEM SHALL respond 400 naming the missing or invalid fields and SHALL NOT publish it.
- **REQ-064** WHEN a Reviewer or Admin rejects a pending candidate with a reason of 1–500 characters THE SYSTEM SHALL set its status to `Rejected` and store the reason and the reviewer.
- **REQ-065** WHEN a Reviewer or Admin edits a pending or auto-rejected candidate's fields THE SYSTEM SHALL save the new normalized fields, keep the original source evidence unchanged, and re-run validation.
- **REQ-066** WHEN a Reviewer or Admin merges a pending candidate into a published item of the same kind THE SYSTEM SHALL set the candidate's status to `Merged`, add its source to the item's sources, refresh the item's `lastVerifiedAt`, and create no new public item.
- **REQ-067** IF approve, reject, edit or merge targets a candidate that is already `Published`, `Merged` or rejected by a person THEN THE SYSTEM SHALL respond 409 and leave it unchanged.
- **REQ-068** WHEN a `ChangedSinceApproval` candidate is approved THE SYSTEM SHALL apply its fields to the linked published item, keeping the same item id.
- **REQ-069** WHEN a Reviewer or Admin edits a published event or deal THE SYSTEM SHALL save the change and the public API SHALL return the new values.
- **REQ-070** WHEN a Reviewer or Admin expires a published item THE SYSTEM SHALL remove it from public lists and search and SHALL return it from its public detail endpoint with status `expired`.
- **REQ-071** WHEN a Reviewer or Admin withdraws a published item THE SYSTEM SHALL remove it from public lists and search and its public detail endpoint SHALL respond 404.
- **REQ-072** WHEN a user performs an audited action (design.md §5: candidate approve/reject/edit/merge, item edit/expire/withdraw, source create/update/manual fetch, user create/deactivate) THE SYSTEM SHALL write an audit entry with the acting user, action, entity type, entity id, UTC time and the changed values, retrievable through the audit endpoint filtered by entity id.
- **REQ-073** WHEN an Admin marks a source as unreliable THE SYSTEM SHALL show `isUnreliable: true` for it in the source list.
- **REQ-074** IF a candidate is rejected without a reason or with a reason longer than 500 characters THEN THE SYSTEM SHALL respond 400 on `reason` and leave the candidate unchanged.
- **REQ-075** IF a merge targets an item of a different kind, an item that is not published, or an unknown id THEN THE SYSTEM SHALL respond 400 on `targetId` and leave the candidate unchanged.

### Public API

- **REQ-079** WHEN a deal excluded by REQ-087 is requested by id THE SYSTEM SHALL return it with status `expired`.
- **REQ-080** WHEN anyone lists events THE SYSTEM SHALL return only published, non-expired, non-withdrawn events ordered by start time ascending, paginated (default page size 20, maximum 100) with `totalCount`.
- **REQ-081** WHEN events are listed with `city`, `category`, `from`, `to` (Europe/Pristina dates) or `search` THE SYSTEM SHALL return only events matching all given filters, where `search` matches title, description or venue case-insensitively and ignoring Albanian diacritics (ë = e, ç = c).
- **REQ-082** IF a list request has an unknown `city` or `category` slug, `from` after `to`, a page below 1 or a page size outside 1–100 THEN THE SYSTEM SHALL respond 400 naming the invalid parameter.
- **REQ-083** WHEN anyone lists deals THE SYSTEM SHALL return only published, non-expired, non-withdrawn deals ordered by `validUntil` ascending with deals without `validUntil` last, paginated, filterable by `city`, `category` and `search` with the same rules as events.
- **REQ-084** THE SYSTEM SHALL include in every public event and deal (list and detail) the source name, source URL, `lastVerifiedAt` and a `freshness` of `fresh` (verified within the last 48 hours) or `stale`.
- **REQ-085** WHEN anyone requests a published event or deal by id THE SYSTEM SHALL return its detail including all its sources; IF the id is unknown, not yet published, rejected or withdrawn THEN THE SYSTEM SHALL respond 404.
- **REQ-086** WHILE a published event's effective end or a published deal's `validUntil` is in the past THE SYSTEM SHALL exclude it from public lists and search and SHALL return it from its detail endpoint with status `expired`.
- **REQ-087** WHILE a published deal has no `validUntil` and was last verified more than 7 days ago THE SYSTEM SHALL exclude it from public lists and search.
- **REQ-088** WHEN anyone requests cities or categories THE SYSTEM SHALL return the Kosovo municipalities and the categories (filterable by kind) with their slugs and Albanian names.
- **REQ-089** WHEN anyone searches with a query of at least 2 characters THE SYSTEM SHALL return matching active events and deals together, each with its kind; IF the query is shorter THEN THE SYSTEM SHALL respond 400 on `q`.

### SEO

- **REQ-090** WHEN a client requests the page `/events/{id}` or `/deals/{id}` of a published item THE SYSTEM SHALL return HTML whose `<title>` contains the item title and which contains `og:title`, `og:description` and `og:url` meta tags, and for events a JSON-LD `Event` with name, startDate and location.
- **REQ-091** WHEN a client requests `/sitemap.xml` THE SYSTEM SHALL return XML listing `/`, `/events`, `/deals` and the `/events/{id}` or `/deals/{id}` URL of every active published item, and no URL of an expired, withdrawn or unpublished item.
- **REQ-092** WHEN a client requests `/robots.txt` THE SYSTEM SHALL return rules that disallow `/admin` and `/api/admin` and reference the sitemap.

### Public web app (Albanian, mobile first)

- **REQ-100** WHEN a visitor opens the home page THE SYSTEM SHALL show the tabs "Sot", "Evente" and "Oferta", where "Sot" lists the active events starting today in Europe/Pristina time and the active deals.
- **REQ-101** WHEN a visitor views an event or deal card or detail page THE SYSTEM SHALL show the title, the start time (events) or validity (deals) in Europe/Pristina time, a link "Burimi: {source name}" to the source URL, and the freshness label "Verifikuar së fundmi" (fresh) or "Kontrolluar së fundi më {date}" (stale).
- **REQ-102** WHEN a visitor selects a city in the "Qyteti" filter on the events page THE SYSTEM SHALL show only events in that city.
- **REQ-103** WHEN a visitor opens the detail page of an expired item THE SYSTEM SHALL show "Ky event ka përfunduar" (event) or "Kjo ofertë ka skaduar" (deal).
- **REQ-104** WHEN a list has no results THE SYSTEM SHALL show "Nuk u gjet asnjë rezultat".
- **REQ-105** WHEN a visitor searches on `/search` THE SYSTEM SHALL show matching events and deals, each labelled "Event" or "Ofertë".
- **REQ-106** WHEN a deal shows both prices THE SYSTEM SHALL show the old price, the new price and the discount as "-{n}%".
- **REQ-107** WHEN a card or detail page shows a synthetic item THE SYSTEM SHALL show the badge "Të dhëna testuese".
- **REQ-108** IF loading a list fails THEN THE SYSTEM SHALL show "Diçka shkoi keq." with a button "Provo sërish" that retries the request.

### Admin web app

- **REQ-110** WHEN an admin opens a candidate at `/admin/items/{id}` THE SYSTEM SHALL show the region "Evidenca nga burimi" with the source evidence and the region "Fushat e normalizuara" with the editable fields (side by side on wide screens, stacked on phones), and the list of validation issues.
- **REQ-111** WHEN a reviewer clicks "Aprovo" on a valid event candidate THE SYSTEM SHALL publish it and the event SHALL then appear on the public events page with its source link and freshness label.
- **REQ-112** WHEN a reviewer clicks "Refuzo", enters a reason in "Arsyeja" and confirms THE SYSTEM SHALL show the candidate as "Refuzuar" in the review queue.
- **REQ-113** WHEN an Admin adds a source on `/admin/sources` and clicks "Merr tani" for it THE SYSTEM SHALL show the run result and the source's health badge.
- **REQ-114** THE admin web app SHALL be fully operable with the keyboard with visible focus, and SHALL meet WCAG 2.2 AA contrast. [manual]

## 6. Non-functional

- Pages are usable at 360 px width, keyboard-navigable, and every form field has a label; respects `prefers-reduced-motion`.
- p95 API latency under 300 ms for public list/detail/search endpoints with 10k published items (load test before release).
- Public pages: Lighthouse performance, accessibility, best practices and SEO ≥ 95 on the mobile profile.
- All times stored in UTC; presented in Europe/Pristina.
- Source fetching identifies itself with a bot User-Agent, honours robots.txt, is limited to one request per host every 10 seconds, and scheduled fetching never runs more often than a source's poll interval.
- AI usage: daily call limit (default 500 in Production), every call persisted with model, request id, tokens and outcome.
- Personal data is limited to admin/reviewer accounts; secrets only in environment variables.
- Observability: structured logs with correlation and run ids; no secrets or passwords in logs.
