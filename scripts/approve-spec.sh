#!/usr/bin/env bash
# HUMAN STEP. Freezes docs/requirements.md and the acceptance tests after you have read them.
#   ./scripts/approve-spec.sh                     first approval (spec-v1)
#   ./scripts/approve-spec.sh --amend "reason"    re-approve after you changed requirements/tests
# Claude is denied this command unless SELF_APPROVE=1 in .factory/factory.conf.
set -euo pipefail
# shellcheck source=_spec.sh
source "$(dirname "${BASH_SOURCE[0]}")/_spec.sh"
cd "$SPEC_ROOT"

AMEND=""; YES=0
while (( $# )); do
  case "$1" in
    --amend) AMEND="${2:-}"; [[ -z "$AMEND" ]] && { echo "--amend needs a reason"; exit 1; }; shift 2 ;;
    --yes) YES=1; shift ;;
    *) echo "unknown argument: $1"; exit 1 ;;
  esac
done

git rev-parse --git-dir >/dev/null 2>&1 || { echo "not a git repository"; exit 1; }
[[ -f docs/requirements.md ]] || { echo "docs/requirements.md missing"; exit 1; }

if spec_is_locked && [[ -z "$AMEND" ]]; then
  echo "Spec is already approved as $(cat "$SPEC_LOCK_DIR/version"). Use --amend \"reason\" to re-approve a change."
  exit 1
fi

if grep -nE '<(FILL|TODO)[^>]*>' docs/requirements.md docs/design.md 2>/dev/null; then
  echo "Template placeholders are still present (lines above). Finish the spec first."; exit 1
fi

echo "== Traceability"
"$SPEC_ROOT/scripts/check-traceability.sh"

SLN="$(ls -1 ./*.slnx ./*.sln 2>/dev/null | head -1 || true)"
if [[ -n "$SLN" && -d tests/App.AcceptanceTests ]]; then
  echo "== Acceptance tests compile (they are expected to FAIL until implemented)"
  dotnet build tests/App.AcceptanceTests -nologo -v q
fi

files="$(spec_list_files)"
echo
echo "== Files that will be locked ($(wc -l <<<"$files")):"
sed 's/^/  /' <<<"$files"
echo
echo "Read docs/requirements.md and skim the acceptance tests before approving."
echo "After approval Claude cannot change them; only you can, with --amend."
if (( YES == 0 )); then
  read -r -p "Type APPROVE to lock the spec: " answer
  [[ "$answer" == "APPROVE" ]] || { echo "Not approved."; exit 1; }
fi

prev="$(cat "$SPEC_LOCK_DIR/version" 2>/dev/null || echo v0)"
next="v$(( ${prev#v} + 1 ))"
spec_write_manifest
echo "$next" >"$SPEC_LOCK_DIR/version"
printf '%s\t%s\t%s\t%s\n' "$next" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(git config user.email || echo unknown)" "${AMEND:-initial approval}" \
  >>"$SPEC_LOCK_DIR/history.tsv"

git add -A
git commit -q -m "spec: approve $next${AMEND:+ - $AMEND}"
git tag -a "spec-$next" -m "Spec approved: $next${AMEND:+ - $AMEND}"

echo
echo "Spec locked as $next (git tag spec-$next)."
echo
echo "Next: in Claude Code (auto mode), run the build loop with:"
echo
cat "$SPEC_ROOT/.factory/goal.txt"
