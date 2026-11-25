# 3D groundwork — parked items (PR4)

Parked from Doktor pass-2 (`reviews/doktor-pass2-3d-ready-2026-11-21.md`). **Do not implement in this PR.** Re-open when Smooth Viewer V0–V4 are green and Isiac opens the 3D slice.

## D1 — Out-of-process mesh thumbs

Native ufbx / Diligent must not parse untrusted `.fbx` / `.glb` in-process during scan or thumbnailing. Ship `palace-mesh-thumb.exe` driven like `FfmpegPoster` (timeout, kill, JSON sidecar). Remember failures with `Asset.Undecodable`. In-proc DLL is interactive viewer only, with a last-opened crash breadcrumb.

## D5 — Multi-file asset moves

`.gltf` + `.bin` + textures, `.fbx` + `.fbm/` must move / organize / reject as one journal batch. Seam: `Palace.Mesh3d.IAssetDependencies` (stub `EmptyAssetDependencies` ships with D11). Until resolve is real, Move / Organize / Delete should refuse `Model3D` assets that have dependencies, with a clear message.

## D12 — Native packaging for `palace_mesh3d.dll`

Diligent D3D12 needs MSVC (not mingw), likely `dxcompiler.dll` / `dxil.dll`. Do not commit an unpinned binary. Build on `windows-latest`, publish a release asset, pin by SHA-256 via `Get-Mesh3d.ps1` (mirror `Get-Ffmpeg.ps1`). Startup `pm3_version` probe hides 3D UI on `DllNotFound`. Keep x64-only explicit. Draft #192 stays reference-only — do not merge or advance it from this work.

## Implemented in PR4 (for orientation)

| Id | Seam |
|---|---|
| D2 | `AssetKind.Model3D` + `AssetKindRegistry` |
| D3 | Kind-aware scan fast path; catalog-ext present ≠ orphan |
| D6 | `GallerySurface` + `ViewerSurfaceHost` |
| D7 | Scan cancel + `ScanProgress` + FfmpegPoster ct |
| D10 | `AppServices.ShutdownAsync` + BackStack clear |
| D11 | `Palace.Mesh3d` + `IMeshRenderer` / `RecordingMeshRenderer` |
