#!/usr/bin/env bash
# Put keel's stylesheet where the build expects it. Do not edit docs/keel.bundle.css
# by hand and do not commit it: it is a build output, gitignored, and produced from here.
#
#   ./update-keel.sh              # install the version pinned in docs/package-lock.json
#   ./update-keel.sh 0.2.3        # move the pin to that version, then install it
#
# The version lives in the lockfile, with an integrity hash. That is the whole point: a
# keel release cannot change this build, and upgrading is a reviewable one-line diff
# rather than 64KB of somebody else's CSS landing in this repository's history.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
DOCS="$HERE/.."
DEST="${DEST:-$DOCS/keel.bundle.css}"

# CI gets this from actions/setup-node; locally fall back to the gh CLI's token, which
# needs the read:packages scope. keel is on GitHub Packages, not the public registry.
export NODE_AUTH_TOKEN="${NODE_AUTH_TOKEN:-$(gh auth token 2>/dev/null || true)}"
if [ -z "$NODE_AUTH_TOKEN" ]; then
  echo "No NODE_AUTH_TOKEN and 'gh auth token' produced nothing; cannot read GitHub Packages." >&2
  exit 1
fi

cd "$DOCS"
if [ $# -gt 0 ]; then
  npm install --no-audit --no-fund --package-lock-only "@adamcoulteroz/keel@$1"
fi
npm ci --no-audit --no-fund

VERSION="$(node -p "require('./node_modules/@adamcoulteroz/keel/package.json').version")"
cp "node_modules/@adamcoulteroz/keel/dist/keel.css" "$DEST"
echo "keel $VERSION -> $DEST"
