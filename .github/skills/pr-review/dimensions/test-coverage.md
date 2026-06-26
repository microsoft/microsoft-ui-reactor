# Test coverage review

You are reviewing a PR diff for the `microsoft/microsoft-ui-reactor` repo and asking:
**are the changes adequately covered by tests?** Apply the shared output
contract in `_shared-contract.md`. Set `Domain: test-coverage` on every finding.

## Test tiers in this repo

Pick the right tier per change (see `AGENTS.md` and `TESTING.md`):

| Testing… | Tier | Location |
|---|---|---|
| Algorithm, pure function, hook bookkeeping, reconciler diff, Yoga math | Unit test (xUnit) | `tests/Reactor.Tests/` |
| Element mount/update against real WinUI controls | Selftest fixture | `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` (run via `tests/Reactor.SelfTests`) |
| Real user input, UIA properties, cross-process | E2E (winapp ui) | `tests/Reactor.AppTests/Tests/` |

Default expectation: **start at the lowest tier that exercises the change.** A
new control/handler needs a **selftest fixture** (it requires a live WinUI
control); pure logic needs a **unit test**; new user-input / accessibility
behavior needs an **E2E** test.

## What to look for

- **New control / descriptor / handler, no selftest fixture.** A new
  `ControlDescriptor` / `IElementHandler` or a new control's mount/update path
  added in `src/Reactor/Core/` should have a fixture in
  `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` exercising mount **and** an
  update (prop change) — the update path is where reconciler bugs hide.
- **New hook or hook behavior, no unit test.** New `Use*` helpers or changes to
  the hooks runtime (`RenderContext` slot bookkeeping) need xUnit coverage of
  call-order, deps comparison, and cleanup.
- **New factory / fluent modifier, no unit test.** `Dsl.cs` / `ElementExtensions.cs`
  additions should have a `Reactor.Tests` test asserting the produced element
  shape and that the fluent chain preserves the concrete type.
- **Echo suppression change, no fixture.** Any change to the value-diff arm or
  `WriteSuppressed` path for a value control needs a selftest fixture that drives
  a controlled write and asserts no echo re-render — this is the highest-risk
  area to leave untested.
- **Yoga / Flex layout change, no fixture.** The Yoga port is fixture-driven
  (~590 fixtures). New layout behavior or a math fix needs a corresponding
  fixture; a behavior change with all existing fixtures still green but no new
  fixture for the new case is a coverage gap.
- **New analyzer rule, no analyzer test.** A new/changed `REACTOR_*` diagnostic
  needs tests covering both a positive (fires) and negative (does not fire on
  idiomatic code) case.
- **Source-generator change, no generated-output test.** Changes to the
  Localization / Wrappers generators need tests over the emitted code.
- **Edge cases not tested.** If a correctness concern (null, empty `ItemsSource`,
  reorder, pool reuse, thread timing) is plausible from the diff, check whether a
  test would catch it; if not, that's a coverage finding too.
- **Console-mutating tests missing isolation.** Tests that write to
  `Console.Out` / `Console.Error` must be in `[Collection("ConsoleTests")]`.
  Flag new console-touching tests that omit it (cross-test interference / flake).
- **Brittle tests.** New tests that depend on real timing, machine display
  scaling, external network, or a running winapp CLI without the proper tier
  guard. E2E that should be a selftest, or a selftest that should be a unit test,
  is both slower and flakier — flag the tier mismatch.

## What to drop

- "Increase coverage to 100%" without a specific uncovered scenario.
- Unit tests for trivial record property getters or pure passthrough factories
  with no logic.
- Asking for tests on generated code itself (e.g. generator *output* files) —
  test the generator instead.

## Severity guide for this dimension

- New control/handler or echo-suppression change with zero fixtures → high.
- New public hook / factory / modifier with no test → high.
- New error path / edge case unreachable in current tests → medium.
- New Yoga behavior or analyzer rule without a fixture/test → medium.
- Console-mutating test missing `[Collection("ConsoleTests")]` → high (CI
  flake risk).
- Wrong test tier (E2E where a selftest suffices) → low/medium with a concrete
  recommendation.
