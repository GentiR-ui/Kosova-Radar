#!/usr/bin/env bash
# Builds the product ONE TASK PER FRESH CLAUDE SESSION (clean context every time).
# Replaces the long /goal session: each task starts without the history of the previous ones,
# reads the state from docs/PROGRESS.md, docs/tasks.md and git, finishes one task, commits, and exits.
#
#   bash scripts/loop.sh                 run until every task is done or the human is needed
#   MODEL=opus bash scripts/loop.sh      use another model for the tasks
#
# Settings (environment variables):
#   MODEL=sonnet          model for normal tasks
#   EFFORT=medium         effort for normal tasks (low | medium | high | xhigh)
#   RISKY_EFFORT=high     effort for tasks marked with 🔒 in docs/tasks.md (auth, security, ingestion...)
#   RELEASE_MODEL=opus    model for the final release session (hardening + QA)
#   MAX_TASKS=80          stop after this many task sessions
#   MAX_TURNS=200         safety cap per session
#   FULL_VERIFY_EVERY=5   run the full ./scripts/verify.sh after this many finished tasks. Acceptance tests
#                         of tasks not built yet are EXPECTED to fail mid-build, so only these count as a
#                         failure: a non-acceptance stage (build, format, integration, lint, audit...) or an
#                         acceptance test that passed in an earlier full verify and fails now (regression).
#   LIMIT_WAIT_MIN=20     minutes to wait before retrying after a usage limit
#   RELEASE_ONLY=1        skip the task loop and run only the release session (e.g. after NEEDS HUMAN
#                         when the only open tasks are blocked on the client)
#
# Stop at any time with Ctrl+C. Run it again later and it continues with the next unchecked task.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT" || exit 1

MODEL="${MODEL:-sonnet}"
EFFORT="${EFFORT:-medium}"
RISKY_EFFORT="${RISKY_EFFORT:-high}"
RELEASE_MODEL="${RELEASE_MODEL:-opus}"
MAX_TASKS="${MAX_TASKS:-80}"
MAX_TURNS="${MAX_TURNS:-200}"
FULL_VERIFY_EVERY="${FULL_VERIFY_EVERY:-5}"
LIMIT_WAIT_MIN="${LIMIT_WAIT_MIN:-20}"
RELEASE_ONLY="${RELEASE_ONLY:-0}"

LOG_DIR=".factory/state/loop"
mkdir -p "$LOG_DIR"
touch .factory/state/loop-active
FV_FLAG="$LOG_DIR/full-verify-failed"          # holds the reason; an empty flag is from an older loop.sh
FV_BASELINE="$LOG_DIR/acceptance-passing.txt"  # acceptance tests that passed in the last good full verify
[[ -s "$FV_FLAG" ]] || rm -f "$FV_FLAG"

command -v claude >/dev/null 2>&1 || { echo "claude CLI not found in PATH"; exit 1; }
[[ -f .factory/spec-lock/manifest.sha256 ]] || { echo "Spec not approved yet: run ./scripts/approve-spec.sh first."; exit 1; }

say() { printf '\n\033[1;36m== %s\033[0m\n' "$*"; }

# Unchecked task lines (examples inside HTML comments are ignored).
open_tasks() { perl -0pe 's/<!--.*?-->//gs' docs/tasks.md | grep -E '^- \[ \] T[0-9]+'; }
count_open() { open_tasks | wc -l | tr -d ' '; }
next_task_line() { open_tasks | head -1; }

# A usage limit ends the run with a non-zero exit and the limit message in the last lines.
hit_limit() { # rc logfile
  (( $1 != 0 )) && tail -n 5 "$2" | grep -qiE "usage limit|session limit|weekly limit|hit your .*limit|limit (will )?reset"
}

run_claude() { # prompt model effort turns logfile
  claude -p "$1" --model "$2" --effort "$3" --max-turns "$4" \
    --permission-mode auto --permission-prompts none 2>&1 | tee "$5"
  return "${PIPESTATUS[0]}"
}

# Names of the acceptance tests that passed in the last ./scripts/verify.sh run (API trx + Playwright json).
acceptance_passing() {
  local trx=".factory/state/verify/acceptance.trx" json=".factory/state/verify/e2e-acceptance.json"
  [[ -f "$trx" ]] && perl -ne 'while (/<UnitTestResult\b[^>]*>/g) { my $t = $&;
      print "api: $1\n" if $t =~ /outcome="Passed"/ && $t =~ /testName="([^"]*)"/ }' "$trx"
  [[ -f "$json" ]] && jq -r '.. | objects | select(has("specs")) | .specs[] | . as $s | .tests[]
      | select(.status == "expected" or .status == "flaky")
      | "ui: \($s.file) > \($s.title) [\(.projectName)]"' "$json" 2>/dev/null
  return 0
}

# Full verify mid-build. Returns 1 and writes the reason to $FV_FLAG only for real problems.
periodic_full_verify() {
  local other regressed now="$LOG_DIR/acceptance-now.txt"
  ./scripts/verify.sh >"$LOG_DIR/full-verify.log" 2>&1
  if grep -q '^VERIFY: PASS' "$LOG_DIR/full-verify.log"; then
    acceptance_passing | LC_ALL=C sort -u >"$FV_BASELINE"; return 0
  fi
  other="$(grep -E '^FAIL ' .factory/state/last-verify.txt | grep -vE '^FAIL +(acceptance-api|e2e-acceptance)( |$)')"
  acceptance_passing | LC_ALL=C sort -u >"$now"
  regressed=""
  [[ -f "$FV_BASELINE" ]] && regressed="$(LC_ALL=C comm -23 "$FV_BASELINE" "$now")"
  if [[ -z "$other" && -z "$regressed" ]]; then
    cp "$now" "$FV_BASELINE"   # only not-yet-built features fail: expected
    say "Full verify: no regressions ($(wc -l <"$now" | tr -d ' ') acceptance tests pass; the rest belong to open tasks)"
    return 0
  fi
  {
    [[ -n "$other" ]] && { echo "Failed stages:"; echo "$other"; }
    [[ -n "$regressed" ]] && { echo "Acceptance tests that passed before and fail now (regressions):"; echo "$regressed"; }
  } >"$FV_FLAG"
  return 1
}

TASK_PROMPT='You are running inside scripts/loop.sh: a FRESH session for exactly ONE task.
Use the build-loop skill. First read CLAUDE.md, the top of docs/PROGRESS.md, docs/tasks.md,
docs/BLOCKERS.md and `git log --oneline -15`; also `git status` (an earlier session may have left
uncommitted work for the same task: continue it instead of starting over).
Then take the first unchecked task in docs/tasks.md that is not blocked and finish it completely
according to its Definition of Done and the build-loop skill (verify --quick PASS, review rules,
tasks.md checkbox, PROGRESS.md entry, one commit). Then STOP: do not start another task.
If every remaining task needs the human, record that in docs/BLOCKERS.md and end your reply with a line
containing only: NEEDS HUMAN
End with a two-line summary in Albanian of what you did.'

RELEASE_PROMPT='You are running inside scripts/loop.sh: a FRESH session for the release phase.
All product tasks in docs/tasks.md are checked. Read CLAUDE.md, docs/PROGRESS.md and docs/BLOCKERS.md,
then run the release skill end to end (hardening with security-reviewer, whole-code review, qa,
full ./scripts/verify.sh, docs/RELEASE.md, docs/LESSONS.md). Check the release tasks in docs/tasks.md
when their DoD holds. End with a line containing only DONE or NEEDS HUMAN, followed by a short summary
in Albanian with the exact actions the human must take.'

tasks_done=0
session=0
stalls=0
waits=0
while [[ "$RELEASE_ONLY" != "1" ]]; do
  open="$(count_open)"
  if (( open == 0 )); then break; fi
  # Only release tasks (T09x) left -> release phase.
  if ! open_tasks | grep -qvE '^- \[ \] T09[0-9]'; then break; fi
  if (( session >= MAX_TASKS )); then say "MAX_TASKS=$MAX_TASKS reached; run the script again to continue."; exit 0; fi

  line="$(next_task_line)"
  effort="$EFFORT"
  [[ "$line" == *"🔒"* ]] && effort="$RISKY_EFFORT"
  head_before="$(git rev-parse HEAD)"
  log="$LOG_DIR/$(date +%Y%m%d-%H%M%S)-task.log"
  session=$((session + 1))
  say "Session $session · $open tasks open · next: $(cut -c1-90 <<<"$line") · $MODEL/$effort"

  prompt="$TASK_PROMPT"
  fixing=0
  if [[ -s "$FV_FLAG" ]]; then
    fixing=1
    prompt="$prompt

IMPORTANT: the periodic full ./scripts/verify.sh found these problems (logs in .factory/state/verify/ and
$LOG_DIR/full-verify.log):
$(cat "$FV_FLAG")
Fixing exactly these is this session's task; commit it as \"fix: full verify\". Acceptance tests of tasks
that are not built yet are expected to fail and are NOT part of this fix. If you find nothing to fix, say why
in docs/BLOCKERS.md."
  fi

  run_claude "$prompt" "$MODEL" "$effort" "$MAX_TURNS" "$log"; rc=$?

  if hit_limit "$rc" "$log"; then
    session=$((session - 1)); waits=$((waits + 1))
    if (( waits > 72 )); then say "Usage limit for more than a day. Stopping."; exit 4; fi
    say "Usage limit reached. Waiting $LIMIT_WAIT_MIN minutes, then continuing (Ctrl+C to stop)."
    sleep $((LIMIT_WAIT_MIN * 60)); continue
  fi
  waits=0

  if grep -qE '^[[:space:]]*NEEDS HUMAN[[:space:]]*$' "$log"; then
    say "NEEDS HUMAN — see the end of $log and docs/BLOCKERS.md"; exit 2
  fi

  if (( fixing )); then
    rm -f "$FV_FLAG"   # one fix attempt; the next periodic full verify checks again
    [[ "$(git rev-parse HEAD)" == "$head_before" ]] && { say "Fix session made no commit; continuing with tasks (see docs/BLOCKERS.md)."; continue; }
  fi

  if [[ "$(git rev-parse HEAD)" == "$head_before" && "$(count_open)" == "$open" ]]; then
    stalls=$((stalls + 1))
    say "No commit and no task closed in this session ($stalls/2). Log: $log"
    if (( stalls >= 2 )); then say "Stopped: two sessions in a row without progress. Read the log, then run again."; exit 3; fi
    continue
  fi
  stalls=0

  closed=0
  if [[ "$(count_open)" -lt "$open" ]]; then tasks_done=$((tasks_done + 1)); closed=1; fi
  if (( closed && FULL_VERIFY_EVERY > 0 && tasks_done % FULL_VERIFY_EVERY == 0 )); then
    if docker info >/dev/null 2>&1; then
      say "Full verification after $tasks_done tasks"
      if periodic_full_verify; then
        grep -q '^VERIFY: PASS' "$LOG_DIR/full-verify.log" && say "Full verify PASS"
      else
        say "Full verify found real problems — the next session fixes them first:"
        cat "$FV_FLAG"
      fi
    else
      say "Docker is not running: skipping the periodic full verification (start Docker Desktop)."
    fi
  fi
done

if [[ "$RELEASE_ONLY" != "1" ]] && (( $(count_open) == 0 )); then say "Every task is checked. Nothing left to do."; rm -f .factory/state/loop-active; exit 0; fi

say "Release phase · $RELEASE_MODEL/high"
while :; do
  log="$LOG_DIR/$(date +%Y%m%d-%H%M%S)-release.log"
  run_claude "$RELEASE_PROMPT" "$RELEASE_MODEL" high 400 "$log"; rc=$?
  if hit_limit "$rc" "$log"; then
    say "Usage limit reached. Waiting $LIMIT_WAIT_MIN minutes, then continuing (Ctrl+C to stop)."
    sleep $((LIMIT_WAIT_MIN * 60)); continue
  fi
  break
done
if grep -qE '^[[:space:]]*DONE[[:space:]]*$' "$log"; then
  say "DONE — read docs/RELEASE.md"
else
  say "Release session ended — read the end of $log and docs/BLOCKERS.md"
fi
