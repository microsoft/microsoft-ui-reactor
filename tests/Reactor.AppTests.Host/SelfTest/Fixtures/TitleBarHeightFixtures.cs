using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Windowing;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #917 — declarative title-bar height (<c>WindowSpec.TitleBarHeight</c> /
/// <c>TitleBar(...).Tall()</c>).
/// <para>
/// The oracle is <c>AppWindowTitleBar.Height</c> (real caption geometry: 32 px
/// standard vs 48 px tall at 100% scale) plus the mounted WinUI <c>TitleBar</c>
/// control's <c>ActualHeight</c> — never the <c>PreferredHeightOption</c>
/// readback, which reports the requested value whether or not it took effect.
/// Every check is differential: it compares against a standard-caption baseline
/// measured in the same run, so it fails if the apply path is removed.
/// </para>
/// <para>
/// Measured facts these fixtures pin down (see the spike in issue #917):
/// the WinUI TitleBar control does NOT follow the caption height, and the
/// native <c>PreferredHeightOption</c> setter throws <c>ERROR_INVALID_STATE</c>
/// on a window that is not content-extended.
/// </para>
/// </summary>
internal static class TitleBarHeightFixtures
{
    // DIP constants. AppWindowTitleBar.Height reports PHYSICAL PIXELS, so every
    // caption assertion converts through the window's DipScale — hard-coding 32/48
    // px would break above 100% scaling, and worse, a Standard caption at 150%
    // measures 48 px and would false-pass a hard-coded Tall check. The WinUI
    // TitleBar control's ActualHeight is already in DIPs and needs no conversion.
    private const int StandardCaptionDip = 32;
    private const int TallCaptionDip = 48;

    /// <summary>Expected physical caption height for a DIP height on this window.</summary>
    private static int CaptionPx(ReactorWindow win, int dip) =>
        (int)Math.Round(dip * win.DipScale, MidpointRounding.AwayFromZero);

    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class TitleBarComponent(Func<TitleBarElement, TitleBarElement> configure) : Component
    {
        public Microsoft.UI.Xaml.Controls.TitleBar? Bar;
        public override Element Render() =>
            VStack(configure(TitleBar("Height")).Set(b => Bar = b), TextBlock("body"));
    }

    private sealed class PlainBodyComponent : Component
    {
        public override Element Render() => VStack(TextBlock("body"));
    }

    /// <summary>
    /// A tall TitleBar whose explicit <c>.Height(...)</c> can be dropped at
    /// runtime, so the post-modifier fallback can be observed.
    /// </summary>
    private sealed class ToggleExplicitHeightComponent : Component
    {
        public Microsoft.UI.Xaml.Controls.TitleBar? Bar;
        public Action<bool>? SetExplicit;

        public override Element Render()
        {
            var (useExplicit, set) = UseState(true);
            SetExplicit = set;
            var bar = TitleBar("Height").Tall();
            if (useExplicit) bar = bar.Height(64);
            return VStack(bar.Set(b => Bar = b), TextBlock("body"));
        }
    }

    /// <summary>
    /// A tall TitleBar that can be removed from the tree at runtime, so the
    /// unmount withdrawal can be observed.
    /// </summary>
    private sealed class ToggleTitleBarComponent : Component
    {
        public Action<bool>? SetVisible;

        public override Element Render()
        {
            var (visible, set) = UseState(true);
            SetVisible = set;
            return visible
                ? VStack(TitleBar("Removable").Tall(), TextBlock("body"))
                : VStack(TextBlock("body"));
        }
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(150);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows.Where(w => w is not null))
        {
            // Best-effort teardown, matching the house pattern in
            // Phase4WindowingFixtures: a window may already be closing,
            // disposed, or mid-native-teardown (the WinUI TitleBar control
            // throws teardown-reentry COMExceptions — issue #537). Anything
            // escaping here would replace a real assertion result with a
            // teardown error. Reported to the diagnostic sink rather than
            // Console, which would interleave with the TAP stream.
            try { win!.Close(); }
            catch (Exception ex)
            {
                DiagnosticLog.SwallowedError(
                    LogCategory.Hosting, "SelfTest.TitleBarHeight.CloseAndSettle", ex);
            }
        }
        await Task.Delay(100);
    }

    private static void Report(string label, ReactorWindow win, Microsoft.UI.Xaml.Controls.TitleBar? bar) =>
        Console.WriteLine(
            $"# {label}: caption={win.AppWindow.TitleBar.Height}px (scale={win.DipScale:0.##}) "
            + $"control={bar?.ActualHeight.ToString("0.##") ?? "<null>"}dip");

    private static WindowSpec Spec(string title) =>
        new() { Title = title, Width = 420, Height = 260 };

    /// <summary>
    /// <c>TitleBar(...).Tall()</c> raises BOTH the system caption and the
    /// control. Differential against an unmodified TitleBar in the same run, so
    /// dropping either half of the apply fails the check.
    /// </summary>
    internal class TitleBarHeightElementTall(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            int baseCaption;
            double baseControl;
            var plain = new TitleBarComponent(static e => e);
            var winPlain = await OpenAndSettle(Spec("Standard caption"), () => plain);
            try
            {
                Report("standard", winPlain, plain.Bar);
                baseCaption = winPlain.AppWindow.TitleBar.Height;
                baseControl = plain.Bar?.ActualHeight ?? -1;
                H.Check("TitleBarHeight_StandardBaseline",
                    baseCaption == CaptionPx(winPlain, StandardCaptionDip)
                    && Math.Abs(baseControl - StandardCaptionDip) < 0.5);
            }
            finally { await CloseAndSettle(winPlain); }

            var tall = new TitleBarComponent(static e => e.Tall());
            var winTall = await OpenAndSettle(Spec("Tall caption"), () => tall);
            try
            {
                Report("tall", winTall, tall.Bar);
                H.Check("TitleBarHeight_ElementTall_RaisesCaption",
                    winTall.AppWindow.TitleBar.Height == CaptionPx(winTall, TallCaptionDip)
                    && winTall.AppWindow.TitleBar.Height != baseCaption);
                // The WinUI TitleBar control does not derive its height from the
                // caption — a tall caption over a 32 DIP control is the bug this
                // half of the feature exists to prevent.
                H.Check("TitleBarHeight_ElementTall_RaisesControl",
                    tall.Bar is { } bar
                    && Math.Abs(bar.ActualHeight - TallCaptionDip) < 0.5
                    && Math.Abs(bar.ActualHeight - baseControl) > 0.5);
            }
            finally { await CloseAndSettle(winTall); }
        }
    }

    /// <summary>
    /// <c>WindowSpec.TitleBarHeight</c> applies to a plain <c>TitleBar(...)</c>
    /// element, and an explicit spec value wins over the element's declaration
    /// for BOTH the caption and the control height.
    /// </summary>
    internal class TitleBarHeightSpecPrecedence(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var comp = new TitleBarComponent(static e => e);
            var win = await OpenAndSettle(
                Spec("Spec tall") with { TitleBarHeight = WindowTitleBarHeight.Tall }, () => comp);
            try
            {
                Report("specTall", win, comp.Bar);
                H.Check("TitleBarHeight_SpecTall_RaisesCaption",
                    win.AppWindow.TitleBar.Height == CaptionPx(win, TallCaptionDip));
                H.Check("TitleBarHeight_SpecTall_RaisesControl",
                    comp.Bar is { } bar && Math.Abs(bar.ActualHeight - TallCaptionDip) < 0.5);
            }
            finally { await CloseAndSettle(win); }

            // Spec Standard must override the element's Tall — on both halves.
            var overridden = new TitleBarComponent(static e => e.Tall());
            var winOverride = await OpenAndSettle(
                Spec("Spec wins") with { TitleBarHeight = WindowTitleBarHeight.Standard }, () => overridden);
            try
            {
                Report("specWins", winOverride, overridden.Bar);
                H.Check("TitleBarHeight_SpecWinsOverElement",
                    winOverride.AppWindow.TitleBar.Height == CaptionPx(winOverride, StandardCaptionDip)
                    && overridden.Bar is { } bar && Math.Abs(bar.ActualHeight - StandardCaptionDip) < 0.5);
            }
            finally { await CloseAndSettle(winOverride); }
        }
    }

    /// <summary>
    /// An explicit <c>.Height(...)</c> wins over the 48 DIP implied by
    /// <c>.Tall()</c>, while the caption still goes tall.
    /// </summary>
    internal class TitleBarHeightExplicitHeightWins(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var comp = new TitleBarComponent(static e => e.Tall().Height(64));
            var win = await OpenAndSettle(Spec("Explicit height"), () => comp);
            try
            {
                Report("tallPlusExplicit64", win, comp.Bar);
                H.Check("TitleBarHeight_ExplicitHeightWins",
                    win.AppWindow.TitleBar.Height == CaptionPx(win, TallCaptionDip)
                    && comp.Bar is { } bar && Math.Abs(bar.ActualHeight - 64) < 0.5);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// A declared height on a window that never becomes content-extended must
    /// warn, not throw — the native setter raises <c>ERROR_INVALID_STATE</c>
    /// (0x8007139F) in that state, which is the trap issue #917 ran into.
    /// The check is that the window opens and stays usable.
    /// </summary>
    internal class TitleBarHeightNotExtendedDoesNotThrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // Sanity: the raw platform call really does throw in this state, so
            // the fixture below is proving Reactor's guard, not a no-op.
            var probe = await OpenAndSettle(
                Spec("Raw throw probe") with { ExtendsContentIntoTitleBar = false }, () => new PlainBodyComponent());
            bool threw = false;
            try
            {
                try { probe.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall; }
                catch (global::System.Runtime.InteropServices.COMException) { threw = true; }
                H.Check("TitleBarHeight_RawSetterThrowsWhenNotExtended", threw);
            }
            finally { await CloseAndSettle(probe); }

            var win = await OpenAndSettle(
                Spec("Not extended") with
                {
                    ExtendsContentIntoTitleBar = false,
                    TitleBarHeight = WindowTitleBarHeight.Tall,
                },
                () => new PlainBodyComponent());
            try
            {
                Report("notExtended", win, null);
                H.Check("TitleBarHeight_NotExtended_NoThrow_NoApply",
                    !win.NativeWindow.ExtendsContentIntoTitleBar
                    && win.AppWindow.TitleBar.PreferredHeightOption == TitleBarHeightOption.Standard);

                // The declaration must be RETAINED, not dropped: flipping the
                // window into the content-extended mode re-applies it. Without
                // the deferred-apply path this stays Standard, so the check is
                // not satisfiable by a no-op implementation.
                win.Update(Spec("Not extended") with
                {
                    ExtendsContentIntoTitleBar = true,
                    TitleBarHeight = WindowTitleBarHeight.Tall,
                });
                await Harness.Render(150);
                Report("nowExtended", win, null);
                H.Check("TitleBarHeight_ReAppliedOnceExtended",
                    win.AppWindow.TitleBar.Height == CaptionPx(win, TallCaptionDip));
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// A <c>WindowSpec.TitleBarHeight</c> change delivered through
    /// <c>Update</c> — with no element re-render in between — must move BOTH
    /// halves. The control is only reachable from the window, so dropping the
    /// window-side control sync leaves a 48 px caption over a 32 px control.
    /// </summary>
    internal class TitleBarHeightSpecUpdateResizesControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var spec = Spec("Spec update");
            var comp = new TitleBarComponent(static e => e);
            var win = await OpenAndSettle(spec, () => comp);
            try
            {
                Report("beforeSpecUpdate", win, comp.Bar);
                var beforeControl = comp.Bar?.ActualHeight ?? -1;

                win.Update(spec with { TitleBarHeight = WindowTitleBarHeight.Tall });
                await Harness.Render(150);
                Report("afterSpecUpdate", win, comp.Bar);

                H.Check("TitleBarHeight_SpecUpdate_RaisesCaption",
                    win.AppWindow.TitleBar.Height == CaptionPx(win, TallCaptionDip));
                H.Check("TitleBarHeight_SpecUpdate_RaisesControl",
                    Math.Abs(beforeControl - StandardCaptionDip) < 0.5
                    && comp.Bar is { } bar && Math.Abs(bar.ActualHeight - TallCaptionDip) < 0.5);

                win.Update(spec with { TitleBarHeight = WindowTitleBarHeight.Standard });
                await Harness.Render(150);
                Report("afterSpecStandard", win, comp.Bar);
                H.Check("TitleBarHeight_SpecUpdate_LowersControlAgain",
                    win.AppWindow.TitleBar.Height == CaptionPx(win, StandardCaptionDip)
                    && comp.Bar is { } bar2 && Math.Abs(bar2.ActualHeight - StandardCaptionDip) < 0.5);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// Removing an explicit <c>.Height(...)</c> from a still-tall TitleBar must
    /// fall back to the height implied by <c>.Tall()</c>, not to auto. The
    /// reconciler clears a removed Height modifier <em>after</em> the element's
    /// imperative entry runs, so this only holds if the caption-derived height
    /// is re-applied post-modifiers.
    /// </summary>
    internal class TitleBarHeightRemoveExplicitHeight(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var comp = new ToggleExplicitHeightComponent();
            var win = await OpenAndSettle(Spec("Drop explicit height"), () => comp);
            try
            {
                Report("withExplicit64", win, comp.Bar);
                var withExplicit = comp.Bar?.ActualHeight ?? -1;

                comp.SetExplicit?.Invoke(false);
                await win.Host.WaitForIdleAsync();
                await Harness.Render(150);
                Report("explicitRemoved", win, comp.Bar);

                H.Check("TitleBarHeight_RemoveExplicitHeight_FallsBackToTall",
                    Math.Abs(withExplicit - 64) < 0.5
                    && comp.Bar is { } bar
                    && Math.Abs(bar.ActualHeight - TallCaptionDip) < 0.5
                    && win.AppWindow.TitleBar.Height == CaptionPx(win, TallCaptionDip));
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// Removing the declaration on a spec update returns the caption to
    /// Standard rather than stranding the previously applied value.
    /// </summary>
    internal class TitleBarHeightUpdateResets(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var spec = Spec("Update reset") with { TitleBarHeight = WindowTitleBarHeight.Tall };
            var comp = new TitleBarComponent(static e => e);
            var win = await OpenAndSettle(spec, () => comp);
            try
            {
                Report("beforeUpdate", win, comp.Bar);
                var applied = win.AppWindow.TitleBar.Height;

                win.Update(spec with { TitleBarHeight = null });
                await Harness.Render(150);
                Report("afterUpdate", win, comp.Bar);

                H.Check("TitleBarHeight_UpdateResetsToStandard",
                    applied == CaptionPx(win, TallCaptionDip)
                    && win.AppWindow.TitleBar.Height == CaptionPx(win, StandardCaptionDip));
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// Unmounting the <c>TitleBar</c> withdraws both of its contributions: the
    /// caption returns to Standard, and the <c>ExtendsContentIntoTitleBar</c>
    /// inference is no longer asserted on a later spec update that leaves the
    /// flag unset. Without the unmount hook the "ever mounted" latch would keep
    /// the window content-extended and tall for its remaining lifetime.
    /// </summary>
    internal class TitleBarHeightUnmountWithdraws(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var comp = new ToggleTitleBarComponent();
            var spec = Spec("Unmount withdraws");
            var win = await OpenAndSettle(spec, () => comp);
            try
            {
                Report("titleBarMounted", win, null);
                var mountedCaption = win.AppWindow.TitleBar.Height;
                var mountedExtended = win.NativeWindow.ExtendsContentIntoTitleBar;

                comp.SetVisible?.Invoke(false);
                await win.Host.WaitForIdleAsync();
                await Harness.Render(150);
                Report("titleBarUnmounted", win, null);

                H.Check("TitleBarHeight_Unmount_ResetsCaption",
                    mountedCaption == CaptionPx(win, TallCaptionDip)
                    && win.AppWindow.TitleBar.Height == CaptionPx(win, StandardCaptionDip));

                // A spec update that leaves ExtendsContentIntoTitleBar unset must
                // now infer false — the element that justified the inference is
                // gone. This is the half the "ever mounted" latch got wrong.
                win.Update(spec with { Title = "Unmount withdraws 2" });
                await Harness.Render(150);
                H.Check("TitleBarHeight_Unmount_WithdrawsExtendInference",
                    mountedExtended && !win.NativeWindow.ExtendsContentIntoTitleBar);
            }
            finally { await CloseAndSettle(win); }
        }
    }
}
