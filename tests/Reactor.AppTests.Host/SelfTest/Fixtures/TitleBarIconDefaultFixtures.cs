using System.Runtime.InteropServices;
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

    /// <summary>
    /// A title bar whose configuration can be flipped at runtime, so a mounted control
    /// can be re-rendered on demand. <c>ReactorWindow.Update</c> does not schedule a
    /// render, so any assertion about what happens on the *next* render needs this.
    /// </summary>
    private sealed class ToggleBarComponent(Func<int, TitleBarElement, TitleBarElement> configure) : Component
    {
        public WinUI.TitleBar? Bar;
        public Action<int>? SetPhase;

        public override Element Render()
        {
            var (phase, set) = UseState(0);
            SetPhase = set;
            return VStack(configure(phase, TitleBar("IconToggle")).Set(b => Bar = b), TextBlock("body"));
        }
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
        //
        // The set is the one the window can plausibly be in at close: mid-native-teardown
        // (COMException), already disposed (ObjectDisposedException, which derives from
        // InvalidOperationException), or already closing. Mirrors the predicate
        // ReactorWindow.IsIconApplyFailure uses at the same boundary. Anything outside it
        // is a genuine bug in the fixture and should surface.
        try { win?.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
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

                // Make the negative explicit. The scratch root is freshly created under a
                // GUID name, so nothing can pre-exist in it — but "the convention probe
                // finds nothing here" is the entire basis for reading a null IconSource
                // below as evidence about the feature, so assert it rather than imply it.
                var scratchConvention = global::System.IO.Path.Join(scratch, "Assets", "AppIcon.ico");
                var scratchHasIcon = global::System.IO.File.Exists(scratchConvention);
                Console.WriteLine($"# zero: scratch convention present={scratchHasIcon} ({scratchConvention})");
                H.Check("TitleBarIcon_Zero_ScratchRootHasNoConventionAsset", !scratchHasIcon);

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
                    H.Check("TitleBarIcon_Zero_NoIconPart", IconPartIsAbsentOrCollapsed(part));
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
                        H.Check("TitleBarIcon_NoIcon_NoIconPart", IconPartIsAbsentOrCollapsed(part));
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

    /// <summary>The URI behind a title bar's current image icon, or null.</summary>
    private static Uri? IconUri(WinUI.TitleBar bar) =>
        ((bar.IconSource as WinUI.ImageIconSource)?.ImageSource as BitmapImage)?.UriSource;

    /// <summary>
    /// True when the template's icon part is absent or has collapsed to nothing.
    /// <para>
    /// Compared against a tolerance rather than <c>== 0</c>: <c>ActualWidth</c> is a
    /// layout double, so exact equality is the wrong instrument even when the value is
    /// nominally zero. Measured shape is that <c>PART_Icon</c> is absent from the tree
    /// entirely when there is no <c>IconSource</c>, so the width arm is a fallback.
    /// </para>
    /// </summary>
    private static bool IconPartIsAbsentOrCollapsed(FrameworkElement? part)
        => part is null || Math.Abs(part.ActualWidth) < 0.001;

    // ════════════════════════════════════════════════════════════════════════
    //  Regression: the inherited icon is AMBIENT window state, not element
    //  state. A OneWay descriptor entry decides whether to write by comparing
    //  get(oldElement) against get(newElement) — and both read the same ambient
    //  value, so after the window icon changes they compare equal and the
    //  control keeps the stale icon forever. This arm fails against that shape
    //  and passes against the Imperative + window-push shape.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultFollowsWindowIconChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var first = CreateExternalIcon(scratch);
                var second = global::System.IO.Path.Join(scratch, "Second.ico");
                global::System.IO.File.Copy(TestIcoPath, second, overwrite: true);

                var comp = new BarComponent(static e => e);
                var win = await OpenAndSettle(
                    Spec("IconFollows") with { Icon = WindowIcon.FromPath(first) }, () => comp);
                try
                {
                    var bar = comp.Bar;
                    H.Check("TitleBarIcon_Follows_BarMounted", bar is not null);
                    if (bar is null) return;

                    var before = IconUri(bar);
                    Console.WriteLine($"# follows: before={before}");
                    H.Check($"TitleBarIcon_Follows_InitialIcon (uri={before})",
                        before is not null
                        && before.LocalPath.EndsWith("External.ico", StringComparison.OrdinalIgnoreCase));

                    // Change ONLY the window icon. The element is untouched, so nothing
                    // an element diff can observe has changed.
                    win.Update(win.Spec with { Icon = WindowIcon.FromPath(second) });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);

                    var after = IconUri(bar);
                    Console.WriteLine($"# follows: after={after}");
                    H.Check($"TitleBarIcon_Follows_TracksWindowIconChange (uri={after})",
                        after is not null
                        && after.LocalPath.EndsWith("Second.ico", StringComparison.OrdinalIgnoreCase));
                    H.Check("TitleBarIcon_Follows_IconActuallyChanged", before != after);

                    // Dropping the window icon entirely, with no convention asset in the
                    // scratch root, must take the title-bar mark away too.
                    win.Update(win.Spec with { Icon = null });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# follows: cleared={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Follows_ClearsWhenWindowIconRemoved", bar.IconSource is null);
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
    //  An element that owns its icon slot keeps it across a window icon change.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultExplicitSurvivesWindowIconChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var external = CreateExternalIcon(scratch);

                var comp = new BarComponent(
                    static e => e.Icon(new FontIconData("\uE734", "Segoe Fluent Icons")));
                var win = await OpenAndSettle(Spec("ExplicitSurvives"), () => comp);
                try
                {
                    H.Check("TitleBarIcon_Survives_ExplicitBefore",
                        comp.Bar?.IconSource is WinUI.FontIconSource);

                    win.Update(win.Spec with { Icon = WindowIcon.FromPath(external) });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);

                    Console.WriteLine($"# survives: after={comp.Bar?.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Survives_ExplicitAfterWindowIconChange",
                        comp.Bar?.IconSource is WinUI.FontIconSource);
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
    //  Two windows, two icons. The pull direction (a descriptor prop resolving
    //  through ReactorApp.ActiveHostInternal) is per-window correct only because
    //  ReactorHost scopes that static around each render. The push direction
    //  runs from ReactorWindow.ApplyChrome, which is NOT inside a render scope —
    //  an app can call win.Update(...) from anywhere — so a window has to resolve
    //  its OWN spec there rather than whatever host happens to be active.
    //  With a single window there is nothing to be wrong about; this is the only
    //  arm that can tell the two apart.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultIsPerWindow(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(90);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var iconA = global::System.IO.Path.Join(scratch, "WindowA.ico");
                var iconB = global::System.IO.Path.Join(scratch, "WindowB.ico");
                var iconC = global::System.IO.Path.Join(scratch, "WindowC.ico");
                foreach (var p in new[] { iconA, iconB, iconC })
                    global::System.IO.File.Copy(TestIcoPath, p, overwrite: true);

                var compA = new BarComponent(static e => e);
                var compB = new BarComponent(static e => e);
                ReactorWindow? winA = null;
                ReactorWindow? winB = null;
                try
                {
                    winA = await OpenAndSettle(
                        Spec("WinA") with { Icon = WindowIcon.FromPath(iconA) }, () => compA);
                    winB = await OpenAndSettle(
                        Spec("WinB") with { Icon = WindowIcon.FromPath(iconB) }, () => compB);

                    H.Check("TitleBarIcon_PerWindow_BothMounted",
                        compA.Bar is not null && compB.Bar is not null);
                    if (compA.Bar is null || compB.Bar is null) return;

                    var a = IconUri(compA.Bar);
                    var b = IconUri(compB.Bar);
                    Console.WriteLine($"# perwindow: A={a}");
                    Console.WriteLine($"# perwindow: B={b}");

                    H.Check($"TitleBarIcon_PerWindow_A_UsesOwnIcon (uri={a})",
                        a is not null
                        && a.LocalPath.EndsWith("WindowA.ico", StringComparison.OrdinalIgnoreCase));
                    H.Check($"TitleBarIcon_PerWindow_B_UsesOwnIcon (uri={b})",
                        b is not null
                        && b.LocalPath.EndsWith("WindowB.ico", StringComparison.OrdinalIgnoreCase));
                    H.Check("TitleBarIcon_PerWindow_TheyDiffer", a != b);

                    // Update only B. A must not follow it, and B must land on its own
                    // new icon rather than on whichever host happened to be active.
                    winB.Update(winB.Spec with { Icon = WindowIcon.FromPath(iconC) });
                    await winB.Host.WaitForIdleAsync();
                    await Harness.Render(200);

                    var aAfter = IconUri(compA.Bar);
                    var bAfter = IconUri(compB.Bar);
                    Console.WriteLine($"# perwindow: A after={aAfter}");
                    Console.WriteLine($"# perwindow: B after={bAfter}");

                    H.Check($"TitleBarIcon_PerWindow_B_TracksItsOwnUpdate (uri={bAfter})",
                        bAfter is not null
                        && bAfter.LocalPath.EndsWith("WindowC.ico", StringComparison.OrdinalIgnoreCase));
                    H.Check($"TitleBarIcon_PerWindow_A_Unaffected (uri={aAfter})",
                        aAfter is not null
                        && aAfter.LocalPath.EndsWith("WindowA.ico", StringComparison.OrdinalIgnoreCase));

                    // The discriminating case. Updating B above proves little on its
                    // own: B was opened last, so the ambient ActiveHostInternal was
                    // already B's and a spec-vs-ambient mix-up would look identical to
                    // correct behaviour. Update the EARLIER window instead, so the
                    // window applying chrome is deliberately not the ambient one — that
                    // is the only arrangement where reading ambient state instead of the
                    // window's own spec produces a visibly wrong icon.
                    var iconD = global::System.IO.Path.Join(scratch, "WindowD.ico");
                    global::System.IO.File.Copy(TestIcoPath, iconD, overwrite: true);
                    winA.Update(winA.Spec with { Icon = WindowIcon.FromPath(iconD) });
                    await winA.Host.WaitForIdleAsync();
                    await Harness.Render(200);

                    var aFinal = IconUri(compA.Bar);
                    var bFinal = IconUri(compB.Bar);
                    Console.WriteLine($"# perwindow: A final={aFinal}");
                    Console.WriteLine($"# perwindow: B final={bFinal}");

                    H.Check($"TitleBarIcon_PerWindow_A_TracksOwnUpdateWhileNotAmbient (uri={aFinal})",
                        aFinal is not null
                        && aFinal.LocalPath.EndsWith("WindowD.ico", StringComparison.OrdinalIgnoreCase));
                    H.Check($"TitleBarIcon_PerWindow_B_UnaffectedByAUpdate (uri={bFinal})",
                        bFinal is not null
                        && bFinal.LocalPath.EndsWith("WindowC.ico", StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    await CloseAndSettle(winB);
                    await CloseAndSettle(winA);
                }
            }
            finally
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(null);
                DeleteScratch(scratch);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Raw .Set(...) setters run AFTER every descriptor prop -- the documented
    //  "setters apply last / win" rule (spec 058, DescriptorHandler.ApplySetters).
    //  So an author writing .Set(b => b.IconSource = ...) owns the slot even
    //  though the element declares no Icon. The out-of-band push from
    //  ApplyChrome has no setters to replay, so it must not clobber a value it
    //  did not write.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultDoesNotClobberSetterIcon(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var first = CreateExternalIcon(scratch);
                var second = global::System.IO.Path.Join(scratch, "Second.ico");
                global::System.IO.File.Copy(TestIcoPath, second, overwrite: true);

                // No .Icon(...) and no .NoIcon() -- the element does not own the slot by
                // declaration, so only the setter marks it as author-owned.
                var comp = new ToggleBarComponent(static (phase, e) => phase == 0
                    ? e.Set(static b => b.IconSource = new WinUI.FontIconSource { Glyph = "\uE8A5" })
                    : e);
                var win = await OpenAndSettle(
                    Spec("SetterIcon") with { Icon = WindowIcon.FromPath(first) }, () => comp);
                try
                {
                    var bar = comp.Bar;
                    H.Check("TitleBarIcon_Setter_BarMounted", bar is not null);
                    if (bar is null) return;

                    // Positive control: the setter must have won at mount, or the arms
                    // below would pass for the wrong reason.
                    Console.WriteLine($"# setter: before={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Setter_WinsAtMount", bar.IconSource is WinUI.FontIconSource);

                    // Drop the setter with the window icon UNTOUCHED. Ordering is the
                    // whole point: the projection still equals what Apply last recorded,
                    // so its equality fast path is live and would skip the write, leaving
                    // the setter's icon stranded forever. Changing the window icon first
                    // makes `projected` differ, routes around the fast path, and tests
                    // nothing — I confirmed that by mutation-checking the other ordering
                    // and watching it stay green.
                    comp.SetPhase?.Invoke(1);
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);

                    Console.WriteLine($"# setter: dropped={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Setter_RemovalRestoresInheritedIcon",
                        bar.IconSource is WinUI.ImageIconSource);

                    // Ownership released: a later window-icon change tracks again.
                    win.Update(win.Spec with { Icon = WindowIcon.FromPath(second) });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    var uri = IconUri(bar);
                    Console.WriteLine($"# setter: tracks again={uri}");
                    H.Check($"TitleBarIcon_Setter_TracksAgainAfterRelease (uri={uri})",
                        uri is not null
                        && uri.LocalPath.EndsWith("Second.ico", StringComparison.OrdinalIgnoreCase));

                    // Re-add the setter, then change the window icon underneath it: the
                    // out-of-band push must not clobber the author's value.
                    comp.SetPhase?.Invoke(0);
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    H.Check("TitleBarIcon_Setter_ReclaimsSlot", bar.IconSource is WinUI.FontIconSource);

                    var third = global::System.IO.Path.Join(scratch, "Third.ico");
                    global::System.IO.File.Copy(TestIcoPath, third, overwrite: true);
                    win.Update(win.Spec with { Icon = WindowIcon.FromPath(third) });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);

                    Console.WriteLine($"# setter: after={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Setter_SurvivesWindowIconChange",
                        bar.IconSource is WinUI.FontIconSource);
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
    //  The null-setter case that value identity alone cannot see. Starting with
    //  no window icon, the inherited projection is null and a setter writing
    //  IconSource = null leaves the control holding the same null reference --
    //  so "is it still the value I wrote?" answers yes for the author's null.
    //  Adding a window icon must still not overwrite it.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultDoesNotClobberNullSetterIcon(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // No convention asset and no spec icon: the projection starts null, which is
            // the arrangement that makes the author's null indistinguishable by identity.
            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var icon = CreateExternalIcon(scratch);

                var comp = new ToggleBarComponent(static (_, e) => e.Set(static b => b.IconSource = null));
                var win = await OpenAndSettle(Spec("NullSetter"), () => comp);
                try
                {
                    var bar = comp.Bar;
                    H.Check("TitleBarIcon_NullSetter_BarMounted", bar is not null);
                    if (bar is null) return;

                    H.Check("TitleBarIcon_NullSetter_StartsNull", bar.IconSource is null);

                    win.Update(win.Spec with { Icon = WindowIcon.FromPath(icon) });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# nullsetter: after update={bar.IconSource?.GetType().Name ?? "<null>"}");

                    // The contract: a setter writing IconSource = null over a null
                    // projection holds the same reference this type wrote, so the push
                    // cannot see it and may write once. The next render re-runs the
                    // setter and ObserveAfterSetters latches ownership — from then on the
                    // author's null is permanent. Assert the settled state, and then that
                    // it stays settled across a further window-icon change.
                    comp.SetPhase?.Invoke(1);
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# nullsetter: after render={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_NullSetter_SetterWinsOnNextRender", bar.IconSource is null);

                    var other = global::System.IO.Path.Join(scratch, "Other.ico");
                    global::System.IO.File.Copy(TestIcoPath, other, overwrite: true);
                    win.Update(win.Spec with { Icon = WindowIcon.FromPath(other) });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# nullsetter: after 2nd update={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_NullSetter_OwnershipLatches", bar.IconSource is null);
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
    //  Same path, different bytes. ApplyChrome reloads the caption's HICON from
    //  disk on every apply, so if the resync skips on Uri equality the title bar
    //  keeps a stale decode of a file the caption has already refreshed — the
    //  exact divergence sharing the resolver is meant to prevent.
    //
    //  The oracle is decoded pixel size, not the URI: the path is unchanged by
    //  construction, so only the decode can tell the two states apart. The test
    //  icon decodes at 32x32 and the replacement PNG at 256x256.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultRefreshesReplacedFile(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            // Ships with the WinUI framework package rather than this host, so the path is
            // asserted rather than assumed — a moved asset must fail loudly, not silently
            // turn this fixture into a no-op.
            var replacement = global::System.IO.Path.Join(
                AppContext.BaseDirectory, "Microsoft.UI.Xaml", "Assets", "NoiseAsset_256x256_PNG.png");
            H.Check($"TitleBarIcon_Replace_ReplacementAssetPresent ({replacement})",
                global::System.IO.File.Exists(replacement));
            if (!global::System.IO.File.Exists(replacement)) return;

            var scratch = CreateScratchAppRoot(withConventionAsset: false);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);
                var iconPath = global::System.IO.Path.Join(scratch, "Swappable.ico");
                global::System.IO.File.Copy(TestIcoPath, iconPath, overwrite: true);

                var comp = new BarComponent(static e => e);
                var win = await OpenAndSettle(
                    Spec("Replace") with { Icon = WindowIcon.FromPath(iconPath) }, () => comp);
                try
                {
                    var bar = comp.Bar;
                    H.Check("TitleBarIcon_Replace_BarMounted", bar is not null);
                    if (bar is null) return;

                    var before = await DecodedSize(bar);
                    Console.WriteLine($"# replace: before={before}");
                    H.Check($"TitleBarIcon_Replace_InitialDecode (px={before})", before > 0);

                    // Same path, different bytes. Changing Title (not Icon) is what makes
                    // this discriminating: the projected Uri is identical, so only a
                    // forced re-decode can move the reading.
                    global::System.IO.File.Copy(replacement, iconPath, overwrite: true);
                    win.Update(win.Spec with { Title = "Replace (updated)" });
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(300);

                    var after = await DecodedSize(bar);
                    Console.WriteLine($"# replace: after={after}");
                    H.Check($"TitleBarIcon_Replace_PicksUpNewBytes (before={before} after={after})",
                        after > 0 && after != before);
                }
                finally { await CloseAndSettle(win); }
            }
            finally
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(null);
                DeleteScratch(scratch);
            }
        }

        /// <summary>Decoded pixel width of the title bar's current image icon, or 0.</summary>
        private static async Task<int> DecodedSize(WinUI.TitleBar bar)
        {
            if ((bar.IconSource as WinUI.ImageIconSource)?.ImageSource is not BitmapImage bmp)
                return 0;
            var (opened, _, w, _) = await AwaitDecode(bmp);
            return opened ? w : 0;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Toggling a MOUNTED title bar between the inherited default, .NoIcon()
    //  and an explicit icon. Every transition must reach the control.
    //
    //  Note this does NOT exercise a shallow-skip hazard: Element.ShallowEquals
    //  (which gates Reconciler.Update's whole-element skip) has no
    //  TitleBarElement arm, so a TitleBar pair falls to `_ => false` and never
    //  skips. TitleBarIconDefaultTests pins that invariant, so if someone later
    //  adds such an arm without comparing Icon/SuppressIcon, the unit test
    //  catches it. This fixture covers the live apply path instead.
    // ════════════════════════════════════════════════════════════════════════
    internal class TitleBarIconDefaultTogglesOnRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var scratch = CreateScratchAppRoot(withConventionAsset: true);
            try
            {
                TitleBarIconDefault.SetBaseDirectoryForTests(scratch);

                // phase 0: inherit. phase 1: .NoIcon(). phase 2: explicit glyph.
                var comp = new ToggleBarComponent(static (phase, e) => phase switch
                {
                    1 => e.NoIcon(),
                    2 => e.Icon(new FontIconData("\uE734", "Segoe Fluent Icons")),
                    _ => e,
                });
                var win = await OpenAndSettle(Spec("Toggle"), () => comp);
                try
                {
                    var bar = comp.Bar;
                    H.Check("TitleBarIcon_Toggle_BarMounted", bar is not null);
                    if (bar is null) return;

                    Console.WriteLine($"# toggle: p0={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Toggle_InheritsAtStart", bar.IconSource is WinUI.ImageIconSource);

                    comp.SetPhase?.Invoke(1);
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# toggle: p1={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Toggle_NoIconClearsMountedIcon", bar.IconSource is null);

                    comp.SetPhase?.Invoke(2);
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# toggle: p2={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Toggle_ExplicitIconApplies", bar.IconSource is WinUI.FontIconSource);

                    comp.SetPhase?.Invoke(0);
                    await win.Host.WaitForIdleAsync();
                    await Harness.Render(200);
                    Console.WriteLine($"# toggle: p0again={bar.IconSource?.GetType().Name ?? "<null>"}");
                    H.Check("TitleBarIcon_Toggle_ReturnsToInherited", bar.IconSource is WinUI.ImageIconSource);
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
}
