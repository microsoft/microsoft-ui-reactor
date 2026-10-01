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

    private static async Task<ReactorWindow> OpenAndSettle(
        WindowSpec spec, Func<Component> root, Action<Microsoft.UI.Reactor.Hosting.ReactorHost>? configure = null)
    {
        var win = ReactorApp.OpenWindow(spec, root, configure);
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

    /// <summary>
    /// A value the app set imperatively before Reactor's first write — as a
    /// <c>configure:</c> callback would — is what withdrawing the declaration restores,
    /// not the platform default.
    /// </summary>
    internal class WithdrawRestoresAppValue(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            var comp = new ThemedBarComponent(null);
            var win = await OpenAndSettle(Spec("Theme app value"), () => comp);
            try
            {
                // Differs from both the platform baseline and the value declared below,
                // so restoring either of those instead cannot pass.
                const TitleBarTheme appValue = TitleBarTheme.UseDefaultAppMode;
                win.AppWindow.TitleBar.PreferredTheme = appValue;

                comp.SetTheme!(WindowTitleBarTheme.Dark);
                await Settle(win);
                Report("declaredOverApp", win);
                H.Check("TitleBarTheme_AppValue_DeclarationApplies", Caption(win) == TitleBarTheme.Dark);

                comp.SetTheme!(null);
                await Settle(win);
                Report("withdrawnToApp", win);
                H.Check("TitleBarTheme_AppValue_WithdrawRestoresIt",
                    Caption(win) == appValue && baseline != appValue);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>Two title bars, only one of which declares a theme.</summary>
    private sealed class TwoBarsComponent : Component
    {
        public Action<bool>? SetShowDeclaring;
        public Action<bool>? SetShowPlain;

        public override Element Render()
        {
            var (showDeclaring, setShowDeclaring) = UseState(true);
            var (showPlain, setShowPlain) = UseState(true);
            SetShowDeclaring = setShowDeclaring;
            SetShowPlain = setShowPlain;
            return VStack(
                showDeclaring ? TitleBar("Declaring").PreferredTheme(WindowTitleBarTheme.Dark) : Empty(),
                showPlain ? TitleBar("Plain") : Empty(),
                TextBlock("body"));
        }
    }

    /// <summary>
    /// With several title bars mounted, only the bar that declared the theme owns it:
    /// a bar that declares nothing neither withdraws it on mount nor on unmount, and
    /// the declaring bar's unmount withdraws it even though another bar remains.
    /// </summary>
    internal class MultipleBarsOwnership(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            var comp = new TwoBarsComponent();
            var win = await OpenAndSettle(Spec("Theme two bars"), () => comp);
            try
            {
                // The plain bar mounts after the declaring one; a null declaration from
                // it must not withdraw the other bar's theme.
                Report("bothMounted", win);
                H.Check("TitleBarTheme_MultiBar_PlainMountKeepsTheme",
                    Caption(win) == TitleBarTheme.Dark && baseline != TitleBarTheme.Dark);

                comp.SetShowPlain!(false);
                await Settle(win);
                Report("plainUnmounted", win);
                H.Check("TitleBarTheme_MultiBar_PlainUnmountKeepsTheme", Caption(win) == TitleBarTheme.Dark);

                comp.SetShowPlain!(true);
                await Settle(win);
                comp.SetShowDeclaring!(false);
                await Settle(win);
                Report("declaringUnmounted", win);
                H.Check("TitleBarTheme_MultiBar_DeclaringUnmountRestores", Caption(win) == baseline);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// A declaring bar replaced by a subtree that contains an equally-declaring bar.
    /// Changing the child's element TYPE (TitleBar → VStack) selects the reconciler's
    /// mount-then-unmount branch, so the old bar's unmount arrives after the new bar
    /// has claimed the theme; that stale unmount must not withdraw it.
    /// </summary>
    internal class SurvivesTypeReplacement(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        private sealed class SwapComponent : Component
        {
            public Action<int>? SetPhase;

            public override Element Render()
            {
                var (phase, set) = UseState(0);
                SetPhase = set;
                var bar = TitleBar("Swap").PreferredTheme(WindowTitleBarTheme.Dark);
                return VStack(phase == 0 ? bar : VStack(bar), TextBlock("body"));
            }
        }

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            var comp = new SwapComponent();
            var win = await OpenAndSettle(Spec("Theme swap"), () => comp);
            try
            {
                H.Check("TitleBarTheme_Swap_AppliedBefore",
                    Caption(win) == TitleBarTheme.Dark && baseline != TitleBarTheme.Dark);

                comp.SetPhase!(1);
                await Settle(win);
                Report("afterTypeSwap", win);
                H.Check("TitleBarTheme_Swap_ThemeSurvives", Caption(win) == TitleBarTheme.Dark);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>
    /// A <c>configure:</c> callback runs after the window is constructed but before the
    /// root mounts. A declared spec theme must still win over a value the callback sets,
    /// and withdrawing the declaration must hand the callback's value back.
    /// </summary>
    internal class ConfigureCallbackValue(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            // Differs from the platform baseline and from the declared value, so neither
            // "callback wins" nor "restores the platform default" can pass.
            const TitleBarTheme configured = TitleBarTheme.UseDefaultAppMode;
            var spec = Spec("Theme configure") with { TitleBarTheme = WindowTitleBarTheme.Dark };
            var win = await OpenAndSettle(spec, () => new PlainBodyComponent(),
                configure: host => host.Window.AppWindow.TitleBar.PreferredTheme = configured);
            try
            {
                Report("specOverConfigure", win);
                H.Check("TitleBarTheme_Configure_DeclarationWins", Caption(win) == TitleBarTheme.Dark);

                win.Update(spec with { TitleBarTheme = null });
                await Settle(win);
                Report("withdrawnToConfigure", win);
                H.Check("TitleBarTheme_Configure_WithdrawRestoresCallbackValue",
                    Caption(win) == configured && baseline != configured);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    /// <summary>Two title bars that both declare a theme.</summary>
    private sealed class TwoDeclaringBarsComponent : Component
    {
        public Action<bool>? SetShowSecond;
        public Action<bool>? SetShowFirst;

        public override Element Render()
        {
            var (showFirst, setShowFirst) = UseState(true);
            var (showSecond, setShowSecond) = UseState(true);
            SetShowFirst = setShowFirst;
            SetShowSecond = setShowSecond;
            return VStack(
                showFirst ? TitleBar("First").PreferredTheme(WindowTitleBarTheme.Dark) : Empty(),
                showSecond ? TitleBar("Second").PreferredTheme(WindowTitleBarTheme.Light) : Empty(),
                TextBlock("body"));
        }
    }

    /// <summary>
    /// With two declaring bars, the most recent declaration wins; unmounting it hands the
    /// caption to the other bar's still-live declaration rather than to the baseline.
    /// The unchanged first bar is skipped by reconciliation, so only per-control
    /// tracking can recover its declaration.
    /// </summary>
    internal class TwoDeclaringBars(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(45);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var baseline = await MeasureBaseline();

            var comp = new TwoDeclaringBarsComponent();
            var win = await OpenAndSettle(Spec("Theme two declaring"), () => comp);
            try
            {
                Report("bothDeclaring", win);
                H.Check("TitleBarTheme_TwoDeclaring_LatestWins", Caption(win) == TitleBarTheme.Light);

                comp.SetShowSecond!(false);
                await Settle(win);
                Report("secondUnmounted", win);
                H.Check("TitleBarTheme_TwoDeclaring_FallsBackToRemainingDeclaration",
                    Caption(win) == TitleBarTheme.Dark && baseline != TitleBarTheme.Dark);

                comp.SetShowFirst!(false);
                await Settle(win);
                Report("bothUnmounted", win);
                H.Check("TitleBarTheme_TwoDeclaring_LastUnmountRestoresBaseline", Caption(win) == baseline);
            }
            finally { await CloseAndSettle(win); }
        }
    }
}
