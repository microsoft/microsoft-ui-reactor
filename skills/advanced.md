---
name: reactor-advanced
description: >
  Reactor.Advanced Win2D canvases: choosing Win2DCanvas, Win2DAnimatedCanvas,
  or Win2DVirtualCanvas; using UseDrawState, UseCanvasResources, and
  UseDrawCommand; and following Win2D threading/device-loss rules. Also the
  experimental WinUI TableView and Chart controls (TableView, TextColumn,
  TemplateColumn, Chart, LineSeries, BarSeries, AreaSeries).
---

# Reactor Advanced

Use this skill when adding immediate-mode Win2D drawing through `Microsoft.UI.Reactor.Advanced`. The full guide is `docs/guide/win2d-canvas.md`; the performance sample is `samples/apps/particle-storm/`.

## Pick the right canvas

| Workload | Use | Why |
|---|---|---|
| Static or data-driven drawing that changes after state updates | `Win2DCanvas.Of(onDraw, redrawKey)` | Manual invalidation; pass a changing `RedrawKey` for every value the draw callback reads. |
| Game loop, physics, particles, visualizers, steady FPS | `Win2DAnimatedCanvas.Of(onUpdate, onDraw, drawState, isPaused)` | Win2D owns the tick and calls update/draw on the game thread. |
| Huge scrollable or tiled surfaces | `Win2DVirtualCanvas.Of(onRegionDraw, contentSize)` | Draws only invalidated visible regions; update tiles by changing `InvalidateRegions`. |

## Threading cheat sheet

| Callback | Thread | Rule |
|---|---|---|
| `Win2DCanvas.OnDraw` | UI thread | Safe to read Reactor state captured by render. |
| `Win2DAnimatedCanvas.OnUpdate` / `.OnDraw` | Win2D game thread | Do **not** touch WinUI controls. Use thread-safe data or `UseState(threadSafe: true)` for UI handoff. |
| `Win2DVirtualCanvas.OnRegionDraw` | UI thread | Keep work bounded to the invalidated region. |
| `UseCanvasResources` factory | Worker/game thread | Create device resources and return a disposable/recreatable object. |

Treat `Ref.Current` from `UseDrawState` like a volatile field. Re-read it and make the referenced object safe for any cross-thread mutation. Debug builds add a sentinel that appends `docs/guide/win2d-canvas.md#threading` to likely WinUI thread-affinity exceptions from animated callbacks.

## Reference graph model

Element refs are reactive cells. `.Ref(cell)` writes the mounted
`FrameworkElement` into the cell and writes `null` on unmount.
Reference properties (`TeachingTip.Target`, `.LabeledBy`, `.DescribedBy`,
`.FlowsTo`, `.FlowsFrom`, `.XYFocus*`, and custom descriptor
`.Reference` / `.ReferenceList`) subscribe to those cells instead of
sampling `.Current`.

Resolution is one-way and push-based: cells enqueue dirty reference
edges during commit, then Reactor flushes the dirty set after the tree is
stable. Cycles are allowed because each property slot is a one-way edge.
List references preserve author declaration order and omit unresolved
targets. Advanced authors should use `descriptor.Reference` for regular
controls and `binding.Reference` only as the bridge for hand-coded
handlers.

## Hooks and recipes

```csharp
var state = ctx.UseDrawState(() => new ParticleField(20_000));

var sprite = ctx.UseCanvasResources<CanvasBitmap>(async device =>
    await CanvasBitmap.LoadAsync(device, "Assets/particle.png"));

var draw = ctx.UseDrawCommand(model, static (session, args, m) =>
    m.Render(session), deps: [model.Version]);
```

`UseCanvasResources` is the device-loss recipe: allocate from the supplied `CanvasDevice`, draw only when the returned ref is non-null, and let the hook dispose/recreate after `CanvasDevice.DeviceLost`.

For multiple canvases, use `.UseSharedDevice()` on the elements to share a single `CanvasDevice` instead of creating one per control — reduces device-creation overhead and memory footprint.

## Performance proof

The Particle Storm sample (`samples/apps/particle-storm/`) is the canonical pattern: pure Reactor chrome controls parameters; `Win2DAnimatedCanvas` renders the hot particle path; `UseDrawState` holds the particle buffers; `UseCanvasResources` owns sprites and other device resources.

## WinUI TableView and Chart (experimental)

`Microsoft.UI.Reactor.Advanced.Tabular` and `.Charts` map the platform `Microsoft.UI.Xaml.Controls.Tabular.TableView` and `Microsoft.UI.Xaml.Controls.Charts.Chart`. They need the Windows App SDK `-experimental` channel and are `[Experimental]` (CS8305). The handlers merge `TabularControlsResources` / `XamlChartsResources` into `Application.Resources` on first use — no `App.xaml` edit — but an **unpackaged app must set `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`** (or be MSIX-packaged): the controls load their themes from `ms-appx:///`, which a framework-dependent unpackaged app cannot resolve. For D3-style custom charts (pie, radial, hierarchy) keep using `Microsoft.UI.Reactor.Charting`. Samples: `samples/TableView.Sample`, `samples/WinUIChart.Sample`.

```csharp
using Microsoft.UI.Reactor.Advanced.Tabular;
using Microsoft.UI.Reactor.Advanced.Charts;
using static Microsoft.UI.Reactor.Advanced.Factories;

record Person(int Id, string Name, string Team, int Age);   // immutable — no INPC

var (people, setPeople) = UseState(InitialPeople);           // Person[]
void Rename(Person p, string name) =>
    setPeople([.. people.Select(x => x.Id == p.Id ? x with { Name = name } : x)]);

TableView(people,
        TextColumn<Person>("Name", p => p.Name, Rename, width: 160), // native text column, edit -> callback
        TextColumn<Person>("Team", p => p.Team),                     // read-only, sorts by text
        TemplateColumn<Person>("Age", p => TextBlock($"{p.Age}"))    // Reactor cell
            .SortBy<Person>((a, b) => a.Age.CompareTo(b.Age)))        // reflection-free sort
    .KeyRows<Person>(p => p.Id)                                       // diff snapshots by identity
    .SelectionMode(TableViewSelectionMode.Single)
    .SelectionChanged((_, item) => setSelected(item as Person))
    .FilterRows<Person>(p => p.Age >= minAge)
    .GroupRows<Person>(p => p.Team);

Chart(
        AreaSeries(months, forecast, "Forecast"),
        BarSeries(months, actual, "Actual").Vertical(),
        LineSeries(months, target, "Target").Markers())
    .Legend("Monthly results");
```

| Rule | Why |
|---|---|
| Pass immutable snapshots (arrays/lists of records) and replace them to change rows; add `.KeyRows<T>(...)` for stable identity. | The handler diffs each snapshot into a collection it owns — the table never resets, so selection, scroll and edits survive. A user-owned `INotifyCollectionChanged` collection is bound as-is instead. |
| Prefer `TextColumn<T>(header, read, onEdit)`; edits arrive as a callback, nothing is written into the row. | It is the native text column (accessible TextBlock cell, TextBox editor, UIA value pattern) fed by a pathless binding + C# converter — no reflection, NativeAOT-safe. |
| `BoundColumn(header, path)` is the classic `{Binding}` for mutable INPC models; under NativeAOT mark row types `[WinRT.GeneratedBindableCustomProperty]` (partial), and opt into editing with `.ReadOnly(false)`. | The path resolves by name through `ICustomPropertyProvider`; the control defaults to read-only. |
| Columns reconcile by position; same column type ⇒ updated in place (width/sort state survive). | Inserting/removing/retyping a column rebuilds the native column list. |
| Template cells re-render when the column record changes; hoist static column lists to skip that. | Cells belong to realized rows, not items — keep per-item state in the item. |
| Memoize `FilterRows`/`GroupRows` delegates for large tables. | They re-apply whenever the delegate instance changes. |
| X values are categories (strings or numbers) or `DateTimeOffset`s; `LinearAxis` is a Y (value) axis only. | The native X slot rejects a linear axis. |
| Series that share an X axis should pass the same X collection instance. | One shared native `Samples` per collection, as XAML declares it. |
| `BarSeries` is horizontal by default — call `.Vertical()` for columns. Horizontal bars get their own axes. | The native chart refuses to share axes across those layouts. |
| Assign a new array (or use an `ObservableCollection`) to update a series. | Values are re-read only when the collection reference changes. |
