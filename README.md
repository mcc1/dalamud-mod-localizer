# Dalamud Mod Localizer Template

Reusable translation and build template for Dalamud mods.

This repository contains the shared layer extracted from the AutoRetainer-TW workflow:

- `DalamudModLocalizer.csproj`
- `Program.cs`
- `TranslationRewriter.cs`
- `scripts/clone_mod.sh`
- `scripts/prepare_dalamud.sh`
- `scripts/package_build.sh`
- `scripts/sync_repo.py`
- `scripts/validate_consumer_patches.py`
- `.github/workflows/reusable-build-mod.yml`
- `.github/workflows/reusable-sync-repo-json.yml`

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

Consumer repositories can also call `.github/workflows/reusable-sync-repo-json.yml` to:

- generate a full `repo.json` from `plugins.sources.json`
- push the refreshed `repo.json` into a GitLab Pages repository

That reusable workflow expects:

- inputs:
  - `gitlab_repo_url`
  - `gitlab_repo_branch`
  - `repo_sources_path`
  - `repo_json_path`
- secret:
  - `gitlab_push_token`

Recommended consumer secret name:

- `GITLAB_PUSH_TOKEN`

Required reminder for new consumer repos:

- if the repo uses `reusable-sync-repo-json.yml`, you must add `GITLAB_PUSH_TOKEN` to the consumer repo's GitHub Actions secrets before expecting `repo.json` sync to GitLab to work
- the token must be valid for the target GitLab repo and have repository read/write access

Typical consumer call pattern:

```yaml
  sync-gitlab-pages:
    uses: mcc1/dalamud-mod-localizer/.github/workflows/reusable-sync-repo-json.yml@main
    with:
      gitlab_repo_url: https://git.example.com/group/plugin-feed.git
      gitlab_repo_branch: master
      repo_sources_path: plugins.sources.json
      repo_json_path: repo.json
    secrets:
      gitlab_push_token: ${{ secrets.GITLAB_PUSH_TOKEN }}
```

If `GITLAB_PUSH_TOKEN` is missing, expired, or lacks scope, the consumer repo will still build and publish GitHub releases, but the final GitLab push step will fail with `HTTP Basic: Access denied`.

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

## Patch Validation

Do not treat the committed source snapshot as the real patch base.

For `sync` and `build`, the actual patch base is:

- workflow source state
- then `Run Localizer`
- then `Apply Consumer Patches`

That means this is the wrong order:

1. edit `mod_repo_dir/` source snapshot directly
2. try to extract a patch afterward

Use this order instead:

1. identify the intended workflow mode
2. reproduce the same pre-patch state
3. make the code change against that state
4. validate patches locally
5. only then run GitHub Actions

Local validation command:

```bash
python scripts/validate_consumer_patches.py \
  --consumer-repo /path/to/Lifestream-zhTW \
  --workflow-mode sync \
  --mod-repo-url https://github.com/NightmareXIV/Lifestream.git \
  --mod-ref e91124d8f7fb0477b46d2c12c9db0fd59e66f3ad \
  --mod-repo-dir Lifestream \
  --localizer-source-subpaths Lifestream \
  --localizer-dict-path zh-TW.json
```

What this script does:

- copies the consumer repo into a temp workspace
- reproduces `sync` / `build` / `extract` source selection
- runs the localizer
- checks consumer patches in workflow order

Use it before every new patch or patch refactor. This is the guardrail that prevents:

- editing the wrong patch base
- stacking multiple patches on the same feature line
- discovering patch breakage only after burning an Actions run

## Included Docs

- [docs/MOD_TRANSLATION_BUILD_PLAYBOOK.md](docs/MOD_TRANSLATION_BUILD_PLAYBOOK.md)
- [docs/TEMPLATE_EXTRACTION_NOTES.md](docs/TEMPLATE_EXTRACTION_NOTES.md)

## Credits

- Original translation/localizer foundation and prior project history: Miaki
- Reusable workflow extraction, AutoRetainer baseline stabilization, and template split work: mcc

Keep this credit in derivative repos unless you have a stronger provenance record to replace it.
