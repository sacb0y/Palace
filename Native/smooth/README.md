# palace_smooth (Smooth Viewer V0)

Native Diligent presenter for WinUI `SwapChainPanel`:

- D3D12 preferred, D3D11 fallback
- RGBA16F scRGB composition swap chain
- Max frame latency 1 + waitable object
- Clear + HDR scRGB test pattern (placeholder until Geo HLSL)
- Real frame-time stats (`psv_last_frame_stats`)

## Build (Windows x64)

```powershell
# Prefer a build dir outside the repo (Diligent FetchContent ships .cs that
# SDK-style Palace.csproj would otherwise compile). In-tree Native/smooth/build
# is gitignored and DefaultItemExcluded.
cmake -S Native/smooth -B $env:TEMP\palace-smooth-build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build $env:TEMP\palace-smooth-build --config Release
Copy-Item $env:TEMP\palace-smooth-build\Release\palace_smooth.dll Native\smooth\win-x64\ -Force
```

CI uses `$RUNNER_TEMP\palace-smooth-build`. Do not add redundant helper scripts —
invoke cmake directly (Isiac 2026-11-21).

## Product setting

Settings → Viewer → **Smooth GPU presenter (experimental)** — default **off**.
Current viewer unchanged when off. Open the presenter window from that card.
