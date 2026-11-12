---
cursor:
  subagentId: "bc-ce12dfe3-b8d3-56a7-a514-0d436569c406"
---

# #194 Tags sidebar — FENRIR-STUDIO retest (0.3.81)

| Field | Value |
|---|---|
| **Verdict** | **PASS** |
| **Build SHA** | `f4788a41b33d6ddfcf24d3a6693f61916e6c57a4` |
| **Version** | 0.3.81 |
| **Host** | FENRIR-STUDIO |
| **DPI** | 144 → **150%** PerMonitorV2 |
| **Window** | Not maximized (~1280×900 DIP) |
| **Identity** | `PalaceOrganizeTest-5E1F` 0.3.81.0 (never C20A7629) |
| **Clicks** | Real `winapp ui click` after ForceForeground: All tags → Uncategorized → Recent → Starred → Aliases |
| **FenrirDesktop** | Not touched |

## Checklist

| Check | Result |
|---|---|
| Ungrouped leaf chips neutral (no random FaceColor dots) | **PASS** — `black hair`, `Clothing`, `Prompt`, `Shot`, `Style` have no dots |
| FaceColor only on groups-with-children | **PASS** — chip dots on `Character` + `Subject` only |
| Sidebar Subject/group dots still coloured | **PASS** — periwinkle/lavender Subject dot visible |
| Aliases sort label not clipped | **PASS** — `Date modified · Newest` fully visible |

## What was clicked

1. `NavTags`
2. Fixed views: `SelTagScopeAll` / `BtnTagGroup_Ungrouped` / `SelTagScopeRecent` / `SelTagScopeStarred` / `SelTagScopeAliases`
3. Captures on All tags (chips) and Aliases (sort)
