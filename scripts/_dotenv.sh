# shellcheck shell=bash
# Exports KEY=value lines from .env (git-ignored) without evaluating them, so values may contain ; & $ or spaces.
# Windows line endings are tolerated. Comments and blank lines are skipped.
load_dotenv() { # file
  local file="${1:-.env}" line
  [[ -f "$file" ]] || return 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ "$line" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] || continue
    export "${line%%=*}=${line#*=}"
  done <"$file"
}
