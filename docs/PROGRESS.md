# Progress

> One entry per finished task or loop iteration, newest first. This is how a fresh session
> (or the human) understands the state of the project in 30 seconds.

<!--
## 2026-01-15 · T010 done
- Built: invoice create/list API + page; 6 acceptance tests green (REQ-001, REQ-002, REQ-P04).
- Verify: quick PASS. Reviewer: 1 LOW (naming) fixed. Security: none.
- Next: T011.
-->

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

