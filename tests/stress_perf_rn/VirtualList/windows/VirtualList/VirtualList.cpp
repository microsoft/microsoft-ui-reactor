// VirtualList.cpp : Defines the entry point for the application.
//
// Modified for stress-perf benchmarking — same shape as StocksGrid.cpp:
//   • Parses our --headless / --count / --duration / --percent CLI flags
//     out of the command line and forwards them to JS as initial props on
//     the React root, so headless runs are real CLI parameters rather than
//     bake-time process.env values (RN bundles process.env at compile
//     time; runtime env vars never reach JS).
//   • Maximizes the window at startup so renders happen at full screen
//     real estate, matching the C# variants which call SetPresenter(FullScreen).
//

#include "pch.h"
#include "VirtualList.h"

#include "AutolinkedNativeModules.g.h"

#include "NativeModules.h"

#include <shellapi.h>      // CommandLineToArgvW
#include <string>
#include <string_view>

// A PackageProvider containing any turbo modules you define within this app project
struct CompReactPackageProvider
    : winrt::implements<CompReactPackageProvider, winrt::Microsoft::ReactNative::IReactPackageProvider> {
 public: // IReactPackageProvider
  void CreatePackage(winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept {
    AddAttributedModules(packageBuilder, true);
  }
};

namespace {

struct CliArgs {
  bool headless = false;
  double percent = 10.0;     // StocksGrid only
  double duration = 5.0;
  int count = 5000;          // VirtualList default
};

CliArgs ParseCli() {
  CliArgs out;
  int argc = 0;
  LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
  if (!argv) return out;

  auto eq = [](LPCWSTR a, LPCWSTR b) {
    return std::wstring_view(a) == std::wstring_view(b);
  };

  // argv[0] is the exe path; user args start at 1.
  for (int i = 1; i < argc; ++i) {
    if (eq(argv[i], L"--headless")) {
      out.headless = true;
    } else if (eq(argv[i], L"--percent") && i + 1 < argc) {
      out.percent = _wtof(argv[++i]);
    } else if (eq(argv[i], L"--duration") && i + 1 < argc) {
      out.duration = _wtof(argv[++i]);
    } else if (eq(argv[i], L"--count") && i + 1 < argc) {
      out.count = _wtoi(argv[++i]);
    }
  }
  LocalFree(argv);
  return out;
}

} // namespace

// The entry point of the Win32 application
_Use_decl_annotations_ int CALLBACK WinMain(HINSTANCE instance, HINSTANCE, PSTR /* commandLine */, int showCmd) {
  // Initialize WinRT
  winrt::init_apartment(winrt::apartment_type::single_threaded);

  // Enable per monitor DPI scaling
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

  // Find the path hosting the app exe file
  WCHAR appDirectory[MAX_PATH];
  GetModuleFileNameW(NULL, appDirectory, MAX_PATH);
  PathCchRemoveFileSpec(appDirectory, MAX_PATH);

  // Parse our CLI flags before constructing the app so we can route them as
  // initial props.
  const auto cli = ParseCli();

  // Create a ReactNativeWin32App with the ReactNativeAppBuilder
  auto reactNativeWin32App{winrt::Microsoft::ReactNative::ReactNativeAppBuilder().Build()};

  // Configure the initial InstanceSettings for the app's ReactNativeHost
  auto settings{reactNativeWin32App.ReactNativeHost().InstanceSettings()};
  // Register any autolinked native modules
  RegisterAutolinkedNativeModulePackages(settings.PackageProviders());
  // Register any native modules defined within this app project
  settings.PackageProviders().Append(winrt::make<CompReactPackageProvider>());

#if BUNDLE
  // Load the JS bundle from a file (not Metro):
  // Set the path (on disk) where the .bundle file is located
  settings.BundleRootPath(std::wstring(L"file://").append(appDirectory).append(L"\\Bundle\\").c_str());
  // Set the name of the bundle file (without the .bundle extension)
  settings.JavaScriptBundleFile(L"index.windows");
  // Disable hot reload
  settings.UseFastRefresh(false);
#else
  // Load the JS bundle from Metro
  settings.JavaScriptBundleFile(L"index");
  // Enable hot reload
  settings.UseFastRefresh(true);
#endif
#if _DEBUG
  // For Debug builds
  // Enable Direct Debugging of JS
  settings.UseDirectDebugger(true);
  // Enable the Developer Menu
  settings.UseDeveloperSupport(true);
#else
  // For Release builds:
  // Disable Direct Debugging of JS
  settings.UseDirectDebugger(false);
  // Disable the Developer Menu
  settings.UseDeveloperSupport(false);
#endif

  // Get the AppWindow so we can configure its initial title and size.
  auto appWindow{reactNativeWin32App.AppWindow()};
  appWindow.Title(L"VirtualList");
  // Maximize on launch — the StressPerf C# variants run full-screen too, so
  // we want the same render surface area to keep the comparison fair.
  if (auto op = appWindow.Presenter().try_as<winrt::Microsoft::UI::Windowing::OverlappedPresenter>()) {
    op.Maximize();
  } else {
    // Fallback: if we can't get an OverlappedPresenter (e.g. compact-overlay
    // mode in some hosts), at least make the window large.
    appWindow.Resize({1600, 1200});
  }

  // Get the ReactViewOptions so we can set the initial RN component to load
  auto viewOptions{reactNativeWin32App.ReactViewOptions()};
  viewOptions.ComponentName(L"VirtualList");

  // Forward parsed CLI to JS as initial props on the React root. App.tsx
  // reads these via its own props parameter.
  viewOptions.InitialProps([cli](winrt::Microsoft::ReactNative::IJSValueWriter const& writer) {
    writer.WriteObjectBegin();
    writer.WritePropertyName(L"headless");
    writer.WriteBoolean(cli.headless);
    writer.WritePropertyName(L"percent");
    writer.WriteDouble(cli.percent);
    writer.WritePropertyName(L"duration");
    writer.WriteDouble(cli.duration);
    writer.WritePropertyName(L"count");
    writer.WriteInt64(cli.count);
    writer.WriteObjectEnd();
  });

  // Start the app
  reactNativeWin32App.Start();
}
