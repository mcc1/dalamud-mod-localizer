#!/usr/bin/env bash
set -euo pipefail

: "${DALAMUD_ASSET_URL:?DALAMUD_ASSET_URL is required}"
: "${DALAMUD_ASSETS_JSON_URL:?DALAMUD_ASSETS_JSON_URL is required}"
: "${DALAMUD_REQUIRED_FILES:?DALAMUD_REQUIRED_FILES is required}"

runner_tmp="${RUNNER_TEMP:-/tmp}"
mkdir -p "$runner_tmp/dalamud"
asset_url="${DALAMUD_ASSET_URL}"

echo "Using Dalamud asset: $asset_url"

case "$asset_url" in
  *.zip|*.ZIP)
    curl -fL --retry 3 "$asset_url" -o "$runner_tmp/dalamud/dalamud.zip"
    unzip -q -o "$runner_tmp/dalamud/dalamud.zip" -d "$runner_tmp/dalamud"
    ;;
  *.7z|*.7Z)
    curl -fL --retry 3 "$asset_url" -o "$runner_tmp/dalamud/dalamud.7z"
    if command -v 7z >/dev/null 2>&1; then
      7z x -y "$runner_tmp/dalamud/dalamud.7z" "-o$runner_tmp/dalamud"
    elif command -v bsdtar >/dev/null 2>&1; then
      bsdtar -xf "$runner_tmp/dalamud/dalamud.7z" -C "$runner_tmp/dalamud"
    else
      sudo apt-get update
      sudo apt-get install -y p7zip-full
      7z x -y "$runner_tmp/dalamud/dalamud.7z" "-o$runner_tmp/dalamud"
    fi
    ;;
  *.tar.gz|*.tgz|*.TAR.GZ|*.TGZ)
    curl -fL --retry 3 "$asset_url" -o "$runner_tmp/dalamud/dalamud.tar.gz"
    tar -xzf "$runner_tmp/dalamud/dalamud.tar.gz" -C "$runner_tmp/dalamud"
    ;;
  *)
    echo "Unsupported asset format: $asset_url"
    exit 1
    ;;
esac

assets_json_path="$runner_tmp/dalamud/assetCN.json"
curl -fL --retry 3 "$DALAMUD_ASSETS_JSON_URL" -o "$assets_json_path"

jq -r '.Assets[] | [.Url, .FileName] | @tsv' "$assets_json_path" | while IFS=$'\t' read -r url file_name; do
  if [ -z "$url" ] || [ -z "$file_name" ]; then
    continue
  fi
  out_path="$runner_tmp/dalamud/$file_name"
  mkdir -p "$(dirname "$out_path")"
  curl -fL --retry 3 "$url" -o "$out_path"
done

missing_required=0
while IFS= read -r file; do
  if [ -z "$file" ]; then
    continue
  fi
  if [ ! -f "$runner_tmp/dalamud/$file" ]; then
    echo "Missing required dependency: $file"
    missing_required=1
  fi
done < <(printf '%s\n' "$DALAMUD_REQUIRED_FILES")

if [ "$missing_required" -eq 1 ]; then
  echo "Current Dalamud package is incomplete for plugin compilation."
  exit 1
fi

if [ -n "${GITHUB_ENV:-}" ]; then
  echo "DALAMUD_HOME=$runner_tmp/dalamud/" >> "$GITHUB_ENV"
else
  echo "DALAMUD_HOME=$runner_tmp/dalamud/"
fi
