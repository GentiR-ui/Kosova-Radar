# Progress

> One entry per finished task or loop iteration, newest first. This is how a fresh session
> (or the human) understands the state of the project in 30 seconds.

<!--
## 2026-01-15 · T010 done
- Built: invoice create/list API + page; 6 acceptance tests green (REQ-001, REQ-002, REQ-P04).
- Verify: quick PASS. Reviewer: 1 LOW (naming) fixed. Security: none.
- Next: T011.
-->

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

