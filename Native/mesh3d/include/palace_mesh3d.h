#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#ifdef PALACE_MESH3D_EXPORTS
#  define PM3_API __declspec(dllexport)
#else
#  define PM3_API __declspec(dllimport)
#endif

enum pm3_status {
    PM3_OK = 0,
    PM3_FAILED = 1,
    PM3_MISSING_DILIGENT = 2,
    PM3_UNSUPPORTED_ARCH = 3,
    PM3_BAD_ARGUMENT = 4,
    PM3_NOT_ATTACHED = 5,
    PM3_LOAD_FAILED = 6
};

/* UTF-8 version string; never null when the DLL loads. */
PM3_API const char* pm3_version(void);

PM3_API void* pm3_create(void);
PM3_API void  pm3_destroy(void* host);

/* Creates IDXGISwapChain1 for WinUI SwapChainPanel (caller AddRefs via out). */
PM3_API int pm3_create_composition_swapchain(void* host, unsigned width, unsigned height, void** out_dxgi_swapchain);

PM3_API int pm3_resize(void* host, unsigned width, unsigned height);

/* UTF-16 path for Windows. Loads glTF/GLB with Diligent PBR + IBL when linked. */
PM3_API int pm3_load_gltf(void* host, const wchar_t* path_utf16);

PM3_API int pm3_render_frame(void* host);

/* ID3D12Device* + ID3D12GraphicsCommandList* for a future Streamline hook. */
PM3_API int pm3_probe_streamline(void* host, void** out_device, void** out_command_list);

/* Writes UTF-8 into buffer; returns required bytes including NUL. */
PM3_API int pm3_last_error(void* host, char* buffer, int buffer_bytes);

#ifdef __cplusplus
}
#endif
