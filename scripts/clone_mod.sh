#!/usr/bin/env bash
set -euo pipefail

: "${MOD_REPO_URL:?MOD_REPO_URL is required}"
: "${MOD_REF:?MOD_REF is required}"
: "${MOD_REPO_DIR:?MOD_REPO_DIR is required}"

rm -rf "$MOD_REPO_DIR"
git clone "$MOD_REPO_URL" "$MOD_REPO_DIR"
if ! git -C "$MOD_REPO_DIR" rev-parse --verify "$MOD_REF^{commit}" >/dev/null 2>&1; then
  git -C "$MOD_REPO_DIR" fetch origin "$MOD_REF"
fi
git -C "$MOD_REPO_DIR" checkout --detach "$MOD_REF"
git -C "$MOD_REPO_DIR" submodule sync --recursive
git -C "$MOD_REPO_DIR" submodule update --init --recursive

if [ -n "${GITHUB_ENV:-}" ]; then
  echo "MOD_SHORT_REF=$(git -C "$MOD_REPO_DIR" rev-parse --short=8 HEAD)" >> "$GITHUB_ENV"
else
  echo "MOD_SHORT_REF=$(git -C "$MOD_REPO_DIR" rev-parse --short=8 HEAD)"
fi
