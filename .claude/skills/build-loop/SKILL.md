---
name: build-loop
description: Implementation loop of the SaaS factory after the spec is approved. Picks the next task, implements it, verifies, gets independent review, commits, and repeats until every task is done. Use when /goal starts the build or the user says to continue building.
---

# Build loop

Precondition: `.factory/spec-lock/manifest.sha256` exists (spec approved). If not, stop and tell the user
(in Albanian) to run `./scripts/approve-spec.sh`.

## Start of every session / after compaction
1. `mkdir -p .factory/state && touch .factory/state/loop-active` (enables the Stop gate).
2. Read CLAUDE.md, the top of `docs/PROGRESS.md`, `docs/tasks.md`, `docs/BLOCKERS.md`, `git log --oneline -15`.
3. Run `./scripts/verify.sh --quick`. If something that used to pass is red, fixing it is the next task.

## One iteration = one task
1. **Pick** the first unchecked task whose dependencies are done and that is not blocked.
2. **Plan** in 5–10 lines: REQs, which acceptance tests should turn green, files to touch, migrations.
   If it will exceed ~400 changed lines, split it in `docs/tasks.md` first.
3. **Implement** following `docs/stack/dotnet-react.md`. Write unit/integration tests alongside.
   Run the task's acceptance tests directly while working:
   `dotnet test tests/App.AcceptanceTests --filter "Req=REQ-001"` · `cd web && npm run e2e:acceptance -- --grep "REQ-001"`.
4. **Verify:** `./scripts/verify.sh --quick` must end with `VERIFY: PASS`.
5. **Review:**
   - **Security:** if the task touches auth, user data, input handling, files, payments or external calls
     (tasks marked 🔒 always do), delegate to `security-reviewer`. Never skipped.
   - **Code review:** delegate to `reviewer` ("Review the uncommitted diff for task T0xx, REQs ...") when the task
     changes more than ~150 lines or contains business rules. A small task without business rules may skip it:
     write `Review: skipped (small)` in its PROGRESS entry. If the two previous PROGRESS entries both say
     `skipped (small)`, review now the combined diff since the last reviewed commit instead.
   - Fix every HIGH/MEDIUM, re-verify, re-review. Max 3 rounds; leftovers → BLOCKERS.
6. **Record:** check the box in `docs/tasks.md`, add the entry at the top of `docs/PROGRESS.md`, ADRs if any,
   then `git add -A && git commit -m "T0xx: <title>"`.
7. **Started by `scripts/loop.sh`** (the prompt says so): stop after the commit; the script starts a fresh
   session for the next task and runs the periodic full `./scripts/verify.sh` itself.
   **Started by `/goal`:** continue with the next task without stopping to report; after T002 and then every
   ~5 tasks run the full `./scripts/verify.sh` (needs Docker) to catch drift early.

## When stuck
- Same failure after 3 attempts: stop guessing. Re-read the requirement and the design contract, read the
  full error and logs in `.factory/state/verify/`, check the official docs for the API you use, then ask
  `reviewer` for a root-cause analysis with the error, the files and what you tried.
- Still stuck: write a BLOCKERS entry (what, evidence, what was tried, what a human must decide) and move on.
- You believe an acceptance test is wrong: never touch it. Ask `test-author` to judge it against the
  requirement. If it says TEST WRONG, record the dispute in BLOCKERS with its proposed fix and continue.
- Locked files changed by accident (verify reports spec integrity failure): `./scripts/restore-spec.sh`.

## When every task is checked
Run the `release` skill.

## When only human-dependent items remain
If every remaining task or failure needs the human (credentials, payments, disputed acceptance test, no Docker),
run the release skill as far as possible, then print `NEEDS HUMAN` followed by the BLOCKERS list, in Albanian
with the exact actions the human must take (e.g. add `REQ-004 | reason` to `.factory/waivers`, or
`./scripts/approve-spec.sh --amend "..."`).
