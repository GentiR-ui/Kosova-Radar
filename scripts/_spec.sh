#!/usr/bin/env bash
# Shared spec-lock helpers (sourced by approve-spec.sh, verify.sh, restore-spec.sh).
# Portable: bash 3.2 (macOS) and GNU/BSD tools.

# Byte-order sort/comm so results are identical on every machine (scoped: never leaks into dotnet/npm).
csort() { LC_ALL=C sort "$@"; }
ccomm() { LC_ALL=C comm "$@"; }

SPEC_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SPEC_CONF="$SPEC_ROOT/.factory/factory.conf"
SPEC_LOCK_DIR="$SPEC_ROOT/.factory/spec-lock"
SPEC_MANIFEST="$SPEC_LOCK_DIR/manifest.sha256"

spec_conf() {
  local key="$1" def="${2:-}" v
  v="$(grep -E "^${key}=" "$SPEC_CONF" 2>/dev/null | tail -1 | cut -d= -f2-)"
  v="${v%\"}"; v="${v#\"}"
  printf '%s' "${v:-$def}"
}

spec_locked_paths() { spec_conf LOCKED_PATHS "docs/requirements.md tests/App.AcceptanceTests/ web/e2e/acceptance/"; }

# sha256_of FILE -> hex digest (sha256sum on Linux, shasum on macOS)
sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum -- "$1" | cut -d' ' -f1
  else shasum -a 256 -- "$1" | cut -d' ' -f1; fi
}

# All files currently covered by the lock, relative to the repo root, sorted.
spec_list_files() {
  local p
  (
    cd "$SPEC_ROOT" || exit 1
    for p in $(spec_locked_paths); do
      if [[ "$p" == */ ]]; then
        if [[ -d "$p" ]]; then
          find "${p%/}" -type f \
            -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' \
            -not -path '*/test-results/*' -not -path '*/playwright-report/*' -not -path '*/TestResults/*' \
            -not -name '.DS_Store'
        fi
      else
        [[ -f "$p" ]] && printf '%s\n' "$p"
      fi
    done | csort
  )
}

spec_write_manifest() { # writes "<sha256>  <path>" lines for every locked file
  local f
  mkdir -p "$SPEC_LOCK_DIR"
  (
    cd "$SPEC_ROOT" || exit 1
    spec_list_files | while IFS= read -r f; do printf '%s  %s\n' "$(sha256_of "$f")" "$f"; done
  ) >"$SPEC_MANIFEST"
}

spec_is_locked() { [[ -f "$SPEC_MANIFEST" ]]; }

# Prints problems and returns 1 if locked files changed, appeared or disappeared since approval.
# On success the LAST line is always "spec integrity OK (...)".
spec_check_integrity() {
  spec_is_locked || { echo "spec integrity: not locked yet (run scripts/approve-spec.sh after the spec phase)"; return 0; }
  local status=0 hash f changed="" listed current extra
  while IFS= read -r line; do
    hash="${line%%  *}"; f="${line#*  }"
    if [[ ! -f "$SPEC_ROOT/$f" ]]; then changed+="  $f: MISSING"$'\n'
    elif [[ "$(sha256_of "$SPEC_ROOT/$f")" != "$hash" ]]; then changed+="  $f: MODIFIED"$'\n'; fi
  done <"$SPEC_MANIFEST"
  if [[ -n "$changed" ]]; then
    echo "spec integrity: approved files were changed:"; printf '%s' "$changed"; status=1
  fi
  listed="$(cut -c67- "$SPEC_MANIFEST" | csort)"
  current="$(spec_list_files)"
  extra="$(ccomm -13 <(printf '%s\n' "$listed") <(printf '%s\n' "$current") | sed '/^$/d')"
  if [[ -n "$extra" ]]; then
    echo "spec integrity: files added to locked folders after approval:"; printf '%s\n' "$extra" | sed 's/^/  /'; status=1
  fi
  if (( status == 0 )); then
    echo "spec integrity OK ($(wc -l <"$SPEC_MANIFEST" | tr -d ' ') locked files, $(cat "$SPEC_LOCK_DIR/version" 2>/dev/null || echo '?'))"
  else
    echo "Fix: run ./scripts/restore-spec.sh to return locked files to the approved version."
  fi
  return $status
}

# Human-granted waivers: .factory/waivers lines "REQ-004 | reason". Prints the ids, one per line.
spec_waived_ids() {
  [[ -f "$SPEC_ROOT/.factory/waivers" ]] || return 0
  grep -oE '^[[:space:]]*REQ-(P[0-9]{2}|[0-9]{3})' "$SPEC_ROOT/.factory/waivers" | tr -d ' \t' | csort -u
}
