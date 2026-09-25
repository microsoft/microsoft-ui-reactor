# Microsoft.UI.Reactor.Advanced

**Optional Reactor components with heavier native and graphics dependencies — a Win2D canvas family for immediate-mode drawing, the D3-derived charting subsystem, the docking / dock-layout subsystem, a CommonMark markdown renderer, and a virtualized data grid, all inside a Reactor element tree.**

## About

`Microsoft.UI.Reactor.Advanced` extends [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) with components that pull in larger native or graphics stacks: a Win2D canvas family (manual, animated, and virtual) that lets you draw with `CanvasDrawingSession` directly from a declarative Reactor component; the `Microsoft.UI.Reactor.Charting` subsystem — a C# port of the D3 primitives plus ready-made chart components and chart accessibility; the `Microsoft.UI.Reactor.Docking` subsystem — a Visual-Studio-style docking host with floating windows, tear-off tabs, splitters, and layout persistence; the `Microsoft.UI.Reactor.Markdown` subsystem — a CommonMark renderer (C# md4c port) that turns markdown into a Reactor element tree; and a virtualized, editable **data grid** (`Microsoft.UI.Reactor.Controls` `DataGrid`) with typed columns, sorting/filtering, paging, and column resize/reorder.

This package is intentionally separate from the core framework so that apps which don't need Win2D, charting, docking, markdown, or the data grid keep their trim/AOT closure and native payload isolated. The relocated types keep their existing namespaces (`Microsoft.UI.Reactor.Charting` / `.Docking` / `.Markdown`, and the data-grid records in `Microsoft.UI.Reactor.Controls`), so moving to this package is a package reference with no source change — except the DSL *entry points* that lived in the shared core `Factories` partial: the `Markdown(...)` factory and the data grid's `DataGrid(...)` / `Column(...)` / `AutoColumns(...)` factories move into `Microsoft.UI.Reactor.Advanced.Factories` (add `using static Microsoft.UI.Reactor.Advanced.Factories;`).

## How to Use

Install the package alongside `Microsoft.UI.Reactor`:

```shell
dotnet add package Microsoft.UI.Reactor.Advanced
```

Drop a Win2D canvas into any component. The `onDraw` callback runs on the UI thread with a `CanvasDrawingSession`; pass a `redrawKey` to trigger invalidation when your state changes:

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using static Microsoft.UI.Reactor.Factories;          // core DSL
using static Microsoft.UI.Reactor.Advanced.Factories; // Win2D DSL

internal sealed class CanvasDemo : Component
{
    public override Element Render()
    {
        var (radius, setRadius) = UseState(40f);

        return VStack(12,
            Win2DCanvas(
                onDraw: (session, args) =>
                {
                    session.Clear(Colors.Black);
                    session.FillCircle(120, 120, radius, Colors.DeepSkyBlue);
                },
                redrawKey: radius),
            Button("Grow", () => setRadius(radius + 10f))
        ).Padding(24);
    }
}
```

When `radius` changes, the new `redrawKey` tells Reactor to invalidate the canvas and redraw.

## Key Features

- **`Win2DCanvas`** — manual-invalidate canvas (`CanvasControl`); redraws when its `redrawKey` changes.
- **`Win2DAnimatedCanvas`** — game-loop canvas (`CanvasAnimatedControl`) whose update and draw callbacks run on the Win2D game thread.
- **`Win2DVirtualCanvas`** — virtualized canvas (`CanvasVirtualControl`) for very large drawing surfaces.
- **Async resource creation** — overloads accept an `onCreateResources` callback tracked by Win2D for loading bitmaps and other device resources.
- **Isolated native payload** — keeps Win2D out of the core framework's trim/AOT closure.
- **Charting (`Microsoft.UI.Reactor.Charting`)** — a C# port of the D3 primitives (scales, shapes, layouts, color/format/interpolate) plus high-level `Charts` components and chart accessibility. Import with `using static Microsoft.UI.Reactor.Charting.Charts;`.
- **Docking (`Microsoft.UI.Reactor.Docking`)** — a Visual-Studio-style docking host: dockable tool windows and documents, floating windows, tear-off tabs, splitters, keyboard navigation, and JSON layout persistence. Register the native renderer with `DockingNativeInterop.Register(reconciler)`.
- **Markdown (`Microsoft.UI.Reactor.Markdown`)** — a CommonMark renderer (C# port of the md4c parser) that turns a markdown string into a Reactor element tree. The `Markdown(...)` factory ships in `Microsoft.UI.Reactor.Advanced.Factories`, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.
- **Data grid (`Microsoft.UI.Reactor.Controls` `DataGrid`)** — a virtualized, editable data grid with typed columns, sorting/filtering, paging, and column resize/reorder. The `DataGrid(...)` / `Column(...)` / `AutoColumns(...)` factories ship in `Microsoft.UI.Reactor.Advanced.Factories`, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`; the element/column records keep their `Microsoft.UI.Reactor.Controls` namespace.

## Main Types

- **`Win2DCanvas(...)`** — factory for a manual-invalidate Win2D canvas.
- **`Win2DAnimatedCanvas(...)`** — factory for an animated game-loop canvas.
- **`Win2DVirtualCanvas(...)`** — factory for a virtualized canvas.
- **`Win2DCanvasElement`** — immutable element produced by `Win2DCanvas`.

## Best Practices

- **Drive redraws with `redrawKey`.** Pass the state that affects the drawing (or a composite of it) as `redrawKey`. The canvas only invalidates when the key changes, so avoid allocating a new key object on every render when nothing changed.
- **Create device resources in `onCreateResources`, not `onDraw`.** Load bitmaps and other GPU resources in the resource callback so they survive device-lost events; the `onDraw` callback should stay allocation-free and fast.
- **Mind the thread for animated canvases.** `Win2DAnimatedCanvas` invokes `onUpdate`/`onDraw` on the Win2D game thread, not the UI thread. Don't touch Reactor component state directly from those callbacks — pass data in through `drawState` and marshal back with a `threadSafe: true` state setter if you need to update the UI.
- **Keep Win2D optional.** Reference this package only from the projects that need it so apps that don't draw keep a smaller trim/AOT closure and native payload.

## Additional Documentation

- [Win2D canvas guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/win2d-canvas.md)
- [Samples](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples)
- [Win2D documentation](https://microsoft.github.io/Win2D/WinUI3/html/Introduction.htm)

## Related Packages

- [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — the core declarative WinUI 3 framework (required).
- [`Microsoft.UI.Reactor.Devtools`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Devtools) — optional developer-loop devtools host.

## Feedback & Contributing

`Microsoft.UI.Reactor.Advanced` is part of the open-source Reactor project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
