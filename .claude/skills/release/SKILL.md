---
name: release
description: Final phase of the SaaS factory. Full hardening, whole-codebase review, exploratory QA, full verification, release notes and lessons for the starter kit. Use when every product task in docs/tasks.md is done.
---

# Release candidate

1. **T090 hardening:** delegate to `security-reviewer` with scope "whole application, harden checklist".
   Fix all HIGH/MEDIUM with regression tests; repeat until "Open HIGH/MEDIUM: 0" (max 3 rounds → BLOCKERS).
2. **Whole-code review:** delegate to `reviewer` with scope "entire codebase vs docs/design.md". Same rule.
3. **T091 QA:** delegate to `qa`. Fix HIGH/MEDIUM; keep its flow scripts as regression tests in web/e2e/flows.
4. **Full gate:** `./scripts/verify.sh` (not --quick) must end with `VERIFY: PASS` and show `spec integrity OK`.
5. **Image check (if Docker is available):** `docker compose --profile app up -d --build`, then
   `curl -fsS http://localhost:8080/health` and open `/` once; `docker compose --profile app stop`.
6. **Write `docs/RELEASE.md`:**
   - What was built: table REQ id → status (automated test name or [manual] check result).
   - How to run locally and how to deploy: required env vars (from `.env.example`), database migration step
     (`dotnet ef migrations bundle` or `database update` against production), HTTPS/proxy, backups, logs.
   - Human checklist before going live: secrets set, domain + TLS, backups enabled, [manual] REQs checked,
     legal pages (privacy, terms) if collecting personal data, analytics to measure the success signal.
   - Known limitations and open BLOCKERS.
7. **Write `docs/LESSONS.md`:** what slowed the loop, what the reviewers kept finding, what to change in the
   starter kit (tag `[kit]`): new platform requirements, better defaults, hook or script fixes.
8. `rm -f .factory/state/loop-active`, commit `release: candidate <date>`.
9. **Tell the user (in Albanian):** the app is ready for their review, what to check, the deploy checklist,
   and the [kit] lessons worth copying back into the starter. Do not deploy or push yourself.
