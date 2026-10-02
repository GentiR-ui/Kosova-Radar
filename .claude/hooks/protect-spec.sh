#!/usr/bin/env bash
# PreToolUse guard for the spec lock.
#  - Always-protected files (factory config, gate scripts, build props, .claude/): only the human edits them.
#  - Locked files (requirements + acceptance tests): frozen once scripts/approve-spec.sh ran.
#  - Before the lock, acceptance-test folders are writable only by the test-author subagent.
#  - The test-author may not read implementation code (tests come from the spec, not the code).
# Bash analysis is a heuristic guardrail, not a security boundary: scripts/verify.sh re-checks the
# locked files by checksum and counts executed acceptance tests, which catches anything that slips past.

set -uo pipefail
HOOK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "$HOOK_DIR/lib.sh"
need_jq_and_root

input="$(cat)"
tool="$(jq -r '.tool_name // ""' <<<"$input")"
agent="$(jq -r '.agent_type // ""' <<<"$input")"

read -r -a LOCKED <<<"$(conf LOCKED_PATHS "docs/requirements.md tests/App.AcceptanceTests/ web/e2e/acceptance/")"
read -r -a ALWAYS <<<"$(conf ALWAYS_PROTECTED ".factory/spec-lock/ .factory/factory.conf .factory/secret-patterns.txt .factory/waivers .factory/goal.txt scripts/ .claude/ *Directory.Build.props *Directory.Build.targets *Directory.Build.rsp *Directory.Packages.props")"
read -r -a IMPL <<<"$(conf IMPLEMENTATION_PATHS "src/ web/src/")"
LOCKED_TESTS=()
for p in "${LOCKED[@]}"; do [[ "$p" != docs/* ]] && LOCKED_TESTS+=("$p"); done

BLOCKER_HINT="If you believe this file is wrong, write the evidence in docs/BLOCKERS.md, ask the test-author subagent to confirm against docs/requirements.md, and continue with other tasks. Never weaken or skip a test to get green."

# check_write REL -> denies if writing REL is not allowed for this agent/phase
check_write() {
  local rel="$1"
  [[ -z "$rel" ]] && return 0
  if matches_any "$rel" "${ALWAYS[@]}"; then
    deny "PROTECTED: $rel is factory infrastructure and only the human may change it. Record what you need in docs/BLOCKERS.md and continue."
  fi
  if matches_any "$rel" "${LOCKED[@]}"; then
    if is_locked; then
      deny "LOCKED: $rel is part of the approved spec (see .factory/spec-lock). To undo accidental changes run ./scripts/restore-spec.sh. $BLOCKER_HINT"
    fi
    if (( ${#LOCKED_TESTS[@]} )) && matches_any "$rel" "${LOCKED_TESTS[@]}" && [[ "$agent" != "test-author" ]]; then
      deny "Acceptance tests may only be written by the test-author subagent (delegate to it). Unit/integration tests go in tests/App.UnitTests, tests/App.IntegrationTests or web/src/**/*.test.ts(x); extra browser flows in web/e2e/flows."
    fi
  fi
  return 0
}

check_read() { # REL -> test-author may not read implementation code
  local rel="$1"
  if [[ "$agent" == "test-author" ]] && matches_any "$rel" "${IMPL[@]}"; then
    deny "test-author must stay independent of the implementation: derive tests from docs/requirements.md and the API/UI contracts in docs/design.md only."
  fi
  return 0
}

# ---------------------------------------------------------------- Bash command analysis
# Runs in one subshell; helpers set globals instead of forking (hooks have a timeout).
CUR_BASE="$ROOT_L"
LIST_RE='[[:space:]](-l|--list)[[:space:]]'
INPLACE_RE='^-[A-Za-z]*i'
ASSIGN_RE='^[A-Za-z_][A-Za-z0-9_]*='
emit() { printf '%s\t%s\n' "$1" "$2"; }
unquote() { _UQ="${1//\"/}"; _UQ="${_UQ//\'/}"; }
resolve() { # token -> sets _RES to a repo-relative path ("" for things that are not paths)
  _RES=""
  unquote "$1"
  [[ -z "$_UQ" || "$_UQ" == -* || "$_UQ" == *'$'* || "$_UQ" == '~'* ]] && return 0
  relpath_v "$_UQ" "$CUR_BASE"; _RES="$_REL"
}
emit_writes() { local tok; for tok in "$@"; do resolve "$tok"; [[ -n "$_RES" ]] && emit W "$_RES"; done; return 0; }

analyze_bash() {
  local cmd="$1" seg seg2 r i name sub tok has_inplace
  local -a w
  # Drop heredoc bodies: their lines are data, not commands (keeps "cat > f <<EOF" itself).
  cmd="$(printf '%s\n' "$cmd" | perl -0pe 's/<<-?[ \t]*([\x27"]?)(\w+)\1([^\n]*)\n.*?\n[ \t]*\2[ \t]*(?=\n|$)/<<$2$3/gs')"
  while IFS= read -r seg; do
    seg="${seg#"${seg%%[![:space:]]*}"}"
    [[ -z "$seg" ]] && continue

    # Redirection targets: "> f", ">> f", "2> f", "&> f" (but not "2>&1").
    while IFS= read -r r; do
      [[ -n "$r" ]] && emit_writes "$r"
    done < <(grep -oE '>>?[[:space:]]*[^&[:space:];|<>][^[:space:];|<>]*' <<<"$seg" | sed -E 's/^>>?[[:space:]]*//')

    seg2="$(sed -E 's/[0-9]*&?>>?[[:space:]]*[^[:space:];|<>]+//g' <<<"$seg")"
    read -r -a w <<<"$seg2"
    (( ${#w[@]} )) || continue
    i=0
    while (( i < ${#w[@]} )); do
      case "${w[i]}" in
        sudo|env|command|builtin|nohup|time|nice|exec) i=$((i + 1)) ;;
        timeout) i=$((i + 2)) ;;
        *=*) if [[ "${w[i]}" =~ $ASSIGN_RE ]]; then i=$((i + 1)); else break; fi ;;
        *) break ;;
      esac
    done
    (( i < ${#w[@]} )) || continue
    unquote "${w[i]}"; name="${_UQ##*/}"

    # Executing (not just reading) the approval script in any form: ./x, bash -x x, sh -c "x", source x.
    case "$name" in
      cat|grep|egrep|rg|head|tail|less|more|wc|diff|ls|git|shellcheck|bat|file|stat|echo|printf) ;;
      *) for tok in "${w[@]:i}"; do unquote "$tok"; [[ "${_UQ##*/}" == "approve-spec.sh" ]] && emit X approve; done ;;
    esac

    if [[ "$agent" == "test-author" ]]; then
      for tok in "${w[@]:i}"; do resolve "$tok"; [[ -n "$_RES" ]] && emit R "$_RES"; done
    fi

    case "$name" in
      cd)
        if (( i + 1 < ${#w[@]} )); then
          unquote "${w[i+1]}"
          if [[ "$_UQ" == /* ]]; then normpath_v "$_UQ"; else normpath_v "$CUR_BASE/$_UQ"; fi
          CUR_BASE="$_NP"
        else
          CUR_BASE="$ROOT_L"
        fi ;;
      git)
        sub="${w[i+1]:-}"
        case "$sub" in
          tag)
            if (( i + 2 < ${#w[@]} )) && ! [[ " ${w[*]:i+2} " =~ $LIST_RE ]]; then emit X gittag; fi ;;
          checkout|restore|rm|mv|clean|apply|switch) emit_writes "${w[@]:i+2}" ;;
        esac ;;
      rm|unlink|truncate|touch|shred|tee|mkdir|rmdir|mv) emit_writes "${w[@]:i+1}" ;;
      chmod|chown) emit_writes "${w[@]:i+2}" ;;
      cp|install|ln|rsync) emit_writes "${w[${#w[@]}-1]}" ;;
      dd)
        for tok in "${w[@]:i+1}"; do [[ "$tok" == of=* ]] && emit_writes "${tok#of=}"; done ;;
      sed|perl|gsed)
        has_inplace=0
        for tok in "${w[@]:i+1}"; do
          if [[ "$tok" =~ $INPLACE_RE || "$tok" == --in-place* ]]; then has_inplace=1; fi
        done
        if (( has_inplace )); then emit_writes "${w[@]:i+1}"; fi ;;
      python|python3|node|ruby|deno|bun)
        if grep -qE 'open\(|write|unlink|rename|remove|rmSync|copy|replace' <<<"$seg"; then
          while IFS= read -r tok; do emit_writes "$tok"; done \
            < <(grep -oE '[A-Za-z0-9_.][A-Za-z0-9_./-]*/[A-Za-z0-9_./-]+' <<<"$seg")
        fi ;;
    esac
  done < <(printf '%s\n' "$cmd" | perl -pe 's/&&|\|\||;|\|/\n/g')
  return 0
}

# ---------------------------------------------------------------- dispatch
case "$tool" in
  Edit|Write|NotebookEdit|MultiEdit)
    path="$(jq -r '.tool_input.file_path // .tool_input.notebook_path // ""' <<<"$input")"
    check_write "$(relpath "$path")"
    ;;

  Read|Grep|Glob)
    if [[ "$agent" == "test-author" ]]; then
      path="$(jq -r '.tool_input.file_path // .tool_input.path // ""' <<<"$input")"
      if [[ -z "$path" && "$tool" != "Read" ]]; then
        deny "test-author: pass an explicit path (docs/, tests/App.AcceptanceTests/ or web/e2e/acceptance/) so the search cannot reach implementation code."
      fi
      check_read "$(relpath "$path")"
    fi
    ;;

  Bash)
    cmd="$(jq -r '.tool_input.command // ""' <<<"$input")"
    analysis="$(analyze_bash "$cmd")"
    while IFS=$'\t' read -r kind target; do
      case "$kind" in
        X)
          if [[ "$target" == approve && "$(conf SELF_APPROVE 0)" != "1" ]]; then
            deny "Only the human approves the spec (scripts/approve-spec.sh). Finish the spec phase, summarize it for the user in Albanian, and stop."
          fi
          if [[ "$target" == gittag ]]; then
            deny "git tags are reserved for spec approvals and releases made by the human."
          fi ;;
        W) check_write "$target" ;;
        R) check_read "$target" ;;
      esac
    done <<<"$analysis"
    ;;
esac
exit 0
