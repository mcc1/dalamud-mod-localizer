# Template Repository Notes

## Purpose

This repository is the shared template/framework layer extracted from the AutoRetainer-TW build repo.

It is meant to be consumed by mod-specific repositories that keep only:

- their wrapper workflow
- their translation dictionary
- their pinned mod version
- their pinned Dalamud inputs
- their package include rules

## Included Shared Assets

- `DalamudModLocalizer.csproj`
- `Program.cs`
- `TranslationRewriter.cs`
- `scripts/clone_mod.sh`
- `scripts/prepare_dalamud.sh`
- `scripts/package_build.sh`
- `.github/workflows/reusable-build-mod.yml`
- `docs/MOD_TRANSLATION_BUILD_PLAYBOOK.md`

## Consumer Configuration Surface

Consumer repos must provide:

- `mod_name`
- `mod_version`
- `mod_repo_url`
- `mod_ref`
- `mod_repo_dir`
- `localizer_source_subpaths`
- `localizer_dict_path`
- `build_project_path`
- `package_build_dir`
- `artifact_basename`
- `release_tag_prefix`
- `package_include_patterns`
- `dalamud_asset_url`
- `dalamud_assets_json_url`
- `dalamud_required_files`

Optional behavior flags:

- `commit_changes`
- `publish_release`

## Initial Compatibility Reference

The first compatibility reference for this template is the AutoRetainer baseline:

- AutoRetainer version: `4.5.1.13`
- AutoRetainer source ref: `4f658f35a89341f78d5de412482dd7183824cb90`
- Dalamud asset URL:
  - `https://github.com/yanmucorp/Dalamud/releases/download/25-12-26-01/latest.7z`
- Dalamud assets JSON URL:
  - `https://raw.githubusercontent.com/yanmucorp/DalamudAssets/master/assetCN.json`

## Guardrails

- Do not replace pinned compatibility inputs with floating `latest` logic for build decisions.
- Do not assume API13 compatibility unless the consumer mod has been verified for it.
- Keep the workflow SDK install set on `8.0.x` and `9.0.x` unless the compatibility baseline is intentionally updated.

## Credit

- Original translation/localizer foundation and prior project history: Miaki
- Reusable workflow extraction, AutoRetainer baseline stabilization, and template split work: mcc
