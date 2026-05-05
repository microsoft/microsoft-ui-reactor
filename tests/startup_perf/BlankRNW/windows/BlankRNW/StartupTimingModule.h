#pragma once

#include "pch.h"
#include "NativeModules.h"
#include "RNWAppTracing.h"

// TurboModule that hands native QPC timestamps to JS and lets JS report
// the computed TRUE-TTFP / TRUE-TTI back so they fire as ETW events on
// the BenchmarkSyntheticApps provider (matching what -lift does).
REACT_MODULE(StartupTiming)
struct StartupTiming {
  REACT_SYNC_METHOD(getTimings)
  React::JSValueObject getTimings() noexcept {
    const int64_t nowUs = QpcNowUs();
    React::JSValueObject result;
    result["processInitMs"]   = static_cast<double>(g_qpcAppBuiltUs - g_qpcWinMainUs) / 1000.0;
    result["configMs"]        = static_cast<double>(g_qpcBeforeStartUs - g_qpcAppBuiltUs) / 1000.0;
    result["nativeElapsedMs"] = static_cast<double>(nowUs - g_qpcWinMainUs) / 1000.0;
    return result;
  }

  REACT_METHOD(reportReactMounted)
  void reportReactMounted() noexcept {
    TraceReactMounted();
  }

  REACT_METHOD(reportMetrics)
  void reportMetrics(double ttfpMs, double ttiMs) noexcept {
    TraceTTFP(ttfpMs);
    TraceTTI(ttiMs);
  }
};
