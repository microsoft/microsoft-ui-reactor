# ReactorCharting Native Chart Migration — Design Spec

Migrate the high-level chart DSL (`LineChart`, `BarChart`, `AreaChart`,
`PieChart`, `TreeChart`, `ForceGraph`) from imperative `XamlHostElement`
rendering to native Microsoft.UI.Reactor (Reactor) element trees using the existing D3 drawing
primitives.

---

## Status

**Implemented** — Line, Bar, Area, Pie, and Tree charts migrated to native
D3 element trees. ForceGraph kept as XamlHostElement (Option A). Handles
updated with `[Obsolete]` on `Redraw()`. ReactorCharting.Sample converted to
state-driven updates.

---

## Problem Statement

The high-level chart factories in `ReactorCharting/Charts/Charts.cs` bypass
Reactor's virtual tree entirely. Each `ChartElement<T>` wraps a
`XamlHostElement` that creates a raw WinUI `Canvas` and imperatively
calls `canvas.Children.Add(...)` to draw shapes:

```csharp
// Current: imperative Canvas drawing inside XamlHostElement
private void FullRender(Canvas canvas, IReadOnlyList<T> data)
{
    canvas.Children.Clear();   // Destroy everything
    // ... create WinUI shapes one by one ...
    canvas.Children.Add(new Shapes.Path { ... });
    canvas.Children.Add(new Shapes.Line { ... });
}
```

This means:

1. **No diffing.** Every re-render clears all Canvas children and
   recreates them from scratch, even when only one data point changed.
2. **No element pooling.** Shapes are allocated and discarded every frame
   instead of being reused from Reactor's `ElementPool`.
3. **No animation.** Chart elements can't use `.OpacityTransition()`,
   `.LayoutAnimation()`, `.InteractionStates()`, or any Reactor modifier
   because the shapes aren't in the reconciler's tree.
4. **No theming.** Hard-coded `SolidColorBrush` objects don't respond to
   `Theme.*` tokens or dark/light mode changes.
5. **No accessibility.** Canvas children have no `AutomationName`,
   `HeadingLevel`, or other Reactor accessibility modifiers.
6. **XamlHostElement was silently dropped.** Before the reconciler fix
   (adding `XamlHostElement`/`XamlPageElement` to the built-in
   mount/update switches), chart elements rendered as empty space unless
   the app called `XamlInterop.Register(host.Reconciler)` — a
   requirement that no other Reactor element has.

Meanwhile, the low-level D3 drawing DSL (`D3Canvas`, `D3Rect`, `D3Path`,
`D3Line`, `D3Text`, etc.) already returns native Reactor `Element` types.
The ReactorCharting.Gallery samples (42 charts) use this DSL exclusively and get
full reconciler support. The high-level chart DSL should too.

---

## Goal

Replace the `XamlHostElement`-based rendering in `ChartElement<T>`,
`PieChartElement<T>`, `TreeChartElement<T>`, and `ForceGraphElement` with
pure Reactor element trees built from the existing D3 DSL primitives. After
migration:

- Charts diff and patch like any other Reactor subtree
- Charts participate in element pooling
- Chart shapes can use Reactor modifiers (animation, theming, accessibility)
- No special registration or escape hatches needed
- `OnReady` / `ChartHandle.Redraw` still works for live-data scenarios
- The `using static Microsoft.UI.Reactor.Charting.Charts;` API is unchanged

---

## Design

### 1. ChartElement<T> becomes a virtual Element, not XamlHostElement

**Before:**
```csharp
public Element ToElement() =>
    new XamlHostElement(BuildCanvas, UpdateCanvas) { TypeKey = "ReactorChartingChart_Line" };
```

**After:**
```csharp
public Element ToElement() => BuildElement(Data);

private Element BuildElement(IReadOnlyList<T> data)
{
    if (data.Count == 0) return Empty();

    double plotLeft = _marginLeft, plotTop = _marginTop;
    double plotWidth = _width - _marginLeft - _marginRight;
    double plotHeight = _height - _marginTop - _marginBottom;

    var (xMin, xMax) = D3Extent.Extent(data, XAccessor);
    var (yMin, yMax) = D3Extent.Extent(data, YAccessor);
    var xScale = new LinearScale([xMin, xMax], [plotLeft, plotLeft + plotWidth]).Nice();
    var yScale = new LinearScale([yMin, yMax], [plotTop + plotHeight, plotTop]).Nice();

    return D3Canvas(_width, _height,
        [.. _showGrid ? D3Grid(yScale, plotLeft, plotWidth) : [],
         .. RenderData(data, xScale, yScale, plotLeft, plotTop, plotWidth, plotHeight),
         .. _showAxes ? D3Axes(xScale, yScale, plotLeft, plotTop, plotWidth, plotHeight) : []]
    );
}
```

Each chart type implements `RenderData` returning `Element[]`:

| Chart type | RenderData produces |
|-----------|-------------------|
| Line | `D3LinePath<T>(data, xMap, yMap)` |
| Bar | `D3Rect(...)` per data point with `BandScale` |
| Area | `D3AreaPath<T>(data, xMap, baseline, yMap)` + `D3LinePath` overlay |
| Pie | `D3Pie<T>(data, value, cx, cy, outerRadius, innerRadius)` |

### 2. Keying for efficient diffing

Each data-driven shape should carry a `.WithKey(...)` so the reconciler
can match old and new elements positionally or by identity:

```csharp
// Bar chart: key by data index for O(n) positional reconciliation
var bars = data.Select((d, i) =>
    D3Rect(x, y, barWidth, barHeight)
        .WithKey($"bar-{i}")
        .Fill(fillBrush)
);
```

For small datasets (< 100 points), positional keying by index is
sufficient. For larger datasets, consider keying by the X value or a
user-provided key selector.

### 3. PieChartElement<T>

Replace the imperative arc rendering with `D3Pie<T>()` which already
returns `Element[]`:

```csharp
private Element BuildElement(IReadOnlyList<T> data)
{
    var palette = _colorPalette ?? D3Color.Category10;
    double cx = _width / 2, cy = _height / 2;
    double outerRadius = Math.Min(cx, cy) - 10;

    return D3Canvas(_width, _height,
        [.. D3Pie(data, ValueAccessor, cx, cy, outerRadius, _innerRadius, _padAngle),
         .. LabelAccessor != null ? RenderLabels(data, cx, cy, outerRadius) : []]
    );
}
```

### 4. TreeChartElement<T>

Replace the imperative tree rendering with `TreeLayout` + D3 primitives:

```csharp
private Element BuildElement(T rootData)
{
    var layout = TreeLayout.Create<T>().Size(_width, _height);
    var root = layout.Hierarchy(rootData, ChildrenAccessor);
    layout.Layout(root);

    var links = CollectLinks(root).Select(pair =>
        D3Link(pair.Parent.X, pair.Parent.Y, pair.Child.X, pair.Child.Y,
            stroke: Brush(_linkColor)));

    var nodes = CollectNodes(root).Select(node =>
        D3Circle(node.X, node.Y, _nodeRadius)
            .Fill(node.Children.Count > 0 ? Brush(_nodeColor) : Brush("white"))
            .Stroke(Brush(_nodeColor)));

    var labels = LabelAccessor != null
        ? CollectNodes(root).Select(node =>
            D3Text(node.X + _nodeRadius + 4, node.Y - 6, LabelAccessor(node.Data)))
        : [];

    return D3Canvas(_width, _height, [.. links, .. nodes, .. labels]);
}
```

### 5. ForceGraphElement

Force graphs are interactive — nodes have drag handlers. The current
design stores WinUI `Ellipse[]` and `Line[]` references in
`ForceGraphHandle` for direct manipulation via `SyncPositions()`.

This is the hardest chart to migrate because the `SyncPositions()` pattern
bypasses the virtual tree by design (60fps position updates via
`Canvas.SetLeft/SetTop`). Two options:

**Option A: Keep XamlHostElement for ForceGraph only.**
Force graphs are inherently imperative (physics simulation + drag). The
`SyncPositions()` pattern is the right approach for 60fps updates. Keep
`ForceGraphElement` as `XamlHostElement` (now supported natively by the
reconciler).

**Option B: State-driven renders with positional reconciliation.**
Store simulation state in `UseRef`, tick via `UseEffect` timer, re-render
on each tick. Use positional keying for O(n) diffing:

```csharp
UseEffect(() => {
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    timer.Tick += (_, _) => { sim.Tick(); rerender(); };
    timer.Start();
    return () => timer.Stop();
});

return D3Canvas(width, height,
    [.. sim.Links.Select((l, i) =>
        D3Line(nodes[l.Source].X, nodes[l.Source].Y,
               nodes[l.Target].X, nodes[l.Target].Y)
            .WithKey($"link-{i}")),
     .. sim.Nodes.Select((n, i) =>
        D3Circle(n.X, n.Y, n.Radius)
            .WithKey($"node-{i}")
            .Fill(palette[i % palette.Count]))]
);
```

**Recommendation: Option A.** The overhead of full reconciliation at 60fps
is unnecessary when `SyncPositions()` does exactly the right thing. The
other chart types change data infrequently and benefit from diffing; force
graphs change every 16ms and need direct manipulation.

### 6. OnReady / ChartHandle

The `OnReady(Action<ChartHandle<T>>)` callback currently exposes a
`Redraw(data)` method for pushing live data without a Reactor re-render.

After migration, the caller has two options:

1. **Just change state.** Since charts are now regular elements, calling
   `setData(newData)` triggers a normal re-render. The reconciler diffs
   the old and new element trees and patches only what changed. This is
   the recommended approach for most use cases.

2. **OnReady for escape hatch.** For high-frequency updates (e.g., 60fps
   streaming), `OnReady` can still expose the underlying `Canvas` control
   via `.Set()` for direct manipulation. This is the same pattern other
   Reactor elements use for WinUI escape hatches.

Deprecate `ChartHandle.Redraw()` in favor of state-driven re-renders.
Keep `OnReady` for the Canvas reference escape hatch.

---

## Migration Plan

### Phase 1: Line / Bar / Area charts (low risk)

1. Add `BuildElement()` method to `ChartElement<T>` that returns a
   `D3Canvas(...)` element tree using D3 DSL primitives
2. Change `ToElement()` to call `BuildElement()` instead of creating
   `XamlHostElement`
3. Remove `BuildCanvas()`, `UpdateCanvas()`, and all imperative
   `FullRender*()` methods
4. Add `.WithKey()` to data-driven shapes for positional diffing
5. Verify: ReactorCharting.Sample gallery still animates at 800ms intervals
6. Verify: doc app charts render identically

### Phase 2: PieChart (low risk)

1. Replace `PieChartElement<T>.FullRender()` with `D3Pie<T>()` call
2. Add label rendering via `D3Text` elements
3. Remove `XamlHostElement` usage

### Phase 3: TreeChart (medium risk)

1. Replace imperative `Visit()` + `canvas.Children.Add()` with
   `D3Link()` + `D3Circle()` + `D3Text()` element arrays
2. Use `TreeLayout` as before for position computation
3. Key tree nodes by depth + index for efficient diffing

### Phase 4: ForceGraph (keep as-is)

1. Leave `ForceGraphElement` using the reconciler's built-in
   `XamlHostElement` support
2. Document that force graphs use direct manipulation for performance
3. Consider Option B as a future enhancement if reconciler performance
   allows 60fps diffing

### Phase 5: Cleanup

1. Deprecate `ChartHandle<T>.Redraw()` — add `[Obsolete]` with message
   directing to state-driven re-renders
2. Remove `UpdateCanvas` methods from migrated chart types
3. Update ReactorCharting.Sample to use state-driven data updates
4. Update doc app charting examples if API surface changed

---

## Performance Considerations

**Element count.** A line chart with 100 points generates ~115 elements
(1 path + ~5 grid lines + ~11 axis ticks + ~11 axis labels). This is
well within Reactor's reconciler budget — NetPulse renders 700+ elements at
60fps with room to spare.

**Bar charts scale linearly.** 100 bars = ~150 elements (100 rects +
~50 axis/grid). Still fast. For 1000+ bars, consider a single `D3Path`
with all bar outlines in one SVG path string.

**Pie charts are small.** Typically 3-10 slices = 6-20 elements.

**Diffing cost.** Positional keying means the reconciler does O(n) checks
per render. For a 100-point line chart where only the path data changed,
only the `PathElement.PathDataString` comparison triggers an update — all
other elements (axes, grid) are `ShallowEquals` and skip.

**Path data dedup.** `UpdatePath` already compares `PathDataString` before
setting the expensive COM `p.Data` property. A re-render with unchanged
data costs essentially zero.

---

## What Does NOT Change

- `using static Microsoft.UI.Reactor.Charting.Charts;` import
- `LineChart<T>(data, x, y)` factory signature
- `.Width()`, `.Height()`, `.Margin()`, `.Stroke()`, `.Fill()` builder methods
- `.ShowAxes()`, `.ShowGrid()` configuration
- `PieChart<T>(data, value, label)` factory signature
- `.InnerRadius()`, `.PadAngle()`, `.SetColors()` configuration
- `TreeChart<T>(root, children, label)` factory signature
- `ForceGraph(nodes, links)` factory signature
- `implicit operator Element` conversion

The migration is internal to the chart element classes. No API breaking
changes.

---

## Testing

- [ ] ReactorCharting.Sample renders all 6 chart types identically
- [ ] ReactorCharting.Gallery 42 samples unaffected (they already use D3 DSL)
- [ ] Doc app charting screenshots match pre-migration output
- [ ] Animated data updates (ReactorCharting.Sample 800ms timer) still work
- [ ] ForceGraph drag interaction still works
- [ ] Theme switching updates chart colors (new capability)
- [ ] Dark mode: chart axes/grid adapt (new capability)
- [ ] ReactorCharting unit tests pass (257 tests)
- [ ] Reactor unit tests pass (3182 tests)
