# palace_smooth (Smooth Viewer V0 presenter + V1 tiles)

Native Diligent presenter for WinUI `SwapChainPanel`:

- D3D12 preferred, D3D11 fallback
- RGBA16F scRGB composition swap chain
- Max frame latency 1 + waitable object
- Clear + HDR scRGB test pattern (V0); V1 adds tile pool + mip CS
- Real frame-time stats (`psv_last_frame_stats`)

## V1 tiles

- `Texture2DArray` RGBA16F Working (BT.2020 linear, 1.0 = 203 nits, premultiplied)
- Slot 512², **Border=2** (508 content) — Catmull-Rom gutter
- `SmoothMipDownsampleCS` (`shaders/SmoothMipDownsample.hlsl`): box filter, AntiRing ON, `SMOOTH_SRGB8=0`
- Scratch 512² then `CopyTexture` into array slot (never same SRV+UAV)
- Coarse levels ≤2048 pinned in LRU (C#)
- `ToneMap.hlsl` included from `Palace.Imaging/Shaders` (no copy); `linear`→`v` patch applied in-repo

## Build (Windows x64)

```powershell
cmake -S Native/smooth -B Native/smooth/build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build Native/smooth/build --config Release --target palace_smooth
Copy-Item Native\smooth\build\Release\palace_smooth.dll Native\smooth\win-x64\ -Force
```

Runtime HLSL: Diligent + `D3DCompile` / FXC `*_5_0`. Static `/MT` CRT. Diligent backends trimmed as in V0.

## Product setting

Settings → Viewer → **Smooth GPU presenter (experimental)** — default **off**.
