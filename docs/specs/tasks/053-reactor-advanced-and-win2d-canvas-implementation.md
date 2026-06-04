# Reactor.Advanced + Win2DCanvas — Implementation Tasks

Derived from: [`docs/specs/053-reactor-advanced-and-win2d-canvas.md`](../053-reactor-advanced-and-win2d-canvas.md).

> **Status:** Not started. Spec is design-converged; this tracker decomposes
> spec §16's four-phase plan into a step-by-step checklist that can be
> paused and resumed without losing place.
>
> **Conventions** (mirroring `048-control-registration-and-trimming-implementation.md`):
> - Every task is a checkbox; mark `[x]` only when its artifact (code +
>   tests + doc update + verified perf number, where applicable) is landed.
> - The Reactor `xunit + selftest + solution build` triple gate must stay
>   green after every phase. Run order on Windows-x64:
>   `dotnet build Reactor.slnx -p:Platform=x64` →
>   `dotnet test tests/Reactor.Tests -p:Platform=x64 --no-build` →
>   `dotnet test tests/Reactor.Advanced.Tests -p:Platform=x64 --no-build`
>   (new in Phase 1) →
>   `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test`.
> - **No changes under `src/Reactor/`.** Spec §2 Goal 5: any edit there is
>   a bug to file against the V1 public surface (spec 047), not a
>   workaround to ship with this work. If a task here is blocked because
>   the V1 surface is insufficient, stop and open an upstream issue
>   instead of adding `InternalsVisibleTo`.
> - Phases are sequential. Phase 1 lands the library + L0/L1 contracts;
>   Phase 2 ships the Particle Storm sample that proves the perf claim;
>   Phase 3 lands docs + selftests + the new agent skill; Phase 4 is the
>   post-merge follow-up bag.

## Exit gate (all must hold to declare 053 done)

1. `src/Reactor.Advanced/Reactor.Advanced.csproj` exists, lives in
   `Reactor.slnx`, depends on `Microsoft.Graphics.Win2D` and
   `ProjectReference`-s `src/Reactor/Reactor.csproj`. No
   `InternalsVisibleTo` from Reactor into Advanced.
2. The three element record + handler + factory holder triples ship
   (`Win2DCanvas`, `Win2DAnimatedCanvas`, `Win2DVirtualCanvas`) using
   Pattern A registration per spec 048 §6. Element constructors are
   `internal`; factory holders are the sole construction entry point.
3. The three hooks (`UseDrawState`, `UseCanvasResources`,
   `UseDrawCommand`) live in
   `src/Reactor.Advanced/Win2D/Hooks/` as `RenderContext` extensions,
   built on the public Reactor hook surface (no `InternalsVisibleTo`).
4. `tests/Reactor.Advanced.Tests/` is a new xunit test project covering
   element construction discipline, handler prop-diff behavior, echo
   suppression on the value-bearing props (`IsPaused`,
   `TargetElapsedTime`, `ClearColor`), `RedrawKey`→`Invalidate` plumbing,
   hook lifecycle (mount/unmount/device-loss), and Pattern A
   registration idempotence. Project is added to `Reactor.slnx` and CI.
5. Three new selftest fixtures (`Win2D_Canvas_Mount`,
   `Win2D_AnimatedCanvas_Mount`, `Win2D_VirtualCanvas_Mount`) live in
   `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Win2DCanvasFixtures.cs`
   and are registered in **both** the name list and switch arm of
   `SelfTestFixtureRegistry.cs` (mandatory two-place registration).
6. `samples/apps/particle-storm/` exists, runs, sustains ≥60 fps with
   50,000 particles on a documented baseline x64 dev laptop, publishes
   AOT-clean on `win-x64` (via `-p:PublishAotInternal=true`), and ships
   a README that records the baseline machine + measured fps.
7. `mur pack-local` produces
   `local-nupkgs/Microsoft.UI.Reactor.Advanced.0.0.0-local.nupkg`
   alongside the existing core package. The ReactorDemo `dryrun` project
   (or an equivalent throwaway in `tests/aot_trim_proof/`) compiles
   against the local feed and renders one of each canvas type.
8. Trim/AOT: `PublishTrimmed=true` + `IsAotCompatible=true` on
   `Reactor.Advanced.csproj` produce **zero** new warnings beyond what
   `Microsoft.Graphics.Win2D` itself reports. A trim-isolation probe
   (see Phase 1.7) shows that a published app that never names
   `Win2DCanvas`/`Win2DAnimatedCanvas`/`Win2DVirtualCanvas` does not
   root those handlers / their `CanvasControl*` types from the
   Reactor.Advanced.dll side.
9. New user guide page `docs/guide/win2d-canvas.md` compiles cleanly
   from `docs/_pipeline/templates/win2d-canvas.md.dt` via
   `mur docs compile`, with a companion doc-app under
   `docs/_pipeline/apps/win2d-canvas/`, at least one runnable snippet
   per element type, one screenshot, the standard solid-tier
   table/Tips/Next-Steps structure, and the `winui-ref:` link to
   Microsoft's Win2D documentation set.
10. New agent skill `reactor-advanced` lives in **both**
    `skills/advanced.md` (loose-file skill) and
    `plugins/reactor/skills/reactor-advanced/SKILL.md` (plugin-form
    skill). The skill documents the three element shapes, the
    threading rules (spec §8.1), the device-loss / `UseCanvasResources`
    contract (§8.2), and the Particle Storm recipe.
11. CI green: `Reactor.Advanced.Tests` is added to the unit-test job;
    the new selftest fixtures pass under both JIT and AOT selftest
    runs (§16 Phase 3); the AOT smoke for Particle Storm is included
    in the existing `aot-trim-proof` or `aot-selftests` job.
12. Full `xunit + selftest + solution build` triple gate green after
    every phase.

---

## Phase 0 — Pre-flight (no code; reduce surprise risk)

Phase 0 captures the small handful of decisions that the spec leaves to
the implementation PR (spec §15) so the rest of the plan can proceed
without mid-flight rework.

### 0.1 Decide and record open-question resolutions (spec §15)

- [ ] **§15 Q3 (shared device).** Decide whether
      `UseCanvasResources` opts into `UseSharedDevice` automatically when
      called from a component above multiple canvases. Default
      recommendation: **no** for Phase 1 (explicit author opt-in via a
      `useSharedDevice: true` parameter on the hook); revisit if a
      sample needs it. Record the decision in the spec's §15 close-out
      append.
- [ ] **§15 Q5 (test host).** Decide whether the canvas selftests live in
      `tests/Reactor.AppTests.Host` (adds Win2D to every selftest run)
      or in a sibling `tests/Reactor.AppTests.Advanced.Host` (adds a
      second host process). Default recommendation: **same host** —
      Win2D managed assembly is small, native binaries are only loaded
      on first `CanvasControl` activation, and CI cost is the deciding
      factor. Record the decision in the spec's §15 close-out append.
- [ ] **§15 Q1 (Win2D native asset trim).** Confirm with the
      WindowsAppSDK team whether the native `runtimes/win-*/native/`
      payload can be filtered; if not, document the resulting fixed
      cost in the Phase 2 sample README and the spec's §10 caveat.

### 0.2 Pin Win2D version + Directory.Build.props update

- [ ] Add `<Win2DVersion>1.3.0</Win2DVersion>` (or the current stable)
      to `Directory.Build.props` next to the existing
      `<WindowsAppSDKVersion>` so every project pins the same Win2D
      version. Verify by referencing `$(Win2DVersion)` in a throwaway
      build before §1.1.
- [ ] Confirm the chosen Win2D version supports
      `TargetPlatformMinVersion=10.0.17763.0` (the value spec §11.2
      uses); document the actual minimum if higher.

### 0.3 Phase 0 exit gate

- [ ] Open-question decisions recorded in the spec (or in a sibling
      `docs/specs/053/decisions.md` if the spec is locked).
- [ ] `Directory.Build.props` carries `$(Win2DVersion)`; a trivial
      `<PackageReference Include="Microsoft.Graphics.Win2D" Version="$(Win2DVersion)" />`
      in any sample project resolves.

---

## Phase 1 — Library + L0/L1 (spec §16 Phase 1)

Lands the new project, the three element/handler/factory triples, the
three hooks, the unit-test project, and the trim-isolation probe.

### 1.1 New project `src/Reactor.Advanced/Reactor.Advanced.csproj`

- [ ] Create `src/Reactor.Advanced/` with the `.csproj` shape from spec
      §11.2 (TargetFramework `net10.0-windows10.0.22621.0`,
      `RootNamespace=Microsoft.UI.Reactor.Advanced`,
      `AssemblyName=Reactor.Advanced`, `UseWinUI=true`,
      `WindowsAppSDKSelfContained=false`, `PublishTrimmed=true`,
      `IsAotCompatible=true`, `IsPackable=true`,
      `PackageId=Microsoft.UI.Reactor.Advanced`,
      `GenerateDocumentationFile=true`).
- [ ] Add `<PackageReference>` to `Microsoft.WindowsAppSDK` and
      `Microsoft.Graphics.Win2D` (both via `$(...)Version` props).
- [ ] Add `<ProjectReference Include="..\Reactor\Reactor.csproj" />`.
      **Do not** add `InternalsVisibleTo` from Reactor.dll.
- [ ] Add `src/Reactor.Advanced/ReactorAdvancedAssemblyInfo.cs` (matches
      the discipline in `src/Reactor/`).
- [ ] Add `src/Reactor.Advanced/README.md` (package landing copy: what
      the package is, links to the guide page and Particle Storm
      sample).
- [ ] Register in `Reactor.slnx`:
      `<Project Path="src/Reactor.Advanced/Reactor.Advanced.csproj" />`
      at the top level next to the core project.
- [ ] Build clean: `dotnet build src/Reactor.Advanced/Reactor.Advanced.csproj -p:Platform=x64`
      with zero warnings. Verify trim/AOT-warnings-as-errors compiles
      (no Reactor.Advanced code yet to warn about).

### 1.2 `Win2DCanvas` — manual-invalidate canvas (spec §6.1)

- [ ] `src/Reactor.Advanced/Win2D/Win2DCanvasElement.cs`: sealed record
      with `internal` ctor; props per spec §6.1 (`OnDraw`,
      `OnCreateResources`, `ClearColor`, `RedrawKey`, `Setters`).
- [ ] `src/Reactor.Advanced/Win2D/Win2DCanvasHandler.cs`: implements
      `IElementHandler<Win2DCanvasElement, CanvasControl>` per spec 047
      §3. Wires `Draw` and `CreateResources` events via
      `bind.OnCustomEvent<...>` with the element-tag-refresh
      callback-survival pattern (Marquee proof reference). `Update`
      diffs `RedrawKey` (reference inequality) and calls
      `ctrl.Invalidate()` on change; diffs `ClearColor` and
      `WriteSuppressed` per spec 048 echo-suppression discipline.
- [ ] `src/Reactor.Advanced/Win2D/Win2DCanvas.cs`: public static factory
      holder with the static cctor calling
      `ControlRegistry.Register<Win2DCanvasElement, CanvasControl>(static () => new Win2DCanvasHandler())`
      (Pattern A per spec 048 §6). Expose `Of(onDraw, redrawKey)` and
      `Of(onDraw, onCreateResources, redrawKey)` overloads from §6.1.
- [ ] Verify `static () => ...` lambda compiles closure-free (use IL
      inspection or analyzer if available) — this is the trimming
      contract.
- [ ] Unmount path calls `ctrl.RemoveFromVisualTree()` before
      `ctx.ReturnControl(ctrl)` (spec §8.3).

### 1.3 `Win2DAnimatedCanvas` — game-loop canvas (spec §6.2)

- [ ] `Win2DAnimatedCanvasElement.cs`: sealed record with `internal`
      ctor; props per spec §6.2 (`OnUpdate(args, state)`,
      `OnDraw(session, args, state)`, `OnCreateResources`,
      `TargetElapsedTime`, `IsPaused`, `DrawState`, `ClearColor`,
      `Setters`).
- [ ] `Win2DAnimatedCanvasHandler.cs`: implements
      `IElementHandler<Win2DAnimatedCanvasElement, CanvasAnimatedControl>`.
      Wires `Update`, `Draw`, `CreateResources`. The `Update`/`Draw`
      delegates are passed the current `DrawState` from the latest
      element via the tag refresh (so re-renders that swap state are
      visible on the next tick without resubscription). Value-bearing
      `IsPaused`/`TargetElapsedTime`/`ClearColor` use the
      `BindFor(ctrl, newEl).WriteSuppressed(...)` discipline (spec §9 +
      048 §6). **Only writes on real control drift** per repo memory:
      "Descriptor controlled-prop Update must suppress-write ONLY on
      real control drift (current != newValue)".
- [ ] `Win2DAnimatedCanvas.cs`: factory holder, Pattern A
      registration, `Of(onUpdate, onDraw, drawState, isPaused)`
      factory.
- [ ] Debug-build thread-affinity sentinel (spec §8.1 mitigation 2):
      wrap user `OnUpdate`/`OnDraw` in `try/catch` for
      `InvalidOperationException` with the WinUI affinity message;
      append a hint pointing at the docs guide page §8.1 anchor. Gate
      on `#if DEBUG` so retail builds pay nothing.

### 1.4 `Win2DVirtualCanvas` — tiled canvas (spec §6.3)

- [ ] `Win2DVirtualCanvasElement.cs`: sealed record with `internal`
      ctor; props per spec §6.3 (`OnRegionDraw(session, rect)`,
      `OnCreateResources`, `ContentSize`, `InvalidateRegions`,
      `Setters`).
- [ ] `Win2DVirtualCanvasHandler.cs`: implements
      `IElementHandler<Win2DVirtualCanvasElement, CanvasVirtualControl>`.
      Wires `RegionsInvalidated` and `CreateResources`. `Update` diffs
      `ContentSize` (write-through) and `InvalidateRegions` (reference
      inequality → `ctrl.Invalidate(rect)` per rect on the new list).
- [ ] `Win2DVirtualCanvas.cs`: factory holder, Pattern A registration,
      `Of(onRegionDraw, contentSize)` factory.

### 1.5 Fluent modifiers (spec §6.4)

- [ ] `src/Reactor.Advanced/Win2D/Win2DCanvasModifiers.cs` with
      `ClearColor<TElement>`, `Paused`, `TargetFps`, and a typed `Set`
      extension per spec §6.4. Type-preserving via
      `<TElement> where TElement : Element` so chains keep concrete
      type (matches existing `ElementExtensions` style).
- [ ] **Naming check:** `ClearColor` collides with the element
      property name — confirm the extension on the *element* is named
      `WithClearColor` if needed to avoid C# member-name collision, or
      keep `ClearColor` as the property name and use an `init` setter
      with the same name on the extension (compiler-tested). Pick one
      and document in the .cs file header.

### 1.6 Hooks (spec §7, L1)

- [ ] `src/Reactor.Advanced/Win2D/Hooks/UseDrawState.cs`: `static Ref<T>
      UseDrawState<T>(this RenderContext ctx, Func<T> init) where T :
      class` — thin `UseRef` wrapper per spec §7.1. XML doc explicitly
      calls out the "treat `Current` like a `volatile` field" contract
      from §8.1.
- [ ] `src/Reactor.Advanced/Win2D/Hooks/UseCanvasResources.cs`:
      `Ref<TResources?> UseCanvasResources<TResources>(this RenderContext ctx,
      Func<CanvasDevice, ValueTask<TResources>> create,
      Action<TResources>? dispose = null)` per spec §7.2. Internal
      state machine on the host component's hook list via `UseRef` +
      `UseEffect`. Subscribes to `CanvasDevice.DeviceLost` and re-runs
      `create` with the fresh device; runs `dispose` (or
      `IDisposable.Dispose`) on unmount. **Must compile against the
      public Reactor hook surface only** — if it needs anything
      internal, stop and file a Reactor-side issue (spec §3.2 / Goal
      5).
- [ ] `src/Reactor.Advanced/Win2D/Hooks/UseDrawCommand.cs`: memoized
      draw delegate per spec §7.3; rebuilds only when `deps` change
      (use `UseMemo`).
- [ ] Decide and implement the wiring channel for
      `UseCanvasResources` ↔ the next-mounted canvas (spec §7.2
      mentions a `Context<T>` sentinel). Implementation note: if a
      proper `Context<T>` propagation isn't available on the public
      surface yet, fall back to "the hook returns a `Ref` that the
      author passes explicitly to the canvas element via a new
      `Resources` prop on each Element record"; choose the simpler
      shape and update the spec §7.2 close-out.

### 1.7 Trim-isolation probe (spec §10)

Mirror the Phase 2 Hello-World probe pattern from spec
048 (`tests/aot_trim_proof/Reactor.AotHelloWorld/`).

- [ ] Extend the existing AOT trim probe (or add a sibling
      `Reactor.AotHelloWorld.Advanced`) that references
      `Microsoft.UI.Reactor.Advanced` but **never names** any of the
      three canvas factories. Assert the published binary contains
      **no** `Win2DCanvasHandler`, `Win2DAnimatedCanvasHandler`,
      `Win2DVirtualCanvasHandler`, `CanvasAnimatedControl`,
      `CanvasVirtualControl` symbols on the Reactor.Advanced side.
      (Win2D's own types may remain — that's the SDK's choice.)
- [ ] A *positive* sibling probe that **does** call
      `Win2DCanvas.Of(...)` asserts the symbols **do** appear, proving
      the trim test isn't a false-positive vacuum.
- [ ] Wire both probes into the `aot-trim-proof` CI job in
      `.github/workflows/ci.yml`. Document the runbook in
      `tests/aot_trim_proof/README.md` (extend the existing file).

### 1.8 Unit tests — `tests/Reactor.Advanced.Tests/`

New xunit project. Modeled after `tests/Reactor.Tests/` discipline
(`-p:Platform=x64` everywhere). All tests run **without** a live
WinUI window — Win2D types that need activation should be mocked or
asserted via element/handler shape, not control instances.

- [ ] Create `tests/Reactor.Advanced.Tests/Reactor.Advanced.Tests.csproj`
      (xunit + `Microsoft.NET.Test.Sdk` + `ProjectReference` to
      `src/Reactor.Advanced/`).
- [ ] Register the new project in `Reactor.slnx`.
- [ ] Add to the unit-test CI job in `.github/workflows/ci.yml` so
      `dotnet test tests/Reactor.Advanced.Tests` runs on every PR.

#### 1.8.1 Element record discipline

- [ ] `ElementConstructorTests`: assert each of the three element
      records has an `internal` (not `public`) constructor; factory
      holders are the sole public entry point.
- [ ] `FactoryHolderTests`: assert calling the factory triggers the
      static cctor, which registers in `ControlRegistry.Default`.
      Use a fresh `AssemblyLoadContext` per test to isolate cctor side
      effects, **or** structure each test so the registration is
      observable (e.g., assert
      `ControlRegistry.TryResolve(typeof(Win2DCanvasElement), out _)`
      is true after the first factory call).
- [ ] `PatternAStaticLambdaTests`: reflect over the handler factory
      delegate and assert it is a static method with no closure target
      (mirrors spec 048 verification pattern).

#### 1.8.2 Handler prop-diff and echo discipline

- [ ] `Win2DCanvasHandlerUpdateTests`:
   - [ ] `RedrawKey` change → `Invalidate()` called exactly once.
   - [ ] `RedrawKey` unchanged → `Invalidate()` not called.
   - [ ] `ClearColor` change → `WriteSuppressed` write; equal value →
         no write.
   - [ ] Callback identity change (`OnDraw`) refreshes the tag without
         resubscribing the WinUI `Draw` handler (use an event-fire
         counter probe).
- [ ] `Win2DAnimatedCanvasHandlerUpdateTests`:
   - [ ] `IsPaused` toggle writes through suppressed; pause→pause
         no-ops.
   - [ ] `TargetElapsedTime` change writes through suppressed;
         equal-value no-op.
   - [ ] `DrawState` swap is reflected on the *next* synthetic
         `Update` invocation (without a control reactivation).
- [ ] `Win2DVirtualCanvasHandlerUpdateTests`:
   - [ ] `InvalidateRegions` reference-change calls `Invalidate(rect)`
         per rect; reference-equal skips.
   - [ ] `ContentSize` change writes through.

#### 1.8.3 Hook tests

- [ ] `UseDrawStateTests`: `init` runs exactly once across re-renders;
      the returned `Ref<T>.Current` is stable identity; mock-unmount
      runs `Dispose` if `T : IDisposable`.
- [ ] `UseCanvasResourcesTests`:
   - [ ] `create` runs once per device; re-runs on a synthetic
         device-lost callback; old resources disposed first.
   - [ ] Custom `dispose` is invoked if provided; falls back to
         `IDisposable.Dispose` if not.
   - [ ] Unmount cancels in-flight `create` task without throwing.
- [ ] `UseDrawCommandTests`: memoization respects `deps`; identity
      stable when deps stable; new delegate when any dep changes.

#### 1.8.4 Modifier tests

- [ ] `Win2DCanvasModifiersTests`: `ClearColor`/`Paused`/`TargetFps`
      return new elements with updated props (record `with` semantics);
      original element unchanged; chainable.

### 1.9 Local-pack workflow

- [ ] Extend `mur pack-local` (or the equivalent script under
      `tools/`) to pack `src/Reactor.Advanced/Reactor.Advanced.csproj`
      alongside the core package. Output:
      `local-nupkgs/Microsoft.UI.Reactor.Advanced.0.0.0-local.nupkg`.
- [ ] Verify `C:\Users\andersonch\Code\ReactorDemo\dryrun` can
      `<PackageReference Include="Microsoft.UI.Reactor.Advanced" Version="0.0.0-local" />`
      and resolve via the existing `nuget.config` local-feed mapping
      (no source feed changes required).
- [ ] Update the release-workflow `version` parameter logic to
      lock-step Advanced with core (spec §11.4).

### 1.10 Phase 1 exit gate (spec §16 Phase 1)

- [ ] `dotnet build Reactor.slnx -p:Platform=x64` clean.
- [ ] `dotnet test tests/Reactor.Advanced.Tests -p:Platform=x64`
      green.
- [ ] `dotnet test tests/Reactor.Tests -p:Platform=x64` (no
      regressions in existing tests).
- [ ] `dotnet publish src/Reactor.Advanced -p:PublishTrimmed=true -p:Platform=x64 -r win-x64`
      produces **zero new** trim/AOT warnings beyond the Win2D
      baseline. Record the baseline warning set in
      `src/Reactor.Advanced/README.md`.
- [ ] `mur pack-local` produces both nupkgs side-by-side; dryrun
      project compiles against the local feed and renders one of each
      canvas type (a 20-line smoke component).
- [ ] Trim-isolation probes (§1.7) green: negative probe shows the
      handlers/controls are stripped; positive probe shows they
      survive.

---

## Phase 2 — Particle Storm sample (spec §16 Phase 2)

### 2.1 Project scaffolding

- [ ] Create `samples/apps/particle-storm/ParticleStorm.csproj`
      modeled after `samples/apps/netpulse/NetPulse.csproj`: `WinExe`,
      `TargetFramework=net10.0-windows10.0.22621.0`,
      `WindowsPackageType=None`, `Platforms=x64;ARM64`,
      `RuntimeIdentifier=win-x64` defaulted, single
      `<ProjectReference Include="..\..\..\src\Reactor.Advanced\Reactor.Advanced.csproj" />`
      (transitively brings in Reactor core).
- [ ] Add to `Reactor.slnx`.
- [ ] Stub `samples/apps/particle-storm/App.cs` with the
      `ReactorApp.Run<App>("Particle Storm", width: 1280, height: 800)`
      entry point.

### 2.2 `ParticleField` physics engine

Lives in `samples/apps/particle-storm/ParticleField.cs`. **No
Reactor.Advanced.dll dependency** — this is a pure physics/render
class consumed by the canvas's `OnUpdate`/`OnDraw` callbacks.

- [ ] `Particle` struct: `(float x, float y, float vx, float vy, byte
      hue)`. Use a struct, not a class — no GC churn at 50k particles.
- [ ] `Particle[] _particles` flat buffer; capacity allocated once,
      Logical count tracked separately so resizing via the slider
      doesn't realloc.
- [ ] `Step(elapsed, count, gravity, drag, cursorX, cursorY)` —
      per-particle update. SIMD-friendly inner loop preferred (use
      `System.Numerics.Vector<T>` where it doesn't sacrifice
      readability; document the choice).
- [ ] `Render(session, palette)` — uses `CanvasSpriteBatch` to issue
      all `count` particles in one batched call. This is the
      perf-critical path: confirm via PerfView that the draw call is
      O(1) batches.
- [ ] `Burst(x, y, n, palette)` — spawn N particles at the given
      cursor position.

### 2.3 Reactor chrome — Sidebar (spec §12.2)

- [ ] `samples/apps/particle-storm/Sidebar.cs`: function component
      taking the state tuples from `App.Render()` (count, gravity,
      drag, paused, palette, fps refs). Layout via `VStack` +
      `Slider` + `ComboBox` + `ToggleSwitch` + `Button` per spec
      §12.2. Width ~280 px.
- [ ] Live `Text` block showing measured fps + active particle count.
      Subscribe via a `UseEffect` polling `fps.Current` on a 250 ms
      timer (or whatever cadence reads cleanly — fps text doesn't
      need 60 Hz refresh).
- [ ] `Palette` enum with at least 4 named presets (Galaxy, Ember,
      Glacier, Garden) and a static `Color[]` LUT per palette.

### 2.4 Wire the canvas

- [ ] `App.Render()` body matches spec §12.2 / Appendix A: a
      2-column `Grid`, `Sidebar` on the left, `Win2DAnimatedCanvas.Of`
      on the right with `.ClearColor(Colors.Black)` and
      `drawState: field, isPaused: paused`.
- [ ] `OnUpdate` and `OnDraw` are inline lambdas — no
      `UseDrawCommand` needed unless a measurable closure-alloc
      regression shows up. Keep the snippet identical to spec
      Appendix A so it reads cleanly when copied to the docs.
- [ ] Pointer-move on the canvas writes cursor coordinates into a
      `UseRef<Point>` consumed by `Particle.Step`. Decide between
      `Setters` hatch on the canvas element vs. a parent `Border`
      with pointer handlers; prefer the parent-Border idiom because
      it doesn't reach inside the canvas.

### 2.5 Performance baseline

- [ ] Document the dev laptop baseline in
      `samples/apps/particle-storm/README.md`: CPU model, GPU, memory,
      Windows version. Measured fps at 1k / 10k / 50k / 100k
      particles. Format mirrors the existing `samples/apps/`
      perf-note convention if present.
- [ ] If the 50k-particles-at-60-fps target isn't met on the
      baseline machine, profile and document the bottleneck before
      moving on. Possible mitigations: switch to
      `CanvasRenderTarget` pre-rendered sprite atlas; tune the
      `Vector<T>` lane count; reduce per-frame allocations in the
      palette lookup.

### 2.6 AOT publish gate

- [ ] `dotnet publish samples/apps/particle-storm -c Release -r win-x64 -p:PublishAotInternal=true`
      completes with zero new trim/AOT warnings.
- [ ] Published AOT exe launches, renders 50k particles, sustains the
      target fps on the baseline machine.
- [ ] Add the publish step to a CI job (extend `aot-trim-proof` or
      `aot-selftests`) so the AOT-clean property is regression-tested.

### 2.7 Phase 2 exit gate (spec §16 Phase 2)

- [ ] Sample runs on x64 and ARM64 dev laptops; sustains ≥60 fps
      with 50,000 particles on the documented baseline.
- [ ] Live slider drag for particle count changes physics on the next
      visible frame.
- [ ] Publishes AOT-clean on `win-x64` (and ARM64 if the CI runner
      supports it).
- [ ] README documents the baseline machine + measured numbers.

---

## Phase 3 — Docs + selftests + skill (spec §16 Phase 3)

### 3.1 Doc app `docs/_pipeline/apps/win2d-canvas/`

Follows `docs/_pipeline/ai-author-skill.md` (the doc-app discipline).

- [ ] `docs/_pipeline/apps/win2d-canvas/win2d-canvas.csproj`: standard
      Reactor doc-app `.csproj` (WinExe, `UseWinUI=true`,
      `WindowsPackageType=None`) **plus** `ProjectReference` to
      `src/Reactor.Advanced/Reactor.Advanced.csproj` (the only doc-app
      that references Advanced — keep it self-contained).
- [ ] `docs/_pipeline/apps/win2d-canvas/App.cs`: a single window with
      one of each canvas type, each tagged with `<snippet:...>`
      markers per the AI-author-skill conventions:
   - [ ] `<snippet:manual-canvas>` — `Win2DCanvas.Of` drawing a count
         that re-renders on a button click. Demonstrates `RedrawKey`.
   - [ ] `<snippet:animated-canvas>` — `Win2DAnimatedCanvas.Of` with
         `UseDrawState` for a few hundred animated dots (small enough
         that the screenshot is meaningful at default app window
         size).
   - [ ] `<snippet:virtual-canvas>` — `Win2DVirtualCanvas.Of` showing
         a large logical content size with a couple of tiles drawn
         differently to make the virtualization visible.
   - [ ] `<snippet:use-canvas-resources>` — a small `CanvasBitmap`
         loaded via `UseCanvasResources` and drawn on the animated
         canvas; demonstrates device-loss-safe resource acquisition.
- [ ] `docs/_pipeline/apps/win2d-canvas/doc-manifest.yaml`:
      `app: title/width/height`, one `screenshot` entry per canvas
      type (`manual`, `animated`, `virtual`), with `crop: content` for
      the first two and `crop: none` for `virtual` (full client frame
      matters there).

### 3.2 Template `docs/_pipeline/templates/win2d-canvas.md.dt`

Solid-tier minimum (3 snippets, 1 screenshot, table, Tips, Next
Steps); aim for comprehensive (caveat, mental model paragraph,
patterns, common mistakes, ≥5 cross-links).

- [ ] Front-matter: `title`, `app: win2d-canvas`, `order` (slot
      between `effects` and the advanced track — pick a free number
      and document it), `audience: advanced`, tier `comprehensive`,
      `winui-ref: https://microsoft.github.io/Win2D/WinUI3/html/Introduction.htm`.
- [ ] ≥80-word mental-model lead paragraph framing the retained ↔
      immediate-mode escape hatch and where Win2D sits relative to
      pure Reactor (paraphrase spec §1).
- [ ] Sections:
   - [ ] "Three canvases, three workloads" — the table from spec §4.
   - [ ] "Manual canvas (`Win2DCanvas`)" — snippet + `RedrawKey`
         explanation + the `ai:caveat` about "invalidate is your
         responsibility for non-key state changes".
   - [ ] "Animated canvas (`Win2DAnimatedCanvas`)" — snippet,
         **threading** section flagged as required reading (link to
         the `## Threading` section below), `UseDrawState` + state
         survival across renders.
   - [ ] "Virtual canvas (`Win2DVirtualCanvas`)" — snippet, tile
         invalidation via `InvalidateRegions`.
   - [ ] "Hooks" — `UseDrawState`, `UseCanvasResources`,
         `UseDrawCommand` with one snippet each (the
         `UseCanvasResources` snippet doubles as the device-loss
         example).
   - [ ] "Threading" — the spec §8.1 table + the "treat Ref.Current
         like volatile" rule + the debug-mode sentinel callout.
   - [ ] "Device loss" — paraphrase spec §8.2.
   - [ ] "Performance: Particle Storm" — link to
         `samples/apps/particle-storm/` with the sample's screenshot.
   - [ ] `## Tips` (≥3 items: pick the right canvas, prefer
         `SpriteBatch` for >10k draws, use `UseCanvasResources` not
         `OnDraw`-local allocs).
   - [ ] `## Patterns` (the Particle Storm reactive-coupling pattern,
         the device-loss-safe resource pattern, the manual-invalidate
         pattern).
   - [ ] `## Common Mistakes` (≥3: touching WinUI controls from
         `OnUpdate`, missing `RedrawKey` for value-driven scenes,
         relying on `OnDraw` to allocate per frame).
   - [ ] `## Next Steps` (≥3 cross-links: the Particle Storm sample,
         spec 053, the upstream Win2D docs).
- [ ] At least one `<!-- ai:caveat -->` block and ≥5 inline
      cross-links to other Reactor pages (`Hooks`, `Effects`,
      `Components`, `Animation`, `Extending Reactor controls`).
- [ ] `concept-aliases:` declared if needed (e.g. `Win2D`,
      `CanvasControl`, `CanvasAnimatedControl`).
- [ ] `mur docs compile` produces `docs/guide/win2d-canvas.md` with
      snippets resolved and at least the manual + animated
      screenshots inlined. **Do not** hand-edit the generated
      `docs/guide/win2d-canvas.md` (per repo convention: edit
      templates, not generated output).
- [ ] Add a link to the new page from
      `docs/_pipeline/templates/cheat-sheet.md.dt` and
      `docs/_pipeline/templates/advanced.md.dt` so it's reachable
      from the guide TOC.
- [ ] Update `docs/_pipeline/templates/architecture-overview.md.dt`
      (or equivalent) to mention `Reactor.Advanced` as a sibling
      package and Win2D as the first inhabitant.

### 3.3 Selftest fixtures

`tests/Reactor.AppTests.Host/SelfTest/Fixtures/Win2DCanvasFixtures.cs`
— per repo memory: "new AppTests.Host selftest fixtures must be
registered MANUALLY in two places in SelfTestFixtureRegistry.cs".

- [ ] `Win2D_Canvas_Mount`: mounts a `Win2DCanvas`, drives one
      `RedrawKey` change, asserts the `OnDraw` callback fired at
      least once, unmounts cleanly (no `CanvasControl` leak — use
      `WaitFor` against a weak reference per the host's existing
      idiom).
- [ ] `Win2D_AnimatedCanvas_Mount`: mounts a `Win2DAnimatedCanvas`,
      waits one tick via `Harness.WaitFor` (NOT a one-shot snapshot —
      per repo memory: "Selftest probes for lazily-realized WinUI
      content must poll, not assert a one-shot snapshot"), toggles
      `IsPaused`, asserts the tick count plateaus, unmounts.
- [ ] `Win2D_VirtualCanvas_Mount`: mounts a `Win2DVirtualCanvas`,
      asserts at least one `OnRegionDraw` fires, swaps
      `InvalidateRegions`, asserts a follow-up call.
- [ ] Add Reactor.Advanced as a `ProjectReference` to
      `tests/Reactor.AppTests.Host/Reactor.AppTests.Host.csproj` (the
      decision recorded in §0.1).
- [ ] **Two-place registration** in
      `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`:
   - [ ] Add fixture names to the name list.
   - [ ] Add `case` arms in the name→constructor switch.
- [ ] Verify via filter: `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test --filter "Win2D"`
      discovers and runs the three fixtures.

### 3.4 Agent skill `reactor-advanced`

Two-place install per the repo's existing skill layout (mirrors
`reactor-charts`, `reactor-docking`, etc.).

- [ ] `skills/advanced.md` (loose-file skill): YAML front-matter with
      `name: reactor-advanced`, `description:` summarizing Win2D
      canvas elements + the three hooks + threading rules. Body:
      decision table (which canvas to pick), threading cheat sheet,
      `UseCanvasResources` device-loss recipe, link to the Particle
      Storm sample, link to `docs/guide/win2d-canvas.md` for depth.
- [ ] `plugins/reactor/skills/reactor-advanced/SKILL.md`: same
      content, formatted per the existing plugin-skill folder
      convention (look at
      `plugins/reactor/skills/reactor-charts/SKILL.md` as the
      template).
- [ ] If a `plugins/reactor/skills/reactor-advanced/reference.md`
      sibling helps (cross-reference for the three element factories
      + the three hooks with signatures), add it; otherwise skip.
- [ ] Bundle the loose-file skill into the
      `Microsoft.UI.Reactor.Advanced` nupkg under
      `agentkit/skills/reactor-advanced.md` (spec §11.5) so users who
      install the package via NuGet get the skill in their Copilot
      CLI surface automatically. Mirror the packing convention used
      by the core package for its skills.

### 3.5 Reference page entries

The reference auto-gen (per `ai-author-skill.md` §9) pulls XML doc
comments from public APIs. Make sure the new surface has solid XML
docs so the generated reference pages aren't empty.

- [ ] Every public type and member in `src/Reactor.Advanced/` has an
      XML doc comment. The threading-sensitive members (animated
      `OnUpdate`/`OnDraw`, the hooks) link to the guide's
      `## Threading` section anchor.
- [ ] After `mur docs compile`, verify the auto-generated reference
      pages (`docs/guide/reference/elements/Win2DCanvas.md`,
      `docs/guide/reference/hooks/UseDrawState.md`, etc.) exist and
      render.
- [ ] Add `<!-- ref:Win2DCanvas -->` (and friends) cross-link
      markers in the new template where appropriate so the
      "Featured in" callout populates on the reference pages.

### 3.6 Phase 3 exit gate (spec §16 Phase 3)

- [ ] Selftests green: `Win2D_Canvas_Mount`,
      `Win2D_AnimatedCanvas_Mount`, `Win2D_VirtualCanvas_Mount` pass
      under JIT. Each in isolation (`--filter`) and as part of the
      full suite.
- [ ] AOT selftest run green for the same three fixtures (per repo
      memory: NativeAOT host realizes lazy content slower than JIT —
      use `WaitFor`, not one-shot snapshots).
- [ ] `mur docs compile` green; `docs/guide/win2d-canvas.md`
      generated with snippets and screenshots resolved; cross-link
      analyzer happy.
- [ ] `Reactor.Advanced.Tests` runs in CI; selftest CI job runtime
      within +10 % of baseline (spec §16 Phase 3 criterion 3).
- [ ] Agent skill installed in both locations; loads via
      `reactor_local_skill advanced` (or equivalent) and lists the
      three canvas factories + three hooks with usage notes.

---

## Phase 4 — Post-merge follow-ups (spec §16 Phase 4)

These are intentionally separate from the main phases and gated on
the merge of Phase 3. Each gets its own issue/spec when triggered.

- [ ] Open `docs/specs/proposals/054-win2d-scene-graph.md` (or the
      next free spec number) for the L2 declarative scene graph if
      user feedback requests it.
- [ ] For each plausible §13 inhabitant of `Reactor.Advanced`
      (`Composition`, `Media`, `Inking`, `Maps`, Charting relocation),
      open a tracking issue with a one-line motivation when demand
      surfaces. Do **not** preemptively scaffold their folders.
- [ ] Investigate the Win2D native-asset trim story (§15 Q1) with the
      WindowsAppSDK team. Capture findings in
      `docs/specs/053/native-asset-trim-investigation.md` regardless
      of outcome.
- [ ] Hot-reload validation (§15 Q2): edit an `OnDraw` closure
      while the sample runs under `dotnet watch`, verify the next
      frame picks up the new closure for both `Win2DCanvas` (after
      the next `RedrawKey` change) and `Win2DAnimatedCanvas` (next
      tick). Add a Tips entry to the guide if any pitfall surfaces.
- [ ] Multi-canvas device sharing (§15 Q3): if user feedback
      reports the explicit-opt-in shape is awkward, revisit the
      automatic-shared-device decision recorded in §0.1 and open a
      follow-up spec.

---

## Appendix — Quick reference of what's new vs touched

**New files (Phase 1):**
- `src/Reactor.Advanced/Reactor.Advanced.csproj`
- `src/Reactor.Advanced/ReactorAdvancedAssemblyInfo.cs`
- `src/Reactor.Advanced/README.md`
- `src/Reactor.Advanced/Win2D/Win2DCanvasElement.cs`
- `src/Reactor.Advanced/Win2D/Win2DCanvasHandler.cs`
- `src/Reactor.Advanced/Win2D/Win2DCanvas.cs`
- `src/Reactor.Advanced/Win2D/Win2DAnimatedCanvasElement.cs`
- `src/Reactor.Advanced/Win2D/Win2DAnimatedCanvasHandler.cs`
- `src/Reactor.Advanced/Win2D/Win2DAnimatedCanvas.cs`
- `src/Reactor.Advanced/Win2D/Win2DVirtualCanvasElement.cs`
- `src/Reactor.Advanced/Win2D/Win2DVirtualCanvasHandler.cs`
- `src/Reactor.Advanced/Win2D/Win2DVirtualCanvas.cs`
- `src/Reactor.Advanced/Win2D/Win2DCanvasModifiers.cs`
- `src/Reactor.Advanced/Win2D/Hooks/UseDrawState.cs`
- `src/Reactor.Advanced/Win2D/Hooks/UseCanvasResources.cs`
- `src/Reactor.Advanced/Win2D/Hooks/UseDrawCommand.cs`
- `tests/Reactor.Advanced.Tests/Reactor.Advanced.Tests.csproj` + suite

**New files (Phase 2):**
- `samples/apps/particle-storm/ParticleStorm.csproj`
- `samples/apps/particle-storm/App.cs`
- `samples/apps/particle-storm/ParticleField.cs`
- `samples/apps/particle-storm/Sidebar.cs`
- `samples/apps/particle-storm/README.md`

**New files (Phase 3):**
- `docs/_pipeline/apps/win2d-canvas/win2d-canvas.csproj`
- `docs/_pipeline/apps/win2d-canvas/App.cs`
- `docs/_pipeline/apps/win2d-canvas/doc-manifest.yaml`
- `docs/_pipeline/templates/win2d-canvas.md.dt`
- `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Win2DCanvasFixtures.cs`
- `skills/advanced.md`
- `plugins/reactor/skills/reactor-advanced/SKILL.md`

**Touched files (across phases):**
- `Reactor.slnx` (3 new projects)
- `Directory.Build.props` (`$(Win2DVersion)`)
- `.github/workflows/ci.yml` (Reactor.Advanced.Tests + AOT probes)
- `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`
  (two-place fixture registration)
- `tests/Reactor.AppTests.Host/Reactor.AppTests.Host.csproj`
  (ProjectReference to Reactor.Advanced — §0.1 decision)
- `tests/aot_trim_proof/README.md` (extended runbook)
- `docs/_pipeline/templates/cheat-sheet.md.dt`,
  `advanced.md.dt`, `architecture-overview.md.dt` (cross-links)
- `mur` pack-local + release-workflow scripts (lock-stepped Advanced
  versioning)

**Explicitly NOT touched:** anything under `src/Reactor/`. Per spec §2
Goal 5, the entire integration is author-side and any required change
to Reactor core is a Reactor-side bug to file separately.
