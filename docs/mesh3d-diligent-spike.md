# Diligent D3D12 3D viewer spike (draft)

No version bump while this stays a draft. Apply a PATCH bump when the spike is eventually merged.

Dev-only throwaway path to prove Diligent on **D3D12** inside a WinUI **SwapChainPanel**, loading a **glTF** with **PBR + IBL**, and obtaining native **ID3D12Device** + **ID3D12GraphicsCommandList** for a later NVIDIA Streamline hook. **x64 only.** Not product UI.

## Enable

```text
PALACE_MESH3D_SPIKE=1
```

Settings â†’ About â†’ **Open 3D spike** (hidden unless the flag is set).

## Native host

```powershell
.\tools\mesh3d\build.ps1
# optional full Diligent link:
.\tools\mesh3d\build.ps1 -WithDiligent -DiligentRoot D:\src\Diligent
```

See `Native/mesh3d/README.md`. Sample mesh: `Assets/Mesh3dSpike/SpikeBox.gltf`.

## Streamline

`pm3_probe_streamline` / `Mesh3dStreamlineProbe` only confirm device + command list pointers. No Streamline / DLSS DLLs in this spike (license gate stays with Doktor).

## FENRIR-STUDIO verification (2026-11-11)

- Built `palace_mesh3d.dll` via `.\tools\mesh3d\build.ps1 -WithDiligent` (BelowNormal / shared machine).
- Packaged Debug under test identity `PalaceMesh3dSpike-192` — never `C20A7629`.
- Sample `Assets/Mesh3dSpike/SpikeBox.gltf` only.
- **Renders:** SwapChainPanel presents D3D12 clear color (dark charcoal). Full glTF PBR mesh draw still needs DiligentCore/Tools/FX linked in CMake (define path arms load today).
- **Streamline probe:** OK — `ID3D12Device` + `ID3D12GraphicsCommandList` obtained.
