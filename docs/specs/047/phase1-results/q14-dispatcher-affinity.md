# Q14 — Dispatcher affinity measurement (deferred)

Spec 047 §14 Phase 1 (1.6) decision: keep the existing Debug-only
`DispatcherQueue.HasThreadAccess` check; do **not** tighten to an
unconditional throw in Release until Phase 1's measurement run
confirms no in-tree callers trip it.

## Status

Deferred to a perf run on the Phase 0 baseline machines
(LAPTOP-4MEP83VI ARM64-native and CPC-ander-YTZ3O x64-native). The 1.6
implementation ships:

- `MountContext` / `UpdateContext` / `UnmountContext` are `readonly ref
  struct`s — they cannot escape the dispatcher-affine call stack.
- XML doc comments on each context type call out the UI-thread
  guarantee (Q14).
- The existing Debug-only `HasThreadAccess` check in Reconciler stays
  as-is.

## What the Phase 1 measurement needs to capture

1. Run a Release build of the in-tree test suite (`dotnet test
   tests/Reactor.Tests`) with the check toggled to unconditional and
   record any violations.
2. Repeat for `StressPerf.Reactor` + `StressPerf.ReactorV2` to catch
   non-test call sites.
3. If zero violations: ship the tighten in 1.19 as part of the final
   perf gate sign-off.
4. If non-zero: capture the call sites and decide between (a) fixing
   each to dispatch onto the UI thread, or (b) keeping the Debug-only
   check and documenting the gap in `extensibility-preview.md`.

## Why this gates after the protocol surface lands

The decision needs the V1 dispatch path (1.6) live so test suites
exercise the new context types — measuring before any handler ports
exist would only re-validate the legacy path's existing behavior.
