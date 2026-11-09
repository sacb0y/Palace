# N8 — Tag alias export/import + library filter expand (design)

**Status:** Doktor wording OK @ tip `5f9a5d2f` (notes below folded in). Engine may proceed; park code for Doktor review before merge.  
**Against tip:** `main` @ `8cffefe` (**0.3.23**). Alias engine (T4), applied-only Implied (T5), portable tags (`palace-tags/1`) already ship; this note fills the portable + filter gaps.

No UI inventing. No Bugbot. No T2/T8 board work.

---

## Current tip (facts)

| Area | Behavior today |
|---|---|
| Portable schema | `Helpers/TagPortable.cs` — `palace-tags/1`; per tag: `name`, `priority`, `color`, `starred`, `parents[]`, `implies[]`. **No alias field yet.** |
| Export / import | `CatalogService.ExportTagsJsonAsync` / `ImportTagsJsonAsync` — tags + memberships + implications. Upsert by name; **per-edge** cycle refusal + counters for parents/implicits; never wipe unrelated tags. **Not** whole-file atomic. |
| Alias engine | `TagAlias` table; `TagAliasResolve` (Ideal / CanAdd / WouldCycle); `AddAliasAsync` is n→1, self/cycle refuse, idempotent same edge; migrates `AssetTag` alias→ideal. |
| Assign | Alias rewritten to ideal **before** implications; `Source=Implied` refused on assign (T5+). FTS stores **applied ideal names only**. |
| Search / FTS | `TagFtsQuery.Build` expands a matched name → ideal (+ implying children), cap 24 OR terms. |
| Library tag filter | `TagFilter.ExpandEach` = **membership descendants only** — no alias resolve yet. |
| Implied rows | Importers / assign never rematerialize `Source=Implied`. |

**Gap:** aliases do not round-trip in `.palace-tags.json`; Library tag-filter alias chip ≠ ideal chip.

---

## 1. Portable format — aliases

### Shape — keep `palace-tags/1`

Optional field on the **alias** entry (edge = `TagAlias.AliasTagId → IdealTagId`, n→1):

```json
{
  "schema": "palace-tags/1",
  "exportedAt": "…",
  "tags": [
    { "name": "dog", "priority": 0 },
    { "name": "doggy", "aliasOf": "dog" },
    { "name": "pup", "aliasOf": "dog" }
  ]
}
```

- **`aliasOf`:** single string (ideal **name**). Not an array. No dual `aliases[]` on the ideal in v1.
- Omit when null/empty (`WhenWritingNull`).
- Alias tags remain ordinary `Tag` rows; `aliasOf` is only the sibling edge.

### Silent-drop / back-compat

| Consumer | Behavior |
|---|---|
| **Old importer** (pre-N8) reading a file that has `aliasOf` | System.Text.Json ignores unknown properties → upserts the alias as a **plain tag**; **relationship is lost** (silent drop). Document this; do not bump schema for that alone. |
| **New importer** reading an old file without `aliasOf` | Tags / parents / implies only — unchanged. |
| **New importer** reading `aliasOf` | Plan + apply alias edges (after tag upserts so names resolve). |

### Export

`TagPortable.FromCatalog` takes alias edges. For each `TagAlias` row, set `aliasOf` on the alias entry to the ideal’s name. Skip orphan ids (same as membership/implication).

### Import — per-edge refusal + counters (not whole-file atomic)

`ImportTagsJsonAsync` already applies creates/updates then memberships then implications **edge by edge**, counting added/rejected/skipped. Aliases follow the **same** pattern. Do **not** describe or implement import as an all-or-nothing whole-file transaction that rolls back prior successful edges.

| Case | Action |
|---|---|
| Empty / whitespace `aliasOf` | **Skip** that edge (`AliasSkipped`). Tag upsert still happens. |
| Missing ideal name | **EnsureNamed** stub create (same as parents/implies), then bind. |
| Same alias → same ideal already present | Idempotent OK. |
| Alias already bound to a **different** ideal | **Reject** edge; keep existing binding (`AliasRejected`). |
| Self (`aliasOf` equals own name) | Reject. |
| Cycle / would-cycle | Reject (`TagAliasResolve.WouldCycle` / `CanAdd`). |
| Ideal is itself an alias | Resolve through **ultimate ideal** (same-file chains and existing DB map); refuse if that collapses to self. |

Prefer applying via the live `AddAliasAsync` / `TryAddAlias` path so `AssetTag` migrate runs. Counters: `AliasAdded`, `AliasRejected`, `AliasSkipped` → `TagMergePlan.Summary`.

Import does **not** wipe unrelated tags or unrelated alias edges omitted from the file.

### Same-file alias chains

When the file contains `a aliasOf b` and `b aliasOf c`, plan/apply so bindings resolve to the **ultimate** ideal (`c`), consistent with `AddAliasAsync` resolving the ideal argument through the alias map (including edges already accepted earlier in the same import).

### Pre-existing PlanMerge note (unchanged by N8)

`parents` / `implies` endpoint names are **not** alias-resolved in `PlanMerge` today (edges use the named tag ids after upsert). N8 does not change that for membership/implication. Alias edges alone go through ultimate-ideal resolve.

### Implied AssetTag rows

Portable JSON is the **tag graph only**. Never write `AssetTag`. Never invent `Source=Implied`. T5+ invariant stands.

---

## 2. Library filters — expand aliases

### Goal

Filtering by an **alias** or its **ideal** matches the **same assets**. Shared helper: `TagAliasResolve.Ideal`.

### Intentional difference from search

| Path | Alias | Implication / parent expand |
|---|---|---|
| Search / FTS (`TagFtsQuery`) | Resolve → ideal, OR ideal name (+ implying children) | **Yes** — parent expand |
| Library tag filter (`TagFilter`) | Resolve → ideal, then membership of ideal | **No** implication closure — **intentional** |

N8 does **not** add implication closure to library filters. Alias chip ≡ ideal chip for membership-based Any/All/None only.

### Filter set construction

1. `idealId = TagAliasResolve.Ideal(selectedId, aliasMap)`.
2. Start set = `{ idealId }` ∪ membership descendants of **ideal** (`TagFilter.Expand(idealId, memberships)`).
3. **Optional stale-row defense:** union other alias tag ids that map to the same ultimate ideal (and the raw selected id if it differed), so any leftover `AssetTag` on an alias still hits. Prefer including this union.

No new UI — wire where `LibraryViewModel.Filters` / Tags mosaic already call `TagFilter.ExpandEach`.

### Combined browse + search

Filter SQL (alias-expanded) then intersect FTS hits — unchanged shape.

---

## Tests (implementation)

- Export round-trip includes `aliasOf`; old JSON without it still imports.
- Empty `aliasOf` skipped; missing ideal EnsureNamed; rebind/cycle/self rejected; same edge idempotent; same-file chain → ultimate ideal.
- Import does not create Implied `AssetTag` rows; not whole-file atomic (one bad alias edge does not undo earlier tag upserts).
- Filter by alias and by ideal → same asset id set (Any/All/None); no implication closure on filter.
- Search("alias") still works as today (unchanged path).

---

## Out of scope

- Alias create/edit UI (Gooey / T2–T8).
- Schema bump to `palace-tags/2`.
- Changing implication materialization or giving filters FTS-style parent expand.
- Merging before Doktor code review.
