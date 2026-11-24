# palace_smooth (Smooth Viewer V0)

Native Diligent presenter for WinUI `SwapChainPanel`:

- D3D12 preferred, D3D11 fallback
- RGBA16F scRGB composition swap chain
- Max frame latency 1 + waitable object
- Clear + HDR scRGB test pattern (placeholder until Geo HLSL)
- Real frame-time stats (`psv_last_frame_stats`)

## Build (Windows x64)

```powershell
# In-tree build is fine: Palace.csproj DefaultItemExcludes Native\smooth\build\**
# and Native\smooth\out\** so Diligent FetchContent .cs are never compiled into WinUI.
# Do not add SharpGen or build Diligent's .NET projects.
cmake -S Native/smooth -B Native/smooth/build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build Native/smooth/build --config Release
Copy-Item Native\smooth\build\Release\palace_smooth.dll Native\smooth\win-x64\ -Force
```

Runtime HLSL uses Diligent + `D3DCompile` (system `d3dcompiler`); no `dxcompiler` / `dxil` package.
CI caches `Native/smooth/build/_deps` on Diligent tag v2.5.6 + Native/smooth sources.
Do not add redundant helper scripts — invoke cmake directly (Isiac 2026-11-21).

## Product setting

Settings → Viewer → **Smooth GPU presenter (experimental)** — default **off**.
Current viewer unchanged when off. Open the presenter window from that card.
