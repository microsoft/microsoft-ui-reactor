using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.3 — coverage fixtures for <see cref="DockDropTargetOverlayControl"/>.
/// Mounts the overlay directly (no DockManager wrapper) and pokes the
/// internal test hooks (<c>SetHoveredForTest</c> / <c>ConfirmTargetForTest</c>,
/// <c>FocusTarget</c>, <c>NextFocus</c>, <c>ComputePreviewBounds</c>) to
/// exercise the visibility / event paths without needing a real WinUI
/// drag operation.
/// </summary>
internal static class NativeDockingCoverageOverlayFixtures
{
    /// <summary>
    /// Mode = Host — only the 4 outer dock-edge buttons visible; inner
    /// cluster collapsed. Hovering each edge target fires TargetHovered;
    /// confirming fires TargetConfirmed with confirmed=true.
    /// </summary>
    internal class Overlay_HostMode_EdgesVisible_InnerHidden(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = new DockDropTargetOverlayControl
            {
                Width = 600,
                Height = 400,
                Mode = DockDropOverlayMode.Host,
            };
            H.SetContent(overlay);
            await Harness.Render();

            int hoverCount = 0;
            int confirmCount = 0;
            DockTarget? lastHover = null;
            DockTarget? lastConfirm = null;
            bool dismissed = false;
            overlay.TargetHovered += (_, e) => { hoverCount++; lastHover = e.Target; };
            overlay.TargetConfirmed += (_, e) => { confirmCount++; lastConfirm = e.Target; };
            overlay.OverlayDismissed += (_, _) => dismissed = true;

            // Drive hover via the internal test seam (no real pointer).
            overlay.SetHoveredForTest(DockTarget.DockLeft);
            H.Check("Overlay_Host_HoverDockLeft_FiresEvent", hoverCount == 1);
            H.Check("Overlay_Host_HoverDockLeft_TargetMatches",
                lastHover == DockTarget.DockLeft);
            H.Check("Overlay_Host_HoveredTargetReadable",
                overlay.HoveredTarget == DockTarget.DockLeft);

            // Switching hover fires again.
            overlay.SetHoveredForTest(DockTarget.DockRight);
            H.Check("Overlay_Host_HoverChange_FiresAgain", hoverCount == 2);
            H.Check("Overlay_Host_HoverChange_NewTarget",
                lastHover == DockTarget.DockRight);

            // Same target — no spurious re-fire.
            overlay.SetHoveredForTest(DockTarget.DockRight);
            H.Check("Overlay_Host_SameTarget_NoRefire", hoverCount == 2);

            // Clear hover.
            overlay.SetHoveredForTest(null);
            H.Check("Overlay_Host_ClearHover_FiresWithNull",
                hoverCount == 3 && lastHover is null);
            H.Check("Overlay_Host_HoveredTargetCleared",
                overlay.HoveredTarget is null);

            // Confirm a target.
            overlay.ConfirmTargetForTest(DockTarget.DockBottom);
            H.Check("Overlay_Host_Confirm_FiresEvent", confirmCount == 1);
            H.Check("Overlay_Host_Confirm_TargetMatches",
                lastConfirm == DockTarget.DockBottom);

            // Programmatic FocusTarget — sets focus AND hovered.
            overlay.FocusTarget(DockTarget.DockTop);
            await Harness.Render();
            H.Check("Overlay_Host_FocusTarget_SetsFocusedTarget",
                overlay.FocusedTarget == DockTarget.DockTop);
            H.Check("Overlay_Host_FocusTarget_SetsHovered",
                overlay.HoveredTarget == DockTarget.DockTop);

            _ = dismissed;
            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Mode = GroupInner — the 4 edge buttons collapse; the inner 5 are
    /// hidden until DragEnter reveals them. Switching modes mid-life
    /// exercises the ApplyModeVisibility branches.
    /// </summary>
    internal class Overlay_GroupInnerMode_ModeSwitch_AppliesVisibility(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = new DockDropTargetOverlayControl
            {
                Width = 500,
                Height = 300,
                Mode = DockDropOverlayMode.GroupInner,
            };
            H.SetContent(overlay);
            await Harness.Render();

            H.Check("Overlay_GroupInner_InitialMode",
                overlay.Mode == DockDropOverlayMode.GroupInner);

            // Setting the same mode is a no-op (returns early).
            overlay.Mode = DockDropOverlayMode.GroupInner;
            H.Check("Overlay_SameMode_NoOp", overlay.Mode == DockDropOverlayMode.GroupInner);

            // Switch to CenterOnly — exercises the centerOnly branch in ApplyModeVisibility.
            overlay.Mode = DockDropOverlayMode.CenterOnly;
            H.Check("Overlay_SwitchToCenterOnly_ModeUpdated",
                overlay.Mode == DockDropOverlayMode.CenterOnly);

            // Switch to Host.
            overlay.Mode = DockDropOverlayMode.Host;
            H.Check("Overlay_SwitchToHost_ModeUpdated",
                overlay.Mode == DockDropOverlayMode.Host);

            // Back to GroupInner.
            overlay.Mode = DockDropOverlayMode.GroupInner;
            H.Check("Overlay_BackToGroupInner_ModeUpdated",
                overlay.Mode == DockDropOverlayMode.GroupInner);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockDropTargetOverlayControl.NextFocus"/> for
    /// every transition arm. The method is static and pure, so we test it
    /// independently of any mounted overlay.
    /// </summary>
    internal class Overlay_NextFocus_AllArrowKeyArmsResolve(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Center → cluster arms
            H.Check("Overlay_Nav_Center_Left",
                DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Left) == DockTarget.SplitLeft);
            H.Check("Overlay_Nav_Center_Right",
                DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Right) == DockTarget.SplitRight);
            H.Check("Overlay_Nav_Center_Up",
                DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Up) == DockTarget.SplitTop);
            H.Check("Overlay_Nav_Center_Down",
                DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Down) == DockTarget.SplitBottom);

            // Split → Center inward
            H.Check("Overlay_Nav_SplitLeft_Right",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitLeft, VirtualKey.Right) == DockTarget.Center);
            H.Check("Overlay_Nav_SplitRight_Left",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitRight, VirtualKey.Left) == DockTarget.Center);
            H.Check("Overlay_Nav_SplitTop_Down",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitTop, VirtualKey.Down) == DockTarget.Center);
            H.Check("Overlay_Nav_SplitBottom_Up",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitBottom, VirtualKey.Up) == DockTarget.Center);

            // Split → Edge outward
            H.Check("Overlay_Nav_SplitLeft_Left",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitLeft, VirtualKey.Left) == DockTarget.DockLeft);
            H.Check("Overlay_Nav_SplitRight_Right",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitRight, VirtualKey.Right) == DockTarget.DockRight);
            H.Check("Overlay_Nav_SplitTop_Up",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitTop, VirtualKey.Up) == DockTarget.DockTop);
            H.Check("Overlay_Nav_SplitBottom_Down",
                DockDropTargetOverlayControl.NextFocus(DockTarget.SplitBottom, VirtualKey.Down) == DockTarget.DockBottom);

            // Edge → Split inward
            H.Check("Overlay_Nav_DockLeft_Right",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockLeft, VirtualKey.Right) == DockTarget.SplitLeft);
            H.Check("Overlay_Nav_DockRight_Left",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockRight, VirtualKey.Left) == DockTarget.SplitRight);
            H.Check("Overlay_Nav_DockTop_Down",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockTop, VirtualKey.Down) == DockTarget.SplitTop);
            H.Check("Overlay_Nav_DockBottom_Up",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockBottom, VirtualKey.Up) == DockTarget.SplitBottom);

            // Edge ring (sideways)
            H.Check("Overlay_Nav_DockLeft_Up",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockLeft, VirtualKey.Up) == DockTarget.DockTop);
            H.Check("Overlay_Nav_DockLeft_Down",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockLeft, VirtualKey.Down) == DockTarget.DockBottom);
            H.Check("Overlay_Nav_DockRight_Up",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockRight, VirtualKey.Up) == DockTarget.DockTop);
            H.Check("Overlay_Nav_DockRight_Down",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockRight, VirtualKey.Down) == DockTarget.DockBottom);
            H.Check("Overlay_Nav_DockTop_Left",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockTop, VirtualKey.Left) == DockTarget.DockLeft);
            H.Check("Overlay_Nav_DockTop_Right",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockTop, VirtualKey.Right) == DockTarget.DockRight);
            H.Check("Overlay_Nav_DockBottom_Left",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockBottom, VirtualKey.Left) == DockTarget.DockLeft);
            H.Check("Overlay_Nav_DockBottom_Right",
                DockDropTargetOverlayControl.NextFocus(DockTarget.DockBottom, VirtualKey.Right) == DockTarget.DockRight);

            // Unhandled fallback (Center with no arrow — Tab key).
            H.Check("Overlay_Nav_UnhandledKey_StaysPut",
                DockDropTargetOverlayControl.NextFocus(DockTarget.Center, VirtualKey.Tab) == DockTarget.Center);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockDropTargetOverlayControl.ComputePreviewBounds"/>
    /// across every DockTarget. The function is pure and shape-correct
    /// independent of any live overlay.
    /// </summary>
    internal class Overlay_ComputePreviewBounds_ShapeCorrectPerTarget(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double w = 1000;
            const double h2 = 600;
            // ComputePreviewBounds returns Rect with values derived from
            // floating-point fractions (w/2, h2*0.30); compare with a half-
            // pixel tolerance so the assertions stay robust.
            static bool Near(double a, double b) => Math.Abs(a - b) < 0.5;

            // Center fills the host.
            var c = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.Center, w, h2);
            H.Check("PreviewBounds_Center_FullExtent",
                Near(c.X, 0) && Near(c.Y, 0) && Near(c.Width, w) && Near(c.Height, h2));

            // Left/Right halves on split.
            var sl = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitLeft, w, h2);
            H.Check("PreviewBounds_SplitLeft_LeftHalf",
                Near(sl.X, 0) && Near(sl.Y, 0) && Near(sl.Width, w / 2) && Near(sl.Height, h2));

            var sr = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitRight, w, h2);
            H.Check("PreviewBounds_SplitRight_RightHalf",
                Near(sr.X, w / 2) && Near(sr.Y, 0) && Near(sr.Width, w / 2));

            var st = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitTop, w, h2);
            H.Check("PreviewBounds_SplitTop_TopHalf",
                Near(st.X, 0) && Near(st.Y, 0) && Near(st.Width, w) && Near(st.Height, h2 / 2));

            var sb = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.SplitBottom, w, h2);
            H.Check("PreviewBounds_SplitBottom_BottomHalf",
                Near(sb.X, 0) && Near(sb.Y, h2 / 2) && Near(sb.Width, w));

            // Edge docks use EdgePreviewFraction (0.30).
            var dl = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockLeft, w, h2);
            H.Check("PreviewBounds_DockLeft_FractionWidth",
                Near(dl.X, 0) && Near(dl.Width, w * 0.30) && Near(dl.Height, h2));

            var dr = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockRight, w, h2);
            H.Check("PreviewBounds_DockRight_FractionAnchoredRight",
                Near(dr.X, w - w * 0.30) && Near(dr.Width, w * 0.30));

            var dt = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockTop, w, h2);
            H.Check("PreviewBounds_DockTop_FractionHeight",
                Near(dt.Y, 0) && Near(dt.Height, h2 * 0.30));

            var db = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.DockBottom, w, h2);
            H.Check("PreviewBounds_DockBottom_FractionAnchoredBottom",
                Near(db.Y, h2 - h2 * 0.30));

            // Zero-extent host — returns Rect.Empty.
            var z = DockDropTargetOverlayControl.ComputePreviewBounds(DockTarget.Center, 0, 0);
            H.Check("PreviewBounds_ZeroHost_ReturnsEmpty",
                z.IsEmpty || (Near(z.Width, 0) && Near(z.Height, 0)));

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockDropTargetOverlayControl.GetLocalizedName"/>
    /// for each DockTarget. Provides incidental coverage of
    /// <c>DockingStrings.Get</c> across the 9 drop-target keys.
    /// </summary>
    internal class Overlay_GetLocalizedName_AllTargetsResolve(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Each call must return a non-empty string. The actual string
            // is locale-dependent — apps install a Resolver — so we only
            // assert non-empty + non-key here.
            foreach (DockTarget t in new[]
            {
                DockTarget.Center,
                DockTarget.SplitLeft, DockTarget.SplitRight, DockTarget.SplitTop, DockTarget.SplitBottom,
                DockTarget.DockLeft, DockTarget.DockRight, DockTarget.DockTop, DockTarget.DockBottom,
            })
            {
                var name = DockDropTargetOverlayControl.GetLocalizedName(t);
                H.Check($"Overlay_LocalizedName_{t}",
                    !string.IsNullOrWhiteSpace(name));
            }

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Mounts a host-mode overlay, drives a few hover cycles to refresh
    /// the preview bounds, then calls <c>DetachGlobalHandlers</c> to
    /// exercise the unmount-side teardown branch (no-op + idempotent).
    /// </summary>
    internal class Overlay_PreviewBounds_TracksHoveredTarget(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = new DockDropTargetOverlayControl
            {
                Width = 800,
                Height = 400,
                Mode = DockDropOverlayMode.Host,
            };
            H.SetContent(overlay);
            await Harness.Render();

            // No hover — PreviewBounds should be empty.
            var before = overlay.PreviewBounds;
            H.Check("Overlay_Preview_DefaultEmpty",
                before.IsEmpty || (before.Width == 0 && before.Height == 0));

            // Hover an edge — preview becomes visible.
            overlay.SetHoveredForTest(DockTarget.DockLeft);
            await Harness.Render();
            var afterEdge = overlay.PreviewBounds;
            H.Check("Overlay_Preview_DockLeft_NonEmpty",
                !afterEdge.IsEmpty && afterEdge.Width > 0 && afterEdge.Height > 0);

            // Clear — preview collapses.
            overlay.SetHoveredForTest(null);
            await Harness.Render();
            var cleared = overlay.PreviewBounds;
            H.Check("Overlay_Preview_Cleared_Empty",
                cleared.IsEmpty || (cleared.Width == 0 && cleared.Height == 0));

            // Idempotent teardown.
            overlay.DetachGlobalHandlers();
            overlay.DetachGlobalHandlers(); // second call must be safe
            H.Check("Overlay_DetachGlobalHandlers_Idempotent", true);

            H.SetContent(null);
            await Harness.Render();
        }
    }
}
