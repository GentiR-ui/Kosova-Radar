#!/usr/bin/env bash
# Runs Kosova Radar on localhost:
#   PostgreSQL (Docker) + API on http://localhost:5080 (Development) + web on http://localhost:5173
#
#   bash scripts/dev.sh        then open http://localhost:5173  and  http://localhost:5173/admin
#
# .env (git-ignored) is loaded first, so Ai__Provider=Gemini + Ai__ApiKey use the real Gemini API instead of
# the Fake provider. Stop with Ctrl+C. The database keeps its data in a Docker volume
# (`docker compose stop db` stops it).
#   NO_AI=1 bash scripts/dev.sh   ignore the AI settings of .env (Fake provider), e.g. before the Gemini task is built
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT" || exit 1
# shellcheck source=_dotenv.sh
source "$ROOT/scripts/_dotenv.sh"

command -v dotnet >/dev/null 2>&1 || { echo ".NET SDK not found in PATH."; exit 1; }
command -v npm >/dev/null 2>&1 || { echo "Node.js/npm not found in PATH."; exit 1; }
docker info >/dev/null 2>&1 || { echo "Docker is not running: start Docker Desktop and run this again."; exit 1; }

load_dotenv .env
[[ "${NO_AI:-0}" == "1" ]] && unset Ai__Provider Ai__ApiKey Ai__Model
echo "AI provider: ${Ai__Provider:-Fake (no .env)}"
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Default="${ConnectionStrings__Default:-Host=localhost;Port=5432;Database=app;Username=app;Password=app}"

echo "== Database"
docker compose up -d --wait db || exit 1

echo "== Web dependencies"
[[ -d web/node_modules ]] || (cd web && npm ci) || exit 1

echo "== API (build + start on :5080)"
dotnet build src/App.Api -nologo -v q || exit 1
dotnet run --project src/App.Api --no-build --urls http://localhost:5080 &
api_pid=$!
stop_api() {
  kill "$api_pid" 2>/dev/null
  # On Windows `dotnet run` leaves the App.Api process behind when its parent is killed.
  command -v taskkill >/dev/null 2>&1 && taskkill //F //IM App.Api.exe >/dev/null 2>&1
  wait "$api_pid" 2>/dev/null
}
trap stop_api EXIT
trap 'exit 130' INT TERM

for _ in $(seq 1 90); do
  curl -fsS http://localhost:5080/health >/dev/null 2>&1 && break
  kill -0 "$api_pid" 2>/dev/null || { echo "The API stopped during startup (see the output above)."; exit 1; }
  sleep 2
done
curl -fsS http://localhost:5080/health >/dev/null 2>&1 || { echo "The API did not become healthy within 3 minutes."; exit 1; }

cat <<'MSG'

  Kosova Radar is running:
    Public site   http://localhost:5173
    Admin         http://localhost:5173/admin
                  admin@kosovaradar.local / Radar-admin-dev-2026   (Development only)
    Sources       Admin → Burimet → "Prishtina.com — Evente" / "Ka' Po Dalim" → "Merr tani"
                  then Admin → Shqyrtimi to approve what you want public.

  Ctrl+C stops the API and the web server.

MSG
cd web && npm run dev
