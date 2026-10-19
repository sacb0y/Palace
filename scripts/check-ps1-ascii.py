#!/usr/bin/env python3
"""Fail if any .ps1 in the repo is not ASCII-only.

Windows PowerShell 5.1 reads a script that has no BOM as the system ANSI code
page. UTF-8 bytes for an em dash or ellipsis then fail to parse, and a caller
can continue with the release Package.appxmanifest.

Policy: every .ps1 is ASCII (bytes 0x00-0x7F). A UTF-8 BOM (EF BB BF) fails
this check because those bytes are not ASCII. Pure ASCII has the same bytes
in ANSI and UTF-8, so PowerShell 5.1 parses it either way.
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIP_DIRS = {".git", "bin", "obj", ".vs", "node_modules"}


def main() -> int:
    bad: list[str] = []
    checked = 0
    for path in sorted(ROOT.rglob("*.ps1")):
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        checked += 1
        data = path.read_bytes()
        for index, byte in enumerate(data):
            if byte > 127:
                line = data.count(b"\n", 0, index) + 1
                rel = path.relative_to(ROOT).as_posix()
                bad.append(f"{rel}:{line}: non-ASCII byte 0x{byte:02X}")
                break
    if bad:
        print(
            "PowerShell scripts must be ASCII-only "
            "(Windows PowerShell 5.1 parses no-BOM files as the ANSI code page):",
            file=sys.stderr,
        )
        for item in bad:
            print(f"  {item}", file=sys.stderr)
        return 1
    print(f"OK ASCII: {checked} .ps1 file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
