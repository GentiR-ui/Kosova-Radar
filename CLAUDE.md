# SaaS factory — rules for Claude

You build SaaS products end to end from this starter. The human gives the idea, approves the spec once,
and approves the release once. Everything in between is yours, without asking questions.
Talk to the user in Albanian. Write code, commits and docs in English.

## Phases

1. **Spec** — skill `new-saas`: idea → `docs/requirements.md` (EARS, REQ ids) → `docs/design.md` → `docs/tasks.md`
   → `spec-reviewer` critique → `scripts/scaffold.sh` → `test-author` writes acceptance tests → summary to the human.
2. **Approval** — the human runs `./scripts/approve-spec.sh`. This locks requirements + acceptance tests.
3. **Build** — skill `build-loop`, driven by the `/goal` in `.factory/goal.txt`.
4. **Release** — skill `release`: hardening, final QA, `docs/RELEASE.md`, lessons. The human deploys.

## Source of truth (read at the start of every session)

- `docs/requirements.md` WHAT · `docs/design.md` HOW (contracts) · `docs/tasks.md` ORDER
- `docs/PROGRESS.md` state · `docs/DECISIONS.md` ADRs · `docs/BLOCKERS.md` open problems
- `docs/stack/dotnet-react.md` code conventions — read before writing code
- `git log --oneline -20` and `.factory/state/last-verify.txt`

## Hard rules

- **Locked after approval:** `docs/requirements.md`, `tests/App.AcceptanceTests/**`, `web/e2e/acceptance/**`.
  Hooks block edits; `verify.sh` checks checksums. If you think an acceptance test is wrong: write the evidence
  in `docs/BLOCKERS.md`, ask `test-author` to confirm against the requirement, then move to other tasks.
  Never weaken, skip, delete or work around a test to get green.
- **Yours to change freely:** unit, integration and regression tests (incl. `tests/App.IntegrationTests/AppFactory.cs`),
  `web/e2e/flows`, `web/playwright.config.ts`.
- **Human-owned (hooks block edits):** `scripts/`, `.claude/`, `.factory/` config, `Directory.Build.*`. If one of them
  is in your way, describe the needed change in `docs/BLOCKERS.md`.
- **Only `test-author` writes acceptance tests.** You never write them yourself.
- **Never ask the user implementation questions.** Resolve in this order: codebase → docs/ → official
  documentation (web) → `spec-reviewer` or `reviewer` subagent → choose the simplest maintainable option →
  record it as an ADR in `docs/DECISIONS.md` → continue.
- **Stop only for:** missing credentials or secrets, payments, production deploys, anything irreversible
  outside this repository. Record it in `docs/BLOCKERS.md`, keep working on everything else. When only such
  items remain, finish with the line `NEEDS HUMAN` and the list (the human may add waivers in `.factory/waivers`).
- **Default stack is fixed** (`docs/design.md` §1). Deviate only when a requirement cannot be met; that needs an ADR.
- **Design contracts are binding.** Implement the API/UI in `docs/design.md` exactly; if it must change, update it
  in the same commit with an ADR.
- **Security is structural:** every endpoint has an explicit auth decision, every tenant-owned entity
  implements `ITenantOwned`, every input is validated, no secrets in code (use env vars + `.env.example`).
- **Small steps:** one task per commit, under ~400 changed lines; split bigger tasks in `docs/tasks.md`.
- Never run plain `dotnet format`; use `dotnet format App.slnx --exclude tests/App.AcceptanceTests`.
- Never use `git push`, `git tag`, `git reset --hard`, or destructive database commands.

## Definition of done for a task

1. Acceptance tests for its REQs pass; new behaviour has unit/integration tests.
2. `./scripts/verify.sh --quick` ends with `VERIFY: PASS`.
3. `reviewer` has no HIGH/MEDIUM findings; `security-reviewer` too when the task touches auth, tenant data,
   input handling, files, payments or external calls (use the `harden` skill checklist).
4. Box checked in `docs/tasks.md`, entry at the top of `docs/PROGRESS.md`, one commit `T0xx: <title>`.

## Subagents (use them when they add independence or focus, not for trivial work)

| Agent | Use for |
|---|---|
| `spec-reviewer` | critique requirements/design before approval; settle product or architecture choices |
| `test-author` | write acceptance tests from the spec (cannot read implementation code) |
| `reviewer` | code review of each task's diff against design + conventions |
| `security-reviewer` | hardening review of risky tasks and the final pass |
| `qa` | final exploratory testing of the running app on desktop and mobile |

## Commands

```
./scripts/scaffold.sh               create the default-stack skeleton (once, in the spec phase)
./scripts/verify.sh --quick         fast gate after each task
./scripts/verify.sh                 full gate (integration, acceptance, E2E, audits, secrets)
./scripts/restore-spec.sh           undo accidental changes to locked files
docker compose up -d --wait db      local PostgreSQL
```
