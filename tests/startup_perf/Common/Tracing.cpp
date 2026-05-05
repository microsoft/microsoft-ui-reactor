#include <windows.h>
#include <atomic>
#include <cassert>
#include <cstring>
#include <TraceLoggingProvider.h>
#include "Tracing.h"
#include <evntrace.h>

#define EVENT_NAME_XB_XAML_APP_LOADED "XamlAppLoaded"
#define EVENT_NAME_XB_WINDOW_LOADED "WindowLoaded"
#define EVENT_NAME_XB_JS_BUNDLE_LOADED "JSBundleLoaded"
#define EVENT_NAME_XB_REACT_MOUNTED "ReactMounted"
#define EVENT_NAME_XB_FIRST_RENDER "FirstRender"
#define EVENT_NAME_XB_FIRST_IDLE "FirstIdle"
#define EVENT_NAME_XB_PROCESS_STOP "ProcessStop"

constexpr int64_t MICROSOFT_KEYWORD_MEASURES = 0x0000400000000000; // Bit 46

static std::atomic_uint64_t s_sequenceNum{ 0 };
static bool s_registered = false;
static char s_appName[64] = "Unknown";

// Same provider as microsoft-ui-xaml-lift/.../Common/Tracing.cpp.
// Do not change the GUID — calibration depends on it.
TRACELOGGING_DEFINE_PROVIDER(g_BenchmarkTraceProvider,
    "BenchmarkSyntheticApps",
    // {FD80D616-E92B-4B2B-9BED-131ADA36A8FD}
    (0xfd80d616, 0xe92b, 0x4b2b, 0x9b, 0xed, 0x13, 0x1a, 0xda, 0x36, 0xa8, 0xfd),
    TraceLoggingOptionGroup(0x4f50731a, 0x89cf, 0x4782, 0xb3, 0xe0, 0xdc, 0xe8, 0xc9, 0x04, 0x76, 0xba)
);

#define XBTraceInfo(eventName, ...)                                                                       \
    TraceLoggingWrite(g_BenchmarkTraceProvider, eventName, TraceLoggingLevel(TRACE_LEVEL_INFORMATION),    \
                      TraceLoggingKeyword(MICROSOFT_KEYWORD_MEASURES),                                    \
                      TraceLoggingPid(GetCurrentProcessId(), "Pid"),                                      \
                      TraceLoggingString(s_appName, "AppName"),                                           \
                      __VA_ARGS__);

namespace Tracing
{

HRESULT Register()
{
    HRESULT hr = TraceLoggingRegister(g_BenchmarkTraceProvider);
    if (SUCCEEDED(hr))
        s_registered = true;
    return hr;
}

void SetAppName(const char* appName)
{
    if (appName)
        strncpy_s(s_appName, appName, _TRUNCATE);
}

void Unregister()
{
    TraceLoggingUnregister(g_BenchmarkTraceProvider);
    s_registered = false;
}

void TraceWinMainEntry()
{
    assert(s_registered && "Tracing::Register() must be called before emitting events");
    XBTraceInfo("wWinMainEntry",
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceXamlAppLoaded()
{
    XBTraceInfo(EVENT_NAME_XB_XAML_APP_LOADED,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceWindowLoaded()
{
    XBTraceInfo(EVENT_NAME_XB_WINDOW_LOADED,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceJSBundleLoaded()
{
    XBTraceInfo(EVENT_NAME_XB_JS_BUNDLE_LOADED,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceReactMounted()
{
    XBTraceInfo(EVENT_NAME_XB_REACT_MOUNTED,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceFirstRender()
{
    XBTraceInfo(EVENT_NAME_XB_FIRST_RENDER,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceFirstIdle()
{
    XBTraceInfo(EVENT_NAME_XB_FIRST_IDLE,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceMessagePumpIdle()
{
    XBTraceInfo("MessagePumpIdle",
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

void TraceProcessStop()
{
    XBTraceInfo(EVENT_NAME_XB_PROCESS_STOP,
        TraceLoggingUInt64(s_sequenceNum.fetch_add(1, std::memory_order_relaxed), "Seq"));
}

} // namespace Tracing
