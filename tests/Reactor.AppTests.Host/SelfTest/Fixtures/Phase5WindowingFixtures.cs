using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Windowing;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 5 fixtures for content-driven top-level window sizing.</summary>
internal static class Phase5WindowingFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class FixedContent(double width, double height) : Component
    {
        public override Element Render() => Border(TextBlock("content")).Width(width).Height(height);
    }

    private sealed class ResizableContent : Component
    {
        public Action? Grow { get; private set; }

        public override Element Render()
        {
            var (large, setLarge) = UseState(false);
            Grow = () => setLarge(true);
            return Border(TextBlock("content")).Width(large ? 420 : 280).Height(large ? 320 : 180);
        }
    }

    /// <summary>
    /// Alternates the root control *type* on demand, so the reconciler hands
    /// <c>OnHostContentRendered</c> a different <c>FrameworkElement</c> instance
    /// and <c>AttachSizeToContentRoot</c> takes its root-replacement path.
    /// </summary>
    private sealed class SwappableRootContent : Component
    {
        public Action? Swap { get; private set; }

        public override Element Render()
        {
            var (alt, setAlt) = UseState(false);
            Swap = () => setAlt(!alt);
            return alt
                ? VStack(TextBlock("content")).Width(300).Height(200)
                : Border(TextBlock("content")).Width(300).Height(200);
        }
    }

    /// <summary>
    /// Alternates the root between a real control and <c>Empty()</c>. The
    /// reconciler returns <c>null</c> for an <c>EmptyElement</c> root
    /// (Reconciler.cs — "if (newElement is null or EmptyElement) ... return null"),
    /// so <c>OnHostContentRendered</c> receives <c>null</c> and
    /// <c>AttachSizeToContentRoot</c> takes its null-root path while
    /// size-to-content is still enabled.
    /// </summary>
    private sealed class VanishingRootContent : Component
    {
        public Action? Toggle { get; private set; }

        public override Element Render()
        {
            var (gone, setGone) = UseState(false);
            Toggle = () => setGone(!gone);
            return gone
                ? Empty()
                : Border(TextBlock("content")).Width(300).Height(200);
        }
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(120);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows)
        {
            if (win is null) continue;
            try { win.Close(); } catch { }
        }
        await Task.Delay(100);
    }

    // Forced GC + finalizer drain is intentional here. See the comment on
    // CollectWindowResources in Phase2WindowingFixtures.cs for rationale —
    // releases accumulated WinRT/COM handles between fixtures to stay under
    // the WinUI 3 native-handle exhaustion threshold (PR #536 repro).
    private static async Task CollectWindowResources()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(50);
    }

    private static (int Width, int Height) ExpectedWindowSize(ReactorWindow win, double contentWidthDip, double contentHeightDip)
    {
        uint dpi = (uint)(win.Dpi == 0 ? 96 : win.Dpi);
        var rect = new Native.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Round(contentWidthDip * dpi / 96.0),
            Bottom = (int)Math.Round(contentHeightDip * dpi / 96.0),
        };
        long style = (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_STYLE);
        long exStyle = (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_EXSTYLE);
        _ = Native.AdjustWindowRectExForDpi(ref rect, unchecked((uint)style), false, unchecked((uint)exStyle), dpi);
        return (Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
    }

    private static bool WidthNear(ReactorWindow win, int expected, int tolerance = 6)
        => Math.Abs(win.AppWindow.Size.Width - expected) <= tolerance;

    private static bool HeightNear(ReactorWindow win, int expected, int tolerance = 6)
        => Math.Abs(win.AppWindow.Size.Height - expected) <= tolerance;

    private static nint Hwnd(ReactorWindow win) => WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);

    internal class SizeToContentWidthTracks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Width",
                Width = 240,
                Height = 220,
                SizeToContent = WindowSizeToContent.Width,
            }, () => new FixedContent(400, 120));
            try
            {
                var expected = ExpectedWindowSize(win, 400, 120);
                bool settled = await Harness.WaitFor(() => WidthNear(win, expected.Width), maxPasses: 10, perPassMs: 30);
                H.Check("SizeToContent_Width_Tracks", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentHeightTracks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Height",
                Width = 360,
                Height = 160,
                SizeToContent = WindowSizeToContent.Height,
            }, () => new FixedContent(160, 300));
            try
            {
                var expected = ExpectedWindowSize(win, 160, 300);
                bool settled = await Harness.WaitFor(() => HeightNear(win, expected.Height), maxPasses: 10, perPassMs: 30);
                H.Check("SizeToContent_Height_Tracks", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentWidthAndHeight(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Both",
                Width = 240,
                Height = 160,
                SizeToContent = WindowSizeToContent.WidthAndHeight,
            }, () => new FixedContent(420, 280));
            try
            {
                var expected = ExpectedWindowSize(win, 420, 280);
                bool settled = await Harness.WaitFor(
                    () => WidthNear(win, expected.Width) && HeightNear(win, expected.Height),
                    maxPasses: 10,
                    perPassMs: 30);
                H.Check("SizeToContent_WidthAndHeight", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentRespectsMinMax(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC MinMax",
                Width = 240,
                Height = 160,
                MinWidth = 500,
                SizeToContent = WindowSizeToContent.WidthAndHeight,
            }, () => new FixedContent(400, 300));
            try
            {
                int minWidthPx = (int)Math.Round(500 * win.DipScale);
                var expected = ExpectedWindowSize(win, 400, 300);
                bool settled = await Harness.WaitFor(
                    () => win.AppWindow.Size.Width >= minWidthPx && HeightNear(win, expected.Height),
                    maxPasses: 10,
                    perPassMs: 30);
                H.Check("SizeToContent_RespectsMinMax", settled);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentNoOpWhenMaximized(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0;
            var spec = new WindowSpec { Title = "STC Max", Width = 360, Height = 240 };
            var win = await OpenAndSettle(spec, () => new FixedContent(500, 400));
            try
            {
                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);
                win.SizeToContentApplyCountForTests = 0;
                win.Update(spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(120);
                H.Check("SizeToContent_NoOpWhenMaximized_State", Native.IsZoomed(Hwnd(win)));
                // Exactly once, not once per layout pass: ApplySizeToContent runs
                // off LayoutUpdated, and DiagnosticLog.Warning is release-visible,
                // so a regression to per-pass warning is an unbounded ETW stream.
                // `> 0` would pass either way.
                H.Check("SizeToContent_NoOpWhenMaximized_Warning", ReactorWindow.SizeToContentMaximizedWarningCountForTests == 1);
                H.Check("SizeToContent_NoOpWhenMaximized_NoResize", win.SizeToContentApplyCountForTests == 0);
            }
            finally { ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// The title-bar-height warning must fire on entering the invalid
    /// combination, not on every chrome re-apply. <c>ApplyChrome</c> runs on any
    /// unequal spec, so an app that keeps <c>TitleBarHeight</c> set while
    /// <c>ExtendsContentIntoTitleBar</c> stays false would otherwise emit one
    /// release-visible warning per unrelated field change.
    /// </summary>
    internal class TitleBarHeightWarningIsEdgeTriggered(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.TitleBarHeightNotExtendedWarningCountForTests = 0;
            // TitleBarHeight set while content-extension is explicitly false is
            // the invalid combination the warning exists for.
            var spec = new WindowSpec
            {
                Title = "TBH 0",
                Width = 320,
                Height = 240,
                TitleBarHeight = WindowTitleBarHeight.Tall,
                ExtendsContentIntoTitleBar = false,
            };
            var win = await OpenAndSettle(spec, () => new FixedContent(200, 120));
            try
            {
                var afterOpen = ReactorWindow.TitleBarHeightNotExtendedWarningCountForTests;
                H.Check("TitleBarHeight_WarnedOnEntry", afterOpen >= 1);

                // Change only an unrelated field, repeatedly. The invalid
                // combination persists unchanged, so it must not re-warn.
                for (var i = 1; i <= 4; i++)
                {
                    win.Update(spec with { Title = $"TBH {i}" });
                    await Harness.Render(30);
                }
                H.Check("TitleBarHeight_NoRewarnPerUnrelatedUpdate",
                    ReactorWindow.TitleBarHeightNotExtendedWarningCountForTests == afterOpen);

                // Removing the height clears the invalid combination. It was
                // never applied (invalid), so this takes ApplyTitleBarHeight's
                // "never declared" early return — the path that used to leave
                // the latch set.
                win.Update(spec with { Title = "TBH none", TitleBarHeight = null });
                await Harness.Render(30);
                H.Check("TitleBarHeight_RemovalDoesNotWarn",
                    ReactorWindow.TitleBarHeightNotExtendedWarningCountForTests == afterOpen);

                // Re-declaring it is a new invalid state and must warn again.
                win.Update(spec with { Title = "TBH again" });
                await Harness.Render(30);
                H.Check("TitleBarHeight_RedeclareWarnsAgain",
                    ReactorWindow.TitleBarHeightNotExtendedWarningCountForTests == afterOpen + 1);
            }
            finally { ReactorWindow.TitleBarHeightNotExtendedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// The no-drag-affordance warning must fire on entering the condition, not on
    /// every <c>Update</c>. <c>Update</c> validates ahead of its own equality
    /// check, so an app holding a chromeless, non-draggable window while changing
    /// any unrelated field would otherwise emit one release-visible warning per
    /// update for the life of the window.
    /// </summary>
    internal class NoDragAffordanceWarningIsEdgeTriggered(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            WindowSpec.NoDragAffordanceWarningCountForTests = 0;
            var spec = new WindowSpec
            {
                Title = "No drag 0",
                Width = 320,
                Height = 240,
                Style = WindowStyle.None,
                IsMovableByBackground = false,
            };
            var win = await OpenAndSettle(spec, () => new FixedContent(200, 120));
            try
            {
                // Construction validates once, which is the entering edge.
                H.Check("NoDrag_WarnedOnConstruction",
                    WindowSpec.NoDragAffordanceWarningCountForTests == 1);

                // Change only an unrelated field, repeatedly. The suspicious
                // combination persists, so it must not re-warn.
                for (var i = 1; i <= 4; i++)
                {
                    win.Update(spec with { Title = $"No drag {i}" });
                    await Harness.Render(30);
                }
                H.Check("NoDrag_NoRewarnPerUpdate",
                    WindowSpec.NoDragAffordanceWarningCountForTests == 1);

                // Clearing the condition re-arms it...
                win.Update(spec with { Title = "movable", IsMovableByBackground = true });
                await Harness.Render(30);
                H.Check("NoDrag_ClearedWithoutWarning",
                    WindowSpec.NoDragAffordanceWarningCountForTests == 1);

                // ...so entering it again is a new edge and warns once more.
                win.Update(spec with { Title = "no drag again", IsMovableByBackground = false });
                await Harness.Render(30);
                H.Check("NoDrag_ReentryWarnsAgain",
                    WindowSpec.NoDragAffordanceWarningCountForTests == 2);
            }
            finally { WindowSpec.NoDragAffordanceWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }
    /// <summary>
    /// Turning size-to-content off and on again while maximized must warn for
    /// each ignored spell.
    ///
    /// <para>
    /// The re-arm lives in <c>AttachSizeToContentRoot</c>'s Manual branch, and
    /// <c>Update</c> reaches it deterministically: switching to Manual makes the
    /// spec unequal, so <c>ApplyChrome</c> runs and calls
    /// <c>OnHostContentRendered</c> (<c>ReactorWindow.cs</c> ~line 641), which
    /// re-attaches. No render is required. This pins that chain — if the Manual
    /// branch stops re-arming, the second spell goes unreported.
    /// </para>
    /// </summary>
    internal class SizeToContentMaximizedWarningRearmsAfterManual(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0;
            var spec = new WindowSpec { Title = "STC Manual", Width = 360, Height = 240 };
            var win = await OpenAndSettle(spec, () => new FixedContent(500, 400));
            try
            {
                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);

                var enabled = spec with { SizeToContent = WindowSizeToContent.WidthAndHeight };
                win.Update(enabled);
                await Harness.Render(120);
                H.Check("STCManual_WarnedOnFirstSpell",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 1);

                // Off, then on again — a second ignored spell.
                win.Update(spec with { Title = "STC Manual off", SizeToContent = WindowSizeToContent.Manual });
                win.Update(enabled with { Title = "STC Manual on again" });
                win.ApplySizeToContentForTests();

                H.Check("STCManual_StillMaximized", Native.IsZoomed(Hwnd(win)));
                H.Check("STCManual_RearmedAfterManual",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 2);
            }
            finally { ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// The maximized warning must stay latched across a root replacement.
    /// <c>OnHostContentRendered</c> runs per render and
    /// <c>AttachSizeToContentRoot</c> detaches whenever the root instance
    /// differs, then re-applies immediately — so re-arming the edge on detach
    /// would emit one release-visible warning per render for an app that
    /// alternates root controls while maximized, which is the unbounded ETW
    /// stream the edge-trigger exists to prevent.
    /// </summary>
    internal class SizeToContentMaximizedWarningSurvivesRootSwap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0;
            var spec = new WindowSpec { Title = "STC Max Swap", Width = 360, Height = 240 };
            SwappableRootContent? content = null;
            var win = await OpenAndSettle(spec, () => content = new SwappableRootContent());
            try
            {
                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);
                win.Update(spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(120);

                H.Check("SizeToContent_RootSwap_Maximized", Native.IsZoomed(Hwnd(win)));
                var afterEnable = ReactorWindow.SizeToContentMaximizedWarningCountForTests;
                H.Check("SizeToContent_RootSwap_WarnedOnce", afterEnable == 1);

                // Swap the root control type several times while still maximized.
                for (var i = 0; i < 3; i++)
                {
                    content!.Swap!();
                    await Harness.Render(120);
                }

                H.Check("SizeToContent_RootSwap_StillMaximized", Native.IsZoomed(Hwnd(win)));
                // Still exactly one: the spell never ended, so no re-arm.
                H.Check("SizeToContent_RootSwap_NoRewarnPerRender",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 1);
            }
            finally { ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// The maximized-warning edge must also survive the root rendering to
    /// *nothing*. <c>Empty()</c> reconciles to a null control, which reaches
    /// <c>AttachSizeToContentRoot</c> with <c>root is null</c> while
    /// <c>SizeToContent</c> is still enabled — that is a detach, not the end of
    /// the ignored-while-maximized spell, so it must not re-arm. Re-arming there
    /// lets an app that alternates <c>Empty()</c> with a real root emit one
    /// release-visible warning per render for a single maximized spell.
    /// </summary>
    internal class SizeToContentMaximizedWarningSurvivesEmptyRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0;
            var spec = new WindowSpec { Title = "STC Max Empty", Width = 360, Height = 240 };
            VanishingRootContent? content = null;
            var win = await OpenAndSettle(spec, () => content = new VanishingRootContent());
            try
            {
                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);
                win.Update(spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(120);

                H.Check("SizeToContent_EmptyRoot_Maximized", Native.IsZoomed(Hwnd(win)));
                H.Check("SizeToContent_EmptyRoot_WarnedOnce",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 1);

                // Vanish and restore the root repeatedly, still maximized and
                // still size-to-content. Each restore re-enters the attach path
                // with a real root; each vanish enters it with null.
                for (var i = 0; i < 3; i++)
                {
                    content!.Toggle!();   // -> Empty(), null root
                    await Harness.Render(120);
                    content!.Toggle!();   // -> real root again
                    await Harness.Render(120);
                }

                H.Check("SizeToContent_EmptyRoot_StillMaximized", Native.IsZoomed(Hwnd(win)));
                // The spell never ended, so the latch must still be held.
                H.Check("SizeToContent_EmptyRoot_NoRewarnPerRender",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 1);
            }
            finally { ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// The mirror of <see cref="SizeToContentMaximizedWarningSurvivesEmptyRoot"/>:
    /// preserving the latch across a null root must not let it survive a genuine
    /// *new* maximized spell. With no root attached, the SizeChanged/LayoutUpdated
    /// handlers are gone and <c>ApplySizeToContent</c> returns at its null-root
    /// guard, so nothing on the content path can observe a restore. The window-state
    /// observer (<c>OnAppWindowChanged</c>) is root-independent and must re-arm, or
    /// the next maximized spell is silently suppressed — a false negative.
    /// </summary>
    internal class SizeToContentMaximizedWarningRearmsAfterRestoreWithNullRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0;
            var spec = new WindowSpec { Title = "STC Max NullRoot Restore", Width = 360, Height = 240 };
            VanishingRootContent? content = null;
            var win = await OpenAndSettle(spec, () => content = new VanishingRootContent());
            try
            {
                // Spell 1: maximized with a real root -> warns once.
                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);
                win.Update(spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(120);
                H.Check("SizeToContent_NullRootRestore_WarnedFirstSpell",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 1);

                // Drop the root, so no content-path observer remains.
                content!.Toggle!();
                await Harness.Render(120);

                // Restore and re-maximize entirely while the root is null. This is
                // the transition only OnAppWindowChanged can see.
                Native.ShowWindow(Hwnd(win), Native.SW_RESTORE);
                await Harness.WaitFor(() => !Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);
                H.Check("SizeToContent_NullRootRestore_Restored", !Native.IsZoomed(Hwnd(win)));

                Native.ShowWindow(Hwnd(win), Native.SW_MAXIMIZE);
                await Harness.WaitFor(() => Native.IsZoomed(Hwnd(win)), maxPasses: 10, perPassMs: 30);

                // Bring a real root back: spell 2 must report.
                content!.Toggle!();
                await Harness.Render(120);

                H.Check("SizeToContent_NullRootRestore_StillMaximized", Native.IsZoomed(Hwnd(win)));
                H.Check("SizeToContent_NullRootRestore_WarnedSecondSpell",
                    ReactorWindow.SizeToContentMaximizedWarningCountForTests == 2);
            }
            finally { ReactorWindow.SizeToContentMaximizedWarningCountForTests = 0; await CloseAndSettle(win); }
        }
    }

    internal class SizeToContentAspectRatioBothRejected(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            EnsureUIDispatcher();
            bool threw = false;
            try { new WindowSpec { AspectRatio = 1.5, SizeToContent = WindowSizeToContent.WidthAndHeight }.Validate(); }
            catch (ArgumentException) { threw = true; }
            H.Check("SizeToContent_AspectRatio_BothRejected", threw);
            return Task.CompletedTask;
        }
    }

    internal class SizeToContentNoReentrancy(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC Reentrancy",
                Width = 240,
                Height = 160,
                SizeToContent = WindowSizeToContent.WidthAndHeight,
            }, () => new FixedContent(420, 320));
            try
            {
                await Harness.Render(160);
                H.Check("SizeToContent_NoReentrancy_Applied", win.SizeToContentApplyCountForTests >= 1);
                H.Check("SizeToContent_NoReentrancy_SingleResize", win.SizeToContentApplyCountForTests == 1);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private static ReactorWindow.MINMAXINFO InitialMinMaxInfo() => new()
    {
        ptMinTrackSize = new ReactorWindow.POINT { X = 1, Y = 2 },
        ptMaxTrackSize = new ReactorWindow.POINT { X = 10000, Y = 10001 },
    };

    internal class SizeToContentMinMaxInfoSuite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "STC MinMax Suite",
                Width = 240,
                Height = 180,
                SizeToContent = WindowSizeToContent.Width,
            }, () => new FixedContent(360, 300));
            try
            {
                var info = InitialMinMaxInfo();
                win.ApplyMinMaxInfoForTests(ref info);
                H.Check("SizeToContent_MinMaxInfoClampsWidth_X", info.ptMinTrackSize.X == info.ptMaxTrackSize.X && info.ptMinTrackSize.X > 1);
                H.Check("SizeToContent_MinMaxInfoClampsWidth_YUnchanged", info.ptMinTrackSize.Y == 2 && info.ptMaxTrackSize.Y == 10001);

                win.Update(win.Spec with { SizeToContent = WindowSizeToContent.Height });
                await Harness.Render(60);
                info = InitialMinMaxInfo();
                win.ApplyMinMaxInfoForTests(ref info);
                H.Check("SizeToContent_MinMaxInfoClampsHeight_Y", info.ptMinTrackSize.Y == info.ptMaxTrackSize.Y && info.ptMinTrackSize.Y > 2);
                H.Check("SizeToContent_MinMaxInfoClampsHeight_XUnchanged", info.ptMinTrackSize.X == 1 && info.ptMaxTrackSize.X == 10000);

                win.Update(win.Spec with { SizeToContent = WindowSizeToContent.WidthAndHeight });
                await Harness.Render(60);
                info = InitialMinMaxInfo();
                win.ApplyMinMaxInfoForTests(ref info);
                H.Check("SizeToContent_MinMaxInfoClampsBoth_X", info.ptMinTrackSize.X == info.ptMaxTrackSize.X && info.ptMinTrackSize.X > 1);
                H.Check("SizeToContent_MinMaxInfoClampsBoth_Y", info.ptMinTrackSize.Y == info.ptMaxTrackSize.Y && info.ptMinTrackSize.Y > 2);
            }
            finally { await CloseAndSettle(win); await CollectWindowResources(); }
        }
    }

    private static class Native
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int SW_MAXIMIZE = 3;
        public const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustWindowRectExForDpi(ref RECT lpRect, uint dwStyle,
            [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle, uint dpi);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(nint hWnd);
    }
}
