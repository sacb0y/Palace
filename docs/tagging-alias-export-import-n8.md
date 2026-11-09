# N8 — Tag alias export/import + library filter expand (design)

**Status:** design only — park for **Doktor** wording approval before any engine code.  
**Against tip:** `main` @ `8cffefe` (**0.3.23**, after #154). Alias engine (T4), applied-only Implied (T5), portable tags (`palace-tags/1`) already ship; this note fills the portable + filter gaps.

No UI inventing. No Bugbot. Do not merge engine changes from this PR.

---

## Current tip (facts, not proposals)

| Area | Behavior today |
|---|---|
| Portable schema | `Helpers/TagPortable.cs` — `palace-tags/1`; per tag: `name`, `priority`, `color`, `starred`, `parents[]`, `implies[]`. **No alias field.** |
| Export / import | `CatalogService.ExportTagsJsonAsync` / `ImportTagsJsonAsync` — tags + memberships + implications only. Upsert by name; cycle-safe parents/implicits; never wipe unrelated tags. |
| Alias engine | `TagAlias` table; `TagAliasResolve` (Ideal / CanAdd / WouldCycle); `AddAliasAsync` is n→1, self/cycle refuse, idempotent same edge; migrates `AssetTag` alias→ideal. |
| Assign | Alias rewritten to ideal **before** implications; `Source=Implied` refused on assign (T5+). FTS stores **applied ideal names only**. |
| Search / FTS | `TagFtsQuery.Build` expands a matched name → ideal (+ implying children), cap 24 OR terms. Querying an alias finds ideal-tagged assets. |
| Library tag filter | `LibraryViewModel.Filters` → `TagFilter.ExpandEach(ids, memberships)` then `GetAssetsForTagFilterAsync`. Expand is **membership descendants only** — **no alias resolve**. |
| Implied rows | Importers / assign never rematerialize `Source=Implied`. Organize / export-tags-on-asset use applied-only (`GetExportTagsAsync`). |

**Gap:** aliases round-trip nowhere in `.palace-tags.json`, and picking an alias chip in Library tag browse does not match the same assets as picking its ideal (or as searching the alias).

---

## 1. Portable format — aliases

### Shape

Keep schema family **`palace-tags/1`** (parser already accepts any `palace-tags/*`). Add one optional field on the **alias** entry (edge direction matches `TagAlias.AliasTagId → IdealTagId`, and n→1):

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

- **`aliasOf`:** single string (ideal **name**), not an array — encodes n→1.
- Omit when null/empty (`WhenWritingNull`); old exports without the field stay valid.
- Do **not** dual-list aliases on the ideal (`aliases: […]`) in v1 — one representation avoids import contradictions.
- Alias tags remain ordinary `Tag` rows (name/priority/color/starred/parents/implies may still appear). `aliasOf` is only the sibling edge.

### Export

`TagPortable.FromCatalog` gains alias edges (or a parallel list). For each `TagAlias` row, set `aliasOf` on the alias entry to the ideal’s name. Unknown / orphan edge ids are skipped (same as today’s membership/implication filters).

### Import — back-compat

| File | Behavior |
|---|---|
| Existing `palace-tags/1` without `aliasOf` | Unchanged: tags / parents / implies only. |
| New files with `aliasOf` | Plan + apply alias edges after tag upserts (and after parent/imply planning so names resolve). |

Missing ideal name → **EnsureNamed** stub create (same as parents/implies today), then bind.

### Import — conflict / refuse (mirror `TagAliasResolve` + `TryAddAlias`)

Apply through the same guards as live `AddAliasAsync` (prefer calling that path so `AssetTag` migrate runs):

| Case | Action |
|---|---|
| Same alias → same ideal already present | Idempotent OK (no-op / count as present). |
| Alias already bound to a **different** ideal | **Reject** that edge; keep existing binding. Count `AliasRejected`. |
| Ideal name empty / unresolvable after EnsureNamed | Skip; count `AliasSkipped`. |
| Self (`aliasOf` equals own name) | Reject. |
| Cycle / would-cycle (incl. transitive) | Reject (`WouldCycle`). |
| Ideal is itself an alias | Resolve to ultimate ideal first (catalog already does this on add); refuse if that collapses to self. |

Planner counters (names illustrative): `AliasAdded`, `AliasRejected`, `AliasSkipped` — fold into `TagMergePlan.Summary`.

Import still **does not wipe** unrelated tags or unrelated alias edges not mentioned in the file.

### Implied AssetTag rows

Portable JSON is the **tag graph only** (tags, memberships, implications, aliases). It never writes `AssetTag`. Importers must **not** invent `Source=Implied` rows. Live assign already drops Implied; T5+ purge keeps that invariant. N8 does not reopen materializing implications.

---

## 2. Library filters — expand aliases

### Goal

Filtering by an **alias** or its **ideal** must match the **same assets**, consistent with search: **aliases resolve before implications**.

### Paths (no new UI)

| Path | Today | N8 intent |
|---|---|---|
| Library tag browse / Any·All·None | `TagFilter.Expand` = membership walk on selected ids → `AssetTag IN (…)`. Selected alias id rarely hits applied rows (assignments store ideal). | Before membership expand, resolve each selected id with `TagAliasResolve.Ideal`. Build each set from the **ideal** (+ membership descendants of that ideal). Optionally include the raw selected id and other aliases of that ideal so any stale alias-row still hits. |
| Search box (FTS) | Already expands alias→ideal (+ implying children) in `TagFtsQuery`. | Unchanged; filter work should **not** diverge from this resolve order. |
| Combined (tag browse **and** search) | Filter SQL then intersect FTS hits. | After filter alias expand, intersection stays as today. |

Implication note (existing T5/N4 policy, not reinvented here): FTS parent expand ORs implying children so searching a parent finds applied-only children; library tag-filter expand remains **membership**, not implication closure. N8 only adds **alias** parity on the filter path so alias chip ≡ ideal chip.

### Order (explicit)

1. Resolve selected filter tag → ideal (`TagAliasResolve.Ideal`).  
2. Membership-expand from that ideal (`TagFilter.Expand`).  
3. (Search path only, already shipped) implication / parent OR expansion in FTS — after alias resolve.

### Tests to require when implementing (not in this PR)

- Export round-trip includes `aliasOf`; old JSON without it still imports.  
- Import rejects rebind / cycle / self; accepts idempotent same edge; EnsureNamed for missing ideal.  
- Import does not create Implied `AssetTag` rows.  
- Filter by alias and by ideal return the same asset id set (Any / All / None).  
- Search("alias") and filter(alias) agree on a single-ideal fixture.

---

## Out of scope (this note)

- Alias create/edit UI (Gooey / T2–T8 board work).  
- Schema bump to `palace-tags/2` (optional later if Doktor wants a hard break).  
- Changing implication materialization or FTS applied-only storage.  
- Engine implementation — **park until Doktor wording OK**.

---

## Open for Doktor

1. **`aliasOf` on the alias entry** (singular) vs listing `aliases[]` on the ideal — proposal is `aliasOf`.  
2. Keep **`palace-tags/1`** with optional field vs bump schema id.  
3. On conflicting rebind: **silent reject + count** (like implication cycles) vs surface in import summary UI copy (Settings already shows `TagMergePlan.Summary`).  
4. Filter set: resolve-to-ideal + membership only, or also union every alias id of that ideal for stale-row defense?
