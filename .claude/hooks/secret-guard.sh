#!/usr/bin/env bash
# PreToolUse guard: blocks writing real secrets into the repo.
set -uo pipefail
HOOK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "$HOOK_DIR/lib.sh"

input="$(cat)"
path="$(jq -r '.tool_input.file_path // ""' <<<"$input")"
content="$(jq -r '[.tool_input.content, .tool_input.new_string, (.tool_input.edits // [] | .[].new_string)] | map(select(. != null)) | join("\n")' <<<"$input")"
base="$(basename "$path")"

# Real env files never get written by the agent; placeholders go in .env.example.
if [[ "$base" == ".env" || ( "$base" == .env.* && "$base" != ".env.example" ) ]]; then
  deny "Do not write real environment files ($base). Put placeholder keys in .env.example and read real values from environment variables / user-secrets."
fi

PATTERN_FILE="$FACTORY_DIR/secret-patterns.txt"
[[ -f "$PATTERN_FILE" ]] || exit 0
while IFS= read -r line; do
  [[ -z "$line" || "$line" == \#* ]] && continue
  name="${line%%|*}"; re="${line#*|}"
  if grep -qE -- "$re" <<<"$content"; then
    deny "Possible secret detected ($name) in $base. Never hardcode secrets: read them from configuration/environment variables and document the key name in .env.example."
  fi
done <"$PATTERN_FILE"
exit 0
