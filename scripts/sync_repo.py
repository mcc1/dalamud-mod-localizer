import io
import json
import os
import re
import sys
import urllib.error
import urllib.request
import zipfile
from pathlib import Path


ROOT = Path.cwd()
repo_json_env = os.environ.get("REPO_JSON_PATH")
sources_json_env = os.environ.get("REPO_SOURCES_PATH")
REPO_JSON_PATH = ROOT / (repo_json_env or "repo.json")
SOURCES_JSON_PATH = ROOT / (sources_json_env or "plugins.sources.json")
GITHUB_API = "https://api.github.com"
ALLOWED_KEYS = {
    "Author",
    "Name",
    "InternalName",
    "AssemblyVersion",
    "Description",
    "ApplicableVersion",
    "RepoUrl",
    "Tags",
    "DalamudApiLevel",
    "LoadRequiredState",
    "LoadSync",
    "CanUnloadAsync",
    "LoadPriority",
    "IconUrl",
    "Punchline",
    "AcceptsFeedback",
    "IsTestingExclusive",
    "DownloadLinkInstall",
    "DownloadLinkUpdate",
    "ImageUrls",
}


def http_get_json(url: str, token: str | None = None):
    req = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            **({"Authorization": f"Bearer {token}"} if token else {}),
            "User-Agent": "repo-json-sync",
        },
    )
    with urllib.request.urlopen(req) as resp:
        return json.load(resp)


def http_get_bytes(url: str, token: str | None = None) -> bytes:
    req = urllib.request.Request(
        url,
        headers={
            "Accept": "application/octet-stream",
            **({"Authorization": f"Bearer {token}"} if token else {}),
            "User-Agent": "repo-json-sync",
        },
    )
    with urllib.request.urlopen(req) as resp:
        return resp.read()


def pick_release(releases: list[dict], include_prereleases: bool) -> dict:
    for release in releases:
        if release.get("draft"):
            continue
        if release.get("prerelease") and not include_prereleases:
            continue
        return release
    raise RuntimeError("No matching release found.")


def get_release_by_tag(source_repo: str, tag: str, token: str | None) -> dict:
    return http_get_json(
        f"{GITHUB_API}/repos/{source_repo}/releases/tags/{tag}",
        token=token,
    )


def pick_asset(assets: list[dict], asset_name_regex: str | None) -> dict:
    if asset_name_regex:
        pattern = re.compile(asset_name_regex)
        for asset in assets:
            if pattern.match(asset["name"]):
                return asset
    for asset in assets:
        if asset["name"].lower().endswith(".zip"):
            return asset
    raise RuntimeError("No matching zip asset found.")


def read_manifest_from_zip(zip_bytes: bytes, manifest_path: str) -> dict:
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as zf:
        with zf.open(manifest_path) as fp:
            return json.load(fp)


def sync_entry(source: dict, token: str | None) -> dict:
    fixed_release_tag = source.get("fixed_release_tag")
    if fixed_release_tag:
        release = get_release_by_tag(source["source_repo"], fixed_release_tag, token)
    else:
        releases = http_get_json(
            f"{GITHUB_API}/repos/{source['source_repo']}/releases",
            token=token,
        )
        release = pick_release(releases, source.get("include_prereleases", False))

    asset = pick_asset(release.get("assets", []), source.get("asset_name_regex"))
    zip_bytes = http_get_bytes(asset["browser_download_url"], token=token)
    manifest = read_manifest_from_zip(zip_bytes, source["manifest_path"])

    synced = {}
    synced.update({k: v for k, v in manifest.items() if k in ALLOWED_KEYS})
    synced["DownloadLinkInstall"] = asset["browser_download_url"]
    synced["DownloadLinkUpdate"] = asset["browser_download_url"]
    synced["RepoUrl"] = f"https://github.com/{source['source_repo']}"

    overrides = source.get("manifest_overrides", {})
    if overrides:
        synced.update({k: v for k, v in overrides.items() if k in ALLOWED_KEYS})

    return synced


def main() -> int:
    token = os.environ.get("GITHUB_TOKEN")
    sources = json.loads(SOURCES_JSON_PATH.read_text(encoding="utf-8"))
    ordered = []
    for source in sources:
        updated = sync_entry(source, token)
        internal_name = updated.get("InternalName", source["internal_name"])
        print(
            f"Synchronized {internal_name}: "
            f"{updated.get('AssemblyVersion')} "
            f"(API {updated.get('DalamudApiLevel')})"
        )
        ordered.append(updated)

    REPO_JSON_PATH.write_text(
        json.dumps(ordered, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except urllib.error.HTTPError as exc:
        print(f"HTTP error: {exc.code} {exc.reason}", file=sys.stderr)
        raise
    except Exception as exc:
        print(f"Sync failed: {exc}", file=sys.stderr)
        raise
