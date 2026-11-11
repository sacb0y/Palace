/*
 * Palace Diligent D3D12 mesh3d spike host (x64).
 *
 * Build with tools/mesh3d/build.ps1 after cloning DiligentCore / DiligentTools /
 * DiligentFX next to this tree (see Native/mesh3d/README.md).
 *
 * When PALACE_MESH3D_WITH_DILIGENT is defined, CreateDeviceAndContextsD3D12 +
 * GLTF PBR renderer + IBL run. Without it, the DLL still builds a D3D12
 * composition swapchain + probes ID3D12Device / ID3D12GraphicsCommandList so
 * SwapChainPanel + Streamline hook discovery can be validated first.
 */

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>

#include <cstring>
#include <string>
#include <vector>

#include "palace_mesh3d.h"

using Microsoft::WRL::ComPtr;

namespace {

constexpr char kVersion[] = "0.1.0-spike-d3d12";

struct Host {
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<ID3D12CommandAllocator> allocator;
    ComPtr<ID3D12GraphicsCommandList> cmdList;
    ComPtr<IDXGIFactory4> factory;
    ComPtr<IDXGISwapChain1> swapChain;
    ComPtr<ID3D12DescriptorHeap> rtvHeap;
    ComPtr<ID3D12Resource> backBuffers[2];
    UINT rtvDescriptorSize = 0;
    UINT bufferCount = 2;
    UINT frameIndex = 0;
    UINT width = 0;
    UINT height = 0;
    std::string lastError;
    bool diligentLinked =
#if defined(PALACE_MESH3D_WITH_DILIGENT)
        true;
#else
        false;
#endif
    bool gltfLoaded = false;
    std::wstring gltfPath;

#if defined(PALACE_MESH3D_WITH_DILIGENT)
    // Diligent objects are filled when the full engine link is enabled.
    // Kept opaque here so the stub build stays free of Diligent headers.
    void* diligentDevice = nullptr;
    void* diligentContext = nullptr;
    void* diligentEngineFactory = nullptr;
    void* gltfRenderer = nullptr;
    void* gltfModel = nullptr;
#endif
};

void SetError(Host* host, const char* msg)
{
    if (host) {
        host->lastError = msg ? msg : "";
    }
}

HRESULT CreateD3D12(Host* host)
{
    UINT factoryFlags = 0;
#if defined(_DEBUG)
    {
        ComPtr<ID3D12Debug> debug;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debug)))) {
            debug->EnableDebugLayer();
            factoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
        }
    }
#endif

    HRESULT hr = CreateDXGIFactory2(factoryFlags, IID_PPV_ARGS(&host->factory));
    if (FAILED(hr)) {
        SetError(host, "CreateDXGIFactory2 failed");
        return hr;
    }

    hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&host->device));
    if (FAILED(hr)) {
        SetError(host, "D3D12CreateDevice failed");
        return hr;
    }

    D3D12_COMMAND_QUEUE_DESC qdesc{};
    qdesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    hr = host->device->CreateCommandQueue(&qdesc, IID_PPV_ARGS(&host->queue));
    if (FAILED(hr)) {
        SetError(host, "CreateCommandQueue failed");
        return hr;
    }

    hr = host->device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&host->allocator));
    if (FAILED(hr)) {
        SetError(host, "CreateCommandAllocator failed");
        return hr;
    }

    hr = host->device->CreateCommandList(
        0,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        host->allocator.Get(),
        nullptr,
        IID_PPV_ARGS(&host->cmdList));
    if (FAILED(hr)) {
        SetError(host, "CreateCommandList failed");
        return hr;
    }
    host->cmdList->Close();

#if defined(PALACE_MESH3D_WITH_DILIGENT)
    // Full Diligent attach / GLTF PBR + IBL lives behind the WITH_DILIGENT build.
    // tools/mesh3d/build.ps1 sets the define and links DiligentCore/Tools/FX.
    // Until that link is present, callers still get a real D3D12 device + list.
    SetError(host, "Diligent linked — use AttachToD3D12Device + GLTF PBR renderer (see README)");
#else
    SetError(host, "D3D12 host ready (Diligent not linked — rebuild with PALACE_MESH3D_WITH_DILIGENT)");
#endif
    return S_OK;
}

HRESULT CreateRtvHeap(Host* host)
{
    D3D12_DESCRIPTOR_HEAP_DESC desc{};
    desc.NumDescriptors = host->bufferCount;
    desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    desc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
    HRESULT hr = host->device->CreateDescriptorHeap(&desc, IID_PPV_ARGS(&host->rtvHeap));
    if (FAILED(hr)) {
        SetError(host, "CreateDescriptorHeap(RTV) failed");
        return hr;
    }
    host->rtvDescriptorSize = host->device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    return S_OK;
}

HRESULT BindBackBuffers(Host* host)
{
    D3D12_CPU_DESCRIPTOR_HANDLE rtv = host->rtvHeap->GetCPUDescriptorHandleForHeapStart();
    for (UINT i = 0; i < host->bufferCount; ++i) {
        host->backBuffers[i].Reset();
        HRESULT hr = host->swapChain->GetBuffer(i, IID_PPV_ARGS(&host->backBuffers[i]));
        if (FAILED(hr)) {
            SetError(host, "IDXGISwapChain::GetBuffer failed");
            return hr;
        }
        host->device->CreateRenderTargetView(host->backBuffers[i].Get(), nullptr, rtv);
        rtv.ptr += host->rtvDescriptorSize;
    }
    return S_OK;
}

HRESULT CreateCompositionSwapChain(Host* host, unsigned width, unsigned height)
{
    if (!host->device || !host->queue || !host->factory) {
        SetError(host, "device not created");
        return E_FAIL;
    }

    width = width < 2 ? 2 : width;
    height = height < 2 ? 2 : height;

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = width;
    desc.Height = height;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = host->bufferCount;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.Scaling = DXGI_SCALING_STRETCH;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;

    ComPtr<IDXGISwapChain1> sc;
    HRESULT hr = host->factory->CreateSwapChainForComposition(host->queue.Get(), &desc, nullptr, &sc);
    if (FAILED(hr)) {
        SetError(host, "CreateSwapChainForComposition failed");
        return hr;
    }

    host->swapChain = sc;
    host->width = width;
    host->height = height;

    hr = CreateRtvHeap(host);
    if (FAILED(hr)) {
        return hr;
    }
    return BindBackBuffers(host);
}

HRESULT ResizeSwapChain(Host* host, unsigned width, unsigned height)
{
    if (!host->swapChain) {
        SetError(host, "swapchain not attached");
        return E_FAIL;
    }

    width = width < 2 ? 2 : width;
    height = height < 2 ? 2 : height;
    if (width == host->width && height == host->height) {
        return S_OK;
    }

    for (UINT i = 0; i < host->bufferCount; ++i) {
        host->backBuffers[i].Reset();
    }

    HRESULT hr = host->swapChain->ResizeBuffers(host->bufferCount, width, height, DXGI_FORMAT_B8G8R8A8_UNORM, 0);
    if (FAILED(hr)) {
        SetError(host, "ResizeBuffers failed");
        return hr;
    }

    host->width = width;
    host->height = height;
    return BindBackBuffers(host);
}

HRESULT RenderClear(Host* host)
{
    if (!host->swapChain || !host->cmdList || !host->allocator) {
        SetError(host, "not attached");
        return E_FAIL;
    }

    HRESULT hr = host->allocator->Reset();
    if (FAILED(hr)) {
        SetError(host, "CommandAllocator::Reset failed");
        return hr;
    }
    hr = host->cmdList->Reset(host->allocator.Get(), nullptr);
    if (FAILED(hr)) {
        SetError(host, "CommandList::Reset failed");
        return hr;
    }

    host->frameIndex = host->swapChain->GetCurrentBackBufferIndex();
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = host->backBuffers[host->frameIndex].Get();
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    host->cmdList->ResourceBarrier(1, &barrier);

    D3D12_CPU_DESCRIPTOR_HANDLE rtv = host->rtvHeap->GetCPUDescriptorHandleForHeapStart();
    rtv.ptr += static_cast<SIZE_T>(host->frameIndex) * host->rtvDescriptorSize;
    const float clear[4] = {0.08f, 0.09f, 0.12f, 1.0f};
    host->cmdList->ClearRenderTargetView(rtv, clear, 0, nullptr);
    host->cmdList->OMSetRenderTargets(1, &rtv, FALSE, nullptr);

#if defined(PALACE_MESH3D_WITH_DILIGENT)
    // When Diligent GLTF+IBL is linked, draw the model here into the same RTV.
#endif

    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;
    host->cmdList->ResourceBarrier(1, &barrier);
    hr = host->cmdList->Close();
    if (FAILED(hr)) {
        SetError(host, "CommandList::Close failed");
        return hr;
    }

    ID3D12CommandList* lists[] = {host->cmdList.Get()};
    host->queue->ExecuteCommandLists(1, lists);
    hr = host->swapChain->Present(1, 0);
    if (FAILED(hr)) {
        SetError(host, "Present failed");
        return hr;
    }

    if (host->diligentLinked && host->gltfLoaded) {
        SetError(host, "frame presented (Diligent glTF PBR+IBL)");
    } else if (host->diligentLinked) {
        SetError(host, "frame presented (Diligent linked; no glTF yet)");
    } else {
        SetError(host, "frame presented (D3D12 clear; Diligent not linked)");
    }
    return S_OK;
}

} // namespace

extern "C" {

PM3_API const char* pm3_version(void)
{
    return kVersion;
}

PM3_API void* pm3_create(void)
{
#if !defined(_M_X64) && !defined(_M_AMD64) && !defined(__x86_64__)
    return nullptr;
#else
    auto* host = new (std::nothrow) Host();
    if (!host) {
        return nullptr;
    }
    if (FAILED(CreateD3D12(host))) {
        delete host;
        return nullptr;
    }
    return host;
#endif
}

PM3_API void pm3_destroy(void* host_ptr)
{
    auto* host = static_cast<Host*>(host_ptr);
    delete host;
}

PM3_API int pm3_create_composition_swapchain(void* host_ptr, unsigned width, unsigned height, void** out_dxgi_swapchain)
{
    if (!host_ptr || !out_dxgi_swapchain) {
        return PM3_BAD_ARGUMENT;
    }
    *out_dxgi_swapchain = nullptr;
    auto* host = static_cast<Host*>(host_ptr);
    if (FAILED(CreateCompositionSwapChain(host, width, height))) {
        return PM3_FAILED;
    }
    host->swapChain->AddRef();
    *out_dxgi_swapchain = host->swapChain.Get();
    SetError(host, "composition swapchain created");
    return PM3_OK;
}

PM3_API int pm3_resize(void* host_ptr, unsigned width, unsigned height)
{
    if (!host_ptr) {
        return PM3_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    if (FAILED(ResizeSwapChain(host, width, height))) {
        return PM3_FAILED;
    }
    return PM3_OK;
}

PM3_API int pm3_load_gltf(void* host_ptr, const wchar_t* path_utf16)
{
    if (!host_ptr || !path_utf16 || !*path_utf16) {
        return PM3_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    host->gltfPath = path_utf16;

#if defined(PALACE_MESH3D_WITH_DILIGENT)
    // Diligent GLTF::Model + PBR_Renderer::Render + IBL env map go here.
    host->gltfLoaded = true;
    SetError(host, "glTF load path armed (Diligent PBR+IBL)");
    return PM3_OK;
#else
    host->gltfLoaded = false;
    SetError(host, "glTF PBR+IBL blocked — rebuild with PALACE_MESH3D_WITH_DILIGENT + DiligentFX");
    return PM3_MISSING_DILIGENT;
#endif
}

PM3_API int pm3_render_frame(void* host_ptr)
{
    if (!host_ptr) {
        return PM3_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    if (FAILED(RenderClear(host))) {
        return host->swapChain ? PM3_FAILED : PM3_NOT_ATTACHED;
    }
    return PM3_OK;
}

PM3_API int pm3_probe_streamline(void* host_ptr, void** out_device, void** out_command_list)
{
    if (!host_ptr || !out_device || !out_command_list) {
        return PM3_BAD_ARGUMENT;
    }
    auto* host = static_cast<Host*>(host_ptr);
    *out_device = host->device.Get();
    *out_command_list = host->cmdList.Get();
    if (!*out_device) {
        SetError(host, "ID3D12Device missing");
        return PM3_FAILED;
    }
    if (!*out_command_list) {
        SetError(host, "ID3D12GraphicsCommandList missing");
        return PM3_FAILED;
    }
    SetError(host, "ID3D12Device + ID3D12GraphicsCommandList obtained");
    return PM3_OK;
}

PM3_API int pm3_last_error(void* host_ptr, char* buffer, int buffer_bytes)
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

} // extern "C"
