# Dalamud Mod Localizer Template

Reusable translation and build template for Dalamud mods.

This repository contains the shared layer extracted from the AutoRetainer-TW workflow:

- `DalamudModLocalizer.csproj`
- `Program.cs`
- `TranslationRewriter.cs`
- `scripts/clone_mod.sh`
- `scripts/prepare_dalamud.sh`
- `scripts/package_build.sh`
- `.github/workflows/reusable-build-mod.yml`

## What It Does

The shared workflow now supports three operating modes:

- `sync`
  - clone a pinned upstream mod repository
  - run the Roslyn-based string localizer
  - apply consumer patches
  - prepare pinned Dalamud dependencies
  - build and package the plugin
  - optionally commit the regenerated source snapshot back to the consumer repo
- `build`
  - use the consumer repo's existing source snapshot
  - run the localizer against that snapshot
  - apply consumer patches
  - build and package without re-cloning upstream
- `extract`
  - use the consumer repo's existing source snapshot
  - run the localizer only to refresh `zh-TW.json`
  - skip build, packaging, artifact upload, and source sync

## Workflow Contract

Consumer repositories pass these `workflow_call` inputs:

- `workflow_mode`
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

Optional flags:

- `commit_changes`
- `publish_release`
- `template_repo`
- `template_ref`

## Runtime Assumptions

- localizer target: `net8.0`
- consumer mod may target: `net9.0-windows7.0`
- workflow SDK install set: `8.0.x` and `9.0.x`

## Consumer Usage

Consumer repos should call the shared workflow like this:

```yaml
jobs:
  autoretainer:
    uses: mcc1/dalamud-mod-localizer/.github/workflows/reusable-build-mod.yml@main
    secrets: inherit
    with:
      workflow_mode: sync
      mod_name: AutoRetainer
      mod_version: "4.5.1.13"
      mod_repo_url: https://github.com/PunishXIV/AutoRetainer.git
      mod_ref: 4f658f35a89341f78d5de412482dd7183824cb90
      mod_repo_dir: AutoRetainer
      localizer_source_subpaths: AutoRetainer
      localizer_dict_path: zh-TW.json
      build_project_path: AutoRetainer/AutoRetainer/AutoRetainer.csproj
      package_build_dir: AutoRetainer/AutoRetainer/bin/Release
      artifact_basename: AutoRetainer
      release_tag_prefix: autoretainer-v
      package_include_patterns: |
        *.dll
        *.json
        *.pdb
        res/*.png
      dalamud_asset_url: https://github.com/yanmucorp/Dalamud/releases/download/25-12-26-01/latest.7z
      dalamud_assets_json_url: https://raw.githubusercontent.com/yanmucorp/DalamudAssets/master/assetCN.json
      dalamud_required_files: |
        Dalamud.dll
        Dalamud.Common.dll
        ImGui.NET.dll
        Lumina.dll
        Lumina.Excel.dll
        ImGuiScene.dll
        InteropGenerator.Runtime.dll
        FFXIVClientStructs.dll
```

Recommended mode usage:

- `workflow_mode: extract`
  - high-frequency dictionary collection while a new mod is still missing strings
- `workflow_mode: build`
  - normal day-to-day build/package runs against the consumer repo's current source
- `workflow_mode: sync`
  - upstream/base refresh only when you intentionally upgrade the pinned mod version

## Included Docs

- [docs/MOD_TRANSLATION_BUILD_PLAYBOOK.md](docs/MOD_TRANSLATION_BUILD_PLAYBOOK.md)
- [docs/TEMPLATE_EXTRACTION_NOTES.md](docs/TEMPLATE_EXTRACTION_NOTES.md)

## Credits

- Original translation/localizer foundation and prior project history: Miaki
- Reusable workflow extraction, AutoRetainer baseline stabilization, and template split work: mcc

Keep this credit in derivative repos unless you have a stronger provenance record to replace it.
