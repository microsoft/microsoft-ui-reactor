# Microsoft.UI.Reactor.Advanced

**Optional Reactor components with heavier native and graphics dependencies — starting with a Win2D canvas family for immediate-mode drawing inside a Reactor element tree.**

## About

`Microsoft.UI.Reactor.Advanced` extends [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) with components that pull in larger native or graphics stacks. Its first surface is a Win2D canvas family (manual, animated, and virtual) that lets you draw with `CanvasDrawingSession` directly from a declarative Reactor component.

This package is intentionally separate from the core framework so that apps which don't need Win2D keep their trim/AOT closure and native payload isolated.

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

## Main Types

| Type | Description |
|------|-------------|
| `Win2DCanvas(...)` | Factory for a manual-invalidate Win2D canvas. |
| `Win2DAnimatedCanvas(...)` | Factory for an animated game-loop canvas. |
| `Win2DVirtualCanvas(...)` | Factory for a virtualized canvas. |
| `Win2DCanvasElement` | Immutable element produced by `Win2DCanvas`. |

## Additional Documentation

- [Win2D canvas guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/win2d-canvas.md)
- [Samples](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples)
- [Win2D documentation](https://microsoft.github.io/Win2D/WinUI3/html/Introduction.htm)

## Related Packages

- [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — the core declarative WinUI 3 framework (required).
- [`Microsoft.UI.Reactor.Devtools`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Devtools) — optional developer-loop devtools host.
- [`Microsoft.UI.Reactor.ProjectTemplates`](https://www.nuget.org/packages/Microsoft.UI.Reactor.ProjectTemplates) — `dotnet new` templates.

## Feedback & Contributing

`Microsoft.UI.Reactor.Advanced` is part of the open-source Reactor project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
