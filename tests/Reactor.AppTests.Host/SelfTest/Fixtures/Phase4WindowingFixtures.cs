using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 4 fixtures for style, DWM corner preference, and z-order level.</summary>
internal static class Phase4WindowingFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class StubComponent : Component
    {
        public override Element Render() => TextBlock("ok");
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec)
    {
        var win = ReactorApp.OpenWindow(spec, () => new StubComponent());
        await win.Host.WaitForIdleAsync();
        await Harness.Render(80);
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

    private static nint Hwnd(ReactorWindow win) => WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
    private static long StyleBits(ReactorWindow win) => (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_STYLE);
    private static long ExStyleBits(ReactorWindow win) => (long)Native.GetWindowLongPtr(Hwnd(win), Native.GWL_EXSTYLE);

    private static bool IsAbove(ReactorWindow upper, ReactorWindow lower)
    {
        nint target = Hwnd(lower);
        for (nint current = Hwnd(upper); current != 0; current = Native.GetWindow(current, Native.GW_HWNDNEXT))
        {
            if (current == target) return true;
        }
        return false;
    }

    internal class WindowStyleNoneBorderless(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "Style None",
                Width = 260,
                Height = 180,
                Style = WindowStyle.None,
                IsMovableByBackground = true,
            });
            try
            {
                bool settled = await Harness.WaitFor(() =>
                {
                    long bits = StyleBits(win);
                    return (bits & (Native.WS_BORDER | Native.WS_CAPTION | Native.WS_SYSMENU)) == 0;
                }, maxPasses: 20, perPassMs: 20);

                long bits = StyleBits(win);
                H.Check("WindowStyle_None_NoBorder", settled && (bits & Native.WS_BORDER) == 0);
                H.Check("WindowStyle_None_NoCaption", settled && (bits & Native.WS_CAPTION) == 0);
                H.Check("WindowStyle_None_NoSysMenu", settled && (bits & Native.WS_SYSMENU) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowStyleToolWindowHidesTaskbar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "ToolWindow Default", Width = 260, Height = 180, Style = WindowStyle.ToolWindow });
            try
            {
                long bits = ExStyleBits(win);
                H.Check("WindowStyle_ToolWindow_HidesTaskbar_ToolBit", (bits & Native.WS_EX_TOOLWINDOW) != 0);
                H.Check("WindowStyle_ToolWindow_HidesTaskbar_NoAppBit", (bits & Native.WS_EX_APPWINDOW) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowStyleToolWindowRespectsExplicitShowInTaskbar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec
            {
                Title = "ToolWindow Explicit",
                Width = 260,
                Height = 180,
                Style = WindowStyle.ToolWindow,
                ShowInTaskbar = true,
            });
            try
            {
                long bits = ExStyleBits(win);
                H.Check("WindowStyle_ToolWindow_Explicit_AppBit", (bits & Native.WS_EX_APPWINDOW) != 0);
                H.Check("WindowStyle_ToolWindow_Explicit_NoToolBit", (bits & Native.WS_EX_TOOLWINDOW) == 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowStyleRuntimeUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "Style Runtime", Width = 260, Height = 180, IsMovableByBackground = true };
            var win = await OpenAndSettle(spec);
            try
            {
                win.Update(spec with { Style = WindowStyle.None });
                bool noneSettled = await Harness.WaitFor(() =>
                {
                    long bits = StyleBits(win);
                    return (bits & (Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_BORDER)) == 0;
                }, maxPasses: 20, perPassMs: 20);
                long none = StyleBits(win);
                H.Check("WindowStyle_RuntimeUpdate_None", noneSettled && (none & (Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_BORDER)) == 0);

                win.Update(spec with { Style = WindowStyle.Default });
                bool caption = await Harness.WaitFor(
                    () => (StyleBits(win) & Native.WS_CAPTION) != 0, maxPasses: 12, perPassMs: 10);
                long normal = StyleBits(win);
                H.Check("WindowStyle_RuntimeUpdate_DefaultCaption", caption);
                H.Check("WindowStyle_RuntimeUpdate_DefaultSysMenu", (normal & Native.WS_SYSMENU) != 0);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class CornerStyleApply(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            if (Environment.OSVersion.Version.Build < 22000)
            {
                H.Skip("CornerStyle_Apply", "DWM corner preference is only round-trippable on Windows 11+");
                return;
            }

            var cases = new[]
            {
                (WindowCornerStyle.Default, DwmInterop.DWMWCP_DEFAULT, "Default"),
                (WindowCornerStyle.Square, DwmInterop.DWMWCP_DONOTROUND, "Square"),
                (WindowCornerStyle.Rounded, DwmInterop.DWMWCP_ROUND, "Rounded"),
                (WindowCornerStyle.RoundedSmall, DwmInterop.DWMWCP_ROUNDSMALL, "RoundedSmall"),
            };

            foreach (var (style, expected, name) in cases)
            {
                var win = await OpenAndSettle(new WindowSpec { Title = $"Corner {name}", Width = 240, Height = 160, CornerStyle = style });
                try
                {
                    int actual;
                    int hr = DwmInterop.DwmGetWindowAttribute(Hwnd(win), DwmInterop.DWMWA_WINDOW_CORNER_PREFERENCE, out actual, sizeof(int));
                    if (hr != 0)
                        H.Skip($"CornerStyle_Apply_{name}", $"DwmGetWindowAttribute failed: 0x{hr:X8}");
                    else
                        H.Check($"CornerStyle_Apply_{name}", actual == expected);
                }
                finally { await CloseAndSettle(win); }
            }
        }
    }

    internal class WindowLevelAlwaysOnTopStyleBitSet(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Topmost", Width = 260, Height = 180, Level = WindowLevel.AlwaysOnTop });
            try { H.Check("WindowLevel_AlwaysOnTop_StyleBitSet", (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0); }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class WindowLevelFloatingAboveSiblings(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow? normal = null;
            ReactorWindow? floating = null;
            try
            {
                normal = await OpenAndSettle(new WindowSpec { Title = "Normal Sibling", Width = 260, Height = 180 });
                floating = await OpenAndSettle(new WindowSpec { Title = "Floating Sibling", Width = 260, Height = 180, Level = WindowLevel.Floating });
                normal.Activate();
                bool above = await Harness.WaitFor(() => IsAbove(floating, normal), maxPasses: 10, perPassMs: 40);
                H.Check("WindowLevel_Floating_AboveSiblings", above);
            }
            finally { await CloseAndSettle(floating, normal); }
        }
    }

    internal class WindowLevelFloatingAboveOwner(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            ReactorWindow? owner = null;
            ReactorWindow? floating = null;
            try
            {
                owner = await OpenAndSettle(new WindowSpec { Title = "Owner", Width = 260, Height = 180 });
                floating = await OpenAndSettle(new WindowSpec { Title = "Owned Floating", Width = 240, Height = 160, Owner = owner, Level = WindowLevel.Floating });
                owner.Activate();
                bool above = await Harness.WaitFor(() => IsAbove(floating, owner), maxPasses: 10, perPassMs: 40);
                H.Check("WindowLevel_Floating_AboveOwner", above);
            }
            finally { await CloseAndSettle(floating, owner); }
        }
    }

    /// <summary>
    /// Issue #1707. Windows can silently refuse a runtime <c>HWND_TOPMOST</c> while another
    /// process owns the foreground, which only reproduces in a locked or background session, so
    /// the refusal is simulated with <see cref="ReactorWindow.RefuseTopmostForTests"/>: the apply
    /// skips <c>SetWindowPos</c> and leaves the bit clear, the same observable state. Reactor must
    /// warn once per refusal and reapply the level when the app gains the foreground
    /// (<c>WM_ACTIVATEAPP</c>) or any of its windows activates, including the refused one.
    /// </summary>
    internal class WindowLevelRefusedTopmostReapplied(Harness h) : SelfTestFixtureBase(h)
    {
        private static bool TopmostBit(ReactorWindow win) => (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0;

        private static int Warnings => Volatile.Read(ref ReactorWindow.TopmostRefusedWarningCountForTests);

        // Drops win back to Normal (a real, never-refused HWND_NOTOPMOST), then requests
        // AlwaysOnTop with the next apply refused. True when the refusal took.
        private static async Task<bool> RefuseAlwaysOnTop(ReactorWindow win, WindowSpec spec)
        {
            win.Update(spec with { Level = WindowLevel.Normal });
            await win.Host.WaitForIdleAsync();
            win.RefuseTopmostForTests = 1;
            win.Update(spec with { Level = WindowLevel.AlwaysOnTop });
            await win.Host.WaitForIdleAsync();
            return win.RefuseTopmostForTests == 0 && !TopmostBit(win);
        }

        // The reapply is a real SetWindowPos, so in a locked or background session Windows can
        // refuse it too. Skip only when a direct call is refused as well and another process
        // owns the foreground, as WindowLevel_RuntimeFlip does (issue #927).
        private void CheckReapplied(string name, ReactorWindow win, bool reapplied)
        {
            if (!reapplied && !OsGrantsTopmost(win, out bool foregroundIsForeign) && foregroundIsForeign)
            {
                H.Skip(name, "issue #927: Windows refused HWND_TOPMOST while another process owned the foreground");
                return;
            }
            H.Check(name, reapplied);
        }

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "Refused Topmost", Width = 260, Height = 180 };
            ReactorWindow? win = null;
            ReactorWindow? other = null;
            ReactorWindow? third = null;
            try
            {
                win = await OpenAndSettle(spec);
                other = await OpenAndSettle(new WindowSpec { Title = "Refused Topmost Other", Width = 260, Height = 180 });
                other.Activate();
                bool winInactive = await Harness.WaitFor(() => !win.IsActive && other.IsActive, maxPasses: 25, perPassMs: 40);

                int warningsBefore = Warnings;
                win.RefuseTopmostForTests = 1;
                win.Update(spec with { Level = WindowLevel.AlwaysOnTop });
                await win.Host.WaitForIdleAsync();
                H.Check("WindowLevel_RefusedTopmost_Simulated", win.RefuseTopmostForTests == 0 && !TopmostBit(win));
                H.Check("WindowLevel_RefusedTopmost_Warns", Warnings == warningsBefore + 1);

                // An unrelated spec change re-applies the level. Still refused, it must not warn again.
                win.RefuseTopmostForTests = 1;
                win.Update(spec with { Level = WindowLevel.AlwaysOnTop, Title = "Refused Topmost Retitled" });
                await win.Host.WaitForIdleAsync();
                H.Check("WindowLevel_RefusedTopmost_WarnsOnce",
                    win.RefuseTopmostForTests == 0 && !TopmostBit(win) && Warnings == warningsBefore + 1);

                // The app gaining the foreground reaches every top-level window as WM_ACTIVATEAPP,
                // even when no Reactor window activates. Sending it doesn't activate win.
                _ = Native.SendMessage(Hwnd(win), Native.WM_ACTIVATEAPP, 1, 0);
                CheckReapplied("WindowLevel_RefusedTopmost_ReappliedOnActivateApp", win, TopmostBit(win));

                // Once granted, the warning re-arms: the next refusal warns again.
                bool refusedAgain = await RefuseAlwaysOnTop(win, spec);
                H.Check("WindowLevel_RefusedTopmost_WarningRearms", refusedAgain && Warnings == warningsBefore + 2);

                // Another window activating reapplies the refused one, which stays inactive.
                // win is still refused here, and opening `third` activates it.
                third = await OpenAndSettle(new WindowSpec { Title = "Refused Topmost Third", Width = 260, Height = 180 });
                bool thirdActive = await Harness.WaitFor(() => third.IsActive, maxPasses: 25, perPassMs: 40);
                bool otherReapplied = await Harness.WaitFor(() => TopmostBit(win), maxPasses: 25, perPassMs: 40);
                if (!winInactive || !thirdActive || win.IsActive)
                {
                    H.Skip("WindowLevel_RefusedTopmost_ReappliedOnOtherWindowActivation",
                        $"no clean activation edge on another window (other={winInactive}, third={thirdActive}, win active={win.IsActive})");
                }
                else
                {
                    CheckReapplied("WindowLevel_RefusedTopmost_ReappliedOnOtherWindowActivation", win, otherReapplied);
                }

                // The refused window's own activation reapplies it too.
                bool refusedForSelf = await RefuseAlwaysOnTop(win, spec);
                win.Activate();
                bool activated = await Harness.WaitFor(() => win.IsActive, maxPasses: 25, perPassMs: 40);
                bool selfReapplied = await Harness.WaitFor(() => TopmostBit(win), maxPasses: 25, perPassMs: 40);
                if (!refusedForSelf || !activated)
                {
                    H.Skip("WindowLevel_RefusedTopmost_ReappliedOnActivation",
                        $"no clean self-activation edge (refused={refusedForSelf}, activated={activated})");
                }
                else
                {
                    CheckReapplied("WindowLevel_RefusedTopmost_ReappliedOnActivation", win, selfReapplied);
                }
            }
            finally { await CloseAndSettle(third, other, win); }
        }
    }

    internal class WindowLevelRuntimeFlip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "Level Runtime", Width = 260, Height = 180 };
            var win = await OpenAndSettle(spec);
            try
            {
                // Issue #927. Windows can refuse a runtime HWND_TOPMOST: SetWindowPos returns TRUE,
                // but WM_WINDOWPOSCHANGING arrives with the insert-after rewritten to a non-topmost
                // window the test window is already below, so nothing moves and the bit stays
                // clear. Measured with the workstation locked (LockApp in the foreground) late in
                // long local full runs. An immediate retry, a raw SetWindowPos without
                // SWP_NOACTIVATE, HWND_TOP followed by HWND_TOPMOST, and
                // OverlappedPresenter.IsAlwaysOnTop all fail the same way, so it is OS policy
                // rather than Reactor or WinUI. A freshly launched process in the same session
                // still got topmost, which matches the fixture never failing in isolation. It has
                // not been seen in CI. When the bit doesn't follow, the fixture issues the same
                // call itself. It skips only when that is refused too and another process owns
                // the foreground; any other mismatch fails. Reactor reapplies a refused level on
                // the app's next activation (#1707, covered by WindowLevelRefusedTopmostReapplied),
                // which this fixture doesn't wait for.
                //
                // Earlier, refuted hypotheses: a too-short wait, the secondary window's host not
                // being awaited (a real gap, fixed below), and Z-order churn from leaked Floating
                // windows, which the population probe below still rules out on every run.
                var allWindows = ReactorApp.Windows;
                int registeredFloatingSpecs = 0;
                for (int i = 0; i < allWindows.Count; i++)
                    if (allWindows[i].Spec.Level == WindowLevel.Floating) registeredFloatingSpecs++;
                Console.WriteLine(
                    $"# WindowLevel_RuntimeFlip population (issue #927): ReactorApp.Windows={allWindows.Count}, " +
                    $"registered Spec.Level==Floating={registeredFloatingSpecs} " +
                    $"(disposal NOT checked — see below)");
                // Deliberately a SUPERSET of what actually churns Z-order:
                // ReassertWindowLevelsForActivation additionally skips `_disposed`, which is
                // private on ReactorWindow and unreachable from here even with InternalsVisibleTo.
                // An over-count of 0 implies the participating count is also 0. Only the Floating
                // count is asserted, not the total window count: total would red on any unrelated
                // fixture's leak and misattribute it to this fixture.
                H.Check("WindowLevel_RuntimeFlip_NoRegisteredFloatingWindows", registeredFloatingSpecs == 0);

                win.Update(spec with { Level = WindowLevel.AlwaysOnTop });
                // Harness.Render only awaits the primary window's host, so await this one's.
                await win.Host.WaitForIdleAsync();
                bool topmost = await Harness.WaitFor(
                    () => (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0,
                    maxPasses: 25, perPassMs: 40);
                if (!topmost)
                {
                    // Read before the probe below, which sets the bit when the OS allows it.
                    bool bitSet = (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0;
                    bool specTookUpdate = win.Spec.Level == WindowLevel.AlwaysOnTop;
                    bool osGranted = true, foregroundIsForeign = false;
                    if (!bitSet) osGranted = OsGrantsTopmost(win, out foregroundIsForeign);
                    if (ShouldSkipRefusedTopmost(specTookUpdate, bitSet, osGranted, foregroundIsForeign))
                    {
                        const string reason =
                            "issue #927: Windows refused HWND_TOPMOST while another process owned the " +
                            "foreground (a direct SetWindowPos was refused too), so the runtime flip " +
                            "can't be observed";
                        H.Skip("WindowLevel_RuntimeFlip_Topmost", reason);
                        // The bit never got set, so the Normal half would pass without testing anything.
                        H.Skip("WindowLevel_RuntimeFlip_Normal", reason);
                        return;
                    }
                    ReportLevelMismatch(win, WindowLevel.AlwaysOnTop, expectTopmostBit: true, specTookUpdate, bitSet);
                }
                H.Check("WindowLevel_RuntimeFlip_Topmost", topmost);

                win.Update(spec with { Level = WindowLevel.Normal });
                await win.Host.WaitForIdleAsync();
                bool normal = await Harness.WaitFor(
                    () => (ExStyleBits(win) & Native.WS_EX_TOPMOST) == 0,
                    maxPasses: 25, perPassMs: 40);
                if (!normal)
                {
                    ReportLevelMismatch(win, WindowLevel.Normal, expectTopmostBit: false,
                        specTookUpdate: win.Spec.Level == WindowLevel.Normal,
                        bitSet: (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0);
                }
                H.Check("WindowLevel_RuntimeFlip_Normal", normal);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// Pure verdict selection for <see cref="ReportLevelMismatch"/>, split out so the three-way
    /// branch is reachable without a real failure. Returns a short
    /// stable tag plus the guidance text; the tag is what a test can assert on without pinning
    /// the prose.
    ///
    /// <para>The assertion in the fixture reads only the native ex-style bit, which cannot by
    /// itself distinguish three different bugs. Combining <c>win.Spec.Level</c> with a re-read of
    /// the bit after the poll gave up separates them:</para>
    /// <list type="bullet">
    /// <item><description><b>spec stale</b> — the Update never reached Reactor's own state, so
    /// the fault is upstream of the native apply entirely.</description></item>
    /// <item><description><b>spec updated, bit still wrong</b> — Reactor accepted it but
    /// SetWindowPos was never issued, or was issued with the wrong insert-after.</description></item>
    /// <item><description><b>spec updated, bit now CORRECT</b> — it landed after the poll
    /// exhausted. That is a lost wakeup / late apply, not a missing one, and it is the one case a
    /// longer budget would actually have fixed.</description></item>
    /// </list>
    /// </summary>
    internal static (string Tag, string Text) LevelMismatchVerdict(bool specTookUpdate, bool bitNowCorrect)
    {
        if (!specTookUpdate)
            return ("spec-stale",
                "Reactor's spec did NOT take the update — the failure is upstream of the " +
                "native apply, in Update delivery/reconciliation, not in SetWindowPos.");

        if (bitNowCorrect)
            return ("late-apply",
                "The bit is CORRECT now, so the apply landed after the poll exhausted — a late " +
                "apply / lost wakeup rather than a missing one. This is the only branch a longer " +
                "budget would fix.");

        return ("apply-missing",
            "Reactor's spec DID take the update, but the native bit still has not followed — " +
            "Reactor's SetWindowPos apply is the suspect (never issued, or issued with the wrong " +
            "insert-after). For the Topmost half, the fixture's own SetWindowPos, issued after the " +
            "poll, did take, or no foreign foreground owner could be confirmed.");
    }

    /// <summary>
    /// Emits a TAP comment carrying <see cref="LevelMismatchVerdict"/>'s diagnosis, so the next
    /// occurrence answers the question instead of costing another investigation.
    ///
    /// <para>Written as a TAP comment rather than folded into the check name so
    /// <c>WindowLevel_RuntimeFlip_Topmost</c> stays greppable for flake tracking.</para>
    /// </summary>
    /// <param name="specTookUpdate">Whether <c>win.Spec.Level</c> equals <paramref name="requested"/>.</param>
    /// <param name="bitSet">
    /// WS_EX_TOPMOST re-read after the poll gave up and before any probe that could change it.
    /// If it is correct by then, the apply was merely late.
    /// </param>
    private static void ReportLevelMismatch(
        ReactorWindow win, WindowLevel requested, bool expectTopmostBit, bool specTookUpdate, bool bitSet)
    {
        var (tag, verdict) = LevelMismatchVerdict(
            specTookUpdate: specTookUpdate,
            bitNowCorrect: bitSet == expectTopmostBit);

        Console.WriteLine(
            $"# WindowLevel_RuntimeFlip diagnostic (issue #927) [{tag}]: requested={requested}, " +
            $"spec.Level={win.Spec.Level}, WS_EX_TOPMOST={(bitSet ? "set" : "clear")}, " +
            $"expected {(expectTopmostBit ? "set" : "clear")}. {verdict}");
    }

    /// <summary>
    /// Pure skip-or-fail decision for the Topmost half once the poll gave up, split out so
    /// <see cref="WindowLevelVerdictBranches"/> can cover it without reproducing the refusal.
    /// Skips only when Reactor's spec took the update, the bit is still clear, the fixture's own
    /// SetWindowPos was refused too, and another process was positively identified as owning
    /// the foreground. Every other combination is a Reactor failure.
    /// </summary>
    internal static bool ShouldSkipRefusedTopmost(
        bool specTookUpdate, bool bitSet, bool osGranted, bool foregroundIsForeign)
        => specTookUpdate && !bitSet && !osGranted && foregroundIsForeign;

    /// <summary>
    /// True only when the foreground lookup succeeded and named a different, nonzero process.
    /// A null foreground window (common during activation changes or with no interactive
    /// foreground) or a failed <c>GetWindowThreadProcessId</c> is unknown, not foreign.
    /// </summary>
    internal static bool IsForeignForeground(nint foregroundHwnd, uint lookupThreadId, uint foregroundPid, uint selfPid)
        => foregroundHwnd != 0 && lookupThreadId != 0 && foregroundPid != 0 && foregroundPid != selfPid;

    /// <summary>
    /// Issues the same <c>SetWindowPos(HWND_TOPMOST)</c> Reactor does and reports whether the bit
    /// took. False means Windows refused the request for this process. Leaves the window topmost
    /// when it succeeds.
    /// </summary>
    /// <param name="foregroundIsForeign">See <see cref="IsForeignForeground"/>, measured at probe time.</param>
    private static bool OsGrantsTopmost(ReactorWindow win, out bool foregroundIsForeign)
    {
        nint foregroundHwnd = Native.ForegroundWindow();
        uint foregroundPid = 0;
        uint lookupThreadId = foregroundHwnd == 0
            ? 0
            : Native.WindowProcessId(foregroundHwnd, out foregroundPid);
        foregroundIsForeign = IsForeignForeground(
            foregroundHwnd, lookupThreadId, foregroundPid, (uint)Environment.ProcessId);
        nint hwnd = Hwnd(win);
        bool ok = Native.MakeTopmost(hwnd, out int error);
        bool granted = (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0;
        Console.WriteLine(
            $"# WindowLevel_RuntimeFlip OS probe (issue #927): direct SetWindowPos(HWND_TOPMOST) " +
            $"returned {ok} (error {error}), WS_EX_TOPMOST {(granted ? "set" : "still clear")}, " +
            $"foreground hwnd {foregroundHwnd} pid {foregroundPid} (foreign={foregroundIsForeign}), " +
            $"this pid {Environment.ProcessId}");
        return granted;
    }

    /// <summary>
    /// Exhaustively covers the three-way verdict in <see cref="LevelMismatchVerdict"/>, the
    /// skip decision in <see cref="ShouldSkipRefusedTopmost"/> and the foreground classification
    /// in <see cref="IsForeignForeground"/>. They only execute when
    /// <c>WindowLevel_RuntimeFlip</c> doesn't see the bit follow, so without this fixture they
    /// are never exercised in CI. The verdict checks cover the whole 2x2 input domain, and the
    /// skip checks pin the one skipping combination plus each single-input flip away from it.
    /// </summary>
    internal class WindowLevelVerdictBranches(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            H.Check("WindowLevelVerdict_SpecStale",
                LevelMismatchVerdict(specTookUpdate: false, bitNowCorrect: false).Tag == "spec-stale");

            // spec-stale must win even when the bit happens to read correct: if the update never
            // reached Reactor's state, the native apply is not the suspect regardless of the bit.
            H.Check("WindowLevelVerdict_SpecStaleWinsOverCorrectBit",
                LevelMismatchVerdict(specTookUpdate: false, bitNowCorrect: true).Tag == "spec-stale");

            H.Check("WindowLevelVerdict_LateApply",
                LevelMismatchVerdict(specTookUpdate: true, bitNowCorrect: true).Tag == "late-apply");

            H.Check("WindowLevelVerdict_ApplyMissing",
                LevelMismatchVerdict(specTookUpdate: true, bitNowCorrect: false).Tag == "apply-missing");

            H.Check("WindowLevelSkip_RefusedWhileBackgroundSkips",
                ShouldSkipRefusedTopmost(specTookUpdate: true, bitSet: false, osGranted: false, foregroundIsForeign: true));
            H.Check("WindowLevelSkip_SpecStaleFails",
                !ShouldSkipRefusedTopmost(specTookUpdate: false, bitSet: false, osGranted: false, foregroundIsForeign: true));
            H.Check("WindowLevelSkip_LateApplyFails",
                !ShouldSkipRefusedTopmost(specTookUpdate: true, bitSet: true, osGranted: false, foregroundIsForeign: true));
            H.Check("WindowLevelSkip_OsGrantedFails",
                !ShouldSkipRefusedTopmost(specTookUpdate: true, bitSet: false, osGranted: true, foregroundIsForeign: true));
            H.Check("WindowLevelSkip_RefusedWithoutForeignForegroundFails",
                !ShouldSkipRefusedTopmost(specTookUpdate: true, bitSet: false, osGranted: false, foregroundIsForeign: false));

            const uint self = 100, other = 200;
            H.Check("WindowLevelForeground_ForeignProcess",
                IsForeignForeground(foregroundHwnd: 1, lookupThreadId: 7, foregroundPid: other, selfPid: self));
            H.Check("WindowLevelForeground_SelfIsNotForeign",
                !IsForeignForeground(foregroundHwnd: 1, lookupThreadId: 7, foregroundPid: self, selfPid: self));
            H.Check("WindowLevelForeground_NullWindowIsUnknown",
                !IsForeignForeground(foregroundHwnd: 0, lookupThreadId: 0, foregroundPid: 0, selfPid: self));
            H.Check("WindowLevelForeground_FailedLookupIsUnknown",
                !IsForeignForeground(foregroundHwnd: 1, lookupThreadId: 0, foregroundPid: other, selfPid: self));
            H.Check("WindowLevelForeground_ZeroPidIsUnknown",
                !IsForeignForeground(foregroundHwnd: 1, lookupThreadId: 7, foregroundPid: 0, selfPid: self));

            return Task.CompletedTask;
        }
    }

    private static class Native
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_BORDER = 0x00800000;
        public const long WS_CAPTION = 0x00C00000;
        public const long WS_SYSMENU = 0x00080000;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_APPWINDOW = 0x00040000;
        public const long WS_EX_TOPMOST = 0x00000008;
        public const uint GW_HWNDNEXT = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly nint HWND_TOPMOST = -1;

        // Same call and flags as ReactorWindow.ApplyWindowLevel(AlwaysOnTop).
        public static bool MakeTopmost(nint hWnd, out int error)
        {
            bool ok = SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            error = ok ? 0 : Marshal.GetLastWin32Error();
            return ok;
        }

        public static nint ForegroundWindow() => GetForegroundWindow();

        public const uint WM_ACTIVATEAPP = 0x001C;

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern nint SendMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

        public static uint WindowProcessId(nint hWnd, out uint processId)
            => GetWindowThreadProcessId(hWnd, out processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        public static nint GetWindowLongPtr(nint hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint GetWindow(nint hWnd, uint uCmd);
    }
}
