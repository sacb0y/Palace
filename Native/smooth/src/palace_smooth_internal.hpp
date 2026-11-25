#pragma once

#include "palace_smooth.h"
#include "smooth_image_cb.h"

#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_5.h>
#include <wrl/client.h>

#include <string>

using Microsoft::WRL::ComPtr;

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
#  include "EngineFactoryD3D12.h"
#  include "RenderDeviceD3D12.h"
#  include "CommandQueueD3D12.h"
#  include "EngineFactoryD3D11.h"
#  include "RenderDeviceD3D11.h"
#  include "EngineFactory.h"
#  include "RenderDevice.h"
#  include "DeviceContext.h"
#  include "PipelineState.h"
#  include "Shader.h"
#  include "Texture.h"
#  include "Buffer.h"
#  include "ShaderResourceBinding.h"
#  include "RefCntAutoPtr.hpp"
using namespace Diligent;
#endif

struct Host {
    int backend = PSV_BACKEND_D3D12;
    unsigned width = 0;
    unsigned height = 0;
    float dpiScaleX = 1.f;
    float dpiScaleY = 1.f;
    std::string lastError;
    psv_frame_stats lastStats{};
    bool deviceLost = false;

    ComPtr<IDXGIFactory2> factory;
    ComPtr<IDXGISwapChain3> swapChain;
    ComPtr<IDXGIAdapter3> adapter3;
    HANDLE frameLatencyWaitable = nullptr;

    ComPtr<ID3D12Device> d3d12Device;
    ComPtr<ID3D12CommandQueue> d3d12Queue;
#if defined(PSV_TEST_HOOKS)
    DWORD infoQueueCookie = 0;
#endif

    ComPtr<ID3D11Device> d3d11Device;
    ComPtr<ID3D11DeviceContext> d3d11Context;

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    RefCntAutoPtr<IRenderDevice> diligentDevice;
    RefCntAutoPtr<IDeviceContext> diligentContext;
    RefCntAutoPtr<IPipelineState> patternPso;
    RefCntAutoPtr<IShaderResourceBinding> patternSrb;
    bool diligentReady = false;

    /* V1 tile pool (RGBA16F Working, SMOOTH_SRGB8=0). */
    RefCntAutoPtr<ITexture> tileArray;
    RefCntAutoPtr<ITexture> mipScratch; /* TEX_2D_ARRAY ArraySize=1; never SRV+UAV same as pool */
    RefCntAutoPtr<ITexture> readbackStaging; /* STAGING TEX_2D_ARRAY ArraySize=1, CPU_ACCESS_READ */
    RefCntAutoPtr<IPipelineState> mipPso;
    RefCntAutoPtr<IShaderResourceBinding> mipSrb;
    RefCntAutoPtr<IBuffer> mipCb;
    RefCntAutoPtr<IShaderSourceInputStreamFactory> shaderFactory;
    unsigned tileCapacity = 0;
    bool tilePoolReady = false;
    bool mipReady = false;
    /* Semicolon-separated dirs for SmoothMipDownsample / SmoothImage + ToneMap. */
    char shaderSearchDirs[512]{};

    /* V2 SmoothImage viewport pass. */
    RefCntAutoPtr<IPipelineState> imagePso;
    RefCntAutoPtr<IShaderResourceBinding> imageSrb;
    RefCntAutoPtr<IBuffer> viewCb;
    RefCntAutoPtr<IBuffer> toneCb;
    RefCntAutoPtr<IBuffer> instanceBuf;
    RefCntAutoPtr<ITexture> toneLut;
    psv_view_cb viewCpu{};
    psv_tone_cb toneCpu{};
    unsigned instanceCount = 0;
    unsigned toneLutSize = 0;
    bool imageReady = false;
    bool viewportEnabled = false;
#endif

    LARGE_INTEGER qpcFreq{};
};

void SetError(Host* host, const char* msg);
void ClearError(Host* host);
void UnbindAndIdleDiligent(Host* host);
/* AttachToD3D12Device + raw DXGI Present — call after Flush so Diligent recycles
 * dynamic CBV_SRV_UAV. No-op if removed / no context. */
void PsvFinishDiligentFrame(Host* host);
bool BindDxgiAdapter3(Host* host);

/* Cheap DXGI removed poll. Sets host->deviceLost when reason != S_OK.
 * callSite: poll | present | create | tileUpload | tileMipBox | … */
bool PollDeviceRemoved(Host* host, const char* callSite = "poll");

/* First flip of deviceLost: log psv scope + callSite + HRESULT, then DRED dump. */
void PsvMarkDeviceLost(Host* host, const char* callSite, HRESULT hr);

/* Env PALACE_SMOOTH_DRED=1 (Release OK): auto-breadcrumbs + page-fault before CreateDevice. */
void PsvTryEnableDredBeforeCreate();
void PsvDumpDred(Host* host);

/* Debug log / Diligent callback / D3D12 InfoQueue (palace_smooth_debug.cpp). */
void PsvSetApiTag(const char* tag);
const char* PsvGetApiTag();
void PsvInstallDiligentDebugLogging();
void PsvAppendDebugNote(const char* note);
/* Non-Ok TileUpload / TileMipBox (and similar): status code + lastError string. */
void PsvLogNonOkStatus(int code, const char* lastError);
void PsvSetupD3D12InfoQueue(Host* host);
void PsvDrainD3D12InfoQueue(Host* host);

struct PsvApiScope {
    explicit PsvApiScope(const char* tag) { PsvSetApiTag(tag); }
    ~PsvApiScope() { PsvSetApiTag(""); }
    PsvApiScope(const PsvApiScope&) = delete;
    PsvApiScope& operator=(const PsvApiScope&) = delete;
};

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
bool EnsureImagePipeline(Host* host);
HRESULT BindAndDrawImage(Host* host);
void DestroyImagePass(Host* host);
#endif
