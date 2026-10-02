# Tasks

> ORDER of work. Vertical slices: each task delivers working behaviour end to end (API + UI + tests),
> closes in one commit, and stays under ~400 changed lines. Split anything bigger.
> Format: `- [ ] T### — title — REQs — Definition of Done`. Check the box only when its DoD holds.

## Platform

- [ ] T001 — Platform baseline: tenancy model, Identity cookie auth, register/login/logout/me, `/health`, problem details, rate limiting on login, security headers, SPA hosting from wwwroot, migrations — REQ-P01..P09, P11 — DoD: API acceptance tests for these REQs green; integration tests for the tenant query filter; `scripts/verify.sh --quick` green
- [ ] T002 — Web shell: router, TanStack Query client, register/login/home/logout screens per design.md §6, responsive layout — REQ-P10, P11 — DoD: Playwright acceptance for P10/P11 green on desktop and mobile projects

## Product

<!--
- [ ] T010 — Create and list invoices (API + list page + create form) — REQ-001, REQ-002 — DoD: acceptance tests green; cross-tenant test for the new endpoints (REQ-P04) green; security-reviewer has no HIGH/MEDIUM
- [ ] T011 — Send invoice — REQ-003 — DoD: ...
-->

<FILL: product slices T010, T011, ...>

## Release

- [ ] T090 — Full hardening pass over the whole app (harden skill) — DoD: security-reviewer report has no HIGH/MEDIUM
- [ ] T091 — Final QA (release skill): qa subagent explores every flow on desktop + mobile; RELEASE.md written — DoD: `scripts/verify.sh` full PASS
