#!/usr/bin/env bash
# Creates a new product repository from this starter kit (committed files only).
#   ./scripts/new-project.sh ../invoicer
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="${1:-}"
[[ -n "$target" ]] || { echo "usage: $0 <target-directory>"; exit 1; }
[[ -e "$target" && -n "$(ls -A "$target" 2>/dev/null)" ]] && { echo "$target exists and is not empty"; exit 1; }

cd "$ROOT"
git rev-parse --verify HEAD >/dev/null 2>&1 || { echo "commit the starter kit first"; exit 1; }
kit_version="$(git describe --tags --always 2>/dev/null || git rev-parse --short HEAD)"

mkdir -p "$target"
git archive HEAD | tar -x -C "$target"
cd "$target"
rm -rf .factory/spec-lock .factory/state
chmod +x scripts/*.sh .claude/hooks/*.sh
git init -q -b main
git add -A
git commit -q -m "chore: start from saas-starter $kit_version"
echo "Created $(pwd) from saas-starter $kit_version."
echo "Next: cd $(pwd) && claude   then describe the product idea (the new-saas skill takes it from there)."
