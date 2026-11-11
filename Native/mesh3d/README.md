# palace_mesh3d (Diligent D3D12 spike)

x64-only native host for the Palace 3D viewer spike:

1. Diligent Engine on **D3D12** (when built with `-DPALACE_MESH3D_WITH_DILIGENT=ON`)
2. Composition **DXGI** swapchain for WinUI **SwapChainPanel**
3. glTF load path for **PBR + IBL** (Diligent FX `GLTF_PBR_Renderer`)
4. **Streamline probe**: `ID3D12Device` + `ID3D12GraphicsCommandList` via `pm3_probe_streamline`

## Build (Windows x64)

```powershell
# From repo root — stub D3D12 host (swapchain + Streamline probe, no Diligent link yet)
.\tools\mesh3d\build.ps1

# Full Diligent link (clone DiligentCore / DiligentTools / DiligentFX first)
.\tools\mesh3d\build.ps1 -WithDiligent -DiligentRoot D:\src\Diligent
```

Output: `Native/mesh3d/win-x64/palace_mesh3d.dll` (copied into the packaged app when present).

## Diligent layout expected under `-DiligentRoot`

```
<DiligentRoot>/
  DiligentCore/
  DiligentTools/
  DiligentFX/
```

Pin commits in `SOURCE.txt` when a binary is published. Do not commit Diligent sources into Palace.

## Streamline

This spike only **obtains** the native device and command list. It does **not** load NVIDIA Streamline production DLLs (license gate stays with Doktor before any DLSS / Ray Reconstruction build).

## Dev flag

Set `PALACE_MESH3D_SPIKE=1`, then Settings → About → **Open 3D spike**.
