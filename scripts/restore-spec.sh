#!/usr/bin/env bash
# Returns every locked file to the last approved version (undoes accidental edits). Safe for Claude to run.
set -euo pipefail
# shellcheck source=_spec.sh
source "$(dirname "${BASH_SOURCE[0]}")/_spec.sh"
cd "$SPEC_ROOT"

spec_is_locked || { echo "Spec is not locked; nothing to restore."; exit 0; }
tag="spec-$(cat "$SPEC_LOCK_DIR/version")"
git rev-parse -q --verify "refs/tags/$tag" >/dev/null || { echo "Tag $tag not found."; exit 1; }

for p in $(spec_locked_paths); do
  git cat-file -e "$tag:${p%/}" 2>/dev/null && git checkout "$tag" -- "${p%/}"
done

listed="$(cut -c67- "$SPEC_MANIFEST" | csort)"
ccomm -13 <(printf '%s\n' "$listed") <(spec_list_files) | sed '/^$/d' | while IFS= read -r extra; do
  echo "removing file added after approval: $extra"
  rm -f -- "$extra"
done

spec_check_integrity
