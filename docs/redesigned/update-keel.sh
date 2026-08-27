#!/usr/bin/env bash
# Refresh the vendored copy of keel. Do not edit docs/keel.bundle.css by hand: run this.
set -euo pipefail

DEST="${1:-$(dirname "$0")/../keel.bundle.css}"
VERSION="${2:-}"

# Resolve the version first and then fetch AT that tag. Fetching the default
# branch and labelling it with the latest tag would stamp a version the content
# is not, which is worse than no stamp at all.
if [ -z "$VERSION" ]; then
  VERSION=$(gh api repos/AdamCoulterOz/keel/tags --jq '.[0].name')
fi

gh api "repos/AdamCoulterOz/keel/contents/src/Keel/wwwroot/keel.bundle.css?ref=$VERSION" \
  --jq '.content' | base64 -d > "$DEST"

printf '/* vendored from AdamCoulterOz/keel %s. Refresh with ./update-keel.sh, do not edit. */\n' \
  "$VERSION" | cat - "$DEST" > "$DEST.tmp" && mv "$DEST.tmp" "$DEST"

echo "keel $VERSION -> $DEST"
