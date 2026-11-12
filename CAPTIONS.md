# Doktor gaps — FENRIR-STUDIO tip `ffc9c2b` / 0.3.78 (#193 only)

**Identity:** `PalaceStudioShot-078doktor` (never C20A7629) · not FenrirDesktop · not maximized  
**Tip:** `ffc9c2bb5b415b00a6720fa2c05573ff8651e831` — `Make docked ScrPreviewPane Transparent (0.3.78)` on `cursor/preview-dock-opaque-reflow-c237` (not #194 stack)  
**Capture:** PerMonitorV2 + DWM + CopyFromScreen · SendInput · **no @(0,0) clicks**

## Verdict — **PASS**

| Gap | Result | Artifact |
|---|---|---|
| (a) re-open-after-✕ acrylic | **PASS** | `17-reopen-after-close-acrylic.png` orange=25744 |
| (b) widen→docked distinct | **PASS** | `08-widen-to-docked.png` (+ `10-docked-glass.png`) |
| (c) harness no @(0,0) | **PASS** | zero landed clicks; guard in harness |
| (d) build SHA per shot | **PASS** | every `*.caption.txt` includes `buildSha=ffc9c2bb5b415b00a6720fa2c05573ff8651e831` |
