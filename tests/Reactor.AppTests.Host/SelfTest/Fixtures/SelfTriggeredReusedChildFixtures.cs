using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// A panel child that is reference-equal across renders (a reused element instance, an
/// explicitly memoized wrapper, or a reused children array) must not swallow a state change
/// made by a component inside it.
///
/// <para>The setter marks that component's node self-triggered and asks the host for a pass.
/// The pass re-renders from the root, so it has to walk back down to the component. The
/// cheap child-skip arms in <c>ChildReconciler</c> (positional <c>UpdateCommonChild</c> and
/// the keyed prefix and suffix loops) skip any child for which
/// <c>Element.CanSkipUpdate</c> holds, and a reference-equal child always satisfies it. Before
/// the fix those arms never consulted the dirty-ancestor path, so the descent stopped at the
/// reused child and the component's new state never reached the screen. These fixtures click
/// the component's OWN button, so the root's state never changes and only the self-triggered
/// path can deliver the update.</para>
/// </summary>
internal static partial class SelfTriggeredReusedChildFixtures
{
    private sealed class Probe
    {
        public int Renders;
        public Action<int>? SetCount;
        public Action? OnRender;
    }

    private sealed record CounterProps(string Name, Probe Probe);

    /// <summary>Owns its state; only its own button (or the captured setter) changes it.</summary>
    private sealed class SelfCounter : Component<CounterProps>
    {
        public override Element Render()
        {
            Props.Probe.Renders++;
            var (count, setCount) = UseState(0);
            Props.Probe.SetCount = setCount;
            Props.Probe.OnRender?.Invoke();
            return VStack(
                TextBlock($"{Props.Name}:{count}"),
                Button($"{Props.Name}+", () => setCount(count + 1)));
        }
    }

    private static ComponentElement Counter(string name, Probe probe) =>
        Component<SelfCounter, CounterProps>(new CounterProps(name, probe));

    /// <summary>
    /// Clicks the counter's own button twice and checks the label follows. Two clicks, because
    /// a fix that only drained the first pending update (or a stale label that happened to match
    /// once) would still fail the second.
    /// </summary>
    private static async Task ClickTwiceAndCheck(Harness h, string prefix, string name, Probe probe)
    {
        h.Check($"{prefix}_Mount", h.FindText($"{name}:0") is not null && probe.Renders == 1,
            $"renders={probe.Renders}");

        h.ClickButton($"{name}+");
        await Harness.Render();
        h.Check($"{prefix}_FirstClickRendered", h.FindText($"{name}:1") is not null,
            $"renders={probe.Renders}");
        h.Check($"{prefix}_FirstClickStaleLabelGone", h.FindText($"{name}:0") is null);

        h.ClickButton($"{name}+");
        await Harness.Render();
        h.Check($"{prefix}_SecondClickRendered", h.FindText($"{name}:2") is not null,
            $"renders={probe.Renders}");
        h.Check($"{prefix}_RenderedOncePerClick", probe.Renders == 3, $"renders={probe.Renders}");
    }

    /// <summary>
    /// Positional arm: one component element instance, built once and re-emitted as a panel
    /// child on every render (the shape of an app that reuses a single DockManager element).
    /// A second counter rebuilt on every render never takes the skip arm, so it is the positive
    /// control: the same click-to-render path works in this tree, and only the reused child
    /// was stuck.
    /// </summary>
    internal sealed class PositionalReusedInstance(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();
            var freshProbe = new Probe();
            var stable = Counter("posInst", probe);

            var host = H.CreateHost();
            host.Mount(ctx => VStack(TextBlock("posInst-header"), stable, Counter("posFresh", freshProbe)));
            await Harness.Render();

            H.ClickButton("posFresh+");
            await Harness.Render();
            H.Check("SelfTrigReuse_PosInst_FreshControlRendered", H.FindText("posFresh:1") is not null,
                $"renders={freshProbe.Renders}");

            await ClickTwiceAndCheck(H, "SelfTrigReuse_PosInst", "posInst", probe);
        }
    }

    /// <summary>
    /// Positional arm: the residual edge the <c>UpdateCommonChild</c> comment documented —
    /// <c>UseMemo(() =&gt; Border(Counter()), [])</c> yields a reference-equal Border whose
    /// Counter self-triggers.
    /// </summary>
    internal sealed class PositionalMemoizedWrapper(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var memoized = ctx.UseMemo(() => (Element)Border(Counter("posMemo", probe)));
                return VStack(TextBlock("posMemo-header"), memoized);
            });
            await Harness.Render();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_PosMemo", "posMemo", probe);
        }
    }

    /// <summary>
    /// Positional arm, nested: a reused container whose children array is reused too, so the
    /// inner panel's reconcile also sees reference-equal children. The descent has to get past
    /// the outer skip, the inner skip, and a Border before it reaches the component.
    /// </summary>
    internal sealed class PositionalNestedStableTree(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();
            var stableTree = VStack(
                TextBlock("posNest-inner-label"),
                Border(Counter("posNest", probe)));

            var host = H.CreateHost();
            host.Mount(ctx => VStack(TextBlock("posNest-header"), stableTree));
            await Harness.Render();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_PosNest", "posNest", probe);
        }
    }

    /// <summary>
    /// Positional arm behind a <c>UseMemoCellsByIndex</c> hint. The structural fast path
    /// declines because the panel is on the dirty-ancestor path, and the full walk it falls
    /// back to reaches the component cell through <c>UpdateCommonChild</c>.
    /// </summary>
    internal sealed class PositionalHintedRange(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly int[] Items = { 0, 1, 2 };

        public override async Task RunAsync()
        {
            var probe = new Probe();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // changedIndices is always empty, so every cell is reused reference-equal.
                var cells = ctx.UseMemoCellsByIndex(
                    Items,
                    Array.Empty<int>(),
                    (item, i) => i == 1 ? Counter("posHint", probe) : TextBlock($"posHint-cell-{i}"));
                return VStack(TextBlock("posHint-header"), VStack(cells));
            });
            await Harness.Render();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_PosHint", "posHint", probe);
        }
    }

    /// <summary>Keyed prefix arm: the reused keyed child leads the list.</summary>
    internal sealed class KeyedPrefix(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();
            var stable = Counter("keyPre", probe).WithKey("keyPre");

            var host = H.CreateHost();
            host.Mount(ctx => VStack(stable, TextBlock("keyPre-tail").WithKey("keyPre-tail")));
            await Harness.Render();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_KeyPre", "keyPre", probe);
        }
    }

    /// <summary>
    /// Keyed suffix arm: the head's key changes on every render, so the prefix loop stops at
    /// index 0 and the reused keyed child at the end is reached by the suffix loop.
    /// </summary>
    internal sealed class KeyedSuffix(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();
            var stable = Counter("keySuf", probe).WithKey("keySuf");
            int pass = 0;

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBlock("keySuf-head").WithKey($"keySuf-head-{++pass}"),
                stable));
            await Harness.Render();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_KeySuf", "keySuf", probe);
            H.Check("SelfTrigReuse_KeySuf_PrefixBroke", pass >= 3, $"passes={pass}");
        }
    }

    /// <summary>
    /// Negative control: the fix must decline the skip only for the child that holds the
    /// pending component. A reused sibling that is not on the dirty path stays skipped, so its
    /// component does not re-render when its neighbour updates.
    /// </summary>
    internal sealed class SiblingStaysSkipped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probeA = new Probe();
            var probeB = new Probe();
            var stableA = Counter("sibA", probeA);
            var stableB = Counter("sibB", probeB);

            var host = H.CreateHost();
            host.Mount(ctx => VStack(stableA, stableB));
            await Harness.Render();

            H.Check("SelfTrigReuse_Sibling_Mount",
                H.FindText("sibA:0") is not null && H.FindText("sibB:0") is not null);

            H.ClickButton("sibA+");
            await Harness.Render();
            H.Check("SelfTrigReuse_Sibling_ClickedUpdated", H.FindText("sibA:1") is not null,
                $"rendersA={probeA.Renders}");
            H.Check("SelfTrigReuse_Sibling_OtherNotRerendered", probeB.Renders == 1,
                $"rendersB={probeB.Renders}");
            H.Check("SelfTrigReuse_Sibling_OtherUnchanged", H.FindText("sibB:0") is not null);

            H.ClickButton("sibB+");
            await Harness.Render();
            H.Check("SelfTrigReuse_Sibling_SecondUpdated", H.FindText("sibB:1") is not null,
                $"rendersB={probeB.Renders}");
            H.Check("SelfTrigReuse_Sibling_FirstNotRerendered", probeA.Renders == 2,
                $"rendersA={probeA.Renders}");
        }
    }

    /// <summary>
    /// The skip arms find a panel's dirty children with one <c>IndexOf</c> each, instead of
    /// reading every skipped sibling's control to ask whether it is on the dirty path. The
    /// resolution only exists while a pass runs, so it is captured from inside the
    /// self-triggered counter's own render. Updating two reused counters together puts two
    /// children of the same panel on the dirty path: both must render, and both must still be
    /// found by index rather than by the per-child fallback.
    /// </summary>
    internal sealed class DirtyChildrenFoundByIndex(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probeA = new Probe();
            var probeB = new Probe();
            var stableA = Counter("idxA", probeA);
            var stableB = Counter("idxB", probeB);

            var host = H.CreateHost();
            host.Mount(ctx => VStack(TextBlock("idx-header"), stableA, TextBlock("idx-middle"), stableB));
            await Harness.Render();

            var header = H.FindText("idx-header");
            var panel = header is null ? null : VisualTreeHelper.GetParent(header) as Panel;
            H.Check("SelfTrigReuse_Index_PanelFound", panel is not null && panel.Children.Count == 4);
            if (panel is null || probeA.SetCount is null || probeB.SetCount is null)
                return;

            string? resolved = null;
            probeA.OnRender = () => resolved = host.Reconciler
                .ResolveDirtyChildIndices(panel, new PanelChildCollection(panel)).ToString();

            probeA.SetCount(1);
            await Harness.Render();
            H.Check("SelfTrigReuse_Index_SingleRendered", H.FindText("idxA:1") is not null);
            H.Check("SelfTrigReuse_Index_SingleFoundByIndex", resolved == "At(1)", $"resolved={resolved}");

            resolved = null;
            probeA.SetCount(2);
            probeB.SetCount(1);
            await Harness.Render();
            H.Check("SelfTrigReuse_Index_BothRendered",
                H.FindText("idxA:2") is not null && H.FindText("idxB:1") is not null,
                $"rendersA={probeA.Renders} rendersB={probeB.Renders}");
            H.Check("SelfTrigReuse_Index_BothFoundByIndex", resolved == "AtAll(1,3)", $"resolved={resolved}");

            // Outside a pass there is no dirty path, so nothing resolves.
            H.Check("SelfTrigReuse_Index_NoneBetweenPasses",
                host.Reconciler.ResolveDirtyChildIndices(panel, new PanelChildCollection(panel)).IsEmpty);
        }
    }

    /// <summary>A control that keeps its children in an inner panel rather than in itself.</summary>
    private sealed partial class InnerPanelHost : UserControl
    {
        public InnerPanelHost() => Content = Inner;

        public StackPanel Inner { get; } = new();
    }

    private sealed record InnerPanelHostElement(Element[] Items) : Element;

    /// <summary>
    /// A descriptor whose <c>Panel&lt;&gt;</c> collection is the inner panel's, as a third-party
    /// control might declare it. The child reconciler is handed the outer control as the parent.
    /// </summary>
    private sealed class InnerPanelHostHandler : DescriptorHandler<InnerPanelHostElement, InnerPanelHost>
    {
        public InnerPanelHostHandler() : base(HostDescriptor) { }

        private static readonly ControlDescriptor<InnerPanelHostElement, InnerPanelHost> HostDescriptor = new()
        {
            Children = new Panel<InnerPanelHostElement, InnerPanelHost>(
                GetChildren: static e => e.Items,
                GetCollection: static c => c.Inner.Children),
        };
    }

    /// <summary>
    /// The fallback path. When the parent handed to the child reconciler does not own the
    /// collection, the dirty path continues from the parent through the inner panel, which is not
    /// a member of the collection, so no index can be resolved. The reconciler then tests each
    /// skip-eligible child's control against the dirty path instead, and the reused counter must
    /// still update. The resolution is captured mid-pass to prove this fixture takes the fallback
    /// rather than the index path.
    /// </summary>
    internal sealed class InnerPanelCollectionUsesFallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ControlRegistry.Register<InnerPanelHostElement, InnerPanelHost>(static () => new InnerPanelHostHandler());

            var probe = new Probe();
            var stable = Counter("innerHost", probe);

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBlock("innerHost-header"),
                new InnerPanelHostElement(new Element[] { TextBlock("innerHost-label"), stable })));
            await Harness.Render();

            var hostControl = H.FindControl<InnerPanelHost>(_ => true);
            H.Check("SelfTrigReuse_InnerHost_Mounted",
                hostControl is not null && hostControl.Inner.Children.Count == 2,
                $"children={hostControl?.Inner.Children.Count}");
            if (hostControl is null)
                return;

            string? resolved = null;
            probe.OnRender = () => resolved = host.Reconciler
                .ResolveDirtyChildIndices(hostControl, new PanelChildCollection(hostControl.Inner.Children))
                .ToString();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_InnerHost", "innerHost", probe);
            H.Check("SelfTrigReuse_InnerHost_ResolvedByFallback", resolved == "ProbeEachChild",
                $"resolved={resolved}");
        }
    }

    /// <summary>
    /// <c>Memo(key, () =&gt; …)</c> used directly as a panel child. An unchanged key keeps the
    /// mounted subtree without re-running the factory, but a component inside it can still
    /// update its own state, and that update must render. A pass that does not involve the
    /// subtree must still leave it alone.
    /// </summary>
    internal sealed class KeyedMemoChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();
            int factoryCalls = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"keyMemo-tick:{tick}"),
                    Button("keyMemo-tick+", () => setTick(tick + 1)),
                    Memo("keyMemo", () =>
                    {
                        factoryCalls++;
                        return Border(Counter("keyMemo", probe));
                    }));
            });
            await Harness.Render();

            await ClickTwiceAndCheck(H, "SelfTrigReuse_KeyMemo", "keyMemo", probe);

            int callsBefore = factoryCalls;
            H.ClickButton("keyMemo-tick+");
            await Harness.Render();
            H.Check("SelfTrigReuse_KeyMemo_ParentRerendered", H.FindText("keyMemo-tick:1") is not null);
            H.Check("SelfTrigReuse_KeyMemo_UnrelatedPassSkipsFactory", factoryCalls == callsBefore,
                $"factoryCalls={factoryCalls} before={callsBefore}");
            H.Check("SelfTrigReuse_KeyMemo_UnrelatedPassSkipsComponent", probe.Renders == 3,
                $"renders={probe.Renders}");
            H.Check("SelfTrigReuse_KeyMemo_StateKept", H.FindText("keyMemo:2") is not null);
        }
    }

    /// <summary>
    /// The case found in an app: one <see cref="DockManager"/> element instance reused as a
    /// panel child. A model operation queues a mutation and asks the docking component to
    /// re-render itself; the drain only happens if the pass gets past the reused element.
    /// </summary>
    internal sealed class DockManagerReusedPanelChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var solution = new ToolWindow
            {
                Title = "Reuse Solution",
                Key = "reuse:solution",
                Content = TextBlock("reuse-body-solution"),
            };
            var output = new ToolWindow
            {
                Title = "Reuse Output",
                Key = "reuse:output",
                Content = TextBlock("reuse-body-output"),
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { solution, output }),
            };

            host.Mount(_ => VStack(TextBlock("dockReuse-header"), managerEl));
            await Harness.Render();

            var model = DockHostModelBridge.Get(managerEl);
            H.Check("SelfTrigReuse_Dock_ModelResolved", model is not null);
            if (model is null)
                return;

            model.PinToSide(output, DockSide.Right);
            bool drained = await Harness.WaitFor(() =>
                model.Pending.Count == 0 && H.FindButton("Reuse Output") is not null);
            H.Check("SelfTrigReuse_Dock_PinDrained", drained,
                $"pending={model.Pending.Count} sideButton={H.FindButton("Reuse Output") is not null}");

            model.Hide(solution);
            bool hidden = await Harness.WaitFor(() => model.Pending.Count == 0);
            H.Check("SelfTrigReuse_Dock_SecondOpDrained", hidden, $"pending={model.Pending.Count}");

            host.Mount(_ => TextBlock("dockReuse-done"));
            await Harness.Render();
        }
    }
}
