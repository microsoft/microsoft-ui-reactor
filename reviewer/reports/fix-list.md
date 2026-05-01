# Code Review Fix List

**Generated**: 2026-04-11
**Total Findings**: 139
**By Severity**: Critical: 1, High: 28, Medium: 85, Low: 25
**Reports Consolidated**: 35

---

## F001
- **File**: ReactorCharting/Scale/LogScale.cs:37-42
- **Severity**: critical
- **Priority**: P0
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `LogScale.Invert()` performs linear interpolation instead of exponential, returning mathematically incorrect results. For domain [1,100] and range [0,1]: `Map(10)=0.5` (correct) but `Invert(0.5)=50.5` instead of `10`.
- **Evidence**: `BuildPiecewise` with `isLog:false` applies linear normalize+interpolate but never applies `Pow()` to convert back from log-space. `PowScale` correctly applies `InversePowTransform` — LogScale omits the equivalent.
- **Fix**: Add exponential transform to the inverse path: compute `Log(r0)` and `Log(r1)`, interpolate linearly in log-space, then exponentiate.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F002
- **File**: Reactor/Core/Reconciler.cs:470-534
- **Severity**: high
- **Priority**: P1
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-08
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `UnmountRecursive` does not clean up animation-related event subscriptions or static dictionary entries for `InteractionStates`, `KeyframeAnimations`, or `ScrollAnimations`, while the parallel `UnmountAndCollect` (lines 627-698) does.
- **Evidence**: `UnmountAndCollect` lines 641-649 checks and clears interaction/keyframe/scroll state. `UnmountRecursive` has no equivalent cleanup.
- **Fix**: Add the same animation state cleanup block from `UnmountAndCollect` to `UnmountRecursive` before the component node cleanup at line 484.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F003
- **File**: Reactor/Core/Reconciler.cs:1121
- **Severity**: high
- **Priority**: P1
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-08
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `_interactionTrackers` is a `static readonly Dictionary<UIElement, InteractionStateTracker>` that holds strong references preventing GC. Same issue for `_keyframeTriggerValues` at line 1375. Without cleanup from UnmountRecursive (F002), entries accumulate.
- **Evidence**: Entries added in `ApplyInteractionStates` (line 1144). Removal only in `ClearInteractionStates` (line 1188), which is not called from `UnmountRecursive`.
- **Fix**: Resolved by F002's fix. Additionally, consider `ConditionalWeakTable<UIElement, InteractionStateTracker>` for defense-in-depth. Also flagged by: general (general-batch-1)
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F004
- **File**: Reactor/Core/Reconciler.cs:374-380
- **Severity**: high
- **Priority**: P1
- **Domain**: concurrency
- **Pattern**: CONC-RACE-02
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `CreateComponentRerender` closure accesses `_componentNodes` (non-thread-safe `Dictionary`) from any thread invoking `_requestRerender`. Thread-safe `UseState` setters and `UseCommand` invoke this from background threads.
- **Evidence**: Closure calls `_componentNodes.TryGetValue(control, out var node)` while the UI thread may be modifying it during mount/unmount reconciliation.
- **Fix**: Move `SelfTriggered = true` logic to the beginning of `ReconcileComponent` (UI thread guaranteed), or use `ConcurrentDictionary`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F005
- **File**: Reactor/Core/Reconciler.cs:402-422
- **Severity**: high
- **Priority**: P1
- **Domain**: performance
- **Pattern**: CS-PERF-ALLOC
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ShouldUpdateWithProps` uses `Type.GetMethod` with `System.Reflection` on every component reconciliation. Allocates `MethodInfo[]`, `ParameterInfo[]`, `new object[]` and boxes `bool` return per call.
- **Evidence**: Lines 406-418: `compType.GetMethod("ShouldUpdate", ...)` performs full reflection scan per reconciliation. `Component<TProps>` already has the virtual method.
- **Fix**: Add `IPropsComparable` interface with `CompareProps(object?, object?)` method. Implement on `Component<TProps>` to dispatch without reflection.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F006
- **File**: Reactor/Core/Reconciler.cs:2138-2149
- **Severity**: high
- **Priority**: P1
- **Domain**: api-design
- **Pattern**: CS-API-LOCALE-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ParseColumnDef`/`ParseRowDef` use `double.TryParse` without `CultureInfo.InvariantCulture`. Grid strings like `"0.5*"` break on non-English locales where decimal separator is `,`.
- **Evidence**: Line 2138: `double.TryParse(def, out var px)` — no culture override. Dsl.cs:245 `$"{starValue:F6}*"` is also culture-dependent.
- **Fix**: Use `CultureInfo.InvariantCulture` in both parse and format sides.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F007
- **File**: Reactor/Core/RenderContext.cs:670-686
- **Severity**: high
- **Priority**: P1
- **Domain**: error-handling
- **Pattern**: EH-EXC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `UseCommand` re-entrance guard `if (isExecuting) return;` captures a stale value from the render closure. Between `setIsExecuting(true)` and next re-render, the old closure still has `isExecuting = false` — TOCTOU race allows double execution.
- **Evidence**: Line 667-670: `isExecuting` in the lambda body is the value captured at memo-creation time. Rapid clicks before re-render bypass the guard.
- **Fix**: Use `UseRef(false)` for a ref-based guard that reads live value: `if (guardRef.Current) return; guardRef.Current = true;`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F008
- **File**: Reactor/Core/RenderContext.cs:675-688
- **Severity**: high
- **Priority**: P1
- **Domain**: concurrency
- **Pattern**: CONC-ASYNC-05
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `_ = Task.Run(async () => { ... })` in `UseCommand` discards the Task. If `asyncAction()` throws, the exception is silently swallowed. User receives no error feedback.
- **Evidence**: Lines 675-688: `_ =` discard. The `finally` block resets `setIsExecuting(false)`, masking the failure entirely.
- **Fix**: Add a catch block that surfaces the error via a callback/error state, or log via `Debug.WriteLine` at minimum.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F009
- **File**: Reactor/Core/ObservableTreeTracker.cs:101-132
- **Severity**: high
- **Priority**: P1
- **Domain**: concurrency
- **Pattern**: CONC-RACE-02
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `OnNestedPropertyChanged` mutates non-thread-safe `_subscriptions` Dictionary and `_visiting` HashSet via `SyncSubscriptions()`, but `PropertyChanged` events can fire from any thread.
- **Evidence**: Lines 15-17: non-thread-safe collections. `_requestRerender()` is thread-safe but `SyncSubscriptions(root)` at line 127 is not.
- **Fix**: Marshal `SyncSubscriptions` to UI thread via `DispatcherQueue.TryEnqueue`, or replace with `ConcurrentDictionary`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F010
- **File**: Reactor/Hosting/PreviewCaptureServer.cs:62
- **Severity**: high
- **Priority**: P1
- **Domain**: concurrency
- **Pattern**: CONC-ASYNC-05
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `_ = ListenAsync()` discards the Task. If `GetContextAsync()` throws an unexpected exception, the listener loop silently dies. Preview server stops accepting requests with no error logged.
- **Evidence**: Line 62: explicit discard. `ListenAsync()` only catches `ObjectDisposedException` and `HttpListenerException`.
- **Fix**: Add `.ContinueWith(t => Console.Error.WriteLine(...), TaskContinuationOptions.OnlyOnFaulted)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F011
- **File**: Reactor/Yoga/YogaAlgorithm.cs:24-36
- **Severity**: high
- **Priority**: P1
- **Domain**: concurrency
- **Pattern**: CONC-RACE-01
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `s_currentGenerationCount++` is a non-atomic read-modify-write on a static field at the entry of every layout calculation. Duplicated generation counts cause cache validation bugs.
- **Evidence**: Line 24: `private static uint s_currentGenerationCount;`. Line 36: `s_currentGenerationCount++;`. Class comment acknowledges: "Thread-unsafe."
- **Fix**: Replace with `Interlocked.Increment(ref s_currentGenerationCount)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F012
- **File**: samples/apps/monaco-editor/Monaco/MonacoEditor.cs:255
- **Severity**: high
- **Priority**: P1
- **Domain**: error-handling
- **Pattern**: CS-PERF-UI-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `PushAllStateAsync` embeds `EditorFontSize` into JavaScript via default interpolation. On non-English locales, `14.5` renders as `"14,5"`, producing invalid JS `monacoSetFontSize(14,5)`.
- **Evidence**: Line 255 lacks culture spec. Line 293 correctly uses `CultureInfo.InvariantCulture` for the same value — proving this is an oversight.
- **Fix**: `await ExecuteScriptAsync($"monacoSetFontSize({EditorFontSize.ToString(CultureInfo.InvariantCulture)})");`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F013
- **File**: samples/apps/monaco-editor/Monaco/MonacoEditor.cs:125-167
- **Severity**: high
- **Priority**: P1
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-08
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: Race between WebView2 async initialization and control unload causes permanent double `WebMessageReceived` subscription. If `OnUnloaded` fires while `EnsureCoreWebView2Async` is awaiting, unsubscribe is skipped.
- **Evidence**: Line 197: guard `_webView?.CoreWebView2 is not null` — if null at unload, no-op. Line 167 subscribes. Line 125 subscribes again on recycled load. Two active subscriptions permanently.
- **Fix**: Always unsubscribe before subscribing: `coreWv.WebMessageReceived -= OnWebMessageReceived; coreWv.WebMessageReceived += OnWebMessageReceived;`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F014
- **File**: Reactor/PropertyGrid/ReflectionTypeMetadataProvider.cs:205-221
- **Severity**: high
- **Priority**: P1
- **Domain**: error-handling
- **Pattern**: EH-NULL-03
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Constructor-matching `Compose` lambda looks up updates using camelCase constructor parameter names against a PascalCase dictionary. For C# records, every immutable edit is silently dropped.
- **Evidence**: Line 211: `updates.TryGetValue(paramName, out var updatedValue)` — `paramName` is camelCase, `updates` keys are PascalCase. Line 191: `paramToProperty` map uses `OrdinalIgnoreCase` — confirming developers were aware of casing.
- **Fix**: Look up by property name instead of parameter name, or use case-insensitive dictionary.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F015
- **File**: Reactor.Cli/Loc/TranslateCommand.cs:118-137
- **Severity**: high
- **Priority**: P1
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `--missing-only` and `else` branches contain identical code. The flag has zero effect on runtime behavior despite being documented in help text.
- **Evidence**: Lines 118-127 and 128-137 are character-for-character identical — both skip only human-reviewed translations.
- **Fix**: The `--missing-only` branch should skip ALL existing translations: `if (existingKeys.ContainsKey(entry.Key)) continue;`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F016
- **File**: Reactor.LocGenerator/LocSourceGenerator.cs:162
- **Severity**: high
- **Priority**: P1
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `entry.Key` and `ns` embedded directly into C# string literals without escaping `\` or `"`. A key containing these characters produces broken generated code.
- **Evidence**: Line 162: `$"...new(\"{ns}\", \"{entry.Key}\");"` — raw interpolation. Keys come from `.resw` XML attribute values.
- **Fix**: Add `EscapeStringLiteral` helper: `s.Replace("\\", "\\\\").Replace("\"", "\\\"")` and apply to both `ns` and `entry.Key`.
- **Manager Decision**: DECLINED
- **Implementation**: :black_square_button: Not started

---

## F017
- **File**: Reactor/Animation/TransitionEngine.cs:27-34, 155-174
- **Severity**: high
- **Priority**: P1
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `RunTransition`'s `SuppressTransition` and `RunFade` don't reset stale `Offset`/`Scale` on the incoming visual. Cached pages animated out via `RunSlide` carry stale compositor properties.
- **Evidence**: `SuppressTransition` sets only `Opacity=1`. `RunFade` only animates Opacity. `RunSlide` at line 113 explicitly sets `inVisual.Offset = inStart` — proving the pattern is needed.
- **Fix**: Reset `inVisual.Offset = Vector3.Zero; inVisual.Scale = Vector3.One;` at the top of `RunTransition`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F018
- **File**: Reactor/Animation/ScrollAnimation.cs:27-57
- **Severity**: high
- **Priority**: P1
- **Domain**: error-handling
- **Pattern**: CS-API-LOCALE-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: All expression-building methods embed `float` parameters via default interpolation. On non-English locales, `0.5f` produces `"0,5"` — malformed compositor expressions.
- **Evidence**: Line 27: `$"scroll.Translation.Y * {factor}f"` — on `de-DE` produces invalid expression. All four methods affected.
- **Fix**: Use `FormattableString.Invariant($"scroll.Translation.Y * {factor}f")` for all interpolated expressions.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F019
- **File**: Reactor/Hosting/ReactorHost.cs:16-355
- **Severity**: high
- **Priority**: P1
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-01
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `ReactorHost` holds `Reconciler` (IDisposable) but doesn't implement `IDisposable`. Window close handler only sets `_disposed = true` — never calls cleanup. `ReactorApp.ActiveHost` (static) never cleared.
- **Evidence**: Line 94: `_window.Closed += (_, _) => _disposed = true;`. Compare with `ReactorHostControl.Dispose()` lines 392-409 which correctly runs all cleanups.
- **Fix**: Implement `IDisposable`. In `Dispose()`: run cleanups, dispose reconciler, null references, clear `ActiveHost`. Change `Closed` handler to call `Dispose()`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F020
- **File**: Reactor/Navigation/NavigationStack.cs:81
- **Severity**: high
- **Priority**: P1
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-08
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `OnChanged` delegate property is set during hook execution but never cleared during unmount. Holds strong reference to component's render infrastructure, preventing GC.
- **Evidence**: Set in RenderContext.cs:474. `LifecycleGuard` is cleared (Reconciler.cs:497) but `OnChanged` has no equivalent cleanup path.
- **Fix**: Add `Detach()` method to `INavigationHandle` that nulls `OnChanged`, `Guard`, `LifecycleGuard`. Call during unmount.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F021
- **File**: ReactorCharting/Scale/BandScale.cs:172-179
- **Severity**: high
- **Priority**: P1
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `PointScale<T>.Copy()` does not preserve the `Padding` value. The `Padding` property is write-only (no getter), so Copy can't read it.
- **Evidence**: Lines 172-179: Copy sets Domain, Range, Align but not Padding. Line 164: `set => _band.PaddingOuter = value;` — no getter.
- **Fix**: Add getter: `get => _band.PaddingOuter;` and copy it: `copy.Padding = Padding;`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F022
- **File**: ReactorCharting/Shape/Curve.cs:338-340
- **Severity**: high
- **Priority**: P1
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `CurveCardinal.BezierCurveTo` uses wrong point differences for control points. D3 reference uses `(_x2 - _x0)` for CP1 and `(_x1 - x)` for CP2; the port uses `(x - _x0)` for both.
- **Evidence**: Lines 338-340 compute both control points with `(x - _x0)` (3-point span) instead of correct 2-point spans, producing visually incorrect cardinal splines.
- **Fix**: Replace with: `_x1 + _k * (_x2 - _x0), _y1 + _k * (_y2 - _y0), _x2 - _k * (x - _x1), _y2 - _k * (y - _y1), _x2, _y2`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F023
- **File**: ReactorCharting/Layout/Treemap.cs:142-174
- **Severity**: high
- **Priority**: P1
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `TileSquarify` does not implement the squarify algorithm. Contains scaffolding that doesn't influence output. Falls through to simple `TileDice`/`TileSlice`. Six local variables are dead code.
- **Evidence**: Lines 147-149: `remaining`, `row`, `rowValue` accumulated but never used. Lines 170-173: unconditionally delegate to TileDice/TileSlice. This is the default tiling mode.
- **Fix**: Implement full squarify algorithm with row-flushing logic per d3-hierarchy's `squarify.js`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F024
- **File**: ReactorCharting/Voronoi/Delaunay.cs:97-121
- **Severity**: high
- **Priority**: P1
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Triangulation retains seed triangle AND creates fan triangles from every point, producing overlapping triangles. Not Bowyer-Watson as documented.
- **Evidence**: Seed triangle at line 101 never removed. Fan sub-triangles at lines 113-115 overlap the seed. Downstream Neighbors() and Voronoi() produce incorrect results.
- **Fix**: Replace fan triangulation with correct Delaunay algorithm. At minimum, remove seed triangle when subdivided by interior points.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F025
- **File**: vscode-reactor/src/extension.ts:309
- **Severity**: high
- **Priority**: P1
- **Domain**: security
- **Pattern**: SEC-INJ-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `killPreviewProcess` passes `pid` into a `taskkill` command via `cp.execSync` string interpolation — shell command injection pattern.
- **Evidence**: Line 309: `` cp.execSync(`taskkill /T /F /PID ${pid}`, { stdio: "ignore" }) ``.
- **Fix**: Use array-argument form: `cp.execFileSync("taskkill", ["/T", "/F", "/PID", pid.toString()], { stdio: "ignore" });`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F026
- **File**: vscode-reactor/src/extension.ts:418-422
- **Severity**: high
- **Priority**: P1
- **Domain**: security
- **Pattern**: SEC-INJ-03
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Component names from preview server interpolated into HTML without escaping. `"`, `<`, `>` in names break HTML structure.
- **Evidence**: Lines 419-422: `` `<option value="${c}">${c}</option>` `` — no `escapeHtml()`.
- **Fix**: Add HTML-escape helper and apply: `` `<option value="${escapeHtml(c)}">${escapeHtml(c)}</option>` ``.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F027
- **File**: Reactor/Hosting/ReactorApp.cs:176-201
- **Severity**: high
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-03
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `PreviewCaptureServer` instance created and started but never disposed. Holds `HttpListener` (binds network port) and `DispatcherQueueTimer`.
- **Evidence**: Line 176: `var server = new PreviewCaptureServer(...)`. Line 201: `server.Start()`. No `Dispose()` or `using`.
- **Fix**: `host.Window.Closed += (_, _) => server.Dispose();`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F028
- **File**: samples/apps/monaco-editor/Monaco/MonacoEditor.cs:32-37
- **Severity**: high
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-01
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `MonacoEditor` creates `WebView2` (wraps Chromium browser process) at line 32. No code path ever calls `_webView.Close()`. ElementPool keeps WebView2 alive indefinitely.
- **Evidence**: No `Close()`, `Dispose()`, or finalizer in MonacoEditor.cs. ElementPool has no drain/shutdown method.
- **Fix**: Add `Dispose()` on MonacoEditor calling `_webView?.Close()`. Have ElementPool implement IDisposable with drain.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F029
- **File**: Reactor/Animation/TransitionEngine.cs:40-76
- **Severity**: high
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-03
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `CompositionScopedBatch` (IDisposable) created at line 40 is never disposed. The batch must stay alive until `Completed` fires, but is never disposed afterward.
- **Evidence**: Line 40: creates batch. Line 68: `batch.End()`. Line 69-76: `Completed` handler doesn't dispose the batch.
- **Fix**: Dispose in `Completed` handler: `batch.Completed += (_, _) => { /* existing code */; batch.Dispose(); };`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F030
- **File**: Reactor/Core/Reconciler.cs:342-346
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `catch (Exception ex) when (_errorBoundaryDepth == 0)` catches all exceptions including `OutOfMemoryException` and `StackOverflowException`.
- **Evidence**: Line 342: catches base `Exception`. Line 345: replaces component with `TextElement` error message.
- **Fix**: Filter: `catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F031
- **File**: Reactor/Core/Reconciler.cs:476-481, 630-638
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Four bare `catch { }` blocks around ConnectedAnimationService operations silently swallow all exceptions, not just "not available" cases.
- **Evidence**: Lines 481, 638, 1557, 1571 all have bare catch blocks. Comment says "may not be available" but catch swallows everything.
- **Fix**: Catch only expected types: `catch (COMException) { }` or add `Debug.WriteLine` in catch body.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F032
- **File**: Reactor/Core/Reconciler.cs:89
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `EnableBitmaskDiff` is `public static bool` with no volatile or Interlocked access. Process-global, not thread-safe, modifiable by any code.
- **Evidence**: Line 89: `public static bool EnableBitmaskDiff { get; set; }`. On ARM64 without barrier, writes may not be visible.
- **Fix**: Make instance property on `Reconciler`, or declare backing field `volatile`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F033
- **File**: Reactor/Core/Reconciler.cs:2335-2352
- **Severity**: medium
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-02
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `Dispose()` doesn't unmount `NavigationHostNode.CurrentChildControl` for active navigation hosts. Non-component controls with animation state leak.
- **Evidence**: Dispose iterates `_navigationHostNodes.Values`, clears cache, but no `UnmountRecursive(node.CurrentChildControl)`.
- **Fix**: Add `UnmountRecursive(node.CurrentChildControl)` before `node.Cache?.Clear()`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F034
- **File**: Reactor/Core/Reconciler.cs:2335-2352
- **Severity**: medium
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-02
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `Dispose()` doesn't clean up `ElementPool _pool`. Pool holds up to 32 recycled controls per type.
- **Evidence**: Line 28: `private readonly ElementPool _pool = new();`. Dispose never calls pool cleanup. ElementPool has no `Clear()` method.
- **Fix**: Add `ElementPool.Clear()` method and call from `Reconciler.Dispose()`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F035
- **File**: Reactor/Core/ElementPool.cs:220-224
- **Severity**: medium
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-POOL-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `CleanElement` for TextBlock only resets 3 properties, but MountText conditionally sets 6 more (FontStyle, TextWrapping, TextAlignment, TextTrimming, IsTextSelectionEnabled, FontFamily). Also missing: ToggleSwitch doesn't reset IsEnabled; common block missing RenderTransform/FlowDirection.
- **Evidence**: CleanElement lines 220-224 vs MountText lines 221-229. Also ToggleSwitch case (line 272) missing `toggle.IsEnabled = true;`.
- **Fix**: Add `ClearValue` calls for all conditionally-set properties. Add `toggle.IsEnabled = true;` to ToggleSwitch case. Also flagged by: lifecycle (lifecycle-batch-2)
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F036
- **File**: Reactor/Core/ChildReconciler.cs:456-461
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `GetKey` called with `-1` positionalIndex for live panel elements produces `"__pos_-1_TypeName"` for ALL unkeyed elements of same type, causing them to map to the same key. Only first is indexed via `TryAdd`.
- **Evidence**: Line 277: `GetKey(tagElement, -1)`. Line 278: `TryAdd` doesn't overwrite. Line 312-313: lookup uses real positional index — mismatch.
- **Fix**: Use loop variable `i` as positional index at line 277: `GetKey(tagElement, i)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F037
- **File**: Reactor/Core/ObservableTreeTracker.cs:135-142
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `FindRoot()` relies on undocumented Dictionary insertion order to return the root. After `Remove` + `Add` cycles in `SyncSubscriptions`, enumeration order is not guaranteed.
- **Evidence**: Line 138: comment "Dictionary preserves insertion order" — not true after removals. Lines 46-56: `SyncSubscriptions` removes and adds keys.
- **Fix**: Store root explicitly: `private INotifyPropertyChanged? _root;` set in `SyncSubscriptions`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F038
- **File**: Reactor/Core/ObservableTreeTracker.cs:86-95
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Bare `catch` in `Walk` and `OnNestedPropertyChanged` silently swallows all exceptions from `prop.GetValue(node)`, including fatal ones.
- **Evidence**: Lines 86-95 and 113-132: bare catch blocks with no logging. `ILogger` exists but is not used.
- **Fix**: Narrow to non-fatal: `catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)` and log.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F039
- **File**: Reactor/Core/PersistedStateCache.cs:11-19
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-NULL-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `TryGet<T>` performs unchecked cast `(T)boxed!`. Type mismatch throws `InvalidCastException` — surprising for a `TryGet` pattern.
- **Evidence**: Line 15: `value = (T)boxed!;`. No `is T` check.
- **Fix**: Guard: `if (_cache.TryGetValue(key, out var boxed) && boxed is T typed) { value = typed; return true; }`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F040
- **File**: Reactor/Core/PersistedStateCache.cs:9
- **Severity**: medium
- **Priority**: P2
- **Domain**: concurrency
- **Pattern**: CONC-RACE-01
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: Static `Dictionary<string, object?>` with no synchronization. All methods perform unsynchronized reads/writes.
- **Evidence**: Line 9: `private static readonly Dictionary<string, object?> _cache = new();`. No lock or ConcurrentDictionary.
- **Fix**: Replace with `ConcurrentDictionary<string, object?>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F041
- **File**: Reactor/Hosting/ReactorHost.cs:303-306
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: CS-ERR-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ReactorHost.Render()` rethrows outer exceptions (crashes app), while `ReactorHostControl.Render()` calls `ShowErrorFallback(ex)` (shows error UI). Behavioral divergence for the same reconciliation bug.
- **Evidence**: ReactorHost line 306: `throw;`. ReactorHostControl line 346: `ShowErrorFallback(ex);`. Both have identical `ShowErrorFallback` methods.
- **Fix**: Replace `throw;` with `ShowErrorFallback(ex);` in ReactorHost.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F042
- **File**: Reactor/Hosting/ReactorHost.cs:128-136
- **Severity**: medium
- **Priority**: P2
- **Domain**: concurrency
- **Pattern**: CONC-RACE-04
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: TOCTOU race in `RequestRender` between `_isRendering` check and `_renderPending` CAS can lose a state update.
- **Evidence**: Between `_isRendering = false` (line 310) and `Interlocked.Exchange(ref _renderPending, 0)` (line 150), a background thread's CAS fails silently.
- **Fix**: After CAS fails, set `_needsRerender = true` as fallback.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F043
- **File**: Reactor/Hosting/ReactorHost.cs:30-31
- **Severity**: medium
- **Priority**: P2
- **Domain**: concurrency
- **Pattern**: CONC-RACE-06
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `_isRendering` and `_needsRerender` are plain `bool` fields accessed cross-thread without `volatile`. On ARM64, writes may not be visible.
- **Evidence**: Lines 30-31: plain `bool`. `RequestRender()` is called from background threads via `UseState(threadSafe: true)`.
- **Fix**: Declare both as `volatile bool`. Apply same in ReactorHostControl.cs:55-56.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F044
- **File**: Reactor/Hosting/ReactorHost.cs:321-331
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `AttachThemeListener` subscribes handler to the first root element and sets `_themeListenerAttached = true`. If root is replaced, handler stays on old control; new root has no listener.
- **Evidence**: Line 322-324: guard `if (_themeListenerAttached) return;` prevents subscribing to new control.
- **Fix**: Track the subscribed element. On replacement, subscribe to the new element.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F045
- **File**: Reactor/Hosting/ReactorApp.cs:33
- **Severity**: medium
- **Priority**: P2
- **Domain**: concurrency
- **Pattern**: CONC-RACE-01
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: `ActiveHost` is a static auto-property without `volatile`. Written on UI thread, read from hot reload background thread.
- **Evidence**: Line 33: plain auto-property. Sibling `_options` correctly uses `Volatile.Read/Write`.
- **Fix**: Use `Volatile.Read/Volatile.Write` pattern matching `Options`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F046
- **File**: Reactor/Hosting/PreviewCaptureServer.cs:122-127
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `OnCaptureTimerTick` wraps entire frame capture in bare `catch` at 10-30 FPS. No logging at all — in Release builds, errors are completely invisible.
- **Evidence**: Lines 122-127: `catch { // Swallow capture errors }`. Includes `OutOfMemoryException`, `AccessViolationException`.
- **Fix**: Add first-occurrence logging with counter to avoid log spam.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F047
- **File**: Reactor/Hosting/PreviewCaptureServer.cs:152-154
- **Severity**: medium
- **Priority**: P2
- **Domain**: security
- **Pattern**: SEC-AUTH-07
- **Agent**: security
- **Status**: :black_square_button: PENDING
- **Finding**: `Access-Control-Allow-Origin: *` on all responses. Any webpage can silently read preview frames, enumerate components, switch active component.
- **Evidence**: Lines 152-154: wildcard CORS headers on every response.
- **Fix**: Restrict to VS Code webview origin, or use a shared secret token.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F048
- **File**: Reactor/Animation/AnimationScope.cs:61-79
- **Severity**: medium
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-03
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `CompositionScopedBatch` created at line 71 in `WithAnimationAsync` is never ended if user-supplied `action` throws. `batch.End()` at line 76 not in try/finally.
- **Evidence**: Line 74: `WithAnimation(curve, action)` invokes user code. If it throws, line 76 skipped. Orphaned batch captures subsequent animations.
- **Fix**: Wrap in try/catch: `try { WithAnimation(curve, action); } catch { batch.End(); throw; }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F049
- **File**: samples/apps/monaco-editor/Monaco/MonacoEditor.cs:331-341
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ExecuteWithErrorHandling` catches all `Exception` types and writes to `Debug.WriteLine` (no-op in Release). Four locations silently swallow all exceptions.
- **Evidence**: Lines 331-341: `catch (Exception ex) { Debug.WriteLine(...); }`. In Release builds, every error is invisible.
- **Fix**: Replace `Debug.WriteLine` with `Trace.TraceWarning` or `ILogger`. Catch only expected types (`InvalidOperationException`, `COMException`).
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F050
- **File**: samples/apps/monaco-editor/Monaco/MonacoEditor.cs:368-369
- **Severity**: medium
- **Priority**: P2
- **Domain**: security
- **Pattern**: SEC-INJ-03
- **Agent**: interop
- **Status**: :black_square_button: PENDING
- **Finding**: `UpdateOptions` passes `optionsJson` directly into JS expression via `ExecuteScriptAsync` without sanitization. Only method that doesn't use `JsonSerializer.Serialize`.
- **Evidence**: Line 368-369: raw string injection. Line 375 (`FindText`) correctly serializes.
- **Fix**: `ExecuteScriptAsync($"monacoUpdateOptions(JSON.parse({JsonSerializer.Serialize(optionsJson, ...)}))");`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F051
- **File**: Reactor/Navigation/NavigationStack.cs:76, 241-244
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Guard` declared as `Func<NavigatingFromContext, bool>?` but `InvokeGuard` discards the return value. Cancellation is solely via `ctx.Cancel()`.
- **Evidence**: Line 243: `Guard?.Invoke(ctx);` — result discarded. Line 244: `return !ctx.IsCancelled;`.
- **Fix**: Change to `Action<NavigatingFromContext>?` to match actual contract.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F052
- **File**: Reactor/Navigation/NavigationHandle.cs:247-257
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-NULL-04
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `NavigationState<TRoute>.Current` declared with `default!`. Deserialized null flows through `RestoreState` violating `where TRoute : notnull` constraint.
- **Evidence**: Line 269: `= default!;`. Line 249: only checks wrapper non-null, not individual properties.
- **Fix**: Validate: `if (state.Current is null) throw new JsonException("Navigation state must include a non-null 'current' route.");`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F053
- **File**: Reactor/Navigation/NavigationHandle.cs:123-148
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Navigate` sets `_pendingTransitionOverride` before guard check. If guard cancels, override persists. Next successful GoBack/GoForward uses the stale transition.
- **Evidence**: Line 126: set before guard. Lines 147-148: no cleanup on failure.
- **Fix**: Clear override when navigation fails: `if (!success) _pendingTransitionOverride = null;`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F054
- **File**: Reactor/Core/Localization/IntlAccessor.cs:148-163
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `FormatNumber` applies Min/MaxFractionDigits sequentially to a single `NumberDecimalDigits` property. When `min > max`, minimum is silently violated. `NumberDecimalDigits` can't express a range.
- **Evidence**: Lines 153-156: `Math.Max` then `Math.Min`. The "N" format always emits exactly `NumberDecimalDigits` digits.
- **Fix**: Use `Math.Clamp(nfi.NumberDecimalDigits, min, max)` with validation that `min <= max`, or use custom format string.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F055
- **File**: Reactor/Core/Localization/IntlAccessor.cs:266-280
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: CS-PERF
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ToArgsDictionary` uses `GetProperties` + `GetValue` reflection on every `Message()` call during render. Anonymous objects always take the reflection path.
- **Evidence**: Lines 266-280: `args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)` allocates `PropertyInfo[]` per call.
- **Fix**: Cache with `ConcurrentDictionary<Type, PropertyInfo[]>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F056
- **File**: Reactor/Core/Localization/MessageCache.cs:27-34
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: CS-PERF
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Format` allocates `new Dictionary<string, object?>()` on every call where `args` is null/empty. Called from render path — one allocation per localized string per render.
- **Evidence**: Lines 30-32: allocates empty dictionary or copies existing dictionary on every call.
- **Fix**: Use static empty dictionary: `private static readonly IReadOnlyDictionary<string, object?> EmptyArgs = new Dictionary<string, object?>();`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F057
- **File**: Reactor/Core/Localization/ReswResourceProvider.cs:107-130
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ParseReswFile` catches bare `Exception` and returns `null`. Swallows fatal exceptions. `Debug.WriteLine` stripped in Release.
- **Evidence**: Lines 109-130: `catch (Exception ex) { Debug.WriteLine(...); return null; }`.
- **Fix**: `catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F058
- **File**: Reactor/Core/Localization/IntlAccessor.cs:117-143
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: CS-PERF-UI-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Asset()` calls `File.Exists()` (synchronous I/O) up to twice on the UI thread during component render.
- **Evidence**: Lines 127, 137: `System.IO.File.Exists()`. Called during render via `UseIntl()` hook.
- **Fix**: Cache resolution results in `Dictionary<string, string>` keyed by input path.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F059
- **File**: Reactor/Core/Localization/NumberFormatOptions.cs:16
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `CurrencyCode` is a public property never read anywhere. Users setting `CurrencyCode = "EUR"` get no effect.
- **Evidence**: Grep returns one result: the declaration. `IntlAccessor.FormatNumber` never reads it.
- **Fix**: Either implement support or remove the property until functionality ships.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F060
- **File**: Reactor/Core/CommandInterop.cs:40-56
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `FromCommand<T>` hardcodes `CanExecute = true`, discarding `ICommand.CanExecute` semantics. Comments claim per-call evaluation that doesn't exist.
- **Evidence**: Line 51: `CanExecute = true` literal. XML doc claims CanExecute is called on ICommand but it isn't.
- **Fix**: Fix misleading comments, or add `Func<T, bool>? CanExecutePredicate` to `Command<T>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F061
- **File**: Reactor/Elements/Dsl.cs:755
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: CS-ERR-UNSAFE
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `FilterChildren` fast path passes through `EmptyElement` instances while the slow (expansion) path removes them — inconsistent semantics.
- **Evidence**: Line 749: only checks for `null` and `GroupElement`. Line 765: correctly filters `EmptyElement`.
- **Fix**: Add `EmptyElement` to fast-path check: `if (children[i] is null or GroupElement or EmptyElement)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F062
- **File**: Reactor/Elements/Dsl.cs:234-263
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-03
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `InterspersedGrid` doesn't validate proportions are non-negative. Negative values produce invalid grid strings that cause WinUI `ArgumentException`.
- **Evidence**: Line 245: `$"{starValue:F6}*"` — negative doubles produce `"-0.500000*"`.
- **Fix**: Add validation loop: `if (proportions[i] < 0 || double.IsNaN(...)) throw new ArgumentOutOfRangeException(...)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F063
- **File**: Reactor/Elements/ElementExtensions.cs:269-270, 285-286, 309-310
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: CS-PERF-ALLOC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: String color overloads of `Background`, `Foreground`, `WithBorder` create `new SolidColorBrush` on every render — a DependencyObject allocation per re-render.
- **Evidence**: `BrushHelper.Parse` always creates `new SolidColorBrush(parsed)`. Called from element construction during every render.
- **Fix**: Defer brush creation to reconciler (store color string in modifiers), or document that `Brush` overload should be used on hot paths.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F064
- **File**: Reactor/Animation/Transition.cs:70
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `GetEnterTransition()` falls back to `Transition` (wrapper) when DirectionalTransition has null enter, while `GetExitTransition()` correctly returns null. Causes unnecessary `MarkCompositorTainted` and empty batch creation.
- **Evidence**: Line 70: `d.EnterTransition ?? Transition`. Lines 78-79: exit correctly returns null.
- **Fix**: Remove `?? Transition` fallback on line 70 to match exit-side pattern.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F065
- **File**: Reactor/Animation/KeyframeBuilder.cs:48-62
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `At(float progress, ...)` accepts unconstrained float but WinUI's `InsertKeyFrame` requires [0.0, 1.0]. Invalid values cause runtime error at animation creation.
- **Evidence**: Lines 48-62: no range check. Compositor throws `ArgumentException` later.
- **Fix**: Add validation: `if (progress < 0f || progress > 1f) throw new ArgumentOutOfRangeException(...)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F066
- **File**: Reactor/Flex/FlexPanel.cs:425-443
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `SyncYogaTree()` allocates `new HashSet<UIElement>()` and `new List<UIElement>()` on every call — during `MeasureOverride`, a WinUI layout hot path.
- **Evidence**: Lines 428-432: per-call allocations during layout.
- **Fix**: Replace with instance-level scratch collections cleared at start of each call.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F067
- **File**: Reactor/Yoga/YogaNode.cs:405-419
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ProcessDimensions()` allocates `new[] { YogaDimension.Width, YogaDimension.Height }` on every invocation — once per node per layout pass.
- **Evidence**: Line 407: `foreach (var dim in new[] { ... })`. For 100-node tree at 30fps, ~3000 throwaway arrays/second.
- **Fix**: Replace with `for (int d = 0; d < 2; d++) { var dim = (YogaDimension)d; ... }`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F068
- **File**: Reactor/Yoga/AlgorithmUtils.cs:382-395
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `CalculateFlexLine()` allocates two `new List<YogaNode>()` per call. Combined with callers, 3-4 list allocations per flex container per layout pass.
- **Evidence**: Lines 382, 395: per-call allocations. `GetLayoutChildren()` materialized twice in same pass.
- **Fix**: Materialize once per node and pass through. Use list pooling for `itemsInFlow`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F069
- **File**: Reactor/Yoga/YogaConfig.cs:20-22
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `YogaConfig.Default` is a mutable singleton. Any code can mutate it, silently affecting all YogaNodes using default config.
- **Evidence**: Lines 20-22: `static readonly` instance with public setters. All parameterless YogaNodes use it.
- **Fix**: Add `Freeze()` method or `Debug.Assert` guards in setters when instance is default.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F070
- **File**: Reactor/Markdown/MarkdownBuilder.cs:122-134
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Build()` calls `Md4cParser.Parse()` and discards the return value. Parser errors silently produce partial/empty tree.
- **Evidence**: Lines 126-134: `int` return discarded. `?? VStack()` fallback returns empty container, masking error.
- **Fix**: Capture and check: `int ret = Md4cParser.Parse(...); if (ret != 0) Debug.WriteLine(...);`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F071
- **File**: Reactor/Markdown/Md4cHtml.cs:162-185
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-06
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `AppendUtf8Codepoint` allocates a new `char[]` array on every call for entity resolution.
- **Evidence**: Lines 167-177: `chars = new[] { (char)codepoint }` per entity.
- **Fix**: Replace with `Span<char>` over `stackalloc char[2]`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F072
- **File**: Reactor/Markdown/Md4cHtml.cs:135, 147
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `UrlEscaped` uses string interpolation `$"%{utf8Buf[b]:X2}"` per byte in a loop — allocates temporary string per non-ASCII byte.
- **Evidence**: Line 135: up to 4 string allocations per non-ASCII character.
- **Fix**: Write hex digits directly: `output.Append('%'); output.Append(HexDigit(utf8Buf[b] >> 4)); output.Append(HexDigit(utf8Buf[b] & 0xF));`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F073
- **File**: Reactor/PropertyGrid/ReflectionTypeMetadataProvider.cs:145
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-NULL-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `property.GetValue(owner)!` uses null-forgiving operator, but `PropertyInfo.GetValue` returns null for nullable properties. `(int)value` NREs for `int?` properties.
- **Evidence**: Line 145: `GetValue = () => property.GetValue(owner)!`. TypeRegistry.cs:94: `NumberBox((int)value, ...)`.
- **Fix**: Change `PropertyDescriptor.GetValue` return type to `Func<object?>` and handle nulls in editors.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F074
- **File**: Reactor/PropertyGrid/ReflectionTypeMetadataProvider.cs:91-101
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `CreateDescriptorBound` re-reads all 9 custom attributes via `GetCustomAttribute` for every property on every `Decompose` call (render path), despite attributes being immutable.
- **Evidence**: Lines 119-128: 9 `GetCustomAttribute` calls per property per render. `BuildMetadata` already has access to properties.
- **Fix**: Pre-compute attribute metadata once during `BuildMetadata` and pass to `CreateDescriptorBound`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F075
- **File**: Reactor/PropertyGrid/TypeRegistry.cs:65-67, 162-164
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: EH-NULL-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `TryResolvePrimitive` and `TryResolveArray` use `metadata = null!` for out parameter. Accessing after `false` return gives NRE with no compiler warning.
- **Evidence**: Lines 67, 164: `metadata = null!;`. No `[NotNullWhen(true)]` annotation.
- **Fix**: Change to `[NotNullWhen(true)] out TypeMetadata? metadata` with `metadata = null;`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F076
- **File**: Reactor.Cli/Loc/ExtractCommand.cs:21-24 (and 4 other commands)
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: All CLI argument parsers silently ignore flags without values. `duct loc extract --source` (no path) silently uses default. 11 occurrences across 5 commands.
- **Evidence**: `if (i + 1 < args.Length) sourcePath = args[++i]; break;` — silent no-op when value missing.
- **Fix**: Emit error: `if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --source requires a value."); return 1; }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F077
- **File**: Reactor.Cli/Loc/AzureOpenAiProvider.cs:66-82
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `TranslateAsync` awaits `tcs.Task` with no timeout or cancellation. If SDK session terminates abnormally, CLI hangs forever.
- **Evidence**: Line 80-82: `content = await tcs.Task;` with no timeout. Caller passes no CancellationToken.
- **Fix**: Add timeout: `var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2)));`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F078
- **File**: Reactor.Cli/Loc/InterpolationConverter.cs:221-228
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-07
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `EscapeForIcu` escapes quotes but not `{`/`}`. Literal braces in C# interpolated strings produce malformed ICU messages.
- **Evidence**: Lines 221-228: only `text.Replace("'", "''")`. Doubled braces in source produce literal braces that ICU treats as placeholders.
- **Fix**: Add: `.Replace("{", "'{'").Replace("}", "'}'")`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F079
- **File**: Reactor.Cli/Loc/ReswWriter.cs:54-55
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `existingEntries` parameter accepted but never referenced. Creates misleading API contract — callers expect dedup but get none.
- **Evidence**: Lines 54-55: parameter declared. Method body (lines 57-127) never reads it.
- **Fix**: Remove parameter, or add dedup check inside `Write`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F080
- **File**: Reactor.Cli/Loc/ReswReader.cs:116-119
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ParseReswFile` catches all exceptions with bare `catch { }` and returns empty list. Callers can't distinguish "zero entries" from "file unreadable."
- **Evidence**: Lines 96-121: `catch { // Skip malformed files }`. Swallows everything including `OutOfMemoryException`.
- **Fix**: `catch (XmlException) { Console.Error.WriteLine($"[WARN] Skipping malformed .resw: {filePath}"); }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F081
- **File**: Reactor.Cli/Loc/ReswWriter.cs:40-44
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `LoadExisting` bare `catch { }` — if file is transiently locked, returns empty dict. Caller then re-adds all entries, creating duplicates.
- **Evidence**: Lines 25-44: same bare catch pattern. Transient lock → duplicate `<data>` elements.
- **Fix**: Catch `XmlException` specifically. Let I/O exceptions propagate.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F082
- **File**: Reactor.Cli/Loc/PruneCommand.cs:196-199
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `RemoveKeys` bare `catch { }` silently swallows write failures. Produces inconsistent state across locales.
- **Evidence**: Lines 178-199: `catch { // Skip malformed files }`. Failed saves lower `filesModified` count with no indication which file failed.
- **Fix**: `catch (Exception ex) { Console.Error.WriteLine($"  Warning: Could not update '{reswFile}': {ex.Message}"); }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F083
- **File**: Reactor.Cli/Loc/PruneCommand.cs:137-141
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-02
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ScanSourceReferences` bare `catch { continue; }` silently skips unreadable .cs files. Excluded references appear "unused" and get pruned — data loss.
- **Evidence**: Lines 132-141: `catch { continue; }` with no file name reported.
- **Fix**: `catch (IOException ex) { Console.Error.WriteLine($"  Warning: Could not read '{file}': {ex.Message}"); continue; }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F084
- **File**: Reactor.Cli/Loc/ValidateCommand.cs:55-68
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: When `--default-locale` doesn't match any locale, validation produces "passed" with zero meaningful checks. Vacuously correct in CI.
- **Evidence**: Line 55: `sourceFiles` empty. Steps 2-3 iterate empty keys → zero findings → "Validation passed."
- **Fix**: Check and warn: `if (sourceFiles.Count == 0) { Console.Error.WriteLine($"WARN: No .resw files for '{defaultLocale}'."); warnings++; }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F085
- **File**: Reactor.Cli/Docs/ScreenshotCapture.cs:28-35
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `RedirectStandardError = true` but stderr never read. If child writes >4KB to stderr, it blocks, making capture server unresponsive.
- **Evidence**: Line 33: stderr redirected. Lines 115-140: only stdout drained.
- **Fix**: Either drain stderr in background or `RedirectStandardError = false`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F086
- **File**: Reactor.Cli/Docs/ScreenshotCapture.cs:93-96
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Per-screenshot `catch (Exception ex)` catches all types and logs only `ex.Message` — losing type, stack trace, inner exceptions.
- **Evidence**: Lines 93-96: `Console.Error.WriteLine($" ✗ {ex.Message}");`.
- **Fix**: Log `ex` instead of `ex.Message`. Narrow catch to expected types.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F087
- **File**: Reactor.Cli/Docs/ScreenshotCapture.cs:87-89
- **Severity**: medium
- **Priority**: P2
- **Domain**: security
- **Pattern**: SEC-INJ-05
- **Agent**: security
- **Status**: :black_square_button: PENDING
- **Finding**: Screenshot output path built from YAML manifest `screenshot.Id` and `screenshot.Format` without path traversal validation. `id: "../../evil"` escapes output directory.
- **Evidence**: Line 87: `Path.Combine(topicDir, $"{screenshot.Id}.{screenshot.Format}")`.
- **Fix**: Canonicalize and validate containment: `if (!candidatePath.StartsWith(Path.GetFullPath(topicDir), ...)) throw ...`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F088
- **File**: Reactor.LocGenerator/LocSourceGenerator.cs:157-161
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `entry.Value` escaped for XML entities but not newlines. Multi-line values break `/// <summary>` doc comments — continuation lines missing `///` prefix.
- **Evidence**: Lines 157-161: only `&`, `<`, `>` replaced. Newline in value splits comment across lines.
- **Fix**: Add `.Replace("\r\n", " ").Replace("\n", " ")` before XML escaping.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F089
- **File**: ReactorCharting/Array/Bin.cs:92-95
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `D3Ticks.TickStep(min, max, count)` called redundantly N+2 times in `ThresholdSturges`. Already computed and stored in `step` at line 76.
- **Evidence**: Lines 92, 95: `Math.Abs(D3Ticks.TickStep(...))` — redundant with `step`. TickStep involves `Log10`, `Pow`, `Round`.
- **Fix**: Replace with the already-computed `step` variable.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F090
- **File**: ReactorCharting/Scale/QuantizeScale.cs:88-94
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `InvertExtent()` accesses `_domain[0]` and `_domain[^1]` without checking if `_domain` is empty. Default-constructed scale crashes.
- **Evidence**: Line 92: `_domain[0]` on empty array → `IndexOutOfRangeException`.
- **Fix**: Guard: `if (_domain.Length == 0) return (double.NaN, double.NaN);`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F091
- **File**: ReactorCharting/Scale/BandScale.cs:164
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `PointScale<T>.Padding` is write-only (no getter). Anti-pattern per Framework Design Guidelines. Causes F021.
- **Evidence**: Line 164: `public double Padding { set => _band.PaddingOuter = value; }`. Companion `Align` has both getter and setter.
- **Fix**: Add getter: `get => _band.PaddingOuter;`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F092
- **File**: ReactorCharting/Shape/Arc.cs:91-101
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `SetCornerRadius()` has no effect on output. Both branches (rc > Epsilon and else) produce identical paths. Dead code.
- **Evidence**: Lines 91-101: identical `MoveTo + Arc` in both branches. Lines 106-107: unused variables.
- **Fix**: Either implement full corner radius per d3's `arc.js`, or remove and throw `NotImplementedException`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F093
- **File**: ReactorCharting/Layout/Sankey.cs:103
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ComputeNodeHeights` calls `graph.Nodes.Max(n => n.Depth)` without guarding empty collection. Line 130 has the guard — inconsistency.
- **Evidence**: Line 103: no guard. Line 130: `graph.Nodes.Count > 0 ? ... : 0;` — guarded.
- **Fix**: Apply same guard pattern: `if (graph.Nodes.Count == 0) return;`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F094
- **File**: ReactorCharting/Format/Format.cs:146-161
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-EXC-06
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `FormatRounded` and `FormatSI` don't guard against `value == 0`. `Math.Log10(0) = -Infinity` cascades. `FormatSI(0, 6)` returns `"0y"` (zero yocto).
- **Evidence**: Line 149: `Math.Log10(Math.Abs(value))`. Line 156: same call in FormatSI.
- **Fix**: Add early return: `if (value == 0) return "0";`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F095
- **File**: ReactorCharting/Contour/Contour.cs:115-158
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-LINQ-04
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `StitchSegments` uses nested O(n²) loop for matching endpoints. For moderately sized contour grids, stitching becomes dominant cost.
- **Evidence**: Lines 130-152: inner `for j = 0..n` inside `while(extended)` loop.
- **Fix**: Build spatial lookup dictionary keyed by quantized endpoint coordinates for O(1) matching.
- **Manager Decision**: DECLINED
- **Implementation**: :black_square_button: Not started

---

## F096
- **File**: ReactorCharting/Color/D3Color.cs:149-160
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Category10` and `Tableau10` are `public static readonly D3Color[]` — mutable arrays. Any caller can overwrite elements, corrupting shared palette.
- **Evidence**: Lines 149, 156: `public static readonly D3Color[]`. D3Charts.cs:23 shares the same reference.
- **Fix**: Change to `ReadOnlySpan<D3Color>` property or `IReadOnlyList<D3Color>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F097
- **File**: ReactorCharting/Random/Random.cs:62-63, 105-118, 145-148
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Exponential`, `Poisson`, `Weibull` missing parameter validation. `lambda=0` produces Infinity. `Geometric` and `Pareto` correctly validate.
- **Evidence**: Lines 62-63, 105-118, 145-148: no validation. Lines 96, 127: have validation — inconsistency.
- **Fix**: Add: `if (lambda <= 0) throw new ArgumentOutOfRangeException(nameof(lambda));` for each.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F098
- **File**: ReactorCharting/Voronoi/Delaunay.cs:320-330
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ClipToBounds` clamps each vertex independently instead of computing edge-boundary intersections. Produces geometrically incorrect cell boundaries.
- **Evidence**: Lines 323-328: per-vertex clamping shifts vertices along wrong axis, can produce self-intersecting polygons.
- **Fix**: Replace with Sutherland-Hodgman polygon clipping against bounding box edges.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F099
- **File**: vscode-reactor/src/extension.ts:672-691
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `httpGetJson` doesn't check HTTP response status code. Non-2xx responses produce confusing `SyntaxError` from JSON.parse on error body.
- **Evidence**: Lines 674-684: no `res.statusCode` check. Webview's fetch correctly checks `resp.ok`.
- **Fix**: `if (res.statusCode && (res.statusCode < 200 || res.statusCode >= 300)) { res.resume(); reject(new Error(`HTTP ${res.statusCode}`)); }`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F100
- **File**: vscode-reactor/src/extension.ts:190-195
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `isLaunching` flag reset to `false` immediately after `spawn` — before process started or `CAPTURE_PORT=` received. Double-trigger during startup window kills first process.
- **Evidence**: Line 195: `isLaunching = false` synchronously. Port received async later.
- **Fix**: Move `isLaunching = false` into `CAPTURE_PORT=` handler (success) and `exit` handler (failure).
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F101
- **File**: Reactor/Hosting/XamlInterop.cs:21-32
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `TypeKey` property on `XamlHostElement` never checked by `CanUpdate`. Doc claims it controls update behavior but it doesn't.
- **Evidence**: Line 31: doc says TypeKey helps CanUpdate. Reconciler.cs:704-712: CanUpdate never reads TypeKey.
- **Fix**: Remove `TypeKey` and misleading docs, or add it to CanUpdate logic.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F102
- **File**: Reactor/PropertyGrid/PropertyGridComponent.cs:86
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: CS-PERF-LINQ-07
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Two locations perform double dictionary lookup: `ContainsKey` + indexer instead of `TryGetValue`.
- **Evidence**: Line 86: `!expandState.ContainsKey(catKey) || expandState[catKey]`. Line 163: same pattern.
- **Fix**: Replace with `expandState.TryGetValue(catKey, out var catVal)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F103
- **File**: Reactor/PropertyGrid/PropertyGridDefaults.cs:38
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: CONC-ASYNC-05
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `_ = onAdd()` discards Task from async factory. If `onAdd` throws (e.g., `MissingMethodException` from `Activator.CreateInstance`), exception is unobserved.
- **Evidence**: Line 38: `Button("+", () => _ = onAdd())`.
- **Fix**: Wrap: `Button("+", async () => { try { await onAdd(); } catch (Exception ex) { /* log */ } })`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F104
- **File**: Reactor/PropertyGrid/ArrayOperations.cs:47
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-07
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `RemoveAt` for arrays creates `Array.CreateInstance(elementType, array.Length - 1)` without bounds validation. Empty array → `Length - 1 = -1` → `OverflowException`.
- **Evidence**: Line 47: no check for empty or out-of-bounds index.
- **Fix**: Add bounds validation at top: `if (index < 0) throw new ArgumentOutOfRangeException(...);`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F105
- **File**: Reactor/Markdown/Md4cEntity.cs:15-25
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-06
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Entity` struct allocates `new uint[]` per instance (2,126 at static init). Lookup key also allocates throwaway array. `EntityLookup` caller allocates string from span.
- **Evidence**: Line 20: `Codepoints = new uint[] { cp0, cp1 };`. Line 2172: lookup key allocates too.
- **Fix**: Replace `uint[]` with two fixed fields: `Codepoint0`, `Codepoint1`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F106
- **File**: Reactor/Markdown/Md4cTypes.cs:10-30
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: API-TYPE-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `MdAttribute` is `public struct` with mutable public fields. `Text` declared non-nullable but can be null. Violates value-type semantics.
- **Evidence**: Lines 10-14: mutable fields. Line 19: `Simple()` returns `default` with null `Text`.
- **Fix**: Make `readonly struct` with `readonly` fields. Annotate `Text` as `string?`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F107
- **File**: ReactorCharting/Shape/Pie.cs:86-94
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: API-TYPE-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `PieArc<T>` is `record struct` with mutable public fields instead of `init`-only properties.
- **Evidence**: Lines 86-94: six `public` mutable fields.
- **Fix**: Change to `readonly record struct` with positional parameters.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F108
- **File**: ReactorCharting/Chord/Chord.cs:137-163
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: API-TYPE-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ChordData`, `ChordGroup`, `ChordArc`, `ChordEnd` are `record struct` with mutable public fields.
- **Evidence**: Lines 138-163: all use mutable public fields.
- **Fix**: Change all fields to `init`-only properties.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F109
- **File**: ReactorCharting/Charts/Charts.cs:75, 179
- **Severity**: medium
- **Priority**: P2
- **Domain**: error-handling
- **Pattern**: EH-VAL-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ChartHandle.Redraw<T>` has disconnected generic type parameter. Unchecked cast `(IReadOnlyList<T>)data` throws `InvalidCastException` at runtime if type mismatches.
- **Evidence**: Line 75: `Action<object>` erases type. Line 179: fresh `T` unrelated to chart's `T`.
- **Fix**: Make handle classes generic: `ChartHandle<T>` with `Action<IReadOnlyList<T>>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F110
- **File**: ReactorCharting/Format/Format.cs:28-31
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-05
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `FormatValue(double, string)` creates `FormatSpec` + `Func` delegate then immediately invokes — two allocations used once.
- **Evidence**: Lines 28-31: `Format(specifier)(value)` creates and discards.
- **Fix**: Call internal method directly: `var spec = ParseSpecifier(specifier); return FormatValue(value, spec);`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F111
- **File**: ReactorCharting/Layout/Sankey.cs:310-312
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `SankeyLink.Width` is computed from mutable external state (Source.Y1, Y0, Value). Returns `1` before layout with no error signal. Different values at different points during computation.
- **Evidence**: Lines 310-312: `Source != null ? Math.Max(1, ...) : 1`. Before layout, returns `1` silently.
- **Fix**: Cache width as `internal set` property computed during `ComputeLinkBreadths`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F112
- **File**: Reactor.csproj:11 vs Directory.Build.props:10
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Reactor.csproj` overrides `WindowsAppSDKSelfContained` to `false`, contradicting `Directory.Build.props` `true`. Undocumented inconsistency.
- **Evidence**: Reactor.csproj:11 `false`. Directory.Build.props:10 `true`. ReactorCharting inherits as intended.
- **Fix**: If intentional, add comment. If stale, remove override.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F113
- **File**: Reactor.csproj:20 vs Reactor.Cli.csproj:22
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `System.Drawing.Common` version 9.0.0 in Reactor vs 9.0.4 in Reactor.Cli. Version drift across shared dependency.
- **Evidence**: Reactor.csproj:20: `9.0.0`. Reactor.Cli.csproj:22: `9.0.4`.
- **Fix**: Align to latest patch version or adopt NuGet Central Package Management.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F114
- **File**: tests/Reactor.Tests/Reactor.Tests.csproj vs tests/ReactorCharting.Tests/ReactorCharting.Tests.csproj
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: xUnit test projects use different versions including major version gap on runner (2.5.7 vs 3.0.2). Different runner versions produce inconsistent behavior.
- **Evidence**: Reactor.Tests: xunit 2.7.0, runner 2.5.7. ReactorCharting.Tests: xunit 2.9.3, runner 3.0.2.
- **Fix**: Standardize all test projects to same versions. Consider Central Package Management.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F115
- **File**: tests/stress_perf/StressPerf.Shared/StressPerf.Shared.csproj:3
- **Severity**: medium
- **Priority**: P2
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `StressPerf.Shared` targets `net8.0-windows` while all consumers target `net9.0-windows10.0.22621.0`. Only project in solution on .NET 8.
- **Evidence**: Line 3: `net8.0-windows`. All other projects: `net9.0-*`.
- **Fix**: Change to `net9.0-windows`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F116
- **File**: Reactor/Elements/ElementExtensions.cs:139-140, 236-237
- **Severity**: low
- **Priority**: P3
- **Domain**: memory-lifecycle
- **Pattern**: ML-GC-09
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `FontFamily(string)` overloads create `new FontFamily(family)` bypassing `WinRTCache.GetFontFamily()` — the cache built to avoid this exact allocation.
- **Evidence**: Lines 139-140: `new FontFamily(family)`. WinRTCache.cs:9-10 documents why cache exists.
- **Fix**: Replace with `WinRTCache.GetFontFamily(family)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F117
- **File**: Reactor/Core/ElementFactory.cs:64-76
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `PoolInteractiveLeaves` only recurses into `Panel` children. `Border` (not a Panel) wrapping a Button is missed.
- **Evidence**: Line 66: `if (root is Panel panel)`. Border inherits from FrameworkElement, not Panel.
- **Fix**: Add: `else if (root is Border border && border.Child is not null) PoolInteractiveLeaves(border.Child);`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F118
- **File**: Reactor/Core/PersistedStateCache.cs:9
- **Severity**: low
- **Priority**: P3
- **Domain**: memory-lifecycle
- **Pattern**: ML-GC-03
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: Static dictionary with no size limit or eviction. Dynamic keys grow unbounded.
- **Evidence**: Line 9: `static readonly Dictionary`. `Set` adds entries, never removed unless explicit `Remove`/`Clear`.
- **Fix**: Add configurable size limit with LRU eviction, or document stable-key requirement.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F119
- **File**: Reactor/Yoga/YogaNode.cs:153-167
- **Severity**: low
- **Priority**: P3
- **Domain**: performance
- **Pattern**: PERF-ALLOC-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `GetLayoutChildren()` uses `yield return` allocating enumerator state machine, but all callers immediately materialize into `new List<>()`.
- **Evidence**: Lines 153-167: iterator. All 3 call sites: `new List<YogaNode>(node.GetLayoutChildren())`.
- **Fix**: Change to `void CollectLayoutChildren(List<YogaNode> result)` that populates directly.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F120
- **File**: Reactor.Cli/Loc/SourceRewriter.cs:63-65
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Ternary with identical branches — dead code. Both produce `$"Loc.{entry.ReswFileName}.{entry.Key}"`.
- **Evidence**: Lines 63-65: `entry.Namespace != null ? $"Loc.{entry.ReswFileName}.{entry.Key}" : $"Loc.{entry.ReswFileName}.{entry.Key}"`.
- **Fix**: Remove ternary: `var locPath = $"Loc.{entry.ReswFileName}.{entry.Key}";`
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F121
- **File**: Reactor.Cli/Loc/StatusCommand.cs:48-56
- **Severity**: low
- **Priority**: P3
- **Domain**: error-handling
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Same vacuous-output pattern as ValidateCommand (F084). Missing source locale shows 100% coverage for 0 keys.
- **Evidence**: Line 58: `totalKeys = 0`. Line 118-119: coverage falls through to 100.0% via ternary.
- **Fix**: Warn if `sourceFiles` is empty.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F122
- **File**: Reactor.Cli/Loc/ValidateCommand.cs:58
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `allSourceNamespaces` populated but never read — dead code.
- **Evidence**: Line 58: declared. Line 62: populated. No subsequent use.
- **Fix**: Remove lines 58 and 62.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F123
- **File**: Reactor.Cli/Docs/SnippetExtractor.cs:18
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `relativePath.StartsWith("obj")` / `StartsWith("bin")` without directory separator. Incorrectly skips `objects/`, `binary/`, `bindings/`.
- **Evidence**: Line 18: `StartsWith("bin")` matches `bindings\Helper.cs`.
- **Fix**: `StartsWith("obj" + Path.DirectorySeparatorChar)`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F124
- **File**: Reactor.LocGenerator/ReswParser.cs:27-39
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `ReswFile` class and `DeriveNamespace` method are dead code — never used by production code.
- **Evidence**: Zero production call sites for either. `DeriveNamespace` only in tests.
- **Fix**: Delete `ReswFile` (lines 27-39) and `DeriveNamespace` (lines 80-83).
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F125
- **File**: Reactor/Navigation/DeepLinkMap.cs:27-34
- **Severity**: low
- **Priority**: P3
- **Domain**: error-handling
- **Pattern**: EH-VAL-03
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `RouteArgs.Get<T>` uses `int.Parse(raw)` without `CultureInfo.InvariantCulture`. Parse failures produce raw `FormatException` with no parameter context.
- **Evidence**: Line 30: `int.Parse(raw)` — no culture, no wrapping exception.
- **Fix**: Use `InvariantCulture` and wrap: `throw new FormatException($"Route parameter '{name}' value '{raw}' is not a valid int.")`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F126
- **File**: Reactor/Hosting/RenderStats.cs:8-44
- **Severity**: low
- **Priority**: P3
- **Domain**: api-design
- **Pattern**: CS-API-TYPE-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `RenderStats` is mutable struct with 7+ fields exposed via `ref readonly`. Multi-field struct assignment not atomic — torn reads possible on ARM64.
- **Evidence**: Lines 8-44: 56+ byte struct. Written field-by-field in `Render()`.
- **Fix**: Add comment documenting single-thread-read contract. Mark `readonly`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F127
- **File**: Reactor/Hosting/PreviewCaptureServer.cs:288-290
- **Severity**: low
- **Priority**: P3
- **Domain**: security
- **Pattern**: SEC-INJ-03
- **Agent**: security
- **Status**: :black_square_button: PENDING
- **Finding**: `componentName` from POST body embedded raw in JSON response string. Special characters produce malformed JSON.
- **Evidence**: Lines 288-290: `$"{{\"ok\":true,\"component\":\"{componentName}\"}}"`.
- **Fix**: Use `JsonSerializer.Serialize(new { ok = true, component = componentName })`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F128
- **File**: Reactor/Hosting/PreviewCaptureServer.cs:321-339
- **Severity**: low
- **Priority**: P3
- **Domain**: memory-lifecycle
- **Pattern**: ML-UNSAFE-03
- **Agent**: interop
- **Status**: :black_square_button: PENDING
- **Finding**: Five P/Invoke declarations omit `SetLastError = true`. `GetClientRect`/`GetWindowRect` MSDN docs say to call `GetLastError`.
- **Evidence**: Lines 321-339: no `SetLastError`. ReactorApp.cs:39 correctly specifies it.
- **Fix**: Add `SetLastError = true` to all five declarations.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F129
- **File**: samples/apps/webview2-test/WebView2Test.csproj:16
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Hardcodes `Version="2.0.0-experimental6"` instead of `$(WindowsAppSDKVersion)`. Sole exception in 70+ projects.
- **Evidence**: Line 16: hardcoded. Every other project uses centralized property.
- **Fix**: Change to `Version="$(WindowsAppSDKVersion)"`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F130
- **File**: Reactor.Cli/Reactor.Cli.csproj:20
- **Severity**: low
- **Priority**: P3
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `GitHub.Copilot.SDK` uses floating version `0.1.*`. Non-reproducible builds for pre-1.0 package.
- **Evidence**: Line 20: `Version="0.1.*"`.
- **Fix**: Pin to specific version or use NuGet lock file.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F131
- **File**: tests/ReactorCharting.Tests/ReactorCharting.Tests.csproj
- **Severity**: low
- **Priority**: P3
- **Domain**: general
- **Pattern**: general-quality
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Missing `coverlet.collector` package — ReactorCharting library (6000+ lines) is a code coverage blind spot. `Reactor.Tests` includes it.
- **Evidence**: Reactor.Tests.csproj:23-26: has coverlet. ReactorCharting.Tests: missing.
- **Fix**: Add `coverlet.collector` 6.0.0 package reference.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F132
- **File**: tests/stress_perf/StressPerf.Shared/StressPerf.Shared.csproj:9
- **Severity**: low
- **Priority**: P3
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `AllowUnsafeBlocks=true` enabled but no file contains `unsafe`, `fixed`, `stackalloc`, or pointers. `LibraryImport` doesn't require it.
- **Evidence**: Grep for `unsafe` across all 4 `.cs` files: zero matches.
- **Fix**: Remove `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F133
- **File**: Reactor/Elements/Dsl.cs:725-738
- **Severity**: medium
- **Priority**: P2
- **Domain**: api-design
- **Pattern**: CS-API-012
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `UI.AcrylicBrush(...)` creates new WinRT DependencyObject on every call. Heavier than SolidColorBrush. No caching, no guidance to use `UseMemo`.
- **Evidence**: Lines 731-738: `new AcrylicBrush { ... }` per call. Crosses managed→WinRT boundary.
- **Fix**: Add XML doc warning to cache via `UseMemo`, or provide `WinRTCache.GetAcrylicBrush(...)` pattern.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F134
- **File**: Reactor/Core/ContextExtensions.cs:14-18
- **Severity**: low
- **Priority**: P3
- **Domain**: performance
- **Pattern**: PERF-ALLOC-05
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Provide<T, TValue>` allocates new Dictionary on every call, copying existing entries. Called during every render for context providers.
- **Evidence**: Lines 15-17: `new Dictionary<...>(existing) { [context] = value }` per call.
- **Fix**: Acceptable for 2-4 providers. If profiling shows hot spot, consider immutable dictionary with `.SetItem()`.
- **Manager Decision**: DECLINED
- **Implementation**: :black_square_button: Not started

---

## F135
- **File**: Reactor/Markdown/Md4cEntity.cs:2172
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: PERF-ALLOC-06
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: Per-lookup key construction allocates throwaway `Entity` struct with `uint[]` array, plus `text.ToString()` from span to string.
- **Evidence**: Line 2172: `new Entity(name, 0, 0)` allocates array just for comparison. EntityLookup caller allocates string.
- **Fix**: Accept `ReadOnlySpan<char>` and use `Dictionary<string, Entity>` for O(1) lookup eliminating key construction.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F136
- **File**: Reactor/Core/Reconciler.cs:2335-2352 (ElementPool)
- **Severity**: medium
- **Priority**: P2
- **Domain**: memory-lifecycle
- **Pattern**: ML-DISP-02
- **Agent**: lifecycle
- **Status**: :black_square_button: PENDING
- **Finding**: `Dispose()` does not clean up ElementPool. Pool holds recycled FrameworkElement instances indefinitely.
- **Evidence**: ElementPool has no `Clear()` or `Dispose()`. `_scratchPanel` field also holds StackPanel.
- **Fix**: Add `ElementPool.Clear()` method emptying all per-type stacks. Call from `Reconciler.Dispose()`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F137
- **File**: Reactor/Hosting/ReactorHost.cs (also ReactorHostControl.cs)
- **Severity**: medium
- **Priority**: P2
- **Domain**: concurrency
- **Pattern**: CONC-RACE-06
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: ReactorHostControl.cs:55-56 has the same non-volatile `_isRendering`/`_needsRerender` issue as ReactorHost (F043).
- **Evidence**: Same pattern duplicated in both classes.
- **Fix**: Apply same volatile fix to ReactorHostControl. Also flagged by: safety (safety-batch-3)
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F138
- **File**: Reactor/Hosting/ReactorHostControl.cs:172-180
- **Severity**: medium
- **Priority**: P2
- **Domain**: concurrency
- **Pattern**: CONC-RACE-04
- **Agent**: safety
- **Status**: :black_square_button: PENDING
- **Finding**: ReactorHostControl has the same TOCTOU RequestRender race as ReactorHost (F042).
- **Evidence**: Identical code at lines 172-180.
- **Fix**: Apply same fix as F042. Also flagged by: safety (safety-batch-3)
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

---

## F139
- **File**: Reactor/Core/Localization/IntlAccessor.cs:117-143
- **Severity**: medium
- **Priority**: P2
- **Domain**: performance
- **Pattern**: CS-PERF-UI-01
- **Agent**: general
- **Status**: :black_square_button: PENDING
- **Finding**: `Asset()` performs synchronous `File.Exists()` I/O on potential UI thread, up to twice per call during component render.
- **Evidence**: Lines 127, 137: `System.IO.File.Exists()` called from render path via `UseIntl()` hook.
- **Fix**: Cache asset resolution results in instance-level `Dictionary<string, string>`.
- **Manager Decision**: APPROVED
- **Implementation**: :white_check_mark: Complete (2026-04-11)

