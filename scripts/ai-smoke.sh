#!/usr/bin/env bash
# Live smoke test of the Gemini provider with the key from .env (one real API call). HUMAN STEP (B-001).
#   bash scripts/ai-smoke.sh
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT" || exit 1
# shellcheck source=_dotenv.sh
source "$ROOT/scripts/_dotenv.sh"
load_dotenv .env
[[ -n "${Ai__ApiKey:-}" ]] || { echo "Ai__ApiKey is not set in .env"; exit 1; }
dotnet test tests/App.UnitTests -nologo --filter "FullyQualifiedName~GeminiAiProviderTests.Live_smoke" \
  --logger "console;verbosity=normal"
