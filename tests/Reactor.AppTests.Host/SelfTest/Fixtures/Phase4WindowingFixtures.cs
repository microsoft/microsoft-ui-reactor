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

    internal class WindowLevelRuntimeFlip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var spec = new WindowSpec { Title = "Level Runtime", Width = 260, Height = 180 };
            var win = await OpenAndSettle(spec);
            try
            {
                // NOT a settled fix — issue #927 remains open. Four hypotheses tested and
                // refuted on this fixture, recorded so nobody re-runs them:
                //   1. "the fixed 80ms wait is too tight" — replaced with WaitFor polling;
                //      still failed on a later full run.
                //   2. "the secondary window's host is never awaited" — a REAL gap, fixed
                //      below (Harness.Render only awaits ReactorApp.PrimaryWindow's host;
                //      OpenAndSettle awaits win.Host, the Update path did not). Still failed
                //      1 run in 4 afterwards with a 1.4s poll budget.
                //   3. "intrinsic to the fixture" — 20/20 clean run in isolation
                //      (`--self-test --filter WindowLevel_RuntimeFlip`).
                //   4. "the two preceding Floating fixtures leave topmost/Z-order state" —
                //      20/20 clean with them included (`--filter WindowLevel`).
                // So: ~25% in the full 6122-check run, 0/40 across two narrow scopes. What
                // remains is accumulated state from the hundreds of windows earlier fixtures
                // open and close, or full-run load/duration. Note (1) only rules out sampling
                // too early — an event that never fires, an ordering race decided before the
                // first poll, or a lost wakeup are all budget-insensitive, so "not a
                // too-short-poll problem" is the sound claim, NOT "not a race".
                // The await + poll below are kept because both are correct regardless, and
                // the assertion stays strict: it fails if the bit never flips.
                // Window-population probe for issue #927. Emitted on EVERY run so a full run and
                // a filtered run can be compared without waiting to catch the ~25% flake.
                //
                // RESULT: hypothesis FALSIFIED, and the probe is kept as the standing evidence.
                // ReactorWindow.ReassertFloatingWindowsForActivation iterates ReactorApp.Windows
                // on every activation and re-issues SetWindowPos(HWND_TOP) for every non-disposed
                // window whose Spec.Level is Floating — it skips only _disposed, so a Floating
                // window leaked by an earlier fixture would keep churning Z-order for the rest of
                // the process. That made "leaked Floating windows accumulate over the suite" a
                // strong candidate. Measured: `ReactorApp.Windows=1, registered Spec.Level==Floating=0` at this
                // point in BOTH a full ~6100-check run and `--filter WindowLevel`. Identical. So
                // there is no leak, no accumulated population, and the whole cross-window
                // coupling family is eliminated — including the version where the two preceding
                // Floating fixtures are to blame (also 20/20 clean with them included).
                //
                // What that leaves: ReactorWindow.ApplyWindowLevel discards the SetWindowPos
                // BOOL (`_ = NativeShell.SetWindowPos(...)`, ReactorWindow.cs:810), so a rejected
                // call is silently indistinguishable from one that never took effect. Checking
                // that return plus GetLastError is now the leading next step — it splits "the
                // call was rejected" from "something undid it afterwards", and neither is
                // currently observable.
                var allWindows = ReactorApp.Windows;
                int registeredFloatingSpecs = 0;
                for (int i = 0; i < allWindows.Count; i++)
                    if (allWindows[i].Spec.Level == WindowLevel.Floating) registeredFloatingSpecs++;
                Console.WriteLine(
                    $"# WindowLevel_RuntimeFlip population (issue #927): ReactorApp.Windows={allWindows.Count}, " +
                    $"registered Spec.Level==Floating={registeredFloatingSpecs} " +
                    $"(disposal NOT checked — see below)");
                // Deliberately a SUPERSET of what actually churns Z-order:
                // ReassertFloatingWindowsForActivation additionally skips `_disposed`, which is
                // private on ReactorWindow and unreachable from here even with InternalsVisibleTo.
                // So this counts registered Floating specs, disposed or not. That asymmetry is
                // safe in the direction that matters: an over-count of 0 implies the participating
                // count is also 0, so the falsification above holds a fortiori. It would only
                // mislead in the other direction — a non-zero reading here is a lead to
                // investigate, not proof that anything is still reasserting.
                //
                // Only the Floating count is asserted, not the total window count: total would red
                // on any unrelated fixture's leak and misattribute it to this fixture.
                H.Check("WindowLevel_RuntimeFlip_NoRegisteredFloatingWindows", registeredFloatingSpecs == 0);

                win.Update(spec with { Level = WindowLevel.AlwaysOnTop });
                await win.Host.WaitForIdleAsync();
                bool topmost = await Harness.WaitFor(
                    () => (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0,
                    maxPasses: 25, perPassMs: 40);
                if (!topmost) ReportLevelMismatch(win, WindowLevel.AlwaysOnTop, expectTopmostBit: true);
                H.Check("WindowLevel_RuntimeFlip_Topmost", topmost);

                win.Update(spec with { Level = WindowLevel.Normal });
                await win.Host.WaitForIdleAsync();
                bool normal = await Harness.WaitFor(
                    () => (ExStyleBits(win) & Native.WS_EX_TOPMOST) == 0,
                    maxPasses: 25, perPassMs: 40);
                if (!normal) ReportLevelMismatch(win, WindowLevel.Normal, expectTopmostBit: false);
                H.Check("WindowLevel_RuntimeFlip_Normal", normal);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// Pure verdict selection for <see cref="ReportLevelMismatch"/>, split out so the three-way
    /// branch is reachable without reproducing the ~25%-in-a-full-run flake. Returns a short
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
    /// SetWindowPos was coalesced away or never issued.</description></item>
    /// <item><description><b>spec updated, bit now CORRECT</b> — it landed after the poll
    /// exhausted. That is a lost wakeup / late apply, not a missing one, and it is the one case a
    /// longer budget would actually have fixed — worth knowing, because the 1.4s budget already
    /// failed once and that argues against this branch being the usual cause.</description></item>
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
                "budget would fix, and 1.4s already failed once, so treat a repeat here as a " +
                "signal to re-examine that.");

        return ("apply-missing",
            "Reactor's spec DID take the update, but the native bit still has not followed — " +
            "the SetWindowPos apply is the suspect (coalesced away, or never issued).");
    }

    /// <summary>
    /// Emits a TAP comment carrying <see cref="LevelMismatchVerdict"/>'s diagnosis, so the NEXT
    /// full-run occurrence answers the question instead of costing another investigation. This
    /// fixture only fails in the full ~6100-check run (0/40 across two narrow scopes), so a
    /// reproduction is expensive and the failure needs to carry its own diagnosis.
    ///
    /// <para>Written as a TAP comment rather than folded into the check name so
    /// <c>WindowLevel_RuntimeFlip_Topmost</c> stays greppable for flake tracking.</para>
    /// </summary>
    private static void ReportLevelMismatch(ReactorWindow win, WindowLevel requested, bool expectTopmostBit)
    {
        var specLevel = win.Spec.Level;
        // Re-read AFTER the poll gave up — if it is correct now, the apply was merely late.
        bool bitSet = (ExStyleBits(win) & Native.WS_EX_TOPMOST) != 0;
        var (tag, verdict) = LevelMismatchVerdict(
            specTookUpdate: specLevel == requested,
            bitNowCorrect: bitSet == expectTopmostBit);

        Console.WriteLine(
            $"# WindowLevel_RuntimeFlip diagnostic (issue #927) [{tag}]: requested={requested}, " +
            $"spec.Level={specLevel}, WS_EX_TOPMOST={(bitSet ? "set" : "clear")}, " +
            $"expected {(expectTopmostBit ? "set" : "clear")}. {verdict}");
    }

    /// <summary>
    /// Exhaustively covers the three-way verdict in <see cref="LevelMismatchVerdict"/>. That
    /// branch only executes when <c>WindowLevel_RuntimeFlip</c> actually fails — ~25% of full
    /// runs, and 0/40 under a filter — so without this fixture the diagnostic a future triager
    /// will act on is never exercised in CI. Four checks cover the whole 2x2 input domain, so
    /// collapsing any branch into another fails at least one of them.
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
