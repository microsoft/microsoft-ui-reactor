using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// The live half of <see cref="ReactorDiagnostics"/> component inspection: describing the
/// component at a real wrapper / host root <see cref="UIElement"/>, editing its state from text,
/// re-rendering it, and reading the modifier-applied properties of a tagged control. Everything
/// these calls delegate to is pinned headless in <c>ReactorDiagnosticsComponentsTests</c>.
/// </summary>
internal static class ComponentInspectionFixtures
{
    private const string SkipReason =
        "assembly built without REACTOR_SOURCEMAP (Release) - a plain leaf is not tagged, so it has no element to read modifiers from";

    private sealed record CounterProps(int Step, string AdminPassword);

    private sealed class Counter : Component<CounterProps>
    {
        public override Element Render()
        {
            var (n, _) = UseState(0);
            return TextBlock($"inspect-count:{n}").Width(120).AutomationName("inspect-counter");
        }
    }

    internal class Inspect(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    Component<Counter, CounterProps>(new CounterProps(2, "hunter2")),
                    Memo(c =>
                    {
                        var (s, _) = c.UseState("m0");
                        return TextBlock($"inspect-memo:{s}");
                    })));
                await Harness.Render();

                var leaf = H.FindControl<TextBlock>(t => t.Text == "inspect-count:0");
                var memoLeaf = H.FindControl<TextBlock>(t => t.Text == "inspect-memo:m0");
                H.Check("CompInspect_Mounted", leaf is not null && memoLeaf is not null);
                if (leaf is null || memoLeaf is null) return;

                // ── Class component, described from its wrapper ──
                var wrapper = VisualTreeHelper.GetParent(leaf) as UIElement;
                H.Check("CompInspect_WrapperFound", wrapper is Border);
                if (wrapper is null) return;

                H.Check("CompInspect_LeafIsNotAComponent", ReactorDiagnostics.DescribeComponent(leaf) is null);
                var counter = ReactorDiagnostics.DescribeComponent(wrapper);
                H.Check("CompInspect_ClassDescribed",
                    counter is { Name: "Counter", Kind: "class", IsRoot: false });
                H.Check("CompInspect_ClassProps",
                    counter is not null
                    && counter.Props.Count == 2
                    && counter.Props[0] is { Name: "Step", Type: "int", Value: "2", Redacted: false }
                    && counter.Props[1] is { Name: "AdminPassword", Value: "<redacted>", Redacted: true });
                H.Check("CompInspect_ClassState",
                    counter is not null && counter.State is [{ Index: 0, Kind: "state", Type: "int", Value: "0", Editable: true }]);

                // ── State edit from text re-renders exactly like the setter ──
                H.Check("CompInspect_SetStateAccepted", ReactorDiagnostics.TrySetState(wrapper, 0, "5", out var setError) && setError is null);
                await Harness.Render();
                H.Check("CompInspect_SetStateRerendered", H.FindControl<TextBlock>(t => t.Text == "inspect-count:5") is not null);
                H.Check("CompInspect_SetStateRefusesBadText",
                    !ReactorDiagnostics.TrySetState(wrapper, 0, "five", out var badError) && badError == "'five' is not a valid int");

                // ── Memo component ──
                var memoWrapper = VisualTreeHelper.GetParent(memoLeaf) as UIElement;
                var memo = memoWrapper is null ? null : ReactorDiagnostics.DescribeComponent(memoWrapper);
                H.Check("CompInspect_MemoDescribed",
                    memo is { Kind: "memo", State: [{ Kind: "state", Value: "\"m0\"" }] } && memo.Name.StartsWith("Memo in ", StringComparison.Ordinal));

                // ── Host root (a render function), and an explicit re-render ──
                var rootControl = host.CurrentControl;
                var root = rootControl is null ? null : ReactorDiagnostics.DescribeComponent(rootControl);
                H.Check("CompInspect_RootDescribed", root is { IsRoot: true, Kind: "function" } && root.Name.StartsWith("render in ", StringComparison.Ordinal));
                H.Check("CompInspect_RootRerender", rootControl is not null && ReactorDiagnostics.Rerender(rootControl));
                await Harness.Render();

                // ── Modifier-applied properties on a tagged leaf ──
                var live = H.FindControl<TextBlock>(t => t.Text == "inspect-count:5");
#if REACTOR_SOURCEMAP
                var applied = live is null ? null : ReactorDiagnostics.GetAppliedProperties(live);
                H.Check("CompInspect_AppliedProperties",
                    applied is not null
                    && applied.Contains(new AppliedProperty("Width", "FrameworkElement.Width", "120"))
                    && applied.Contains(new AppliedProperty("AutomationName", "AutomationProperties.Name", "inspect-counter")));
                // The explicit name suppresses the caption-derived default…
                H.Check("CompInspect_ExplicitNameHasNoDefault",
                    applied is not null && !applied.Any(p => p.Modifier == "DefaultAutomationName"));
                // …while an unnamed captioned leaf reports the default Reactor wrote for it.
                var memoApplied = ReactorDiagnostics.GetAppliedProperties(memoLeaf);
                H.Check("CompInspect_DefaultAutomationNameReported",
                    memoApplied.Contains(new AppliedProperty("DefaultAutomationName", "AutomationProperties.Name", "inspect-memo:m0")));
#else
                _ = live;
                H.Skip("CompInspect_AppliedProperties", SkipReason);
                H.Skip("CompInspect_ExplicitNameHasNoDefault", SkipReason);
                H.Skip("CompInspect_DefaultAutomationNameReported", SkipReason);
#endif

                // ── An unmounted component is no longer described or written ──
                host.Mount(ctx => TextBlock("inspect-gone"));
                await Harness.Render();
                H.Check("CompInspect_UnmountedNotDescribed", ReactorDiagnostics.DescribeComponent(wrapper) is null);
                H.Check("CompInspect_UnmountedRefusesWrites",
                    !ReactorDiagnostics.TrySetState(wrapper, 0, "6", out var goneError) && goneError is not null
                    && !ReactorDiagnostics.Rerender(wrapper));
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    private sealed class RootCounter : Microsoft.UI.Reactor.Core.Component
    {
        public override Element Render()
        {
            var (n, _) = UseState(7);
            return TextBlock($"root-anchor:{n}");
        }
    }

    /// <summary>
    /// Every element a host root is reachable from resolves to the root component: the host's
    /// rendered control, its <c>ContentTarget</c>, the dev-overlay wrapper the host installs in
    /// that target while an overlay is on, and a <c>ReactorHostControl</c> and its content.
    /// </summary>
    internal class RootAnchors(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new RootCounter());
            await Harness.Render();

            var target = host.ContentTarget;
            H.Check("RootAnchor_HostHasContentTarget", target is not null && host.CurrentControl is not null);
            if (target is null || host.CurrentControl is null) return;

            static bool IsRoot(ComponentSnapshot? s) =>
                s is { IsRoot: true, Kind: "class", Name: "RootCounter", State: [{ Value: "7" }] };

            H.Check("RootAnchor_RenderedControl", IsRoot(ReactorDiagnostics.DescribeComponent(host.CurrentControl)));
            H.Check("RootAnchor_ContentTarget", IsRoot(ReactorDiagnostics.DescribeComponent(target)));

            // With an overlay on, ContentTarget holds a wrapper around the rendered root.
            var prevOverlay = ReactorFeatureFlags.HighlightReconcileChanges;
            ReactorFeatureFlags.HighlightReconcileChanges = true;
            try
            {
                H.Check("RootAnchor_RerenderForOverlay", ReactorDiagnostics.Rerender(target));
                await Harness.Render();
                var wrapper = target.Child;
                H.Check("RootAnchor_OverlayWrapperInstalled", wrapper is not null && !ReferenceEquals(wrapper, host.CurrentControl));
                H.Check("RootAnchor_OverlayWrapper", wrapper is not null && IsRoot(ReactorDiagnostics.DescribeComponent(wrapper)));
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prevOverlay;
                ReactorDiagnostics.Rerender(target);
                await Harness.Render();
            }

            // A host without a ContentTarget installs its root as the window content.
            var window = H.Window;
            var previousContent = window.Content;
            var bare = new ReactorHost(window);
            try
            {
                bare.Mount(new RootCounter());
                await Harness.WaitFor(() => bare.CurrentControl is not null && ReferenceEquals(window.Content, bare.CurrentControl),
                    maxPasses: 16, perPassMs: 10);
                H.Check("RootAnchor_WindowContent",
                    window.Content is UIElement windowContent && IsRoot(ReactorDiagnostics.DescribeComponent(windowContent)));
            }
            finally
            {
                bare.Dispose();
                window.Content = previousContent;
                await Harness.Render();
            }

            // ReactorHostControl: the control itself and its content are anchors.
            var hostControl = new ReactorHostControl();
            hostControl.Mount(ctx =>
            {
                var (s, _) = ctx.UseState("hc");
                return TextBlock($"host-control:{s}");
            });
            H.SetContent(new Border { Child = hostControl });
            try
            {
                await Harness.WaitFor(() => hostControl.Content is not null, maxPasses: 16, perPassMs: 10);
                var viaControl = ReactorDiagnostics.DescribeComponent(hostControl);
                var viaContent = hostControl.Content is UIElement content ? ReactorDiagnostics.DescribeComponent(content) : null;
                H.Check("RootAnchor_HostControl",
                    viaControl is { IsRoot: true, Kind: "function", State: [{ Value: "\"hc\"" }] }
                    && viaControl.Name.StartsWith("render in ", StringComparison.Ordinal));
                H.Check("RootAnchor_HostControlContent", viaContent is { IsRoot: true, Kind: "function" });

                hostControl.Dispose();
                H.Check("RootAnchor_DisposedHostControlNotDescribed", ReactorDiagnostics.DescribeComponent(hostControl) is null);
            }
            finally
            {
                H.SetContent(null);
            }
        }
    }
}
