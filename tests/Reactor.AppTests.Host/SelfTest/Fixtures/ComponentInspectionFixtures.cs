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

    private static Exception? Record(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return ex;
        }
    }

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

                // Off its UI thread, a read fails even with no process-wide dispatcher captured
                // (an embedded ReactorHostControl without ReactorApp.Run): the element's own
                // dispatcher decides.
                var savedDispatcher = ReactorApp.UIDispatcher;
                Exception? offThread = null;
                try
                {
                    ReactorApp.UIDispatcher = null;
                    var reader = new global::System.Threading.Thread(() =>
                        offThread = Record(() => ReactorDiagnostics.DescribeComponent(wrapper)));
                    reader.Start();
                    reader.Join();
                }
                finally { ReactorApp.UIDispatcher = savedDispatcher; }
                H.Check("CompInspect_OffThreadReadThrows", offThread is InvalidOperationException,
                    offThread?.GetType().Name ?? "no exception");

                // Only hosted reconcilers are inspected: lookups walk the host registry behind
                // GetHosts(), so a reconciler driven directly is never read.
                using (var direct = new Reconciler())
                {
                    var directWrapper = direct.Mount(Component<Counter, CounterProps>(new CounterProps(1, "x")), () => { });
                    H.Check("CompInspect_UnhostedReconcilerNotInspected",
                        directWrapper is not null && ReactorDiagnostics.DescribeComponent(directWrapper) is null);
                }
                // A hosted reconciler with no dispatcher recorded is never inspected from any thread.
                var recorded = host.Reconciler.DiagnosticsDispatcher;
                host.Reconciler.DiagnosticsDispatcher = null;
                H.Check("CompInspect_UncapturedReconcilerNotInspected", ReactorDiagnostics.DescribeComponent(wrapper) is null);
                host.Reconciler.DiagnosticsDispatcher = recorded;
                var counter = ReactorDiagnostics.DescribeComponent(wrapper);
                H.Check("CompInspect_ClassDescribed",
                    counter is { Name: "Counter", Kind: "class", IsRoot: false });
                // NativeAOT/trimming may strip the props record's property metadata; Props then
                // degrades to one "(members unavailable)" row. Either way the secret never leaks.
                var fullProps = counter is not null
                    && counter.Props.Count == 2
                    && counter.Props[0] is { Name: "Step", Type: "int", Value: "2", Redacted: false }
                    && counter.Props[1] is { Name: "AdminPassword", Value: "<redacted>", Redacted: true };
                var opaqueProps = counter is { Props: [{ Name: "Props", Value: "CounterProps (members unavailable)" }] };
                H.Check("CompInspect_ClassProps",
                    counter is not null
                    && !counter.Props.Any(p => p.Value.Contains("hunter2", StringComparison.Ordinal))
                    && (fullProps || (!global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && opaqueProps)));
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

    private sealed class ShellChild : Microsoft.UI.Reactor.Core.Component
    {
        public override Element Render()
        {
            var (s, _) = UseState("c");
            return TextBlock($"shell-child:{s}");
        }
    }

    private sealed class ShellRoot : Microsoft.UI.Reactor.Core.Component
    {
        public override Element Render()
        {
            var (n, _) = UseState(1);
            _ = n;
            return Component<ShellChild>();
        }
    }

    /// <summary>
    /// Every element a host root is reachable from resolves to the root component: the host's
    /// rendered control, its <c>ContentTarget</c>, the dev-overlay wrapper the host installs in
    /// that target while an overlay is on, and a <c>ReactorHostControl</c> and its content. When the
    /// rendered control is also a child component's wrapper, the child wins there.
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

            // A reconciler owned by another UI thread is never read: with this host's dispatcher
            // swapped for a dedicated thread's, its root no longer resolves from here.
            var ownDispatcher = host.Reconciler.DiagnosticsDispatcher;
            var foreignThread = Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnDedicatedThread();
            try
            {
                host.Reconciler.DiagnosticsDispatcher = foreignThread.DispatcherQueue;
                H.Check("RootAnchor_ForeignThreadReconcilerSkipped", ReactorDiagnostics.DescribeComponent(target) is null);
            }
            finally
            {
                host.Reconciler.DiagnosticsDispatcher = ownDispatcher;
                await foreignThread.ShutdownQueueAsync();
            }
            H.Check("RootAnchor_OwnThreadReconcilerRead", IsRoot(ReactorDiagnostics.DescribeComponent(target)));

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

            // A root that renders a component directly: its rendered control is the child's
            // wrapper, which describes the child; the ContentTarget still reaches the root.
            host.Mount(new ShellRoot());
            // Mount swaps the requested root at once but renders later; until then the
            // displayed control still belongs to — and is described as — the previous root.
            H.Check("RootAnchor_RemountGapDescribesDisplayedRoot",
                ReactorDiagnostics.DescribeComponent(target) is { IsRoot: true, Name: "RootCounter", State: [{ Value: "7" }] });
            await Harness.WaitFor(() => H.FindControl<TextBlock>(t => t.Text == "shell-child:c") is not null,
                maxPasses: 16, perPassMs: 10);
            var overlap = host.CurrentControl;
            H.Check("RootAnchor_OverlapRenderedControlIsChild",
                overlap is not null && ReactorDiagnostics.DescribeComponent(overlap) is { IsRoot: false, Name: "ShellChild", State: [{ Value: "\"c\"" }] });
            H.Check("RootAnchor_OverlapContentTargetIsRoot",
                ReactorDiagnostics.DescribeComponent(target) is { IsRoot: true, Name: "ShellRoot", State: [{ Value: "1" }] });
            H.Check("RootAnchor_OverlapEditsAddressTheChild",
                overlap is not null && ReactorDiagnostics.TrySetState(overlap, 0, "d", out _));
            await Harness.WaitFor(() => H.FindControl<TextBlock>(t => t.Text == "shell-child:d") is not null,
                maxPasses: 16, perPassMs: 10);
            H.Check("RootAnchor_OverlapChildRerendered", H.FindControl<TextBlock>(t => t.Text == "shell-child:d") is not null);

            // The root shows an app-supplied render-error fallback: that is not the root's output,
            // so neither the container nor the fallback control describes the root.
            var fail = false;
            var fbHost = H.CreateHost();
            fbHost.RenderErrorHandler = _ => TextBlock("root-app-fallback");
            fbHost.Mount(ctx =>
            {
                var (s, _) = ctx.UseState("ok");
                if (fail) throw new InvalidOperationException("root render failed");
                return TextBlock($"root-recovered:{s}");
            });
            await Harness.Render();
            var fbTarget = fbHost.ContentTarget;
            // Render once successfully first, so the fallback replaces an already-described root.
            H.Check("RootAnchor_RootDescribedBeforeFailure",
                fbTarget is not null && ReactorDiagnostics.DescribeComponent(fbTarget) is { IsRoot: true });
            fail = true;
            fbHost.RequestRender();
            await Harness.WaitFor(() => H.FindText("root-app-fallback") is not null, maxPasses: 16, perPassMs: 10);
            var fallback = H.FindText("root-app-fallback");
            H.Check("RootAnchor_AppFallbackShown", fallback is not null && fbTarget is not null,
                $"content={fbTarget?.Child?.GetType().Name ?? "null"}");
            if (fallback is null || fbTarget is null) return;
            H.Check("RootAnchor_AppFallbackContainerNotDescribed", ReactorDiagnostics.DescribeComponent(fbTarget) is null);
            H.Check("RootAnchor_AppFallbackControlNotDescribed", ReactorDiagnostics.DescribeComponent(fallback) is null);
            fail = false;
            fbHost.RequestRender();
            await Harness.WaitFor(() => H.FindText("root-recovered:ok") is not null, maxPasses: 16, perPassMs: 10);
            H.Check("RootAnchor_RecoveredRootDescribed",
                ReactorDiagnostics.DescribeComponent(fbTarget) is { IsRoot: true, Kind: "function", State: [{ Value: "\"ok\"" }] });

            // A root that renders Empty() has no rendered control; its ContentTarget still anchors
            // it. A private container keeps other (undisposed) hosts out of the picture.
            var emptyTarget = new Border();
            H.SetContent(emptyTarget);
            using (var emptyHost = new ReactorHost(H.Window) { ContentTarget = emptyTarget })
            {
                emptyHost.Mount(ctx =>
                {
                    var (s, _) = ctx.UseState("empty-root");
                    return Empty();
                });
                await Harness.Render();
                H.Check("RootAnchor_EmptyRootViaContentTarget",
                    emptyHost.CurrentControl is null
                    && ReactorDiagnostics.DescribeComponent(emptyTarget) is { IsRoot: true, State: [{ Value: "\"empty-root\"" }] });

                // An empty container proves nothing by itself: an unrelated empty Border the host
                // never mounted into is not claimed…
                var unrelated = new Border();
                emptyHost.ContentTarget = unrelated;
                H.Check("RootAnchor_EmptyRootDoesNotClaimUnrelatedContainer", ReactorDiagnostics.DescribeComponent(unrelated) is null);
                // Re-mounting into it claims the container only together with publishing the new root.
                emptyHost.Mount(ctx =>
                {
                    var (s, _) = ctx.UseState("retargeted");
                    return Empty();
                });
                await Harness.Render();
                H.Check("RootAnchor_RetargetClaimedWithItsRenderedRoot",
                    ReactorDiagnostics.DescribeComponent(unrelated) is { IsRoot: true, State: [{ Value: "\"retargeted\"" }] });
                emptyHost.ContentTarget = emptyTarget;
                emptyHost.Mount(ctx =>
                {
                    var (s, _) = ctx.UseState("empty-root");
                    return Empty();
                });
                await Harness.Render();
                // Rendering into emptyTarget dropped the claim on the earlier container, so pointing
                // the host back at it does not resolve it to the root rendered elsewhere.
                emptyHost.ContentTarget = unrelated;
                H.Check("RootAnchor_RetargetDropsTheEarlierClaim", ReactorDiagnostics.DescribeComponent(unrelated) is null);
                emptyHost.ContentTarget = emptyTarget;

                // …and of two empty-root hosts sharing one container, the latest to mount owns it.
                using var secondEmpty = new ReactorHost(H.Window) { ContentTarget = emptyTarget };
                secondEmpty.Mount(ctx =>
                {
                    var (s, _) = ctx.UseState("second-empty");
                    return Empty();
                });
                await Harness.Render();
                H.Check("RootAnchor_SharedEmptyContainerOwnedByLatestMount",
                    ReactorDiagnostics.DescribeComponent(emptyTarget) is { IsRoot: true, State: [{ Value: "\"second-empty\"" }] });

                // Overlay teardown writes the (empty) raw content back and reclaims the container,
                // even though another host claimed it while the wrapper was installed.
                var overlayBefore = ReactorFeatureFlags.HighlightReconcileChanges;
                try
                {
                    ReactorFeatureFlags.HighlightReconcileChanges = true;
                    emptyHost.RequestRender();
                    await Harness.Render();
                    H.Check("RootAnchor_EmptyRootWrapperInstalled", emptyTarget.Child is not null);
                    ReactorFeatureFlags.HighlightReconcileChanges = false;
                    secondEmpty.Mount(ctx =>
                    {
                        var (s, _) = ctx.UseState("second-empty");
                        return Empty();
                    });
                    emptyHost.RequestRender();
                    await Harness.Render();
                    await Harness.Render();
                    H.Check("RootAnchor_OverlayTeardownReclaimsEmptyContainer",
                        emptyTarget.Child is null
                        && ReactorDiagnostics.DescribeComponent(emptyTarget) is { IsRoot: true, State: [{ Value: "\"empty-root\"" }] });
                }
                finally { ReactorFeatureFlags.HighlightReconcileChanges = overlayBefore; }

                // Disposing the owner releases its claim, so the table holds no disposed host.
                emptyHost.Dispose();
                H.Check("RootAnchor_DisposeReleasesContainerOwnership", !ReactorHost.HasContentTargetOwnerForTest(emptyTarget));
            }
            H.SetContent(null);

            // A host without a ContentTarget installs its root as the window content.
            var window = H.Window;
            var previousContent = window.Content;
            try
            {
                using var bare = new ReactorHost(window);
                bare.Mount(new RootCounter());
                await Harness.WaitFor(() => bare.CurrentControl is not null && ReferenceEquals(window.Content, bare.CurrentControl),
                    maxPasses: 16, perPassMs: 10);
                H.Check("RootAnchor_WindowContent",
                    window.Content is UIElement windowContent && IsRoot(ReactorDiagnostics.DescribeComponent(windowContent)));
            }
            finally
            {
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

                // Content is public: an element a consumer swaps in is not attributed to the root.
                var rendered = hostControl.Content;
                var foreign = new Border();
                hostControl.Content = foreign;
                H.Check("RootAnchor_HostControlForeignContentNotDescribed", ReactorDiagnostics.DescribeComponent(foreign) is null);
                H.Check("RootAnchor_HostControlStillDescribedAfterSwap", ReactorDiagnostics.DescribeComponent(hostControl) is { IsRoot: true });
                hostControl.Content = rendered;

                // Remount gap: the displayed (function) root is still what is described.
                hostControl.Mount(new RootCounter());
                H.Check("RootAnchor_HostControlRemountGap",
                    ReactorDiagnostics.DescribeComponent(hostControl) is { IsRoot: true, Kind: "function", State: [{ Value: "\"hc\"" }] });
                await Harness.WaitFor(() => ReactorDiagnostics.DescribeComponent(hostControl) is { Name: "RootCounter" },
                    maxPasses: 16, perPassMs: 10);
                H.Check("RootAnchor_HostControlRemounted",
                    ReactorDiagnostics.DescribeComponent(hostControl) is { IsRoot: true, Name: "RootCounter" });

                // A root that renders Empty() is still anchored at the control itself.
                hostControl.Mount(ctx =>
                {
                    var (s, _) = ctx.UseState("hc-empty");
                    return Empty();
                });
                await Harness.WaitFor(() => ReactorDiagnostics.DescribeComponent(hostControl) is { State: [{ Value: "\"hc-empty\"" }] },
                    maxPasses: 16, perPassMs: 10);
                H.Check("RootAnchor_HostControlEmptyRoot",
                    ReactorDiagnostics.DescribeComponent(hostControl) is { IsRoot: true, Kind: "function", State: [{ Value: "\"hc-empty\"" }] });

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
