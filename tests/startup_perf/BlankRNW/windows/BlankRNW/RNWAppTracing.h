#pragma once

#include <TraceLoggingProvider.h>
#include <winmeta.h>

// ETW Provider: BenchmarkSyntheticApps {FD80D616-E92B-4B2B-9BED-131ADA36A8FD}
//
// Same provider as ../../../Common/Tracing.h and -lift's RNWAppTracing.h
// — single source of truth for the GUID. We define it locally (rather than
// reaching into Common/Tracing.cpp) so the RNW C++ host is self-contained.
TRACELOGGING_DEFINE_PROVIDER(
    g_hRNWAppProvider,
    "BenchmarkSyntheticApps",
    (0xfd80d616, 0xe92b, 0x4b2b, 0x9b, 0xed, 0x13, 0x1a, 0xda, 0x36, 0xa8, 0xfd),
    TraceLoggingOptionGroup(0x4f50731a, 0x89cf, 0x4782, 0xb3, 0xe0, 0xdc, 0xe8, 0xc9, 0x04, 0x76, 0xba));

inline int64_t QpcNowUs() noexcept {
  static const int64_t freq = [] {
    LARGE_INTEGER f;
    QueryPerformanceFrequency(&f);
    return f.QuadPart;
  }();
  LARGE_INTEGER now;
  QueryPerformanceCounter(&now);
  return (now.QuadPart * 1'000'000) / freq;
}

inline int64_t g_qpcWinMainUs = 0;
inline int64_t g_qpcAppBuiltUs = 0;
inline int64_t g_qpcBeforeStartUs = 0;

// AppName payload — must match what -lift emits if you want the Regions
// XML to resolve our trace under the same instance name.
inline constexpr char RNWAPP_APP_NAME[] = "blank_rnw";

inline void RNWAppTracingRegister() noexcept { TraceLoggingRegister(g_hRNWAppProvider); }
inline void RNWAppTracingUnregister() noexcept { TraceLoggingUnregister(g_hRNWAppProvider); }

inline void CaptureWinMainEntry() noexcept { g_qpcWinMainUs = QpcNowUs(); }
inline void CaptureAppBuilt() noexcept { g_qpcAppBuiltUs = QpcNowUs(); }
inline void CaptureBeforeStart() noexcept { g_qpcBeforeStartUs = QpcNowUs(); }

constexpr int64_t RNWAPP_KEYWORD_MEASURES = 0x0000400000000000;

inline void TraceWinMainEntry() noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "wWinMainEntry",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"));
}

inline void TraceTTFP(double ms) noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "FirstRender",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"),
      TraceLoggingFloat64(ms, "TtfpMs"));
}

inline void TraceTTI(double ms) noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "FirstIdle",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"),
      TraceLoggingFloat64(ms, "TtiMs"));
}

inline void TraceXamlAppLoaded() noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "XamlAppLoaded",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"));
}

inline void TraceWindowLoaded() noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "WindowLoaded",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"));
}

inline void TraceJSBundleLoaded() noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "JSBundleLoaded",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"));
}

inline void TraceReactMounted() noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "ReactMounted",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"));
}

inline void TraceProcessStop() noexcept {
  TraceLoggingWrite(g_hRNWAppProvider, "ProcessStop",
      TraceLoggingLevel(WINEVENT_LEVEL_INFO), TraceLoggingKeyword(RNWAPP_KEYWORD_MEASURES),
      TraceLoggingString(RNWAPP_APP_NAME, "AppName"));
}
