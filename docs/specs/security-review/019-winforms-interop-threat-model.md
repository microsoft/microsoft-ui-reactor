# Chunk 19 — WinForms interop threat model

**Status:** Phase 2 — review complete
**Reviewer:** Security review pass
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Companion:** [000-chunking-and-threat-model.md](000-chunking-and-threat-model.md) §8 (Tier-5 / Chunk 19)

---

## 1. Scope

The chunking doc says "12 files" — that is the file count `git ls-files` returns for the project including the `.csproj`, designer support, and obj/ artifacts; the **review-relevant C# code is three source files totalling ~563 LoC**. There is no `unsafe` code, no FFI beyond a handful of P/Invokes, and no XAML.

| File | LoC | Role |
|---|---:|---|
| `src/Reactor.Interop.WinForms/XamlIslandControl.cs` | 324 | The hosting control: a `System.Windows.Forms.Control` subclass that owns a `DesktopWindowXamlSource`, manages HWND sizing, focus hand-off, and Reactor component instantiation. |
| `src/Reactor.Interop.WinForms/XamlIslandBootstrap.cs` | 143 | Process-level bootstrap: DPI awareness, COM wrappers, WinUI `Application.Start`, message-pump filter, WM_QUIT pump exit. |
| `src/Reactor.Interop.WinForms/ReactorComponentTypeConverter.cs` | 96 | `TypeConverter` that enumerates `AppDomain.CurrentDomain.GetAssemblies()` and offers all concrete `Component` subclasses to the WinForms designer. |
| `src/Reactor.Interop.WinForms/Reactor.Interop.WinForms.csproj` | 20 | `UseWinUI=true` + `UseWindowsForms=true`, references WindowsAppSDK. |

**Total review-relevant LoC: ~563.**

> Note on the "12 files" number from `000-chunking-and-threat-model.md` line 248: the `src/Reactor.Interop.WinForms/` tree contains a `.csproj`, `.csproj.user`, three `.cs` files, and 7+ generated artifacts under `obj/` (`Reactor.Interop.WinForms.GlobalUsings.g.cs`, `*.AssemblyInfo.cs`, `*.AssemblyAttributes.cs`, etc., per build configuration). Only the three hand-written `.cs` files are reviewed below.

---

## 2. Data-flow diagram

```
   Developer-authored WinForms app
                 │
                 │  Main() with [STAThread]
                 ▼
   XamlIslandBootstrap.Run(onReady)              ← XamlIslandBootstrap.cs:65
                 │
                 │  P/Invoke: SetProcessDpiAwarenessContext (user32)
                 │  WinRT.ComWrappersSupport.InitializeComWrappers()
                 │  XamlApp.Start(callback) ─── creates IslandApplication (WinUI Application)
                 │           │
                 │           ├─ MergedDictionaries += XamlControlsResources
                 │           ├─ SWF.Application.AddMessageFilter(XamlPreTranslateFilter)
                 │           ├─ onReady()  ← user creates Form, calls form.Show()
                 │           ├─ SWF.Application.Run()  ← BLOCKS on WinForms message loop
                 │           └─ PostQuitMessage(0)     ← unblocks XAML's RunEventLoop
                 ▼
   Form created with one or more XamlIslandControl instances
                 │
   ┌─────────────┴──── XamlIslandControl (one per island) ────────────────┐
   │                                                                       │
   │  OnHandleCreated:                       ← XamlIslandControl.cs:123    │
   │     _source = new DesktopWindowXamlSource()                           │
   │     _source.Initialize(WindowIdFromWindow(Handle))                    │
   │     SiteBridge attaches a child HWND under the WinForms Handle        │
   │                                                                       │
   │     priority: _pendingContent → _contentFactory → _componentType      │
   │                       │              │                  │             │
   │                       │              │                  ▼             │
   │                       │              │   Activator.CreateInstance(T)  │
   │                       │              │   new ReactorHostControl(...)  │
   │                       │              │   ← XamlIslandControl.cs:175-6 │
   │                       └──────► _source.Content = …                    │
   │                                                                       │
   │  Per-message:  XamlPreTranslateFilter.PreFilterMessage                │
   │     → ContentPreTranslateMessage(ref nativeMsg)  ← Bootstrap.cs:140   │
   │                                                                       │
   │  Per-resize:   GetClientRect(Handle) → SiteBridge.MoveAndResize       │
   │                                       → SetWindowPos(bridgeHwnd…)     │
   │                                                                       │
   │  Per-focus:    NavigateFocus(First|Last)                              │
   │     OnGotFocus checks ModifierKeys & Shift                            │
   │     TakeFocusRequested → Parent.SelectNextControl                     │
   │                                                                       │
   │  Dispose(disposing=true): _source.Dispose() ; _source = null          │
   │     ← XamlIslandControl.cs:315-323                                    │
   └──────────────────────────────────────────────────────────────────────┘

   Designer-time path (Visual Studio designer host process):
        ReactorComponentTypeConverter.GetStandardValues
        → AppDomain.CurrentDomain.GetAssemblies()
          → for each: asm.GetTypes()  — swallowed exceptions
            → IsValidComponentType filter
        Result: a dropdown in the property grid.
        ConvertFrom(string) → walks all loaded assemblies, calls asm.GetType / asm.GetTypes,
        returns first match.
```

**Inputs:** WinForms Win32 messages (handle, message id, wParam, lParam), HWND geometry, ModifierKeys, designer property strings, the developer's own `Component` Type metadata.

**Outputs:** WinUI XAML scene mounted in a child HWND of the WinForms control; window-position calls; focus transfers between WinForms and XAML.

**Persistence:** None. Nothing is written to disk.

---

## 3. Trust boundaries crossed

This chunk is on the framework side of a single boundary: **the in-process boundary between WinForms (managed CLR) and WinUI/WindowsAppSDK (mostly C++/WinRT components reached via COM apartment-affine projections)**. Both halves run on the same STA thread and in the same process.

| Boundary | Direction | Trust assumption (per `000-chunking-and-threat-model.md`) |
|---|---|---|
| Developer-authored WinForms code → this chunk | inbound | Trusted source; threats are correctness/availability bugs in this code that can crash or leak in the developer's app. |
| This chunk → WindowsAppSDK / WinUI | outbound | WindowsAppSDK is a trusted dependency. Marshalling boundary is the review surface — apartment crossings, COM lifetime, HWND ownership. |
| This chunk → user32 P/Invoke (`SetWindowPos`, `GetClientRect`, `SetProcessDpiAwarenessContext`, `PostQuitMessage`) | outbound | Standard Win32; review for parameter validation, error-path leaks. |
| WinForms designer host → `ReactorComponentTypeConverter` | inbound at design time | The designer is part of the developer's IDE — trusted, but reflection-based enumeration runs against whatever assemblies the designer has loaded, which can include arbitrary code. |
| WinForms message pump → `XamlPreTranslateFilter.PreFilterMessage` | inbound | All `MSG` structures the WinForms loop sees pass through the filter into `ContentPreTranslateMessage`. The thread that delivers them is necessarily the STA UI thread. |

There is **no network surface**, **no parser of untrusted data**, **no IPC**, and **no persisted state** in this chunk. The only attacker reach scenarios are (a) bugs in the developer's own app reaching this code, and (b) an attacker who has already won — they have arbitrary code execution in the app process or designer process — using these primitives to amplify impact.

Per the chunk brief: "the integrated app is the developer's own code. Threats are correctness/availability — handle leaks become DoS over the life of an app, COM marshaling bugs can crash hosts."

---

## 4. Asset inventory

Things worth attacking, and why they are or are not interesting:

| Asset | Where | Threat shape |
|---|---|---|
| The `DesktopWindowXamlSource` ↔ child-HWND pair | `_source` field, owned by each `XamlIslandControl` instance | Lifetime correctness. Leaking these over a long-running app is the dominant DoS vector. |
| The cached `ReactorHostControl` instance created by `MountComponentType` | local variable in `XamlIslandControl.cs:175-176`, not retained | Disposable but unreachable for explicit dispose. Reconciler subtree, ETW consumer, observable subscriptions, overlay wirings tied to it (see `src/Reactor/Hosting/ReactorHostControl.cs:553-579`). |
| WinUI `Application` singleton (`IslandApplication`) | `XamlIslandBootstrap.IslandApplication`, created once via `XamlApp.Start` | Ground for theme resources / metadata provider. Not directly attackable — but `_onReady` is a static field, so a second `Run` call after the first returns is dangerous. |
| The static `_onReady` callback | `XamlIslandBootstrap._onReady`, `XamlIslandBootstrap.cs:30` | Held until `IslandApplication.OnLaunched` clears it. If `Run` is called twice (re-entry, or test rigs), the callback can be overwritten or leaked. |
| Process-wide DPI awareness | set once in `Run` via P/Invoke | Set-once Win32 state. Overwriting after a window has been created is undefined. |
| WinForms `IMessageFilter` chain | added in `IslandApplication.OnLaunched`, never removed | Static-lifetime filter object that holds references to whatever the WinUI runtime captures. |
| `Type` objects exposed to the designer | `ReactorComponentTypeConverter.GetStandardValues` enumerates all assemblies | Designer-process integrity — a hostile assembly loaded into the designer could surface as a "valid" component if it inherits `Component` and has a default ctor. |

---

## 5. STRIDE table

Trust model: developer's own code on the inside; in-process WinUI on the outside; STA thread; no transport. Categories that aren't relevant are listed for completeness with "n/a".

| # | STRIDE | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| 1 | **D**oS | `DesktopWindowXamlSource` instances allocated in `OnHandleCreated` are not always reached by `Dispose(disposing)` — handle-recreation cycles (a WinForms `RecreateHandle()` triggered by parenting changes, font changes, RTL flip, etc.) call `OnHandleCreated` again and overwrite `_source` without disposing the prior one. | Developer triggers handle recreation. | One leaked WinUI XAML source + one leaked child HWND per recreation. Over the life of a long-running app this is unbounded. | Medium — handle recreation is uncommon but not rare in WinForms; any code path that flips `RightToLeft`, parents the control across forms, or sets certain styles will recreate. | None. `OnHandleCreated` does not check whether `_source` is already non-null; `OnHandleDestroyed` is not overridden. | **F-1 (High).** See findings. |
| 2 | **D**oS | The `ReactorHostControl` created by `MountComponentType` (`XamlIslandControl.cs:175-176`) is `IDisposable` and owns reconciler/ETW/overlay state, but no reference is kept to it inside `XamlIslandControl`. `XamlIslandControl.Dispose` calls `_source.Dispose()` and never explicitly disposes the host control. | Long-lived process / many island lifecycles. | Cleanup paths in `ReactorHostControl.Dispose` (`src/Reactor/Hosting/ReactorHostControl.cs:553-579`) — `_reconciler.Dispose()`, ETW consumer, overlay wiring, attribution unbind — never run. Memory and ETW session leaks. | High whenever `ComponentType` path is used (the marketed primary path for designer use). | None. `_source.Dispose()` is documented to dispose its `Content`'s element tree but does **not** call `IDisposable.Dispose()` on the `Content` element. | **F-2 (High).** |
| 3 | **D**oS | `_pendingContent` is set on the public setter when `_source` is null, then "consumed" in `OnHandleCreated` (`:142-146`). If the handle is never created (control assigned but never realized), or if `XamlContent` is set, then replaced, the previous `UIElement` is dropped without `Dispose` even if it is disposable. | Developer-app correctness. | Leaked WinUI elements when content is swapped before realization. | Low–medium. | None. The setter at `:53-63` simply replaces. | **F-3 (Low).** Setter-side leak. Documented as "caller must ensure" but easy to miss. |
| 4 | **D**oS | `XamlIslandBootstrap.Run` is a single-shot bootstrap that mutates process-wide state (DPI, COM wrappers, sync context, the `IslandApplication` singleton, the `IMessageFilter` chain) but has **no reentrancy guard**. Calling `Run` a second time (e.g., from a test runner that hosts multiple message-loop sessions, or from a developer who structures startup oddly) silently mutates state and clobbers `_onReady`. | Developer-app correctness; test-host correctness. | The second call's `_onReady` either overwrites the first's (if first hasn't fired yet) or runs against a state where `XamlApp.Current` already exists and `XamlApp.Start` will throw. | Low. Most apps call this once. | None. | **F-4 (Medium).** No idempotence check, no `Interlocked.CompareExchange` on `_onReady`. |
| 5 | **D**oS | `XamlPreTranslateFilter.PreFilterMessage` is registered via `AddMessageFilter` (`XamlIslandBootstrap.cs:103`) **once** in `OnLaunched` and **never removed**. The filter object lives for process lifetime. It calls `ContentPreTranslateMessage` (a P/Invoke to `Microsoft.UI.Windowing.Core.dll`) for **every** message, including after all islands have been disposed. | Developer-app correctness. | (a) Per-message P/Invoke overhead even when there are no islands. (b) If `Microsoft.UI.Windowing.Core.dll` is unloaded — e.g., the WinUI runtime is shut down explicitly — the next message is a `DllNotFoundException` killing the app. | Low. The Windows App SDK is generally process-loaded for the lifetime of the process. | None. | **F-5 (Low).** Worth a comment in code that the filter is intentionally process-lifetime. |
| 6 | **T**ampering / DoS | `XamlIslandBootstrap.cs:140` calls `ContentPreTranslateMessage` on every message and returns its result as `bool` ("we handled it"). The native function's contract is "returns non-zero if the message was translated and the loop should not dispatch it." The wrapper does no parameter validation: if `m.HWnd`, `m.WParam`, `m.LParam` are values that cause WindowsAppSDK to crash, the entire process dies on the message loop. | A library/app bug that posts pathological messages. | Process crash. | Low — the WinForms loop only delivers shaped MSG structures. | None — pure passthrough. | **F-6 (Low).** Acceptable as designed; verify there is a wrapping `try { } catch (SEHException) { }` policy elsewhere or document that the policy is "fail fast." |
| 7 | **T**ampering | The fallback `SetWindowPos` path in `UpdateBridgeSize` (`XamlIslandControl.cs:278-293`) catches **only** `COMException`. A `SiteBridge.WindowId` getter that throws any other type (e.g., `InvalidOperationException` because the bridge was disposed concurrently, or `NullReferenceException` because of a race) propagates out of `OnResize` / `OnLayout` and crashes the layout pass. | Developer-app correctness; WinUI version drift. | Crash inside layout. | Low. | Try/catch on COMException only. | **F-7 (Low).** Broaden the catch or assert preconditions. |
| 8 | **D**oS | `MountComponentType` at `XamlIslandControl.cs:172-177` calls `Activator.CreateInstance(type)` and casts to `ReactorComponent`. Although `ComponentType`'s setter and `ReactorComponentTypeConverter` are both supposed to validate the type, **the runtime mount path does not re-check** — a developer who sets `_componentType` via an internal hook with a non-`Component` type will get an `InvalidCastException` *and* an orphaned half-initialized instance. | Developer-app correctness. | Crash + half-allocation. | Very low. | Type filtering on the setter / TypeConverter side only; runtime path has no defensive check. | **F-8 (Low).** Add a contract check at mount time. |
| 9 | **D**oS / Tampering | `ReactorComponentTypeConverter.GetStandardValues` calls `asm.GetTypes()` on every assembly in `AppDomain.CurrentDomain` (`ReactorComponentTypeConverter.cs:79`). This will raise `ReflectionTypeLoadException` for assemblies whose dependencies the designer hasn't loaded; the code swallows the whole exception with `catch { }`. The same pattern is in `ConvertFrom`. | Designer-process behavior. | Designer pop-ups suppressed; user sees an empty dropdown. Acceptable, but the bare `catch { }` (no exception filter, no logging) silently hides bugs. | Medium for "user confusion," none for security. | Bare `catch { }`. | **F-9 (Low).** Use `ReflectionTypeLoadException` and `e.Types.Where(t => t is not null)` to recover partial results. |
| 10 | **I**nformation disclosure | `_source.Content` exposes the WinUI element tree to the WinForms `Source` getter. WinForms `Properties` window introspection or designer serialization could surface element references at design time. | Hostile designer extension. | The designer host already trusts loaded assemblies — see Asset row 7. The `Source` property is `[Browsable(false)]` (`XamlIslandControl.cs:120`), so the property grid does not render it. | Low. | `[Browsable(false)]` and `[DesignerSerializationVisibility(Hidden)]` on `XamlContent` (`:51-52`) and `ContentFactory` (`:73-74`). | n/a — mitigated. |
| 11 | **D**oS / **T**ampering | The static `_onReady` field (`XamlIslandBootstrap.cs:30`) is a captured delegate reference. If `Run` returns successfully but `OnLaunched` never fires (because `XamlApp.Start` raises), `_onReady` is leaked for process lifetime, holding any captured form/object graph alive. | Developer-app correctness. | GC root that prevents collection of the dev's startup closure. Memory leak only on a path that is itself an error. | Very low. | Set once, cleared inside `OnLaunched` after invocation. | **F-10 (Low).** Wrap `Start` in `try`/`finally` that nulls out `_onReady`. |
| 12 | **R**epudiation | n/a. No logging, no events. | — | — | — | — | n/a. |
| 13 | **E**oP | `Activator.CreateInstance(type)` on a developer-supplied `Type`. The control restricts to `Component` subclasses with default ctors, but a hostile designer plugin loaded into the IDE could publish a "Component" whose ctor has side effects. This is the designer's trust problem, not Reactor's. | Hostile VS extension installed locally — already arbitrary code. | None additional. | n/a. | n/a. | n/a — out of scope. |
| 14 | **S**poofing | n/a. No identity boundary. | — | — | — | — | n/a. |
| 15 | **A**partment threading consistency | `Run` does not assert `Thread.CurrentThread.GetApartmentState() == ApartmentState.STA` or that there is no existing `SynchronizationContext`. The doc-comment says "Must be called on the STA UI thread," but it is enforced only by `XamlApp.Start` failing later. The sample (`samples/WinFormsInterop/Program.cs:14`) decorates `Main` with `[STAThread]` correctly; a developer who calls from another bootstrap path (e.g., a `Task.Run`-spawned thread, or a test fixture) will hit a confusing failure. | Developer-app correctness. | Confusing crash. | Low–Medium for users who don't read the doc-comment. | Doc-comment only. | **F-11 (Low).** Add an explicit `if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException(...)` at entry. |

---

## 6. Findings

### F-1 — Handle recreation leaks `DesktopWindowXamlSource` and child HWND
**Severity:** High (DoS by handle leak in long-running apps)
**Location:** `src/Reactor.Interop.WinForms/XamlIslandControl.cs:123-170`, `:315-323`

`OnHandleCreated` allocates `_source = new DesktopWindowXamlSource()` (`:129`) without checking whether `_source` is already non-null. WinForms recreates a control's HWND in several scenarios — `RightToLeft` change, certain `ControlStyles` flips, parent-form re-parenting on some platforms, font / rendering-mode changes that flip `IWindow` interfaces. Every recreation:

1. Calls `OnHandleDestroyed` (not overridden — the existing `_source` keeps its stale parent HWND).
2. Calls `OnHandleCreated` (a fresh `_source` is allocated, the old one is leaked).

The old `DesktopWindowXamlSource` retains a child HWND parented to the destroyed parent. Both COM objects and the HWND are leaked until process exit.

**Recommendation:**
```csharp
protected override void OnHandleDestroyed(EventArgs e)
{
    _source?.Dispose();
    _source = null;
    base.OnHandleDestroyed(e);
}

protected override void OnHandleCreated(EventArgs e)
{
    base.OnHandleCreated(e);
    if (DesignMode) return;

    _source?.Dispose();          // belt-and-braces in case OnHandleDestroyed didn't run
    _source = new DesktopWindowXamlSource();
    // …rest unchanged…
}
```

Also: when `_source.Dispose()` is called, capture the previous `_source.Content` so we can decide whether to redisplay it on the new handle (`_pendingContent = old._source.Content;`) — otherwise handle recreation also silently drops the user's content.

---

### F-2 — `ReactorHostControl` created by `MountComponentType` is never disposed
**Severity:** High (DoS by reconciler / ETW / overlay leak)
**Location:** `src/Reactor.Interop.WinForms/XamlIslandControl.cs:172-177`, `:315-323`

```csharp
private void MountComponentType(Type type)
{
    if (_source is null) return;
    var component = (ReactorComponent)Activator.CreateInstance(type)!;
    _source.Content = new ReactorHostControl(component);
}
```

`ReactorHostControl` is `IDisposable` (declared `src/Reactor/Hosting/ReactorHostControl.cs:43`) and `Dispose()` cleans up the reconciler, ETW consumer, overlay wiring, attribution registration, and any pointer/spatial maps (`src/Reactor/Hosting/ReactorHostControl.cs:553-579`). `_source.Dispose()` does **not** call `Dispose` on its `Content` — `DesktopWindowXamlSource` releases its reference to the `UIElement` but the WinForms-side wrapper has no way to notify a `ContentControl` that it should run `IDisposable.Dispose`.

**Result:** every `XamlIslandControl` whose `ComponentType` path was used leaks a `Reconciler`, an `EventRing`, possibly an ETW listener, and the rooted overlay wiring. Over the lifetime of an editor / dashboard / IDE-style app this is a continuous resource bleed.

The `XamlContent` and `ContentFactory` paths have the same problem if the developer sets a disposable `ReactorHostControl` directly — but those are documented as "caller responsible." `MountComponentType` is owned end-to-end by this class.

**Recommendation:** Track the host control in a field and dispose in `Dispose(bool)`:

```csharp
private ReactorHostControl? _ownedHostControl;

private void MountComponentType(Type type)
{
    if (_source is null) return;
    _ownedHostControl?.Dispose();
    var component = (ReactorComponent)Activator.CreateInstance(type)!;
    _ownedHostControl = new ReactorHostControl(component);
    _source.Content = _ownedHostControl;
}

protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        try { _ownedHostControl?.Dispose(); } catch { /* best effort */ }
        _ownedHostControl = null;
        _source?.Dispose();
        _source = null;
    }
    base.Dispose(disposing);
}
```

Also rewire the `ComponentType` setter (`:103-114`) to dispose the previous host before creating a new one, since changing the property at runtime is allowed.

---

### F-3 — `XamlContent` setter and `ContentFactory` swap drop disposable elements without `Dispose`
**Severity:** Low
**Location:** `src/Reactor.Interop.WinForms/XamlIslandControl.cs:53-63`, `:75-85`

The `XamlContent` setter unconditionally overwrites `_source.Content` or `_pendingContent`. If the previous element was a `ReactorHostControl` (or any `IDisposable` `UIElement`), it is dropped. Same story for `ContentFactory` — if the factory is replaced after the source has been initialized, the replacement re-invokes the new factory and overwrites Content without disposing the prior content.

**Recommendation:** Document explicitly, or add `(_source.Content as IDisposable)?.Dispose()` before assignment. The docstring at `:48-49` says "caller must ensure the WinUI object is not created at design time" but is silent on lifetime.

---

### F-4 — `XamlIslandBootstrap.Run` has no reentrancy / idempotence guard
**Severity:** Medium
**Location:** `src/Reactor.Interop.WinForms/XamlIslandBootstrap.cs:65-83`, static `_onReady` at `:30`

Sequential calls clobber `_onReady`. `XamlApp.Start` will throw on the second invocation (the WinUI runtime is single-instance) but only after `_onReady` has been overwritten and only after `SetProcessDpiAwarenessContext` has been called a second time. A test rig that drives `Run` per-test will see hangs or silent first-callback drops.

**Recommendation:**
```csharp
private static int _started;

public static void Run(Action onReady)
{
    if (Interlocked.Exchange(ref _started, 1) != 0)
        throw new InvalidOperationException(
            "XamlIslandBootstrap.Run can only be called once per process.");
    // …existing body…
}
```

---

### F-5 — `XamlPreTranslateFilter` lifetime
**Severity:** Low (informational)
**Location:** `src/Reactor.Interop.WinForms/XamlIslandBootstrap.cs:103`, `:129-142`

`AddMessageFilter` is paired with no `RemoveMessageFilter`. The filter is intentionally process-lifetime; document it. Also: `PreFilterMessage` should not catch / swallow exceptions (currently it does not, which is correct), but this is worth a code comment so a future reviewer doesn't add one.

---

### F-6 — `ContentPreTranslateMessage` passthrough has no guard against late shutdown
**Severity:** Low
**Location:** `src/Reactor.Interop.WinForms/XamlIslandBootstrap.cs:37`, `:140`

The P/Invoke target is `Microsoft.UI.Windowing.Core.dll`, which is loaded for the WinAppSDK lifetime. After `PostQuitMessage(0)` fires (`:117`) but before the process exits, residual `MSG`s may still pump through `XamlPreTranslateFilter`. If the WinAppSDK has already started teardown, `ContentPreTranslateMessage` may take a slow path. Acceptable but worth verifying empirically with a destructive shutdown stress test.

---

### F-7 — Catch on `COMException` only is too narrow
**Severity:** Low
**Location:** `src/Reactor.Interop.WinForms/XamlIslandControl.cs:286-293`

```csharp
catch (COMException)
{
    // WindowId may not be available in all WinAppSDK configurations.
    …
}
```

A future SDK that throws `InvalidOperationException` on the `WindowId` getter, or a thread race with disposal that produces `NullReferenceException`, will not be caught and will surface as a layout-pass crash.

**Recommendation:** Either widen to `catch (Exception ex) when (ex is COMException or InvalidOperationException)`, or precondition-check `_source` and `_source.SiteBridge` are non-null before the block (note: this method has no thread guard — see F-12).

---

### F-8 — `MountComponentType` does no defensive type check
**Severity:** Low
**Location:** `src/Reactor.Interop.WinForms/XamlIslandControl.cs:172-177`

`(ReactorComponent)Activator.CreateInstance(type)!` will throw `InvalidCastException` if `type` is not a `Component` subclass. The setter at `:103-114` accepts arbitrary `Type`, and only the `TypeConverter` filters; binding from XAML or reflection bypasses that filter.

**Recommendation:**
```csharp
private void MountComponentType(Type type)
{
    if (_source is null) return;
    if (!typeof(ReactorComponent).IsAssignableFrom(type) || type.IsAbstract
        || type.GetConstructor(Type.EmptyTypes) is null)
        throw new ArgumentException(
            $"ComponentType must be a concrete Component subclass with a default constructor: {type.FullName}",
            nameof(type));
    var component = (ReactorComponent)Activator.CreateInstance(type)!;
    _source.Content = new ReactorHostControl(component);
}
```

The same check should run in the `ComponentType` setter (`:103-114`) so the bad assignment is rejected before it is stored.

---

### F-9 — Bare `catch { }` in `ReactorComponentTypeConverter`
**Severity:** Low (designer-time UX, not security)
**Location:** `src/Reactor.Interop.WinForms/ReactorComponentTypeConverter.cs:39`, `:54`, `:84`

Three sites swallow every exception, including `OutOfMemoryException`, `StackOverflowException` (technically bare-catch can't catch SO but is often misleading), and `ReflectionTypeLoadException`. The standard pattern is:

```csharp
try
{
    foreach (var t in asm.GetTypes())
        …
}
catch (ReflectionTypeLoadException ex)
{
    foreach (var t in ex.Types.Where(t => t is not null))
        if (IsValidComponentType(t!)) types.Add(t!);
}
```

This both narrows the catch and recovers partial type lists, improving the designer dropdown experience.

---

### F-10 — `_onReady` static field can be leaked on `XamlApp.Start` failure
**Severity:** Low
**Location:** `src/Reactor.Interop.WinForms/XamlIslandBootstrap.cs:30`, `:70-83`

```csharp
_onReady = onReady;
…
XamlApp.Start(_ => { …; new IslandApplication(); });
```

If `XamlApp.Start` throws (for example, a second invocation as in F-4, or a missing WindowsAppSDK runtime), the assigned `_onReady` is still rooted as a static field. The captured closure is held for process lifetime.

**Recommendation:** Wrap in try/finally to ensure `_onReady` is cleared on any failure:

```csharp
_onReady = onReady;
try
{
    XamlApp.Start(_ => { …; new IslandApplication(); });
}
catch
{
    _onReady = null;
    throw;
}
```

---

### F-11 — No STA / apartment assertion in `Run`
**Severity:** Low (developer DX, surfaced as confusing crash)
**Location:** `src/Reactor.Interop.WinForms/XamlIslandBootstrap.cs:65-83`

The doc-comment at `:63` says "Must be called on the STA UI thread," but the code does not enforce. A failure manifests inside `XamlApp.Start` as `RPC_E_CHANGED_MODE` / `CO_E_NOTINITIALIZED` with no breadcrumb pointing back to the apartment requirement.

**Recommendation:** Fast-fail with a clear error:

```csharp
if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
    throw new InvalidOperationException(
        "XamlIslandBootstrap.Run requires an STA thread. " +
        "Decorate Main with [STAThread] or call from an STA-pumped thread.");
```

---

### F-12 — Layout/DPI/resize callbacks have no thread-affinity assertion
**Severity:** Low (informational)
**Location:** `src/Reactor.Interop.WinForms/XamlIslandControl.cs:233-247`, `:263-294`, `:309-313`

`UpdateBridgeSize` accesses `_source.SiteBridge.MoveAndResize` and a SiteBridge HWND. WinForms guarantees `OnResize`/`OnLayout`/`OnDpiChangedAfterParent` come from the UI thread that owns the HWND, but a developer who wires a custom callback into `_contentFactory` that re-enters layout from a different thread will trigger a cross-apartment COM call. No `CheckAccess` / `DispatcherQueue.HasThreadAccess` assertion is performed.

**Recommendation:** This is a property of the public design — the cleanest fix is a doc-comment on `XamlIslandControl` that states "All public members must be touched on the WinForms-owning UI thread," and a `Debug.Assert(InvokeRequired == false)` at the head of `UpdateBridgeSize` to catch dev-time regressions.

---

## 7. Open questions

1. **Does `DesktopWindowXamlSource.Dispose` propagate to the `Content`?** The Windows App SDK source is closed; the public docs are silent. Empirically, it does *not* call `IDisposable.Dispose` on the element. Confirm with the WindowsAppSDK team or via a reflection probe before declaring F-2 fixed.
2. **Is there a known WindowsAppSDK call that re-enters `OnHandleCreated` while `_source` is still alive?** Specifically, do `RightToLeft` flips on the parent control trigger a child-handle recreation while the bridge HWND is parented under the old HWND? F-1 assumes yes; needs an integration-test confirmation.
3. **Does the `IMessageFilter` survive a `WinForms.Application.Restart()` cycle?** If the chain is rebuilt, the filter is leaked from the old session into the new (because `IslandApplication` is a singleton and won't rerun `OnLaunched`). Worth a lab repro.
4. **Should `ComponentType` accept generic `Component` types?** `ReactorComponentTypeConverter.IsValidComponentType` filters by `IsAbstract` and parameterless ctor only; an open-generic `Component<>` would slip through the filter and crash on `Activator.CreateInstance`. Low-likelihood given how Reactor `Component`s are written, but worth a defensive `t.IsGenericTypeDefinition` exclusion.
5. **Trust of `Microsoft.UI.Windowing.Core.dll` `EntryPoint = "ContentPreTranslateMessage"`** — the entrypoint name is unmangled, suggesting `extern "C"`. Confirm that the export remains stable across WinAppSDK majors; if it is renamed, every message dispatch crashes the host. Likely fine but is a single point of failure that pins the SDK.

---

## 8. Out-of-scope referrals

| Concern | Owning chunk |
|---|---|
| `ReactorHostControl.Dispose` correctness (the cleanup target of F-2) — does `_reconciler.Dispose()` actually drain pending callbacks? Are ETW consumers fully torn down? | **Chunk 14** (Reconciler & component model) and **Chunk 15** (Hosting / ETW). |
| The `Component` ctor surface — what does `Activator.CreateInstance(type)` execute? Are there `Component` subclasses with side-effecting ctors? | **Chunk 14**. |
| WinUI XAML element-tree marshaling between WinForms and WinUI threads (none happens here, but if any user-code hook in `_contentFactory` does, it crosses the same boundary the rest of Reactor crosses). | **Chunk 14** / **Chunk 16**. |
| The designer's loaded-assembly trust posture (what plugins are allowed to publish `Component` subclasses to `ReactorComponentTypeConverter`?) | The IDE itself; not Reactor. |
| `samples/WinFormsInterop/**` correctness as illustrative code that might be copied. | **Chunk 18** (sample-app native interop) covers FFI; sample WinForms code is illustrative-only and falls under §10 of `000-chunking-and-threat-model.md` ("Sample apps' application logic"). |
| `tests/Reactor.WinFormsTests.Host/**` — the test host. Test code is out of scope per `000-chunking-and-threat-model.md` §10. |
| WindowsAppSDK / WinUI internal correctness. Trusted dependency per the global trust model. |

---

## Summary

The chunk's surface is small (≈563 LoC), has no transport, no parser, and no persisted state. The threat picture is dominated by **availability** (handle / COM / reconciler leaks compounding over the life of a long-running developer app) and a few minor **tampering** corner-cases at the boundary with WinAppSDK's COM surface.

Two findings are genuinely actionable and should ship as fixes:

- **F-1** — handle recreation leaks the `DesktopWindowXamlSource` (override `OnHandleDestroyed`).
- **F-2** — the `ReactorHostControl` created by `MountComponentType` is never disposed, leaking the entire reconciler subtree.

The remaining nine findings are quality / defensive improvements, all low severity. There are **no information-disclosure findings** — window-content marshaling does not cross any trust boundary in this chunk, and designer-time properties are correctly attributed `[Browsable(false)]` / `[DesignerSerializationVisibility.Hidden]`.
