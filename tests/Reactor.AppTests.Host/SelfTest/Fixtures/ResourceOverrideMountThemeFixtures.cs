using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount-theme bug — a ThemeRef-backed <c>ResourceOverrides</c>
/// (<c>.Resources(r =&gt; r.Set(key, Theme.Ref(srcKey)))</c>) on a subtree whose
/// effective theme comes from an <b>ancestor's</b> <c>RequestedTheme</c> (not the
/// element's own) must resolve its concrete brush against that ancestor theme at
/// mount — NOT the app fallback.
///
/// <para>ROOT CAUSE: <c>ApplyResourceOverrides</c> resolved via
/// <c>ThemeRef.Resolve(key, fe)</c>, whose effective-theme walk needs the element to
/// be parented. At mount the control is not yet in the visual tree, so an ancestor
/// <c>RequestedTheme</c> is unreachable and resolution falls back to
/// <c>Application.Current.RequestedTheme</c>. A subtree with ancestor
/// <c>RequestedTheme=Dark</c> under a Light app got the LIGHT brush. FIX: the
/// reconciler threads the ancestor-aware effective theme top-down
/// (<c>_ambientRequestedTheme</c>) and passes it to <c>ApplyResourceOverrides</c>,
/// which resolves via <c>ThemeRef.Resolve(key, isDark)</c>.</para>
///
/// <para>DETERMINISM: the ancestor theme is always set to the OPPOSITE of the live
/// app theme, so "resolve against ancestor" and "resolve against app" produce
/// different brushes regardless of whether the host app runs Light or Dark. A unique
/// themed <c>ResourceDictionary</c> (explicit Light + Dark <c>ThemeDictionaries</c>)
/// is merged into <c>Application.Current.Resources</c> so <c>ThemeRef.Resolve</c>
/// returns the exact shared brush instance per theme — asserted by reference.
/// RED before the fix (resolves the app variant), GREEN after (the ancestor variant).</para>
/// </summary>
internal static class ResourceOverrideMountThemeFixtures
{
    private static SolidColorBrush MakeBrush(byte r, byte g, byte b) =>
        new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, r, g, b));

    private static SolidColorBrush? ResourceBrush(FrameworkElement? fe, string key)
    {
        if (fe?.Resources is { } res && res.TryGetValue(key, out var v))
            return v as SolidColorBrush;
        return null;
    }

    // The app-level ResourceDictionary has a Source (XamlControlsResources) and rejects
    // direct local values, but a merged Source-less dictionary is fine. Install one whose
    // ThemeDictionaries carry a different brush per theme under a unique key; ThemeRef.Resolve
    // discovers it via its ThemeDictionaries scan over MergedDictionaries. #660 — the
    // (key,theme)->Brush cache is invalidated on install so a prior run's entry can't leak.
    private static ResourceDictionary InstallThemedDict(string key, SolidColorBrush light, SolidColorBrush dark)
    {
        var dict = new ResourceDictionary();
        dict.ThemeDictionaries["Light"] = new ResourceDictionary { [key] = light };
        dict.ThemeDictionaries["Dark"] = new ResourceDictionary { [key] = dark };
        Application.Current.Resources.MergedDictionaries.Add(dict);
        global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        return dict;
    }

    // ancestorTheme is the OPPOSITE of the live app theme; the matching ancestor brush is the
    // value the fix must resolve, and the app-theme brush is the (wrong) pre-fix value.
    private static (ElementTheme ancestorTheme, SolidColorBrush ancestorBrush, SolidColorBrush appBrush)
        AncestorAgainstApp(SolidColorBrush light, SolidColorBrush dark)
    {
        var appIsDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return appIsDark
            ? (ElementTheme.Light, light, dark)
            : (ElementTheme.Dark, dark, light);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Single-level inheritance — RED pre-fix, GREEN post-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A child carrying ONLY a ThemeRef-backed ResourceOverride (no own RequestedTheme)
    /// sits directly under an ancestor whose RequestedTheme is the opposite of the app
    /// theme. At mount the child must resolve the ancestor brush. RED pre-fix (resolved
    /// the app brush via the unparented fe walk falling back to app theme), GREEN after.
    /// </summary>
    internal sealed class AncestorThemeResolvesAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_Single";
        private const string TargetKey = "Item1Target_Single";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 220);
            var dark = MakeBrush(220, 0, 0);
            var (ancestorTheme, ancestorBrush, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        TextBlock("singleChild")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                    .RequestedTheme(ancestorTheme));

                await Harness.Render();

                var tb = H.FindControl<TextBlock>(t => t.Text == "singleChild");
                H.Check("Item1Single_ChildPresent", tb is not null);
                var resolved = ResourceBrush(tb, TargetKey);
                // The crux: ancestor brush (opposite of app), not the app fallback.
                H.Check("Item1Single_ResolvedAncestorBrush", ReferenceEquals(resolved, ancestorBrush));
                H.Check("Item1Single_NotAppBrush", !ReferenceEquals(resolved, appBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Multi-level inheritance through a themeless intermediate — RED pre-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The ancestor RequestedTheme is inherited across an intermediate container that
    /// declares no theme of its own. The deeply-nested child's override must still
    /// resolve the ancestor brush at mount — proving the ambient threads through the
    /// whole subtree, not just one level.
    /// </summary>
    internal sealed class NestedAncestorThemeResolvesAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_Nested";
        private const string TargetKey = "Item1Target_Nested";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 200);
            var dark = MakeBrush(200, 0, 0);
            var (ancestorTheme, ancestorBrush, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        VStack( // intermediate: no RequestedTheme of its own
                            TextBlock("nestedChild")
                                .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))))
                    .RequestedTheme(ancestorTheme));

                await Harness.Render();

                var tb = H.FindControl<TextBlock>(t => t.Text == "nestedChild");
                H.Check("Item1Nested_ChildPresent", tb is not null);
                var resolved = ResourceBrush(tb, TargetKey);
                H.Check("Item1Nested_ResolvedAncestorBrush", ReferenceEquals(resolved, ancestorBrush));
                H.Check("Item1Nested_NotAppBrush", !ReferenceEquals(resolved, appBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Element's OWN RequestedTheme still wins — regression guard (GREEN both)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the override-bearing element declares its OWN RequestedTheme (differing from
    /// the ancestor), its own theme must win — the ambient only supplies the inherited
    /// fallback. This stayed correct pre-fix (fe.RequestedTheme is set before
    /// ApplyResourceOverrides) and must remain correct after; a guard that the fix never
    /// lets an inherited ancestor theme override an explicit own theme.
    /// </summary>
    internal sealed class OwnThemeWinsAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_OwnWins";
        private const string TargetKey = "Item1Target_OwnWins";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 180);
            var dark = MakeBrush(180, 0, 0);
            // Ancestor = opposite of app; the child's OWN theme = the app theme (so it
            // differs from the ancestor). The child must resolve its OWN-theme brush.
            var (ancestorTheme, _, ownBrush) = AncestorAgainstApp(light, dark);
            var ownTheme = ancestorTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            var ancestorBrush = ownTheme == ElementTheme.Dark ? light : dark; // the brush the child must NOT pick
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        TextBlock("ownChild")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))
                            .RequestedTheme(ownTheme))
                    .RequestedTheme(ancestorTheme));

                await Harness.Render();

                var tb = H.FindControl<TextBlock>(t => t.Text == "ownChild");
                H.Check("Item1OwnWins_ChildPresent", tb is not null);
                var resolved = ResourceBrush(tb, TargetKey);
                H.Check("Item1OwnWins_ResolvedOwnBrush", ReferenceEquals(resolved, ownBrush));
                H.Check("Item1OwnWins_NotAncestorBrush", !ReferenceEquals(resolved, ancestorBrush));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Post-mount ancestor theme toggle re-resolves through BOTH Update paths
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The mount fix also threads the effective theme through the UPDATE path. Toggling an
    /// ancestor's <c>RequestedTheme</c> on a later render must re-resolve the descendants'
    /// ThemeRef ResourceOverrides — through BOTH arms of <c>Reconciler.Update</c>:
    /// <list type="bullet">
    /// <item>a child that stays shallow-equal (constant text) is declined by
    /// <c>CanSkipUpdate</c> (it carries ThemeRefs) and routed to Update's element-level
    /// shallow-skip arm, which re-applies against the ancestor-aware effective theme;</item>
    /// <item>a child that also changes a prop (text carries a counter) takes the full-update
    /// branch, which re-applies the same way.</item>
    /// </list>
    /// Uses concrete ThemeRef overrides (NOT self-healing {ThemeResource}), so the brush
    /// only flips if Reactor actually re-resolves — and does so lag-free via the threaded
    /// ambient rather than fe's ActualTheme view. Also guards the MUST-FIX exception-safety
    /// refactor of the ambient save/restore in Update.
    /// </summary>
    internal sealed class PostMountAncestorToggleReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_Toggle";
        private const string TargetKey = "Item1Target_Toggle";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 160);
            var dark = MakeBrush(160, 0, 0);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (theme, setTheme) = ctx.UseState(ElementTheme.Dark);
                    var (counter, setCounter) = ctx.UseState(0);
                    return VStack(
                        Button("ToggleAncestorTheme", () =>
                        {
                            setTheme(theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark);
                            setCounter(counter + 1);
                        }),
                        VStack(
                            // Constant text → shallow-equal across renders → Update's
                            // element-level shallow-skip arm re-resolves via the ambient.
                            TextBlock("toggleSkipChild").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))),
                            // Text carries the counter → NOT shallow-equal → Update's
                            // full-update branch re-resolves via the ambient.
                            TextBlock($"toggleFullChild-{counter}").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                        .RequestedTheme(theme));
                });

                await Harness.Render();

                // Ancestor Dark at mount → both children resolve the Dark brush.
                var skipTb = H.FindControl<TextBlock>(t => t.Text == "toggleSkipChild");
                var fullTb = H.FindControl<TextBlock>(t => t.Text == "toggleFullChild-0");
                H.Check("Item1Toggle_MountChildrenPresent", skipTb is not null && fullTb is not null);
                H.Check("Item1Toggle_SkipChildMountDark", ReferenceEquals(ResourceBrush(skipTb, TargetKey), dark));
                H.Check("Item1Toggle_FullChildMountDark", ReferenceEquals(ResourceBrush(fullTb, TargetKey), dark));

                // Toggle ancestor Dark→Light (and bump the counter so fullChild changes a prop).
                H.ClickButton("ToggleAncestorTheme");

                // Shallow-equal child re-resolves to Light via Update's shallow-skip arm.
                var skipReResolved = await Harness.WaitFor(() =>
                {
                    var tb = H.FindControl<TextBlock>(t => t.Text == "toggleSkipChild");
                    return ReferenceEquals(ResourceBrush(tb, TargetKey), light);
                });
                H.Check("Item1Toggle_SkipChildReResolvedLight", skipReResolved);

                // Prop-changed child re-resolves to Light via Update's full-update branch.
                var fullReResolved = await Harness.WaitFor(() =>
                {
                    var tb = H.FindControl<TextBlock>(t => t.Text == "toggleFullChild-1");
                    return ReferenceEquals(ResourceBrush(tb, TargetKey), light);
                });
                H.Check("Item1Toggle_FullChildReResolvedLight", fullReResolved);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Host-inherited theme — RequestedTheme set OUTSIDE the Reactor tree
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An app sets <c>RequestedTheme</c> on a native ancestor of a
    /// <c>ReactorHostControl</c> (typically the window root) rather than inside the
    /// Reactor tree. ThemeRef-backed resource overrides must resolve against that
    /// inherited theme at mount, whether the host renders after it is parented or
    /// renders while detached and is parented later. The parented arm is RED before
    /// the fix (it resolved the app-theme brush and stayed stale, since no theme change
    /// follows) and GREEN after (the reconciler seeds the host-inherited theme for root
    /// passes). The detached arm requires a skip-bypassing resource refresh even if
    /// a theme-event render runs before Loaded. The
    /// realized-row arm covers ItemsView rows, which realize during layout outside any
    /// root pass and inherit the repeater's theme.
    /// </summary>
    internal sealed class HostInheritedThemeResolvesAtMount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "HostInheritedThemedBrush";
        private const string TargetKey = "HostInheritedTarget";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 120);
            var dark = MakeBrush(120, 0, 0);
            var (ancestorTheme, ancestorBrush, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                // Arm 1: parented under the themed native ancestor before the first render.
                var parentedHost = new ReactorHostControl();
                H.SetContent(new Border { RequestedTheme = ancestorTheme, Child = parentedHost });
                parentedHost.Mount(ctx => Border(TextBlock("hostParented"))
                    .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))));
                await parentedHost.WaitForIdleAsync();
                var parentedResolved = ResourceBrush(parentedHost.Content as FrameworkElement, TargetKey);
                H.Check("HostTheme_ParentedResolvedAncestorBrush", ReferenceEquals(parentedResolved, ancestorBrush));
                H.Check("HostTheme_ParentedNotAppBrush", !ReferenceEquals(parentedResolved, appBrush));
                H.SetContent(null);
                parentedHost.Dispose();

                // Arm 2: rendered while detached, then parented under the themed ancestor.
                var detachedHost = new ReactorHostControl();
                // Keep the root identical: a theme-event render alone skips its child.
                var detachedTree = Border(Border(TextBlock("hostDetached"))
                    .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))));
                detachedHost.Mount(ctx => detachedTree);
                await detachedHost.WaitForIdleAsync();
                var detachedChild = (detachedHost.Content as Border)?.Child as FrameworkElement;
                H.Check("HostTheme_DetachedInitiallyAppBrush",
                    ReferenceEquals(ResourceBrush(detachedChild, TargetKey), appBrush));
                var detachedParent = new Border { RequestedTheme = ancestorTheme, Child = detachedHost };
                H.SetContent(detachedParent);
                var reResolved = await Harness.WaitFor(() =>
                    ReferenceEquals(ResourceBrush(detachedChild, TargetKey), ancestorBrush));
                H.Check("HostTheme_DetachedReResolvedOnLoaded", reResolved);
                detachedParent.RequestedTheme = ElementTheme.Default;
                H.Check("HostTheme_NestedOverrideFollowsAppTheme", await Harness.WaitFor(() =>
                    ReferenceEquals(ResourceBrush(detachedChild, TargetKey), appBrush)));
                detachedParent.RequestedTheme = ancestorTheme;
                H.Check("HostTheme_NestedOverrideFollowsAncestorToggle", await Harness.WaitFor(() =>
                    ReferenceEquals(ResourceBrush(detachedChild, TargetKey), ancestorBrush)));
                H.SetContent(null);
                detachedHost.Dispose();

                // Arm 3: rows an ItemsView realizes during layout, outside any root pass.
                var listHost = new ReactorHostControl();
                var listParent = new Border { RequestedTheme = ancestorTheme, Child = listHost };
                H.SetContent(listParent);
                var listTree = ItemsView(new[] { "rowA", "rowB" }, static s => s, (s, _) =>
                        ItemContainer(Border(TextBlock(s)).Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                            .WithKey(s))
                    .Height(200);
                listHost.Mount(ctx => listTree);
                await listHost.WaitForIdleAsync();
                var rowResolved = await Harness.WaitFor(() =>
                    H.FindControl<TextBlock>(t => t.Text == "rowA")?.Parent is FrameworkElement row &&
                    ReferenceEquals(ResourceBrush(row, TargetKey), ancestorBrush));
                H.Check("HostTheme_RealizedRowResolvedAncestorBrush", rowResolved);
                listParent.RequestedTheme = ElementTheme.Default;
                H.Check("HostTheme_RealizedRowFollowsAppTheme", await Harness.WaitFor(() =>
                    H.FindControl<TextBlock>(t => t.Text == "rowA")?.Parent is FrameworkElement row &&
                    ReferenceEquals(ResourceBrush(row, TargetKey), appBrush)));
                listParent.RequestedTheme = ancestorTheme;
                H.Check("HostTheme_RealizedRowFollowsAncestorToggle", await Harness.WaitFor(() =>
                    H.FindControl<TextBlock>(t => t.Text == "rowA")?.Parent is FrameworkElement row &&
                    ReferenceEquals(ResourceBrush(row, TargetKey), ancestorBrush)));
                H.SetContent(null);
                listHost.Dispose();
            }
            finally
            {
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    internal sealed class HostInheritedThemeReviewRegressions(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "HostReviewBrush";
        private const string TargetKey = "HostReviewTarget";

        private static Element View(string text) =>
            Border(TextBlock(text).Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))));

        private bool HasBrush(string text, SolidColorBrush brush) =>
            ReferenceEquals(ResourceBrush(H.FindControl<TextBlock>(t => t.Text == text), TargetKey), brush);

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 50, 120);
            var dark = MakeBrush(120, 50, 0);
            var (theme, expected, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var target = new Border();
                var parent = new Border { RequestedTheme = theme, Child = target };
                H.SetContent(parent);
                using (var host = new ReactorHost(H.Window) { ContentTarget = target })
                {
                    var tree = View("targetHost");
                    host.Mount(_ => tree);
                    H.Check("HostReview_ContentTargetInitialTheme", await Harness.WaitFor(() => HasBrush("targetHost", expected)));
                    parent.RequestedTheme = ElementTheme.Default;
                    H.Check("HostReview_ContentTargetDefault", await Harness.WaitFor(() => HasBrush("targetHost", appBrush)));
                    parent.RequestedTheme = theme;
                    host.RenderErrorHandler = _ => View("targetFallback");
                    host.Mount(_ => throw new InvalidOperationException("root theme regression"));
                    H.Check("HostReview_ContentTargetFallback", await Harness.WaitFor(() => HasBrush("targetFallback", expected)));
                }

                foreach (var failDuringReconcile in new[] { false, true })
                {
                    SolidColorBrush? mountedBrush = null;
                    using var host = new ReactorHostControl
                    {
                        RenderErrorHandler = _ => View("controlFallback").OnMount(fe =>
                            mountedBrush = ResourceBrush((fe as Border)?.Child as FrameworkElement, TargetKey))
                    };
                    var fallbackParent = new Border { RequestedTheme = theme, Child = host };
                    H.SetContent(fallbackParent);
                    if (failDuringReconcile)
                        host.Mount(_ => TextBlock("never mounted").Set(_ => throw new InvalidOperationException("mount theme regression")));
                    else
                        host.Mount(_ => throw new InvalidOperationException("root theme regression"));
                    await host.WaitForIdleAsync();
                    H.Check($"HostReview_ControlFallbackAtMount_Reconcile={failDuringReconcile}", ReferenceEquals(mountedBrush, expected));
                    fallbackParent.RequestedTheme = ElementTheme.Default;
                    H.Check($"HostReview_ControlFallbackDefault_Reconcile={failDuringReconcile}", await Harness.WaitFor(() => HasBrush("controlFallback", appBrush)));
                    H.SetContent(null);
                }

                var rows = new[] { "typedList", "typedGrid", "typedTree", "list", "grid" };
                Element[] lists =
                [
                    ListView(new[] { rows[0] }, static s => s, (s, _) => View(s)).Height(150),
                    GridView(new[] { rows[1] }, static s => s, (s, _) => View(s)).Height(150),
                    TreeView(new[] { rows[2] }, keySelector: static s => s,
                        childrenSelector: static _ => Array.Empty<string>(), viewBuilder: s => View(s)).Height(150),
                    ListView(View(rows[3])).Height(150),
                    GridView(View(rows[4])).Height(150),
                ];
                for (var i = 0; i < lists.Length; i++)
                {
                    using var host = new ReactorHostControl();
                    H.SetContent(new Border { RequestedTheme = theme, Child = host });
                    var list = lists[i];
                    host.Mount(_ => list);
                    H.Check($"HostReview_Container_{rows[i]}", await Harness.WaitFor(() => HasBrush(rows[i], expected)));
                    H.SetContent(null);
                }
            }
            finally
            {
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    internal sealed class HostInheritedThemePooledRows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string source = "HostPoolBrush";
            const string target = "HostPoolTarget";
            var light = MakeBrush(0, 80, 120);
            var dark = MakeBrush(120, 80, 0);
            var (theme, expected, appBrush) = AncestorAgainstApp(light, dark);
            var dict = InstallThemedDict(source, light, dark);
            using var reconciler = new Reconciler();
            var anchor = new Border { RequestedTheme = theme };
            H.SetContent(anchor);
            var tree = Border(TextBlock("pooled").Resources(r => r.Set(target, Theme.Ref(source))));
            var factory = new ElementFactory<int>(new[] { 0, 1 }, (_, _) => tree, reconciler, static () => { }, null);
            try
            {
                var first = factory.GetElement(new ElementFactoryGetArgs { Data = 0, Parent = anchor });
                anchor.Child = first;
                H.Check("HostPool_InitialBrush", ReferenceEquals(ResourceBrush((first as Border)?.Child as FrameworkElement, target), expected));
                factory.RecycleElement(new ElementFactoryRecycleArgs { Element = first, Parent = anchor });
                anchor.RequestedTheme = ElementTheme.Default;
                var reused = factory.GetElement(new ElementFactoryGetArgs { Data = 0, Parent = anchor });
                H.Check("HostPool_ActuallyReused", ReferenceEquals(first, reused));
                H.Check("HostPool_RefreshNestedOverride", ReferenceEquals(ResourceBrush((reused as Border)?.Child as FrameworkElement, target), appBrush));

                // Force an update that mounts a new descendant, rather than relying on
                // the existing visual ancestry to repair the brush after parenting.
                factory.RecycleElement(new ElementFactoryRecycleArgs { Element = reused, Parent = anchor });
                anchor.RequestedTheme = theme;
                factory.UpdateInPlace(new[] { 0, 1 }, (_, _) =>
                    Border(Border(TextBlock("replacement").Resources(r => r.Set(target, Theme.Ref(source))))));
                var replacement = factory.GetElement(new ElementFactoryGetArgs { Data = 1, Parent = anchor });
                H.Check("HostPool_ReplacementUsesSameContainer", ReferenceEquals(first, replacement));
                var descendant = ((replacement as Border)?.Child as Border)?.Child as FrameworkElement;
                H.Check("HostPool_NewDescendantInheritsTheme", ReferenceEquals(ResourceBrush(descendant, target), expected));
            }
            finally
            {
                if (anchor.Child is { } child)
                    reconciler.UnmountChild(child);
                H.SetContent(null);
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Sibling isolation — the ambient save/restore must not leak across peers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two sibling subtrees under different <c>RequestedTheme</c>s, each with a ThemeRef
    /// override, in a single render. Each must resolve its OWN ancestor brush — proving the
    /// per-frame ambient save/restore (and the exception-safe field placement) never leaks
    /// the first sibling's theme into the second's subtree.
    /// </summary>
    internal sealed class SiblingSubtreesResolveIndependently(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SrcKey = "Item1ThemedBrush_Sibling";
        private const string TargetKey = "Item1Target_Sibling";

        public override async Task RunAsync()
        {
            var light = MakeBrush(0, 0, 140);
            var dark = MakeBrush(140, 0, 0);
            var dict = InstallThemedDict(SrcKey, light, dark);
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx =>
                    VStack(
                        VStack(TextBlock("siblingDark").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                            .RequestedTheme(ElementTheme.Dark),
                        VStack(TextBlock("siblingLight").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))))
                            .RequestedTheme(ElementTheme.Light)));

                await Harness.Render();

                var darkTb = H.FindControl<TextBlock>(t => t.Text == "siblingDark");
                var lightTb = H.FindControl<TextBlock>(t => t.Text == "siblingLight");
                H.Check("Item1Sibling_ChildrenPresent", darkTb is not null && lightTb is not null);
                H.Check("Item1Sibling_DarkSubtreeResolvedDark", ReferenceEquals(ResourceBrush(darkTb, TargetKey), dark));
                H.Check("Item1Sibling_LightSubtreeResolvedLight", ReferenceEquals(ResourceBrush(lightTb, TargetKey), light));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(dict);
                global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
            }
        }
    }
}
