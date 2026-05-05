#pragma once

#include <minwindef.h>

// Mirrors microsoft-ui-xaml-lift/Samples/FrameworkBenchmarkBlankApps/Common/Tracing.h
// Provider name + GUID + event names match exactly so that:
//   - the same WPR profile (Tracing.wprp) captures both -lift and our apps
//   - the same Regions XML resolves both
//   - WPA shows -lift and our variants side-by-side on the same regions panel

namespace Tracing
{

HRESULT Register();
void SetAppName(const char* appName);
void Unregister();

void TraceWinMainEntry();
void TraceXamlAppLoaded();
void TraceWindowLoaded();
void TraceJSBundleLoaded();
void TraceReactMounted();
void TraceFirstRender();
void TraceFirstIdle();
void TraceMessagePumpIdle();
void TraceProcessStop();

} // namespace Tracing
