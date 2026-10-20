# HDR PNG packaged verify (FenrirDesktop)

Tip: `9fcceeed73dee5bb9a3b44a1df9155d3f2810bf7` · PR #120 · 0.2.7 · test identity `PalaceHdrPng-120`

## Captions

### `01-details-color-hdr.png`
Overlay Details · **Color & HDR**: Color `BT.2020 · PQ`; HDR `HDR PNG · tonemap SDR 80 nits` (MaxCLL / FALL / Max / Avg visible). Temp copy `LiraProfileHDR.png` only.

### `02-overlay-details-hdr.png`
Same window after focusing Details card (Color & HDR still expanded).

## Display probe / advanced-color (text)

```
Palace.DisplayHdr DXGI no Output6 ? assumeHdr=0 displayHdr=0 ? CCD not queried
```

- **swap=** not emitted (unknown-display path only adds `swap=R16G16B16A16_FLOAT/scRGB(G10/P709)` when `assumeHdr=1`)
- **Windows dxdiag:** AdvancedColorSupported **AdvancedColorEnabled**; Active Color Mode **HDR**
- Present used **SDR tonemap @ 80 nits** despite OS HDR ON, because DXGI Output6 probe failed

## Constraints honored

- BelowNormal + `-m:1` build
- Test identity `PalaceHdrPng-120` (never C20A7629)
- Image: `%TEMP%\palace-hdr-png-aec2\LiraProfileHDR.png` only
