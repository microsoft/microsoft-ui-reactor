# AOT / trim proof — spec 048 §11 / §13.2

This folder validates that spec 048's lazy registration shape actually
delivers trim wins: when an app uses only `TextBlock` and `Button` from
the Reactor catalog, the AOT-published binary must NOT contain handlers
or WinUI types for the controls it doesn't reach.

## Layout

```
tests/aot_trim_proof/
├── README.md                                  ← this file
├── Reactor.AotHelloWorld/                     ← the minimal Reactor app
│   ├── Reactor.AotHelloWorld.csproj
│   └── App.cs
├── Reactor.AotHelloWorld.TrimAssertions/      ← xUnit assertion harness
│   ├── Reactor.AotHelloWorld.TrimAssertions.csproj
│   └── TrimAssertionTests.cs
└── RegStaticReadBench/                        ← Phase 2 perf baseline (spec §9)
    ├── RegStaticReadBench.csproj
    └── Program.cs
```

`Reactor.AotHelloWorld` is the **smallest possible Reactor app**: a
single `TextBlock` and a single `Button` mounted via `ReactorApp.Run`.
The body of `App.Render()` touches no other catalog factories.

`Reactor.AotHelloWorld.TrimAssertions` is the **empirical guard**. It
binary-scans the AOT-published output for forbidden symbols
(`Marquee*`, `GridViewHandler`, `ListViewHandler`, the descriptor-backed
`*DescriptorHandler` set such as `TreeViewDescriptorHandler` /
`TabViewDescriptorHandler` / `PivotDescriptorHandler`, and the
Reactor-owned element-record names for the same controls) and fails
loudly if any survive. It also asserts a positive control (the entry
point name) so a future trimmer that strips everything cannot silently
pass.

> **§11 caveat applied.** Earlier drafts of the forbidden list also probed
> WinUI control type names (`Microsoft.UI.Xaml.Controls.{TreeView, GridView,
> TabView, CalendarView, NumberBox}`). Empirical observation: those names
> survive in the NativeAOT .exe even when no Reactor factory references
> them, because WinAppSDK's CsWinRT projection layer carries a complete
> type-table for COM activation regardless of which controls the app
> actually uses. The probe is omitted today per spec §11
> ("an SDK regression that re-roots its own controls is out of scope for
> this guard"). Reactor-side rooting is fully covered by the
> Reactor-owned symbol set, which is the spec-relevant invariant.

## Local runbook

From the repo root:

```powershell
# 1. Publish the Hello-World app.
#    Note the PublishAotInternal gate — Reactor.AotHelloWorld.csproj only
#    flips <PublishAot>true</PublishAot> when this property is set,
#    because Reactor.Analyzers (netstandard2.0) cannot tolerate the
#    PublishAot=true propagation through ProjectReferences.
dotnet publish tests/aot_trim_proof/Reactor.AotHelloWorld `
    -c Release -r win-x64 -p:PublishAotInternal=true -p:Platform=x64 `
    -o ${PWD}/artifacts/aot-hello-world

# 2. Point the assertion harness at the publish folder, then test.
$env:REACTOR_AOT_PUBLISH_DIR = "${PWD}/artifacts/aot-hello-world"
dotnet test tests/aot_trim_proof/Reactor.AotHelloWorld.TrimAssertions --nologo
```

The assertion project also auto-discovers the publish folder under
`tests/aot_trim_proof/Reactor.AotHelloWorld/bin/**/publish/` if you
forget the env var, picking the most recent one by mtime.

## Phase 2 perf baseline (`RegStaticReadBench`)

`RegStaticReadBench` is a standalone .NET 10 console app that measures
the per-call cost of the `Reg<TElement, TControl, THandler>.Done`
static-field-read shape spec §7 will distribute across every built-in
factory in Phase 3. It does **not** depend on Reactor — it models the
CLR pattern directly so the baseline isn't sensitive to
WinAppSDK-activation noise.

```powershell
dotnet run -c Release --project tests/aot_trim_proof/RegStaticReadBench
```

Results are written to stdout and analyzed in
[`docs/specs/048/perf-results/phase2-baseline.md`](../../docs/specs/048/perf-results/phase2-baseline.md).
TL;DR: the inline-shape per-call cost is sub-noise (~0.3 ns/op,
indistinguishable from an empty loop); the no-inlining upper bound is
~1.7 ns/op — well below an element-record allocation. The Phase 3 PR
re-runs M1/M2 from `tests/perf_bench/PerfBench.ControlModel` to land
the final empirical proof.

## CI runbook

The `aot-trim-proof` job in `.github/workflows/ci.yml` runs in parallel
with the other CI jobs (gated only by the `changes` filter, not chained
after `aot-selftests`), publishes the Hello-World app with
`-r win-x64 -p:PublishAotInternal=true`, sets `REACTOR_AOT_PUBLISH_DIR`
to the publish folder, and runs the assertion test project.

A failure means **the trim contract broke**. Either:

1. **A Reactor catalog change re-rooted a control.** A new eager
   `Register…` call survived Phase 3, an attribute-driven discovery
   shape leaked through, or a sample got pulled in as a
   `ProjectReference` instead of a separate solution. Investigate the
   reachability chain in the published binary — `ILLink` warnings
   during `dotnet publish` are the first place to look.
2. **A WinAppSDK SDK uplift re-rooted an internal type.** The
   forbidden list intentionally scopes itself to *Reactor-owned*
   class names (per the §11 caveat above — fully-qualified WinUI
   control names like `Microsoft.UI.Xaml.Controls.TreeView` survive
   in the published binary regardless of which Reactor factories are
   reachable, so probing for them would produce false positives).
   If a SDK regression re-roots Reactor-owned handler/element types
   transitively, the right fix is either to wait for the SDK to fix
   or to investigate why the Reactor-side closure no longer trims.
   **Do NOT** simply delete the assertion.

## Failing-loud test

Verify the assertion isn't vacuous by temporarily adding
`Marquee.Of("x")` to `App.Render()`:

```csharp
return VStack(
    TextBlock($"Hello, Reactor! Clicks: {clicks}"),
    Button("Click", () => setClicks(clicks + 1)),
    Marquee.Of("x")   // ← add this; the assertion MUST fail.
);
```

Then publish + test as above. The `PublishedBinary_DoesNotContain_ForbiddenSymbols`
test should fail with a clear violation list naming `MarqueeControl` and
`MarqueeHandler`. Revert the change once you've confirmed the fail-loud
behavior.

## Caveats (spec §11)

- **Scope.** The forbidden list checks **Reactor-side rooting only**.
  WinAppSDK's own internal trim story is evolving; an SDK regression
  that re-roots its own controls is out of scope for this guard.
  Empirically the WinAppSDK CsWinRT projection bag preserves
  `Microsoft.UI.Xaml.Controls.*` type-name strings in the AOT binary
  regardless of which controls the app uses — those probes were tried
  and removed (see comment in `TrimAssertionTests.ForbiddenSymbols`).
  Reactor-owned `*Handler` / `*DescriptorHandler` / `*Element` classes
  are the spec-relevant invariant and are fully covered.
- **NativeAOT vs. trimmed-only.** This project defaults to
  `PublishAot=true`. If the SDK forces a regression to
  `PublishTrimmed=true` + `TrimMode=full` in the future, swap the
  property in `Reactor.AotHelloWorld.csproj` — the assertion harness
  is publish-shape-agnostic.
- **Adding new must-trim controls.** Add the symbol(s) to
  `TrimAssertionTests.ForbiddenSymbols`. The right shape is the
  Reactor-owned class name (`MyControlHandler`, `MyControlElement`,
  or `MyControlDescriptorHandler`) — NOT the WinUI type name (see
  the §11 caveat above for why).

## Why isn't this in `Reactor.slnx`?

By design — the slnx build must not depend on a NativeAOT toolchain or
a successful publish. This project tree is invoked directly by CI and
by the runbook above, so it can opt into heavier infrastructure
(`PublishAot=true`, the WinAppSDK self-contained runtime) without
slowing the routine `dotnet build Reactor.slnx` loop.
