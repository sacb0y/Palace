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
    HANDLE frameLatencyWaitable = nullptr;

    ComPtr<ID3D12Device> d3d12Device;
    ComPtr<ID3D12CommandQueue> d3d12Queue;

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
    RefCntAutoPtr<ITexture> mipScratch; /* 512² single-slice; never SRV+UAV same resource */
    RefCntAutoPtr<IPipelineState> mipPso;
    RefCntAutoPtr<IShaderResourceBinding> mipSrb;
    RefCntAutoPtr<IBuffer> mipCb;
    unsigned tileCapacity = 0;
    bool tilePoolReady = false;
    bool mipReady = false;
    /* Semicolon-separated dirs for SmoothMipDownsample.hlsl (and later ToneMap includes). */
    char shaderSearchDirs[512]{};
#endif

    LARGE_INTEGER qpcFreq{};
};

void SetError(Host* host, const char* msg);
void UnbindAndIdleDiligent(Host* host);
