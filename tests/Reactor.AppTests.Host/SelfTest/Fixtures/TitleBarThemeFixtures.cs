using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Windowing;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1297 — declarative caption theme (<c>WindowSpec.TitleBarTheme</c> /
/// <c>TitleBar(...).PreferredTheme(...)</c>).
/// <para>
/// The oracle is <c>AppWindow.TitleBar.PreferredTheme</c>. Every "applied" check
/// also requires the value to differ from the baseline a fresh window reports,
/// measured in the same run, so it cannot pass on a platform default that happens
/// to coincide. That the property really restyles the caption buttons on a
/// content-extended window was verified by screenshot when the feature landed.
/// </para>
/// </summary>
internal static class TitleBarThemeFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    /// <summary>A title bar whose declared theme and presence can change at runtime.</summary>
    private sealed class ThemedBarComponent(WindowTitleBarTheme? initial) : Component
    {
        public Action<WindowTitleBarTheme?>? SetTheme;
        public Action<bool>? SetVisible;

        public override Element Render()
        {
            var (theme, setTheme) = UseState(initial);
            var (visible, setVisible) = UseState(true);
            SetTheme = setTheme;
            SetVisible = setVisible;
            if (!visible) return VStack(TextBlock("body"));
            var bar = TitleBar("Theme");
            if (theme is { } t) bar = bar.PreferredTheme(t);
            return VStack(bar, TextBlock("body"));
        }
    }

    private static WindowSpec Spec(string title) => new() { Title = title, Width = 420, Height = 260 };

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(150);
        return win;
    }

    private static async Task Settle(ReactorWindow win)
    {
        await win.Host.WaitForIdleAsync();
        await Harness.Render(100);
    }

    private static async Task CloseAndSettle(ReactorWindow win)
    {
        // The WinUI TitleBar control can throw teardown-reentry COMExceptions on close
        // (issue #537); a window may also already be closing or disposed.
        try { win.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "SelfTest.TitleBarTheme.CloseAndSettle", ex);
        }
        await Task.Delay(100);
    }

    private static TitleBarTheme Caption(ReactorWindow win) => win.AppWindow.TitleBar.PreferredTheme;

    private static void Report(string label, ReactorWindow win) =>
        Console.WriteLine($"# {label}: caption={Caption(win)}");

    /// <summary>The value a window reports when nothing has declared a theme.</summary>
    private static async Task<TitleBarTheme> MeasureBaseline()
    {
        var win = await OpenAndSettle(Spec("Theme baseline"), () => new ThemedBarComponent(null));
        try
        {
            Report("baseline", win);
            return Caption(win);
        }
        finally { await CloseAndSettle(win); }
    }

    /// <summary>
    /// The element's declaration is applied on mount, follows runtime changes, and is
    /// withdrawn — back to the baseline — when removed and when the bar unmounts.
    /// </summary>
    internal class ElementDeclaration(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            var comp = new ThemedBarComponent(WindowTitleBarTheme.Dark);
            var win = await OpenAndSettle(Spec("Theme element"), () => comp);
            try
            {
                Report("mountDark", win);
                H.Check("TitleBarTheme_Element_AppliedOnMount",
                    Caption(win) == TitleBarTheme.Dark && baseline != TitleBarTheme.Dark);

                comp.SetTheme!(WindowTitleBarTheme.Light);
                await Settle(win);
                Report("updateLight", win);
                H.Check("TitleBarTheme_Element_FollowsUpdate",
                    Caption(win) == TitleBarTheme.Light && baseline != TitleBarTheme.Light);

                comp.SetTheme!(null);
                await Settle(win);
                Report("removed", win);
                H.Check("TitleBarTheme_Element_RemovalRestoresBaseline", Caption(win) == baseline);

                comp.SetTheme!(WindowTitleBarTheme.Dark);
                await Settle(win);
                comp.SetVisible!(false);
                await Settle(win);
                Report("unmounted", win);
                H.Check("TitleBarTheme_Element_UnmountRestoresBaseline", Caption(win) == baseline);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// <c>WindowSpec.TitleBarTheme</c> wins over the element, including through
    /// <c>Update</c>, and withdrawing it falls back to the element's declaration.
    /// </summary>
    internal class SpecPrecedence(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(30);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var spec = Spec("Theme spec") with { TitleBarTheme = WindowTitleBarTheme.Light };
            var comp = new ThemedBarComponent(WindowTitleBarTheme.Dark);
            var win = await OpenAndSettle(spec, () => comp);
            try
            {
                Report("specLight_elementDark", win);
                H.Check("TitleBarTheme_Spec_WinsOverElement", Caption(win) == TitleBarTheme.Light);

                win.Update(spec with { TitleBarTheme = null });
                await Settle(win);
                Report("specWithdrawn", win);
                H.Check("TitleBarTheme_Spec_WithdrawFallsBackToElement", Caption(win) == TitleBarTheme.Dark);

                win.Update(spec with { TitleBarTheme = WindowTitleBarTheme.Light });
                await Settle(win);
                Report("specRestored", win);
                H.Check("TitleBarTheme_Spec_UpdateApplies", Caption(win) == TitleBarTheme.Light);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// With nothing declared, Reactor never writes: a theme the app set imperatively
    /// survives re-renders and spec updates.
    /// </summary>
    internal class UndeclaredLeavesAppValue(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(30);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var spec = Spec("Theme undeclared");
            var comp = new ThemedBarComponent(null);
            var win = await OpenAndSettle(spec, () => comp);
            try
            {
                var appValue = Caption(win) == TitleBarTheme.Dark ? TitleBarTheme.Light : TitleBarTheme.Dark;
                win.AppWindow.TitleBar.PreferredTheme = appValue;

                comp.SetVisible!(false);
                await Settle(win);
                comp.SetVisible!(true);
                await Settle(win);
                win.Update(spec with { Title = "Theme undeclared (updated)" });
                await Settle(win);
                Report("afterRerenders", win);

                H.Check("TitleBarTheme_Undeclared_AppValuePreserved", Caption(win) == appValue);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    private sealed class PlainBodyComponent : Component
    {
        public override Element Render() => VStack(TextBlock("body"));
    }

    /// <summary>
    /// <c>WindowSpec.TitleBarTheme</c> on a window with no <c>TitleBar(...)</c> element:
    /// applied at open, follows <c>Update</c>, and withdrawing it restores the baseline.
    /// No element mount path runs here, so only the spec path can satisfy the checks.
    /// </summary>
    internal class SpecOnlyWithoutElement(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            var spec = Spec("Theme spec only") with { TitleBarTheme = WindowTitleBarTheme.Dark };
            var win = await OpenAndSettle(spec, () => new PlainBodyComponent());
            try
            {
                Report("specOnlyDark", win);
                H.Check("TitleBarTheme_SpecOnly_AppliedAtOpen",
                    Caption(win) == TitleBarTheme.Dark && baseline != TitleBarTheme.Dark);

                win.Update(spec with { TitleBarTheme = WindowTitleBarTheme.Light });
                await Settle(win);
                Report("specOnlyLight", win);
                H.Check("TitleBarTheme_SpecOnly_FollowsUpdate",
                    Caption(win) == TitleBarTheme.Light && baseline != TitleBarTheme.Light);

                win.Update(spec with { TitleBarTheme = null });
                await Settle(win);
                Report("specOnlyWithdrawn", win);
                H.Check("TitleBarTheme_SpecOnly_WithdrawRestoresBaseline", Caption(win) == baseline);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// An element declaration applies on a window whose spec explicitly sets
    /// <c>ExtendsContentIntoTitleBar = false</c>, where the mount path returns before
    /// <c>SetTitleBar</c> — so the theme must be applied ahead of that early return.
    /// </summary>
    internal class ElementWithoutContentExtension(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(30);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var spec = Spec("Theme not extended") with { ExtendsContentIntoTitleBar = false };
            var comp = new ThemedBarComponent(WindowTitleBarTheme.Dark);
            var win = await OpenAndSettle(spec, () => comp);
            try
            {
                Report("notExtendedDark", win);
                // Positive control: the scenario really is a non-extended window.
                H.Check("TitleBarTheme_NotExtended_WindowNotExtended",
                    !win.NativeWindow.ExtendsContentIntoTitleBar);
                H.Check("TitleBarTheme_NotExtended_ElementApplied", Caption(win) == TitleBarTheme.Dark);

                comp.SetTheme!(WindowTitleBarTheme.Light);
                await Settle(win);
                Report("notExtendedLight", win);
                H.Check("TitleBarTheme_NotExtended_FollowsUpdate", Caption(win) == TitleBarTheme.Light);
            }
            finally { await CloseAndSettle(win); }
        }
    }
}
