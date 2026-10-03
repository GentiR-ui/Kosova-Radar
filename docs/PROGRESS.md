# Progress

> One entry per finished task or loop iteration, newest first. This is how a fresh session
> (or the human) understands the state of the project in 30 seconds.

<!--
## 2026-01-15 · T010 done
- Built: invoice create/list API + page; 6 acceptance tests green (REQ-001, REQ-002, REQ-P04).
- Verify: quick PASS. Reviewer: 1 LOW (naming) fixed. Security: none.
- Next: T011.
-->

## 2026-10-03 · Spec phase done (awaiting approval)
- Spec: 92 requirements (1 manual), design contracts, 16 ADRs, 47 tasks; spec-reviewer 3 rounds (all blockers applied).
- Scaffold: default stack; pinned Microsoft.OpenApi 2.12.2 in App.Api (transitive 2.0.0 has GHSA-v5pm-xwqc-g5wc and failed restore); `.gitattributes` keeps `*.sh` LF.
- Acceptance tests (test-author): 172 xUnit declarations, 21 Playwright tests; traceability OK. Not executed yet: Docker daemon was not running.
- Next: human runs ./scripts/approve-spec.sh, then T001.

