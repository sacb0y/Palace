# FenrirDesktop merge pack — PR #208 V2 r3

| Field | Value |
|---|---|
| Tip | `a6dcdcb4f3a2b28feaf3d889dd9cd28369d73ea6` |
| ProductVersion | `0.3.121+a6dcdcb4f3a2b28feaf3d889dd9cd28369d73ea6` |
| Package | PalaceOrganizeTest-5E1F |
| Host | FenrirDesktop (no DPI) |
| Capture | HWND PrintWindow |

## Numbers

| Run | firstCoarseMs | gpuMips | rbMax | uploaded |
|---|---:|---:|---:|---|
| OFF Release | 3203 | 0 | 0 | 6/6 |
| ON Release + GPU_EVIDENCE | 3439 | 0 | 0 | 6/6 |

## Shots
01 fit · 02 zoom 1.5/3.3/32 · 03 pan · 04 resize-at-fit · 05 after device-lost recovery (2→2) · 10 OFF baseline

Fence ERROR×3 between RemoveDevice and CreateDevice: see device-lost-NOTES.md (America/New_York armed 13:19:07.360 → re-arm 13:19:08.633; log L123→L128/130/132→L151).

No merge. Re-bump 0.3.122 only at merge (after #207).
