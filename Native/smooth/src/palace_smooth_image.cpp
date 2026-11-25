/*
 * Smooth Viewer V2 — SmoothImageVS/PS viewport pass (Geo).
 * Per-frame view CB, tone CB + LUT, instanced tiles, ONE/INV_SRC_ALPHA.
 * Tone mapping only in the final PS (ToneMap.hlsl include).
 */

#include "palace_smooth_internal.hpp"

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(PALACE_SMOOTH_WITH_DILIGENT)

namespace {

constexpr unsigned kMaxInstances = 4096;
constexpr unsigned kDefaultLutSize = 256;

bool EnsureShaderFactoryImage(Host* host)
{
    if (host->shaderFactory) {
        return true;
    }
    if (!host->diligentDevice) {
        SetError(host, "Diligent device missing for image shader factory");
        return false;
    }
    auto* engFactory = host->diligentDevice->GetEngineFactory();
    if (!engFactory) {
        SetError(host, "GetEngineFactory failed");
        return false;
    }
    const char* dirs = host->shaderSearchDirs[0] ? host->shaderSearchDirs : ".";
    engFactory->CreateDefaultShaderSourceStreamFactory(dirs, &host->shaderFactory);
    if (!host->shaderFactory) {
        SetError(host, "CreateDefaultShaderSourceStreamFactory failed");
        return false;
    }
    return true;
}

bool CreateImagePipeline(Host* host)
{
    if (!EnsureShaderFactoryImage(host)) {
        return false;
    }

    ShaderCreateInfo sci{};
    sci.SourceLanguage = SHADER_SOURCE_LANGUAGE_HLSL;
    sci.Desc.ShaderType = SHADER_TYPE_VERTEX;
    sci.EntryPoint = "SmoothImageVS";
    sci.Desc.Name = "SmoothImageVS";
    sci.FilePath = "SmoothImage.hlsl";
    sci.pShaderSourceStreamFactory = host->shaderFactory;

    RefCntAutoPtr<IShader> vs;
    host->diligentDevice->CreateShader(sci, &vs);
    if (!vs) {
        SetError(host, "CreateShader SmoothImageVS failed (vs_5_0)");
        return false;
    }

    sci.Desc.ShaderType = SHADER_TYPE_PIXEL;
    sci.EntryPoint = "SmoothImagePS";
    sci.Desc.Name = "SmoothImagePS";
    RefCntAutoPtr<IShader> ps;
    host->diligentDevice->CreateShader(sci, &ps);
    if (!ps) {
        SetError(host, "CreateShader SmoothImagePS failed (ps_5_0 / ToneMap.hlsl?)");
        return false;
    }

    GraphicsPipelineStateCreateInfo psoCI{};
    psoCI.PSODesc.Name = "SmoothImagePSO";
    psoCI.PSODesc.PipelineType = PIPELINE_TYPE_GRAPHICS;
    psoCI.GraphicsPipeline.NumRenderTargets = 1;
    psoCI.GraphicsPipeline.RTVFormats[0] = TEX_FORMAT_RGBA16_FLOAT;
    psoCI.GraphicsPipeline.PrimitiveTopology = PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP;
    psoCI.GraphicsPipeline.RasterizerDesc.CullMode = CULL_MODE_NONE;
    psoCI.GraphicsPipeline.DepthStencilDesc.DepthEnable = False;

    auto& blend = psoCI.GraphicsPipeline.BlendDesc.RenderTargets[0];
    blend.BlendEnable = True;
    blend.SrcBlend = BLEND_FACTOR_ONE;
    blend.DestBlend = BLEND_FACTOR_INV_SRC_ALPHA;
    blend.BlendOp = BLEND_OPERATION_ADD;
    blend.SrcBlendAlpha = BLEND_FACTOR_ONE;
    blend.DestBlendAlpha = BLEND_FACTOR_INV_SRC_ALPHA;
    blend.BlendOpAlpha = BLEND_OPERATION_ADD;
    blend.RenderTargetWriteMask = COLOR_MASK_ALL;

    /* DYNAMIC so pool recreate / LUT swap can rebind without rebuilding the SRB. */
    ShaderResourceVariableDesc vars[] = {
        {SHADER_TYPE_VERTEX | SHADER_TYPE_PIXEL, "SmoothViewCB", SHADER_RESOURCE_VARIABLE_TYPE_STATIC},
        {SHADER_TYPE_PIXEL, "SmoothToneCB", SHADER_RESOURCE_VARIABLE_TYPE_STATIC},
        {SHADER_TYPE_VERTEX | SHADER_TYPE_PIXEL, "Tiles", SHADER_RESOURCE_VARIABLE_TYPE_DYNAMIC},
        {SHADER_TYPE_PIXEL, "TileArr", SHADER_RESOURCE_VARIABLE_TYPE_DYNAMIC},
        {SHADER_TYPE_PIXEL, "ToneLut", SHADER_RESOURCE_VARIABLE_TYPE_DYNAMIC},
        {SHADER_TYPE_PIXEL, "LinearClamp", SHADER_RESOURCE_VARIABLE_TYPE_STATIC},
    };
    psoCI.PSODesc.ResourceLayout.Variables = vars;
    psoCI.PSODesc.ResourceLayout.NumVariables = _countof(vars);
    psoCI.PSODesc.ResourceLayout.DefaultVariableType = SHADER_RESOURCE_VARIABLE_TYPE_MUTABLE;

    SamplerDesc linearClampDesc{
        FILTER_TYPE_LINEAR, FILTER_TYPE_LINEAR, FILTER_TYPE_LINEAR,
        TEXTURE_ADDRESS_CLAMP, TEXTURE_ADDRESS_CLAMP, TEXTURE_ADDRESS_CLAMP};
    ImmutableSamplerDesc samplers[] = {
        {SHADER_TYPE_PIXEL, "LinearClamp", linearClampDesc}
    };
    psoCI.PSODesc.ResourceLayout.ImmutableSamplers = samplers;
    psoCI.PSODesc.ResourceLayout.NumImmutableSamplers = _countof(samplers);

    psoCI.pVS = vs;
    psoCI.pPS = ps;
    host->diligentDevice->CreateGraphicsPipelineState(psoCI, &host->imagePso);
    if (!host->imagePso) {
        SetError(host, "CreateGraphicsPipelineState SmoothImage failed");
        return false;
    }

    BufferDesc vcb{};
    vcb.Name = "SmoothViewCB";
    vcb.Size = sizeof(psv_view_cb);
    vcb.Usage = USAGE_DYNAMIC;
    vcb.BindFlags = BIND_UNIFORM_BUFFER;
    vcb.CPUAccessFlags = CPU_ACCESS_WRITE;
    host->diligentDevice->CreateBuffer(vcb, nullptr, &host->viewCb);
    if (!host->viewCb) {
        SetError(host, "CreateBuffer SmoothViewCB failed");
        return false;
    }

    BufferDesc tcb{};
    tcb.Name = "SmoothToneCB";
    tcb.Size = sizeof(psv_tone_cb);
    tcb.Usage = USAGE_DYNAMIC;
    tcb.BindFlags = BIND_UNIFORM_BUFFER;
    tcb.CPUAccessFlags = CPU_ACCESS_WRITE;
    host->diligentDevice->CreateBuffer(tcb, nullptr, &host->toneCb);
    if (!host->toneCb) {
        SetError(host, "CreateBuffer SmoothToneCB failed");
        return false;
    }

    BufferDesc ib{};
    ib.Name = "SmoothTileInstances";
    ib.Size = sizeof(psv_tile_instance) * kMaxInstances;
    ib.Usage = USAGE_DYNAMIC;
    ib.BindFlags = BIND_SHADER_RESOURCE;
    ib.CPUAccessFlags = CPU_ACCESS_WRITE;
    ib.Mode = BUFFER_MODE_STRUCTURED;
    ib.ElementByteStride = sizeof(psv_tile_instance);
    host->diligentDevice->CreateBuffer(ib, nullptr, &host->instanceBuf);
    if (!host->instanceBuf) {
        SetError(host, "CreateBuffer SmoothTileInstances failed");
        return false;
    }

    /* Fixed 256×1 R32F LUT (Geo); UpdateTexture overwrite — no per-image recreate. */
    TextureDesc ltd{};
    ltd.Name = "SmoothToneLut";
    ltd.Type = RESOURCE_DIM_TEX_2D;
    ltd.Width = kDefaultLutSize;
    ltd.Height = 1;
    ltd.Format = TEX_FORMAT_R32_FLOAT;
    ltd.MipLevels = 1;
    ltd.BindFlags = BIND_SHADER_RESOURCE;
    ltd.Usage = USAGE_DEFAULT;
    host->diligentDevice->CreateTexture(ltd, nullptr, &host->toneLut);
    if (!host->toneLut) {
        SetError(host, "CreateTexture ToneLut failed");
        return false;
    }
    host->toneLutSize = kDefaultLutSize;

    if (auto* var = host->imagePso->GetStaticVariableByName(SHADER_TYPE_VERTEX, "SmoothViewCB")) {
        var->Set(host->viewCb);
    }
    if (auto* var = host->imagePso->GetStaticVariableByName(SHADER_TYPE_PIXEL, "SmoothViewCB")) {
        var->Set(host->viewCb);
    }
    if (auto* var = host->imagePso->GetStaticVariableByName(SHADER_TYPE_PIXEL, "SmoothToneCB")) {
        var->Set(host->toneCb);
    }

    host->imagePso->CreateShaderResourceBinding(&host->imageSrb, true);
    if (!host->imageSrb) {
        SetError(host, "CreateShaderResourceBinding SmoothImage failed");
        return false;
    }

    host->imageReady = true;
    ClearError(host);
    return true;
}

void BindImageMutable(Host* host)
{
    if (!host->imageSrb || !host->tileArray || !host->instanceBuf || !host->toneLut) {
        return;
    }
    if (auto* var = host->imageSrb->GetVariableByName(SHADER_TYPE_VERTEX, "Tiles")) {
        var->Set(host->instanceBuf->GetDefaultView(BUFFER_VIEW_SHADER_RESOURCE));
    }
    if (auto* var = host->imageSrb->GetVariableByName(SHADER_TYPE_PIXEL, "Tiles")) {
        var->Set(host->instanceBuf->GetDefaultView(BUFFER_VIEW_SHADER_RESOURCE));
    }
    if (auto* var = host->imageSrb->GetVariableByName(SHADER_TYPE_PIXEL, "TileArr")) {
        var->Set(host->tileArray->GetDefaultView(TEXTURE_VIEW_SHADER_RESOURCE));
    }
    if (auto* var = host->imageSrb->GetVariableByName(SHADER_TYPE_PIXEL, "ToneLut")) {
        var->Set(host->toneLut->GetDefaultView(TEXTURE_VIEW_SHADER_RESOURCE));
    }
}

} // namespace

bool EnsureImagePipeline(Host* host)
{
    if (host->imageReady && host->imagePso) {
        return true;
    }
    return CreateImagePipeline(host);
}

HRESULT BindAndDrawImage(Host* host)
{
    if (!host->diligentReady || !host->swapChain) {
        SetError(host, "Diligent not ready");
        return E_FAIL;
    }
    if (!host->viewportEnabled || host->instanceCount == 0) {
        return S_FALSE;
    }
    if (!host->tilePoolReady || !host->tileArray) {
        SetError(host, "tile pool required for image pass");
        return E_FAIL;
    }
    if (!EnsureImagePipeline(host)) {
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

    {
        void* data = nullptr;
        host->diligentContext->MapBuffer(host->viewCb, MAP_WRITE, MAP_FLAG_DISCARD, data);
        if (data) {
            *static_cast<psv_view_cb*>(data) = host->viewCpu;
            host->diligentContext->UnmapBuffer(host->viewCb, MAP_WRITE);
        }
    }
    {
        void* data = nullptr;
        host->diligentContext->MapBuffer(host->toneCb, MAP_WRITE, MAP_FLAG_DISCARD, data);
        if (data) {
            *static_cast<psv_tone_cb*>(data) = host->toneCpu;
            host->diligentContext->UnmapBuffer(host->toneCb, MAP_WRITE);
        }
    }

    BindImageMutable(host);
    host->diligentContext->SetPipelineState(host->imagePso);
    host->diligentContext->CommitShaderResources(host->imageSrb, RESOURCE_STATE_TRANSITION_MODE_TRANSITION);

    Viewport vp{};
    vp.Width = static_cast<float>(host->width);
    vp.Height = static_cast<float>(host->height);
    vp.MaxDepth = 1.f;
    host->diligentContext->SetViewports(1, &vp, host->width, host->height);

    DrawAttribs draw{};
    draw.NumVertices = 4;
    draw.NumInstances = host->instanceCount;
    host->diligentContext->Draw(draw);
    /* Hygiene: clear RTs before Flush so a Debug Close assert cannot leave RTs
     * bound for a nested Tick upload (MessageBox pump / reentrancy). */
    host->diligentContext->SetRenderTargets(0, nullptr, nullptr, RESOURCE_STATE_TRANSITION_MODE_NONE);
    host->diligentContext->Flush();
    /* Always clear again after Flush even if Debug asserted mid-Close. */
    host->diligentContext->SetRenderTargets(0, nullptr, nullptr, RESOURCE_STATE_TRANSITION_MODE_NONE);
    PsvDrainD3D12InfoQueue(host);

    /* PRESENT transition before Present (D3D12 debug layer). */
    if (host->backend == PSV_BACKEND_D3D12 && backTex) {
        StateTransitionDesc barrier{
            backTex,
            RESOURCE_STATE_UNKNOWN,
            RESOURCE_STATE_PRESENT,
            STATE_TRANSITION_FLAG_UPDATE_STATE};
        host->diligentContext->TransitionResourceStates(1, &barrier);
    }

    backTex.Release();
    ClearError(host);
    SetError(host, "image pass drawn");
    return S_OK;
}

void DestroyImagePass(Host* host)
{
    host->viewportEnabled = false;
    host->instanceCount = 0;
    host->imageSrb.Release();
    host->imagePso.Release();
    host->viewCb.Release();
    host->toneCb.Release();
    host->instanceBuf.Release();
    host->toneLut.Release();
    host->toneLutSize = 0;
    host->imageReady = false;
}

#endif /* DILIGENT */

extern "C" {

PSV_API int psv_viewport_enable(void* host_ptr, int enabled)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    if (enabled) {
        if (!EnsureImagePipeline(host)) {
            return PSV_FAILED;
        }
        host->viewportEnabled = true;
        ClearError(host);
    } else {
        host->viewportEnabled = false;
        host->instanceCount = 0;
        ClearError(host);
    }
    return PSV_OK;
#else
    (void)enabled;
    SetError(host, "Diligent required for viewport");
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_viewport_set_view(void* host_ptr, const psv_view_cb* view)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !view) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    host->viewCpu = *view;
    return PSV_OK;
#else
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_viewport_set_tone(void* host_ptr, const psv_tone_cb* tone)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !tone) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    host->toneCpu = *tone;
    return PSV_OK;
#else
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_viewport_set_lut(void* host_ptr, const float* samples, unsigned count)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !samples || count < 2) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    if (!host->diligentReady || !host->diligentDevice) {
        return PSV_FAILED;
    }
    if (!EnsureImagePipeline(host)) {
        return PSV_FAILED;
    }

    /* Fixed 256 LUT: copy min(count, 256) samples; pad remainder by clamp. */
    if (!host->toneLut) {
        TextureDesc ltd{};
        ltd.Name = "SmoothToneLut";
        ltd.Type = RESOURCE_DIM_TEX_2D;
        ltd.Width = kDefaultLutSize;
        ltd.Height = 1;
        ltd.Format = TEX_FORMAT_R32_FLOAT;
        ltd.MipLevels = 1;
        ltd.BindFlags = BIND_SHADER_RESOURCE;
        ltd.Usage = USAGE_DEFAULT;
        host->diligentDevice->CreateTexture(ltd, nullptr, &host->toneLut);
        if (!host->toneLut) {
            SetError(host, "CreateTexture ToneLut failed");
            return PSV_FAILED;
        }
        host->toneLutSize = kDefaultLutSize;
    }

    float lut[kDefaultLutSize];
    const unsigned n = count < kDefaultLutSize ? count : kDefaultLutSize;
    std::memcpy(lut, samples, n * sizeof(float));
    for (unsigned i = n; i < kDefaultLutSize; ++i) {
        lut[i] = samples[n - 1];
    }

    TextureSubResData sub{};
    sub.pData = lut;
    sub.Stride = kDefaultLutSize * sizeof(float);
    Box box{};
    box.MinX = 0;
    box.MaxX = kDefaultLutSize;
    box.MinY = 0;
    box.MaxY = 1;
    box.MinZ = 0;
    box.MaxZ = 1;
    host->diligentContext->UpdateTexture(
        host->toneLut,
        0,
        0,
        box,
        sub,
        RESOURCE_STATE_TRANSITION_MODE_TRANSITION,
        RESOURCE_STATE_TRANSITION_MODE_TRANSITION);
    ClearError(host);
    return PSV_OK;
#else
    (void)count;
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_viewport_set_instances(
    void* host_ptr,
    const psv_tile_instance* instances,
    unsigned count)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    if (count > kMaxInstances) {
        SetError(host, "instance count exceeds 4096");
        return PSV_BAD_ARGUMENT;
    }
    if (!EnsureImagePipeline(host)) {
        return PSV_FAILED;
    }
    if (count == 0 || !instances) {
        host->instanceCount = 0;
        return PSV_OK;
    }

    {
        void* data = nullptr;
        host->diligentContext->MapBuffer(host->instanceBuf, MAP_WRITE, MAP_FLAG_DISCARD, data);
        if (!data) {
            SetError(host, "Map instance buffer failed");
            return PSV_FAILED;
        }
        std::memcpy(data, instances, sizeof(psv_tile_instance) * count);
        host->diligentContext->UnmapBuffer(host->instanceBuf, MAP_WRITE);
    }
    host->instanceCount = count;
    ClearError(host);
    return PSV_OK;
#else
    (void)instances;
    (void)count;
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_viewport_ready(void* host_ptr)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return 0;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    return (host->imageReady && host->viewportEnabled) ? 1 : 0;
#else
    return 0;
#endif
}

} // extern "C"
