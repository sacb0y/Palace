/*
 * Palace Smooth Viewer V0 — Diligent presenter into WinUI SwapChainPanel.
 *
 * RGBA16F scRGB composition swap chain, max frame latency 1 + waitable object,
 * clear + HDR scRGB test pattern, real frame-time stats.
 *
 * D3D12 preferred; D3D11 fallback. DiligentCore when PALACE_SMOOTH_WITH_DILIGENT.
 */

#include "palace_smooth_internal.hpp"
#include "pattern_hlsl.h"

#include <cmath>
#include <cstdio>
#include <cstring>

#if defined(PSV_TEST_HOOKS)
#  include <d3d12sdklayers.h>
#endif

namespace {
/* Survives psv_create failure so LastError(null) can report "device still removed". */
char g_lastCreateError[256] = {};
} // namespace

void SetError(Host* host, const char* msg)
{
    if (host) {
        host->lastError = msg ? msg : "";
    }
    if (msg && msg[0]) {
        std::snprintf(g_lastCreateError, sizeof(g_lastCreateError), "%s", msg);
    }
}

void ClearError(Host* host)
{
    if (host) {
        host->lastError.clear();
    }
}

bool PollDeviceRemoved(Host* host)
{
    if (!host) {
        return true;
    }
    if (host->deviceLost) {
        return true;
    }
    if (host->d3d12Device) {
        const HRESULT reason = host->d3d12Device->GetDeviceRemovedReason();
        if (reason != S_OK) {
            host->deviceLost = true;
            char buf[96];
            std::snprintf(
                buf,
                sizeof(buf),
                "device removed hr=0x%08X",
                static_cast<unsigned>(reason));
            SetError(host, buf);
            return true;
        }
        return false;
    }
    if (host->d3d11Device) {
        const HRESULT reason = host->d3d11Device->GetDeviceRemovedReason();
        if (reason != S_OK) {
            host->deviceLost = true;
            char buf[96];
            std::snprintf(
                buf,
                sizeof(buf),
                "device removed hr=0x%08X",
                static_cast<unsigned>(reason));
            SetError(host, buf);
            return true;
        }
    }
    return false;
}

bool BindDxgiAdapter3(Host* host)
{
    if (!host) {
        return false;
    }
    if (host->adapter3) {
        return true;
    }

    LUID want{};
    bool haveLuid = false;
    if (host->d3d12Device) {
        want = host->d3d12Device->GetAdapterLuid();
        haveLuid = true;
    } else if (host->d3d11Device) {
        ComPtr<IDXGIDevice> dxgiDev;
        if (SUCCEEDED(host->d3d11Device.As(&dxgiDev)) && dxgiDev) {
            ComPtr<IDXGIAdapter> ad;
            if (SUCCEEDED(dxgiDev->GetAdapter(&ad)) && ad) {
                return SUCCEEDED(ad.As(&host->adapter3)) && host->adapter3;
            }
        }
        return false;
    }

    if (!haveLuid || !host->factory) {
        return false;
    }

    for (UINT i = 0;; ++i) {
        ComPtr<IDXGIAdapter1> ad1;
        if (host->factory->EnumAdapters1(i, &ad1) == DXGI_ERROR_NOT_FOUND) {
            break;
        }
        if (!ad1) {
            continue;
        }
        DXGI_ADAPTER_DESC1 desc{};
        if (FAILED(ad1->GetDesc1(&desc))) {
            continue;
        }
        if (desc.AdapterLuid.LowPart == want.LowPart && desc.AdapterLuid.HighPart == want.HighPart) {
            return SUCCEEDED(ad1.As(&host->adapter3)) && host->adapter3;
        }
    }
    return false;
}

namespace {

constexpr char kVersion[] = "0.3.0-smooth-v2";
constexpr DXGI_FORMAT kScrgbFormat = DXGI_FORMAT_R16G16B16A16_FLOAT;
constexpr UINT kBufferCount = 2;
constexpr UINT kLatencyFlags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;

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

#if defined(PSV_TEST_HOOKS)
/* Must run before D3D12CreateDevice. Diligent EnableValidation alone is not
 * enough when we AttachToD3D12Device on an app-owned device. Requires the
 * Windows "Graphics Tools" optional feature — do not install system-wide here. */
bool TryEnableD3D12DebugLayerBeforeCreate()
{
    ComPtr<ID3D12Debug> dbg;
    const HRESULT hr = D3D12GetDebugInterface(IID_PPV_ARGS(&dbg));
    if (FAILED(hr) || !dbg) {
        PsvAppendDebugNote(
            "D3D12GetDebugInterface failed — install Windows optional feature "
            "'Graphics Tools' for Debug-layer InfoQueue (not installing here)");
        SetError(nullptr, "D3D12 debug layer unavailable (Graphics Tools?)");
        return false;
    }
    dbg->EnableDebugLayer();
    PsvAppendDebugNote("D3D12 debug layer enabled before CreateDevice");
    return true;
}
#endif

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
#if defined(PSV_TEST_HOOKS)
    TryEnableD3D12DebugLayerBeforeCreate();
#endif
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&host->d3d12Device));
    if (FAILED(hr)) {
        SetError(host, "D3D12CreateDevice failed");
        return false;
    }
    /* TDR sticky: CreateDevice can hand back a removed device — never Diligent on it. */
    if (host->d3d12Device->GetDeviceRemovedReason() != S_OK) {
        SetError(host, "device still removed");
        host->deviceLost = true;
        host->d3d12Device.Reset();
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
    if (!BindDxgiAdapter3(host)) {
        SetError(host, "BindDxgiAdapter3 failed after D3D12 init");
        /* Non-fatal for present; VRAM query will fail until rebound. */
    }
    if (!CreatePatternPipeline(host)) {
        return false;
    }
    PsvInstallDiligentDebugLogging();
    PsvSetupD3D12InfoQueue(host);
    host->diligentReady = true;
    ClearError(host);
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
    if (!BindDxgiAdapter3(host)) {
        SetError(host, "BindDxgiAdapter3 failed after D3D11 init");
    }
    if (!CreatePatternPipeline(host)) {
        return false;
    }
    host->diligentReady = true;
    ClearError(host);
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

} // namespace (temp close for Unbind export)

void UnbindAndIdleDiligent(Host* host)
{
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (!host || !host->diligentContext) {
        return;
    }
    /* Never Flush/WaitForIdle on a removed device — that path AVs under TDR. */
    if (PollDeviceRemoved(host)) {
        return;
    }
    /* Drop RT binds so DXGI ResizeBuffers can reclaim the backbuffer. */
    host->diligentContext->SetRenderTargets(0, nullptr, nullptr, RESOURCE_STATE_TRANSITION_MODE_NONE);
    host->diligentContext->Flush();
    host->diligentContext->WaitForIdle();
    if (host->diligentDevice) {
        host->diligentDevice->ReleaseStaleResources(true);
    }
#else
    (void)host;
#endif
}

namespace {

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
        backTex.Release();
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
    host->diligentContext->SetRenderTargets(0, nullptr, nullptr, RESOURCE_STATE_TRANSITION_MODE_NONE);
    host->diligentContext->Flush();
    /* Unbind + release wrap so the next psv_resize can ResizeBuffers. */
    host->diligentContext->SetRenderTargets(0, nullptr, nullptr, RESOURCE_STATE_TRANSITION_MODE_NONE);
    PsvDrainD3D12InfoQueue(host);
    backTex.Release();
    return S_OK;
}

#else /* !PALACE_SMOOTH_WITH_DILIGENT */

bool InitStubD3D12(Host* host)
{
#if defined(PSV_TEST_HOOKS)
    TryEnableD3D12DebugLayerBeforeCreate();
#endif
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&host->d3d12Device));
    if (FAILED(hr)) {
        SetError(host, "stub D3D12CreateDevice failed");
        return false;
    }
    if (host->d3d12Device->GetDeviceRemovedReason() != S_OK) {
        SetError(host, "device still removed");
        host->deviceLost = true;
        host->d3d12Device.Reset();
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
    PsvSetupD3D12InfoQueue(host);
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
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    /* Drop all Diligent refs before touching the D3D device (TDR-safe order). */
    DestroyImagePass(host);
    psv_tile_pool_destroy(host);
    host->patternSrb.Release();
    host->patternPso.Release();
    host->shaderFactory.Release();
    /* Unbind RTs + WaitForIdle / ReleaseStaleResources — skipped when already removed. */
    UnbindAndIdleDiligent(host);
    host->diligentContext.Release();
    host->diligentDevice.Release();
    host->diligentReady = false;
#endif
    ReleaseWaitable(host);
    host->swapChain.Reset();
    host->adapter3.Reset();
    host->factory.Reset();
    host->d3d11Context.Reset();
    host->d3d11Device.Reset();
    host->d3d12Queue.Reset();

#if defined(PSV_TEST_HOOKS)
    if (host->d3d12Device) {
        /* AddRef/Release pair reports live refcount (expect 1 = this ComPtr only → 0 after Reset). */
        const ULONG afterAdd = host->d3d12Device->AddRef();
        const ULONG afterRel = host->d3d12Device->Release();
        (void)afterAdd;
        char buf[128];
        std::snprintf(
            buf,
            sizeof(buf),
            "psv_destroy: D3D12 refcount=%lu (expect 0 after ComPtr Reset)",
            static_cast<unsigned long>(afterRel > 0 ? afterRel - 1 : 0));
        OutputDebugStringA(buf);
        OutputDebugStringA("\n");
    }
#endif
    host->d3d12Device.Reset();
    delete host;
}

PSV_API int psv_create_composition_swapchain(void* host_ptr, unsigned width, unsigned height, void** out_dxgi_swapchain)
{
    if (!host_ptr || !out_dxgi_swapchain) {
        return PSV_BAD_ARGUMENT;
    }
    *out_dxgi_swapchain = nullptr;
    auto* host = static_cast<Host*>(host_ptr);
    /* Never hard-clear deviceLost — derive from GetDeviceRemovedReason. */
    if (PollDeviceRemoved(host)) {
        SetError(host, "device still removed");
        return PSV_DEVICE_LOST;
    }

    IUnknown* deviceOrQueue = nullptr;
    if (host->backend == PSV_BACKEND_D3D12) {
        deviceOrQueue = host->d3d12Queue.Get();
    } else {
        deviceOrQueue = host->d3d11Device.Get();
    }
    if (FAILED(CreateCompositionSwapChainDxgi(host, width, height, deviceOrQueue))) {
        if (PollDeviceRemoved(host)) {
            SetError(host, "device still removed");
            return PSV_DEVICE_LOST;
        }
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

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    /* Must drop Diligent RT wraps before DXGI will allow ResizeBuffers. */
    UnbindAndIdleDiligent(host);
#endif
    if (host->backend == PSV_BACKEND_D3D11 && host->d3d11Context) {
        host->d3d11Context->OMSetRenderTargets(0, nullptr, nullptr);
        host->d3d11Context->Flush();
    }

    HRESULT hr = host->swapChain->ResizeBuffers(kBufferCount, width, height, kScrgbFormat, kLatencyFlags);
    if (FAILED(hr)) {
        char buf[96];
        std::snprintf(buf, sizeof(buf), "ResizeBuffers failed hr=0x%08X", static_cast<unsigned>(hr));
        SetError(host, buf);
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
    PsvApiScope api("psv_render_frame");
    if (!host_ptr) {
        return PSV_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    if (!host->swapChain) {
        return PSV_NOT_ATTACHED;
    }
    /* Cheap removed poll before any Diligent work. */
    if (PollDeviceRemoved(host)) {
        return PSV_DEVICE_LOST;
    }

    LARGE_INTEGER t0{}, tWait{}, tRender{}, tPresent{};
    QueryPerformanceCounter(&t0);

    /* Non-alertable, non-blocking: skip the frame if the waitable is not ready. */
    if (host->frameLatencyWaitable) {
        const DWORD wait = WaitForSingleObjectEx(host->frameLatencyWaitable, 0, FALSE);
        if (wait == WAIT_TIMEOUT) {
            QueryPerformanceCounter(&tWait);
            host->lastStats.wait_ms = ElapsedMs(host->qpcFreq, t0, tWait);
            host->lastStats.render_ms = 0.0;
            host->lastStats.present_ms = 0.0;
            host->lastStats.total_ms = host->lastStats.wait_ms;
            host->lastStats.width = host->width;
            host->lastStats.height = host->height;
            host->lastStats.backend = host->backend;
            SetError(host, "frame latency not ready — skipped");
            return PSV_OK;
        }
    }
    QueryPerformanceCounter(&tWait);

    HRESULT hr = E_FAIL;
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->viewportEnabled && host->instanceCount > 0) {
        hr = BindAndDrawImage(host);
        if (hr == S_FALSE) {
            hr = BindAndDrawPattern(host);
        }
    } else {
        hr = BindAndDrawPattern(host);
    }
#else
    hr = BindAndDrawPattern(host);
#endif
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
    PsvDrainD3D12InfoQueue(host);
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
    /* Clear sticky lastError so frame logs stay clean after a good present. */
    SetError(host, "");
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
    } else if (g_lastCreateError[0]) {
        /* psv_create failure path — host already deleted. */
        msg = g_lastCreateError;
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

PSV_API int psv_debug_remove_device(void* host_ptr)
{
#if !defined(PSV_TEST_HOOKS)
    (void)host_ptr;
    return PSV_FAILED;
#else
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return PSV_BAD_ARGUMENT;
    }
    if (!host->d3d12Device) {
        SetError(host, "psv_debug_remove_device: no D3D12 device");
        return PSV_FAILED;
    }
    ComPtr<ID3D12Device5> d5;
    if (FAILED(host->d3d12Device.As(&d5)) || !d5) {
        SetError(host, "psv_debug_remove_device: ID3D12Device5 unavailable");
        return PSV_FAILED;
    }
    d5->RemoveDevice();
    host->deviceLost = true;
    SetError(host, "psv_debug_remove_device: RemoveDevice invoked");
    return PSV_DEVICE_LOST;
#endif
}

} // extern "C"
