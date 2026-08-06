# Reactor on Uno Platform — samples

Samples that run **Microsoft.UI.Reactor** on Uno Platform Skia targets
(desktop, WebAssembly, Android), backed by the
[`Reactor.Uno`](../../src/Reactor.Uno) port library.

| Sample | What it shows | Targets |
| --- | --- | --- |
| [`ReactorUnoCounter`](ReactorUnoCounter) | Minimal counter — the smallest Reactor-on-Uno app | desktop, wasm, android, ios |
| [`ReactorUnoShowcase`](ReactorUnoShowcase) | ToggleSwitch, Slider, ProgressBar, CheckBox, ComboBox, pickers, multi-window | desktop, wasm, android, ios |
| [`file-based/Counter.cs`](file-based/Counter.cs) | A whole WinUI-style Reactor app in **one `.cs` file** (`dotnet run Counter.cs`) | desktop |

Both app samples are **single Uno projects targeting all four heads** — the
`Component` source is identical everywhere; only the entry point differs.

## Prerequisites

- **.NET 10 SDK** (`10.0.100`+ — see [`global.json`](global.json))
- The **`wasm-tools`** workload for the WebAssembly target: `dotnet workload install wasm-tools`
- The **`android`** / **`ios`** workloads for the mobile heads: `dotnet workload install android ios`
  (iOS compiles on Windows; deploying to a device still needs a Mac)

The Uno projects resolve `Uno.Sdk` from the nearby `global.json` and restore from
`nuget.org` only (see [`nuget.config`](nuget.config)); they do **not** inherit the
repo-root Windows/WinAppSDK build infrastructure.

## Run

### ReactorUnoCounter / ReactorUnoShowcase

```bash
# Desktop (X11 / Win32 / macOS / FrameBuffer — picked at runtime)
cd samples/Uno/ReactorUnoCounter
dotnet run -f net10.0-desktop

# WebAssembly (opens a localhost URL in your browser)
dotnet run -f net10.0-browserwasm
```

Swap `ReactorUnoCounter` for `ReactorUnoShowcase` to run the control showcase.

### Android / iOS

The same projects, different heads. Only the entry point is platform-specific:
desktop and **iOS** both use `ReactorApp.Run<T>()` (Uno gives the Apple heads the
same host-builder shape as desktop); **Android** is the one target with no console
entry point, so `Platforms/Android/Main.Android.cs` starts from an `Activity` and
asks Reactor for the `Application`:

```csharp
public class Application : Microsoft.UI.Xaml.NativeApplication
{
    public Application(IntPtr javaReference, JniHandleOwnership transfer)
        : base(() => ReactorApp.CreateApplication<CounterApp>("Reactor Counter (Uno)"),
               javaReference, transfer) { }
}
```

```bash
cd samples/Uno/ReactorUnoCounter

# Deploy + launch on a connected device or emulator (check `adb devices` first)
dotnet build -f net10.0-android -t:Run

# iOS — compiles on Windows; deploying to a device needs a Mac
dotnet build -f net10.0-ios
```

> **Don't sideload the Debug APK.** A Debug Android build uses .NET's *Fast
> Deployment*: the managed assemblies are **not inside the APK** — the deploy
> tooling pushes them separately to `files/.__override__/`. Installing the APK by
> hand therefore produces an app that aborts at startup with
> `No assemblies found in '…/.__override__/arm64-v8a'` (SIGABRT), which the phone
> reports only as a generic failure. Use `-t:Run`, or build a self-contained APK
> with `-p:EmbedAssembliesIntoApk=true` (or `-c Release`) if you really need to
> sideload one.

> Reactor apps contain **no XAML**, so there is no `ApplicationDefinition` for
> Uno's iOS Hot Restart helper generator to read and it fails with `Uno0005`. The
> samples set `<UnoDisableHotRestartHelperGeneration>true</...>`, which disables
> only VS's iOS *Hot Restart* deploy helper — not Hot Reload.

### file-based/Counter.cs

A single-file app — no project, no `.csproj`. Reactor owns the Uno `Application`,
so the file's root component must **not** be named `App` (Uno.Sdk generates its own
`App`).

```bash
cd samples/Uno/file-based
dotnet run Counter.cs
```

## Build the whole Uno set

From the repo root:

```bash
dotnet build Reactor.Uno.slnx
```

The solution builds per-TFM across all four heads:

```bash
dotnet build Reactor.Uno.slnx -f net10.0-desktop
dotnet build Reactor.Uno.slnx -f net10.0-browserwasm
dotnet build Reactor.Uno.slnx -f net10.0-android
dotnet build Reactor.Uno.slnx -f net10.0-ios
```

> The file-based `Counter.cs` is not a project and is not in the solution — run it
> directly with `dotnet run Counter.cs`.

## Hot Reload

`dotnet watch` (desktop) and the Uno Dev Server (wasm / Android / iOS) both
re-render edits to a `Component.Render()` body live, preserving `UseState`.

No opt-in is required: there is **no `HotReload` UnoFeature**, the dev-server
client is referenced automatically in Debug builds, and the Dev Server itself is
started by the IDE. (`.UseStudio()` is not needed either — that enables **Hot
Design**, Uno's runtime *XAML* designer, which doesn't apply to a XAML-free
framework.)

One caveat: from the CLI, `dotnet watch -f <tfm>` only works against a
**single-targeted** head — see
[the Hot Reload section in the port README](../../src/Reactor.Uno/README.md#hot-reload).
