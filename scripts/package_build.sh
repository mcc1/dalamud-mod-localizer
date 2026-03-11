#!/usr/bin/env bash
set -euo pipefail

: "${MOD_VERSION:?MOD_VERSION is required}"
: "${ARTIFACT_BASENAME:?ARTIFACT_BASENAME is required}"
: "${PACKAGE_BUILD_DIR:?PACKAGE_BUILD_DIR is required}"
: "${PACKAGE_INCLUDE_PATTERNS:?PACKAGE_INCLUDE_PATTERNS is required}"
: "${RELEASE_TAG_PREFIX:?RELEASE_TAG_PREFIX is required}"
: "${MOD_SHORT_REF:?MOD_SHORT_REF is required}"

runner_tmp="${RUNNER_TEMP:-/tmp}"
package_name="${ARTIFACT_BASENAME}-${MOD_VERSION}-${MOD_SHORT_REF}.zip"
package_path="$runner_tmp/$package_name"

cd "$PACKAGE_BUILD_DIR"
mapfile -t include_patterns < <(printf '%s\n' "$PACKAGE_INCLUDE_PATTERNS")
cmd=(zip -r "$package_path" .)
for pattern in "${include_patterns[@]}"; do
  if [ -n "$pattern" ]; then
    cmd+=(-i "$pattern")
  fi
done
"${cmd[@]}"

if [ -n "${GITHUB_ENV:-}" ]; then
  echo "PACKAGED_ZIP=$package_path" >> "$GITHUB_ENV"
  echo "PACKAGED_NAME=$package_name" >> "$GITHUB_ENV"
  echo "RELEASE_TAG=${RELEASE_TAG_PREFIX}${MOD_VERSION}-${MOD_SHORT_REF}" >> "$GITHUB_ENV"
else
  echo "PACKAGED_ZIP=$package_path"
  echo "PACKAGED_NAME=$package_name"
  echo "RELEASE_TAG=${RELEASE_TAG_PREFIX}${MOD_VERSION}-${MOD_SHORT_REF}"
fi
