#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#ifdef PALACE_SMOOTH_EXPORTS
#  define PSV_API __declspec(dllexport)
#else
#  define PSV_API __declspec(dllimport)
#endif

enum psv_status {
    PSV_OK = 0,
    PSV_FAILED = 1,
    PSV_MISSING_DILIGENT = 2,
    PSV_UNSUPPORTED_ARCH = 3,
    PSV_BAD_ARGUMENT = 4,
    PSV_NOT_ATTACHED = 5,
    PSV_DEVICE_LOST = 6,
    PSV_RESIZE_FAILED = 7
};

enum psv_backend {
    PSV_BACKEND_AUTO = 0,
    PSV_BACKEND_D3D12 = 1,
    PSV_BACKEND_D3D11 = 2
};

typedef struct psv_frame_stats {
    double wait_ms;
    double render_ms;
    double present_ms;
    double total_ms;
    unsigned long long frame_index;
    unsigned width;
    unsigned height;
    int backend; /* psv_backend actually used */
} psv_frame_stats;

/* UTF-8; never null when the DLL loads. */
PSV_API const char* psv_version(void);

/* Prefer D3D12; fall back to D3D11 when backend is AUTO. */
PSV_API void* psv_create(int backend);
PSV_API void  psv_destroy(void* host);

/* Creates IDXGISwapChain1 for WinUI SwapChainPanel (caller owns via out AddRef).
 * Format: R16G16B16A16_FLOAT scRGB. Flags include FRAME_LATENCY_WAITABLE_OBJECT.
 * Max frame latency is set to 1. */
PSV_API int psv_create_composition_swapchain(void* host, unsigned width, unsigned height, void** out_dxgi_swapchain);

PSV_API int psv_resize(void* host, unsigned width, unsigned height);

/* Records composition scale for DPI-change lifecycle (does not recreate buffers). */
PSV_API int psv_set_dpi_scale(void* host, float scale_x, float scale_y);

/* Clears + draws HDR scRGB test pattern, waits on latency object, presents.
 * Fills last frame stats. Returns PSV_DEVICE_LOST when Present reports device removed. */
PSV_API int psv_render_frame(void* host);

PSV_API int psv_last_frame_stats(void* host, psv_frame_stats* out);

/* Writes UTF-8 into buffer; returns required bytes including NUL. */
PSV_API int psv_last_error(void* host, char* buffer, int buffer_bytes);

/* Backend actually created (D3D12 or D3D11). */
PSV_API int psv_backend(void* host);

/* --- V1 tile pool / mip (RGBA16F Working, Border=2, SMOOTH_SRGB8=0) --- */

/* Semicolon-separated search dirs for HLSL (SmoothMipDownsample.hlsl + ToneMap.hlsl). */
PSV_API int psv_set_shader_search_dirs(void* host, const char* utf8_dirs);

/* Creates Texture2DArray pool (capacity slices, 512² RGBA16F) + mip scratch + CS. */
PSV_API int psv_tile_pool_create(void* host, unsigned capacity);

PSV_API void psv_tile_pool_destroy(void* host);

/* Upload one 512² RGBA16F Working tile (row-pitch = 512*8) into array slice. */
PSV_API int psv_tile_upload(void* host, unsigned slice, const void* rgba16f_bytes, int byte_count);

/*
 * Box-filter mip one parent slot via SmoothMipDownsampleCS.
 * slices4x4: 16 uints row-major child ring (-1..2); 0xFFFFFFFF = outside.
 * parent_content_origin_l1: content origin at L+1 (Origin = 2 * that at L).
 * Writes scratch then CopyTexture into dst_slice.
 */
PSV_API int psv_tile_mip_box(
    void* host,
    unsigned level_l_w,
    unsigned level_l_h,
    int parent_content_origin_l1_x,
    int parent_content_origin_l1_y,
    unsigned dst_slice,
    const unsigned* slices4x4);

/* True when mip CS compiled (FXC/D3DCompile cs_5_0 path via Diligent). */
PSV_API int psv_tile_mip_ready(void* host);

#ifdef __cplusplus
}
#endif
