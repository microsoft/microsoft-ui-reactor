using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Reactor.Tests.Shared;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// End to end for the <c>Microsoft-UI-Reactor-Perf</c> spans: a live host renders while a
/// <b>non-elevated private ETW session</b> scoped to this process — <c>winapp perf</c>'s attach
/// mode — records the provider, and the ETL is decoded through TDH. Proves the spans a real
/// render produces arrive, pair up, nest, and carry the ids and counts the schema promises:
/// the pass, the root and child components (mount and update), the setState owner, the keyed
/// child edits, and rows a virtualized list realizes during layout, outside any pass.
///
/// <para>A selftest because the spans come from the reconciler and hosts driving real WinUI
/// controls; the headless unit tests cover the provider and its wire schema.</para>
/// </summary>
internal class PerfTrace_PrivateSessionRecordsRenderSpans(Harness h) : SelfTestFixtureBase(h)
{
    private static readonly string[] Checks =
    [
        "PerfTrace_Captured",
        "PerfTrace_SpansBalanced",
        "PerfTrace_PassPerRender",
        "PerfTrace_RootMountedInsidePass",
        "PerfTrace_ChildMountReason",
        "PerfTrace_StateChangeOwner",
        "PerfTrace_ChildUpdateAfterState",
        "PerfTrace_ComponentInsidePassAndReconcile",
        "PerfTrace_RootEffectsSpan",
        "PerfTrace_KeyedEditCounts",
        "PerfTrace_RowsRealizedOutsidePass",
        "PerfTrace_KeyedListDiagnostic",
        "PerfTrace_PropsAndContextReasons",
        "PerfTrace_ThrowingRenderClosesItsSpan",
        "PerfTrace_ReplacedCount",
    ];

    public override async Task RunAsync()
    {
        var run = await PerfTraceScenario.RunAsync(H, level: 5, keywords: 0xF);
        if (run is null)
        {
            foreach (var check in Checks)
                H.Skip(check, PerfTraceScenario.SkipReason);
            return;
        }
        var events = run.Value.Events;
        Console.WriteLine($"# perf trace: {events.Count} events, decode errors={events.Count(e => e.DecodeError is not null)}");
        H.Check("PerfTrace_Captured", events.Count > 0 && events.All(e => e.DecodeError is null && e.IsTraceLogging));

        var spans = Pair(events, out bool balanced);
        Console.WriteLine($"# perf trace: {spans.Count} spans; " + string.Join(", ",
            spans.GroupBy(s => s.Name).Select(g => $"{g.Key}={g.Count()}")));
        // A Stop's componentId is its Start's.
        H.Check("PerfTrace_SpansBalanced", balanced && spans
            .Where(s => s.Name is "ComponentRender" or "Effects")
            .All(s => Equals(s.Start.Fields["componentId"], s.Stop.Fields["componentId"])));

        var passes = spans.Where(s => s.Name == "RenderPass").ToList();
        // Mount, the counter's own update and the shuffle each ran a pass (Harness.Render may
        // flush more, never fewer); every pass names this host.
        H.Check("PerfTrace_PassPerRender",
            passes.Count >= 3 && passes.Select(p => p.Start.Fields["hostId"]).Distinct().Count() == 1);

        var components = spans.Where(s => s.Name == "ComponentRender").ToList();
        var root = components.FirstOrDefault(c => (string)c.Start.Fields["name"] == "FuncElement");
        H.Check("PerfTrace_RootMountedInsidePass",
            root is not null && (string)root.Start.Fields["reason"] == "mount" && root.Parent?.Name == "RenderPass");

        var counter = components.Where(c => (string)c.Start.Fields["name"] == nameof(PerfTraceCounter)).ToList();
        Console.WriteLine("# perf trace: counter renders " + string.Join(", ",
            counter.Select(c => $"{c.Start.Fields["componentId"]}:{c.Start.Fields["reason"]}")));
        ulong counterId = counter.Count > 0 ? (ulong)counter[0].Start.Fields["componentId"] : 0;
        H.Check("PerfTrace_ChildMountReason",
            counter.Count > 0 && counterId != 0 && (string)counter[0].Start.Fields["reason"] == "mount");

        // The click's setState names the counter (the "updater"), and the counter's next
        // render reports "state" under the same id.
        var stateChange = events.FirstOrDefault(e => e.Name == "StateChange" && Equals(e.Fields["componentId"], counterId));
        H.Check("PerfTrace_StateChangeOwner",
            stateChange is not null && (string)stateChange.Fields["hook"] == "UseState" && (bool)stateChange.Fields["changed"]);
        var update = counter.FirstOrDefault(c => stateChange is not null && c.Start.Timestamp > stateChange.Timestamp);
        H.Check("PerfTrace_ChildUpdateAfterState",
            update is not null && (ulong)update.Start.Fields["componentId"] == counterId
            && (string)update.Start.Fields["reason"] == "state");

        // Inclusive nesting: pass > root component > reconcile > child component.
        H.Check("PerfTrace_ComponentInsidePassAndReconcile",
            update is not null && update.Ancestors().Any(a => a.Name == "Reconcile")
            && update.Ancestors().Any(a => a.Name == "RenderPass"));

        H.Check("PerfTrace_RootEffectsSpan",
            spans.Any(s => s.Name == "Effects" && s.Parent?.Name == "RenderPass"
                && root is not null && Equals(s.Start.Fields["componentId"], root.Start.Fields["componentId"])));

        // [a, b, c] -> [c, a, d]: b removed, d inserted, and one of a / c moved.
        var shuffle = spans.FirstOrDefault(s => s.Name == "ChildReconcile"
            && Equals(s.Start.Fields["oldCount"], 3u) && Equals(s.Start.Fields["newCount"], 3u)
            && !Equals(s.Stop.Fields["inserted"], 0u));
        Console.WriteLine(shuffle is null ? "# perf trace: no keyed shuffle span" :
            $"# perf trace: shuffle inserted={shuffle.Stop.Fields["inserted"]} removed={shuffle.Stop.Fields["removed"]} moved={shuffle.Stop.Fields["moved"]} replaced={shuffle.Stop.Fields["replaced"]}");
        H.Check("PerfTrace_KeyedEditCounts",
            shuffle is not null && Equals(shuffle.Stop.Fields["inserted"], 1u) && Equals(shuffle.Stop.Fields["removed"], 1u)
            && Equals(shuffle.Stop.Fields["moved"], 1u) && Equals(shuffle.Stop.Fields["replaced"], 0u));

        // Rows the repeater realizes during layout run outside any pass, yet each still gets a
        // mount span inside its own outermost Reconcile span.
        var rows = components.Where(c => (string)c.Start.Fields["name"] == nameof(PerfTraceRow)).ToList();
        var outside = rows.Where(r => r.Ancestors().All(a => a.Name != "RenderPass")).ToList();
        var rowEntries = outside.Select(r => r.Ancestors().LastOrDefault(a => a.Name == "Reconcile"))
            .Select(e => e is null ? "none" : $"{e.Start.Fields["entry"]}/{e.Start.Fields["element"]}").Distinct().ToList();
        Console.WriteLine($"# perf trace: rows={rows.Count} realized outside a pass={outside.Count} entries={string.Join(",", rowEntries)}");
        H.Check("PerfTrace_RowsRealizedOutsidePass",
            outside.Count > 0 && outside.All(r => (string)r.Start.Fields["reason"] == "mount")
            && rowEntries.SequenceEqual(["mount/ComponentElement<String>"]));

        // Duplicate sibling keys are reported (not a bailout: the panel still reconciles).
        H.Check("PerfTrace_KeyedListDiagnostic",
            events.Any(e => e.Name == "KeyedListDiagnostic" && (string)e.Fields["kind"] == "duplicateKey"
                && !(bool)e.Fields["bailedOut"]));

        // The EventSource is off here, so these reasons come from the perf spans' own
        // classification (the context one from the FuncElement refinement it also enables).
        string[] Reasons(string name) => components.Where(c => (string)c.Start.Fields["name"] == name)
            .Select(c => (string)c.Start.Fields["reason"]).ToArray();
        var propsReasons = Reasons(nameof(PerfTracePropsChild));
        var funcReasons = Reasons(nameof(FuncElement));
        Console.WriteLine($"# perf trace: props child={string.Join(",", propsReasons)} func={string.Join(",", funcReasons)}"
            + $" eventSourceOn={ComponentRenderTrace.IsEnabled}");
        H.Check("PerfTrace_PropsAndContextReasons",
            !ComponentRenderTrace.IsEnabled && propsReasons.Contains("props") && funcReasons.Contains("context"));

        // The child that throws inside an error boundary still closes its span (Pair drops a
        // span whose Stop never came, and SpansBalanced checks it closed under the same id).
        var thrower = components.Where(c => (string)c.Start.Fields["name"] == nameof(PerfTraceThrower)).ToList();
        Console.WriteLine("# perf trace: thrower renders " + string.Join(",", thrower.Select(c => c.Start.Fields["reason"])));
        H.Check("PerfTrace_ThrowingRenderClosesItsSpan",
            thrower.Any(c => (string)c.Start.Fields["reason"] == "props") && balanced);

        // TextBlock -> Button in the same slot replaces that control in place.
        H.Check("PerfTrace_ReplacedCount",
            spans.Any(s => s.Name == "ChildReconcile" && Equals(s.Start.Fields["oldCount"], 1u)
                && Equals(s.Start.Fields["newCount"], 1u) && Equals(s.Stop.Fields["replaced"], 1u)));
    }

    /// <summary>A span reconstructed from a Start/Stop pair on one thread.</summary>
    private sealed class Span(CapturedEtwEvent start, Span? parent)
    {
        public CapturedEtwEvent Start { get; } = start;
        public CapturedEtwEvent Stop { get; set; } = null!;
        public Span? Parent { get; } = parent;
        public string Name => Start.Name!;

        public IEnumerable<Span> Ancestors()
        {
            for (var p = Parent; p is not null; p = p.Parent) yield return p;
        }
    }

    // Per-thread stack pairing — how winapp nests scopes across providers. Balanced when every
    // Stop closes the innermost open span of the same name and nothing is left open.
    private static List<Span> Pair(List<CapturedEtwEvent> events, out bool balanced)
    {
        balanced = true;
        var spans = new List<Span>();
        var stacks = new Dictionary<int, Stack<Span>>();
        foreach (var e in events.OrderBy(e => e.Timestamp))
        {
            if (e.Opcode is not (1 or 2)) continue;
            if (!stacks.TryGetValue(e.ThreadId, out var stack))
                stacks[e.ThreadId] = stack = new Stack<Span>();
            if (e.Opcode == 1)
            {
                var span = new Span(e, stack.Count > 0 ? stack.Peek() : null);
                stack.Push(span);
                spans.Add(span);
            }
            else if (stack.Count > 0 && stack.Peek().Name == e.Name)
            {
                stack.Pop().Stop = e;
            }
            else
            {
                Console.WriteLine($"# perf trace: unmatched stop {e.Name} (open: {(stack.Count > 0 ? stack.Peek().Name : "none")})");
                balanced = false;
            }
        }
        if (stacks.Values.Any(s => s.Count > 0))
        {
            Console.WriteLine("# perf trace: left open: " + string.Join(", ", stacks.Values.SelectMany(s => s).Select(s => s.Name)));
            balanced = false;
        }
        spans.RemoveAll(s => s.Stop is null);
        return spans;
    }
}

/// <summary>
/// The emit sites' enable checks: a session that asks for some keywords must cost the app
/// only those. Each round records a full render scenario under a keyword subset and compares
/// what the provider handed ETW with what the session recorded — a write the session did not
/// ask for is dropped by ETW, so it shows up only as a surplus here. The rounds
/// (<see cref="PerfTraceScenario.GateRounds"/>) turn each keyword on alone, so an emit site
/// checking the wrong keyword or level is caught, not just one with no check at all.
/// </summary>
internal class PerfTrace_UnrequestedKeywordsWriteNothing(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        foreach (var (label, level, keywords) in PerfTraceScenario.GateRounds)
        {
            var run = await PerfTraceScenario.RunAsync(H, level, keywords);
            if (run is null)
            {
                H.Skip($"PerfTrace_OnlyRequestedWrites_{label}", PerfTraceScenario.SkipReason);
                continue;
            }
            var (events, written) = run.Value;
            var keywordsSeen = events.Select(e => e.Keyword).Distinct().ToList();
            Console.WriteLine($"# perf gate {label}: written={written} recorded={events.Count} keywords=" +
                string.Join("|", keywordsSeen.Select(k => "0x" + k.ToString("X"))));
            H.Check($"PerfTrace_OnlyRequestedWrites_{label}",
                events.Count > 0 && written == events.Count && keywordsSeen.All(k => (k & keywords) != 0));
        }
    }
}

/// <summary>
/// The selftests' recording harness: runs a render scenario under a private, PID-scoped
/// session (winapp's attach mode) and returns what was recorded and what was written.
/// </summary>
internal static class PerfTraceScenario
{
    /// <summary>
    /// The keyword subsets the gating fixtures record under: each keyword alone (so an emit
    /// site checking the wrong keyword writes in a round that did not ask for it), every
    /// keyword but Pass, and everything at level 4 (so a Verbose site checking an
    /// Informational gate shows up too).
    /// </summary>
    public static readonly (string Label, byte Level, ulong Keywords)[] GateRounds =
    [
        ("Pass", 5, ReactorPerfTrace.Keywords.Pass),
        ("Component", 5, ReactorPerfTrace.Keywords.Component),
        ("Children", 5, ReactorPerfTrace.Keywords.Children),
        ("State", 5, ReactorPerfTrace.Keywords.State),
        ("NotPass", 5, ReactorPerfTrace.Keywords.Component | ReactorPerfTrace.Keywords.Children | ReactorPerfTrace.Keywords.State),
        ("AllLevel4", 4, 0xF),
    ];

    public const string SkipReason = "this account may not start an ETW session (not elevated, not in Performance Log Users)";

    private static readonly string[] Rows = Enumerable.Range(0, 200).Select(i => "row" + i).ToArray();

    /// <summary>
    /// A <c>ReactorHost</c> scenario: mount, a child component's own setState, a keyed reorder,
    /// duplicate sibling keys, and rows a virtualized list realizes during layout.
    /// </summary>
    public static Task<(List<CapturedEtwEvent> Events, long Written)?> RunAsync(Harness h, byte level, ulong keywords)
    {
        Microsoft.UI.Reactor.Hosting.ReactorHost? host = null;
        return CaptureAsync(h, level, keywords,
            async () =>
            {
                host = h.CreateHost();
                host.Mount(ctx =>
                {
                    var (keys, setKeys) = ctx.UseState(new[] { "a", "b", "c" });
                    bool shuffled = keys[0] == "c";
                    return VStack(
                        Button("perf-shuffle", () => setKeys(["c", "a", "d"])),
                        Component<PerfTraceCounter>(),
                        VStack(keys.Select(k => (Element)TextBlock("key " + k).WithKey(k)).ToArray()),
                        VStack(TextBlock("dup 1").WithKey("dup"), TextBlock("dup 2").WithKey("dup")),
                        LazyVStack(Rows, static r => r, static (r, _) => Component<PerfTraceRow, string>(r)).Height(120),
                        // The shuffle also changes a child's props, a consumed context, a slot's
                        // control type, and makes a child throw inside an error boundary.
                        Component<PerfTracePropsChild, string>(keys[0]),
                        RenderEachTime(c => TextBlock("theme " + c.UseContext(PerfTracePropsChild.Theme))),
                        VStack(shuffled ? Button("perf-swapped") : TextBlock("perf swap me")),
                        ErrorBoundary(Component<PerfTraceThrower, bool>(shuffled), TextBlock("perf caught")))
                        .Provide(PerfTracePropsChild.Theme, keys[0]);
                });
                await Harness.Render();
                await Harness.Render();

                h.ClickButton("perf-inc");
                await Harness.Render();
                h.ClickButton("perf-shuffle");
                await Harness.Render();
                await Harness.Render();
            },
            () => host?.Dispose());
    }

    /// <summary>A <c>ReactorHostControl</c> scenario: mount, then a root setState.</summary>
    public static Task<(List<CapturedEtwEvent> Events, long Written)?> RunHostControlAsync(Harness h, byte level, ulong keywords)
    {
        Microsoft.UI.Reactor.Hosting.ReactorHostControl? control = null;
        return CaptureAsync(h, level, keywords,
            async () =>
            {
                control = new Microsoft.UI.Reactor.Hosting.ReactorHostControl();
                var root = new PerfTraceControlRoot();
                control.Mount(root);
                h.SetContent(new Microsoft.UI.Xaml.Controls.Border { Child = control });
                // A standalone control is not the active host, so Harness.Render() cannot await it.
                await Harness.WaitFor(() => root.Bump is not null, maxPasses: 32, perPassMs: 10);
                await control.WaitForIdleAsync();
                root.Bump?.Invoke();
                await Harness.WaitFor(() => root.Renders >= 2, maxPasses: 32, perPassMs: 10);
                await control.WaitForIdleAsync();
            },
            () => control?.Dispose());
    }

    private static async Task<(List<CapturedEtwEvent> Events, long Written)?> CaptureAsync(
        Harness h, byte level, ulong keywords, Func<Task> drive, Action dispose)
    {
        string etl = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(),
            "reactor-perf-selftest-" + Guid.NewGuid().ToString("N") + ".etl");
        EtwPrivateCapture capture;
        try
        {
            capture = EtwPrivateCapture.Start(etl);
        }
        catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return null;
        }

        try
        {
            long written;
            using (capture)
            {
                capture.Enable(ReactorPerfTrace.ProviderId, level, keywords);
                // Read on this (UI) thread right after the enable and right before the stop, with
                // no await in between either pair, so nothing else writes inside the window.
                long before = ReactorPerfTrace.WriteCountForTests;
                await drive();
                written = ReactorPerfTrace.WriteCountForTests - before;
                capture.Stop();
            }
            var events = EtwPrivateCapture.Read(etl, ReactorPerfTrace.ProviderId)
                .Where(e => e.ProcessId == Environment.ProcessId)
                .ToList();
            return (events, written);
        }
        finally
        {
            dispose();
            h.SetContent(null);
            try { global::System.IO.File.Delete(etl); } catch (global::System.IO.IOException) { }
        }
    }
}

/// <summary>
/// The other host: a <c>ReactorHostControl</c>'s passes, root span and root effects, and the
/// same keyword gating (its render loop is a separate copy of <c>ReactorHost</c>'s).
/// </summary>
internal class PerfTrace_HostControlSpansAndGating(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var run = await PerfTraceScenario.RunHostControlAsync(H, level: 5, keywords: 0xF);
        if (run is null)
        {
            foreach (var check in PerfTraceScenario.GateRounds.Select(r => "PerfTrace_HostControl_OnlyRequestedWrites_" + r.Label).Prepend("PerfTrace_HostControl_RootSpans"))
                H.Skip(check, PerfTraceScenario.SkipReason);
            return;
        }
        var events = run.Value.Events;
        string Name(CapturedEtwEvent e) => e.Fields.TryGetValue("name", out var n) ? (string)n : "";
        var rootStarts = events.Where(e => e.Name == "ComponentRender" && e.Opcode == 1 && Name(e) == nameof(PerfTraceControlRoot)).ToList();
        var passStarts = events.Where(e => e.Name == "RenderPass" && e.Opcode == 1).ToList();
        var effects = events.Where(e => e.Name == "Effects" && e.Opcode == 1).ToList();
        Console.WriteLine($"# perf host control: root renders=" + string.Join(",", rootStarts.Select(e => e.Fields["reason"]))
            + $" passes={passStarts.Count} effects={effects.Count}");
        H.Check("PerfTrace_HostControl_RootSpans",
            rootStarts.Select(e => (string)e.Fields["reason"]).SequenceEqual(["mount", "state"])
            && rootStarts.Select(e => e.Fields["componentId"]).Distinct().Count() == 1
            && passStarts.Count >= 2
            && effects.Any(e => Equals(e.Fields["componentId"], rootStarts[0].Fields["componentId"])));

        foreach (var (label, level, keywords) in PerfTraceScenario.GateRounds)
        {
            var gated = await PerfTraceScenario.RunHostControlAsync(H, level, keywords);
            var (recorded, written) = gated!.Value;
            Console.WriteLine($"# perf host control gate {label}: written={written} recorded={recorded.Count}");
            H.Check($"PerfTrace_HostControl_OnlyRequestedWrites_{label}",
                recorded.Count > 0 && written == recorded.Count && recorded.All(e => (e.Keyword & keywords) != 0));
        }
    }
}

internal sealed class PerfTraceControlRoot : Component
{
    public Action? Bump;
    public int Renders;

    public override Element Render()
    {
        Renders++;
        var (n, setN) = UseState(0);
        Bump = () => setN(n + 1);
        return VStack(TextBlock($"control {n}"), Component<PerfTraceRow, string>("control row"));
    }
}

internal sealed class PerfTraceCounter : Component
{
    public override Element Render()
    {
        var (n, setN) = UseState(0);
        return VStack(TextBlock($"perf count {n}"), Button("perf-inc", () => setN(n + 1)));
    }
}

internal sealed class PerfTraceRow : Component<string>
{
    public override Element Render() => TextBlock(Props).Height(24);
}

internal sealed class PerfTracePropsChild : Component<string>
{
    public static readonly Context<string> Theme = new("");

    public override Element Render() => TextBlock("props " + Props);
}

internal sealed class PerfTraceThrower : Component<bool>
{
    public override Element Render()
        => Props ? throw new InvalidOperationException("PerfTrace selftest: deliberate render failure") : TextBlock("perf thrower");
}
