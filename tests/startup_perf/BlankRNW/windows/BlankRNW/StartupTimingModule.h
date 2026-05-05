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

  // Split into two methods so each ETW event fires at its actual moment —
  // FirstRender from inside requestAnimationFrame, FirstIdle from inside
  // requestIdleCallback. Calling both from the idle callback (the prior
  // shape) would collapse FirstRender's timestamp onto FirstIdle's,
  // making harness-computed TTFP equal TTI.
  REACT_METHOD(reportFirstRender)
  void reportFirstRender(double ttfpMs) noexcept { TraceTTFP(ttfpMs); }

  REACT_METHOD(reportFirstIdle)
  void reportFirstIdle(double ttiMs) noexcept { TraceTTI(ttiMs); }
};
