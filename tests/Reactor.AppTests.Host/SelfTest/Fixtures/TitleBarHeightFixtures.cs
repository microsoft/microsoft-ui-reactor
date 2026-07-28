using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
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
    private const int StandardCaption = 32;
    private const int TallCaption = 48;

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

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(150);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows)
        {
            if (win is null) continue;
            try { win.Close(); } catch { }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(100);
    }

    private static WindowSpec Spec(string title) =>
        new() { Title = title, Width = 420, Height = 260 };

    private static void Report(string label, ReactorWindow win, Microsoft.UI.Xaml.Controls.TitleBar? bar) =>
        Console.WriteLine(
            $"# {label}: caption={win.AppWindow.TitleBar.Height} control={bar?.ActualHeight.ToString("0.##") ?? "<null>"}");

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
                    baseCaption == StandardCaption && Math.Abs(baseControl - StandardCaption) < 0.5);
            }
            finally { await CloseAndSettle(winPlain); }

            var tall = new TitleBarComponent(static e => e.Tall());
            var winTall = await OpenAndSettle(Spec("Tall caption"), () => tall);
            try
            {
                Report("tall", winTall, tall.Bar);
                H.Check("TitleBarHeight_ElementTall_RaisesCaption",
                    winTall.AppWindow.TitleBar.Height == TallCaption
                    && winTall.AppWindow.TitleBar.Height != baseCaption);
                // The WinUI TitleBar control does not derive its height from the
                // caption — a tall caption over a 32 px control is the bug this
                // half of the feature exists to prevent.
                H.Check("TitleBarHeight_ElementTall_RaisesControl",
                    tall.Bar is { } bar
                    && Math.Abs(bar.ActualHeight - TallCaption) < 0.5
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
                    win.AppWindow.TitleBar.Height == TallCaption);
                H.Check("TitleBarHeight_SpecTall_RaisesControl",
                    comp.Bar is { } bar && Math.Abs(bar.ActualHeight - TallCaption) < 0.5);
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
                    winOverride.AppWindow.TitleBar.Height == StandardCaption
                    && overridden.Bar is { } bar && Math.Abs(bar.ActualHeight - StandardCaption) < 0.5);
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
                    win.AppWindow.TitleBar.Height == TallCaption
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
                    applied == TallCaption && win.AppWindow.TitleBar.Height == StandardCaption);
            }
            finally { await CloseAndSettle(win); }
        }
    }
}
