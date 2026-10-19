#!/usr/bin/env python3
"""Write Package.appxmanifest Identity Version from Directory.Build.props PalaceVersion.0."""
from __future__ import annotations

import re
import sys
from pathlib import Path


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    props = root / "Directory.Build.props"
    manifest = root / "Package.appxmanifest"
    props_text = props.read_text(encoding="utf-8")
    match = re.search(r"<PalaceVersion>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</PalaceVersion>", props_text)
    if not match:
        print("Directory.Build.props has no PalaceVersion", file=sys.stderr)
        return 1
    version = f"{match.group(1)}.0"
    text = manifest.read_text(encoding="utf-8")
    new_text, n = re.subn(
        r'(<Identity\b[^>]*\bVersion=")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+(")',
        rf"\g<1>{version}\2",
        text,
        count=1,
    )
    if n != 1:
        print("Could not find Identity Version in Package.appxmanifest", file=sys.stderr)
        return 1
    if new_text != text:
        manifest.write_text(new_text, encoding="utf-8", newline="\n")
        print(f"Synced Package.appxmanifest Identity Version -> {version}")
    else:
        print(f"Already at {version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
