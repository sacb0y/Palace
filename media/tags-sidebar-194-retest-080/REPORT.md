---
cursor:
  subagentId: "bc-ce12dfe3-b8d3-56a7-a514-0d436569c406"
---

# #194 Tags sidebar — FENRIR-STUDIO retest (0.3.80)

| Field | Value |
|---|---|
| **Verdict** | **PASS** |
| **Build SHA** | `e41fe805330bab89f09952602b7b0fd1df75448b` |
| **Version** | 0.3.80 |
| **Host** | FENRIR-STUDIO |
| **DPI** | 144 → **150%** PerMonitorV2 |
| **Window** | Not maximized (~1280×900 DIP) |
| **Identity** | `PalaceOrganizeTest-5E1F` 0.3.80.0 (never C20A7629) |
| **Clicks** | Real `winapp ui click` (ForceForeground): All tags → Uncategorized → Recent → Starred → Aliases |
| **FenrirDesktop** | Not touched |

## Checklist

| Check | Result |
|---|---|
| Subject/group dots clearly visible (not black-on-black) | **PASS** — lavender/periwinkle face colour on dark chrome |
| Full sidebar (views + GROUPS + Add) | PASS |
| Aliases view: Tag filter single chevron | PASS |
| Aliases sort label not clipped at ~1280 DIP | PASS (`Sort: Date modified · Newest`) |
| Fluent icons / flat accent / counts R-aligned | PASS |

## What was clicked

1. `NavTags`
2. `SelTagScopeAll` / `BtnTagGroup_Ungrouped` / `SelTagScopeRecent` / `SelTagScopeStarred` / `SelTagScopeAliases`
3. Captures on All tags (sidebar) and Aliases (main pane + dots crop)
