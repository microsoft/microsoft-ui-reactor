# TableView in Reactor (native C++/WinRT, WinAppSDK 2.0.1)

This sample hosts the **native C++/WinRT `TableView`** control — built as a **separate
satellite binary** `Microsoft.UI.Xaml.Controls.Advanced.dll` and projected to C# against
**public WinAppSDK 2.0.1** — live inside a [Reactor](../../..) app.

```
*** native Microsoft.UI.Xaml.Controls.TableView activated + ItemsSource set inside Reactor mount (WinAppSDK 2.0.1) ***
```

## What it shows

- The advanced WinUI `TableView` does **not** have to live in the WinUI framework DLL to be
  used from Reactor — it ships as its own DLL and is consumed via a C#/WinRT projection.
- It is **ABI-compatible with public 2.0.1**: the satellite DLL has no static framework
  imports and resolves framework/MUXC types via runtime WinRT activation (version-agnostic).
- Idiomatic Reactor authoring: `TableView(items)` (see `TableViewFacade.cs`) renders the
  native control declaratively, with reactive `ItemsSource` updates.

## Layout

| File | Role |
|------|------|
| `App.cs` | Reactor `Component` rendering `TableView(Data)` |
| `TableViewFacade.cs` | the `TableView(...)` DSL facade (wraps the native control via `XamlHostElement`) |
| `Program.cs` | `ReactorApp.Run<App>(...)` entry |
| `TableView.Projection/` | cswinrt projection of the native component winmd vs WinAppSDK 2.0.1 |
| `Microsoft.UI.Xaml.Controls.Advanced.dll` | the native satellite control DLL (deployed as Content) |
| `app.manifest` | embedded WinRT activation manifest mapping the TableView runtimeclasses to the satellite DLL |
| `selftest.ps1` | headless functional self-test (asserts activation inside the Reactor mount) |

## Run

```pwsh
dotnet run --project TableViewDemo.csproj -c Release -p:Platform=x64
```

## Self-test (used by the `TableView Demo Self-Test` CI workflow)

```pwsh
./selftest.ps1
```

## How the native DLL is wired (the non-obvious bits)

1. The projection (`TableView.Projection.dll`) is a normal `ProjectReference`, so it lands in
   `deps.json` and resolves cleanly in a self-contained app.
2. The native `Advanced.dll` is deployed next to the exe (`Content` + `CopyToOutputDirectory`).
3. WinRT activation is registered by **embedding** `app.manifest` via `<ApplicationManifest>`
   (a side-by-side `.exe.manifest` is ignored because .NET embeds its own). The manifest maps
   each TableView runtimeclass to `Microsoft.UI.Xaml.Controls.Advanced.dll`; the WinAppSDK
   detour handles framework activation.

## Extending to a first-class control

`TableViewFacade` is the thin W2 facade. To make `TableView` a first-class Reactor control
(typed `TableViewElement`, columns, selection props, pooling), implement a
`ControlDescriptor<TableViewElement, TableView>` per the repo `AGENTS.md` "Adding a new WinUI
control" guide and add a selftest fixture under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`.
