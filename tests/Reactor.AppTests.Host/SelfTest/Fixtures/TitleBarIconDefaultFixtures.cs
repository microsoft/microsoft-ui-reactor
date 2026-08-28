using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// A <c>TitleBar</c> with no declared icon inherits the window's.
/// <para>
/// Two oracles per arm, because either alone is weak. <c>TitleBar.IconSource</c>
/// proves the write happened and picked the right projection; the presence of the
/// template's <c>PART_Icon</c> viewbox proves layout actually consumed it. Measured
/// on WinApp SDK 2.1: with no <c>IconSource</c>, <c>PART_Icon</c> is absent from the
/// visual tree entirely (deferred load) rather than merely zero-width, so the
/// difference between the arms is presence, not a size comparison that could read
/// zero for unrelated reasons.
/// </para>
/// <para>
/// The <b>convention</b> arm is the load-bearing one. The three WinAppSDK templates
/// this feature unblocks call <c>ReactorApp.Run&lt;App&gt;("Name")</c> with no
/// <c>icon:</c> argument and ship <c>Assets\AppIcon.ico</c>, so their window icon —
/// and now their title-bar mark — resolves through the convention probe with
/// <c>WindowSpec.Icon</c> null throughout.
/// </para>
/// </summary>
internal static class TitleBarIconDefaultFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class BarComponent(Func<TitleBarElement, TitleBarElement> configure) : Component
    {
        public WinUI.TitleBar? Bar;
        public override Element Render() =>
            VStack(configure(TitleBar("IconDefault")).Set(b => Bar = b), TextBlock("body"));
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(200);
        return win;
    }

    private static async Task CloseAndSettle(ReactorWindow? win)
    {
        // Best-effort teardown, matching the house pattern: the WinUI TitleBar control
        // throws teardown-reentry COMExceptions (issue #537), and anything escaping
        // here would replace a real assertion result with a teardown error.
        try { win?.Close(); }
        catch (Exception ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "SelfTest.TitleBarIconDefault.Close", ex);
        }
        await Task.Delay(120);
    }

    private static WindowSpec Spec(string title) =>
        new() { Title = title, Width = 480, Height = 260 };

    /// <summary>
    /// The icon the self-test host already ships for the window-icon fixtures. Reused
    /// rather than adding another binary; note it is deliberately <b>not</b> named
    /// <c>AppIcon.ico</c>, so it cannot satisfy the convention probe and make this
    /// fixture's zero control vacuous.
    /// </summary>
    private const string TestIcoRelative = "Assets/SelfTestWindowIcon.ico";

    private static string TestIcoPath =>
        global::System.IO.Path.Join(AppContext.BaseDirectory, "Assets", "SelfTestWindowIcon.ico");

    /// <summary>
    /// Builds a scratch app-root containing <c>Assets\AppIcon.ico</c>, for pointing
    /// <see cref="TitleBarIconDefault.SetBaseDirectoryForTests"/> at. Never writes into
    /// the host's own output directory: an icon there would give every other windowing
    /// fixture a window icon and silently destroy this fixture's zero control.
    /// </summary>
    private static string CreateScratchAppRoot(bool withConventionAsset)
    {
        var root = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(),
            "ReactorTitleBarIcon_" + Guid.NewGuid().ToString("N"));
        global::System.IO.Directory.CreateDirectory(root);
        if (withConventionAsset)
        {
            var assets = global::System.IO.Path.Join(root, "Assets");
            global::System.IO.Directory.CreateDirectory(assets);
            global::System.IO.File.Copy(TestIcoPath, global::System.IO.Path.Join(assets, "AppIcon.ico"));
        }
        return root;
    }

    /// <summary>
    /// Copies the test icon to a loose file in <paramref name="root"/> — outside the
    /// real package root, and outside the <c>Assets</c> convention directory, so it can
    /// only be reached by an explicit <c>WindowIcon.FromPath</c>.
    /// </summary>
    private static string CreateExternalIcon(string root)
    {
        var path = global::System.IO.Path.Join(root, "External.ico");
        global::System.IO.File.Copy(TestIcoPath, path, overwrite: true);
        return path;
    }

    private static void DeleteScratch(string root)
    {
        try { global::System.IO.Directory.Delete(root, recursive: true); }
        catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "SelfTest.TitleBarIconDefault.Scratch", ex);
        }
    }

    private static T? FindFirst<T>(DependencyObject? root) where T : class
    {
        if (root is null) return null;
        if (root is T match) return match;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var hit = FindFirst<T>(VisualTreeHelper.GetChild(root, i));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>
    /// The template's icon viewbox, or null when the control realized none.
    /// </summary>
    private static FrameworkElement? FindIconPart(WinUI.TitleBar bar)
    {
        bar.ApplyTemplate();
        bar.UpdateLayout();
        return FindFirst<FrameworkElement>(bar) is null
            ? null
            : FindNamed(bar, "PART_Icon");
    }

    private static FrameworkElement? FindNamed(DependencyObject root, string name)
    {
        if (root is FrameworkElement fe && fe.Name == name) return fe;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var hit = FindNamed(VisualTreeHelper.GetChild(root, i), name);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Waits for the icon's bitmap to finish decoding. <c>PixelWidth</c> is 0 until
    /// <c>ImageOpened</c> fires, so reading it eagerly measures the wait, not the image.
    /// </summary>
    private static async Task<(bool Opened, string Failure, int Width, int Height)> AwaitDecode(BitmapImage bmp)
    {
        if (bmp.PixelWidth > 0) return (true, "", bmp.PixelWidth, bmp.PixelHeight);

        var failure = "";
        var tcs = new TaskCompletionSource();
        void OnOpened(object? s, RoutedEventArgs e) => tcs.TrySetResult();
        void OnFailed(object? s, ExceptionRoutedEventArgs e) { failure = e.ErrorMessage; tcs.TrySetResult(); }
        bmp.ImageOpened += OnOpened;
        bmp.ImageFailed += OnFailed;
        try { await Task.WhenAny(tcs.Task, Task.Delay(5000)); }
        finally
        {
            bmp.ImageOpened -= OnOpened;
            bmp.ImageFailed -= OnFailed;
        }
        return (failure.Length == 0 && bmp.PixelWidth > 0, failure, bmp.PixelWidth, bmp.PixelHeight);
    }

    /// <summary>
    /// Asserts an inherited icon is present on both oracles. <paramref name="prefix"/>
    /// names the arm so a failure says which source was being inherited from.
    /// </summary>
    private static async Task CheckInheritedIcon(Harness h, string prefix, BarComponent comp)
    {
        var bar = comp.Bar;
        h.Check($"{prefix}_BarMounted", bar is not null);
        if (bar is null) return;

        // Every check below runs unconditionally rather than short-circuiting on the
        // first failure. The two oracles are meant to be independent evidence, so a
        // regression must be able to redden each of them on its own — a cascade of
        // skips would leave the layout oracle unproven exactly when it matters.
        var source = bar.IconSource;
        Console.WriteLine($"# {prefix}: IconSource={source?.GetType().Name ?? "<null>"}");
        h.Check($"{prefix}_IsImageIconSource", source is WinUI.ImageIconSource);

        var bmp = (source as WinUI.ImageIconSource)?.ImageSource as BitmapImage;
        h.Check($"{prefix}_ImageSourceIsBitmap", bmp is not null);

        // Oracle 1 — the icon actually decoded. PixelWidth is 0 until ImageOpened
        // fires, so an eager read would measure the wait rather than the image.
        var opened = false;
        var failure = "";
        int w = 0, hgt = 0;
        if (bmp is not null)
        {
            (opened, failure, w, hgt) = await AwaitDecode(bmp);
            Console.WriteLine($"# {prefix}: uri={bmp.UriSource} opened={opened} failed='{failure}' px={w}x{hgt}");
        }
        h.Check($"{prefix}_Decoded (px={w}x{hgt} failed='{failure}')", opened && w > 0 && hgt > 0);

        // Guard the inverse trap before oracle 2: a TitleBar that never laid out would
        // report a missing icon part for a reason unrelated to this feature.
        h.Check($"{prefix}_BarLaidOut (w={bar.ActualWidth:0.##} h={bar.ActualHeight:0.##})",
            bar.ActualWidth > 0 && bar.ActualHeight > 0);

        // Oracle 2 — layout consumed the icon. Measured: with no IconSource the
        // template leaves PART_Icon out of the visual tree entirely.
        var part = FindIconPart(bar);
        Console.WriteLine($"# {prefix}: PART_Icon={(part is null ? "<absent>" : $"{part.ActualWidth:0.##}x{part.ActualHeight:0.##}")}");
        h.Check($"{prefix}_IconPartRealized", part is not null && part.ActualWidth > 0 && part.ActualHeight > 0);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  The load-bearing arm: no WindowSpec.Icon at all, icon comes from the
    //  Assets\AppIcon.ico convention — which is how all three WinAppSDK
    //  templates actually run.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultFromConvention(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // The zero control only means something if nothing else in this host can
            // supply an icon. Assert that rather than trusting it.
            var hostConvention = global::System.IO.Path.Join(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            var hostHasIcon = global::System.IO.File.Exists(hostConvention);
            Console.WriteLine($"# host convention asset present={hostHasIcon} ({hostConvention})");
            H.Check("TitleBarIcon_HostShipsNoConventionAsset", !hostHasIcon);

            var scratch = CreateScratchAppRoot(withConventionAsset: true);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var comp = new BarComponent(static e => e);
                var win = await OpenAndSettle(Spec("Convention"), () => comp);
                try
                {
                    await CheckInheritedIcon(H, "TitleBarIcon_Convention", comp);
                }
                finally { await CloseAndSettle(win); }
            }
            finally
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(null);
                DeleteScratch(scratch);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Zero control — no icon anywhere. Establishes that a non-null IconSource
    //  in the other arms is attributable to the feature and not to something
    //  the host already supplied.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultZeroControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var comp = new BarComponent(static e => e);
                var win = await OpenAndSettle(Spec("Zero"), () => comp);
                try
                {
                    var bar = comp.Bar;
                    H.Check("TitleBarIcon_Zero_BarMounted", bar is not null);
                    if (bar is null) return;

                    Console.WriteLine($"# zero: IconSource={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Zero_NoIconSource", bar.IconSource is null);

                    // Same guard as the positive arms: prove layout ran, so the absent
                    // icon part is evidence about the icon and not about the pass.
                    H.Check($"TitleBarIcon_Zero_BarLaidOut (w={bar.ActualWidth:0.##} h={bar.ActualHeight:0.##})",
                        bar.ActualWidth > 0 && bar.ActualHeight > 0);
                    var part = FindIconPart(bar);
                    Console.WriteLine($"# zero: PART_Icon={(part is null ? "<absent>" : $"{part.ActualWidth:0.##}")}");
                    H.Check("TitleBarIcon_Zero_NoIconPart", part is null || part.ActualWidth == 0);
                }
                finally { await CloseAndSettle(win); }
            }
            finally
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(null);
                DeleteScratch(scratch);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Declared WindowSpec.Icon — the explicit opt-in path, both source kinds.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultFromWindowSpec(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // Empty scratch root: the convention probe must find nothing, so a rendered
            // icon here is attributable to WindowSpec.Icon and to nothing else.
            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);

                // (a) FromPath naming a file OUTSIDE the package root — the file:
                // branch, whose URI is built straight from the verified path with no
                // mapping assumption.
                var external = CreateExternalIcon(scratch);
                var comp = new BarComponent(static e => e);
                var win = await OpenAndSettle(
                    Spec("DeclaredExternal") with { Icon = WindowIcon.FromPath(external) }, () => comp);
                try
                {
                    await CheckInheritedIcon(H, "TitleBarIcon_External", comp);
                    var uri = ((comp.Bar?.IconSource as WinUI.ImageIconSource)?.ImageSource
                        as BitmapImage)?.UriSource;
                    H.Check($"TitleBarIcon_External_UsesFileScheme (uri={uri})",
                        uri is not null && uri.Scheme == "file");
                }
                finally { await CloseAndSettle(win); }

                // (b) FromPath naming a file INSIDE the package root — upgraded to the
                // ms-appx: form, so a packaged app gets XAML's MRT-aware path even
                // though the author wrote a plain path.
                var insideComp = new BarComponent(static e => e);
                var insideWin = await OpenAndSettle(
                    Spec("DeclaredInside") with { Icon = WindowIcon.FromPath(TestIcoPath) }, () => insideComp);
                try
                {
                    await CheckInheritedIcon(H, "TitleBarIcon_Declared", insideComp);
                    var uri = ((insideComp.Bar?.IconSource as WinUI.ImageIconSource)?.ImageSource
                        as BitmapImage)?.UriSource;
                    H.Check($"TitleBarIcon_Declared_UpgradedToMsAppx (uri={uri})",
                        uri is not null && uri.Scheme == "ms-appx");
                }
                finally { await CloseAndSettle(insideWin); }

                // (c) FromResource naming a real asset under the package root —
                // exercises the ms-appx: branch end to end, including the re-derivation
                // back to a resource URI. The scratch override deliberately does NOT
                // move what ms-appx resolves to, which is the bug this arm caught.
                var resComp = new BarComponent(static e => e);
                var resWin = await OpenAndSettle(
                    Spec("DeclaredResource") with
                    {
                        Icon = WindowIcon.FromResource("ms-appx:///" + TestIcoRelative),
                    },
                    () => resComp);
                try
                {
                    await CheckInheritedIcon(H, "TitleBarIcon_Resource", resComp);
                    var uri = ((resComp.Bar?.IconSource as WinUI.ImageIconSource)?.ImageSource
                        as BitmapImage)?.UriSource;
                    H.Check($"TitleBarIcon_Resource_UsesMsAppxScheme (uri={uri})",
                        uri is not null && uri.Scheme == "ms-appx");
                }
                finally { await CloseAndSettle(resWin); }
            }
            finally
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(null);
                DeleteScratch(scratch);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Precedence and opt-out.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultExplicitAndOptOut(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: true);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);

                // An explicit icon must beat the inherited one. FontIconSource is a
                // different projection from the ImageIconSource the default produces,
                // so this cannot pass by accident.
                var explicitIcon = new BarComponent(
                    static e => e.Icon(new FontIconData("\uE734", "Segoe Fluent Icons")));
                var winExplicit = await OpenAndSettle(Spec("Explicit"), () => explicitIcon);
                try
                {
                    var src = explicitIcon.Bar?.IconSource;
                    Console.WriteLine($"# explicit: IconSource={src?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_ExplicitWins", src is WinUI.FontIconSource);
                }
                finally { await CloseAndSettle(winExplicit); }

                // .NoIcon() must beat the inherited one too — in the other direction.
                var suppressed = new BarComponent(static e => e.NoIcon());
                var winNone = await OpenAndSettle(Spec("NoIcon"), () => suppressed);
                try
                {
                    var bar = suppressed.Bar;
                    Console.WriteLine($"# noicon: IconSource={bar?.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_NoIconSuppresses", bar is not null && bar.IconSource is null);
                    if (bar is not null)
                    {
                        H.Check($"TitleBarIcon_NoIcon_BarLaidOut (w={bar.ActualWidth:0.##})", bar.ActualWidth > 0);
                        var part = FindIconPart(bar);
                        H.Check("TitleBarIcon_NoIcon_NoIconPart", part is null || part.ActualWidth == 0);
                    }
                }
                finally { await CloseAndSettle(winNone); }
            }
            finally
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(null);
                DeleteScratch(scratch);
            }
        }
    }
}
