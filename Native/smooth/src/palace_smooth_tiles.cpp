/*
 * Smooth Viewer V1 — GPU tile pool + SmoothMipDownsampleCS (Geo).
 * Working RGBA16F, Border=2, SMOOTH_SRGB8=0, scratch→CopyTexture.
 */

#include "palace_smooth_internal.hpp"

#include <cstdio>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#if defined(PALACE_SMOOTH_WITH_DILIGENT)

namespace {

constexpr unsigned kSlot = 512;
constexpr unsigned kBorder = 2;

/* Avoid Diligent GraphicsTools DefaultShaderSourceStreamFactory (not on our link set).
 * SmoothMipDownsample.hlsl has no #includes — load bytes and compile as Source. */
bool TryLoadShaderFile(Host* host, const char* fileName, std::string& out)
{
    out.clear();
    std::vector<std::string> dirs;
    const char* raw = host->shaderSearchDirs[0] ? host->shaderSearchDirs : ".";
    std::string cur;
    for (const char* p = raw;; ++p) {
        if (*p == ';' || *p == '\0') {
            if (!cur.empty()) {
                dirs.push_back(cur);
                cur.clear();
            }
            if (*p == '\0') {
                break;
            }
            continue;
        }
        cur.push_back(*p);
    }
    if (dirs.empty()) {
        dirs.emplace_back(".");
    }

    for (const auto& dir : dirs) {
        const std::string path = dir + "\\" + fileName;
        std::ifstream in(path, std::ios::binary);
        if (!in) {
            const std::string pathFwd = dir + "/" + fileName;
            in.open(pathFwd, std::ios::binary);
            if (!in) {
                continue;
            }
        }
        std::ostringstream ss;
        ss << in.rdbuf();
        out = ss.str();
        if (!out.empty()) {
            return true;
        }
    }
    SetError(host, "SmoothMipDownsample.hlsl not found in shader search dirs");
    return false;
}

bool CreateMipPipeline(Host* host)
{
    std::string hlsl;
    if (!TryLoadShaderFile(host, "SmoothMipDownsample.hlsl", hlsl)) {
        return false;
    }
    /* SMOOTH_SRGB8=0 for RGBA16F Working (Geo); define before source for FXC/Diligent. */
    const std::string source = std::string("#define SMOOTH_SRGB8 0\n") + hlsl;

    ShaderCreateInfo sci{};
    sci.SourceLanguage = SHADER_SOURCE_LANGUAGE_HLSL;
    sci.Desc.ShaderType = SHADER_TYPE_COMPUTE;
    sci.EntryPoint = "SmoothMipDownsampleCS";
    sci.Desc.Name = "SmoothMipDownsampleCS";
    sci.Source = source.c_str();
    sci.SourceLength = source.size();

    RefCntAutoPtr<IShader> cs;
    host->diligentDevice->CreateShader(sci, &cs);
    if (!cs) {
        SetError(host, "CreateShader SmoothMipDownsampleCS failed (cs_5_0)");
        return false;
    }

    ComputePipelineStateCreateInfo psoCI{};
    psoCI.PSODesc.Name = "SmoothMipDownsamplePSO";
    psoCI.PSODesc.PipelineType = PIPELINE_TYPE_COMPUTE;
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
    cbd.Size = sizeof(psv_downsample_cb) < 128 ? 256 : sizeof(uint32_t) * 24; /* SmoothMipCB ≈ Params+Level+Origin+Slices[4] */
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
    SetError(host, "SmoothMipDownsampleCS ready (SMOOTH_SRGB8=0)");
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
    if (!utf8_dirs) {
        host->shaderSearchDirs[0] = '\0';
    } else {
        std::snprintf(host->shaderSearchDirs, sizeof(host->shaderSearchDirs), "%s", utf8_dirs);
    }
    return PSV_OK;
#else
    (void)utf8_dirs;
    SetError(host, "Diligent required for shader dirs");
    return PSV_MISSING_DILIGENT;
#endif
}

PSV_API int psv_tile_pool_create(void* host_ptr, unsigned capacity)
{
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || capacity < 1) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
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
    td.BindFlags = BIND_SHADER_RESOURCE | BIND_UNORDERED_ACCESS;
    td.Usage = USAGE_DEFAULT;
    host->diligentDevice->CreateTexture(td, nullptr, &host->tileArray);
    if (!host->tileArray) {
        SetError(host, "CreateTexture tile array failed");
        return PSV_FAILED;
    }

    TextureDesc sd{};
    sd.Name = "SmoothMipScratch";
    sd.Type = RESOURCE_DIM_TEX_2D;
    sd.Width = kSlot;
    sd.Height = kSlot;
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
    char buf[96];
    std::snprintf(buf, sizeof(buf), "tile pool %u x 512² RGBA16F Working Border=%u", capacity, kBorder);
    SetError(host, buf);
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
    host->mipSrb.Release();
    host->mipPso.Release();
    host->mipCb.Release();
    host->mipScratch.Release();
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
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !rgba16f_bytes) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
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
    auto* host = static_cast<Host*>(host_ptr);
    if (!host || !slices4x4) {
        return PSV_BAD_ARGUMENT;
    }
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    if (!host->tilePoolReady || !host->mipReady || !host->mipPso) {
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

    host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "Src")->Set(srcSrv);
    host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "Dst")->Set(dstUav);
    if (auto* v = host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "SmoothMipCB")) {
        v->Set(host->mipCb);
    } else if (auto* v0 = host->mipSrb->GetVariableByName(SHADER_TYPE_COMPUTE, "Constants")) {
        v0->Set(host->mipCb);
    } else {
        /* cbuffer register(b0) — Diligent often exposes as buffer name from HLSL */
        host->mipSrb->GetVariableByIndex(SHADER_TYPE_COMPUTE, 0)->Set(host->mipCb);
    }

    host->diligentContext->SetPipelineState(host->mipPso);
    host->diligentContext->CommitShaderResources(host->mipSrb, RESOURCE_STATE_TRANSITION_MODE_TRANSITION);
    DispatchComputeAttribs disp{ (kSlot + 7) / 8, (kSlot + 7) / 8, 1 };
    host->diligentContext->DispatchCompute(disp);
    host->diligentContext->Flush();

    /* Never bind same array SRV+UAV: copy scratch → dst slice. */
    CopyTextureAttribs copy{};
    copy.pSrcTexture = host->mipScratch;
    copy.pDstTexture = host->tileArray;
    copy.DstSlice = dst_slice;
    copy.SrcTextureTransitionMode = RESOURCE_STATE_TRANSITION_MODE_TRANSITION;
    copy.DstTextureTransitionMode = RESOURCE_STATE_TRANSITION_MODE_TRANSITION;
    host->diligentContext->CopyTexture(copy);
    host->diligentContext->Flush();
    SetError(host, "mip box dispatch + CopyTexture ok");
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
    return host->mipReady ? 1 : 0;
#else
    return 0;
#endif
}

} // extern "C"
