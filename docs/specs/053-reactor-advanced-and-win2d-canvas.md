# Reactor.Advanced + Win2DCanvas — Design Proposal

## Status

**Proposed.** No code shipped yet. This spec defines:

1. A new **`src/Reactor.Advanced`** library that ships as a separate NuGet
   package (`Microsoft.UI.Reactor.Advanced`) and depends on both
   `Microsoft.UI.Reactor` and `Microsoft.Graphics.Win2D`.
2. The first inhabitant of that library: a family of **first-class Win2D
   canvas Reactor elements** (`Win2DCanvas`, `Win2DAnimatedCanvas`,
   `Win2DVirtualCanvas`) and the supporting hooks that make Win2D's
   frame-based drawing model feel native to Reactor's component model.
3. A sample app — **`samples/apps/particle-storm`** — that visibly
   demonstrates work that pure Reactor (retained WinUI) cannot do:
   ≥50,000 particles at 60 fps under live Reactor-driven physics
   controls.
4. The factoring rules that future "advanced" components (heavy native
   deps, optional features, controls that don't belong in core
   Reactor.dll) will follow when relocating into `Reactor.Advanced`.

This spec follows [spec 047](047-extensible-control-model.md) (V1 handler
protocol) and [spec 048](048-control-registration-and-trimming.md)
(lazy/trimmable Pattern-A registration). It does **not** propose any
runtime changes to Reactor core — the entire integration uses the
already-public V1 author surface that spec 048 §6 demonstrates with the
external `MarqueeControl` proof.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Goals and non-goals](#2-goals-and-non-goals)
- [§3 Why a new library — `Reactor.Advanced`](#3-why-a-new-library--reactoradvanced)
- [§4 Win2D in one screen — model overview](#4-win2d-in-one-screen--model-overview)
- [§5 Integration strategy](#5-integration-strategy)
- [§6 Public API surface — elements](#6-public-api-surface--elements)
- [§7 Public API surface — hooks](#7-public-api-surface--hooks)
- [§8 Threading, device loss, and lifetime](#8-threading-device-loss-and-lifetime)
- [§9 Echo, pooling, and value-prop discipline](#9-echo-pooling-and-value-prop-discipline)
- [§10 Trim and AOT story](#10-trim-and-aot-story)
- [§11 Project structure and packaging](#11-project-structure-and-packaging)
- [§12 Sample — Particle Storm](#12-sample--particle-storm)
- [§13 Future inhabitants of `Reactor.Advanced`](#13-future-inhabitants-of-reactoradvanced)
- [§14 Alternatives considered](#14-alternatives-considered)
- [§15 Open questions](#15-open-questions)
- [§16 Phasing and exit criteria](#16-phasing-and-exit-criteria)

---

## §1 Motivation

Reactor is great at building declarative WinUI 3 UIs that diff cheaply
and update precisely. It is bad — by design — at drawing 50,000
particles, a real-time spectrogram, an interactive Mandelbrot, or a
fluid simulation. Those workloads need a *retained → immediate-mode*
escape hatch with direct GPU access, and WinUI's blessed answer is
[Win2D](https://microsoft.github.io/Win2D/) (`Microsoft.Graphics.Win2D`
on Windows App SDK 1.2+).

Today an app author wanting Win2D inside a Reactor tree has two ugly
choices:

1. **`Reconciler.RegisterType` from inside the app** — wires a one-off
   `CanvasControl` element, copies the boilerplate from spec 048's
   Marquee proof, manages the `Draw` event and pooling discipline
   themselves. Works, but every app re-invents the wrapper and the
   correctness invariants of value-bearing props / echo suppression /
   pool-survival are easy to miss (see [spec 047 §3](047-extensible-control-model.md#3-what-the-engine-actually-does-for-a-built-in-control)).
2. **Embed a `CanvasControl` via `.Set()` on a host container** — a
   hatch through `BorderElement.Set(b => b.Child = new CanvasControl())`.
   Bypasses Reactor's reconciliation completely; loses pooling,
   attached-DP state, and any chance of Reactor lifecycle integration.

Both choices fail the "deeply integrated" bar. Win2D's frame model
(`Update(args) → Draw(session)` running 60 Hz on a Win2D-owned
background thread) is actually a *natural fit* for Reactor's
"functional-of-state" model: Reactor owns the state, Win2D owns the
pixels, the frame callback reads state and renders. We should make that
pairing first-class.

Concretely we want a new-user experience that looks like this:

```csharp
var (count, setCount) = UseState(20_000);

return VStack(
    Slider(0, 100_000, count, v => setCount((int)v)),
    Win2DAnimatedCanvas(
        onUpdate: (args, _) => _physics.Step(args.Timing.ElapsedTime, count),
        onDraw:   (session, _, _) => _physics.Render(session))
        .Stretch());
```

— no `Reconciler.RegisterType` ceremony, no pool-bypass, no missed
echo-suppress, no thread-affinity surprise.

### 1.1 Why this needs its own library

Reactor core (`Reactor.dll`) ships a deliberately tight surface: every
control type is reachable from `RegisterV1BuiltInHandlers()` and
therefore rooted by the trimmer. Adding Win2D would force every Reactor
app — including a 200 KB "TextBlock + Button" toy app — to drag in
`Microsoft.Graphics.Win2D.dll` and its native ANGLE / DirectX runtime
(~4 MB unpacked) whether they use it or not. That is the precise
scenario spec 048 §1 names as the "north star" anti-pattern.

A separate package keeps Win2D opt-in: an app that adds `<PackageReference
Include="Microsoft.UI.Reactor.Advanced" />` is *announcing* the dep, and
the trimmer gets a clean cut between Reactor core and Win2D-bearing
code.

The library also gives us a natural relocation target for the next
generation of "things that probably shouldn't be in `Reactor.dll`
anymore" — see §13.

---

## §2 Goals and non-goals

### Goals

1. **Deeply integrated Win2D component.** The component is a Reactor
   `Element` that the reconciler mounts/updates/unmounts like any other.
   Its frame callbacks see live Reactor state without round-tripping
   through DependencyProperty bindings.
2. **Three element shapes** that map 1:1 to Win2D's three canvas
   controls — `CanvasControl` (manual invalidate), `CanvasAnimatedControl`
   (steady-tick game loop), `CanvasVirtualControl` (tiled
   region-based redraw). Author picks the one that matches the workload.
3. **Hook bridge** that lets a component carry frame-only state
   (sprites, physics, particle buffers) that survives re-renders and is
   safe to access from Win2D's threading model.
4. **Pattern A registration** (spec 048 §6) — a static factory holder
   per element; the trimmer follows `Factory → cctor → handler →
   CanvasControl`. An app that never names the factory pays nothing.
5. **No core-Reactor changes required.** The entire integration is
   author-side. If this spec implementation file changes any file under
   `src/Reactor/`, that change is a bug to be fixed before merge.
6. **Sample that proves the point** — a Reactor app rendering 50k+
   moving particles at a stable 60 fps with live Reactor controls
   driving physics parameters.
7. **Library is reusable for future "advanced" components** — heavy
   native deps, niche controls, optional toolkits — that don't belong
   in Reactor core. The first inhabitant is Win2D; §13 names plausible
   next candidates.

### Non-goals

1. **Reactor-style scene graph for Win2D drawing.** A declarative
   `Scene(Circle(...), Path(...))` reconciled into Win2D commands is
   *the* dream API but it's a substantial chunk of work (its own
   reconciler, its own diff, its own batching), and the spec 042
   keyed-list-reconciliation infrastructure isn't generalizable to a
   drawing tree out of the box. We **explicitly carve it out** as L2
   future work and ship L0 + L1 first.
2. **Compositor effects pipeline.** Win2D exposes
   `CompositionEffectFactory` / `Effects.*` for shader composition; a
   first-class Reactor wrapping of effect graphs is interesting but
   orthogonal to canvas drawing. Future spec.
3. **Replacing Reactor.Charting.** D3 / SVG charting stays where it is.
   Win2D is an immediate-mode drawing surface, not a charting toolkit.
4. **Hot-path zero-allocation guarantees on the draw callback.** The
   author writes whatever drawing code they want; if they allocate per
   frame, that's their problem. We do not promise to police it.
5. **Win2D NuGet on non-Windows targets.** Reactor.Advanced is a
   `net10.0-windows10.0.22621.0` library, period.

---

## §3 Why a new library — `Reactor.Advanced`

Spec 048 §1 establishes the policy that built-in controls should not be
unconditionally rooted by the constructor. The proposed solution there
(Pattern A: factory-static-cctor + `ControlRegistry.Register`) makes
*every* registration lazy on a per-element basis. That handles the
trimming case, but it does not address a second-order problem: **what
about controls whose dependencies are themselves trim-heavy?**

`Microsoft.Graphics.Win2D` ships as:

- a managed assembly (`Microsoft.Graphics.Canvas.dll`, ~1 MB),
- a native interop dll (`Microsoft.Graphics.Canvas.dll` native, ~3 MB),
- and on Windows App SDK 1.2+ no longer requires VCRT side-by-side, but
  still pulls in DirectX 11 runtime references via the WinRT activatable
  class list. AOT publishing must emit roots for every Win2D type the
  trimmer can reach.

Even with perfect Pattern A discipline, putting Win2D inside
`Reactor.dll` means *the package* roots that native chain (the NuGet
content files for the native dll get copied into every consuming app).
The cleanest separation is: **a new NuGet package for Win2D-aware
content; consumers opt in by referencing it.**

### 3.1 Naming

Considered names:

| Name | Pro | Con |
|---|---|---|
| **`Reactor.Advanced`** | Open-ended; can host more than Win2D; matches user's working title. | Vague; doesn't tell you what's in the box. |
| `Reactor.Win2D` | Crystal-clear scope. | Excludes future inhabitants; we'd need yet another package later. |
| `Reactor.Controls.Win2D` | Follows the spec 047 §1.1 `Reactor.Controls.*` naming. | Implies a *family* of split-out controls, but the family is supposed to live in *core* per spec 048. |
| `Reactor.Graphics` | Captures the Win2D scope plus future GPU/effects work. | Doesn't fit non-graphics future inhabitants (e.g., a `WebView2` companion package). |

**Decision: `Reactor.Advanced`.** Matches the user's working title and
leaves the door open for the future inhabitants in §13. The NuGet ID is
`Microsoft.UI.Reactor.Advanced` for parity with the core package.

The `Win2D` content lives in the `Microsoft.UI.Reactor.Advanced.Win2D`
namespace so the package can grow other namespaces later
(`Microsoft.UI.Reactor.Advanced.Composition`,
`Microsoft.UI.Reactor.Advanced.Media`) without renaming the package.

### 3.2 Dependency direction is strict

```
Microsoft.UI.Reactor.Advanced (Reactor.Advanced.dll)
    │
    ├── ProjectReference ──→ src/Reactor/Reactor.csproj
    └── PackageReference ──→ Microsoft.Graphics.Win2D
```

`Reactor.dll` MUST NOT take a dep on `Reactor.Advanced.dll`. There is
no `InternalsVisibleTo` from Reactor into Advanced — Advanced is an
ordinary external consumer of the V1 protocol surface, the same way
`tests/external_proof/Reactor.External.TestControl` is. This is a
deliberate echo of spec 047 §14 Phase 1 exit gate item 2: the V1
surface must be *first-party quality* without internals leakage. If
Advanced ever needs an internal of Reactor's, that is a bug in
Reactor's public surface to be filed, not a workaround to be
shipped.

---

## §4 Win2D in one screen — model overview

Win2D presents three canvas controls; the integration surfaces one
Reactor element per Win2D control because each has a meaningfully
different programming model that an author *should* pick consciously.

| Win2D control | When you use it | Threading | Reactor element |
|---|---|---|---|
| **`CanvasControl`** | One-shot drawings or invalidate-on-data-change scenes (gauges, chart-like static visuals, paint apps). UI-thread `Draw`. | UI thread | `Win2DCanvas` |
| **`CanvasAnimatedControl`** | Game-loop style: steady tick. `Update(args)` then `Draw(session)` at a target frame rate. Update/Draw run on a Win2D-owned background thread (the "game thread") with its own message pump. | Game thread | `Win2DAnimatedCanvas` |
| **`CanvasVirtualControl`** | Very large or scrollable surfaces (whiteboards, image editors, multi-megapixel artboards). Win2D tiles the surface and fires `RegionsInvalidated` only for visible tiles. | UI thread | `Win2DVirtualCanvas` |

All three share:

- An async device-creation lifecycle (`CreateResources`) with
  device-lost recovery (GPU TDR / display-mode change).
- The same `CanvasDrawingSession` API for the actual drawing
  (`DrawLine`, `DrawCircle`, `FillRectangle`, `DrawImage`, …).
- The same Win2D resource family — `CanvasBitmap`, `CanvasGeometry`,
  `CanvasRenderTarget`, `CanvasSpriteBatch`, `ICanvasImage` effects.

The integration's job is to expose the *unique* part of each control
(when invalidation happens, where Draw runs) as a Reactor element
property, while sharing the *common* parts (resource creation, drawing
session API) through a single set of hooks (§7).

### 4.1 Frame model alignment

Reactor's render model: `state changes → Component.Render() → reconcile
→ patch WinUI tree`. The Win2D control sits in that tree, but its
*pixels* update on a different cadence:

- `Win2DCanvas`: pixels update when Reactor re-renders (we call
  `control.Invalidate()` once after each reconcile, gated on a redraw
  key — see §6.1).
- `Win2DAnimatedCanvas`: pixels update on the game-loop tick,
  independent of Reactor's re-render cadence. Reactor state changes are
  visible to the next `Update` callback as soon as the setter returns
  — there is *no* explicit "push state to Win2D" step because both
  sides read the same hook-backed buffer.
- `Win2DVirtualCanvas`: pixels update when WinUI tells Win2D a tile is
  visible (scrolling). Reactor state changes that affect existing tile
  contents flow through an explicit `control.Invalidate(rect)` call
  (we expose a `RedrawRegion` ref-style helper).

This is the alignment the §1 motivation referenced: Win2D's
frame-callbacks are pull-mode, Reactor's hook state is the same memory
they pull from, and the integration's job is to make that legible.

---

## §5 Integration strategy

The library ships *three layers*. L0 and L1 are in scope for the first
implementation; L2 is documented to anchor the design but explicitly
deferred.

### L0 — Three element records wrapping the three Win2D controls

`Win2DCanvas`, `Win2DAnimatedCanvas`, `Win2DVirtualCanvas`. Each is a
sealed `record` deriving from `Microsoft.UI.Reactor.Core.Element`,
with:

- A `Draw` (or `Update + Draw`, for animated) callback property.
- A `CreateResources` callback for async/device-lost-safe resource
  allocation.
- Element-level props that map to value-bearing Win2D DPs
  (`ClearColor`, `IsPaused`, `TargetElapsedTime`, `UseSharedDevice`,
  `DpiScale`).
- A `Setters` array for the rare property not pulled into the typed
  surface (matches the `MarqueeElement.Setters` convention).

A V1 `IElementHandler<TElement, TControl>` per element. Pattern A
registration through a public static holder class
(`Microsoft.UI.Reactor.Advanced.Win2D.Canvas`). The holder is the sole
construction entry point — element constructors are `internal` exactly
as in the Marquee proof, so the trimmer's reachability chain works
(spec 048 §6).

### L1 — Hooks that bridge Reactor state with Win2D frame callbacks

Three hooks live in `Microsoft.UI.Reactor.Advanced.Win2D.Hooks`:

- `UseDrawState<T>(Func<T> init)` — like `UseRef<T>` but the returned
  value is wrapped so the `Update`/`Draw` callbacks can read it from
  the Win2D background thread without a fence.
- `UseCanvasResources<TResources>(Func<CanvasDevice, ValueTask<TResources>> create, Action<TResources>? dispose = null)`
  — the device-lost-safe resource allocator. Survives re-renders;
  re-runs `create` on `CanvasDevice.DeviceLost`; runs `dispose` on
  unmount.
- `UseDrawCommand<TState>(TState state, Action<CanvasDrawingSession, TState> draw, object[] deps)`
  — a memoized draw delegate so the author doesn't rebuild a closure
  every render. Cheap to skip; useful when the draw delegate itself is
  big and shouldn't be recreated when only unrelated state moves.

The hooks are *opt-in*. A trivial Win2D scene that doesn't need
state-bridging just writes an inline `onDraw` lambda.

### L2 — Declarative Reactor scene graph (deferred, documented)

The dream API:

```csharp
Win2DScene(width: 800, height: 600,
    Circle(cx: 100, cy: 100, r: 50).Fill(Colors.Red),
    Path(geometry).Stroke(Colors.Black, thickness: 2),
    Sprite(_explosion, x: ParticleX, y: ParticleY, frame: tick % 16),
    Group(transform: Matrix3x2.CreateRotation(angle),
        Text("hello", x: 0, y: 0, font: _font)))
```

— a Reactor *element tree* describing the *drawing* itself. A custom
mini-reconciler maps the tree to a sequence of `DrawingSession`
operations, with batching, geometry caching, and structural sharing.
This is genuinely useful for medium-complexity scenes (chart-like,
diagram-like, low-thousands-of-shapes), but it is **not the same
problem as L0/L1** and shipping it on day one would balloon scope. We
ship L0/L1, prove the integration works, then come back to L2 in a
separate spec when there's demand evidence.

L0/L1 alone hit every motivating case in §1: 50k particles, fluid sim,
Mandelbrot, audio visualizer. All of those want immediate-mode drawing,
not a scene tree.

---

## §6 Public API surface — elements

All three elements live in `Microsoft.UI.Reactor.Advanced.Win2D` and
follow the spec 048 §6 Pattern A factory-holder shape.

### 6.1 `Win2DCanvas` — manual-invalidate canvas

```csharp
// src/Reactor.Advanced/Win2D/Win2DCanvasElement.cs
public sealed record Win2DCanvasElement : Element
{
    /// <summary>Synchronous draw callback. Runs on the UI thread.</summary>
    public Action<CanvasDrawingSession, CanvasDrawEventArgs>? OnDraw { get; init; }

    /// <summary>Async resource creation. Runs on a worker thread; the
    /// returned task is awaited by Win2D before the first Draw.</summary>
    public Func<CanvasControl, Task>? OnCreateResources { get; init; }

    /// <summary>Background fill. Defaults to <c>Colors.Transparent</c>.</summary>
    public Windows.UI.Color ClearColor { get; init; }
        = Microsoft.UI.Colors.Transparent;

    /// <summary>Opaque key that, when changed, triggers
    /// <c>CanvasControl.Invalidate()</c> on the next reconcile pass.
    /// Pass any state value the draw callback depends on; if you pass
    /// <c>null</c> the canvas only invalidates on size/dpi changes.</summary>
    public object? RedrawKey { get; init; }

    public Action<CanvasControl>[] Setters { get; init; }
        = Array.Empty<Action<CanvasControl>>();

    internal Win2DCanvasElement() { }
}

// src/Reactor.Advanced/Win2D/Win2DCanvas.cs (factory holder)
public static class Win2DCanvas
{
    static Win2DCanvas() => ControlRegistry.Register<Win2DCanvasElement, CanvasControl>(
        static () => new Win2DCanvasHandler());

    public static Win2DCanvasElement Of(
        Action<CanvasDrawingSession, CanvasDrawEventArgs> onDraw,
        object? redrawKey = null) =>
        new() { OnDraw = onDraw, RedrawKey = redrawKey };

    public static Win2DCanvasElement Of(
        Action<CanvasDrawingSession, CanvasDrawEventArgs> onDraw,
        Func<CanvasControl, Task> onCreateResources,
        object? redrawKey = null) =>
        new() { OnDraw = onDraw, OnCreateResources = onCreateResources, RedrawKey = redrawKey };
}
```

A `using static Microsoft.UI.Reactor.Advanced.Win2D.Canvas;` import
lets you write `Win2DCanvas(onDraw: ...)` flat in component bodies the
same way you write `Button(...)` today.

#### Why `RedrawKey` exists

`CanvasControl` does not redraw spontaneously — the user must call
`Invalidate()`. The integration handler watches the element's
`RedrawKey` in `Update`: if it changed from the prior element instance,
the handler calls `ctrl.Invalidate()`. This is the declarative shape
that lets an author write:

```csharp
var (count, _) = UseState(0);
return Win2DCanvas(
    onDraw: (s, _) => s.DrawText($"count = {count}", 10, 10, Colors.Black),
    redrawKey: count);
```

— and have the canvas redraw exactly when `count` changes, with no
imperative `Invalidate` call in component code.

If `RedrawKey` is null, the handler still invalidates on `SizeChanged`
and DPI changes (Win2D handles that itself), so a static drawing is
correct out of the box.

### 6.2 `Win2DAnimatedCanvas` — game-loop canvas

```csharp
// src/Reactor.Advanced/Win2D/Win2DAnimatedCanvasElement.cs
public sealed record Win2DAnimatedCanvasElement : Element
{
    /// <summary>Per-tick update. Runs on the Win2D game thread; do
    /// not touch UI controls here. The second arg is the per-element
    /// <c>UseDrawState</c> bundle if the author provided one via the
    /// factory overload; otherwise it is <c>null</c>.</summary>
    public Action<CanvasAnimatedUpdateEventArgs, object?>? OnUpdate { get; init; }

    /// <summary>Per-frame draw. Runs on the Win2D game thread.</summary>
    public Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?>? OnDraw { get; init; }

    public Func<CanvasAnimatedControl, Task>? OnCreateResources { get; init; }

    /// <summary>Game-loop tick interval. Default 60 fps
    /// (16.67ms). Setting <see cref="TimeSpan.Zero"/> means
    /// "as fast as possible".</summary>
    public TimeSpan TargetElapsedTime { get; init; }
        = TimeSpan.FromTicks(166_667);

    /// <summary>Stop the game loop without unmounting the canvas.
    /// Toggling <c>true → false</c> resumes from the same logical time.</summary>
    public bool IsPaused { get; init; }

    /// <summary>Optional state bundle passed through to <c>OnUpdate</c>
    /// and <c>OnDraw</c>. Authors pass a <see cref="Ref{T}"/> obtained
    /// from <see cref="UseDrawState"/> when they need mutable per-canvas
    /// state across frames.</summary>
    public object? DrawState { get; init; }

    public Windows.UI.Color ClearColor { get; init; }
        = Microsoft.UI.Colors.Transparent;

    public Action<CanvasAnimatedControl>[] Setters { get; init; }
        = Array.Empty<Action<CanvasAnimatedControl>>();

    internal Win2DAnimatedCanvasElement() { }
}

public static class Win2DAnimatedCanvas
{
    static Win2DAnimatedCanvas() => ControlRegistry.Register<
        Win2DAnimatedCanvasElement, CanvasAnimatedControl>(
        static () => new Win2DAnimatedCanvasHandler());

    public static Win2DAnimatedCanvasElement Of(
        Action<CanvasAnimatedUpdateEventArgs, object?> onUpdate,
        Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?> onDraw,
        object? drawState = null,
        bool isPaused = false) =>
        new() { OnUpdate = onUpdate, OnDraw = onDraw,
                DrawState = drawState, IsPaused = isPaused };
}
```

Notes:

- `IsPaused` and `TargetElapsedTime` are value-bearing — the handler
  uses `BindFor(ctrl, newEl).WriteSuppressed(...)` per spec 048's
  echo-suppression rules.
- `OnUpdate`/`OnDraw` capture *what was passed at the most recent
  reconcile*. To survive re-renders the handler stores them on the
  element-tag refresh, exactly like the Marquee `OnCaptionChanged`
  callback in spec 048's proof — closures do not capture `el`.
- The `DrawState` parameter is opaque to Reactor. Authors typically
  pass `_state.Current` (a `Ref<T>` obtained from `UseRef` or the new
  `UseDrawState`); the callback unboxes it. We could make the signature
  generic — `OnUpdate<TState>` — but doing so per element type pollutes
  the registration story (open generic element types are explicitly
  unsupported per spec 047 §2.1). The `object?` parameter is the
  pragmatic shape.

### 6.3 `Win2DVirtualCanvas` — tiled, region-invalidated canvas

```csharp
public sealed record Win2DVirtualCanvasElement : Element
{
    /// <summary>Per-region draw. Win2D calls this for each invalidated
    /// region during a frame. <c>region</c> is the rect (in DIPs) the
    /// author should fill; drawing outside it is fine but wasted.</summary>
    public Action<CanvasDrawingSession, Rect>? OnRegionDraw { get; init; }

    public Func<CanvasVirtualControl, Task>? OnCreateResources { get; init; }

    /// <summary>Logical content size in DIPs. The canvas reports this
    /// size to its parent ScrollViewer.</summary>
    public Size ContentSize { get; init; } = new(800, 600);

    /// <summary>Optional invalidation request. When this object instance
    /// changes, the handler calls <c>ctrl.Invalidate(rect)</c> for each
    /// rect in the new value. Use to force a redraw of a tile.</summary>
    public IReadOnlyList<Rect>? InvalidateRegions { get; init; }

    public Action<CanvasVirtualControl>[] Setters { get; init; }
        = Array.Empty<Action<CanvasVirtualControl>>();

    internal Win2DVirtualCanvasElement() { }
}

public static class Win2DVirtualCanvas { /* same shape as above */ }
```

The `InvalidateRegions` shape mirrors `RedrawKey` from §6.1 — pass a
new instance to trigger work, pass the same instance to skip.

### 6.4 Common modifier extensions

```csharp
// src/Reactor.Advanced/Win2D/Win2DCanvasModifiers.cs
public static class Win2DCanvasModifiers
{
    public static TElement ClearColor<TElement>(this TElement el, Windows.UI.Color color)
        where TElement : Element /* with ClearColor init prop */
        => el with { ClearColor = color };

    public static Win2DAnimatedCanvasElement Paused(this Win2DAnimatedCanvasElement el, bool paused = true)
        => el with { IsPaused = paused };

    public static Win2DAnimatedCanvasElement TargetFps(this Win2DAnimatedCanvasElement el, double fps)
        => el with { TargetElapsedTime = TimeSpan.FromSeconds(1.0 / fps) };

    public static TElement Set<TElement, TControl>(this TElement el, Action<TControl> setter)
        where TElement : Element where TControl : CanvasControl
        => el with { Setters = [.. el.Setters, setter] };
}
```

These follow the existing Reactor `ElementExtensions` style — fluent,
type-preserving, copy-via-`with`.

---

## §7 Public API surface — hooks

The three hooks listed in §5 (L1). Each lives in
`Microsoft.UI.Reactor.Advanced.Win2D.Hooks` and is a `static`
extension on `RenderContext` to match the existing hook idiom
(`ctx.UseState`, `ctx.UseEffect`).

### 7.1 `UseDrawState<T>`

```csharp
public static Ref<T> UseDrawState<T>(this RenderContext ctx, Func<T> init)
    where T : class
{
    // The init function runs once on first render; the ref survives
    // re-renders and is disposed (if T : IDisposable) on unmount.
    // The returned Ref<T>.Current can be read and written from the
    // Win2D game thread; T must be thread-safe for the access pattern
    // the author uses (typically a custom Particle/Physics/SpriteSheet
    // class with its own locking).
    return ctx.UseRef(init);
}
```

This is a *thin* wrapper over the existing `UseRef`. Its job is mostly
**documentation and discoverability**: developers reading
`Win2DAnimatedCanvas(drawState: state)` should be able to grep for
`UseDrawState` and find the matching producer. It also gives us a
single chokepoint to add cross-thread instrumentation later (frame-rate
guards, race-condition warnings under debug).

### 7.2 `UseCanvasResources<TResources>`

```csharp
public static Ref<TResources?> UseCanvasResources<TResources>(
    this RenderContext ctx,
    Func<CanvasDevice, ValueTask<TResources>> create,
    Action<TResources>? dispose = null)
    where TResources : class
{
    // On mount: schedules the create() to run when the host canvas
    //     control's CreateResources event fires.
    // On device-lost (Win2D's CanvasDevice.DeviceLost event): re-runs
    //     create() with a fresh device.
    // On unmount: runs dispose() if provided; otherwise calls
    //     IDisposable.Dispose on TResources if it implements it.
    // ...
}
```

The hook's implementation lives in `Reactor.Advanced` and uses the
public V1 surface — no internals required. Internally it stores a
small state machine on the component's hook list (via `UseRef` +
`UseEffect`) and wires itself to the *next-mounted* Win2D canvas in
the subtree by passing a sentinel down through `Context<T>`.

The contract for authors: **resources returned from `create` are valid
during `Draw` callbacks and must be re-acquirable on device loss.**
This matches Win2D's documented resource lifetime exactly.

### 7.3 `UseDrawCommand<TState>`

```csharp
public static Action<CanvasDrawingSession, CanvasDrawEventArgs> UseDrawCommand<TState>(
    this RenderContext ctx,
    TState state,
    Action<CanvasDrawingSession, CanvasDrawEventArgs, TState> draw,
    object[] deps);
```

Memoized; rebuilds only when `deps` change. Returns a
`Action<CanvasDrawingSession, CanvasDrawEventArgs>` ready to hand to
`Win2DCanvas.Of(onDraw: ...)`. Saves a closure allocation on hot
re-render paths and acts as the integration's answer to React's
`useCallback`.

---

## §8 Threading, device loss, and lifetime

This is where the integration earns its keep — the rules are the same
ones every Win2D dev internalizes, but Reactor authors are not
typically Win2D devs and the documentation/types should encode them.

### 8.1 Threading

| Callback | Thread |
|---|---|
| `Win2DCanvas.OnDraw` | UI thread. Touching Reactor state from inside is safe. |
| `Win2DCanvas.OnCreateResources` | Worker thread (Win2D-managed). |
| `Win2DAnimatedCanvas.OnUpdate` / `.OnDraw` | **Win2D game thread.** Reading hook state via `Ref<T>.Current` is safe iff `T` is itself thread-safe. Touching WinUI controls is **not** safe. |
| `Win2DAnimatedCanvas.OnCreateResources` | Game thread (before the loop starts). |
| `Win2DVirtualCanvas.OnRegionDraw` | UI thread. |
| `UseCanvasResources` `create` | Worker / game thread per host. |

The animated control's threading is the trap. Mitigations:

1. **`UseDrawState<T>` returns a typed `Ref<T>` whose `Current` getter
   is a plain field read** — no lock, no IDisposable, no
   thread-checked accessor. The author owns thread-safety of `T`. The
   XML doc explicitly says "treat `Current` like a `volatile` field;
   if you mutate `T` from the game thread, write `T` to be
   thread-safe."
2. **Debug-build sentinel.** In debug, `Win2DAnimatedCanvasHandler`
   wraps the user's `OnUpdate` / `OnDraw` in a `try/catch` that catches
   `InvalidOperationException` with the WinUI affinity message and
   appends a hint pointing at this section of the spec.
3. **No automatic dispatcher hop.** We deliberately do not marshal the
   callbacks back to the UI thread; the whole point of the animated
   loop is to *not* go through the UI thread. Authors who want to
   write back to UI state from the loop use the existing
   `UseState(threadSafe: true)` setter, which Reactor already
   marshals.

### 8.2 Device loss

Every `CanvasDevice` can be lost (GPU TDR, monitor unplugged, driver
update). Win2D raises `DeviceLost`; resources must be re-created
against the new device. The integration handles this for the *typed*
resource hook (`UseCanvasResources`) automatically — it subscribes to
`device.DeviceLost`, disposes the prior resources, awaits a fresh
`create()`, and then signals the parent canvas to re-draw.

For ad-hoc resources allocated inside `OnDraw`, the author is
responsible. The XML doc on the element types links to Win2D's
official device-loss documentation.

### 8.3 Lifetime / pool

`CanvasControl` and friends are **not** in Reactor's built-in
`ElementPool` (the pool only knows the core control types per spec
047). The handler's `Mount` calls `ctx.RentControl<CanvasControl>()`
anyway — that path is documented to fall through to `new
CanvasControl()` for non-poolable types, and is the correct way to
write a handler that doesn't have to know whether its control is
poolable (Marquee proof comment, `MarqueeHandler.cs:36-41`).

Unmount disposes the underlying `CanvasControl` via
`ctx.ReturnControl`. Win2D's resource disposal cascade fires
automatically when the control is GC'd, but the integration
proactively calls `ctrl.RemoveFromVisualTree()` to break the parent
reference and let Win2D release native resources eagerly.

---

## §9 Echo, pooling, and value-prop discipline

Per spec 047 §8.3 (the hybrid echo model) and spec 048 §6 Pattern A
requirements:

- `ClearColor`, `IsPaused`, `TargetElapsedTime` are value-bearing
  props. The `Update` handler diffs `oldEl.X != newEl.X` and only then
  writes through `BindFor(ctrl, newEl).WriteSuppressed(...)`. This is
  identical to `MarqueeHandler.Update` and is the discipline the
  Marquee proof's repo memory ("Descriptor controlled-prop Update must
  suppress-write ONLY on real control drift") covers.
- `RedrawKey` and `InvalidateRegions` are *commands*, not state. They
  trigger an `Invalidate()` / `Invalidate(rect)` call when changed;
  there is no read-back to echo. No suppression needed.
- `OnDraw` / `OnUpdate` / `OnCreateResources` are callbacks. They
  attach to the WinUI events `Draw`, `Update`, `CreateResources`
  through `bind.OnCustomEvent<...>` — single-subscribe, trampoline-
  refreshed via the element-tag-refresh trick (Marquee proof line 56).
  Re-renders that change only the callback identity simply update the
  tag; no resubscription, no leak.

The `Setters` array runs *after* the typed prop writes inside an
`ApplySetters` scope. Per spec 047 §8.2 the scope suppresses any
spurious change events the setters might raise — same default behavior
as built-in controls.

---

## §10 Trim and AOT story

The library must publish with `PublishTrimmed=true` and
`IsAotCompatible=true` and produce **zero** new trim/AOT warnings
beyond what `Microsoft.Graphics.Win2D` itself reports. This matches
the Marquee external proof's `.csproj`:

```xml
<PublishTrimmed>true</PublishTrimmed>
<IsAotCompatible>true</IsAotCompatible>
```

The trim chain is identical to the Marquee one:

```
App calls Win2DAnimatedCanvas.Of(...)
   → JIT roots Win2DAnimatedCanvas (factory holder)
   → cctor runs ControlRegistry.Register<E, C>(static () => new Handler())
   → trimmer keeps Win2DAnimatedCanvasElement, Win2DAnimatedCanvasHandler, CanvasAnimatedControl
   → CanvasAnimatedControl pulls in the Win2D native interop
   → all reachable.

App that never calls Win2DAnimatedCanvas.Of(...)
   → factory holder unreached
   → cctor never runs
   → handler unreached
   → CanvasAnimatedControl unrooted by Reactor
   → trimmer can drop it (only the Win2D bits the app uses directly survive).
```

This is exactly the property spec 048 §1 names as the north star.

**Caveat:** `Microsoft.Graphics.Win2D` itself ships native assets that
NuGet's `runtimes/` mechanism unconditionally copies into the
app-output. Trimming managed code doesn't shrink the native payload.
This is a known Win2D-side limitation, not a Reactor.Advanced one, and
documented as such in the spec's open-questions §15.

---

## §11 Project structure and packaging

### 11.1 Project layout

```
src/Reactor.Advanced/
    Reactor.Advanced.csproj
    Win2D/
        Win2DCanvas.cs                       — factory holder
        Win2DCanvasElement.cs                — element record
        Win2DCanvasHandler.cs                — IElementHandler
        Win2DAnimatedCanvas.cs               — factory holder
        Win2DAnimatedCanvasElement.cs        — element record
        Win2DAnimatedCanvasHandler.cs        — IElementHandler
        Win2DVirtualCanvas.cs                — factory holder
        Win2DVirtualCanvasElement.cs         — element record
        Win2DVirtualCanvasHandler.cs         — IElementHandler
        Win2DCanvasModifiers.cs              — fluent ext methods
        Hooks/
            UseDrawState.cs
            UseCanvasResources.cs
            UseDrawCommand.cs
    ReactorAdvancedAssemblyInfo.cs           — assembly metadata + XML doc xmlns
    README.md                                — package landing copy
```

### 11.2 `Reactor.Advanced.csproj` shape

Cribs from `Reactor.External.TestControl.csproj` for the external-
consumer discipline, and from `Reactor.csproj` for the pack settings:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <RootNamespace>Microsoft.UI.Reactor.Advanced</RootNamespace>
    <AssemblyName>Reactor.Advanced</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWinUI>true</UseWinUI>
    <Platforms>AnyCPU;x64;ARM64</Platforms>
    <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>

    <PublishTrimmed>true</PublishTrimmed>
    <IsAotCompatible>true</IsAotCompatible>

    <IsPackable>true</IsPackable>
    <PackageId>Microsoft.UI.Reactor.Advanced</PackageId>
    <Version Condition="'$(Version)' == ''">0.0.0-local</Version>
    <Description>Advanced Reactor components — first inhabitant: Win2D canvas.</Description>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="$(WindowsAppSDKVersion)" />
    <PackageReference Include="Microsoft.Graphics.Win2D" Version="$(Win2DVersion)" />
  </ItemGroup>

  <ItemGroup>
    <!-- NOT InternalsVisibleTo — Reactor.dll's public surface alone
         must be sufficient. Mirrors the external-proof discipline. -->
    <ProjectReference Include="..\Reactor\Reactor.csproj" />
  </ItemGroup>
</Project>
```

`$(Win2DVersion)` is added to `Directory.Build.props` next to the
existing `$(WindowsAppSDKVersion)` so all packages pin the same Win2D
version. Latest stable as of writing is `1.3.0` (Microsoft.Graphics.Win2D
on NuGet).

### 11.3 Solution registration

Add to `Reactor.slnx`:

- `<Project Path="src/Reactor.Advanced/Reactor.Advanced.csproj" />` at
  the top level (next to `src/Reactor/Reactor.csproj`).
- Sample project entry under `/samples/apps/particle-storm/` (see §12).
- Test project `tests/Reactor.Advanced.Tests/Reactor.Advanced.Tests.csproj`
  for the L0 selftests (see §16).

### 11.4 NuGet packaging

`mur pack-local` (the repo's local-pack command) builds
`Reactor.dll` and emits `Microsoft.UI.Reactor.0.0.0-local.nupkg` into
`local-nupkgs/`. We add Reactor.Advanced to the same pack workflow so
`Microsoft.UI.Reactor.Advanced.0.0.0-local.nupkg` lands beside it.
Apps in the ReactorDemo workspace (e.g. `dryrun`) consume both via
`<PackageReference Include="Microsoft.UI.Reactor.Advanced"
Version="0.0.0-local" />` driven by the existing `nuget.config`
local-feed mapping.

The release-workflow `version` parameter applies to both packages
identically (lock-stepped versioning).

### 11.5 Agent kit content

The plan packs a focused `reactor-advanced.md` skill into the NuGet
under `agentkit/skills/` so Copilot CLI users get Win2D guidance
without having to load a separate plugin. Skill includes: the three
element shapes, the threading rules from §8, and the Particle Storm
sample as a recipe. Mirrors what `Reactor.dll` already does for the
`reactor-charts`/`reactor-design`/etc. skills.

---

## §12 Sample — Particle Storm

`samples/apps/particle-storm/` is a single-window WinUI 3 app whose
purpose is to demonstrate **work that pure Reactor cannot do, with
Reactor still in charge of the UI**.

### 12.1 What it does

- A `CanvasAnimatedControl` rendering **50,000 particles** in 2D,
  driven by a simple gravitational N-body approximation (each particle
  attracted to the cursor + a global drag). Renders at a steady
  60 fps on an x64 dev laptop, ARM64-native target.
- Particle positions are stored in a `Particle[]` flat buffer (struct
  of `(float x, float y, float vx, float vy, byte hue)`) — no per-
  particle GC churn.
- The draw call uses `CanvasSpriteBatch` to issue all 50k particle
  sprites in one batched call, which is the Win2D feature that makes
  the perf possible.

### 12.2 Reactor chrome

The sample's UI chrome is pure Reactor — no Win2D except for the
canvas itself:

- A `Sidebar` panel with:
  - `Slider` for particle count (1k → 100k) — live, takes effect on
    next frame.
  - `Slider` for gravity strength.
  - `Slider` for drag coefficient.
  - `ComboBox` for color palette (4 presets).
  - `ToggleSwitch` for pause / resume.
  - `Button` to "Burst" (spawn 1000 particles at cursor).
  - Live `Text` block showing measured fps + particle count.
- A main content area filling the rest of the window with the
  `Win2DAnimatedCanvas` stretched to fit.

```csharp
public override Element Render()
{
    var state = ctx.UseDrawState(() => new ParticleField(initialCount: 20_000));
    var (count, setCount)     = ctx.UseState(20_000);
    var (gravity, setGravity) = ctx.UseState(1.0);
    var (drag, setDrag)       = ctx.UseState(0.02);
    var (paused, setPaused)   = ctx.UseState(false);
    var (palette, setPalette) = ctx.UseState(Palette.Galaxy);
    var fps = ctx.UseRef(0.0);

    return Grid(columns: ["280", "*"], rows: ["*"],
        Sidebar(count, setCount, gravity, setGravity, drag, setDrag,
                paused, setPaused, palette, setPalette, fps),

        Win2DAnimatedCanvas(
            onUpdate: (args, s) => ((ParticleField)s!)
                .Step(args.Timing.ElapsedTime,
                      count, gravity, drag),
            onDraw: (session, args, s) => {
                fps.Current = 1.0 / args.Timing.ElapsedTime.TotalSeconds;
                ((ParticleField)s!).Render(session, palette);
            },
            drawState: state,
            isPaused: paused)
        .Grid(col: 1)
        .ClearColor(Colors.Black));
}
```

This is the *whole* component body. The interesting part — the
reactive coupling between Reactor state (`count`, `gravity`, `drag`,
`palette`) and Win2D's frame loop — is made through the `drawState`
ref plus closures-over-locals. No bespoke wiring.

### 12.3 Why this sample

It demonstrates, in one screen:

1. **Volume Reactor cannot match.** 50k particles at 60 fps via WinUI
   shapes is impossible (and ItemsRepeater is the wrong tool for
   per-frame motion); via WinUI compositor it requires hand-rolled
   `Visual` management and falls apart at this scale.
2. **Reactive coupling.** A Reactor slider drag changes physics on the
   next Win2D frame with no event-wiring boilerplate.
3. **Thread-aware state.** `ParticleField.Step` runs on the Win2D game
   thread; `ParticleField.Render` runs on the same thread; the Reactor
   sliders write `count`/`gravity`/`drag` from the UI thread; the
   coupling is correct because the values flow through `useState`'s
   thread-safe field reads and `useDrawState`'s `Ref<T>.Current`. The
   sample includes a brief comment block calling this out as a learning
   moment.
4. **Native sample for `dryrun`-style demo workflows.** Slots cleanly
   into the existing `samples/apps/*/` layout.

### 12.4 Packaging

The sample app `.csproj` follows the existing pattern in
`samples/apps/netpulse/NetPulse.csproj`: `WinExe`,
`<WindowsPackageType>None</WindowsPackageType>`, `x64;ARM64`
platforms, one `ProjectReference` to `src/Reactor.Advanced/` (which
transitively brings in `src/Reactor/`).

A `samples/apps/particle-storm/README.md` walks new users through the
sample's structure and links back to this spec for the conceptual
overview.

---

## §13 Future inhabitants of `Reactor.Advanced`

The library is named for future-proofing. Plausible next inhabitants,
in rough order of likelihood:

| Candidate | Why it belongs in Advanced (not core) |
|---|---|
| **`Reactor.Advanced.Composition`** — fluent wrappers over `Microsoft.UI.Composition` (effects pipeline, custom spring animations, ShellBackdropController, hand-rolled visuals). | Hooks into spec 014 (animation design) but pulls in DirectX-flavored APIs that not every app should pay for. |
| **`Reactor.Advanced.Media`** — `MediaElement`/`MediaPlayer` ergonomic wrappers, audio capture, camera capture. | `Windows.Media.*` is large; only media apps want the surface. |
| **Move-out: `Reactor.Charting`** *(if ever)* — the D3-style charting subsystem currently in `src/Reactor` could relocate to `Reactor.Advanced.Charting`. | Spec 047 §1.1 anticipates exactly this kind of split-out. Not a day-one goal; included to demonstrate that Advanced is the *destination* for that work. |
| **`Reactor.Advanced.Inking`** — `InkCanvas` / `InkToolbar` ergonomic wrapper. Spec 002 flags both as missing today. | Niche workload; native Ink subsystem is non-trivial. |
| **`Reactor.Advanced.Maps`** — `MapControl` ergonomic wrapper (already exposed in core today, but a richer typed surface for layers / pins / pushpins is a natural Advanced citizen). | Heavy `Windows.Services.Maps` dependency. |

Each future inhabitant follows the same rules:

1. Lives in its own folder under `src/Reactor.Advanced/<Topic>/`.
2. Pattern A registration (factory holder + static cctor + `static
   () => new Handler()` lambda).
3. Trim/AOT clean as a separate gate.
4. Ships in the *same* `Microsoft.UI.Reactor.Advanced` NuGet — we do
   **not** explode into one package per topic. A consumer wanting just
   Win2D and a consumer wanting just Composition both reach for the
   same package, and Pattern A ensures the unused inhabitant trims
   away.

If a particular topic ever grows large enough to justify its own
package (e.g., Media at 5+ MB native asset), we split — but the default
is "everything lives in `Reactor.Advanced` until proven otherwise."

---

## §14 Alternatives considered

### 14.1 Put `Win2DCanvas` directly in `Reactor.dll`

**Rejected.** Spec 048's north star is that core Reactor should not
unconditionally root heavy native deps. Adding Win2D to core would
forces every consumer to ship 4 MB of native interop.

### 14.2 Ship Win2D wrapper as a recipe in the docs, not as a library

**Rejected.** The wrapper has enough subtle correctness invariants
(echo suppression on `IsPaused`/`TargetElapsedTime`, callback-refresh
on element-tag, device-lost recovery in `UseCanvasResources`, debug-
build thread-affinity sentinel in §8.1) that *every* re-implementation
will reinvent the bugs. The library exists to amortize that
discipline across consumers.

### 14.3 Layer 2 (declarative `Scene` tree) on day one

**Rejected as too ambitious for the first cut.** L2 needs its own
mini-reconciler with geometry caching, structural sharing, and a
keyed-diff story that doesn't fit the current `ChildReconciler` shape
(that reconciler writes into a `UIElementCollection`; drawing commands
have no such collection). The L0/L1 surface already hits every
motivating use case in §1. L2 gets its own spec when there's demand
evidence.

### 14.4 One element type with a `Mode = Manual | Animated | Virtual`
enum

**Rejected.** The three controls have *substantively* different
contracts — different threading, different events, different APIs.
Collapsing them into one element record means every author has to
learn that `OnDraw`'s thread depends on `Mode`, which `Mode = Virtual`
requires the otherwise-unused `OnRegionDraw`, and which props are
ignored under which mode. Three element types is more code in the
library but a much simpler mental model for authors. The
factory-holder pattern gives us trim-isolation per element, so the
unused two cost zero in a published app.

### 14.5 Generic `Win2DAnimatedCanvas<TState>` over `DrawState`

**Rejected.** Open-generic element types are an anti-pattern under
spec 047 §2.1 (`No open generics`). Closed-generic specializations
would force the consumer to reach for `TemplatedListElementBase`-style
intermediate base registrations (spec 047 Phase 3 close-out batch G2),
which is a lot of machinery for what is, at worst, a single
boxing-cast of a `Ref<TState>` to `object`. The `object?` parameter
shape in §6.2 is the right tradeoff.

### 14.6 `UseFrame(() => { ... })` — Reactor-driven render loop

A hook that schedules a callback to fire every frame via
`CompositionTarget.Rendering`. Tempting because it sounds like a unified
animation primitive, but it sidesteps the Win2D integration entirely —
the callback runs on the UI thread and can't access Win2D's
`CanvasDrawingSession`. **Rejected as orthogonal.** A future
`Reactor.Advanced.Composition` spec could ship `UseFrame` as part of a
compositor-effects toolkit; it doesn't belong in the Win2D surface.

---

## §15 Open questions

1. **Win2D NuGet native asset trimming.** `Microsoft.Graphics.Win2D`
   copies ~3 MB of native binaries via `runtimes/win-x64/native/` etc.
   regardless of which managed APIs the app uses. Investigate whether
   we can ship a *subset* MSBuild item filter (or document a manual
   one) for apps using only the canvas controls. May be out of our
   hands.
2. **Hot-reload behavior for `OnDraw` closures.** Reactor's hot-reload
   re-runs `Render()` and the handler updates the element tag with the
   new callback. For `Win2DCanvas` (manual invalidate) this works
   trivially — the next reconcile triggers Invalidate, the next Draw
   fires the new closure. For `Win2DAnimatedCanvas` the *next tick*
   picks up the new closure. We should verify the experience and add a
   note to the hot-reload guide.
3. **Multi-canvas device sharing.** Win2D apps with multiple canvas
   controls often share a single `CanvasDevice` via the
   `UseSharedDevice` DP. Should `UseCanvasResources` automatically opt
   into shared-device when called from a parent component above
   multiple canvases? Probably yes, but the implementation has subtle
   ordering concerns. Defer to implementation PR.
4. **Sample under AOT publish.** Verify Particle Storm publishes
   AOT-clean. Spec 048 §1 and prior AOT spec hooks expect this; Win2D
   has documented AOT compatibility from 1.2.0 forward, so the
   expectation is "should just work" — but we treat the publish gate
   as a Phase exit criterion (§16) rather than an assumption.
5. **`Reactor.Advanced.Tests` testing model.** Unit tests for L0
   element handlers fit the existing `Reactor.Tests` xUnit pattern
   (no live window required for prop-write / event-wire tests). For
   selftests of the actual `CanvasControl` mounting under WinUI, we
   need a host fixture under
   `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Win2D*` — adding
   Win2D to the test host pulls Win2D into every selftest run, which
   is acceptable but worth calling out. Decision left to
   implementation PR.

---

## §16 Phasing and exit criteria

### Phase 1 — Library + minimum-viable Win2D integration

- New project `src/Reactor.Advanced/Reactor.Advanced.csproj`.
- `Win2DCanvasElement` + `Win2DCanvasHandler` + `Win2DCanvas` factory
  holder.
- `Win2DAnimatedCanvasElement` + handler + factory.
- `Win2DVirtualCanvasElement` + handler + factory.
- Fluent modifiers (`Win2DCanvasModifiers`).
- The three hooks (`UseDrawState`, `UseCanvasResources`,
  `UseDrawCommand`).
- Unit tests in `tests/Reactor.Advanced.Tests/` covering: element
  records' construction discipline (internal ctor + factory-only
  entry), handler prop-diff behavior, RedrawKey-triggers-Invalidate,
  IsPaused/TargetElapsedTime suppression-on-update.
- `mur pack-local` produces `Microsoft.UI.Reactor.Advanced.0.0.0-
  local.nupkg`.

**Exit gate Phase 1:**

1. Library compiles with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
   (or repo equivalent).
2. `PublishTrimmed=true` + `IsAotCompatible=true` produce **zero new**
   trim/AOT warnings beyond what Win2D itself reports.
3. Unit tests green.
4. A throwaway smoke app referencing `Microsoft.UI.Reactor.Advanced
   0.0.0-local` from the local feed compiles, renders, and demonstrates
   each of the three canvas types working.

### Phase 2 — Particle Storm sample

- `samples/apps/particle-storm/` project, app, `ParticleField`
  implementation, `Sidebar` component, color palettes.
- README walking through what's reactive, what's immediate-mode, and
  the threading model.
- Slotted into `Reactor.slnx`.

**Exit gate Phase 2:**

1. App runs on x64 dev laptop and sustains ≥60 fps with 50,000
   particles (measure with the app's own fps counter; document the
   machine baseline in the README the same way perf benches do).
2. Live slider drag for particle count produces immediate visible
   change (next frame).
3. App publishes AOT-clean on x64.

### Phase 3 — Docs + selftest fixtures

- New user guide page `docs/guide/win2d-canvas.md` (compiled from
  `docs/_pipeline/templates/win2d-canvas.md.dt` per the repo
  documentation convention).
- Selftest fixtures `Win2D_Canvas_Mount`, `Win2D_AnimatedCanvas_Mount`,
  `Win2D_VirtualCanvas_Mount` in
  `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` (and the manual
  two-place registration in `SelfTestFixtureRegistry.cs`, per repo
  convention).
- `reactor-advanced.md` agent skill bundled into both NuGet packages'
  `agentkit/skills/` directories.

**Exit gate Phase 3:**

1. Selftests green: each canvas mounts, takes a callback, raises one
   draw, unmounts cleanly.
2. Guide page renders correctly under `mur docs compile`.
3. CI pipeline includes `Reactor.Advanced.Tests` and the new selftest
   fixtures; baseline run time within +10 % of prior CI.

### Phase 4 — Tasks and follow-ups (post-merge)

- Open the L2 declarative scene-graph spec under `docs/specs/proposals/`
  if developer feedback requests it.
- Open separate specs for any candidate inhabitant in §13 as demand
  surfaces.
- Investigate the Win2D native-asset trim story (§15 Q1) with the
  WindowsAppSDK team.

---

## Appendix A — Sample full code sketch for the sample's `App.cs`

```csharp
// samples/apps/particle-storm/App.cs
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Advanced.Win2D.Hooks;
using static Microsoft.UI.Reactor.Factories;

namespace ParticleStorm;

public sealed class App : Component
{
    public override Element Render()
    {
        var field   = ctx.UseDrawState(() => new ParticleField(20_000));
        var (n,  setN)  = ctx.UseState(20_000);
        var (g,  setG)  = ctx.UseState(1.0);
        var (d,  setD)  = ctx.UseState(0.02);
        var (paused, setPaused) = ctx.UseState(false);
        var (palette, setPalette) = ctx.UseState(Palette.Galaxy);
        var fps = ctx.UseRef(0.0);

        return Grid(columns: ["280", "*"], rows: ["*"],
            Sidebar(n, setN, g, setG, d, setD, paused, setPaused, palette, setPalette, fps)
                .Grid(col: 0),

            Win2DAnimatedCanvas.Of(
                onUpdate: (args, s) => ((ParticleField)s!).Step(
                    args.Timing.ElapsedTime, n, g, d),
                onDraw: (sess, args, s) => {
                    fps.Current = 1 / args.Timing.ElapsedTime.TotalSeconds;
                    ((ParticleField)s!).Render(sess, palette);
                },
                drawState: field,
                isPaused: paused)
            .ClearColor(Microsoft.UI.Colors.Black)
            .Grid(col: 1));
    }
}
```

## Appendix B — Spec cross-references

- [Spec 047](047-extensible-control-model.md) — V1 handler protocol,
  the surface this spec consumes.
- [Spec 047 §14](047-extensible-control-model.md#14-suggested-phasing)
  Phase 1 exit gate item 2 — the external-assembly proof shape this
  spec replicates.
- [Spec 048](048-control-registration-and-trimming.md) — Pattern A
  factory-holder discipline, the trim story.
- [Spec 048 §6](048-control-registration-and-trimming.md#6-pattern-a--the-3p--hand-authored-control)
  — the canonical Pattern A example. Reactor.Advanced is a direct
  application of this shape.
- [Spec 014](014-animation-design.md) — Animation design, complementary
  to Win2D for compositor-level animations.
- [Spec 002 §control-coverage](002-winui3-gap-analysis.md) — InkCanvas/
  InkToolbar named as future Advanced inhabitants.
- [Marquee external proof](../../tests/external_proof/Reactor.External.TestControl/) —
  the working code pattern this spec generalizes for a richer control.
