# Perf trace events — `Microsoft-UI-Reactor-Perf`

**Scope:** the wire contract of Reactor's performance spans: provider identity, keywords,
event ids, opcodes, fields and nesting rules. Written for tools that record and decode them
(`winapp perf`, WPA, TraceEvent, custom ETW consumers). The implementation is
`src/Reactor/Core/Diagnostics/ReactorPerfTrace.cs`; the tests that pin this document are
`tests/Reactor.Tests/Diagnostics/ReactorPerfTraceTests.cs` (schema) and
`tests/Reactor.AppTests.Host/SelfTest/Fixtures/PerfTraceFixtures.cs` (live spans).

## Why a second provider

Reactor's general diagnostics provider, the `Microsoft-UI-Reactor` `EventSource`
(see [Perf Instrumentation](../guide/perf-instrumentation.md)), is not recorded by a
**private** ETW session — the kind a non-elevated tool such as `winapp perf` attaches to a
running app with (`EVENT_TRACE_PRIVATE_LOGGER_MODE` plus a PID scope filter). ETW does call
the EventSource's enable callback, but as of .NET 10 the runtime only turns an EventSource on
for sessions it finds through `EnumerateTraceGuidsEx`, which does not list private loggers, so
the EventSource stays off and writes nothing.

`Microsoft-UI-Reactor-Perf` is registered directly with `EventRegister`, so the enable
callback is all it needs. Its events are TraceLogging: each carries its own metadata, so any
TDH-based decoder reads event names, field names and field types without a manifest. The
EventSource is unchanged and keeps serving its in-process consumers (EventListener,
`dotnet-trace`, DevTools `logs`).

## Provider

| | |
|---|---|
| Name | `Microsoft-UI-Reactor-Perf` |
| GUID | `0902a96c-4e92-5b91-4d2b-5a8058055c78` — the EventSource-style hash of the name, so `*Microsoft-UI-Reactor-Perf` works in `xperf`, `tracelog` and WPR profiles |
| Format | TraceLogging (event metadata in-band, `EVENT_HEADER_EXT_TYPE_EVENT_SCHEMA_TL`); channel 11; provider traits carry the name |
| Registered | once per process, when the first `Reconciler` is created (before the first render) |
| Activity ids | none; spans pair by thread and nesting (see [Nesting](#nesting)) |

## Keywords and levels

| Keyword | Bit | Events | Level |
|---|---|---|---|
| `Pass` | `0x1` | `RenderPass`, `Reconcile`, `Effects` | Informational (4) |
| `Component` | `0x2` | `ComponentRender` | Informational (4) |
| `Children` | `0x4` | `ChildReconcile`, `KeyedListDiagnostic` | Verbose (5) |
| `State` | `0x8` | `StateChange` | Verbose (5) |

Enable `0xF` at level 5 for everything. A session that passes `MatchAnyKeyword = 0` gets every
keyword, and level 0 means every level. `MatchAllKeyword` is honoured either way: an event is
written only when its keyword has every `MatchAllKeyword` bit.

## Events

Spans are a Start (opcode 1) / Stop (opcode 2) pair with the same event name and **different
event ids** (their fields differ). Instant events use opcode 0. Field types are TraceLogging
in-types: `UInt64` (10), `UInt32` (8), `String` = nul-terminated UTF-16 (1), `Bool32` (13,
4 bytes). All events have version 0 and task 0. Fields marked ² were appended in schema
revision 2 (see [Schema revisions](#schema-revisions)); they always follow the revision-1
fields of the same event.

| Id | Name | Opcode | Level | Keyword | Fields |
|---|---|---|---|---|---|
| 1 | `RenderPass` | Start | 4 | `0x1` | `hostId: UInt64`, `requesterId: UInt64`², `requesterName: String`², `requestCount: UInt32`² |
| 2 | `RenderPass` | Stop | 4 | `0x1` | `hostId: UInt64` |
| 3 | `ComponentRender` | Start | 4 | `0x2` | `componentId: UInt64`, `name: String`, `reason: String`, `callSiteFile: String`², `callSiteLine: UInt32`², `callSiteColumn: UInt32`² |
| 4 | `ComponentRender` | Stop | 4 | `0x2` | `componentId: UInt64` |
| 5 | `Reconcile` | Start | 4 | `0x1` | `entry: String`, `element: String` |
| 6 | `Reconcile` | Stop | 4 | `0x1` | `inserted: UInt32`², `removed: UInt32`², `moved: UInt32`², `replaced: UInt32`² |
| 7 | `Effects` | Start | 4 | `0x1` | `componentId: UInt64` |
| 8 | `Effects` | Stop | 4 | `0x1` | `componentId: UInt64` |
| 9 | `ChildReconcile` | Start | 5 | `0x4` | `oldCount: UInt32`, `newCount: UInt32` |
| 10 | `ChildReconcile` | Stop | 5 | `0x4` | `inserted: UInt32`, `removed: UInt32`, `moved: UInt32`, `replaced: UInt32` |
| 11 | `StateChange` | Info | 5 | `0x8` | `componentId: UInt64`, `hook: String`, `changed: Bool32` |
| 12 | `KeyedListDiagnostic` | Info | 5 | `0x4` | `kind: String`, `bailedOut: Bool32` |

### What each span covers

- **`RenderPass`** — one host render pass, from the moment the host's render loop runs to the
  end of the pass: the root component's `Render()`, the reconcile, the commit into the window
  (or `ReactorHostControl`), and the root's effects. `hostId` is a process-unique id per
  `ReactorHost` / `ReactorHostControl` instance. The Start also says what scheduled the pass,
  without the verbose `State` keyword:
  - `requesterId` — the `componentId` of the component whose re-render request scheduled this
    pass: the **first** request since the previous pass started (later ones only coalesced
    into it). It is the component that asked — the one whose hook (`UseState`, `UseReducer`,
    a store or resource subscription, …) requested the render — not the ancestors the request
    passed through, and it is the host's root component when the root's own hooks asked (a
    root replaced by a new `Mount` before the pass ran reports `0`, not its replacement). `0`
    when no component did: the first mount, a host-level re-render (theme, high contrast,
    resources, hot reload, app code calling the host), or a request made before the session
    started listening. That holds when the first request was one of those even if a
    component's request coalesced into the pass afterwards: the pass reports what scheduled
    it, and `requestCount` (which counts only requests made while listening) shows that more
    joined. The same id appears on that component's
    `ComponentRender` spans in the pass (`reason=state`) and on its `StateChange` events.
  - `requesterName` — that component's `name` (as on `ComponentRender`); empty when
    `requesterId` is `0`.
  - `requestCount` — how many render requests coalesced into this pass while the `Pass`
    keyword was on, named or not. `0` means none were recorded (the requests came before the
    session started).
- **`ComponentRender`** — one component render, **inclusive**: `Render()`, the component's
  effects flush, and the reconcile of the subtree it returned (so the WinUI controls its
  subtree creates fall inside it). Emitted on mount, on update, for a host's root component,
  and for rows a virtualized list realizes. For a host root the span ends before the root's
  effects, which get their own `Effects` span.
  - `componentId` — process-unique, never 0, stable for the component instance's lifetime
    (a hot-reload type swap keeps it). The same id is the `componentId` of the
    `Microsoft-UI-Reactor` EventSource's `ComponentRendered` event (40) and of DevTools'
    `ReactorTrace` component lookups, so a trace can be joined with an in-process capture.
  - `name` — a class component's type name (`Counter`, `Row<Int32>`). A function or memo
    component is named by its render function, the way DevTools' component inspector names it:
    `Type.Method` for a method group (`MainPage.Row` for `RenderEachTime(Row)`), and
    `<factory> in Type.Method` for a lambda or local function, with the method it is written
    in (`RenderEachTime in MainPage.Render`, `Memo in MainPage.Render`, and
    `render in App.Main` for a host's render-function root). The name is resolved on the
    component's first traced render and kept for its lifetime, like its id, because reading a
    delegate's method costs ~150 ns and allocates: a component whose render function is
    swapped at the same position (`RenderEachTime(cond ? RowA : RowB)`, which Reactor updates
    in place) keeps its first function's name, where DevTools shows the current one. Under
    Native AOT without the method's metadata it falls back to the bare factory name (`RenderEachTime`,
    `Memo`, `render`). The EventSource keeps reporting `FuncElement` / `MemoElement`.
  - `reason` — why it rendered: `mount`, `state` (a state change in its subtree), `props`,
    `context`, `parent` (no memo gate could skip it), `hotReload`, `forced`.
  - `callSiteFile`, `callSiteLine`, `callSiteColumn` — where the C# that created the component
    is: the `Component<T>()`, `RenderEachTime(…)` or `Memo(…)` call for a child, and the
    `ReactorApp.Run` / `OpenWindow` / `Mount` call for a host root. The line is 1-based; the
    column is the 1-based UTF-16 column of the factory's name (it tells calls on one line
    apart), or `0` when unknown. The file is the **file name only** (`MainPage.cs`): the
    directories are dropped because a compile-time path can carry a user name (spec 044
    §6.2.1); `name` and the line usually disambiguate two files of the same name. These come
    from Reactor's source map, so they are filled only when the app was **built with source
    mapping** (`ReactorSourceMap`, on by default in Debug builds) **and** it was **on at run
    time** when the element was created (`REACTOR_SOURCEMAP=1`, `REACTOR_DIAGNOSTICS=1` in a
    build with the `Reactor.DevtoolsSupport` switch, a `--devtools` launch, or
    `ReactorSourceMap.Enabled = true`). Otherwise `callSiteFile` is empty and both numbers are
    `0`. Turning source mapping on has its own cost (each stamped element carries its call
    site), so a profiler should not turn it on for a timing run it compares against one
    without.
- **`Reconcile`** — an outermost reconciler entry. Inside a `RenderPass` it is the pass's
  reconcile. Outside any pass it is a row an `ItemsRepeater` / `ListView` realizes or reuses
  during layout. `entry` is `reconcile` or `mount` (which reconciler entry point it was);
  `element` is the element type at the entry (`StackElement`, `ComponentElement`,
  `LazyVStackElement<Int32>` — generic types use the same C# form as `name`), or `null` when
  the entry reconciles to no element. The Stop's `inserted`, `removed`, `moved` and `replaced`
  are the panel child-collection edits made anywhere during the entry: exactly the sum of the
  `ChildReconcile` Stop counts nested in it (see below), but counted at level 4 with only the
  `Pass` keyword, so a capture that leaves the verbose `Children` keyword off can still see
  why a reconcile was expensive. A nested `Reconcile` (another host's, inside this one) counts
  its own edits.
- **`Effects`** — a host root's effects flush (`UseEffect` callbacks). `componentId` is the
  root's. Child components' effects run inside their own `ComponentRender` span.
- **`ChildReconcile`** — one container's child-list reconcile (keyed or positional), including
  applying the result to the WinUI collection. `oldCount` / `newCount` are the element counts;
  the Stop's counts are the edits made to **this** container's collection: `inserted`,
  `removed`, `moved` (one per repositioned child) and `replaced` (a slot whose control was
  swapped in place). A nested container's edits are reported by its own nested span.

### Instant events

- **`StateChange`** — a `UseState` setter, `UseReducer` updater or dispatch, or `UsePersisted`
  setter ran.
  `componentId` is the component
  that owns the hook (the "updater"), assigned on first use if the component has not rendered
  under tracing yet; `hook` is `UseState`, `UseReducer` or `UsePersisted`; `changed` is false when the new
  value equalled the old one (no re-render is requested). May be written from a worker thread
  (`threadSafe: true` hooks); the thread id is in the ETW header. A call from a worker thread
  to a hook that is not thread-safe is marshalled to the UI thread first and reported there.
- **`KeyedListDiagnostic`** — a keyed list or a panel's `.WithKey` siblings had an invalid key.
  `kind` is `duplicateKey` or `nullKey`. `bailedOut` is true when the keyed diff gave up and
  the list is re-realized in full this render (costly); false when it was only reported.
  Written for every occurrence (the log and DevTools deduplicate; this does not). The owning
  component is the enclosing `ComponentRender` span.

## Nesting

Reactor renders on its host's UI thread, so spans nest by time on that thread; a decoder can
pair them with a per-thread stack (Start pushes, the Stop of the same name pops). A typical
update pass:

```
RenderPass (hostId, requester=Counter, requestCount=1)
  ComponentRender (root, reason=state, at App.cs:12)
    Reconcile (entry=reconcile) → (inserted=1, removed=1, moved=1, replaced=0)
      ComponentRender (Counter, reason=state, at MainPage.cs:40)
        ChildReconcile (old=3, new=3) → (inserted=1, removed=1, moved=1, replaced=0)
  Effects (root componentId)
```

A row realized during layout, outside any pass:

```
Reconcile (entry=mount, element=ComponentElement<String>)
  ComponentRender (Row, reason=mount)
```

Rules a decoder can rely on:

- A Stop is written whenever its Start was, including when the work threw. A session that
  starts or stops mid-span can still see a Stop without its Start (or the reverse).
- Within one thread, spans are properly nested; Start/Stop of different spans never interleave.
- `componentId` in a Stop always equals its Start's.

## Recording

Non-elevated, a private session scoped to the app's PID (what `winapp perf` does):
`StartTrace` with `EVENT_TRACE_PRIVATE_LOGGER_MODE | EVENT_TRACE_FILE_MODE_SEQUENTIAL` and a PID
filter in `EVENT_TRACE_PROPERTIES_V2.FilterDesc`, then `EnableTraceEx2` for the GUID above with
the same PID filter. `tests/_shared/EtwPrivateCapture.cs` is a complete, minimal example,
including reading the ETL back through `TdhGetEventInformation`.

Elevated (or as a member of Performance Log Users), any system session works too, for
example:

```
xperf -start ReactorPerf -on 0902a96c-4e92-5b91-4d2b-5a8058055c78:0xF:5 -f reactor.etl
xperf -stop ReactorPerf
```

## Cost

With no session listening, every emit site is one check of a static field (the level the
enable callback recorded) and nothing is computed or allocated. That includes the schema
revision 2 data: the requester claim on a re-render request, the host's request note, and the
`Reconcile` edit tally (one `bool` read per panel child reconcile) all sit behind the same
check. While a session listens, an event is one `EventWriteTransfer` call with
stack-allocated descriptors; names, reasons and call-site file names are existing strings (a
file name is the tail of its path, sharing its terminator), so writing allocates nothing. The
one allocation is a function or memo component's `name`, built once per component on its
first traced render (reading `Delegate.Method` and formatting the name).

## Schema revisions

Fields are only ever **appended** to an event, after the ones it already has; an event keeps
version 0. A decoder written for an earlier revision therefore keeps working as long as it
looks fields up by name (or reads only the leading ones) and ignores fields and event ids it
does not know. The revision is not on the wire: tell revision 2 from revision 1 by the
presence of a field (for example `requesterId` on `RenderPass` Start).

| Revision | Reactor | Change |
|---|---|---|
| 1 | issue #1484 | The provider and events 1–12. |
| 2 | issue #1489 | Appended `requesterId`, `requesterName`, `requestCount` to `RenderPass` Start (1); `callSiteFile`, `callSiteLine`, `callSiteColumn` to `ComponentRender` Start (3); `inserted`, `removed`, `moved`, `replaced` to `Reconcile` Stop (6, which had no fields). Changed the `name` *value* of function and memo components from `FuncElement` / `MemoElement` to their render function's name; a consumer that matched those two strings must match the new names (or `RenderEachTime` / `Memo` / `render` prefixes) instead. |

## Stability

This is a contract with external decoders. Never renumber an event, change a field's type or
order, rename an event or field, or bump an event's version (decoders reject versions they do
not know): append a field at the end of an event, or add a new event id, and record it under
[Schema revisions](#schema-revisions). Keyword bits are stable.
