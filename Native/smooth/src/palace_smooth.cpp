/*
 * Palace Smooth Viewer V0 — Diligent presenter into WinUI SwapChainPanel.
 *
 * RGBA16F scRGB composition swap chain, max frame latency 1 + waitable object,
 * clear + HDR scRGB test pattern, real frame-time stats.
 *
 * D3D12 preferred; D3D11 fallback. DiligentCore when PALACE_SMOOTH_WITH_DILIGENT.
 */

#include "palace_smooth.h"
#include "pattern_hlsl.h"

#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_5.h>
#include <wrl/client.h>

#include <cmath>
#include <cstring>
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
#  include "RefCntAutoPtr.hpp"
using namespace Diligent;
#endif

namespace {

constexpr char kVersion[] = "0.1.0-smooth-v0";
constexpr DXGI_FORMAT kScrgbFormat = DXGI_FORMAT_R16G16B16A16_FLOAT;
constexpr UINT kBufferCount = 2;
constexpr UINT kLatencyFlags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;

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

    /* D3D12 path (native queue for CreateSwapChainForComposition). */
    ComPtr<ID3D12Device> d3d12Device;
    ComPtr<ID3D12CommandQueue> d3d12Queue;

    /* D3D11 path. */
    ComPtr<ID3D11Device> d3d11Device;
    ComPtr<ID3D11DeviceContext> d3d11Context;

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    RefCntAutoPtr<IRenderDevice> diligentDevice;
    RefCntAutoPtr<IDeviceContext> diligentContext;
    RefCntAutoPtr<IPipelineState> patternPso;
    RefCntAutoPtr<IShaderResourceBinding> patternSrb;
    bool diligentReady = false;
#endif

    LARGE_INTEGER qpcFreq{};
};

void SetError(Host* host, const char* msg)
{
    if (host) {
        host->lastError = msg ? msg : "";
    }
}

double ElapsedMs(LARGE_INTEGER freq, LARGE_INTEGER a, LARGE_INTEGER b)
{
    if (freq.QuadPart <= 0) {
        return 0.0;
    }
    return (1000.0 * static_cast<double>(b.QuadPart - a.QuadPart)) / static_cast<double>(freq.QuadPart);
}

HRESULT EnsureFactory(Host* host)
{
    if (host->factory) {
        return S_OK;
    }
    UINT flags = 0;
#if defined(_DEBUG)
    flags |= DXGI_CREATE_FACTORY_DEBUG;
#endif
    return CreateDXGIFactory2(flags, IID_PPV_ARGS(&host->factory));
}

void ReleaseWaitable(Host* host)
{
    if (host->frameLatencyWaitable) {
        CloseHandle(host->frameLatencyWaitable);
        host->frameLatencyWaitable = nullptr;
    }
}

HRESULT SetScrgbColorSpace(IDXGISwapChain3* sc)
{
    return sc->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709);
}

HRESULT CreateCompositionSwapChainDxgi(Host* host, unsigned width, unsigned height, IUnknown* deviceOrQueue)
{
    if (!host->factory || !deviceOrQueue) {
        SetError(host, "factory/device missing");
        return E_FAIL;
    }

    width = width < 2 ? 2 : width;
    height = height < 2 ? 2 : height;

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = width;
    desc.Height = height;
    desc.Format = kScrgbFormat;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = kBufferCount;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.Scaling = DXGI_SCALING_STRETCH;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    desc.Flags = kLatencyFlags;

    ComPtr<IDXGISwapChain1> sc1;
    HRESULT hr = host->factory->CreateSwapChainForComposition(deviceOrQueue, &desc, nullptr, &sc1);
    if (FAILED(hr)) {
        SetError(host, "CreateSwapChainForComposition failed");
        return hr;
    }

    ComPtr<IDXGISwapChain3> sc3;
    hr = sc1.As(&sc3);
    if (FAILED(hr)) {
        SetError(host, "QI IDXGISwapChain3 failed");
        return hr;
    }

    hr = sc3->SetMaximumFrameLatency(1);
    if (FAILED(hr)) {
        SetError(host, "SetMaximumFrameLatency(1) failed");
        return hr;
    }

    ReleaseWaitable(host);
    host->frameLatencyWaitable = sc3->GetFrameLatencyWaitableObject();
    if (!host->frameLatencyWaitable) {
        SetError(host, "GetFrameLatencyWaitableObject failed");
        return E_FAIL;
    }

    /* Attach first (WinUI), then color space — matches HdrSwapchainPresenter. */
    (void)SetScrgbColorSpace(sc3.Get());

    host->swapChain = sc3;
    host->width = width;
    host->height = height;
    return S_OK;
}

#if defined(PALACE_SMOOTH_WITH_DILIGENT)

bool CreatePatternPipeline(Host* host)
{
    ShaderCreateInfo sci{};
    sci.SourceLanguage = SHADER_SOURCE_LANGUAGE_HLSL;
    sci.Desc.ShaderType = SHADER_TYPE_VERTEX;
    sci.EntryPoint = "main";
    sci.Desc.Name = "SmoothPatternVS";
    sci.Source = kPatternVS;
    RefCntAutoPtr<IShader> vs;
    host->diligentDevice->CreateShader(sci, &vs);
    if (!vs) {
        SetError(host, "CreateShader VS failed");
        return false;
    }

    sci.Desc.ShaderType = SHADER_TYPE_PIXEL;
    sci.EntryPoint = "main";
    sci.Desc.Name = "SmoothPatternPS";
    sci.Source = kPatternPS;
    RefCntAutoPtr<IShader> ps;
    host->diligentDevice->CreateShader(sci, &ps);
    if (!ps) {
        SetError(host, "CreateShader PS failed");
        return false;
    }

    GraphicsPipelineStateCreateInfo psoCI{};
    psoCI.PSODesc.Name = "SmoothPatternPSO";
    psoCI.PSODesc.PipelineType = PIPELINE_TYPE_GRAPHICS;
    psoCI.GraphicsPipeline.NumRenderTargets = 1;
    psoCI.GraphicsPipeline.RTVFormats[0] = TEX_FORMAT_RGBA16_FLOAT;
    psoCI.GraphicsPipeline.PrimitiveTopology = PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    psoCI.GraphicsPipeline.RasterizerDesc.CullMode = CULL_MODE_NONE;
    psoCI.GraphicsPipeline.DepthStencilDesc.DepthEnable = False;
    psoCI.pVS = vs;
    psoCI.pPS = ps;

    host->diligentDevice->CreateGraphicsPipelineState(psoCI, &host->patternPso);
    if (!host->patternPso) {
        SetError(host, "CreateGraphicsPipelineState failed");
        return false;
    }
    host->patternPso->CreateShaderResourceBinding(&host->patternSrb, true);
    return true;
}

bool InitDiligentD3D12(Host* host)
{
    /* Own D3D12 device + queue so CreateSwapChainForComposition can use the queue,
     * then AttachToD3D12Device so Diligent shares the same device. */
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&host->d3d12Device));
    if (FAILED(hr)) {
        SetError(host, "D3D12CreateDevice failed");
        return false;
    }
    D3D12_COMMAND_QUEUE_DESC qd{};
    qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    hr = host->d3d12Device->CreateCommandQueue(&qd, IID_PPV_ARGS(&host->d3d12Queue));
    if (FAILED(hr)) {
        SetError(host, "CreateCommandQueue failed");
        return false;
    }

    auto* factory = GetEngineFactoryD3D12();
    if (!factory) {
        SetError(host, "GetEngineFactoryD3D12 failed");
        return false;
    }
    factory->LoadD3D12();

    RefCntAutoPtr<ICommandQueueD3D12> cmdQueue;
    factory->CreateCommandQueueD3D12(host->d3d12Device.Get(), host->d3d12Queue.Get(), nullptr, &cmdQueue);
    if (!cmdQueue) {
        SetError(host, "CreateCommandQueueD3D12 failed");
        return false;
    }

    EngineD3D12CreateInfo ci{};
#if defined(_DEBUG)
    ci.EnableValidation = true;
#endif
    ICommandQueueD3D12* queues[] = {cmdQueue};
    IDeviceContext* contexts[1] = {};
    factory->AttachToD3D12Device(
        host->d3d12Device.Get(),
        1,
        queues,
        ci,
        &host->diligentDevice,
        contexts);
    if (!host->diligentDevice || !contexts[0]) {
        SetError(host, "AttachToD3D12Device failed");
        return false;
    }
    host->diligentContext.Attach(contexts[0]);

    host->backend = PSV_BACKEND_D3D12;
    if (!CreatePatternPipeline(host)) {
        return false;
    }
    host->diligentReady = true;
    SetError(host, "Diligent D3D12 ready (AttachToD3D12Device)");
    return true;
}

bool InitDiligentD3D11(Host* host)
{
    auto* factory = GetEngineFactoryD3D11();
    if (!factory) {
        SetError(host, "GetEngineFactoryD3D11 failed");
        return false;
    }

    EngineD3D11CreateInfo ci{};
#if defined(_DEBUG)
    ci.EnableValidation = true;
#endif
    IDeviceContext* contexts[1] = {};
    factory->CreateDeviceAndContextsD3D11(ci, &host->diligentDevice, contexts);
    if (!host->diligentDevice || !contexts[0]) {
        SetError(host, "CreateDeviceAndContextsD3D11 failed");
        return false;
    }
    host->diligentContext.Attach(contexts[0]);

    RefCntAutoPtr<IRenderDeviceD3D11> device11(host->diligentDevice, IID_RenderDeviceD3D11);
    if (!device11) {
        SetError(host, "QI IRenderDeviceD3D11 failed");
        return false;
    }
    host->d3d11Device = device11->GetD3D11Device();
    if (!host->d3d11Device) {
        SetError(host, "GetD3D11Device null");
        return false;
    }
    host->d3d11Device->GetImmediateContext(&host->d3d11Context);

    host->backend = PSV_BACKEND_D3D11;
    if (!CreatePatternPipeline(host)) {
        return false;
    }
    host->diligentReady = true;
    SetError(host, "Diligent D3D11 ready");
    return true;
}

bool InitDiligent(Host* host, int prefer)
{
    if (prefer == PSV_BACKEND_D3D11) {
        return InitDiligentD3D11(host);
    }
    if (prefer == PSV_BACKEND_D3D12) {
        return InitDiligentD3D12(host);
    }
    if (InitDiligentD3D12(host)) {
        return true;
    }
    SetError(host, "D3D12 failed; trying D3D11");
    host->diligentDevice.Release();
    host->diligentContext.Release();
    host->patternPso.Release();
    host->patternSrb.Release();
    host->d3d12Device.Reset();
    host->d3d12Queue.Reset();
    return InitDiligentD3D11(host);
}

HRESULT BindAndDrawPattern(Host* host)
{
    if (!host->diligentReady || !host->swapChain) {
        SetError(host, "Diligent not ready");
        return E_FAIL;
    }

    RefCntAutoPtr<ITexture> backTex;
    if (host->backend == PSV_BACKEND_D3D12) {
        ComPtr<ID3D12Resource> bb;
        HRESULT hr = host->swapChain->GetBuffer(host->swapChain->GetCurrentBackBufferIndex(), IID_PPV_ARGS(&bb));
        if (FAILED(hr) || !bb) {
            SetError(host, "GetBuffer D3D12 failed");
            return hr;
        }
        RefCntAutoPtr<IRenderDeviceD3D12> device12(host->diligentDevice, IID_RenderDeviceD3D12);
        device12->CreateTextureFromD3DResource(bb.Get(), RESOURCE_STATE_PRESENT, &backTex);
    } else {
        ComPtr<ID3D11Texture2D> bb;
        HRESULT hr = host->swapChain->GetBuffer(0, IID_PPV_ARGS(&bb));
        if (FAILED(hr) || !bb) {
            SetError(host, "GetBuffer D3D11 failed");
            return hr;
        }
        RefCntAutoPtr<IRenderDeviceD3D11> device11(host->diligentDevice, IID_RenderDeviceD3D11);
        device11->CreateTexture2DFromD3DResource(bb.Get(), RESOURCE_STATE_UNKNOWN, &backTex);
    }

    if (!backTex) {
        SetError(host, "CreateTextureFromD3DResource failed");
        return E_FAIL;
    }

    auto* rtv = backTex->GetDefaultView(TEXTURE_VIEW_RENDER_TARGET);
    if (!rtv) {
        SetError(host, "GetDefaultView RTV failed");
        return E_FAIL;
    }

    ITextureView* rtvs[] = {rtv};
    host->diligentContext->SetRenderTargets(1, rtvs, nullptr, RESOURCE_STATE_TRANSITION_MODE_TRANSITION);

    const float clear[4] = {0.015f, 0.02f, 0.035f, 1.0f};
    host->diligentContext->ClearRenderTarget(rtv, clear, RESOURCE_STATE_TRANSITION_MODE_TRANSITION);

    host->diligentContext->SetPipelineState(host->patternPso);
    host->diligentContext->CommitShaderResources(host->patternSrb, RESOURCE_STATE_TRANSITION_MODE_TRANSITION);

    Viewport vp{};
    vp.Width = static_cast<float>(host->width);
    vp.Height = static_cast<float>(host->height);
    vp.MaxDepth = 1.f;
    host->diligentContext->SetViewports(1, &vp, host->width, host->height);

    DrawAttribs draw{};
    draw.NumVertices = 3;
    host->diligentContext->Draw(draw);
    host->diligentContext->Flush();
    return S_OK;
}

#else /* !PALACE_SMOOTH_WITH_DILIGENT */

bool InitStubD3D12(Host* host)
{
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&host->d3d12Device));
    if (FAILED(hr)) {
        SetError(host, "stub D3D12CreateDevice failed");
        return false;
    }
    D3D12_COMMAND_QUEUE_DESC qd{};
    qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    hr = host->d3d12Device->CreateCommandQueue(&qd, IID_PPV_ARGS(&host->d3d12Queue));
    if (FAILED(hr)) {
        SetError(host, "stub CreateCommandQueue failed");
        return false;
    }
    host->backend = PSV_BACKEND_D3D12;
    SetError(host, "stub D3D12 ready (Diligent not linked)");
    return true;
}

bool InitStubD3D11(Host* host)
{
    UINT flags = 0;
    D3D_FEATURE_LEVEL fl = D3D_FEATURE_LEVEL_11_0;
    HRESULT hr = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        flags,
        &fl,
        1,
        D3D11_SDK_VERSION,
        &host->d3d11Device,
        nullptr,
        &host->d3d11Context);
    if (FAILED(hr)) {
        SetError(host, "stub D3D11CreateDevice failed");
        return false;
    }
    host->backend = PSV_BACKEND_D3D11;
    SetError(host, "stub D3D11 ready (Diligent not linked)");
    return true;
}

bool InitDiligent(Host* host, int prefer)
{
    if (prefer == PSV_BACKEND_D3D11) {
        return InitStubD3D11(host);
    }
    if (prefer == PSV_BACKEND_D3D12) {
        return InitStubD3D12(host);
    }
    if (InitStubD3D12(host)) {
        return true;
    }
    return InitStubD3D11(host);
}

HRESULT BindAndDrawPattern(Host* host)
{
    /* Stub: clear only via D3D11 RTV when possible; D3D12 stub skips draw. */
    if (host->backend == PSV_BACKEND_D3D11 && host->d3d11Device && host->d3d11Context && host->swapChain) {
        ComPtr<ID3D11Texture2D> bb;
        HRESULT hr = host->swapChain->GetBuffer(0, IID_PPV_ARGS(&bb));
        if (FAILED(hr)) {
            return hr;
        }
        ComPtr<ID3D11RenderTargetView> rtv;
        hr = host->d3d11Device->CreateRenderTargetView(bb.Get(), nullptr, &rtv);
        if (FAILED(hr)) {
            return hr;
        }
        const float clear[4] = {0.08f, 0.12f, 0.35f, 1.0f};
        host->d3d11Context->ClearRenderTargetView(rtv.Get(), clear);
        SetError(host, "stub D3D11 clear (rebuild with Diligent)");
        return S_OK;
    }
    SetError(host, "stub D3D12 present without clear (rebuild with Diligent)");
    return S_OK;
}

#endif

HRESULT PresentSwapChain(Host* host)
{
    HRESULT hr = host->swapChain->Present(1, 0);
    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
        host->deviceLost = true;
        SetError(host, "Present device lost");
        return hr;
    }
    if (FAILED(hr)) {
        SetError(host, "Present failed");
    }
    return hr;
}

} // namespace

extern "C" {

PSV_API const char* psv_version(void)
{
    return kVersion;
}

PSV_API void* psv_create(int backend)
{
#if !defined(_M_X64) && !defined(_M_AMD64) && !defined(__x86_64__)
    return nullptr;
#else
    auto* host = new (std::nothrow) Host();
    if (!host) {
        return nullptr;
    }
    QueryPerformanceFrequency(&host->qpcFreq);
    if (FAILED(EnsureFactory(host))) {
        SetError(host, "CreateDXGIFactory2 failed");
        delete host;
        return nullptr;
    }
    if (!InitDiligent(host, backend)) {
        delete host;
        return nullptr;
    }
    return host;
#endif
}

PSV_API void psv_destroy(void* host_ptr)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return;
    }
    ReleaseWaitable(host);
    delete host;
}

PSV_API int psv_create_composition_swapchain(void* host_ptr, unsigned width, unsigned height, void** out_dxgi_swapchain)
{
    if (!host_ptr || !out_dxgi_swapchain) {
        return PSV_BAD_ARGUMENT;
    }
    *out_dxgi_swapchain = nullptr;
    auto* host = static_cast<Host*>(host_ptr);
    host->deviceLost = false;

    IUnknown* deviceOrQueue = nullptr;
    if (host->backend == PSV_BACKEND_D3D12) {
        deviceOrQueue = host->d3d12Queue.Get();
    } else {
        deviceOrQueue = host->d3d11Device.Get();
    }
    if (FAILED(CreateCompositionSwapChainDxgi(host, width, height, deviceOrQueue))) {
        return PSV_FAILED;
    }
    host->swapChain->AddRef();
    *out_dxgi_swapchain = host->swapChain.Get();
    SetError(host, "composition swapchain created (RGBA16F scRGB, latency 1)");
    return PSV_OK;
}

PSV_API int psv_resize(void* host_ptr, unsigned width, unsigned height)
{
    if (!host_ptr) {
        return PSV_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    if (!host->swapChain) {
        return PSV_NOT_ATTACHED;
    }
    width = width < 2 ? 2 : width;
    height = height < 2 ? 2 : height;
    if (width == host->width && height == host->height) {
        return PSV_OK;
    }

    HRESULT hr = host->swapChain->ResizeBuffers(kBufferCount, width, height, kScrgbFormat, kLatencyFlags);
    if (FAILED(hr)) {
        SetError(host, "ResizeBuffers failed");
        return PSV_RESIZE_FAILED;
    }
    host->width = width;
    host->height = height;
    /* Recreate waitable after resize (MSDN: may invalidate). */
    ReleaseWaitable(host);
    host->frameLatencyWaitable = host->swapChain->GetFrameLatencyWaitableObject();
    (void)SetScrgbColorSpace(host->swapChain.Get());
    SetError(host, "resized");
    return PSV_OK;
}

PSV_API int psv_set_dpi_scale(void* host_ptr, float scale_x, float scale_y)
{
    if (!host_ptr) {
        return PSV_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    host->dpiScaleX = scale_x > 0.f ? scale_x : 1.f;
    host->dpiScaleY = scale_y > 0.f ? scale_y : 1.f;
    return PSV_OK;
}

PSV_API int psv_render_frame(void* host_ptr)
{
    if (!host_ptr) {
        return PSV_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    if (!host->swapChain) {
        return PSV_NOT_ATTACHED;
    }
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }

    LARGE_INTEGER t0{}, tWait{}, tRender{}, tPresent{};
    QueryPerformanceCounter(&t0);

    if (host->frameLatencyWaitable) {
        WaitForSingleObjectEx(host->frameLatencyWaitable, 2000, TRUE);
    }
    QueryPerformanceCounter(&tWait);

    HRESULT hr = BindAndDrawPattern(host);
    QueryPerformanceCounter(&tRender);
    if (FAILED(hr)) {
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
        return PSV_FAILED;
#else
        /* stub may still present */
#endif
    }

    hr = PresentSwapChain(host);
    QueryPerformanceCounter(&tPresent);
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    if (FAILED(hr)) {
        return PSV_FAILED;
    }

    host->lastStats.wait_ms = ElapsedMs(host->qpcFreq, t0, tWait);
    host->lastStats.render_ms = ElapsedMs(host->qpcFreq, tWait, tRender);
    host->lastStats.present_ms = ElapsedMs(host->qpcFreq, tRender, tPresent);
    host->lastStats.total_ms = ElapsedMs(host->qpcFreq, t0, tPresent);
    host->lastStats.frame_index += 1;
    host->lastStats.width = host->width;
    host->lastStats.height = host->height;
    host->lastStats.backend = host->backend;
    return PSV_OK;
}

PSV_API int psv_last_frame_stats(void* host_ptr, psv_frame_stats* out)
{
    if (!host_ptr || !out) {
        return PSV_BAD_ARGUMENT;
    }
    *out = static_cast<Host*>(host_ptr)->lastStats;
    return PSV_OK;
}

PSV_API int psv_last_error(void* host_ptr, char* buffer, int buffer_bytes)
{
    const char* msg = "";
    if (host_ptr) {
        msg = static_cast<Host*>(host_ptr)->lastError.c_str();
    }
    const int need = static_cast<int>(std::strlen(msg)) + 1;
    if (buffer && buffer_bytes > 0) {
        const int n = need < buffer_bytes ? need : buffer_bytes;
        if (n > 0) {
            std::memcpy(buffer, msg, static_cast<size_t>(n - 1));
            buffer[n - 1] = '\0';
        }
    }
    return need;
}

PSV_API int psv_backend(void* host_ptr)
{
    if (!host_ptr) {
        return PSV_BACKEND_AUTO;
    }
    return static_cast<Host*>(host_ptr)->backend;
}

} // extern "C"
