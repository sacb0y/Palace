#!/usr/bin/env python3
"""
CI version gate for Palace PRs.

Fails unless:
  1. Directory.Build.props PalaceVersion matches Package.appxmanifest Identity (MAJOR.MINOR.PATCH.0)
  2. PR version is greater than main's, unless exempt:
       - label `no-version-bump`, or
       - all changed files match the docs/CI/tests-only path filter

Usage (PR):
  python3 scripts/check-version.py --base-ref origin/main

Usage (local / push to main):
  python3 scripts/check-version.py --match-only
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

EXEMPT_PATH_PREFIXES = (
    ".cursor/",
    ".github/",
    "docs/",
    "Palace.Tests/",
    "scripts/",
)

EXEMPT_PATH_EXACT = {
    "AGENTS.md",
    "README.md",
    "LICENSE",
    "LICENSE.txt",
    "ui-tests.ps1",
    ".gitattributes",
    ".gitignore",
    ".editorconfig",
}

EXEMPT_SUFFIXES = (".md",)


def read_palace_version(text: str) -> str | None:
    m = re.search(r"<PalaceVersion>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</PalaceVersion>", text)
    if m:
        return m.group(1)
    # Legacy: Palace.csproj <Version> before Directory.Build.props existed
    m = re.search(r"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>", text)
    return m.group(1) if m else None


def read_manifest_version(text: str) -> str | None:
    m = re.search(r'<Identity\b[^>]*\bVersion="([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"', text)
    if m:
        return m.group(1)
    m = re.search(r'\bVersion="([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"', text)
    return m.group(1) if m else None


def parse_semver(v: str) -> tuple[int, int, int]:
    parts = v.split(".")
    if len(parts) not in (3, 4):
        raise ValueError(f"bad version: {v}")
    return int(parts[0]), int(parts[1]), int(parts[2])


def git_show(ref: str, path: str) -> str | None:
    r = subprocess.run(
        ["git", "show", f"{ref}:{path}"],
        cwd=ROOT,
        capture_output=True,
        text=True,
    )
    if r.returncode != 0:
        return None
    return r.stdout


def git_diff_names(base_ref: str) -> list[str]:
    r = subprocess.run(
        ["git", "diff", "--name-only", f"{base_ref}...HEAD"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    return [line.strip() for line in r.stdout.splitlines() if line.strip()]


def path_is_exempt(path: str) -> bool:
    norm = path.replace("\\", "/")
    if norm in EXEMPT_PATH_EXACT:
        return True
    for prefix in EXEMPT_PATH_PREFIXES:
        if norm.startswith(prefix):
            return True
    return any(norm.endswith(suf) for suf in EXEMPT_SUFFIXES)


def has_no_version_bump_label() -> bool:
    raw = os.environ.get("PR_LABELS", "")
    labels = {x.strip().lower() for x in raw.split(",") if x.strip()}
    return "no-version-bump" in labels


def resolve_base_version(base_ref: str) -> str:
    props = git_show(base_ref, "Directory.Build.props")
    if props:
        v = read_palace_version(props)
        if v:
            return v
    csproj = git_show(base_ref, "Palace.csproj")
    if csproj:
        v = read_palace_version(csproj)
        if v:
            return v
    manifest = git_show(base_ref, "Package.appxmanifest")
    if manifest:
        mv = read_manifest_version(manifest)
        if mv:
            maj, minor, patch, *_ = mv.split(".")
            return f"{maj}.{minor}.{patch}"
    raise SystemExit(f"Could not read version from {base_ref}")


def check_match() -> str:
    props_text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    palace = read_palace_version(props_text)
    if not palace:
        raise SystemExit("Directory.Build.props missing <PalaceVersion>")
    manifest_text = (ROOT / "Package.appxmanifest").read_text(encoding="utf-8")
    manifest = read_manifest_version(manifest_text)
    if not manifest:
        raise SystemExit("Package.appxmanifest missing Identity Version")
    expected = f"{palace}.0"
    if manifest != expected:
        raise SystemExit(
            f"Version mismatch: Directory.Build.props PalaceVersion={palace} "
            f"(expect Identity {expected}) but Package.appxmanifest has {manifest}. "
            f"Run: python3 scripts/sync-appx-version.py"
        )
    # Also ensure Revision is 0
    if not manifest.endswith(".0"):
        raise SystemExit(f"Identity Version must end with .0 (Store), got {manifest}")
    print(f"OK match: PalaceVersion={palace}, Identity={manifest}")
    return palace


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base-ref", default="", help="git ref for main (e.g. origin/main)")
    ap.add_argument("--match-only", action="store_true", help="only check props/manifest sync")
    args = ap.parse_args()

    palace = check_match()

    if args.match_only or not args.base_ref:
        return 0

    if has_no_version_bump_label():
        print("Exempt: PR has label no-version-bump")
        return 0

    changed = git_diff_names(args.base_ref)
    if changed and all(path_is_exempt(p) for p in changed):
        print(f"Exempt: all {len(changed)} changed path(s) are docs/CI/tests-only")
        return 0

    base = resolve_base_version(args.base_ref)
    if parse_semver(palace) <= parse_semver(base):
        raise SystemExit(
            f"Version {palace} must be greater than {args.base_ref} version {base}. "
            f"Bump <PalaceVersion> in Directory.Build.props, sync Package.appxmanifest "
            f"(python3 scripts/sync-appx-version.py), and state the new version + one-line "
            f"user-facing summary in the PR description. "
            f"Docs/CI/tests-only PRs: add label no-version-bump or keep changes on exempt paths."
        )
    print(f"OK bump: {base} -> {palace}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as e:
        print(e.stderr or e, file=sys.stderr)
        raise SystemExit(1)
