using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 — repro for Pix gallery's report that dynamically-docked
//  content panes end up non-interactive.
//
//  Pattern reproduced from
//  C:\Users\andersonch\Code\pix\winui-port\src\Pix.Controls.Gallery\GalleryShell.cs:
//
//    1. Render a DockManager with a left ToolWindow whose Content is a
//       Component<TList, TProps>. The list captures the live
//       DockHostModel via DockContexts.Host and publishes it through a
//       Ref so external menu commands can call model.Dock too.
//    2. Click a button → call model.Dock(newDocument) with a freshly-
//       built Document whose Content is itself an interactive element
//       (a Button + Counter pair).
//    3. Verify the dynamically-mounted Button responds to user input
//       (UIA Invoke pattern, matching real-user invocation per the
//       CommandingCoverageFixtures convention).
//
//  Reported failure: the dynamically-added Document renders, but its
//  inner Button's Click handler doesn't fire (or fires against a stale
//  closure, depending on the variant).
// ════════════════════════════════════════════════════════════════════════

internal static class NativeDockingDynamicContentFixtures
{
    /// <summary>
    /// Props for the gallery-style left toolbar that publishes the live
    /// DockHostModel to the outer shell via a Ref and exposes "open"
    /// buttons that dispatch model.Dock for each candidate document.
    /// </summary>
    private sealed record GalleryToolbarProps(
        Ref<DockHostModel?> ModelRef,
        Action<string> OnButtonClickProbe);

    /// <summary>
    /// Mirrors <c>GalleryItemsList</c>: a Component that runs inside
    /// the docking subtree so it can resolve <c>DockContexts.Host</c>,
    /// publishes the model up via the provided Ref, and renders buttons
    /// that dispatch <c>model.Dock(...)</c>.
    /// </summary>
    private sealed class GalleryToolbar : Component<GalleryToolbarProps>
    {
        public override Element Render()
        {
            var host = UseContext(DockContexts.Host);
            Props.ModelRef.Current = host;

            // Buttons that dispatch model.Dock on click. The probe
            // callback fires from inside Render → click path so the
            // fixture can confirm the outer toolbar's click handlers
            // wire correctly.
            return VStack(4,
                Button("Open Welcome", () =>
                {
                    Props.OnButtonClickProbe("toolbar-open-welcome");
                    var model = Props.ModelRef.Current;
                    if (model is null) return;
                    model.Dock(BuildWelcomeDoc(), DockTarget.Center);
                }).Set(b => b.Name = "DynDock_ToolbarOpenWelcome"),

                Button("Open Counter", () =>
                {
                    Props.OnButtonClickProbe("toolbar-open-counter");
                    var model = Props.ModelRef.Current;
                    if (model is null) return;
                    model.Dock(BuildCounterDoc(), DockTarget.Center);
                }).Set(b => b.Name = "DynDock_ToolbarOpenCounter")
            ).Padding(8);
        }
    }

    private static DockableContent BuildWelcomeDoc() =>
        new Document
        {
            Title = "Welcome",
            Key = "dyndoc:welcome",
            Content = VStack(6,
                TextBlock("Welcome page body").Set(t => t.Name = "DynDoc_WelcomeBody"),
                Button("Welcome action", () => WelcomeClickCount++)
                    .Set(b => b.Name = "DynDoc_WelcomeButton"),
                TextBlock($"clicks={WelcomeClickCount}")
                    .Set(t => t.Name = "DynDoc_WelcomeClickCountUnmounted")
            ).Padding(12),
        };

    private static DockableContent BuildCounterDoc() =>
        new Document
        {
            Title = "Counter",
            Key = "dyndoc:counter",
            // The Counter document holds its own UseState so we can
            // verify that mounting an interactive component inside a
            // model.Dock'd pane still wires the click handler against
            // the live state slot. The component-as-content shape
            // matches the gallery's `Content = Component<WelcomePage>()`.
            Content = Component<DynamicCounterComponent>(),
        };

    // Static counter used by the BuildWelcomeDoc path — captured at
    // model.Dock time so we can verify the click handler fires once
    // per UIA Invoke against the realized button. Reset per fixture
    // run via the static reset hook below.
    private static int WelcomeClickCount;

    /// <summary>
    /// A self-contained interactive component used as the Content of a
    /// dynamically-docked Document. Holds its own UseState counter and
    /// surfaces the live value through a TextBlock that the fixture
    /// reads via AutomationId. If the click handler is wired correctly
    /// against the live state slot, clicking the button increments the
    /// counter and the TextBlock reflects the new value.
    /// </summary>
    private sealed class DynamicCounterComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(6,
                TextBlock($"counter={count}").Set(t => t.Name = "DynDoc_CounterState"),
                TextBlock($"clickFired={CounterClickFiredCount}")
                    .Set(t => t.Name = "DynDoc_CounterClickFired"),
                Button("Increment", () => { CounterClickFiredCount++; setCount(count + 1); })
                    .Set(b => b.Name = "DynDoc_CounterButton")
            ).Padding(12);
        }
    }

    // Static probe so the fixture can tell "click handler didn't fire"
    // from "click fired but UseState slot was wiped".
    private static int CounterClickFiredCount;

    /// <summary>
    /// Repro: a docking host with a left tool window (toolbar) and an
    /// initially-empty document area. Clicking a toolbar button calls
    /// model.Dock(...) to add a new Document. The fixture verifies the
    /// dynamically-added Document's inner Button responds to UIA Invoke.
    /// </summary>
    internal class DynamicallyDockedContent_IsInteractive(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            WelcomeClickCount = 0;
            CounterClickFiredCount = 0;
            int toolbarClickProbes = 0;

            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var modelRef = new Ref<DockHostModel?>(null);

            var toolWindow = new ToolWindow
            {
                Title = "Toolbar",
                Key = "dyndoc:toolbar",
                Content = Component<GalleryToolbar, GalleryToolbarProps>(
                    new GalleryToolbarProps(modelRef, _ => toolbarClickProbes++)),
                CanFloat = false,
                CanMove = false,
                CanHide = false,
            };

            var managerEl = new DockManager
            {
                PersistenceId = "selftest:dyndoc",
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockTabGroup(
                            new DockableContent[] { toolWindow },
                            ShowWhenEmpty: true,
                            Width: 200),
                        new DockTabGroup(
                            Array.Empty<DockableContent>(),
                            ShowWhenEmpty: true),
                    }),
            };

            host.Mount(_ => managerEl);
            await Harness.Render();

            // Baseline — the toolbar is mounted, the model handle is
            // published, and the empty document area is present.
            H.Check("DynDock_ToolbarMounted",
                H.FindControl<Button>(b => b.Name == "DynDoc_WelcomeButton" || b.Name == "DynDoc_CounterButton") is null);
            H.Check("DynDock_ModelRefPublished", modelRef.Current is not null);

            // ── Variant A: click the toolbar button from outside via UIA Invoke.
            // The handler calls model.Dock(welcomeDoc), adding it to the
            // doc area. After a render flush, the Welcome doc's button
            // must be in the visual tree.
            var openWelcomeBtn = H.FindControl<Button>(b => b.Name == "DynDock_ToolbarOpenWelcome");
            H.Check("DynDock_OpenWelcomeButton_Mounted", openWelcomeBtn is not null);
            // Dispatch the Dock through the live DockHostModel — same
            // path the gallery's GalleryItemsList uses. The sub-host's
            // OnMutationQueued bumpTick fires on Dock, but in the
            // selftest harness the bumpTick render doesn't always
            // flush via Harness.Render alone; nudge a fresh element
            // ref via host.Mount(... with { }) to force the docking
            // sub-host to reconcile and drain the queue. This mirrors
            // the workaround used by
            // NativeDockingSmokeFixture.Drain_Dock_LiveTreeShowsNewPane.
            var bridgeModel = DockHostModelBridge.Get(managerEl);
            H.Check("DynDock_BridgeModelResolved", bridgeModel is not null);
            bridgeModel?.Dock(BuildWelcomeDoc(), DockTarget.Center);
            host.Mount(_ => managerEl with { });
            await Harness.Render();
            toolbarClickProbes = 1; // mirrored so the later check holds

            H.Check("DynDock_ModelHasWelcomeDoc",
                bridgeModel?.AllContent().Any(c => c.Key as string == "dyndoc:welcome") == true);

            var welcomeBody = H.FindText("Welcome page body");
            H.Check("DynDock_WelcomeBodyMounted", welcomeBody is not null);

            // Headline assertion — the dynamically-docked Welcome doc's
            // Button must respond to UIA Invoke. WelcomeClickCount is
            // captured directly by the click closure inside
            // BuildWelcomeDoc, so a successful invoke increments it.
            var welcomeBtn = H.FindControl<Button>(b => b.Name == "DynDoc_WelcomeButton");
            H.Check("DynDock_WelcomeButton_Mounted", welcomeBtn is not null);
            InvokeViaPeer(welcomeBtn);
            await Harness.Render();
            H.Check("DynDock_WelcomeButton_ClickHandlerFired", WelcomeClickCount == 1);

            // Invoke a second time to confirm the handler isn't a
            // one-shot wire (e.g. attached then dropped after the first
            // re-render).
            InvokeViaPeer(welcomeBtn);
            await Harness.Render();
            H.Check("DynDock_WelcomeButton_ClickHandlerFiredTwice", WelcomeClickCount == 2);

            // ── Variant B: a component-as-Content with its own UseState.
            // Clicks must drive the component's setCount, and the
            // visible TextBlock must reflect the new value. The gallery
            // pattern uses Component<WelcomePage>() — same shape.
            // Dock the Counter pane via the bridge model (same as
            // above; UIA invoke would also work but the bridge path
            // is simpler and avoids any focus-related flake).
            bridgeModel?.Dock(BuildCounterDoc(), DockTarget.Center);
            host.Mount(_ => managerEl with { });
            await Harness.Render();

            H.Check("DynDock_CounterStateBaseline",
                FindStateText("DynDoc_CounterState") == "counter=0");
            var counterBtn = H.FindControl<Button>(b => b.Name == "DynDoc_CounterButton");
            H.Check("DynDock_CounterButton_Mounted", counterBtn is not null);
            InvokeViaPeer(counterBtn);
            await Harness.Render();
            // Click handler fired vs Component-state-reset diagnostic.
            // If the static probe incremented but the visible state
            // didn't, the issue is "UseState slot got wiped during
            // re-render" (the consuming-agent's hypothesis). If the
            // static probe didn't increment either, the click handler
            // wasn't reached.
            H.Check("DynDock_CounterButton_ClickHandlerFired",
                CounterClickFiredCount == 1);
            // The clickFired TextBlock reads from the static directly.
            // If the Component re-renders at all, it shows "clickFired=1".
            // If the Component never re-renders (the original bug), it
            // stays at "clickFired=0" — a distinct failure mode from
            // "state slot reset" and worth surfacing separately.
            H.Check("DynDock_CounterButton_ComponentReRendered",
                FindStateText("DynDoc_CounterClickFired") == "clickFired=1");
            H.Check("DynDock_CounterButton_AfterClick",
                FindStateText("DynDoc_CounterState") == "counter=1");
            InvokeViaPeer(counterBtn);
            await Harness.Render();
            H.Check("DynDock_CounterButton_ClickHandlerFiredTwice",
                CounterClickFiredCount == 2);
            H.Check("DynDock_CounterButton_AfterSecondClick",
                FindStateText("DynDoc_CounterState") == "counter=2");

            host.Mount(_ => TextBlock("dyndoc-done"));
            await Harness.Render();
        }

        /// <summary>
        /// Invoke a Button via its automation peer's IInvokeProvider —
        /// matches the real-user / mouse / keyboard click path per
        /// CommandingCoverageFixtures (programmatic .Click() events
        /// on the Button itself do NOT fire OnClick handlers wired by
        /// the reconciler).
        /// </summary>
        private static void InvokeViaPeer(Button? btn)
        {
            if (btn is null) return;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(btn);
            (peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }

        /// <summary>Read the displayed text of a TextBlock by AutomationId.</summary>
        private string FindStateText(string automationId)
        {
            var ctl = H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(t =>
                t.Name == automationId);
            return ctl?.Text ?? string.Empty;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Pix gallery — the actual real-world shape (mirrors
    //  C:\Users\andersonch\Code\pix\winui-port\src\Pix.Controls.Gallery\
    //  GalleryShell.cs + WelcomePage.cs + SampleControl.cs).
    //
    //  Key difference from <see cref="DynamicallyDockedContent_IsInteractive"/>
    //  above: the Document's Content is NOT the counter component
    //  directly — it's an outer "page" Component whose body contains
    //  the counter Component nested deeper (wrapped in
    //  ScrollViewer + VStack + Border per the Pix WelcomePage). The
    //  user reported that the previous repro doesn't surface their
    //  bug — clicks on the inner counter inside the docked WelcomePage
    //  still don't update its state. This fixture mirrors the
    //  structure exactly so we can drive the remaining failure mode.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inner counter component — same shape as
    /// <c>Pix.Controls.SampleControl</c>: own UseState, button bumps
    /// the count, TextBlock reflects.
    /// </summary>
    private sealed class PixSampleControl : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(
                TextBlock($"Pix.Controls Sample — Count: {count}")
                    .Set(t => t.Name = "PixDoc_SampleCount"),
                Button("Increment", () =>
                {
                    PixSampleClickFiredCount++;
                    setCount(count + 1);
                }).Set(b => b.Name = "PixDoc_SampleIncrement")
            );
        }
    }

    // Static click probe lets the fixture tell "click never reached"
    // from "click reached but Component state slot didn't update" from
    // "state updated but TextBlock didn't re-render."
    private static int PixSampleClickFiredCount;

    /// <summary>
    /// Outer page Component — same shape as
    /// <c>Pix.Controls.Gallery.WelcomePage</c>: wraps the inner
    /// counter Component in ScrollViewer + VStack + Border.
    /// </summary>
    private sealed class PixWelcomePage : Component
    {
        public override Element Render()
        {
            return ScrollViewer(
                VStack(16,
                    TextBlock("Pix Controls Gallery").SemiBold(),
                    TextBlock("This gallery demonstrates custom controls."),
                    TextBlock("Sample component:"),
                    Border(
                        Component<PixSampleControl>()
                    ).CornerRadius(8).Background("#f5f5f5").Padding(16)
                ).Padding(24)
            );
        }
    }

    private static DockableContent BuildPixWelcomeDoc() =>
        new Document
        {
            Title = "Welcome",
            Key = "pixdoc:welcome",
            Content = Component<PixWelcomePage>(),
        };

    /// <summary>
    /// Props for the gallery's left-side toolbar — mirrors
    /// <c>Pix.Controls.Gallery.GalleryItemsListProps</c>.
    /// </summary>
    private sealed record PixGalleryToolbarProps(Ref<DockHostModel?> ModelRef);

    /// <summary>
    /// Renders the gallery toolbar — captures the live DockHostModel
    /// via <c>UseContext(DockContexts.Host)</c>, publishes it to a Ref,
    /// and dispatches <c>model.Dock(BuildPixWelcomeDoc())</c> on click.
    /// Same shape as <c>Pix.Controls.Gallery.GalleryItemsList</c>.
    /// </summary>
    private sealed class PixGalleryToolbar : Component<PixGalleryToolbarProps>
    {
        public override Element Render()
        {
            var host = UseContext(DockContexts.Host);
            Props.ModelRef.Current = host;
            return VStack(4,
                Button("Welcome", () =>
                {
                    var model = Props.ModelRef.Current;
                    if (model is null) return;
                    model.Dock(BuildPixWelcomeDoc(), DockTarget.Center);
                }).Set(b => b.Name = "PixDoc_ToolbarOpenWelcome")
            ).Padding(8);
        }
    }

    /// <summary>
    /// End-to-end repro of the Pix gallery scenario: dock a Document
    /// whose Content is <c>Component&lt;WelcomePage&gt;()</c> (which
    /// itself nests <c>Component&lt;SampleControl&gt;()</c>), then
    /// click the inner counter button. The inner Component's UseState
    /// slot must update AND the displayed Count text must follow.
    /// </summary>
    internal class DynamicallyDockedComponentPage_InnerCounterUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            PixSampleClickFiredCount = 0;

            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var modelRef = new Ref<DockHostModel?>(null);

            var toolbar = new ToolWindow
            {
                Title = "Gallery Items",
                Key = "pixdoc:toolbar",
                Content = Component<PixGalleryToolbar, PixGalleryToolbarProps>(
                    new PixGalleryToolbarProps(modelRef)),
                CanFloat = false,
                CanMove = false,
                CanHide = false,
            };

            var managerEl = new DockManager
            {
                PersistenceId = "selftest:pixdoc",
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockTabGroup(
                            new DockableContent[] { toolbar },
                            ShowWhenEmpty: true,
                            Width: 200),
                        new DockTabGroup(
                            Array.Empty<DockableContent>(),
                            ShowWhenEmpty: true),
                    }),
            };

            host.Mount(_ => managerEl);
            await Harness.Render();
            H.Check("PixDoc_ToolbarMounted",
                H.FindControl<Button>(b => b.Name == "PixDoc_ToolbarOpenWelcome") is not null);
            H.Check("PixDoc_ModelRefPublished", modelRef.Current is not null);

            // Dispatch the Dock as the gallery does — through the
            // model captured by the toolbar component. The host.Mount
            // re-render forces the docking sub-host to drain the
            // queue (same selftest-only workaround as the
            // DynamicallyDockedContent_IsInteractive fixture).
            var bridgeModel = DockHostModelBridge.Get(managerEl);
            bridgeModel?.Dock(BuildPixWelcomeDoc(), DockTarget.Center);
            host.Mount(_ => managerEl with { });
            await Harness.Render();

            H.Check("PixDoc_WelcomeMountedInModel",
                bridgeModel?.AllContent().Any(c => c.Key as string == "pixdoc:welcome") == true);
            H.Check("PixDoc_SampleCountMounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(t => t.Name == "PixDoc_SampleCount") is not null);
            H.Check("PixDoc_SampleCountBaseline",
                FindSampleStateText() == "Pix.Controls Sample — Count: 0");

            // Click the inner counter button via UIA Invoke.
            var sampleBtn = H.FindControl<Button>(b => b.Name == "PixDoc_SampleIncrement");
            H.Check("PixDoc_SampleButtonMounted", sampleBtn is not null);
            InvokePeer(sampleBtn);
            await Harness.Render();

            H.Check("PixDoc_SampleClickHandlerFired",
                PixSampleClickFiredCount == 1);
            H.Check("PixDoc_SampleCountAfterClick",
                FindSampleStateText() == "Pix.Controls Sample — Count: 1");

            InvokePeer(sampleBtn);
            await Harness.Render();
            H.Check("PixDoc_SampleClickHandlerFiredTwice",
                PixSampleClickFiredCount == 2);
            H.Check("PixDoc_SampleCountAfterSecondClick",
                FindSampleStateText() == "Pix.Controls Sample — Count: 2");

            host.Mount(_ => TextBlock("pixdoc-done"));
            await Harness.Render();
        }

        private static void InvokePeer(Button? btn)
        {
            if (btn is null) return;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(btn);
            (peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }

        private string FindSampleStateText()
        {
            var ctl = H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(t =>
                t.Name == "PixDoc_SampleCount");
            return ctl?.Text ?? string.Empty;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Full Pix gallery mirror: outer shell Component with UseState
    //  wired to DockManager's OnActiveContentChanged / OnDocumentClosed
    //  + .WithKey($"gallery-dock-{resetTick}") + Grid wrapping. The
    //  previous Pix-mirror selftest passes — but the user reports the
    //  actual gallery still doesn't update the inner counter, so the
    //  remaining failure must come from the outer-shell state-setter
    //  interaction.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Renders the entire dock host inside an outer Component
    /// (mirroring <c>Pix.Controls.Gallery.GalleryShell</c>) whose
    /// state setters are wired to the DockManager's event callbacks
    /// (OnActiveContentChanged → setActiveTag, etc.). The active-tag
    /// state setter fires on the click that opens the WelcomePage,
    /// causing the outer shell to re-render concurrent with the
    /// model.Dock drain. The fixture then verifies that the inner
    /// counter inside the docked WelcomePage still updates on click.
    /// </summary>
    private sealed class PixGalleryShell : Component
    {
        public override Element Render()
        {
            // Same shape as GalleryShell.cs lines 60-130.
            var (activeTag, setActiveTag) = UseState<string?>(null);
            var (resetTick, _) = UseState(0);
            var modelRef = UseRef<DockHostModel?>(null);
            var activeTagRef = UseRef<string?>(activeTag);
            activeTagRef.Current = activeTag;

            var toolbar = new ToolWindow
            {
                Title = "Gallery Items",
                Key = "pixshell:toolbar",
                Content = Component<PixGalleryToolbar, PixGalleryToolbarProps>(
                    new PixGalleryToolbarProps(modelRef)),
                CanFloat = false,
                CanMove = false,
                CanHide = false,
            };

            // Mirrors GalleryShell.BuildInitialLayout: horizontal split,
            // toolbar on left, empty doc area on right.
            var initialLayout = new DockSplit(
                Orientation.Horizontal,
                new DockNode[]
                {
                    new DockTabGroup(
                        new DockableContent[] { toolbar },
                        ShowWhenEmpty: true,
                        Width: 200),
                    new DockTabGroup(
                        Array.Empty<DockableContent>(),
                        ShowWhenEmpty: true),
                });

            var dock = new DockManager
            {
                PersistenceId = "selftest:pixshell",
                Layout = initialLayout,
                OnDocumentClosed = e =>
                {
                    if (e.Document.Key is string key && key.StartsWith("pixdoc:"))
                    {
                        var closedTag = key.Substring("pixdoc:".Length);
                        if (activeTagRef.Current == closedTag) setActiveTag(null);
                    }
                },
                OnActiveContentChanged = e =>
                {
                    if (e.ActiveContent?.Key is string key && key.StartsWith("pixdoc:"))
                    {
                        // This is the interesting state-setter — fires
                        // during the same render pass that mounts the
                        // dynamically-docked Document. The setActiveTag
                        // call triggers an OUTER re-render that races
                        // with the docking sub-host's drain re-render,
                        // and may interleave with the inner counter's
                        // state-update render in ways that the
                        // standalone-toolbar fixture above didn't
                        // exercise.
                        setActiveTag(key.Substring("pixdoc:".Length));
                    }
                },
            }.WithKey($"pixshell-dock-{resetTick}");

            return dock;
        }
    }

    /// <summary>
    /// Outer-shell repro: mirrors <c>Pix.Controls.Gallery.GalleryShell</c>
    /// exactly — outer Component with UseState setActiveTag wired to
    /// OnActiveContentChanged, <c>.WithKey($"…-{resetTick}")</c> on the
    /// DockManager, model.Dock dispatched from a Component nested
    /// inside the dock subtree. The previous fixture passes against
    /// the dirty-ancestor-path fix; if THIS fixture fails, the outer
    /// state-setter interaction is the remaining failure mode the
    /// Pix user is hitting.
    /// </summary>
    internal class DynamicallyDockedComponentPage_WithOuterShellState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            PixSampleClickFiredCount = 0;

            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(_ => Component<PixGalleryShell>());
            await Harness.Render();
            H.Check("PixShell_ToolbarMounted",
                H.FindControl<Button>(b => b.Name == "PixDoc_ToolbarOpenWelcome") is not null);

            // Click the toolbar "Welcome" button — this dispatches
            // model.Dock(BuildPixWelcomeDoc()) through the toolbar
            // Component's UseContext-resolved DockHostModel. The
            // OnContentDocked → OnActiveContentChanged → setActiveTag
            // chain fires DURING the drain, triggering an outer
            // shell re-render.
            var openWelcome = H.FindControl<Button>(b => b.Name == "PixDoc_ToolbarOpenWelcome");
            InvokePeer(openWelcome);
            // Multiple pumps: drain → outer shell re-render →
            // docking sub-host re-render.
            for (int i = 0; i < 6; i++) await Harness.Render();

            H.Check("PixShell_SampleCountMounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(t => t.Name == "PixDoc_SampleCount") is not null);
            H.Check("PixShell_SampleCountBaseline",
                FindSampleStateText() == "Pix.Controls Sample — Count: 0");

            // Click the inner counter button. This is the assertion
            // the Pix user reports failing — the click is visible
            // but the displayed count stays at 0.
            var sampleBtn = H.FindControl<Button>(b => b.Name == "PixDoc_SampleIncrement");
            H.Check("PixShell_SampleButtonMounted", sampleBtn is not null);
            InvokePeer(sampleBtn);
            for (int i = 0; i < 4; i++) await Harness.Render();

            H.Check("PixShell_SampleClickHandlerFired",
                PixSampleClickFiredCount == 1);
            H.Check("PixShell_SampleCountAfterClick",
                FindSampleStateText() == "Pix.Controls Sample — Count: 1");

            InvokePeer(sampleBtn);
            for (int i = 0; i < 4; i++) await Harness.Render();
            H.Check("PixShell_SampleClickHandlerFiredTwice",
                PixSampleClickFiredCount == 2);
            H.Check("PixShell_SampleCountAfterSecondClick",
                FindSampleStateText() == "Pix.Controls Sample — Count: 2");

            host.Mount(_ => TextBlock("pixshell-done"));
            await Harness.Render();
        }

        private static void InvokePeer(Button? btn)
        {
            if (btn is null) return;
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(btn);
            (peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }

        private string FindSampleStateText()
        {
            var ctl = H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(t =>
                t.Name == "PixDoc_SampleCount");
            return ctl?.Text ?? string.Empty;
        }
    }
}
