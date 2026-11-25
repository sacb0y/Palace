/*
 * Diligent + D3D12 debug logging for Smooth Viewer automation.
 * Logs under PALACE_DATA_DIR (psv_set_debug_log_path). SetBreakOnError(false)
 * when automation env / log path is set — no MessageBox pump.
 */

#include "palace_smooth_internal.hpp"

#include <atomic>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <mutex>

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
#  include "DebugOutput.h"
#endif

#if defined(PSV_TEST_HOOKS)
#  include <d3d12sdklayers.h>
#endif

namespace {

std::mutex g_logMutex;
char g_logPath[MAX_PATH] = {};
std::atomic<bool> g_logReady{false};
thread_local const char* g_psvApiTag = "";

#if defined(PSV_TEST_HOOKS)
DWORD g_infoQueueCookie = 0;
bool g_infoQueueRegistered = false;
#endif

const char* SeverityName(int severity)
{
    switch (severity) {
    case 0: return "INFO";
    case 1: return "WARN";
    case 2: return "ERROR";
    case 3: return "FATAL";
    default: return "MSG";
    }
}

void AppendDebugLine(const char* line)
{
    if (!g_logReady.load(std::memory_order_acquire) || !g_logPath[0] || !line || !line[0]) {
        return;
    }
    std::lock_guard<std::mutex> lock(g_logMutex);
    FILE* f = nullptr;
    if (fopen_s(&f, g_logPath, "a") != 0 || !f) {
        return;
    }
    std::fputs(line, f);
    if (line[std::strlen(line) - 1] != '\n') {
        std::fputc('\n', f);
    }
    std::fclose(f);
}

void AppendFormatted(
    const char* source,
    const char* severity,
    const char* message,
    const char* function,
    const char* file,
    int line)
{
    char buf[2048];
    const char* api = g_psvApiTag && g_psvApiTag[0] ? g_psvApiTag : "-";
    std::snprintf(
        buf,
        sizeof(buf),
        "smooth-debug source=%s severity=%s psv=%s msg=%s func=%s file=%s line=%d",
        source ? source : "?",
        severity ? severity : "?",
        api,
        message ? message : "",
        function ? function : "",
        file ? file : "",
        line);
    AppendDebugLine(buf);
}

#if defined(PALACE_SMOOTH_WITH_DILIGENT)
void DiligentMessageCallback(
    DEBUG_MESSAGE_SEVERITY severity,
    const Char* message,
    const Char* function,
    const Char* file,
    int line)
{
    AppendFormatted(
        "diligent",
        SeverityName(static_cast<int>(severity)),
        message,
        function,
        file,
        line);
    /* Also mirror to OutputDebugString for local debugging. */
    OutputDebugStringA(message ? message : "");
    OutputDebugStringA("\n");
}
#endif

#if defined(PSV_TEST_HOOKS)
void CALLBACK InfoQueueMessageCallback(
    D3D12_MESSAGE_CATEGORY category,
    D3D12_MESSAGE_SEVERITY severity,
    D3D12_MESSAGE_ID id,
    LPCSTR description,
    void* /*context*/)
{
    (void)category;
    (void)id;
    const char* sev = "MSG";
    switch (severity) {
    case D3D12_MESSAGE_SEVERITY_CORRUPTION: sev = "CORRUPTION"; break;
    case D3D12_MESSAGE_SEVERITY_ERROR: sev = "ERROR"; break;
    case D3D12_MESSAGE_SEVERITY_WARNING: sev = "WARN"; break;
    case D3D12_MESSAGE_SEVERITY_INFO: sev = "INFO"; break;
    case D3D12_MESSAGE_SEVERITY_MESSAGE: sev = "MSG"; break;
    }
    AppendFormatted("d3d12-infoqueue", sev, description, nullptr, nullptr, 0);
}
#endif

bool AutomationNoAssertUi()
{
    char buf[8] = {};
    const DWORD n = GetEnvironmentVariableA("PALACE_SMOOTH_NO_ASSERT_UI", buf, sizeof(buf));
    return n > 0 && buf[0] == '1';
}

} // namespace

void PsvSetApiTag(const char* tag)
{
    g_psvApiTag = tag ? tag : "";
}

const char* PsvGetApiTag()
{
    return g_psvApiTag && g_psvApiTag[0] ? g_psvApiTag : "";
}

void PsvInstallDiligentDebugLogging()
{
#if defined(PALACE_SMOOTH_WITH_DILIGENT)
    SetDebugMessageCallback(DiligentMessageCallback);
    /* No MessageBox when automation env is set OR a log path is configured.
     * Win32 AssertionFailed only shows MessageBox when GetBreakOnError() is true. */
    if (AutomationNoAssertUi() || g_logReady.load(std::memory_order_acquire)) {
        if (auto* f12 = GetEngineFactoryD3D12()) {
            f12->SetBreakOnError(false);
        }
        if (auto* f11 = GetEngineFactoryD3D11()) {
            f11->SetBreakOnError(false);
        }
    }
#endif
}

void PsvAppendDebugNote(const char* note)
{
    AppendFormatted("palace", "NOTE", note, nullptr, nullptr, 0);
}

#if defined(PSV_TEST_HOOKS)
void PsvSetupD3D12InfoQueue(Host* host)
{
    if (!host || !host->d3d12Device) {
        return;
    }
    ComPtr<ID3D12InfoQueue> iq;
    if (FAILED(host->d3d12Device.As(&iq)) || !iq) {
        return;
    }
    iq->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, FALSE);
    iq->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, FALSE);
    iq->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_WARNING, FALSE);
    iq->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_INFO, FALSE);
    iq->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_MESSAGE, FALSE);

    ComPtr<ID3D12InfoQueue1> iq1;
    if (!g_infoQueueRegistered && SUCCEEDED(iq.As(&iq1)) && iq1) {
        DWORD cookie = 0;
        if (SUCCEEDED(iq1->RegisterMessageCallback(
                InfoQueueMessageCallback,
                D3D12_MESSAGE_CALLBACK_FLAG_NONE,
                host,
                &cookie))) {
            g_infoQueueCookie = cookie;
            g_infoQueueRegistered = true;
            host->infoQueueCookie = cookie;
        }
    }
}

void PsvDrainD3D12InfoQueue(Host* host)
{
    if (!host || !host->d3d12Device) {
        return;
    }
    ComPtr<ID3D12InfoQueue> iq;
    if (FAILED(host->d3d12Device.As(&iq)) || !iq) {
        return;
    }
    const UINT64 count = iq->GetNumStoredMessages();
    for (UINT64 i = 0; i < count; ++i) {
        SIZE_T len = 0;
        if (FAILED(iq->GetMessage(i, nullptr, &len)) || len == 0) {
            continue;
        }
        auto* raw = static_cast<uint8_t*>(std::malloc(len));
        if (!raw) {
            continue;
        }
        auto* msg = reinterpret_cast<D3D12_MESSAGE*>(raw);
        if (SUCCEEDED(iq->GetMessage(i, msg, &len)) && msg->pDescription) {
            const char* sev = "MSG";
            switch (msg->Severity) {
            case D3D12_MESSAGE_SEVERITY_CORRUPTION: sev = "CORRUPTION"; break;
            case D3D12_MESSAGE_SEVERITY_ERROR: sev = "ERROR"; break;
            case D3D12_MESSAGE_SEVERITY_WARNING: sev = "WARN"; break;
            case D3D12_MESSAGE_SEVERITY_INFO: sev = "INFO"; break;
            default: break;
            }
            AppendFormatted("d3d12-drain", sev, msg->pDescription, nullptr, nullptr, 0);
        }
        std::free(raw);
    }
    iq->ClearStoredMessages();
}
#else
void PsvSetupD3D12InfoQueue(Host* host)
{
    (void)host;
}

void PsvDrainD3D12InfoQueue(Host* host)
{
    (void)host;
}
#endif

extern "C" {

PSV_API int psv_set_debug_log_path(const char* utf8_path)
{
    if (!utf8_path || !utf8_path[0]) {
        g_logReady.store(false, std::memory_order_release);
        g_logPath[0] = '\0';
        return PSV_BAD_ARGUMENT;
    }
    std::snprintf(g_logPath, sizeof(g_logPath), "%s", utf8_path);
    g_logReady.store(true, std::memory_order_release);
    PsvInstallDiligentDebugLogging();
    AppendFormatted("palace", "NOTE", "debug log path set", nullptr, nullptr, 0);
    return PSV_OK;
}

} // extern "C"
