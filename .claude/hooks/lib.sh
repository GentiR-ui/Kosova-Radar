#!/usr/bin/env bash
# Shared helpers for factory hooks. Sourced, not executed.
# Portable: bash 3.2 (macOS) and GNU/BSD tools. No realpath, no GNU-only flags.

# normpath_v PATH -> sets _NP to the path with "." and ".." removed (no symlink resolution, no subshell)
normpath_v() {
  local p="$1" seg n=0 i
  local -a parts out
  local IFS='/'
  read -r -a parts <<<"$p"
  for seg in ${parts[@]+"${parts[@]}"}; do
    case "$seg" in
      ''|.) ;;
      ..) if (( n > 0 )); then n=$((n - 1)); unset "out[$n]"; fi ;;
      *) out[n]="$seg"; n=$((n + 1)) ;;
    esac
  done
  _NP=""
  if (( n == 0 )); then _NP="/"; else for (( i = 0; i < n; i++ )); do _NP+="/${out[i]}"; done; fi
}
normpath() { normpath_v "$1"; printf '%s' "$_NP"; }

_PD="${CLAUDE_PROJECT_DIR:-$(pwd)}"
if [[ "$_PD" =~ ^[A-Za-z]: ]] && command -v cygpath >/dev/null 2>&1; then _PD="$(cygpath -u "$_PD")"; fi
ROOT_L="$(normpath "$_PD")"
ROOT_P="$(cd "$ROOT_L" 2>/dev/null && pwd -P || printf '%s' "$ROOT_L")"
ROOT="$ROOT_L"
FACTORY_DIR="$ROOT/.factory"
CONF_FILE="$FACTORY_DIR/factory.conf"
LOCK_MANIFEST="$FACTORY_DIR/spec-lock/manifest.sha256"
# shellcheck disable=SC2034  # used by hooks that source this file
STATE_DIR="$FACTORY_DIR/state"

# PreToolUse hooks fail closed: without jq or a project root they block every tool call loudly.
need_jq_and_root() {
  if ! command -v jq >/dev/null 2>&1; then
    echo "Factory hooks need jq (macOS: brew install jq; Linux: apt install jq). Blocking until it is installed." >&2
    exit 2
  fi
  if [[ ! -f "$CONF_FILE" ]]; then
    echo "Factory hooks cannot find $CONF_FILE (CLAUDE_PROJECT_DIR=$ROOT_L). Blocking." >&2
    exit 2
  fi
}

# conf KEY DEFAULT -> value from factory.conf (quotes stripped)
conf() {
  local key="$1" def="${2:-}" v
  v="$(grep -E "^${key}=" "$CONF_FILE" 2>/dev/null | tail -1 | cut -d= -f2-)"
  v="${v%\"}"; v="${v#\"}"
  printf '%s' "${v:-$def}"
}

is_locked() { [[ -f "$LOCK_MANIFEST" ]]; }

# relpath_v PATH [BASE] -> sets _REL to the path relative to the project root (absolute if outside it)
relpath_v() {
  local p="$1" base="${2:-$ROOT_L}" r
  _REL=""
  [[ -z "$p" ]] && return 0
  if [[ "$p" =~ ^[A-Za-z]: ]] && command -v cygpath >/dev/null 2>&1; then p="$(cygpath -u "$p")"; fi
  [[ "$p" != /* ]] && p="$base/$p"
  normpath_v "$p"
  for r in "$ROOT_L" "$ROOT_P"; do
    if [[ "$_NP" == "$r" ]]; then _REL="."; return 0; fi
    if [[ "$_NP" == "$r/"* ]]; then _REL="${_NP#"$r"/}"; return 0; fi
  done
  _REL="$_NP"
}
relpath() { relpath_v "$@"; printf '%s' "$_REL"; }

# matches_any REL PATTERN... -> 0 if REL equals a file pattern, sits under a dir pattern (ending "/"),
# or matches a glob pattern (containing "*"). Case-insensitive (macOS disks usually are).
matches_any() {
  local rel="$1" pat hit=1 restore=0; shift
  if ! shopt -q nocasematch; then shopt -s nocasematch; restore=1; fi
  for pat in "$@"; do
    if [[ "$pat" == *'*'* ]]; then
      # shellcheck disable=SC2053  # intentional glob match
      if [[ "$rel" == $pat ]]; then hit=0; break; fi
    elif [[ "$pat" == */ ]]; then
      if [[ "$rel/" == "$pat" || "$rel" == "$pat"* ]]; then hit=0; break; fi
    elif [[ "$rel" == "$pat" ]]; then
      hit=0; break
    fi
  done
  if (( restore )); then shopt -u nocasematch; fi
  return $hit
}

# text_mentions_any TEXT PATTERN... -> 0 if TEXT contains any pattern (trailing "/" ignored)
text_mentions_any() {
  local text="$1"; shift
  local pat
  for pat in "$@"; do
    [[ "$text" == *"${pat%/}"* ]] && return 0
  done
  return 1
}

# deny REASON -> print PreToolUse deny decision and exit
deny() {
  jq -n --arg r "$1" '{hookSpecificOutput:{hookEventName:"PreToolUse",permissionDecision:"deny",permissionDecisionReason:$r}}'
  exit 0
}
