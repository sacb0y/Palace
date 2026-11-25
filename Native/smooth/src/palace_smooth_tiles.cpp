/*
 * Smooth Viewer V1 — GPU tile pool + SmoothMipDownsampleCS (Geo).
 * Working RGBA16F, Border=2, SMOOTH_SRGB8=0, scratch→CopyTexture.
 */

#include "palace_smooth_internal.hpp"

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(PALACE_SMOOTH_WITH_DILIGENT)

namespace {

constexpr unsigned kSlot = 512;
constexpr unsigned kBorder = 2;

bool EnsureShaderFactory(Host* host)
{
    if (host->shaderFactory) {
        return true;
    }
    if (!host->diligentDevice) {
        SetError(host, "Diligent device missing for shader factory");
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

bool CreateMipPipeline(Host* host)
{
    if (!EnsureShaderFactory(host)) {
        return false;
    }

    ShaderCreateInfo sci{};
    sci.SourceLanguage = SHADER_SOURCE_LANGUAGE_HLSL;
    sci.Desc.ShaderType = SHADER_TYPE_COMPUTE;
    sci.EntryPoint = "SmoothMipDownsampleCS";
    sci.Desc.Name = "SmoothMipDownsampleCS";
    sci.FilePath = "SmoothMipDownsample.hlsl";
    sci.pShaderSourceStreamFactory = host->shaderFactory;
    static const ShaderMacro kMacros[] = { { "SMOOTH_SRGB8", "0" } };
    sci.Macros = { kMacros, _countof(kMacros) };

    RefCntAutoPtr<IShader> cs;
    host->diligentDevice->CreateShader(sci, &cs);
    if (!cs) {
        SetError(host, "CreateShader SmoothMipDownsampleCS failed (cs_5_0)");
        return false;
    }

    ComputePipelineStateCreateInfo psoCI{};
    psoCI.PSODesc.Name = "SmoothMipDownsamplePSO";
    psoCI.PSODesc.PipelineType = PIPELINE_TYPE_COMPUTE;
    /* MUTABLE Src/Dst: rebind the same views each psv_tile_mip_box call.
     * Do not Set(nullptr) / DYNAMIC / ALLOW_OVERWRITE (Wimbly). */
    psoCI.PSODesc.ResourceLayout.DefaultVariableType = SHADER_RESOURCE_VARIABLE_TYPE_MUTABLE;
    psoCI.pCS = cs;
    host->diligentDevice->CreateComputePipelineState(psoCI, &host->mipPso);
    if (!host->mipPso) {
        SetError(host, "CreateComputePipelineState mip failed");
        return false;
    }
    host->mipPso->CreateShaderResourceBinding(&host->mipSrb, true);
    if (!host->mipSrb) {
        SetError(host, "CreateShaderResourceBinding mip failed");
        return false;
    }

    BufferDesc cbd{};
    cbd.Name = "SmoothMipCB";
    cbd.Size = 256;
    cbd.Usage = USAGE_DYNAMIC;
    cbd.BindFlags = BIND_UNIFORM_BUFFER;
    cbd.CPUAccessFlags = CPU_ACCESS_WRITE;
    host->diligentDevice->CreateBuffer(cbd, nullptr, &host->mipCb);
    if (!host->mipCb) {
        SetError(host, "CreateBuffer SmoothMipCB failed");
        return false;
    }

    host->mipReady = true;
    ClearError(host);
    return true;
}

struct SmoothMipCbCpu {
    uint32_t params[4];
    uint32_t levelInfo[4];
    int32_t origin[4];
    uint32_t slices[16];
};

} // namespace

#endif /* DILIGENT */

extern "C" {

PSV_API int psv_set_shader_search_dirs(void* host_ptr, const char* utf8_dirs)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        return PSV_DEVICE_LOST;
    }
    if (!utf8_dirs) {
        host->shaderSearchDirs[0] = '\0';
    } else {
        std::snprintf(host->shaderSearchDirs, sizeof(host->shaderSearchDirs), "%s", utf8_dirs);
        if (std::strlen(utf8_dirs) >= sizeof(host->shaderSearchDirs)) {
            SetError(host, "shaderSearchDirs truncated (max 511 chars)");
        }
    }
    /* Dirs changed — rebuild factory on next mip pipeline create. */
    host->shaderFactory.Release();
    return PSV_OK;
#else
    (void)utf8_dirs;
    SetError(host, "Diligent required for shader dirs");
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_query_video_memory(
    void* host_ptr,
    unsigned long long* out_budget_bytes,
    unsigned long long* out_current_usage_bytes,
    unsigned long long* out_available_bytes)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !out_budget_bytes || !out_current_usage_bytes || !out_available_bytes) {
        return PSV_BAD_ARGUMENT;
    }
    *out_budget_bytes = 0;
    *out_current_usage_bytes = 0;
    *out_available_bytes = 0;

    if (host->deviceLost) {
        SetError(host, "device lost");
        return PSV_DEVICE_LOST;
    }

    if (!host->adapter3 && !BindDxgiAdapter3(host)) {
        SetError(host, "IDXGIAdapter3 unavailable for QueryVideoMemoryInfo");
        return PSV_FAILED;
    }

    DXGI_QUERY_VIDEO_MEMORY_INFO info{};
    HRESULT hr = host->adapter3->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &info);
    if (FAILED(hr)) {
        SetError(host, "QueryVideoMemoryInfo failed");
        return PSV_FAILED;
    }

    *out_budget_bytes = info.Budget;
    *out_current_usage_bytes = info.CurrentUsage;
    *out_available_bytes = info.Budget > info.CurrentUsage ? (info.Budget - info.CurrentUsage) : 0ull;
    ClearError(host);
    return PSV_OK;
}

PSV_API int psv_tile_pool_create(void* host_ptr, unsigned capacity)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || capacity < 1) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        SetError(host, "device lost");
        return PSV_DEVICE_LOST;
    }
    if (!host->diligentReady || !host->diligentDevice) {
        SetError(host, "Diligent not ready for tile pool");
        return PSV_FAILED;
    }

    psv_tile_pool_destroy(host);

    TextureDesc td{};
    td.Name = "SmoothTileArray";
    td.Type = RESOURCE_DIM_TEX_2D_ARRAY;
    td.Width = kSlot;
    td.Height = kSlot;
    td.ArraySize = capacity;
    td.Format = TEX_FORMAT_RGBA16_FLOAT;
    td.MipLevels = 1;
    /* SRV + CopyTexture dest only — UAV lives on scratch. */
    td.BindFlags = BIND_SHADER_RESOURCE;
    td.Usage = USAGE_DEFAULT;
    host->diligentDevice->CreateTexture(td, nullptr, &host->tileArray);
    if (!host->tileArray) {
        SetError(host, "CreateTexture tile array failed");
        return PSV_FAILED;
    }

    TextureDesc sd{};
    sd.Name = "SmoothMipScratch";
    sd.Type = RESOURCE_DIM_TEX_2D_ARRAY;
    sd.Width = kSlot;
    sd.Height = kSlot;
    sd.ArraySize = 1;
    sd.Format = TEX_FORMAT_RGBA16_FLOAT;
    sd.MipLevels = 1;
    sd.BindFlags = BIND_SHADER_RESOURCE | BIND_UNORDERED_ACCESS;
    sd.Usage = USAGE_DEFAULT;
    host->diligentDevice->CreateTexture(sd, nullptr, &host->mipScratch);
    if (!host->mipScratch) {
        SetError(host, "CreateTexture mip scratch failed");
        host->tileArray.Release();
        return PSV_FAILED;
    }

    if (!CreateMipPipeline(host)) {
        host->mipScratch.Release();
        host->tileArray.Release();
        return PSV_FAILED;
    }

    host->tileCapacity = capacity;
    host->tilePoolReady = true;
    ClearError(host);
    return PSV_OK;
#else
    (void)capacity;
    SetError(host, "Diligent required for tile pool");
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API void psv_tile_pool_destroy(void* host_ptr)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    UnbindAndIdleDiligent(host);
    /* Drop image SRB binds that reference the tile array before releasing it. */
    if (host->imageSrb) {
        host->imageSrb.Release();
        if (host->imagePso) {
            host->imagePso->CreateShaderResourceBinding(&host->imageSrb, true);
        }
    }
    host->mipSrb.Release();
    host->mipPso.Release();
    host->mipCb.Release();
    host->mipScratch.Release();
    host->readbackStaging.Release();
    host->tileArray.Release();
    host->tileCapacity = 0;
    host->tilePoolReady = false;
    host->mipReady = false;
#else
    (void)host;
#endif
}

PSV_API int psv_tile_upload(void* host_ptr, unsigned slice, const void* rgba16f_bytes, int byte_count)
{
    PsvApiScope api("psv_tile_upload");
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !rgba16f_bytes) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    /* Cheap GetDeviceRemovedReason before any Diligent call — never UpdateTexture on TDR. */
    if (PollDeviceRemoved(host)) {
        SetError(host, "device lost");
        return PSV_DEVICE_LOST;
    }
    if (!host->tilePoolReady || !host->tileArray) {
        SetError(host, "tile pool not created");
        return PSV_NOT_ATTACHED;
    }
    if (slice >= host->tileCapacity) {
        SetError(host, "tile slice out of range");
        return PSV_BAD_ARGUMENT;
    }
    const int expected = static_cast<int>(kSlot * kSlot * 8);
    if (byte_count != expected) {
        SetError(host, "tile upload size must be 512*512*8");
        return PSV_BAD_ARGUMENT;
    }

    TextureSubResData sub{};
    sub.pData = rgba16f_bytes;
    sub.Stride = kSlot * 8;
    Box box{};
    box.MinX = 0;
    box.MaxX = kSlot;
    box.MinY = 0;
    box.MaxY = kSlot;
    box.MinZ = 0;
    box.MaxZ = 1;
    host->diligentContext->UpdateTexture(
        host->tileArray,
        0,
        slice,
        box,
        sub,
        RESOURCE_STATE_TRANSITION_MODE_TRANSITION,
        RESOURCE_STATE_TRANSITION_MODE_TRANSITION);
    ClearError(host);
    return PSV_OK;
#else
    (void)slice;
    (void)byte_count;
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_tile_readback(void* host_ptr, unsigned slice, void* out_rgba16f_bytes, int byte_count)
{
    PsvApiScope api("psv_tile_readback");
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !out_rgba16f_bytes) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (PollDeviceRemoved(host)) {
        SetError(host, "device lost");
        return PSV_DEVICE_LOST;
    }
    if (!host->tilePoolReady || !host->tileArray || !host->diligentContext) {
        SetError(host, "tile pool not created");
        return PSV_NOT_ATTACHED;
    }
    if (slice >= host->tileCapacity) {
        SetError(host, "tile slice out of range");
        return PSV_BAD_ARGUMENT;
    }
    const int expected = static_cast<int>(kSlot * kSlot * 8);
    if (byte_count != expected) {
        SetError(host, "tile readback size must be 512*512*8");
        return PSV_BAD_ARGUMENT;
    }

    if (!host->readbackStaging) {
        TextureDesc sd{};
        sd.Name = "SmoothTileReadback";
        sd.Type = RESOURCE_DIM_TEX_2D_ARRAY;
        sd.Width = kSlot;
        sd.Height = kSlot;
        sd.ArraySize = 1;
        sd.Format = TEX_FORMAT_RGBA16_FLOAT;
        sd.MipLevels = 1;
        sd.Usage = USAGE_STAGING;
        sd.BindFlags = BIND_NONE;
        sd.CPUAccessFlags = CPU_ACCESS_READ;
        host->diligentDevice->CreateTexture(sd, nullptr, &host->readbackStaging);
        if (!host->readbackStaging) {
            SetError(host, "CreateTexture readback staging failed");
            return PSV_FAILED;
        }
    }

    CopyTextureAttribs copy{};
    copy.pSrcTexture = host->tileArray;
    copy.SrcSlice = slice;
    copy.pDstTexture = host->readbackStaging;
    copy.DstSlice = 0;
    copy.SrcTextureTransitionMode = RESOURCE_STATE_TRANSITION_MODE_TRANSITION;
    copy.DstTextureTransitionMode = RESOURCE_STATE_TRANSITION_MODE_TRANSITION;
    host->diligentContext->CopyTexture(copy);
    host->diligentContext->WaitForIdle();

    MappedTextureSubresource mapped{};
    host->diligentContext->MapTextureSubresource(
        host->readbackStaging,
        0,
        0,
        MAP_READ,
        MAP_FLAG_NONE,
        nullptr,
        mapped);
    if (!mapped.pData) {
        SetError(host, "MapTextureSubresource readback failed");
        return PSV_FAILED;
    }

    auto* dst = static_cast<uint8_t*>(out_rgba16f_bytes);
    const auto* src = static_cast<const uint8_t*>(mapped.pData);
    const Uint64 srcStride = mapped.Stride != 0 ? mapped.Stride : static_cast<Uint64>(kSlot * 8);
    for (unsigned y = 0; y < kSlot; ++y) {
        std::memcpy(dst + static_cast<size_t>(y) * kSlot * 8, src + y * srcStride, kSlot * 8);
    }
    host->diligentContext->UnmapTextureSubresource(host->readbackStaging, 0, 0);
    ClearError(host);
    return PSV_OK;
#else
    (void)slice;
    (void)byte_count;
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_tile_mip_box(
    void* host_ptr,
    unsigned level_l_w,
    unsigned level_l_h,
    int parent_content_origin_l1_x,
    int parent_content_origin_l1_y,
    unsigned dst_slice,
    const unsigned* slices4x4)
{
    PsvApiScope api("psv_tile_mip_box");
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !slices4x4) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (host->deviceLost) {
        SetError(host, "device lost");
        return PSV_DEVICE_LOST;
    }
    if (!host->tilePoolReady || !host->mipReady || !host->mipPso || !host->mipSrb || !host->mipCb) {
        SetError(host, "mip pipeline not ready");
        return PSV_FAILED;
    }
    if (dst_slice >= host->tileCapacity) {
        return PSV_BAD_ARGUMENT;
    }

    SmoothMipCbCpu cb{};
    cb.params[0] = kSlot;
    cb.params[1] = kBorder;
    cb.params[2] = 0; /* box */
    cb.params[3] = 0; /* StraightAlpha=0 premultiplied */
    cb.levelInfo[0] = level_l_w;
    cb.levelInfo[1] = level_l_h;
    cb.levelInfo[2] = 0; /* scratch DstSlice */
    cb.levelInfo[3] = 1; /* AntiRing ON */
    cb.origin[0] = parent_content_origin_l1_x * 2;
    cb.origin[1] = parent_content_origin_l1_y * 2;
    cb.origin[2] = 0;
    cb.origin[3] = 0;
    std::memcpy(cb.slices, slices4x4, sizeof(cb.slices));

    void* mapped = nullptr;
    host->diligentContext->MapBuffer(host->mipCb, MAP_WRITE, MAP_FLAG_DISCARD, mapped);
    if (!mapped) {
        SetError(host, "MapBuffer mip CB failed");
        return PSV_FAILED;
    }
    std::memcpy(mapped, &cb, sizeof(cb));
    host->diligentContext->UnmapBuffer(host->mipCb, MAP_WRITE);

    auto* srcSrv = host->tileArray->GetDefaultView(TEXTURE_VIEW_SHADER_RESOURCE);
    auto* dstUav = host->mipScratch->GetDefaultView(TEXTURE_VIEW_UNORDERED_ACCESS);
    if (!srcSrv || !dstUav) {
        SetError(host, "tile/mip views missing");
        return PSV_FAILED;
    }

    auto* vSrc = host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "Src");
    auto* vDst = host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "Dst");
    auto* vCb = host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "SmoothMipCB");
    if (!vSrc || !vDst || !vCb) {
        SetError(host, "mip SRB missing Src/Dst/SmoothMipCB (need MUTABLE layout)");
        return PSV_FAILED;
    }
    /* Rebind same views each call — no Set(nullptr) on MUTABLE vars. */
    vSrc->Set(srcSrv);
    vDst->Set(dstUav);
    vCb->Set(host->mipCb);

    host->diligentContext->SetPipelineState(host->mipPso);
    host->diligentContext->CommitShaderResources(host->mipSrb, RESOURCE_STATE_TRANSITION_MODE_TRANSITION);
    DispatchComputeAttribs disp{ (kSlot + 7) / 8, (kSlot + 7) / 8, 1 };
    host->diligentContext->DispatchCompute(disp);
    /* No Flush here — batch per level; caller / present path submits. */

    CopyTextureAttribs copy{};
    copy.pSrcTexture = host->mipScratch;
    copy.SrcSlice = 0;
    copy.pDstTexture = host->tileArray;
    copy.DstSlice = dst_slice;
    copy.SrcTextureTransitionMode = RESOURCE_STATE_TRANSITION_MODE_TRANSITION;
    copy.DstTextureTransitionMode = RESOURCE_STATE_TRANSITION_MODE_TRANSITION;
    host->diligentContext->CopyTexture(copy);

    ClearError(host);
    return PSV_OK;
#else
    (void)level_l_w;
    (void)level_l_h;
    (void)parent_content_origin_l1_x;
    (void)parent_content_origin_l1_y;
    (void)dst_slice;
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_tile_mip_ready(void* host_ptr)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return 0;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    return (!host->deviceLost && host->mipReady) ? 1 : 0;
#else
    return 0;
#endif
}

PSV_API int psv_flush(void* host_ptr)
{
    PsvApiScope api("psv_flush");
    auto* host = static_cast<Host*>(host_ptr);
    if (!host) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (PollDeviceRemoved(host)) {
        return PSV_DEVICE_LOST;
    }
    if (host->diligentContext) {
        host->diligentContext->Flush();
        /* Pyramid mip batches may Flush without a present — recycle here too. */
        PsvFinishDiligentFrame(host);
        PsvDrainD3D12InfoQueue(host);
    }
    return PSV_OK;
#else
    return PSV_MISSING_DILIGENT;
#endif
}

} // extern "C"
