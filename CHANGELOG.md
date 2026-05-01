# Changelog

All notable changes to Reactor will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once a `1.0.0` release is cut. While the project is pre-1.0 and labeled experimental,
the public API surface may change between releases without notice.

<!--
Conventions for contributors:

  * Use the standard Keep-a-Changelog buckets: Added / Changed / Deprecated /
    Removed / Fixed / Security. Group entries under those buckets, not under
    per-spec or per-phase headings.
  * Cross-reference the originating spec on every line, e.g. "(spec 033 §1)",
    so readers can navigate from changelog → design rationale.
  * Within a bucket, prefer ordering by spec/section number for predictable
    reading.
  * Cutting a release: rename `## [Unreleased]` to `## [x.y.z] — YYYY-MM-DD`
    and add a fresh empty `## [Unreleased]` block (with all six bucket
    sub-headings) above it.

Spec 033 (WinUI/XAML reviewer feedback response) is the first set of entries
to land under these conventions; subsequent specs follow this shape.
-->

## [Unreleased]

### Added

- `Expr(Func<Element?>)` factory in `Microsoft.UI.Reactor.Factories` for inline
  block-expression bodies inside a DSL tree, removing the
  `((Func<Element?>)(() => …))()` cast ceremony. Pure composition — no hooks,
  no memoization, no reconciler boundary. (spec 033 §5)
- `IPersistedStateScope` interface, `PersistedScope` enum (`Window` /
  `Application`), `ApplicationPersistedScope` (process-wide singleton at
  `ApplicationPersistedScope.Default`, capacity 4096), and
  `WindowPersistedScope` (per-host instance, capacity 1024). All backed by an
  internal `LruCache<TKey,TValue>`. New `RenderContext.UsePersisted<T>(key,
  initial, PersistedScope)` overload makes the scope explicit. (spec 033 §2)
- `Microsoft.UI.Reactor.Factories.RenderEachTime(Func<RenderContext, Element>)` —
  explicit factory for "inline component with own hooks that re-renders every
  parent render". Replaces the soft-deprecated `Func(...)` for the rare cases
  that genuinely want always-re-render semantics. (spec 033 §4)
- `Microsoft.UI.Reactor.GridSize` value type with `Auto` / `Star(weight)` /
  `Px(pixels)` smart constructors, implicit conversion to
  `Microsoft.UI.Xaml.GridLength`, and a strict invariant-culture string
  parser (`Parse`). New typed `Grid(GridSize[], GridSize[], …)` factory
  overload. (spec 033 §1)
- `samples/InteropFirst` — XAML-window-hosts-Reactor demonstration with
  shared `ObservableCollection<Order>`, shared `ICommand`s bridged through
  `CommandInterop.FromCommand`, and shared `App.xaml` brush resources flowing
  through props into a Reactor `Component<TProps>`. (spec 033 §7)
- `BackdropKind` enum and `.Backdrop(BackdropKind)` / `.Backdrop(Func<SystemBackdrop?>)`
  modifier on the root tree for declarative Mica / Acrylic on Reactor-hosted
  windows. `ReactorHost` applies the modifier at the end of each reconcile
  pass and resets the window's backdrop on dispose; `ReactorHostControl` that
  does not own its window no-ops with a one-shot debug log. (spec 033 §6)
- `ElementRef<T>` typed-ref wrapper (`Microsoft.UI.Reactor.Input`),
  `UseElementRef<T>()` hook (`Microsoft.UI.Reactor.Hooks`), and a strongly-typed
  `.Ref<T,TElement>(...)` modifier overload. The typed surface removes the
  `(Button)ref.Current` cast at consumers and adds a DEBUG-only assertion when
  a typed ref is bound to an element of the wrong concrete type. AOT-safe and
  reflection-free at the public surface. (spec 033 §3)
- `Component.UsePersisted<T>(key, initial, PersistedScope)` three-arg overload
  so component subclasses can declare the persisted-state scope (Window vs
  Application) explicitly at the call site, matching the
  `RenderContext.UsePersisted` overload added earlier. (spec 033 §2)

### Changed

- `PersistedStateCache` rewritten over an LRU cache with eviction-on-full
  semantics. The previous "refuse new keys when 4096 entries are present"
  policy is replaced — later, hotter keys are no longer starved by the
  first 4096 keys ever recorded. Application-scope registers an
  `Windows.System.MemoryManager.AppMemoryUsageIncreased` handler and trims
  to 25% of capacity when the OS reports `OverLimit` / `High`. Best-effort:
  hosting models that do not expose the event log a notice and carry on.
  Key validation now requires non-empty keys ≤ 256 chars. (spec 033 §2)
- `GridDefinition` gains a strongly-typed constructor accepting `GridSize[]`
  for columns and rows. The legacy string-array constructor is preserved for
  backward compatibility. (spec 033 §1)
- `ApplicationPersistedScope` and `WindowPersistedScope` now emit one-line
  `Debug.WriteLine` diagnostics on construction, disposal, and (for the
  application scope) memory-pressure trim. Logs only counts and capacity —
  never keys or values, since keys may be derived from user-controlled
  identifiers in apps. (spec 033 §7.10)
- `samples/Reactor.TestApp/Demos/PersistedDemo`, `NavigationDemo`, and
  `samples/apps/regedit` migrated to the explicit
  `UsePersisted(key, initial, PersistedScope.Window)` overload to document
  per-window intent at the call site. (spec 033 §2)

### Deprecated

- `Microsoft.UI.Reactor.Factories.Grid(string[], string[], params Element?[])`
  is marked `[Obsolete]`. Use the strongly-typed
  `Grid(GridSize[], GridSize[], params Element?[])` overload with
  `GridSize.Auto` / `GridSize.Star(weight)` / `GridSize.Px(pixels)` instead.
  Slated for removal in the next minor release. (spec 033 §1)
- `Microsoft.UI.Reactor.Factories.Func(Func<RenderContext, Element>)` is
  marked `[Obsolete]`. Replace with `Memo(ctx => …)` (render once + state
  changes) or `RenderEachTime(ctx => …)` (always re-render). Slated for
  removal in the next minor release. (spec 033 §4)

### Removed

### Fixed

### Security
