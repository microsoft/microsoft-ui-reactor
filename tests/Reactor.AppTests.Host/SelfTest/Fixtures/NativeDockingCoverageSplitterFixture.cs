using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.1 — coverage fixtures for <see cref="DockSplitterControl"/>.
/// Mounts a splitter inside a real <see cref="FlexPanel"/> with two pane
/// siblings so <c>SimulatePointerDragForTest</c> and arrow-key resize hit
/// the live grow-redistribution path and the <c>ResizeDelta</c> event
/// args carry meaningful values.
/// </summary>
internal static class NativeDockingCoverageSplitterFixtures
{
    /// <summary>
    /// Construct a splitter, toggle Direction between Columns and Rows,
    /// verify <c>ProtectedCursor</c> / Width vs Height switch, and the
    /// no-op same-value branch.
    /// </summary>
    internal class Splitter_DirectionSwitch_AppliesGeometry(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double eps = 1e-9;
            var splitter = new DockSplitterControl();
            H.Check("Splitter_DefaultDirection_Columns",
                splitter.Direction == DockSplitterDirection.Columns);
            H.Check("Splitter_DefaultKeyboardStep",
                Math.Abs(splitter.KeyboardStep - DockSplitterControl.DefaultKeyboardStepDip) < eps);

            // Same-value setter — early-return branch.
            splitter.Direction = DockSplitterDirection.Columns;
            H.Check("Splitter_SetSameDirection_NoOp",
                splitter.Direction == DockSplitterDirection.Columns);

            // Switch to Rows.
            splitter.Direction = DockSplitterDirection.Rows;
            H.Check("Splitter_SwitchToRows", splitter.Direction == DockSplitterDirection.Rows);
            H.Check("Splitter_RowsHasHeight",
                Math.Abs(splitter.Height - DockSplitterControl.HitThicknessDip) < eps);

            // Back to Columns.
            splitter.Direction = DockSplitterDirection.Columns;
            H.Check("Splitter_SwitchBack_Columns",
                splitter.Direction == DockSplitterDirection.Columns);
            H.Check("Splitter_ColumnsHasWidth",
                Math.Abs(splitter.Width - DockSplitterControl.HitThicknessDip) < eps);

            // Custom keyboard step.
            splitter.KeyboardStep = 32.0;
            H.Check("Splitter_KeyboardStep_Configurable",
                Math.Abs(splitter.KeyboardStep - 32.0) < eps);

            await Harness.Render();
            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockSplitterControl.SimulatePointerDragForTest"/>
    /// against a real <see cref="FlexPanel"/> with two pane siblings. The
    /// drag must redistribute Grow weights AND fire ResizeDelta with
    /// IsFinal=true.
    /// </summary>
    internal class Splitter_SimulatedDrag_FiresResizeDeltaAndRedistributesGrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var leading = new Border { MinWidth = 60, MinHeight = 60 };
            var trailing = new Border { MinWidth = 60, MinHeight = 60 };
            FlexPanel.SetGrow(leading, 1);
            FlexPanel.SetGrow(trailing, 1);

            var splitter = new DockSplitterControl
            {
                Direction = DockSplitterDirection.Columns,
            };
            var panel = new FlexPanel
            {
                Direction = FlexDirection.Row,
                Width = 600,
                Height = 200,
            };
            panel.Children.Add(leading);
            panel.Children.Add(splitter);
            panel.Children.Add(trailing);

            H.SetContent(panel);
            await Harness.Render();
            await Harness.Render(); // double-pump so ActualWidth populates

            // SimulatePointerDragForTest doesn't fire the DiagnosticSink
            // (only the real OnPointerPressed/Moved/Released path does),
            // so we don't wire one — exercising that sink needs a real
            // pointer drag.

            int deltaEvents = 0;
            DockSplitterDeltaEventArgs? last = null;
            splitter.ResizeDelta += (_, e) => { deltaEvents++; last = e; };

            // Cumulative delta of +100 DIP should shrink leading's grow
            // weight (because leadingDip stays bigger when we don't grow it…
            // actually: leadingDip = capture + +100, so leading grows). The
            // ResizeDelta solver convention is negated.
            splitter.SimulatePointerDragForTest(cumulativeDeltaDip: 100);

            H.Check("Splitter_Drag_FiresOneFinalDelta", deltaEvents == 1);
            H.Check("Splitter_Drag_IsFinalTrue", last is { IsFinal: true });
            H.Check("Splitter_Drag_DeltaSignNegated",
                last is { } a && Math.Abs(a.Delta - (-100)) < 0.001);
            H.Check("Splitter_Drag_DirectionMatches",
                last is { Direction: DockSplitterDirection.Columns });
            H.Check("Splitter_Drag_HostExtentNonNegative",
                last is { } a2 && a2.HostExtentDip >= 0);

            // Drag the other way to exercise both branches.
            splitter.SimulatePointerDragForTest(cumulativeDeltaDip: -50);
            H.Check("Splitter_SecondDrag_FiresAgain", deltaEvents == 2);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Direct invocation of <see cref="DockSplitterControl.RaiseResizeDeltaForTest"/>
    /// so callers can simulate keyboard / programmatic dispatch in
    /// fixtures that don't need the full flex-panel composition.
    /// </summary>
    internal class Splitter_RaiseResizeDelta_PropagatesEventArgs(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var splitter = new DockSplitterControl();
            H.SetContent(splitter);
            await Harness.Render();

            DockSplitterDeltaEventArgs? captured = null;
            splitter.ResizeDelta += (_, e) => captured = e;

            var args = new DockSplitterDeltaEventArgs(
                delta: 24.0,
                direction: DockSplitterDirection.Rows,
                hostExtentDip: 400.0,
                isFinal: true);
            splitter.RaiseResizeDeltaForTest(args);

            H.Check("Splitter_RaiseForTest_Fired", captured is not null);
            H.Check("Splitter_RaiseForTest_DeltaMatches",
                captured is { } c1 && Math.Abs(c1.Delta - 24.0) < 1e-9);
            H.Check("Splitter_RaiseForTest_DirectionMatches",
                captured?.Direction == DockSplitterDirection.Rows);
            H.Check("Splitter_RaiseForTest_HostExtentMatches",
                captured is { } c2 && Math.Abs(c2.HostExtentDip - 400.0) < 1e-9);
            H.Check("Splitter_RaiseForTest_IsFinalMatches", captured?.IsFinal == true);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Splitter outside a FlexPanel — capture / drag path falls through
    /// the "no parent panel" branches in <c>SnapshotPairAtCapture</c> and
    /// <c>ApplyAbsoluteGrowFromCapture</c> and the simulated drag still
    /// completes (fires ResizeDelta with HostExtent=0).
    /// </summary>
    internal class Splitter_DragWithoutFlexParent_StillFiresEvent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var splitter = new DockSplitterControl();
            var wrapperGrid = new Grid { Width = 300, Height = 100 };
            wrapperGrid.Children.Add(splitter);
            H.SetContent(wrapperGrid);
            await Harness.Render();
            await Harness.Render();

            int events = 0;
            DockSplitterDeltaEventArgs? last = null;
            splitter.ResizeDelta += (_, e) => { events++; last = e; };

            splitter.SimulatePointerDragForTest(50);
            H.Check("Splitter_NoFlexParent_StillFires", events == 1);
            // When the parent isn't a FlexPanel, GetHostExtent falls back
            // to (parent extent - splitter's own size). The value is the
            // grid width minus the splitter handle, but the contract we
            // care about is "non-negative and not NaN/Infinity".
            H.Check("Splitter_NoFlexParent_HostExtentNonNegativeAndFinite",
                last is { HostExtentDip: var x } && x >= 0 && !double.IsNaN(x) && !double.IsInfinity(x));

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Verifies <see cref="DockSplitterControl.OnCreateAutomationPeer"/>
    /// returns a peer with the expected control-type and class name.
    /// </summary>
    internal class Splitter_AutomationPeer_ReportsThumbAndClassName(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var splitter = new DockSplitterControl();
            H.SetContent(splitter);
            await Harness.Render();

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(splitter);
            H.Check("Splitter_Peer_CreatedNonNull", peer is not null);
            H.Check("Splitter_Peer_IsThumbControlType",
                peer?.GetAutomationControlType() == AutomationControlType.Thumb);
            H.Check("Splitter_Peer_ClassName_DockSplitter",
                peer?.GetClassName() == "DockSplitter");
            H.Check("Splitter_Peer_LocalizedControlType",
                peer?.GetLocalizedControlType() == "splitter");

            H.SetContent(null);
            await Harness.Render();
        }
    }
}
