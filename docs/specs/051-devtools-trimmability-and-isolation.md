# Devtools Trimmability and Isolation — Design Proposal

## Status

**Proposed — design converged, not yet implemented.** Tracks the fix for
[issue #497](https://github.com/microsoft/microsoft-ui-reactor/issues/497):
a one-line `TextBlock("Hello, world!")` AOT-published app today carries
~1.5 MB (≈10% of the EXE) of dead devtools code, including `System.Text.Json`
reflection-mode metadata, `System.Net.Http` + `System.Net.Security` +
`System.Security.Cryptography`, the full `Microsoft.UI.Reactor.Hosting.Devtools.*`
namespace, `PreviewCaptureServer`, and the entire `Microsoft.UI.Reactor.Docking.*`
tail pulled through `DevtoolsDockingTools.Register(mcp)`.

The fix is two-phase. Phase 1 is a `[FeatureSwitchDefinition]` + `[FeatureGuard]`
pattern that captures the full ~1.5 MB win, removes the existing
`[UnconditionalSuppressMessage("IL2026")]` suppression, and simplifies the
`ReactorApp.Run` public surface by replacing the `bool devtools = false` /
`bool preview = false` parameters with a build-time MSBuild property. Phase 2
splits devtools into a separate optional `Microsoft.UI.Reactor.Devtools` package
behind a thin `IReactorDevtoolsHost` contract in core, so the devtools IL no
longer ships in `Reactor.dll` at all and apps that don't reference the package
cannot transitively reach `HttpListener`, `JsonSerializer`, or the MCP server
types under any AOT or JIT configuration.

This spec adopts the two-layer activation model:

- **Build-time switch** `Reactor.DevtoolsSupport` (csproj
  `RuntimeHostConfigurationOption`) — "this binary is *permitted* to host
  devtools." Default off. Trims the subsystem out when off.
- **Runtime CLI** `--devtools {run|app|list|screenshot|tree}` — "activate
  devtools *this session*." Unchanged from today.

Devtools activates ⇔ both are true. Neither is sufficient on its own. This
matches the BCL idiom (`EventSource.IsSupported`, `BinaryFormatter.IsEnabled`)
and idioms in adjacent toolchains (Chrome `--remote-debugging-port` requires
build-time non-stripped builds; ssh requires an installed daemon plus
`systemctl start`).

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Current shape and the leak edges](#2-current-shape-and-the-leak-edges)
- [§3 Requirements](#3-requirements)
- [§4 Rejected approaches](#4-rejected-approaches)
- [§5 Phase 1 — FeatureSwitch + FeatureGuard](#5-phase-1--featureswitch--featureguard)
- [§6 Phase 2 — Package split + IReactorDevtoolsHost contract](#6-phase-2--package-split--ireactordevtoolshost-contract)
- [§7 The two-layer activation model](#7-the-two-layer-activation-model)
- [§8 API surface changes and migration](#8-api-surface-changes-and-migration)
- [§9 Trimming model — what gets pruned and how](#9-trimming-model--what-gets-pruned-and-how)
- [§10 Coupling points that must be severed](#10-coupling-points-that-must-be-severed)
- [§11 Validation — mstat regression gate](#11-validation--mstat-regression-gate)
- [§12 Testing](#12-testing)
- [§13 Documentation](#13-documentation)
- [§14 Open questions](#14-open-questions)
- [§15 Phasing](#15-phasing)

---

## §1 Motivation

Two problems, both stemming from the same root cause.

**Problem 1 — retail AOT publishes carry ~1.5 MB of dead devtools code.**
Measured against `samples/apps/hello-world-aot` (one-line `TextBlock`,
`devtools: false`, `PublishAot=true`, all the size-stripping flags from issue
#497):

| Reachable today | Why dead in retail | Size |
|---|---|---:|
| `System.Text.Json` reflection-mode `JsonTypeInfo<>` / `JsonPropertyInfo<>` / `JsonConverter<>` | `DevtoolsMcpServer` serializes responses with a non-source-gen `JsonSerializerOptions` (lines 350, 418) | 0.79 MB |
| `System.Net.Http` (`HttpConnection`, `Http2Connection`, `HttpConnectionPool`) | `LockfileRegistry.cs:252` constructs an `HttpClient` for peer probes | 0.44 MB |
| `System.Net.Security` (`SslStream`) | Transitive from `HttpClient` | 0.12 MB |
| `System.Security.Cryptography` | Transitive from `SslStream` | 0.12 MB |
| `Microsoft.UI.Reactor.Hosting.Devtools.*` (`DevtoolsPropertyTools`, `DevtoolsUiaTools`, `DevtoolsMcpServer`, `DevtoolsTools`, `PreviewCaptureServer`, `DevtoolsJsonContext`, `DevtoolsDockingTools`, `LockfileJsonContext`) | Reachable through `TryRunDevtools` | ~0.08 MB |
| `Microsoft.UI.Reactor.Docking.*` (`DockManager`, `DockHostModel`, `PendingMutation`, `DockSnapshot*`, `DockSplitterControl` WinRT vtable) | Reachable only through `DevtoolsDockingTools.Register(mcp)` (`ReactorApp.cs:1017`) | ~0.034 MB |
| Generic-instantiation / regex tail | Indirect | ~0.05 MB |
| **Total** | | **~1.5 MB** (the issue's refreshed estimate is ~1.94 MB once #498 also lands) |

That's ~10% of a 14 MB EXE. The number scales unfavorably: as the framework
grows, the *floor* of an empty Reactor binary stays inflated by a subsystem the
app never asked for.

**Problem 2 — the devtools subsystem is not isolated from core.** `ReactorApp.cs`
holds a hard static reference to `TryRunDevtools`, which in turn holds hard
references to `DevtoolsMcpServer`, `PreviewCaptureServer`, `DevtoolsDockingTools`
and everything they pull in. The `[UnconditionalSuppressMessage("Trimming",
"IL2026", ...)]` on `Run<TRoot>` at `src/Reactor/Hosting/ReactorApp.cs:298,339`
silences the analyzer signal without giving the trimmer anything actionable.
This makes the framework's security/audit blast radius — the surface area
exposed to a hostile lockfile peer or a hostile MCP client — match the
*devtools-on* shape even in *devtools-off* shipping binaries. That's wrong by
construction: a retail binary should not carry an `HttpListener` it can never
reach.

Both problems collapse to one design lever: **break the static call edge from
core to the devtools subsystem** in a way the trimmer can prove dead.

---

## §2 Current shape and the leak edges

The complete inventory of core→devtools coupling, in priority order
(most-leaking first):

| Edge | Citation | What it drags in |
|---|---|---|
| `Run<TRoot>` → `TryRunDevtools` | `src/Reactor/Hosting/ReactorApp.cs:313,353` | Everything below |
| `RunRunSubverb` → `new PreviewCaptureServer(...)` | `src/Reactor/Hosting/ReactorApp.cs:925` | `PreviewCaptureServer` itself (in `Hosting/`, not `Hosting/Devtools/`) |
| `RunRunSubverb` → `DevtoolsDockingTools.Register(mcp)` | `src/Reactor/Hosting/ReactorApp.cs:1017` | All of `Microsoft.UI.Reactor.Docking.*` |
| `DevtoolsMcpServer.HandleAsync` → `JsonSerializer.Serialize` | `src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs:350,418` | Reflection-mode `System.Text.Json` |
| `LockfileRegistry.ProbeAsync` → `HttpClient.GetAsync` | `src/Reactor/Hosting/Devtools/LockfileRegistry.cs:252` | `System.Net.Http`, `System.Net.Security`, `System.Security.Cryptography`, header-parsing regex tail |
| `Element` equality switch → `DockSplitterElement` / `DockDropTargetOverlayElement` | `src/Reactor/Core/Element.cs:845-851` | Type metadata for two Docking records — **independent of devtools** but tracked here because it blocks the #498 sister-cleanup |
| `ReactorApp.DevtoolsEnabled` (bool field) | `src/Reactor/Hosting/ReactorApp.cs:274-278` | Nothing — leaf bool |
| `UseDevtoolsExtensions.UseDevtools` | `src/Reactor/Hooks/UseDevtools.cs:27` | Nothing — reads the bool above |
| `Factories.DevtoolsMenu` | `src/Reactor/Hosting/Devtools/DevtoolsMenuFactory.cs:46` | `MenuFlyout` + `Button` + `ContentDialog` (already in core, no devtools-subsystem leak) |

Two static call edges (the first two rows) account for the entire ~1.5 MB.
Everything else is reachable only through them. That is what makes a single
feature switch sufficient for Phase 1.

The third row (`DevtoolsDockingTools`) is the link that surfaces the Docking
namespace as devtools-only-reachable in `hello-world-aot`. The bottom row
(`Element.cs:845-851`) keeps two Docking record types reachable *regardless* of
the devtools fix — that's #498's territory, not this spec's, but Phase 2 here
removes the last devtools-side dependency, leaving #498 as the only remaining
core→Docking edge.

---

## §3 Requirements

In priority order:

1. **Retail AOT publishes do not pay for devtools.** With the build-time switch
   off and `PublishAot=true`, `hello-world-aot.exe` must drop from ~14.03 MB
   to ≤12.5 MB. No type with namespace `Microsoft.UI.Reactor.Hosting.Devtools.*`,
   no `PreviewCaptureServer`, no `JsonSerializer<>` / `JsonTypeInfo<>` /
   `JsonPropertyInfo<>` / `JsonConverter<>` reflection-mode instantiations, no
   `HttpClient` / `HttpListener` / `SslStream`, no `Microsoft.UI.Reactor.Docking.*`
   (modulo the #498 leak from `Element.cs:845-851`, which this spec does not
   close) appears in the published mstat.
2. **Devtools developer flow is unchanged in spirit.** An app developer who
   opts into devtools (one csproj line) and launches with `--devtools run` /
   `--devtools app` gets exactly the surface they have today — VS Code preview,
   MCP server, screenshot capture, log capture, docking tools.
3. **No `[UnconditionalSuppressMessage("Trimming", ...)]` on `Run`.** The fix
   must give the trimmer real information, not silence the analyzer.
4. **Public API simplifies, not complicates.** The `Run` overloads lose the
   `bool devtools = false` and `bool preview = false` parameters and the
   `ResolveDevtoolsParam` helper. No new public knob is added to compensate;
   the build-time switch lives in MSBuild, where build-time consent belongs.
5. **Activation stays two-layered.** Build-time switch grants *capability*;
   runtime `--devtools` arg grants *activation*. Either alone is insufficient.
6. **CLI fallback message is meaningful.** A user who runs `--devtools run`
   against a binary that doesn't have the switch on must see a clear
   actionable error (the exact `<RuntimeHostConfigurationOption>` snippet to
   add), not silent fall-through to a normal window.
7. **No new analyzer / source generator / MSBuild prop required.** The fix
   uses only stock .NET 9 attributes (`[FeatureSwitchDefinition]`,
   `[FeatureGuard]`) and the stock `RuntimeHostConfigurationOption` item type.

The Phase 2 split adds two more requirements on top:

8. **Devtools IL does not ship in `Reactor.dll`.** An ILSpy decomp of the
   retail `Reactor.dll` must contain no `Microsoft.UI.Reactor.Hosting.Devtools.*`
   types, no `PreviewCaptureServer`, no `LockfileRegistry`.
9. **Apps that don't reference `Microsoft.UI.Reactor.Devtools` cannot
   transitively resolve any devtools type, by reflection or otherwise.** The
   devtools package is the only place the IL exists.

---

## §4 Rejected approaches

### §4.1 `#if DEBUG` around the devtools branch

Tempting one-liner; wrong answer. Test-host projects (`Reactor.AppTests.Host`)
and inproc CI scenarios build in Release with devtools intentionally
enabled. `#if` couples the switch to build configuration when it should be
coupled to a deployment property. Also leaves the `[UnconditionalSuppressMessage]`
in place for non-DEBUG builds (still wrong).

### §4.2 Source-gen `JsonContext` only

The second comment on issue #497 attributes the 0.79 MB JSON tail to two
`JsonSerializer.Serialize(...)` calls in `DevtoolsMcpServer.cs` (lines 350,
418) that use a non-source-gen `JsonSerializerOptions`. Replacing those with
a `JsonSerializerContext` *would* cut tens of KB but it is the wrong fix:
it does nothing about the 1.0 MB `System.Net.Http` + `SslStream` tail, the
~0.08 MB direct devtools namespace, or the ~0.034 MB Docking tail. The whole
graph must become statically dead; a leaf-level optimization in one node of
the graph does not help when the rest of the graph remains reachable.

### §4.3 FeatureGuard *only*, no parameter removal

Keeping the `bool devtools = false` parameter alongside the switch is
incoherent: the parameter is then strictly weaker than the switch (it can
never grant capabilities the switch denies, since the IL it would call is
trimmed), and consumers face two knobs for one decision. Worse, the parameter
becomes a *foot-shaped trap*: `Run(devtools: true)` in code with the switch
off compiles, runs, and silently does nothing. The two-knob shape is
indefensible. Either the parameter goes (this spec) or the switch goes (and
we lose trimming). The parameter goes.

### §4.4 Package split *first*, no feature switch

The cleanest end-state is Phase 2 alone. But it's a big migration: it changes
how every Reactor app consumes devtools (add a `<PackageReference>` to
`Microsoft.UI.Reactor.Devtools`), it requires a new release of a new package
synced to the framework version, and it doesn't deliver the binary-size win
to anyone who doesn't immediately re-publish against the new shape. The
layered approach delivers the win in one PR (Phase 1) and de-risks the
split (Phase 2) by giving Phase 2 a verified mstat regression gate to land
behind.

### §4.5 Conditional `PackageReference` + reflection-only entry

Issue #497 lists this as "less ideal because devtools today is hard-coupled
to `ReactorApp.Run`'s overload signature." Phase 1's switch + Phase 2's
contract together remove that coupling without resorting to reflection-only
plumbing; reflection would defeat the trimming goal anyway.

---

## §5 Phase 1 — FeatureSwitch + FeatureGuard

### §5.1 The switch type

Add to `src/Reactor/Hosting/` (or a new `src/Reactor/Hosting/Features/`
folder if the namespace lands cleaner):

```csharp
namespace Microsoft.UI.Reactor.Hosting;

internal static class ReactorFeatures
{
    /// <summary>
    /// Build-time consent gate for the devtools subsystem (MCP server, preview
    /// capture, lockfile registry, docking tools, log capture). Default off.
    ///
    /// Apps that need devtools enable the switch in their csproj:
    /// <code>
    /// &lt;RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
    ///                                 Value="true" Trim="true" /&gt;
    /// </code>
    /// With <c>Trim="true"</c> the ILC trimmer substitutes this property body
    /// with the configured constant at publish time, and every dead-arm
    /// devtools call chain (DevtoolsMcpServer, PreviewCaptureServer,
    /// LockfileRegistry, DevtoolsDockingTools, the System.Text.Json /
    /// System.Net.Http tails) gets pruned. See spec 051.
    /// </summary>
    [FeatureSwitchDefinition("Reactor.DevtoolsSupport")]
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
    internal static bool IsDevtoolsSupported =>
        AppContext.TryGetSwitch("Reactor.DevtoolsSupport", out var on) && on;
}
```

Both `FeatureGuard` attributes are required: `TryRunDevtools` is
`[RequiresUnreferencedCode]` (line 692), and the JSON / MCP serialization
chain pulls `[RequiresDynamicCode]` warnings through reflection-mode
`JsonSerializer`.

### §5.2 Guard placement

Two guard points cover the entire leak surface:

1. **`Run<TRoot>` / `Run` overloads.** The outer guard. Today:
   ```csharp
   var effectiveDevtools = ResolveDevtoolsParam(devtools, preview);
   if (effectiveDevtools && TryRunDevtools(...)) return;
   ```
   Becomes:
   ```csharp
   if (ReactorFeatures.IsDevtoolsSupported && TryRunDevtools(...)) return;
   ```
   `ResolveDevtoolsParam`, the `_previewParamDeprecationWarned` field, and the
   `effectiveDevtools` local all delete.

2. **`PreviewCaptureServer` ctor site.** `ReactorApp.cs:925` (inside
   `RunRunSubverb`) news up `PreviewCaptureServer` directly. Even though
   the surrounding function is `[RequiresUnreferencedCode]`, the ctor reference
   keeps `PreviewCaptureServer` reachable from `Run` via the (now-substituted)
   guard. With the substitution in place this becomes statically dead via the
   call chain from `Run` and no separate guard is needed at the ctor site —
   but we annotate `PreviewCaptureServer`'s ctor with
   `[RequiresUnreferencedCode("Devtools subsystem; gated by Reactor.DevtoolsSupport.")]`
   anyway so an out-of-tree caller (test, sample, future code) gets the
   warning instead of a silent leak.

The `[UnconditionalSuppressMessage("Trimming", "IL2026", ...)]` attributes
on `Run<TRoot>` (line 298) and `Run` (line 339) **delete**. With FeatureGuard
the analyzer is satisfied.

### §5.3 MSBuild plumbing

Add a default-off declaration to the framework's `.targets` (shipped in
the nupkg under `build/`):

```xml
<!-- Reactor.targets shipped under build/ in Microsoft.UI.Reactor.nupkg -->
<Project>
  <ItemGroup>
    <!-- Default: devtools subsystem is trimmed out of AOT publishes.
         Apps that need devtools override by adding their own
         <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
                                          Value="true" Trim="true" />
         after this targets file is imported. SDK uses last-write-wins for
         duplicate Include keys on RuntimeHostConfigurationOption. -->
    <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
                                    Value="false"
                                    Trim="true" />
  </ItemGroup>
</Project>
```

**Open question Q1** in §14: verify last-write-wins behavior holds for
`RuntimeHostConfigurationOption` items in `dotnet publish`; if not, the
consumer pattern becomes `<RuntimeHostConfigurationOption
Remove="Reactor.DevtoolsSupport" />` followed by re-add. The targets file
should encode whichever idiom actually works.

### §5.4 CLI parser stays unguarded

`DevtoolsCliParser.Parse(args)` is pure string parsing with no transitive
reference to MCP / JSON / HTTP types. It stays in core, unguarded, so that
switch-off binaries can still detect a stray `--devtools run` on the command
line and emit the actionable error:

```csharp
var options = DevtoolsCliParser.Parse(Environment.GetCommandLineArgs());
if (options.Subverb is null) return false;          // no --devtools arg → normal run

if (!ReactorFeatures.IsDevtoolsSupported)
{
    Console.Error.WriteLine(
        "[reactor] --devtools requested but this binary was built without " +
        "Reactor.DevtoolsSupport. Add the following to your csproj and rebuild:");
    Console.Error.WriteLine(
        "  <RuntimeHostConfigurationOption Include=\"Reactor.DevtoolsSupport\" " +
        "Value=\"true\" Trim=\"true\" />");
    return true;
}

return DispatchDevtoolsSubverb(options, ...);       // guarded → trimmable
```

Refactor `TryRunDevtools` to split the parse from the dispatch. Verify
`DevtoolsCliParser` actually has no transitive reach into the devtools
subsystem (option records carry primitives only); if it does, factor the
option types to a pure-data leaf record so the parser stays trim-clean.

### §5.5 Affected internal call sites

`DevtoolsEnabled` (lines 730, 741, 1121) is set by code inside the guarded
arm and read from `UseDevtools` (which is itself a leaf — just returns the
bool). No changes needed; when the guard's arm is dead the writes are dead
too, and the bool stays false, and `UseDevtools` returns false everywhere
forever, and `DevtoolsMenu(...)` returns `Empty()` at line 52 of
`DevtoolsMenuFactory.cs`. Consumer code paths that gate on
`ctx.UseDevtools()` simply never construct their dev-only subtrees. This is
already the documented contract (`UseDevtoolsExtensions` comment at line 19:
"so the subtree is never constructed in retail sessions").

---

## §6 Phase 2 — Package split + IReactorDevtoolsHost contract

Phase 1 captures the binary-size win and removes the suppression. It does not
remove devtools IL from `Reactor.dll`. Phase 2 closes that gap.

### §6.1 The contract type (lives in core)

```csharp
namespace Microsoft.UI.Reactor.Hosting.Devtools;

public sealed record ReactorDevtoolsBootRequest(
    string Title,
    double Width,
    double Height,
    bool FullScreen,
    Action<ReactorHost>? Configure,
    Type? HostRoot);

public interface IReactorDevtoolsHost
{
    /// Returns true if the CLI verbs consumed the launch (the caller should
    /// not proceed to the normal Run loop). False = no devtools verb on the
    /// command line, normal Run loop should proceed.
    bool TryHandleCommandLine(string[] args, ReactorDevtoolsBootRequest request);

    /// Built-in surface for Factories.DevtoolsMenu. Returns null when the
    /// session has no devtools UI active; the factory shim translates null
    /// to Factories.Empty().
    Element? BuildDevtoolsMenu(
        Func<IEnumerable<MenuFlyoutItemBase>>? items,
        string glyph, string toolTip, string? automationId);
}

public static class ReactorDevtoolsBootstrap
{
    private static IReactorDevtoolsHost? _host;
    public static void Register(IReactorDevtoolsHost host) => _host = host;
    internal static IReactorDevtoolsHost? Current => _host;
}
```

The contract has zero dependencies on JSON, HTTP, MCP, capture, or docking
tools. It carries primitives + `Element` + standard WinUI types only.

### §6.2 New package layout

`src/Reactor.Devtools/` → `Microsoft.UI.Reactor.Devtools.nupkg`. Contents:

| Moved from | To |
|---|---|
| `src/Reactor/Hosting/Devtools/*` (all 27 files) | `src/Reactor.Devtools/` |
| `src/Reactor/Hosting/PreviewCaptureServer.cs` | `src/Reactor.Devtools/` |
| `RunListSubverb`, `RunRunSubverb`, `RunScreenshotSubverb`, `RunSlotMachineSubverb` (in `ReactorApp.cs`, the `[RequiresUnreferencedCode]` chain) | `src/Reactor.Devtools/DevtoolsHost.cs` as `IReactorDevtoolsHost.TryHandleCommandLine` implementation |
| The body of `Factories.DevtoolsMenu` | `src/Reactor.Devtools/DevtoolsMenuFactory.cs` as `IReactorDevtoolsHost.BuildDevtoolsMenu` |

Stays in core:

- The `ReactorDevtoolsBootstrap` contract (above).
- A shim `Factories.DevtoolsMenu` that calls
  `ReactorDevtoolsBootstrap.Current?.BuildDevtoolsMenu(...) ?? Empty()`.
  Source-compatible for every consumer.
- `DevtoolsCliParser` — pure string parsing, no devtools-subsystem refs,
  needed by Phase 1's switch-off fallback message. (If it turns out the
  parser has transitive devtools-subsystem refs, move it too and inline a
  minimal "is `--devtools` present?" string check in core.)
- `ReactorApp.DevtoolsEnabled` bool (the leaf-level read state).

### §6.3 Module initializer registers the host

```csharp
// src/Reactor.Devtools/ModuleInit.cs
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        ReactorDevtoolsBootstrap.Register(new DevtoolsHost());
    }
}
```

When the package is referenced, its module initializer runs on first type
load and self-registers. When the package is not referenced, the assembly
isn't loaded and `ReactorDevtoolsBootstrap.Current` stays null.

### §6.4 Core's Run loop after Phase 2

```csharp
public static void Run<TRoot>(...) where TRoot : Component, new()
{
    EmitDipBehaviorChangeNoticeOnce();

    var args = Environment.GetCommandLineArgs();
    var options = DevtoolsCliParser.Parse(args);
    if (options.Subverb is not null)
    {
        if (ReactorDevtoolsBootstrap.Current is { } host
            && ReactorFeatures.IsDevtoolsSupported)   // belt + braces
        {
            var req = new ReactorDevtoolsBootRequest(title, width, height,
                fullScreen, configure, typeof(TRoot));
            if (host.TryHandleCommandLine(args, req)) return;
        }
        else
        {
            EmitDevtoolsNotAvailableMessage(options);
            return;
        }
    }

    RunOnSta(() => { /* normal Application.Start, unchanged */ });
}
```

No `[RequiresUnreferencedCode]` on `Run`. No `[UnconditionalSuppressMessage]`.
The trimmer sees `ReactorDevtoolsBootstrap.Current` as a property that may
return null and walks no further; the `host.TryHandleCommandLine` call is
through an interface and the trimmer cannot follow it without an
implementation in the closure set. When the Devtools package is not
referenced, no implementation exists in the closure set, the call site is
provably uncallable, and the trimmer prunes everything behind it.

### §6.5 Why both `ReactorDevtoolsBootstrap.Current is not null` *and*
`ReactorFeatures.IsDevtoolsSupported`?

Defense in depth. After Phase 2 the package reference is the *primary* gate:
if the consumer doesn't reference the package, the trimmer prunes everything,
even with the switch on. The feature switch becomes the *secondary* gate for
the case where:

- A test host references the Devtools package (so the implementation is
  present at compile time) but specific test scenarios want to run with the
  switch off to exercise the no-devtools code paths.
- A developer wants to publish a single binary that ships with the package
  reference but lets the deployment decide whether devtools activates.

This dual-gate shape also keeps the Phase 1 → Phase 2 migration smooth: the
switch remains the consumer-facing knob; the package reference is plumbing.

---

## §7 The two-layer activation model

The semantic model after both phases:

```
Devtools actually activates  ⇔
    Reactor.Devtools package is referenced            (Phase 2: build-time)
    AND Reactor.DevtoolsSupport switch is true        (Phase 1: build-time)
    AND --devtools {run|app|...} is on the CLI        (runtime, unchanged)
```

The two build-time gates collapse to one knob from the consumer's perspective
(they're both csproj properties). The runtime gate stays separate and matches
the existing `--devtools` verb structure unchanged.

This separation is intentional and important:

| Wrong shape | Why wrong |
|---|---|
| Switch only, no CLI gate | Every debug build with the switch on would spawn an MCP listener on every launch — security and port-conflict footgun. Also `--devtools list`, `--devtools screenshot`, `--devtools tree` are inherently CLI-driven and have no meaning without an entry point. |
| CLI only, no build-time gate | Can't trim. Devtools IL ships in every binary, the whole point of this spec is lost. |
| One conflated `--devtools` arg that both builds and activates | Architecturally impossible: the trimmer runs at publish time, the CLI is parsed at process start. The two events are months apart. |

The build-time half is *capability + binary size*; the runtime half is
*activation + intent*. Same separation as `sshd` install vs.
`systemctl start sshd`, Chrome's non-stripped retail builds vs.
`--remote-debugging-port`, etcd's `--enable-pprof` build tag vs.
`--listen-metrics-urls` runtime flag.

---

## §8 API surface changes and migration

### §8.1 What deletes

| Symbol | Citation | Replacement |
|---|---|---|
| `bool devtools = false` parameter on `Run<TRoot>` | `ReactorApp.cs:304` | csproj `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="true" Trim="true" />` |
| `bool preview = false` parameter on `Run<TRoot>` | `ReactorApp.cs:307` | (already deprecated; collapses) |
| `bool devtools = false` parameter on `Run` (non-generic) | `ReactorApp.cs:346` | as above |
| `bool preview = false` parameter on `Run` (non-generic) | `ReactorApp.cs:349` | (collapses) |
| `internal static bool ResolveDevtoolsParam(bool, bool)` | `ReactorApp.cs:677` | gone |
| `_previewParamDeprecationWarned` field | `ReactorApp.cs` (referenced from 677-684) | gone |
| `[UnconditionalSuppressMessage("Trimming", "IL2026", ...)]` on both `Run` overloads | `ReactorApp.cs:298,339` | gone |

### §8.2 What stays

- `ReactorApp.DevtoolsEnabled` — semantics tighten to `(switch on) AND (CLI
  arg present) AND (subverb handled)` but the property signature and meaning
  for consumers (`UseDevtools()`, `Factories.DevtoolsMenu`) are unchanged.
- `UseDevtoolsExtensions.UseDevtools` — bit-for-bit unchanged.
- `Factories.DevtoolsMenu` — public signature unchanged. After Phase 2 the
  body becomes a one-line shim to `ReactorDevtoolsBootstrap.Current`.
- The `--devtools {run|app|list|screenshot|tree}` CLI verbs — entirely
  unchanged.

### §8.3 Migration story for app authors

**Today:**
```csharp
// Program.cs
ReactorApp.Run<MyApp>("Title", devtools: true);
```

**After Phase 1:**
```xml
<!-- MyApp.csproj -->
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
                                  Value="true" Trim="true" />
</ItemGroup>
```
```csharp
// Program.cs
ReactorApp.Run<MyApp>("Title");
```

**After Phase 2** (additional):
```xml
<!-- MyApp.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.UI.Reactor.Devtools" Version="$(ReactorVersion)" />
</ItemGroup>
```

Common conditional patterns (Debug-only devtools):

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
                                  Value="true" Trim="true" />
  <PackageReference Include="Microsoft.UI.Reactor.Devtools" Version="$(ReactorVersion)" />
</ItemGroup>
```

### §8.4 Deprecation timing for the `devtools:` parameter

Phase 1 deletes the parameter outright. Reactor is pre-1.0 (`Reactor.csproj`
`Version` defaults to `0.0.0-local`); the API surface is explicitly not yet
stable. The CHANGELOG entry and `docs/aot-support.md` migration note are the
deprecation mechanism. If the project decides on a softer landing, the
fallback is to keep the parameter for one release as
`[Obsolete("Use Reactor.DevtoolsSupport in your csproj; this parameter is now ignored.")]`
and have it log a one-time stderr warning when set to `true`. The Open
Question is recorded as §14 Q3.

### §8.5 Affected in-repo call sites

Greppable surface area for the parameter removal (count from current main):

- `samples/` — any sample that passes `devtools: true` needs the switch added
  to its csproj. The Devtools demo sample is the obvious one; others may
  also need it.
- `tests/Reactor.AppTests.Host/` — the test host calls `Run` with devtools
  enabled to exercise the MCP / preview path; csproj edit + Program.cs
  parameter removal.
- `docs/_pipeline/templates/*.md.dt` — any `devtools: true` in a doc snippet
  needs replacing with the csproj pattern. Re-run `mur docs compile`.

All mechanical. No design decisions hidden in these.

---

## §9 Trimming model — what gets pruned and how

### §9.1 The mechanism (Phase 1)

`[FeatureSwitchDefinition("Reactor.DevtoolsSupport")]` tells the ILC trimmer:
when the named switch is configured at publish time (via
`<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
Value="false" Trim="true" />` in the publish closure), replace the body of
the annotated property with the constant `return false;` before doing
reachability analysis. The trimmer then sees:

```csharp
// As the trimmer sees it after substitution:
if (false && TryRunDevtools(...)) return;
```

`false && ...` short-circuits → `TryRunDevtools` reference is dead → walk no
further. Every type reachable only through `TryRunDevtools` becomes
unreachable. That includes the entire devtools namespace, `PreviewCaptureServer`,
`DevtoolsDockingTools`, and everything they transitively use
(`JsonSerializer`, `HttpClient`, `HttpListener`, `SslStream`, the
`System.Text.Json` generic instantiation graph, the `System.Net.Http` header
regex tail, the entire `Microsoft.UI.Reactor.Docking.*` namespace except for
the two records hard-referenced from `Element.cs:845-851`).

`[FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]` and
`[FeatureGuard(typeof(RequiresDynamicCodeAttribute))]` tell the analyzer
(separately from the trimmer): callers may invoke methods annotated with
those `Requires*` attributes from inside an `if (ReactorFeatures.IsDevtoolsSupported)`
arm without triggering IL2026 / IL3050. This is what allows the
`[UnconditionalSuppressMessage]` on `Run` to delete: the FeatureGuard
provides the same analyzer relief, but declaratively and with a typed link
back to the guard.

### §9.2 The mechanism (Phase 2)

After Phase 2 the trimmer doesn't need to substitute anything for apps that
omit the Devtools package: the package's assembly is simply not in the
closure set. Calls through `IReactorDevtoolsHost` are interface calls
through a static property that the trimmer can observe is initialized only
from the never-loaded assembly. With no concrete implementation reachable,
the trimmer prunes the interface dispatch and everything behind it.

For apps that *do* reference the Devtools package but have the switch off,
the Phase 1 FeatureSwitch still substitutes the property to `false` and the
package's own contents get pruned at publish time through dead-arm
elimination inside the Devtools assembly's own `TryHandleCommandLine`
implementation.

### §9.3 What does *not* get pruned

- `ReactorApp.DevtoolsEnabled` bool field — leaf, no leak, cost is two bytes.
- `UseDevtoolsExtensions.UseDevtools` — leaf, returns the bool.
- `Factories.DevtoolsMenu` body (Phase 1 only) — returns `Empty()` at line 52
  when `DevtoolsEnabled` is false. After Phase 2 the body becomes a one-line
  shim and the meaty implementation lives in the Devtools package.
- `DevtoolsCliParser` (Phase 1) — needed by the switch-off fallback message.
  Pure string parsing, no transitive leak; expected size cost a few KB at
  most. Phase 2 may or may not move it; see §6.2.
- `Element.cs:845-851` Docking equality switch — independent of devtools,
  remains the #498 leak. Not closed by this spec.

### §9.4 Cross-check against the issue's success criteria

The issue's validation checklist (refreshed in the second comment) requires:

- [x] Reflection-mode `JsonTypeInfo<>` / `JsonPropertyInfo<>` / `JsonConverter<>` gone — covered by §5.2 outer guard pruning `DevtoolsMcpServer`.
- [x] No `HttpClient` / `SocketsHttpHandler` / `HttpConnection` / `Http2Connection` — covered, prunes `LockfileRegistry`.
- [x] No `SslStream` — transitive from above.
- [x] `Microsoft.UI.Reactor.Navigation` size unchanged at ~64 bytes — this spec touches nothing in Navigation, so the floor is preserved.
- [x] No `Hosting.Devtools.DevtoolsDockingTools` — covered.
- [x] No `Microsoft.UI.Reactor.Docking.*` (modulo the two records pinned from `Element.cs`) — covered by removing the `DevtoolsDockingTools.Register(mcp)` reference; remaining residue is #498 territory.
- [x] No `WinRT.ReactorVtableClasses.…DockSplitter…` — same.
- [x] `HelloWorldAot.exe` drops from ~14.03 MB to **~12.5 MB target** — this is the headline number.

The full refreshed estimate is ~1.94 MB once #498 also lands; this spec
delivers the ~1.5 MB portion that's attributable to devtools alone.

---

## §10 Coupling points that must be severed

Concrete file-and-line list, in order of which phase closes them:

| # | Edge | Citation | Phase |
|---|---|---|---|
| 1 | `Run<TRoot>` direct call to `TryRunDevtools` | `ReactorApp.cs:313` | Phase 1 (switched off → dead arm) |
| 2 | `Run` non-generic direct call to `TryRunDevtools` | `ReactorApp.cs:353` | Phase 1 (same) |
| 3 | `RunRunSubverb` direct `new PreviewCaptureServer(...)` | `ReactorApp.cs:925` | Phase 1 (transitively dead) / Phase 2 (file moves) |
| 4 | `RunRunSubverb` call to `DevtoolsDockingTools.Register(mcp)` | `ReactorApp.cs:1017` | Phase 1 (transitively dead) / Phase 2 (file moves) |
| 5 | `Reactor.dll` IL contains devtools namespace types | (everywhere under `Hosting/Devtools/`) | Phase 2 (move to `Reactor.Devtools.dll`) |
| 6 | `Reactor.dll` IL contains `PreviewCaptureServer` | `Hosting/PreviewCaptureServer.cs` | Phase 2 (move) |
| 7 | `Element` equality switch hard-refs `DockSplitterElement` / `DockDropTargetOverlayElement` | `Element.cs:845-851` | **Out of scope — #498** |
| 8 | `[UnconditionalSuppressMessage("Trimming", "IL2026")]` on `Run<TRoot>` | `ReactorApp.cs:298` | Phase 1 (deleted) |
| 9 | Same on `Run` non-generic | `ReactorApp.cs:339` | Phase 1 (deleted) |
| 10 | `bool devtools = false` / `bool preview = false` parameters | `ReactorApp.cs:304,307,346,349` | Phase 1 (deleted) |
| 11 | `ResolveDevtoolsParam` helper | `ReactorApp.cs:677-684` | Phase 1 (deleted) |

After Phase 1 + Phase 2, the only residual core→devtools-adjacent coupling is
#7, which is #498's problem.

---

## §11 Validation — mstat regression gate

The fix without a regression gate is one careless `using` away from being
silently undone. Add a CI lane that:

1. Publishes `samples/apps/hello-world-aot` with `PublishAot=true` and the
   default (switch-off) configuration.
2. Reads the `.mstat` file (`obj/.../native/HelloWorldAot.mstat`).
3. Asserts the following types are **absent** from the per-type table:
   - any type whose namespace starts with `Microsoft.UI.Reactor.Hosting.Devtools.`
   - `Microsoft.UI.Reactor.Hosting.PreviewCaptureServer`
   - `System.Net.Http.HttpClient`, `SocketsHttpHandler`, `HttpConnection`, `Http2Connection`
   - `System.Net.Security.SslStream`
   - `System.Net.HttpListener`
   - `System.Text.Json.Serialization.Metadata.JsonTypeInfo`1`
   - `System.Text.Json.Serialization.JsonConverter`1`
4. Asserts the published `HelloWorldAot.exe` size is ≤ 12.5 MB (margin for
   monthly drift; tighten over time).
5. Also asserts a **positive control**: a sibling
   `samples/apps/hello-world-aot-devtools-on` (with the switch in csproj)
   contains those types. This catches the regression where the switch
   accidentally becomes a no-op.

Mechanism: use `Mono.Cecil` or sizoscope's own `MstatData*.cs` parser (the
issue describes a one-file tool at
`C:\Users\andersonch\AppData\Local\Temp\sizo-cli\`; upstream it as
`tools/Reactor.MstatVerifier/` so CI doesn't depend on a dev machine path).
The whole verification step should be ~30 lines of C# against the parsed
mstat — keep it small enough that contributors can read it.

CI lane cost: one publish (~10 min for `hello-world-aot`) + one mstat parse
(seconds). Run on every PR that touches `src/Reactor/Hosting/**` or
`src/Reactor.Devtools/**`. Run on a nightly lane unconditionally.

After Phase 2, add a second assertion: ILSpy / `Mono.Cecil` scan of
`Reactor.dll` (the framework assembly itself, regardless of consumer
publish) contains zero types under `Microsoft.UI.Reactor.Hosting.Devtools.`
namespace and no `PreviewCaptureServer`.

---

## §12 Testing

### §12.1 Unit tests

Place in `tests/Reactor.Tests/Hosting/`:

- `ReactorFeatures_IsDevtoolsSupported_ReadsAppContextSwitch` — toggle the
  switch via `AppContext.SetSwitch(...)`, observe the property reflects it.
- `ReactorFeatures_IsDevtoolsSupported_DefaultsOff` — with no switch set, the
  property returns false.
- `DevtoolsCliParser_RecognizesDevtoolsVerbs_WithoutLoadingHandlers` — parse
  `--devtools run --mcp-port 1234`, observe the options record is populated;
  no handler types are constructed. Reflection probe: assert
  `typeof(DevtoolsMcpServer)` is never `Activator.CreateInstance`'d during
  the test.
- `ReactorApp_Run_SwitchOff_WithDevtoolsArg_EmitsActionableError` — capture
  stderr, run `Run` with `--devtools run` in `Environment.GetCommandLineArgs`
  and the switch off, assert the message contains the
  `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"` snippet.

### §12.2 Selftest fixtures

Place in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Spec051*`:

- `DevtoolsMenu_SwitchOff_RendersEmpty` — render a tree containing
  `DevtoolsMenu(...)` with the switch off; assert no `MenuFlyout` materialized.
- `DevtoolsMenu_SwitchOn_RendersTrigger` — same with switch on; assert the
  ⚡ trigger materialized.
- `UseDevtools_SwitchOff_ReturnsFalse` — `ctx.UseDevtools()` inside a
  component returns false.
- `UseDevtools_SwitchOn_PlusCli_ReturnsTrue` — switch on + `--devtools app`;
  returns true.

Register all four fixtures in `SelfTestFixtureRegistry.cs` (both the
fixture-name list at line 198 and the constructor switch arm at line 1369,
per the registry's two-place registration convention).

### §12.3 E2E tests

Place in `tests/Reactor.AppTests/Tests/`:

- `Spec051_DevtoolsCliFallback_EmitsActionableError` — launch a published
  binary with switch off, pass `--devtools run`, assert exit code + stderr
  content.
- `Spec051_DevtoolsEndToEnd_SwitchOn_McpServerStarts` — launch a published
  binary with switch on + `--devtools run --mcp-port 0`, assert the MCP
  server announces ready on the ephemeral port and accepts a basic
  `tools/list` JSON-RPC request.

### §12.4 mstat regression (§11)

Single test in the new `tools/Reactor.MstatVerifier/` runner, invoked from CI.

---

## §13 Documentation

Updates required:

- `docs/aot-support.md` — replace the existing devtools/preview language
  (lines 29 + 33-36) with a section on `Reactor.DevtoolsSupport` and the
  two-layer activation model. The "Navigation serializes deep-link state via
  JsonSerializer" claim at line 35 is already known to be stale (issue #497
  second comment); delete it.
- `docs/guide/devtools.md` (if present) or the equivalent template under
  `docs/_pipeline/templates/` — update the "how to enable devtools" recipe
  from "pass `devtools: true`" to "add the `RuntimeHostConfigurationOption`."
- `samples/apps/minesweeper/` and other samples that call `Run(devtools: ...)`
  — update both Program.cs and csproj.
- `plugins/reactor/skills/reactor-devtools/*.md` — update the agent-kit
  guidance to reflect the csproj pattern.
- `CHANGELOG.md` (or release notes) — document the breaking parameter
  removal under "Breaking changes" with the one-line csproj migration.

After Phase 2, additionally:

- `docs/guide/extending-reactor-controls.md` — note that devtools-adjacent
  control hooks (if any) now live in the optional package.
- Top-level `README.md` — mention the optional Devtools package alongside
  the main package install line.

---

## §14 Open questions

**Q1. `RuntimeHostConfigurationOption` last-write-wins behavior.** Does an
override in the consumer csproj reliably win over the default in
`Reactor.targets`? The MSBuild semantics for `ItemGroup` are "items
accumulate by default unless `Remove`'d." `RuntimeHostConfigurationOption`
is consumed by the SDK targets that build the `.runtimeconfig.json`; the SDK
likely deduplicates by `Include` key with last-write-wins (since otherwise
the runtimeconfig would have duplicate keys, which is invalid), but this
needs verification on .NET 9 SDK before we commit to the simple
`<RuntimeHostConfigurationOption Include="..." Value="true" />` consumer
pattern. If verification fails, the consumer pattern becomes
`<RuntimeHostConfigurationOption Remove="Reactor.DevtoolsSupport" />`
followed by re-add, which we'd then need to document. *Spike before Phase 1
implementation; ~30 minutes.*

**Q2. Should `DevtoolsCliParser` move to the Devtools package in Phase 2?**
The parser is small (~few hundred lines), pure string handling, and needed
in core to emit the switch-off fallback message. Moving it means the
fallback message becomes "this binary doesn't have the Devtools package"
rather than "this binary has the switch off"; both are valid, the second
is more accurate when the package is referenced but the switch is off.
Decision: **keep in core** for Phase 2, since the dual-gate model (§6.5)
distinguishes the two cases and the parser's transitive cost is verified
zero. Reopen if the parser turns out to drag in unexpected types.

**Q3. Soft-deprecation transition for the `devtools:` parameter, or hard
delete in Phase 1?** Reactor is pre-1.0 so hard delete is in-policy. The
counter-argument is the parameter is mentioned by name in the agent-kit
skills and external blog content; a one-release `[Obsolete]` window costs
~50 lines and zero risk. Decision: **proposed hard delete** for the
parameter as part of Phase 1, with the `[Obsolete]` window available as a
fallback if review finds external consumers we should accommodate.

**Q4. Default switch state in `Reactor.AppTests.Host`'s csproj.** The host
runs both devtools-on and devtools-off scenarios. Default on (with selftests
that exercise switch-off paths via `AppContext.SetSwitch` overrides at
runtime) keeps the AOT test publish exercising the devtools-on shape; default
off (with switch-on overrides per-test) keeps the test host's binary size
honest. Decision: **default on**, because the test host is not a retail
target and the AOT-with-devtools shape needs end-to-end coverage. Selftests
that need switch-off behavior set `AppContext.SetSwitch("Reactor.DevtoolsSupport",
false)` in their setup. (Note: runtime overrides do *not* alter trimming —
they only alter the runtime property value. That's the desired behavior for
selftests.)

**Q5. Phase 2 versioning lockstep.** The new `Microsoft.UI.Reactor.Devtools`
nupkg must ship with the same version as `Microsoft.UI.Reactor` (the
contract is internal-shape-coupled). Mechanically this means both projects
share the same `Version` MSBuild property and are tagged together. Worth
documenting in `docs/specs/022-packaging-and-distribution.md` as a
follow-up; not a blocker for this spec.

**Q6. `Reactor.Devtools` as a separate `repo`, separate `csproj`, or
separate target inside the same csproj?** Separate csproj inside the same
repo. Separate repo defeats lockstep versioning. Separate target inside the
same csproj defeats the assembly-boundary isolation that's the whole point.
Decision: **separate csproj, same repo**, `<ProjectReference>` from
`Reactor.Devtools.csproj` → `Reactor.csproj` for the contract types.

---

## §15 Phasing

### Phase 1 — FeatureSwitch + parameter removal (1 PR, ~500 lines)

Scope:
- Add `ReactorFeatures.IsDevtoolsSupported` with both feature attributes.
- Gate the two `Run` overloads on the switch; delete the `devtools` /
  `preview` parameters, `ResolveDevtoolsParam`, the `_previewParamDeprecationWarned`
  field, and both `[UnconditionalSuppressMessage]` attributes.
- Annotate `PreviewCaptureServer`'s ctor with `[RequiresUnreferencedCode]`.
- Split `TryRunDevtools` so CLI parse is unguarded and dispatch is guarded;
  add the actionable switch-off fallback message.
- Ship `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
  Value="false" Trim="true" />` in `Reactor.targets` (added to the nupkg).
- Update all in-repo call sites that pass `devtools: true` (`Run` →
  csproj `RuntimeHostConfigurationOption`).
- Update `docs/aot-support.md` and relevant `*.md.dt` templates; recompile.
- Add unit tests (§12.1) and selftests (§12.2).
- Add `tools/Reactor.MstatVerifier/` and wire to CI (§11, §12.4).
- CHANGELOG entry under "Breaking changes."

Exit gate: `samples/apps/hello-world-aot` published with default config
drops to ≤ 12.5 MB; mstat verifier passes; full test suite green.

### Phase 2 — Package split (separate PR, larger)

Scope:
- Create `src/Reactor.Devtools/` csproj and project layout.
- Define `IReactorDevtoolsHost`, `ReactorDevtoolsBootRequest`,
  `ReactorDevtoolsBootstrap` in core (`src/Reactor/Hosting/Devtools/Contract/`).
- Move `Hosting/Devtools/*`, `PreviewCaptureServer`, the devtools subverb
  dispatch from `ReactorApp.cs` to the new package; implement
  `IReactorDevtoolsHost`.
- Add `[ModuleInitializer]` to the new package to self-register.
- Refactor `Factories.DevtoolsMenu` to the shim shape (§6.2).
- Add ILSpy / Cecil assertion to the mstat verifier: `Reactor.dll` itself
  contains no `Microsoft.UI.Reactor.Hosting.Devtools.*` and no
  `PreviewCaptureServer`.
- Update all consumers of the moved types (samples, tests, agent kit) to
  add the new `<PackageReference Include="Microsoft.UI.Reactor.Devtools" />`.
- Update `docs/specs/022-packaging-and-distribution.md` for the new package.
- CHANGELOG: "New optional package `Microsoft.UI.Reactor.Devtools`."

Exit gate: `Reactor.dll` ILSpy decomp shows no devtools IL; consumers that
omit the new package fail at compile time (not runtime) if they try to
construct devtools types directly; full test suite green.

### Out of scope for spec 051

- `Element.cs:845-851` Docking equality switch — tracked as the second
  half of #498. After this spec lands, that's the only remaining core→Docking
  edge that prevents the Docking namespace from trimming when unused.
- `LockfileRegistry`'s HTTP-vs-TCP probe refactor (issue #497 second
  comment) — orthogonal optimization that reduces *devtools-on* binary
  cost. May be folded into Phase 2 since `LockfileRegistry` moves there
  anyway; not a gate requirement.
- Source-gen `JsonSerializerContext` for `DevtoolsMcpServer.cs:350,418` —
  same: orthogonal optimization, may fold into Phase 2, not gate.
