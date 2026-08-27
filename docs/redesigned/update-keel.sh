#!/usr/bin/env bash
# Refresh the vendored copy of keel. Do not edit docs/keel.bundle.css by hand: run this.
#
#   ./update-keel.sh              # fetch the PINNED version in keel.version
#   ./update-keel.sh v0.2.2       # fetch that version AND record it as the new pin
#
# The pin is deliberate. Resolving "latest tag" at run time means the bundle changes
# whenever keel releases, with nothing in this repo recording that it did — harmless when
# a human runs it and looks at the diff, wrong from CI, where the build would silently
# move under you. Upgrading keel is an edit to keel.version, reviewable like any other.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PIN_FILE="$HERE/keel.version"
DEST="${DEST:-$HERE/../keel.bundle.css}"
VERSION="${1:-}"

if [ -z "$VERSION" ]; then
  [ -f "$PIN_FILE" ] || { echo "No version given and no pin at $PIN_FILE." >&2; exit 1; }
  VERSION="$(tr -d '[:space:]' < "$PIN_FILE")"
  PINNING=""
else
  PINNING="yes"
fi

# Fetch AT the tag. Fetching the default branch and labelling it with a tag would stamp a
# version the content is not, which is worse than no stamp at all.
gh api "repos/AdamCoulterOz/keel/contents/src/Keel/wwwroot/keel.bundle.css?ref=$VERSION" \
  --jq '.content' | base64 -d > "$DEST"

# keel stamps its own version into release assets, but the contents API serves the raw
# file, so stamp it here. Strip any existing stamp first so a re-run cannot double it.
TMP="$DEST.tmp"
grep -v '^/\* vendored from AdamCoulterOz/keel ' "$DEST" > "$TMP" || cp "$DEST" "$TMP"
printf '/* vendored from AdamCoulterOz/keel %s. Refresh with ./update-keel.sh, do not edit. */\n' \
  "$VERSION" | cat - "$TMP" > "$DEST"
rm -f "$TMP"

if [ -n "$PINNING" ]; then
  printf '%s\n' "$VERSION" > "$PIN_FILE"
  echo "keel $VERSION -> $DEST (pin updated)"
else
  echo "keel $VERSION -> $DEST (pinned)"
fi
