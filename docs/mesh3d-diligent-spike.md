# Diligent D3D12 3D viewer spike (0.3.59)

Dev-only throwaway path to prove Diligent on **D3D12** inside a WinUI **SwapChainPanel**, loading a **glTF** with **PBR + IBL**, and obtaining native **ID3D12Device** + **ID3D12GraphicsCommandList** for a later NVIDIA Streamline hook. **x64 only.** Not product UI.

## Enable

```text
PALACE_MESH3D_SPIKE=1
```

Settings → About → **Open 3D spike** (hidden unless the flag is set).

## Native host

```powershell
.\tools\mesh3d\build.ps1
# optional full Diligent link:
.\tools\mesh3d\build.ps1 -WithDiligent -DiligentRoot D:\src\Diligent
```

See `Native/mesh3d/README.md`. Sample mesh: `Assets/Mesh3dSpike/SpikeBox.gltf`.

## Streamline

`pm3_probe_streamline` / `Mesh3dStreamlineProbe` only confirm device + command list pointers. No Streamline / DLSS DLLs in this spike (license gate stays with Doktor).
