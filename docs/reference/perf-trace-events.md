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
keyword, and level 0 means every level, as with TraceLogging providers.

## Events

Spans are a Start (opcode 1) / Stop (opcode 2) pair with the same event name and **different
event ids** (their fields differ). Instant events use opcode 0. Field types are TraceLogging
in-types: `UInt64` (10), `UInt32` (8), `String` = nul-terminated UTF-16 (1), `Bool32` (13,
4 bytes). All events have version 0 and task 0.

| Id | Name | Opcode | Level | Keyword | Fields |
|---|---|---|---|---|---|
| 1 | `RenderPass` | Start | 4 | `0x1` | `hostId: UInt64` |
| 2 | `RenderPass` | Stop | 4 | `0x1` | `hostId: UInt64` |
| 3 | `ComponentRender` | Start | 4 | `0x2` | `componentId: UInt64`, `name: String`, `reason: String` |
| 4 | `ComponentRender` | Stop | 4 | `0x2` | `componentId: UInt64` |
| 5 | `Reconcile` | Start | 4 | `0x1` | `entry: String`, `element: String` |
| 6 | `Reconcile` | Stop | 4 | `0x1` | — |
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
  `ReactorHost` / `ReactorHostControl` instance.
- **`ComponentRender`** — one component render, **inclusive**: `Render()`, the component's
  effects flush, and the reconcile of the subtree it returned (so the WinUI controls its
  subtree creates fall inside it). Emitted on mount, on update, for a host's root component,
  and for rows a virtualized list realizes. For a host root the span ends before the root's
  effects, which get their own `Effects` span.
  - `componentId` — process-unique, never 0, stable for the component instance's lifetime
    (a hot-reload type swap keeps it). The same id is the `componentId` of the
    `Microsoft-UI-Reactor` EventSource's `ComponentRendered` event (40) and of DevTools'
    `ReactorTrace` component lookups, so a trace can be joined with an in-process capture.
  - `name` — the component's type name (`Counter`, `Row<Int32>`); function and memo components
    report `FuncElement` / `MemoElement`.
  - `reason` — why it rendered: `mount`, `state` (a state change in its subtree), `props`,
    `context`, `parent` (no memo gate could skip it), `hotReload`, `forced`.
- **`Reconcile`** — an outermost reconciler entry. Inside a `RenderPass` it is the pass's
  reconcile. Outside any pass it is a row an `ItemsRepeater` / `ListView` realizes or reuses
  during layout. `entry` is `reconcile` or `mount` (which reconciler entry point it was);
  `element` is the element type at the entry (`StackElement`, `ComponentElement`,
  `LazyVStackElement<Int32>` — generic types use the same C# form as `name`), or `null` when
  the entry reconciles to no element.
- **`Effects`** — a host root's effects flush (`UseEffect` callbacks). `componentId` is the
  root's. Child components' effects run inside their own `ComponentRender` span.
- **`ChildReconcile`** — one container's child-list reconcile (keyed or positional), including
  applying the result to the WinUI collection. `oldCount` / `newCount` are the element counts;
  the Stop's counts are the edits made to **this** container's collection: `inserted`,
  `removed`, `moved` (one per repositioned child) and `replaced` (a slot whose control was
  swapped in place). A nested container's edits are reported by its own nested span.

### Instant events

- **`StateChange`** — a `UseState` setter, `UseReducer` updater or `UseReducer` dispatch ran.
  `componentId` is the component
  that owns the hook (the "updater"), assigned on first use if the component has not rendered
  under tracing yet; `hook` is `UseState` or `UseReducer`; `changed` is false when the new
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
RenderPass (hostId)
  ComponentRender (root, reason=state)
    Reconcile (entry=reconcile)
      ComponentRender (Counter, reason=state)
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
enable callback recorded) and nothing is computed or allocated. While a session listens, an
event is one `EventWriteTransfer` call with stack-allocated descriptors; component names and
reasons are already-interned strings, so writing allocates nothing.

## Stability

This is a contract with external decoders. Never renumber an event, change a field's type or
order, or rename an event or field: add a new event id instead. Keyword bits are stable.
