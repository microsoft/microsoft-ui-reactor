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
- Idiomatic Reactor authoring: `TableView(items)` (see `Reactor.Controls.TableView/`) renders the
  native control declaratively as a **first-class, reconciled** Reactor element, with reactive
  `ItemsSource` updates.

## Layout

| File | Role |
|------|------|
| `App.cs` | Reactor `Component` rendering `TableView(Data)` |
| `Reactor.Controls.TableView/` | the **first-class** control library: `TableViewElement` + `TableViewHandler` (`IElementHandler`) + `Factories.TableView(...)`, registered via `ControlRegistry` |
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

## First-class control

`Reactor.Controls.TableView/` makes `TableView` a **first-class Reactor control**: a typed
`TableViewElement` record reconciled by `TableViewHandler`
(`IElementHandler<TableViewElement, Microsoft.UI.Xaml.Controls.TableView>`), registered with
`ControlRegistry` from a `Factories` static constructor — the opt-in third-party-control pattern
documented on `Microsoft.UI.Reactor.Advanced.Factories`. Authors write `TableView(items)` or
`TableView(items, columns)` and get reconciled columns, reactive `ItemsSource`, `SelectionMode` /
`SelectedIndex`, and an `OnSelectionChanged` callback — diffed as minimal writes on a single pooled
control, not a raw `XamlHostElement`. The library is kept **out of core** (`src/Reactor`) because core
must not depend on the POC control binary; it consumes the control purely through the public
extensibility API.

## Consume as a NuGet package

The control can also ship as a self-contained NuGet package so a consumer references it with a
single `<PackageReference>` instead of committing the native binary. `pack-and-verify.ps1` packs and
verifies the whole flow end-to-end:

```pwsh
./pack-and-verify.ps1
```

It produces `Microsoft.UI.Reactor.TableView` (`Reactor.Controls.TableView/` made packable), which bundles:

| Package path | Content |
|---|---|
| `lib/net10.0-windows…/Reactor.Controls.TableView.dll` | the first-class control (element + handler + factories) |
| `lib/net10.0-windows…/TableView.Projection.dll` | the CsWinRT projection (bundled; not a separate package) |
| `runtimes/win-x64/native/Microsoft.UI.Xaml.Controls.Advanced.dll` | the native control DLL (NuGet auto-deploys it next to the consumer's exe) |
| `build/Microsoft.UI.Reactor.TableView.targets` + `build/app.manifest` | supplies the WinRT activation manifest if the consumer hasn't declared one |

It depends on `Microsoft.UI.Reactor` + `Microsoft.WindowsAppSDK`. A consumer then needs only:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.UI.Reactor.TableView" Version="0.0.0-poc" />
</ItemGroup>
```

```csharp
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
// …
public override Element Render() => VStack(12, TextBlock("Native TableView"), TableView(data));
```

`pack-and-verify.ps1` then scaffolds exactly such a consumer, builds it against a local feed, and runs
it headlessly — asserting the native control activates purely through the package. (For a real
distribution the package would be pushed to a feed; the POC uses a generated local feed.)
