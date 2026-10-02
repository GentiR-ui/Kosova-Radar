#!/usr/bin/env bash
# The single verification gate. Claude, the Stop hook, CI and the human all run this same script.
#   scripts/verify.sh           full gate (before marking the project done / before release)
#   scripts/verify.sh --quick   fast gate (after each task; used by the Stop hook)
# The report always shows the spec-integrity line, and the last line is "VERIFY: PASS" or "VERIFY: FAIL",
# so the /goal evaluator can judge the result from the transcript.
# Portable: bash 3.2 (macOS) and GNU/BSD tools.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT" || exit 1
# shellcheck source=_spec.sh
source "$ROOT/scripts/_spec.sh"

MODE="full"
[[ "${1:-}" == "--quick" ]] && MODE="quick"

STATE="$ROOT/.factory/state"
LOG_DIR="$STATE/verify"
mkdir -p "$LOG_DIR"
rm -rf "${LOG_DIR:?}"/*

results=(); failed_logs=(); fail=0
run() {
  local name="$1"; shift
  local log="$LOG_DIR/$name.log" start=$SECONDS
  if "$@" >"$log" 2>&1; then
    results+=("PASS  $name ($((SECONDS - start))s)")
  else
    results+=("FAIL  $name"); failed_logs+=("$log"); fail=1
  fi
}
skip() { results+=("SKIP  $1 - $2"); }
hard_fail() { results+=("FAIL  $1 - $2"); fail=1; }
in_web() { (cd "$ROOT/web" && "$@"); }

SLN="$(ls -1 ./*.slnx ./*.sln 2>/dev/null | head -1)"
HAS_WEB=0; [[ -f web/package.json ]] && HAS_WEB=1
DB_OK=0
if [[ -n "${TEST_DB_CONNECTION:-}" ]] || docker info >/dev/null 2>&1; then DB_OK=1; fi
ALLOW_DOCKER_SKIP="$(spec_conf ALLOW_DOCKER_SKIP 0)"
WAIVED="$(spec_waived_ids | tr '\n' ' ')"; WAIVED="${WAIVED% }"
ACC_CS_DIR="tests/App.AcceptanceTests"
ACC_PW_CONFIG="e2e/acceptance/playwright.config.ts"

no_db() { # stage name
  if [[ "$ALLOW_DOCKER_SKIP" == "1" ]]; then skip "$1" "no Docker / TEST_DB_CONNECTION (allowed by factory.conf)"
  else hard_fail "$1" "needs Docker or TEST_DB_CONNECTION - record in docs/BLOCKERS.md if unavailable"; fi
}

is_waived() { [[ " $WAIVED " == *" $1 "* ]]; }

# Number of acceptance test declarations that must run: one per test declaration line
# ([Fact, Trait("Req", ...)] / test('REQ-...')), skipping lines that mention a waived requirement.
count_declared() { # grep-regex files...
  local re="$1" n=0 line id ok; shift
  while IFS= read -r line; do
    ok=1
    for id in $(grep -oE 'REQ-(P[0-9]{2}|[0-9]{3})' <<<"$line"); do is_waived "$id" && ok=0; done
    n=$((n + ok))
  done < <(grep -hvE '^[[:space:]]*//' "$@" 2>/dev/null | grep -E "$re")
  echo "$n"
}
declared_cs() {
  local -a files=(); local f
  while IFS= read -r f; do files+=("$f"); done < <(find "$ACC_CS_DIR" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null)
  (( ${#files[@]} )) || { echo 0; return; }
  count_declared '\[(Fact|Theory).*Trait\("Req",[[:space:]]*"REQ-' "${files[@]}"
}
declared_pw() {
  local -a files=(); local f
  while IFS= read -r f; do files+=("$f"); done < <(find web/e2e/acceptance -name '*.ts' -not -name 'playwright.config.ts' 2>/dev/null)
  (( ${#files[@]} )) || { echo 0; return; }
  count_declared "(^|[^.A-Za-z_])test\\([[:space:]]*['\"\`]REQ-" "${files[@]}"
}

acceptance_api() {
  local filter=() trx="$LOG_DIR/acceptance.trx" id passed declared
  if [[ -n "$WAIVED" ]]; then
    local f="" ; for id in $WAIVED; do f+="${f:+&}Req!=$id"; done
    filter=(--filter "$f"); echo "waived: $WAIVED"
  fi
  dotnet test "$ACC_CS_DIR" --no-build -nologo ${filter[@]+"${filter[@]}"} \
    --logger "trx;LogFileName=$trx" || return 1
  passed="$(grep -oE 'passed="[0-9]+"' "$trx" | head -1 | grep -oE '[0-9]+')"
  declared="$(declared_cs)"
  echo "acceptance (API): passed=${passed:-0} declared=$declared"
  if (( ${passed:-0} < declared )); then
    echo "Fewer acceptance tests passed than are declared: tests were skipped, excluded or not discovered."
    return 1
  fi
}

acceptance_e2e() {
  local json="$LOG_DIR/e2e-acceptance.json" grepinv=() expected skipped unexpected declared projects
  docker info >/dev/null 2>&1 && docker compose up -d --wait db
  if [[ -n "$WAIVED" ]]; then grepinv=(--grep-invert "$(tr ' ' '|' <<<"$WAIVED")"); echo "waived: $WAIVED"; fi
  # CI=1: never reuse a server already listening on the ports (could be stale or fake).
  CI=1 PLAYWRIGHT_JSON_OUTPUT_NAME="$json" PLAYWRIGHT_JSON_OUTPUT_FILE="$json" \
    in_web npx playwright test -c "$ACC_PW_CONFIG" --reporter=list,json ${grepinv[@]+"${grepinv[@]}"} || return 1
  expected="$(jq '.stats.expected + (.stats.flaky // 0)' "$json")"
  skipped="$(jq '.stats.skipped' "$json")"; unexpected="$(jq '.stats.unexpected' "$json")"
  projects="$(jq '.config.projects | length' "$json")"
  declared="$(declared_pw)"
  echo "acceptance (UI): passed=$expected skipped=$skipped failed=$unexpected declared=$declared x $projects projects"
  (( unexpected == 0 && skipped == 0 && expected >= declared * projects ))
}

e2e_flows() {
  CI=1 in_web npx playwright test --pass-with-no-tests
}

dotnet_vulnerable() {
  # Shipped code only (src/App.Api pulls in every src project transitively); test-only packages do not ship.
  local out rc
  out="$(dotnet list src/App.Api package --vulnerable --include-transitive 2>&1)"; rc=$?
  echo "$out"
  (( rc == 0 )) && ! grep -q "has the following vulnerable packages" <<<"$out"
}

secrets_scan() {
  if command -v gitleaks >/dev/null 2>&1; then
    gitleaks detect --source . --no-banner --redact
    return $?
  fi
  local found=0 name re hits line
  local -a files=()
  while IFS= read -r line; do files+=("$line"); done < <(
    git ls-files -co --exclude-standard 2>/dev/null | grep -v -E '(^\.factory/secret-patterns\.txt$|node_modules/|package-lock\.json$)')
  (( ${#files[@]} )) || { echo "no files to scan"; return 0; }
  while IFS= read -r line; do
    [[ -z "$line" || "$line" == \#* ]] && continue
    name="${line%%|*}"; re="${line#*|}"
    hits="$(grep -l -E -- "$re" "${files[@]}" 2>/dev/null)"
    if [[ -n "$hits" ]]; then echo "possible $name in:"; sed 's/^/  /' <<<"$hits"; found=1; fi
  done <"$ROOT/.factory/secret-patterns.txt"
  (( found == 0 )) && echo "no secrets found (built-in patterns; install gitleaks for deeper scanning)"
  return $found
}

# ---------------------------------------------------------------- stages
run spec-integrity spec_check_integrity
run traceability "$ROOT/scripts/check-traceability.sh"

if [[ -n "$SLN" ]]; then
  run dotnet-build dotnet build "$SLN" -nologo
  if [[ -d tests/App.UnitTests ]]; then run unit-tests dotnet test tests/App.UnitTests --no-build -nologo
  else hard_fail unit-tests "tests/App.UnitTests missing"; fi
else
  hard_fail dotnet "no solution found - run scripts/scaffold.sh"
fi

if (( HAS_WEB )); then
  run web-typecheck in_web npm run typecheck
  run web-unit-tests in_web npm run test
else
  hard_fail web "web/package.json missing - run scripts/scaffold.sh"
fi

if [[ "$MODE" == "full" ]]; then
  if [[ -n "$SLN" ]]; then
    run dotnet-format dotnet format "$SLN" --verify-no-changes --exclude "$ACC_CS_DIR"
    if (( DB_OK )); then
      [[ -d tests/App.IntegrationTests ]] && run integration-tests dotnet test tests/App.IntegrationTests --no-build -nologo
      run acceptance-api acceptance_api
    else
      no_db integration-tests; no_db acceptance-api
    fi
    run dotnet-vulnerable dotnet_vulnerable
  fi
  if (( HAS_WEB )); then
    run web-lint in_web npm run lint
    run web-build in_web npm run build
    run npm-audit in_web npm audit --omit=dev --audit-level=high
    if (( DB_OK )); then
      run e2e-acceptance acceptance_e2e
      run e2e-flows e2e_flows
    else
      no_db e2e-acceptance
    fi
  fi
  run secrets-scan secrets_scan
fi

# ---------------------------------------------------------------- report
{
  echo "== verify ($MODE) $(date -u +%Y-%m-%dT%H:%M:%SZ) @ $(git rev-parse --short HEAD 2>/dev/null || echo no-git)"
  printf '%s\n' "${results[@]}"
  echo "spec: $(tail -n 1 "$LOG_DIR/spec-integrity.log" 2>/dev/null)"
  [[ -n "$WAIVED" ]] && echo "waived by human (.factory/waivers): $WAIVED"
  for f in "$LOG_DIR"/acceptance-api.log "$LOG_DIR"/e2e-acceptance.log; do
    [[ -f "$f" ]] && grep -h '^acceptance (' "$f"
  done
} | tee "$STATE/last-verify.txt"

for log in ${failed_logs[@]+"${failed_logs[@]}"}; do
  echo
  echo "---- $(basename "$log" .log) (last 40 lines; full log: ${log#"$ROOT"/})"
  tail -n 40 "$log"
done

echo
if (( fail )); then echo "VERIFY: FAIL"; exit 1; fi
echo "VERIFY: PASS"
