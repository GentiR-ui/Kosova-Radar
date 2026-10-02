#!/usr/bin/env bash
# Stop hook: while the build loop is active, Claude may not end a turn with a red quick-verify.
# Active only when .factory/state/loop-active exists (created by the build-loop skill).
set -uo pipefail
HOOK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "$HOOK_DIR/lib.sh"

cat >/dev/null   # consume stdin
[[ -f "$STATE_DIR/loop-active" ]] || exit 0

counter="$STATE_DIR/stop-blocks"
max="$(conf STOP_GATE_MAX_BLOCKS 3)"
count="$(cat "$counter" 2>/dev/null || echo 0)"

out="$("$ROOT/scripts/verify.sh" --quick 2>&1)"; rc=$?
if (( rc == 0 )); then
  rm -f "$counter"
  exit 0
fi

count=$((count + 1))
if (( count > max )); then
  rm -f "$counter"
  jq -n --arg m "Stop gate: quick verify still failing after $max attempts. Letting Claude stop; the failure must be recorded in docs/BLOCKERS.md." '{systemMessage:$m}'
  exit 0
fi
echo "$count" >"$counter"
jq -n --arg r "Quick verification failed (attempt $count/$max). Fix it before ending the turn:
$(tail -n 60 <<<"$out")" '{decision:"block", reason:$r}'
exit 0
