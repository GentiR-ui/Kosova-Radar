#!/usr/bin/env bash
# Every requirement in docs/requirements.md must be covered by at least one acceptance test,
# and every acceptance test must point at a real requirement.
#   Requirement ids: REQ-P01 (platform) or REQ-001 (product).
#   A requirement line tagged [manual] is excluded (checked in the release skill instead).
#   Only real test declarations count:
#     C#          [Fact, Trait("Req", "REQ-001")]   (or [Theory, ...])
#     Playwright  test('REQ-001 ...', ...)          (test.skip/test.fixme do not count)
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT" || exit 1
# shellcheck source=_spec.sh
source "$ROOT/scripts/_spec.sh"

REQ_FILE="docs/requirements.md"
ID_RE='REQ-(P[0-9]{2}|[0-9]{3})'
[[ -f "$REQ_FILE" ]] || { echo "traceability: $REQ_FILE is missing"; exit 1; }

nonempty() { sed '/^$/d'; }

# Requirement text without HTML comments (template examples live in comments).
req_text="$(perl -0pe 's/<!--.*?-->//gs' "$REQ_FILE")"
all_ids="$(grep -oE "$ID_RE" <<<"$req_text" | csort -u)"
manual_ids="$(grep -E "$ID_RE" <<<"$req_text" | grep -F '[manual]' | grep -oE "$ID_RE" | csort -u)"
needed="$(ccomm -23 <(printf '%s\n' "$all_ids" | nonempty) <(printf '%s\n' "$manual_ids" | nonempty))"

if [[ -z "$all_ids" ]]; then
  echo "traceability: no requirement ids found in $REQ_FILE"
  exit 1
fi

# Test declarations in the locked acceptance folders only.
tested="$(
  spec_list_files | while IFS= read -r f; do
    case "$f" in
      # Lines starting with // are commented out and do not count.
      *.cs) grep -vE '^[[:space:]]*//' "$f" | grep -oE "Trait\(\"Req\",[[:space:]]*\"$ID_RE\"" ;;
      *.ts) grep -vE '^[[:space:]]*//' "$f" | grep -oE "(^|[^.A-Za-z_])test\([[:space:]]*['\"\`]$ID_RE" ;;
    esac
  done | grep -oE "$ID_RE" | csort -u
)"

missing="$(ccomm -23 <(printf '%s\n' "$needed" | nonempty) <(printf '%s\n' "$tested" | nonempty))"
unknown="$(ccomm -13 <(printf '%s\n' "$all_ids" | nonempty) <(printf '%s\n' "$tested" | nonempty))"

n_all=$(printf '%s\n' "$all_ids" | nonempty | wc -l | tr -d ' ')
n_manual=$(printf '%s\n' "$manual_ids" | nonempty | wc -l | tr -d ' ')
status=0
if [[ -n "$missing" ]]; then
  echo "traceability: requirements without an acceptance test:"; sed 's/^/  - /' <<<"$missing"; status=1
fi
if [[ -n "$unknown" ]]; then
  echo "traceability: acceptance tests reference unknown requirements:"; sed 's/^/  - /' <<<"$unknown"; status=1
fi
(( status == 0 )) && echo "traceability OK: $n_all requirements ($n_manual manual), all automated ones covered"
exit $status
