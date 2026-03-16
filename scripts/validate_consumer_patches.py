#!/usr/bin/env python3
import argparse
import glob
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def run(cmd, *, cwd=None, env=None, capture_output=False):
    print("+", " ".join(cmd))
    return subprocess.run(
        cmd,
        cwd=cwd,
        env=env,
        check=False,
        text=True,
        capture_output=capture_output,
    )


def require_env(name, value):
    if value:
        return value
    raise SystemExit(f"Missing required setting: {name}")


def clone_pinned_mod(workspace, repo_url, mod_ref, mod_repo_dir):
    target_dir = workspace / mod_repo_dir
    if target_dir.exists():
        shutil.rmtree(target_dir)

    result = run(["git", "clone", repo_url, mod_repo_dir], cwd=workspace)
    if result.returncode != 0:
        raise SystemExit(result.returncode)

    verify = run(
        ["git", "-C", str(target_dir), "rev-parse", "--verify", f"{mod_ref}^{{commit}}"],
        capture_output=True,
    )
    if verify.returncode != 0:
        result = run(["git", "-C", str(target_dir), "fetch", "origin", mod_ref])
        if result.returncode != 0:
            raise SystemExit(result.returncode)

    for cmd in (
        ["git", "-C", str(target_dir), "checkout", "--detach", mod_ref],
        ["git", "-C", str(target_dir), "submodule", "sync", "--recursive"],
        ["git", "-C", str(target_dir), "submodule", "update", "--init", "--recursive"],
    ):
        result = run(cmd)
        if result.returncode != 0:
            raise SystemExit(result.returncode)


def run_localizer(workspace, template_repo, mod_repo_dir, source_subpaths, dict_path):
    env = os.environ.copy()
    env["LOCALIZER_REPO_DIR"] = mod_repo_dir
    env["LOCALIZER_SOURCE_SUBPATHS"] = source_subpaths
    env["LOCALIZER_DICT_PATH"] = dict_path

    result = run(
        [
            "dotnet",
            "run",
            "--project",
            str(template_repo / "DalamudModLocalizer.csproj"),
        ],
        cwd=workspace,
        env=env,
    )
    if result.returncode != 0:
        raise SystemExit(result.returncode)


def apply_patches(workspace, patch_glob):
    patches = sorted(glob.glob(str(workspace / patch_glob)))
    if not patches:
        print(f"No patches matched: {patch_glob}")
        return

    for patch in patches:
        print(f"Validating patch: {Path(patch).name}")
        forward = run(
            ["git", "apply", "--check", "--whitespace=nowarn", patch],
            cwd=workspace,
        )
        if forward.returncode == 0:
            apply_result = run(
                ["git", "apply", "--whitespace=nowarn", patch],
                cwd=workspace,
            )
            if apply_result.returncode != 0:
                raise SystemExit(apply_result.returncode)
            continue

        reverse = run(
            ["git", "apply", "--reverse", "--check", "--whitespace=nowarn", patch],
            cwd=workspace,
        )
        if reverse.returncode == 0:
            print(f"Patch already present, skipping: {Path(patch).name}")
            continue

        raise SystemExit(f"Patch could not be applied cleanly: {Path(patch).name}")


def parse_args():
    parser = argparse.ArgumentParser(
        description="Reproduce reusable-build-mod.yml up to the consumer patch stage."
    )
    parser.add_argument("--consumer-repo", default=os.environ.get("CONSUMER_REPO"))
    parser.add_argument("--template-repo", default=str(Path(__file__).resolve().parents[1]))
    parser.add_argument(
        "--workflow-mode",
        default=os.environ.get("WORKFLOW_MODE", "sync"),
        choices=["sync", "build", "extract"],
    )
    parser.add_argument("--mod-repo-url", default=os.environ.get("MOD_REPO_URL"))
    parser.add_argument("--mod-ref", default=os.environ.get("MOD_REF"))
    parser.add_argument("--mod-repo-dir", default=os.environ.get("MOD_REPO_DIR"))
    parser.add_argument(
        "--localizer-source-subpaths",
        default=os.environ.get("LOCALIZER_SOURCE_SUBPATHS"),
    )
    parser.add_argument(
        "--localizer-dict-path",
        default=os.environ.get("LOCALIZER_DICT_PATH", "zh-TW.json"),
    )
    parser.add_argument(
        "--consumer-patch-glob",
        default=os.environ.get("CONSUMER_PATCH_GLOB", ".consumer-patches/*.patch"),
    )
    parser.add_argument(
        "--keep-temp",
        action="store_true",
        help="Keep the temporary validation workspace for inspection.",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    consumer_repo = Path(require_env("CONSUMER_REPO", args.consumer_repo)).resolve()
    template_repo = Path(require_env("TEMPLATE_REPO", args.template_repo)).resolve()
    mod_repo_dir = require_env("MOD_REPO_DIR", args.mod_repo_dir)
    source_subpaths = require_env("LOCALIZER_SOURCE_SUBPATHS", args.localizer_source_subpaths)

    if not consumer_repo.is_dir():
        raise SystemExit(f"Consumer repo not found: {consumer_repo}")
    if not template_repo.is_dir():
        raise SystemExit(f"Template repo not found: {template_repo}")

    temp_root = Path(tempfile.mkdtemp(prefix="dalamud-localizer-validate-"))
    workspace = temp_root / "consumer"

    print(f"Validation workspace: {workspace}")
    shutil.copytree(consumer_repo, workspace, dirs_exist_ok=False)

    try:
        if args.workflow_mode == "sync":
            repo_url = require_env("MOD_REPO_URL", args.mod_repo_url)
            mod_ref = require_env("MOD_REF", args.mod_ref)
            clone_pinned_mod(workspace, repo_url, mod_ref, mod_repo_dir)
        else:
            if not (workspace / mod_repo_dir).is_dir():
                raise SystemExit(
                    f"Expected existing source snapshot for mode={args.workflow_mode}: "
                    f"{workspace / mod_repo_dir}"
                )

        run_localizer(
            workspace,
            template_repo,
            mod_repo_dir,
            source_subpaths,
            args.localizer_dict_path,
        )
        apply_patches(workspace, args.consumer_patch_glob)
        print("Patch validation succeeded.")
    finally:
        if args.keep_temp:
            print(f"Kept validation workspace: {workspace}")
        else:
            shutil.rmtree(temp_root, ignore_errors=True)


if __name__ == "__main__":
    main()
