# Progress

> One entry per finished task or loop iteration, newest first. This is how a fresh session
> (or the human) understands the state of the project in 30 seconds.

<!--
## 2026-01-15 · T010 done
- Built: invoice create/list API + page; 6 acceptance tests green (REQ-001, REQ-002, REQ-P04).
- Verify: quick PASS. Reviewer: 1 LOW (naming) fixed. Security: none.
- Next: T011.
-->

## 2026-10-04 · T021 done
- Built: `GET /api/deals` (`PublicDealQueries`: city/category(deal kind)/search filters, same paging and error reporting as events, order validUntil asc with undated last then id) and `GET /api/search` (`PublicSearchQueries`: active events by start then active deals, paged across the boundary, `q` ≥ 2 chars after trim, punctuation-only `q` → 400 since it folds to empty). `ActiveItems.Deal` = SQL twin of `IsActiveDeal` (dated: `validUntil > now`; undated: verified within 7 days).
- Tests: integration +6 (SQL filter == domain rule at boundaries, ordering, search paging across events/deals, search 400s incl. `--`, deal filters/freshness/paging). Acceptance green for REQ-083 (filters), 084, 086, 087 (stale), 089, 079, 082; still red only: `Deals_list_shows_active_deals…` (needs `/api/admin/deals/{id}/expire|withdraw`, T030) and `Refetching_an_open_ended_deal_brings_it_back` (needs re-seen published item → `lastVerifiedAt`, T028).
- Verify: quick PASS. Reviewer: 2 MEDIUM (punctuation-only search matched everything, thin tests) fixed; LOWs left: paging/slug-resolution preamble duplicated across the three public query classes (`PublicEventQueries.ParseInt` is internal for reuse), deal summary mapping duplicated in detail, `FixedClock` copied in tests, no max length on `q`.
- Next: T022.

## 2026-10-04 · T020 done
- Built: `GET /api/events` (`PublicEventQueries`): published + not ended (`ActiveItems.Event`, SQL twin of `ContentRules.IsActiveEvent`), filters city/category (event kind)/from/to (Pristina midnight via Europe/Belgrade, `to` = midnight of the next date, DST-safe)/search (folded `SearchText` substring), order startsAt then id, paging with long offset (huge page → empty 200), all invalid parameters reported together (`page`/`pageSize` bound as strings so `abc` is named too; dates strict `yyyy-MM-dd`, years 1900-2200). `ExpirationRunner` + `ExpirationJob` (5 min, real clock) set `Expired` on events past effective end and deals past `validUntil`; `JobExecution` row only when something expired (ADR-024).
- Tests: integration +10 (Pristina boundaries on the 2027-03-28 DST day, SQL filter == domain rule, runner on events/deals/boundaries, extreme and malformed parameters); the integration host drops `ExpirationJob`. Acceptance: REQ-080 (page size), 081 (2), 082 events, P05 green; still red only on calls to `/api/deals`, `/api/search` (T021) and `/api/admin/events/{id}/expire|withdraw` (T030).
- Verify: quick PASS, integration 91/91. Reviewer: 6 MEDIUM (offset overflow, date extremes, job vs fixed-date tests, expiration test coverage, undated deals, non-numeric paging) fixed or recorded as ADR-024; LOWs: culture-invariant date parsing and private `StartOfDay` applied; left: slugs are case-sensitive, punctuation-only search is ignored.
- Next: T021 (deals list reuses the same filter/paging helpers pattern; `ActiveItems` needs a deal twin).

## 2026-10-04 · T019 done
- Built: `POST /api/admin/candidates/{id}/approve` (`CandidateApproval`): required fields per kind + the edit-path length/currency/price rules on the stored fields (ingested data never went through them), validation `Rejected` reported on `validation` together with missing fields, Event/Deal + found-or-created Venue + ItemSource + candidate `Published` + audit `candidate.approve` in one save; times stored in UTC (a +02:00 offset made Npgsql throw); `lastVerifiedAt` = fetch time; category = own slug, else the suggestion. Public `GET /api/events/{id}` and `/api/deals/{id}` (`PublicItemQueries`: sources primary first, status active/expired, freshness, withdrawn/malformed id → 404).
- Tests: integration +11 (publish, venue reuse, deal discount/no city, UTC offsets, over-long ingested fields → 400, 409 on repeat, 404/401, deal expiry via X-Test-Now, withdrawn 404). Acceptance: REQ-085 detail, REQ-062/063/067/021 turn green up to the calls into later tasks: the remaining failures hit `/api/events?search`, `/api/deals?search`, `/api/search` (T020/T021), merge (T029) and withdraw (T030).
- Verify: quick PASS, integration 81/81. Reviewer: 3 MEDIUM (stored-field limits → 500, venue race, thin tests) fixed (limits validated, any `DbUpdateException` → 409 so the reviewer retries; deal/expiry/limit tests added); LOWs left: `ApprovedDto` lives in Api, result types duplicate `DecisionResult`, up to 5 queries per detail. No concurrency test for parallel approvals/venue race (relies on xmin + unique index).
- Next: T020. T028 must make approve apply to `matchedItemId` (ChangedSinceApproval, REQ-068).

## 2026-10-04 · T018 done
- Built: `PUT /api/admin/candidates/{id}` (edit, re-validated with `IClock`, source evidence/URL untouched, kind-foreign fields cleared, citySlug/categorySlug must exist, categories of the candidate's kind only) and `POST .../reject` (reason 1-500 after trim, stores reviewer); `CandidateDecisions` service + `CandidateRequestValidator`; open = Pending or auto-rejected, anything else 409 (also on xmin conflict); audit `candidate.edit`/`candidate.reject` in the same save; 256 KB cap on PUT.
- Tests: unit +25 (request rules, Apply), integration +9 (audit before/after, no audit on 400, other-kind category, reject of auto-rejected then 409, bad reasons, 404/401, deal clears event fields). Acceptance: REQ-064, 065, 074 green; 3 REQ-067 tests fail only at `approve` (T019).
- Verify: quick PASS. Reviewer: 0 HIGH/MEDIUM; LOWs applied (409 comment, size limit). Left: edit recomputes only validator-owned issues, so T026/T028 must re-add AiUnavailable/PossibleDuplicate/ChangedSinceApproval on edit too (also in `CandidateDecisions.EditAsync`); the "open candidate" rule exists in IngestionService and CandidateDecisions (extract if a third use appears).
- Next: T019.

## 2026-10-04 · fix: full verify
- Failed in the full run: `dotnet-format` (CRLF working copies from `core.autocrlf=true`, plus migration charset/namespace style), `acceptance-api` (146) and `e2e-acceptance` (36 on public/admin UI). Fixed format with `dotnet format App.slnx --exclude tests/App.AcceptanceTests` and `.gitattributes` (`*.cs`, `*.csproj`, `*.props`, `*.slnx` eol=lf) so it does not come back on Windows.
- The acceptance failures are for features not built yet (AI enrichment, approve/publish, public API, web UI: later tasks), not regressions; left untouched.
- Verify: quick PASS. Review: skipped (formatting only).
- Next: T018.

## 2026-10-04 · T017 done
- Built: `CandidateValidator` (Domain/Content, pure; `now` = fetch time) with issue codes and severities per design §4: MissingTitle, InvalidUrl, Expired (exclusive effective end), MissingStartsAt, InvalidDateRange, UnknownCity (event without city, or stated city that did not resolve), MissingPrice, PriceInconsistent, ConflictingData (> 1 point), SourceUnreliable, InvalidImageUrl (info, image dropped). Outcome rejected > review > valid; status Rejected (no reviewer) or Pending. `IngestionService` validates new candidates and re-validates open ones on in-place update (auto-rejected may become pending again). `discountPercent` was already computed in the DTO.
- Tests: unit +29 (every rule and boundary), integration +1 (auto-rejected candidate becomes pending after the source fixes it). Acceptance: REQ-027..039 and REQ-060 green (63/64); the remaining one (`Discount_is_null_without_both_prices_and_shown_on_the_published_deal`) needs approve + public deal detail (T019).
- Verify: quick PASS. Reviewer: 0 HIGH/MEDIUM; LOWs: `ApplyValidation` replaces `Issues` wholesale, so T026/T028 must re-add their issues (noted in tasks.md); `CandidateValidator.Outcome` stays public for T018 (edit re-validation). Issue messages are English; the UI (T036) maps by code.
- Next: T018.

## 2026-10-04 · T016b done
- Built: `GET /api/admin/candidates` (filters status/validation/kind/sourceId by camelCase names, all bad parameters reported together, newest `fetchedAt` first then id, evidence left out of the list) and `GET /api/admin/candidates/{id}` (fields incl. computed `discountPercent`, evidence, source name, suggested category, `ai` block, `duplicateOf` with resolved title, malformed id → 404); Reviewer policy. Code in `Application/Candidates` + `CandidateEndpoints`.
- Tests: integration +5 (detail resolution, nulls and vanished duplicate target, list order/no evidence, multiple 400 fields, anonymous 401). Acceptance: REQ-030 (2), REQ-061 (3) green; REQ-060 6/7 — `Reviewer_lists_candidates_filtered…` needs the `MissingPrice`/`MissingTitle` issues from T017 (noted in tasks.md).
- Verify: quick PASS. Review: skipped (small, read-only, no business rules).
- Next: T017.

## 2026-10-04 · T016 done
- Built: `ISourceAdapter` (CanHandle/Fetch/Parse, ADR-023; design.md §2 updated), `FixtureSourceAdapter` (refused when fixtures are disallowed), `IngestionService` (lock → `Running` run saved → fetch/parse → lookup rows 1 and 5 → candidates + run + source bookkeeping in one save; per-source `SourceRunLock`, manual wait 30 s then 409; `source.fetch` audit for manual runs only, counts never payload; crash → run `Failed`/`Interrupted` and the source's failure count rises; `MarkInterruptedAsync` at startup), `ReferenceResolver` (city/category slugs, per kind, unresolved stay text), `POST /api/admin/sources/{id}/fetch`, `recentRuns` (last 10) on the source detail.
- Choices: until T017 every new candidate is `Pending`/`NeedsReview` without issues; until T028 an item whose fingerprint belongs to a non-open candidate counts `unchanged`; a source no adapter handles (JsonFeed/Rss until T023/T025) fails with `Unreachable`; run timestamps use the wall clock, `fetchedAt`/`lastFetchedAt`/`lastSuccessAt` use `IClock`. Parser now fails an item containing NUL or a lone surrogate (`InvalidField:*`), and a fixture payload with NUL is a 400 (it was a 500 on save).
- Tests: unit +47 (lock, resolver, NUL/surrogate items, NUL payload), integration +12 (counts, slugs, in-place update, duplicate in one feed, per-item failures, failed run, audit, 404/409, scheduled run without a user, crash → failed, startup `Interrupted`, reviewer 403, anonymous 401). Acceptance: REQ-014 (3), 016 (MalformedPayload), 017, 019 green; the 5 tests that read candidates (REQ-014 summary, 015, 018, 021, P04 fetch) fail only at `GET /api/admin/candidates` (T016b); `Unreachable`/`BlockedAddress` tests need T023.
- Verify: quick PASS. Reviewer: 1 MEDIUM (NUL item aborting the run) fixed + LOWs (open-candidate order, resolver lookups, tests); re-review not run (covered by the new tests). Security-reviewer: 0 HIGH/MEDIUM; LOWs: crash bookkeeping and endpoint tests applied; left as notes: `/fetch` has no rate limit (Admin only, per-source lock), `MarkInterruptedAsync` fails every `Running` row (single instance — T043 runbook must say so), `Fields.Url`/`ImageUrl` unvalidated until T017 (UI must render as text, `href` only for http(s)), T023/T025 adapters must build `SourceFetchException` messages from constants only.
- Next: T016b.

## 2026-10-04 · T015 done
- Built: `RadarFeedParser` (Application/Ingestion): MalformedPayload rules, per-item failures (`NotAnObject`, `InvalidKind`, `MissingIdentity`, `InvalidField:<name>`, `TooLarge` by UTF-8 bytes) with index/reason, kind-specific mapping (event vs deal fields, other kind's fields null), nulls kept, blank = unknown, strict ISO-8601 dates with offset, raw evidence via `GetRawText`; `ItemIdentity` (fingerprint = sha256(sourceId|externalId ?? url), content hash over kind + fields + text + evidence); `NormalizedCandidate`/`FeedParseResult`. City/category slugs are NOT resolved here (T016/T017 do it with reference data).
- Choices (beyond design §6 list): `externalId` > 200 and `url` > 2000 chars fail the item (`InvalidField:*`, DB column limit); wrong-typed field fails the item even if unused for its kind; null/blank payload = MalformedPayload; JSON depth limit 256.
- Tests: unit +119 (all failure rules, mapping tables, hashes, boundaries). Verify: quick PASS. Reviewer: 2 MEDIUM (lenient date parsing, url length) fixed + LOWs (null payload, depth, UTF-8 test); duplicate fingerprints inside one feed are for T016 to handle.
- Next: T016.

## 2026-10-04 · T014 done
- Built: `Candidate`, `Event`, `Deal`, `Venue`, `ItemSource`, `JobExecution` entities + `CandidateFields`/`Issue`, `ContentRules` (effective end, active, freshness, discount, title similarity), EF configurations (jsonb via System.Text.Json value conversion for `Fields`/`Issues` because EF complex-JSON mapping failed at runtime; `xmin` concurrency tokens; indexes; FKs with Restrict to City/Category/Venue/Source/Candidate), migration `ContentModel`. Polymorphic ids (`ItemSource.ItemId`, `MatchedItemId`, `DuplicateOfId`, `PublishedItemId`) have no FK by design.
- Tests: unit +18 (boundaries: exactly 48 h, 6 h default end, discount half-up, similarity 0.8), integration +4 (jsonb round trip, xmin conflict on Candidate/Event/Deal, unique venue per city, FK rejects orphans). Migration applies on an empty DB (AppFactory).
- Verify: quick PASS. Reviewer: 2 MEDIUM (missing FKs, thin tests) fixed; LOW notes: `Evidence` max length is characters not bytes (T015 must enforce 64 KB by bytes), entity `IsActive`/`EffectiveEnd` wrappers unused until T019/T020, jsonb comparer serializes per tracked candidate on save.
- Next: T015.

## 2026-10-03 · T013 done
- Built: `Source` entity + `Sources` migration (health derived on read, never stored), `GET/POST/PUT /api/admin/sources[/{id}]` (Admin policy), `SourceValidator` (per-type poll range, absolute http(s) URL without credentials, 1 MB payload, names-only enums, fixture refused when `Ingestion__AllowFixtureSources=false`), `SourceAdmin` (create/update + audit `source.create`/`source.update` in one save; audit keeps the payload length, never the payload), `IngestionOptions` (true in Development/Testing, false by default), 8 MB request cap on POST/PUT, list does not load payloads. `recentRuns` is `[]` until T016.
- Tests: unit 94 (+43: validator boundaries per type, health thresholds), integration +6 (audit content, failed validation writes no audit, type change replaces location, fixtures refused, list/404, anonymous 401). Acceptance: REQ-010, 011, 073 and P04 green except `Reviewer_cannot_fetch_a_source_and_no_run_happens` (needs `POST /fetch`, T016); REQ-013/072 tests that fetch or read candidates stay red until T016/T016b/T030.
- Verify: quick PASS. Reviewer: 0 HIGH/MEDIUM, LOWs applied (names-only enums, control characters in names, list without payload, request size cap, more tests). Security-reviewer: 0 HIGH/MEDIUM; notes left: stored URL is the trimmed input (T023 must re-parse with `System.Uri` and enforce the SSRF guard at connect time), query-string secrets in `baseUrl` are visible to Reviewers via the audit log (sources must use public URLs), updating an existing fixture source returns 400 once fixtures are disallowed (run path must refuse them, T016), `LastError` is capped at 1000 chars (T016 must truncate and keep response bodies/query strings out).
- Next: T014.

## 2026-10-03 · T012 done
- Built: `GET/POST /api/admin/users`, `POST /api/admin/users/{id}/deactivate` (Admin policy; 409 duplicate, 400 self-deactivation, idempotent 204, 404), `UserAdmin` service (create = user + role + audit in one transaction; deactivate = `IsActive=false` + security-stamp rotation + audit in one save; concurrency failure treated as already deactivated), `/admin/users` page (form, table, deactivate, loading/empty/error states), ADR-022 (password policy = length only, so any 12+ character password works, REQ-001).
- Tests: integration 33 (+5: audit without password, duplicate/validation, 6 parallel creates, Reviewer 403/anonymous 401, rollback on failed role), vitest 14 (+5), Playwright flow `users.spec.ts`. Acceptance: P01/P02/P06, REQ-002, REQ-004 and the other REQ-001/003 tests green (13/16); 3 fail only at their last assertions on `/api/admin/candidates` (T016b) and `/api/admin/sources` (T013), which do not exist yet.
- Verify: quick PASS. Reviewer: 0 HIGH/MEDIUM, LOWs applied (concurrent deactivate, visible loading text, header cell, aria-invalid, button hidden until `/me` loads, stronger tests). Security-reviewer: 0 HIGH/MEDIUM; left as notes: no "last active admin" guard (two admins can deactivate each other), no CSRF header on body-less POSTs (SameSite=Strict only; consider an `X-Requested-With` check app-wide in T043/T090), user list unpaginated (design §5), no confirm step before deactivating.
- Note: `dotnet format` rewrote line endings in unrelated files again; reverted (use `--include` for the touched files).
- Next: T013.

## 2026-10-03 · T011 done
- Built: `AuditLog` entity + migration (jsonb `{ before, after }`, UserEmail snapshot, append-only guard in `SaveChanges`), `IAuditWriter.Record` (joins the caller's `SaveChanges`; actor from `ICurrentUser` claims; secret-like keys dropped), `GET /api/admin/audit` (Reviewer policy, `entityId`, paging, newest first), shared `PagedResult<T>`/`Paging.Parse` (ADR-021).
- Tests: unit 50 (AuditChanges, Paging), integration 28 (write/read, ordering, redaction, 401/200 by role, paging incl. int overflow, real cookie claims, rollback atomicity, append-only). No acceptance test is owned by T011 (REQ-072 is closed in T030).
- Verify: quick PASS. Reviewer: 3 MEDIUM (page overflow, claims untested, atomicity untested) fixed + LOWs (wording, conditional filter, tests moved); security-reviewer: 0 HIGH/MEDIUM, LOWs applied (wider secret-key denylist, append-only guard); left as notes: size cap of `Changes`, index on `At` for the unfiltered list. Re-review of the fixes not run (verified by the new tests).
- Note: `dotnet format` rewrote line endings in unrelated files; those were reverted. Consumers of `IAuditWriter` (T012+) must call `Record` before the action's single `SaveChanges` and pass allowlisted DTOs, not entities.
- Next: T012.

## 2026-10-03 · T010 done
- Built: TextNormalizer (fold/slug, punctuation = word separator, ADR-020), City/Category/ItemKind, CityMatcher (prebuilt folded lookup), idempotent ReferenceDataSeeder (38 municipalities with aliases, 14 categories) on every start, GET /api/cities and /api/categories?kind= (ordered by slug, 400 on unknown kind), camelCase enum JSON.
- Tests: unit 43, integration 19; acceptance REQ-088 green.
- Verify: quick PASS. Reviewer: 1 MEDIUM (category order) fixed + LOWs (kind parsing, file split, PascalCase-code notes in T017/T023, seed race note in T043).
- Note: a full verify ran during T010; its log is in C:/Users/genti/verify-full.log (not reviewed yet).
- Next: T011 (not started — stopped on request).

## 2026-10-03 · T003 done
- Built: router (public layout + lazy admin bundle), typed api wrapper, /admin/login (401/429), admin layout with role-filtered nav and "Dil", dashboard tiles (data in T031), global 401 → login, safe logout, Albanian strings module, strict TS, design tokens (focus-visible, reduced motion), lang="sq".
- Tests: vitest 9; Playwright acceptance P10/P02/P11 6/6 (desktop + mobile).
- Also: fix(T002) 6929676 — LoginThrottle consumed a scoped service; service validation now on in Testing.
- Reviewer: 1 MEDIUM (401 mid-session) fixed + LOWs; re-check of the commit requested.
- Next: T010.

## 2026-10-03 · T002 done
- Built: security headers incl. CSP (first in pipeline), exception handler/problem details, /health, login throttle (atomic reserve/release; per pair 10/min, per email 50/h, per IP across emails, ADR-016/018/019), public per-IP limiter (config, Retry-After, logged), forwarded headers (proxies + CIDR networks), Data Protection keys in PostgreSQL, fail-fast connection string, SPA hosting + anonymous file 404.
- Tests: unit 14, integration 17; acceptance P03/P07/P08/P09/P11 37/37.
- Verify: quick PASS. Reviewer: 1 MEDIUM (throttle race) fixed; security-reviewer: 1 MEDIUM (CPU via random emails) fixed + LOWs; re-check of the commit requested.
- Next: T003.

## 2026-10-03 · T001 done
- Built: Identity roles Admin/Reviewer, bootstrap admin, login/logout/me, AppSignInManager (inactive users never sign in or keep a session), server-side in-memory sessions (ADR-017), X-Test-Now + /api/_test/throw hooks (Dev/Testing only), fallback auth policy, /api/admin fallback 401, InitialIdentity migration.
- Tests: integration 13, unit 6; acceptance P03 (26) + P11 green. P01/P02/P06 → T012, P05 → T020 (their locked tests need later endpoints).
- Verify: quick PASS. Reviewer 3 rounds (timing oracle, logout scope, renewal race fixed); security-reviewer: Open HIGH/MEDIUM 0; remaining LOWs moved into T002/T043.
- Next: T002.

## 2026-10-03 · Spec phase done (awaiting approval)
- Spec: 92 requirements (1 manual), design contracts, 16 ADRs, 47 tasks; spec-reviewer 3 rounds (all blockers applied).
- Scaffold: default stack; pinned Microsoft.OpenApi 2.12.2 in App.Api (transitive 2.0.0 has GHSA-v5pm-xwqc-g5wc and failed restore); `.gitattributes` keeps `*.sh` LF.
- Acceptance tests (test-author): 172 xUnit declarations, 21 Playwright tests; traceability OK. Not executed yet: Docker daemon was not running.
- Next: human runs ./scripts/approve-spec.sh, then T001.

