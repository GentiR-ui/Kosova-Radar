#!/usr/bin/env bash
# PostToolUse: format frontend files right after Claude edits them. Never blocks.
# C# formatting is checked by scripts/verify.sh (dotnet format is too slow per edit).
set -uo pipefail
HOOK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "$HOOK_DIR/lib.sh"

path="$(jq -r '.tool_input.file_path // ""')"
rel="$(relpath "$path")"
prettier="$ROOT/web/node_modules/.bin/prettier"

case "$rel" in
  web/e2e/acceptance/*) exit 0 ;;   # locked files are never reformatted
  web/*.ts|web/*.tsx|web/*.css|web/*.json|web/*.html)
    [[ -x "$prettier" ]] && (cd "$ROOT/web" && "$prettier" --write --log-level warn "${rel#web/}") >/dev/null 2>&1
    ;;
esac
exit 0
