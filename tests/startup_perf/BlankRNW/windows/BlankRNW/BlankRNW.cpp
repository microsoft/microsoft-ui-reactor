// BlankRNW.cpp - entry point for the startup-only RNW baseline.
//
// Mirrors -lift's rnw-fabric-ttfp-tti-bench/windows/RNWApp/RNWApp.cpp:
// timestamps WinMain entry / post-Build / pre-Start with QPC, fires ETW
// events at WinMain entry, XAML-app-loaded, Window-loaded, JSBundle-loaded
// (via InstanceLoaded), and on process stop.  ReactMounted / FirstRender /
// FirstIdle are fired from JS via the StartupTiming TurboModule
// (see App.tsx + StartupTimingModule.h).

#include "pch.h"
#include "BlankRNW.h"

#include "AutolinkedNativeModules.g.h"
#include "NativeModules.h"
#include "RNWAppTracing.h"
#include "StartupTimingModule.h"

struct CompReactPackageProvider
    : winrt::implements<CompReactPackageProvider, winrt::Microsoft::ReactNative::IReactPackageProvider> {
  void CreatePackage(winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept {
    AddAttributedModules(packageBuilder, true);
  }
};

_Use_decl_annotations_ int CALLBACK WinMain(HINSTANCE /*instance*/, HINSTANCE, PSTR /*commandLine*/, int /*showCmd*/) {
  // QPC the WinMain entry tick before anything else, then register the ETW
  // provider and emit wWinMainEntry. Order matters: registering the
  // provider takes ~tens of microseconds, so we want to capture the
  // entry tick first to keep our process-init measurement honest.
  CaptureWinMainEntry();
  RNWAppTracingRegister();
  TraceWinMainEntry();

  winrt::init_apartment(winrt::apartment_type::single_threaded);
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

  WCHAR appDirectory[MAX_PATH];
  GetModuleFileNameW(NULL, appDirectory, MAX_PATH);
  PathCchRemoveFileSpec(appDirectory, MAX_PATH);

  // Build = construct the React host (Hermes init, JS thread, TurboModule
  // registry binding, .hbc IO, etc.). This is the dominant cost of RN
  // startup and the ~220 ms portion called out in -lift's README.
  auto reactNativeWin32App{winrt::Microsoft::ReactNative::ReactNativeAppBuilder().Build()};
  CaptureAppBuilt();
  TraceXamlAppLoaded(); // XAML host is now initialized

  auto settings{reactNativeWin32App.ReactNativeHost().InstanceSettings()};
  RegisterAutolinkedNativeModulePackages(settings.PackageProviders());
  settings.PackageProviders().Append(winrt::make<CompReactPackageProvider>());

  // InstanceLoaded fires once the JS instance is up and the bundle has
  // been evaluated. That's our JSBundleLoaded marker.
  reactNativeWin32App.ReactNativeHost().InstanceSettings().InstanceLoaded(
      [](auto const&, auto const&) noexcept { TraceJSBundleLoaded(); });

#if BUNDLE
  settings.BundleRootPath(std::wstring(L"file://").append(appDirectory).append(L"\\Bundle\\").c_str());
  settings.JavaScriptBundleFile(L"index.windows");
  settings.UseFastRefresh(false);
#else
  settings.JavaScriptBundleFile(L"index");
  settings.UseFastRefresh(true);
#endif
#if _DEBUG
  settings.UseDirectDebugger(true);
  settings.UseDeveloperSupport(true);
#else
  settings.UseDirectDebugger(false);
  settings.UseDeveloperSupport(false);
#endif

  auto appWindow{reactNativeWin32App.AppWindow()};
  appWindow.Title(L"BlankRNW");
  appWindow.Resize({1000, 1000});

  auto viewOptions{reactNativeWin32App.ReactViewOptions()};
  viewOptions.ComponentName(L"BlankRNW");

  TraceWindowLoaded(); // Window is now ready

  CaptureBeforeStart();
  reactNativeWin32App.Start(); // JS execution starts here

  TraceProcessStop();
  RNWAppTracingUnregister();
  return 0;
}
