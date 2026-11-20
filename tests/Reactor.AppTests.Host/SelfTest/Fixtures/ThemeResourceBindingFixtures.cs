using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;
using Color = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <see cref="ThemeRef"/> bindings installed with <c>FrameworkElement.SetThemeResourceBinding</c>.
/// The binding sits at local-value precedence (the old synthesized Style sat below it), so these
/// pin that the old winners still win, that an author Style is no longer replaced, and that the
/// binding stays live through a native theme change. Each scenario uses its own brush key with
/// distinct Light and Dark values, so a color names exactly which source produced it.
/// </summary>
internal static class ThemeResourceBindingFixtures
{
    private static readonly Color LightColor = Color.FromArgb(255, 210, 220, 230);
    private static readonly Color DarkColor = Color.FromArgb(255, 30, 40, 50);
    private static readonly Color HoverColor = Color.FromArgb(255, 0, 160, 0);
    private static readonly Color ExplicitColor = Color.FromArgb(255, 200, 30, 40);

    private static Color? ColorOf(object? brush) => (brush as SolidColorBrush)?.Color;

    private static bool HasLocal(DependencyObject? d, DependencyProperty p) =>
        d is not null && !ReferenceEquals(d.ReadLocalValue(p), DependencyProperty.UnsetValue);

    private static readonly Color OtherLightColor = Color.FromArgb(255, 120, 60, 200);
    private static readonly Color OtherDarkColor = Color.FromArgb(255, 60, 30, 100);

    /// <summary>Runs <paramref name="body"/> with <paramref name="key"/> defined in the app's theme dictionaries.</summary>
    private static Task WithThemeKey(Harness h, string key, Func<ReactorHost, Task> body)
        => WithThemeKeys(h, [key], null, (host, _) => body(host));

    /// <summary>
    /// Runs <paramref name="body"/> with <paramref name="key"/> (Light/Dark colors) and, when given,
    /// <paramref name="otherKey"/> (OtherLight/OtherDark colors) defined in the app's theme
    /// dictionaries. The body receives the Light dictionary so it can edit an entry.
    /// </summary>
    private static async Task WithThemeKeys(Harness h, string[] keys, string? otherKey, Func<ReactorHost, ResourceDictionary, Task> body)
    {
        var resources = new ResourceDictionary();
        var lightDict = new ResourceDictionary();
        var darkDict = new ResourceDictionary();
        foreach (var key in keys)
        {
            lightDict[key] = new SolidColorBrush(LightColor);
            darkDict[key] = new SolidColorBrush(DarkColor);
        }
        if (otherKey is not null)
        {
            lightDict[otherKey] = new SolidColorBrush(OtherLightColor);
            darkDict[otherKey] = new SolidColorBrush(OtherDarkColor);
        }
        resources.ThemeDictionaries["Light"] = lightDict;
        resources.ThemeDictionaries["Dark"] = darkDict;
        Application.Current.Resources.MergedDictionaries.Add(resources);
        using var host = h.CreateHost();
        try
        {
            await body(host, lightDict);
        }
        finally
        {
            h.SetContent(null);
            Application.Current.Resources.MergedDictionaries.Remove(resources);
            await Harness.Render();
        }
    }

    /// <summary>
    /// <c>.Foreground(brush)</c> beats <c>.Foreground(ThemeRef)</c> in either call order, and
    /// dropping the brush hands the property to a live theme binding.
    /// </summary>
    internal sealed class ModifierWinsInEitherOrder(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbModifierOrderBrush", async host =>
        {
            var key = new ThemeRef("TrbModifierOrderBrush");
            Action<bool>? setExplicit = null;
            host.Mount(ctx =>
            {
                var (withExplicit, setter) = ctx.UseState(true);
                setExplicit = setter;
                Element brushFirst = withExplicit
                    ? TextBlock("trb-brush-first").Foreground(new SolidColorBrush(ExplicitColor)).Foreground(key)
                    : TextBlock("trb-brush-first").Foreground(key);
                Element themeFirst = withExplicit
                    ? TextBlock("trb-theme-first").Foreground(key).Foreground(new SolidColorBrush(ExplicitColor))
                    : TextBlock("trb-theme-first").Foreground(key);
                return VStack(brushFirst, themeFirst).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var a = H.FindText("trb-brush-first");
            var b = H.FindText("trb-theme-first");
            H.Check("ThemeResourceBinding_ModifierOrder_BrushFirstWins", ColorOf(a?.Foreground) == ExplicitColor,
                $"fg={ColorOf(a?.Foreground)}");
            H.Check("ThemeResourceBinding_ModifierOrder_ThemeFirstWins", ColorOf(b?.Foreground) == ExplicitColor,
                $"fg={ColorOf(b?.Foreground)}");

            setExplicit!(false);
            await Harness.Render();
            H.Check("ThemeResourceBinding_ModifierOrder_RemovedBrushFallsToTheme",
                ColorOf(a?.Foreground) == LightColor && ColorOf(b?.Foreground) == LightColor,
                $"a={ColorOf(a?.Foreground)} b={ColorOf(b?.Foreground)}");

            // Native theme change, no Reactor render: the binding must still be live.
            ((FrameworkElement)VisualTreeHelper.GetParent(a))!.RequestedTheme = ElementTheme.Dark;
            H.Check("ThemeResourceBinding_ModifierOrder_BindingLive", await Harness.WaitFor(() =>
                ColorOf(a?.Foreground) == DarkColor && ColorOf(b?.Foreground) == DarkColor));
        });
    }

    /// <summary>
    /// Replacing one ThemeRef with an explicit brush while another ThemeRef stays: the brush
    /// that ApplyModifiers just wrote must not be cleared as a stale binding.
    /// </summary>
    internal sealed class ModifierReplacingThemeRefKeepsBrush(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbReplaceBrush", async host =>
        {
            var key = new ThemeRef("TrbReplaceBrush");
            Action<bool>? setReplaced = null;
            host.Mount(ctx =>
            {
                var (replaced, setter) = ctx.UseState(false);
                setReplaced = setter;
                var grid = Grid([GridSize.Star()], [GridSize.Star()], TextBlock("trb-replace")).BorderBrush(key);
                return replaced
                    ? grid.Background(new SolidColorBrush(ExplicitColor))
                    : grid.Background(key);
            });
            await Harness.Render();

            var grid = H.FindControl<WinUI.Grid>(g => g.Children.Count == 1
                && g.Children[0] is WinUI.TextBlock { Text: "trb-replace" });
            H.Check("ThemeResourceBinding_Replace_BoundAtMount",
                ColorOf(grid?.Background) == LightColor && ColorOf(grid?.BorderBrush) == LightColor);

            setReplaced!(true);
            await Harness.Render();
            H.Check("ThemeResourceBinding_Replace_ExplicitBrushKept", ColorOf(grid?.Background) == ExplicitColor,
                $"bg={ColorOf(grid?.Background)} local={HasLocal(grid, WinUI.Panel.BackgroundProperty)}");
            H.Check("ThemeResourceBinding_Replace_OtherBindingKept", ColorOf(grid?.BorderBrush) == LightColor);
        });
    }

    /// <summary>
    /// A <c>.Set</c> setter writing the same property keeps beating the ThemeRef, as a local
    /// value beat the old Style setter.
    /// </summary>
    internal sealed class SetterValueWins(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbSetterBrush", async host =>
        {
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            host.Mount(_ => VStack(
                Border(TextBlock("trb-setter"))
                    .Set(b => b.Background = explicitBrush)
                    .Background(new ThemeRef("TrbSetterBrush"))));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-setter" });
            H.Check("ThemeResourceBinding_Setter_Wins", ReferenceEquals(border?.Background, explicitBrush),
                $"bg={ColorOf(border?.Background)}");
        });
    }

    /// <summary>
    /// A ThemeRef no longer replaces an author <c>Style</c>: the Style's other setters apply,
    /// and dropping the ThemeRef leaves the Style in place.
    /// </summary>
    internal sealed class AuthorStyleKept(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbStyleBrush", async host =>
        {
            var style = new Style(typeof(WinUI.TextBlock));
            style.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 31d));
            Action<bool>? setThemed = null;
            host.Mount(ctx =>
            {
                var (themed, setter) = ctx.UseState(true);
                setThemed = setter;
                var tb = TextBlock("trb-style").Set(t => t.Style = style);
                return VStack(themed ? tb.Foreground(new ThemeRef("TrbStyleBrush")) : tb);
            });
            await Harness.Render();

            var tb = H.FindText("trb-style");
            H.Check("ThemeResourceBinding_Style_AuthorStyleKept",
                tb is not null && ReferenceEquals(tb.Style, style) && Math.Abs(tb.FontSize - 31) < 0.01,
                $"fontSize={tb?.FontSize}");
            H.Check("ThemeResourceBinding_Style_ThemeRefApplied", ColorOf(tb?.Foreground) == LightColor);

            setThemed!(false);
            await Harness.Render();
            var tb1 = H.FindText("trb-style");
            H.Check("ThemeResourceBinding_Style_KeptAfterRemoval",
                ReferenceEquals(tb1, tb) && ReferenceEquals(tb1?.Style, style)
                && !HasLocal(tb1, WinUI.TextBlock.ForegroundProperty));
        });
    }

    /// <summary>
    /// The binding is installed at mount, before the control is parented, so it first resolves
    /// against the app theme. It must re-resolve against the ancestor's <c>RequestedTheme</c>
    /// once the control joins the tree.
    /// </summary>
    internal sealed class MountResolvesAncestorTheme(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbMountBrush", async host =>
        {
            host.Mount(_ => VStack(
                VStack(TextBlock("trb-mount-dark").Foreground(new ThemeRef("TrbMountBrush"))).RequestedTheme(ElementTheme.Dark),
                VStack(TextBlock("trb-mount-light").Foreground(new ThemeRef("TrbMountBrush"))).RequestedTheme(ElementTheme.Light)));
            await Harness.Render();

            var dark = H.FindText("trb-mount-dark");
            var light = H.FindText("trb-mount-light");
            H.Check("ThemeResourceBinding_Mount_DarkAncestorHonoured", ColorOf(dark?.Foreground) == DarkColor,
                $"fg={ColorOf(dark?.Foreground)} actual={dark?.ActualTheme}");
            H.Check("ThemeResourceBinding_Mount_LightAncestorHonoured", ColorOf(light?.Foreground) == LightColor,
                $"fg={ColorOf(light?.Foreground)} actual={light?.ActualTheme}");
        });
    }

    /// <summary>
    /// InteractionStates writes the hover brush over the binding; returning to Normal must
    /// re-install the binding rather than the brush captured at setup, which would freeze the
    /// background at the theme it had then.
    /// </summary>
    internal sealed class InteractionStatesRestoreKeepsBindingLive(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbHoverBrush", async host =>
        {
            var hover = new SolidColorBrush(HoverColor);
            host.Mount(_ => VStack(
                Border(TextBlock("trb-hover"))
                    .Background(new ThemeRef("TrbHoverBrush"))
                    .InteractionStates(s => s.PointerOver(background: hover))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-hover" });
            H.Check("ThemeResourceBinding_Hover_Present", border is not null);
            if (border is null) return;
            H.Check("ThemeResourceBinding_Hover_NormalIsTheme", ColorOf(border.Background) == LightColor);

            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            H.Check("ThemeResourceBinding_Hover_HoverBrush", ReferenceEquals(border.Background, hover));
            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeResourceBinding_Hover_NormalRestored", ColorOf(border.Background) == LightColor);

            ((FrameworkElement)VisualTreeHelper.GetParent(border))!.RequestedTheme = ElementTheme.Dark;
            H.Check("ThemeResourceBinding_Hover_BindingLiveAfterRestore",
                await Harness.WaitFor(() => ColorOf(border.Background) == DarkColor),
                $"bg={ColorOf(border.Background)}");
        });
    }

    /// <summary>
    /// <c>SplitView.PaneBackground(ThemeRef)</c> binds the pane background (the old Style path
    /// had no mapping for it and silently did nothing), including when it replaces an explicit
    /// <c>.PaneBackground(brush)</c> on the same control.
    /// </summary>
    internal sealed class SplitViewPaneBackground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbPaneBrush", async host =>
        {
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            Action<bool>? setThemed = null;
            host.Mount(ctx =>
            {
                var (themed, setter) = ctx.UseState(false);
                setThemed = setter;
                var sv = SplitView(TextBlock("trb-pane"), TextBlock("trb-content")).Height(80);
                return VStack(themed ? sv.PaneBackground(new ThemeRef("TrbPaneBrush")) : sv.PaneBackground(explicitBrush))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var sv = H.FindControl<WinUI.SplitView>(_ => true);
            H.Check("ThemeResourceBinding_Pane_ExplicitBrush", ReferenceEquals(sv?.PaneBackground, explicitBrush));

            setThemed!(true);
            await Harness.Render();
            H.Check("ThemeResourceBinding_Pane_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeResourceBinding_Pane_Bound", ColorOf(sv?.PaneBackground) == LightColor,
                $"pane={ColorOf(sv?.PaneBackground)}");
            if (sv is null) return;
            ((FrameworkElement)VisualTreeHelper.GetParent(sv))!.RequestedTheme = ElementTheme.Dark;
            H.Check("ThemeResourceBinding_Pane_FollowsTheme",
                await Harness.WaitFor(() => ColorOf(sv.PaneBackground) == DarkColor));
        });
    }

    /// <summary>
    /// Issue #522 on pool return: a themed TextBlock that unmounts must hand its control back
    /// without the bound Foreground. <c>ElementPool.CleanElement</c> has no other reset for a
    /// TextBlock's Foreground, so the next renter would inherit the theme brush.
    /// </summary>
    internal sealed class PoolReturnClearsBoundForeground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbPoolBrush", async host =>
        {
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                return phase switch
                {
                    0 => VStack(TextBlock("trb-pool-themed").Foreground(new ThemeRef("TrbPoolBrush"))),
                    1 => VStack(),
                    _ => VStack(TextBlock("trb-pool-plain")),
                };
            });
            await Harness.Render();
            var themed = H.FindText("trb-pool-themed");
            H.Check("ThemeResourceBinding_Pool_ThemedBound",
                themed is not null && HasLocal(themed, WinUI.TextBlock.ForegroundProperty) && ColorOf(themed.Foreground) == LightColor);

            setPhase!(1);
            await Harness.Render();
            setPhase!(2);
            await Harness.Render();

            var plain = H.FindText("trb-pool-plain");
            H.Check("ThemeResourceBinding_Pool_SameInstanceRented", plain is not null && ReferenceEquals(plain, themed));
            H.Check("ThemeResourceBinding_Pool_ForegroundCleared",
                plain is not null && !HasLocal(plain, WinUI.TextBlock.ForegroundProperty),
                $"fg={ColorOf(plain?.Foreground)}");
            H.Check("ThemeResourceBinding_Pool_TrackingCleared",
                plain is not null && !(Reconciler.TryGetReactorState(plain, out var state) && state.ThemeResourceBindings is not null));
        });
    }

    /// <summary>
    /// Dropping one ThemeRef while another stays clears only the dropped property, and a brush a
    /// setter or element property writes in its place on the same render is kept. The clear runs
    /// before the element's setters, so it can't wipe what they write.
    /// </summary>
    internal sealed class PartialRemovalKeepsSameRenderWrites(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbPartialBrush", async host =>
        {
            var key = new ThemeRef("TrbPartialBrush");
            var setterBrush = new SolidColorBrush(ExplicitColor);
            var recordBrush = new SolidColorBrush(HoverColor);
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                var border = Border(TextBlock("trb-partial")).BorderThickness(2);
                Element themed = phase switch
                {
                    1 => border.BorderBrush(key),
                    3 => border.Set(b => b.Background = setterBrush).BorderBrush(key),
                    5 => (border with { Background = recordBrush }).BorderBrush(key),
                    _ => border.Background(key).BorderBrush(key),
                };
                return VStack(themed).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-partial" });
            H.Check("ThemeResourceBinding_Partial_BothBound",
                ColorOf(border?.Background) == LightColor && ColorOf(border?.BorderBrush) == LightColor);
            if (border is null) return;

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeResourceBinding_Partial_DroppedCleared", !HasLocal(border, WinUI.Border.BackgroundProperty),
                $"bg={ColorOf(border.Background)}");
            H.Check("ThemeResourceBinding_Partial_SurvivorKept", ColorOf(border.BorderBrush) == LightColor);

            setPhase!(2);
            await Harness.Render();
            setPhase!(3);
            await Harness.Render();
            H.Check("ThemeResourceBinding_Partial_SetterBrushKept", ReferenceEquals(border.Background, setterBrush),
                $"bg={ColorOf(border.Background)}");

            setPhase!(4);
            await Harness.Render();
            setPhase!(5);
            await Harness.Render();
            H.Check("ThemeResourceBinding_Partial_RecordBrushKept", ReferenceEquals(border.Background, recordBrush),
                $"bg={ColorOf(border.Background)}");

            ((FrameworkElement)VisualTreeHelper.GetParent(border))!.RequestedTheme = ElementTheme.Dark;
            H.Check("ThemeResourceBinding_Partial_SurvivorLive",
                await Harness.WaitFor(() => ColorOf(border.BorderBrush) == DarkColor));
        });
    }

    /// <summary>
    /// Once an element property writes over a binding, Reactor stops owning that property: a
    /// resource refresh, a key change, and finally dropping the ThemeRef all leave the element
    /// property's brush in place, as a local value beat the old Style. A themed sibling shows
    /// the refresh really ran.
    /// </summary>
    internal sealed class ReplacedBindingIsHandedOver(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrbHandOverBrush"], "TrbHandOverOther", async (host, light) =>
        {
            var recordBrush = new SolidColorBrush(ExplicitColor);
            // Theme.NotifyResourcesChanged re-renders every live host; keep it to this one.
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                var border = Border(TextBlock("trb-handover"));
                Element target = phase switch
                {
                    0 => border.Background(new ThemeRef("TrbHandOverBrush")),
                    1 => (border with { Background = recordBrush }).Background(new ThemeRef("TrbHandOverBrush")),
                    2 => (border with { Background = recordBrush }).Background(new ThemeRef("TrbHandOverOther")),
                    _ => border with { Background = recordBrush },
                };
                return VStack(target, TextBlock("trb-handover-probe").Foreground(new ThemeRef("TrbHandOverBrush")))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-handover" });
            H.Check("ThemeResourceBinding_HandOver_Bound", ColorOf(border?.Background) == LightColor);
            if (border is null) return;

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeResourceBinding_HandOver_RecordWins", ReferenceEquals(border.Background, recordBrush));

            var edited = Color.FromArgb(255, 250, 200, 0);
            light["TrbHandOverBrush"] = new SolidColorBrush(edited);
            Theme.NotifyResourcesChanged();
            await host.WaitForIdleAsync();
            H.Check("ThemeResourceBinding_HandOver_RefreshRan",
                await Harness.WaitFor(() => ColorOf(H.FindText("trb-handover-probe")?.Foreground) == edited));
            H.Check("ThemeResourceBinding_HandOver_RecordSurvivesRefresh", ReferenceEquals(border.Background, recordBrush),
                $"bg={ColorOf(border.Background)}");

            setPhase!(2);
            await Harness.Render();
            H.Check("ThemeResourceBinding_HandOver_RecordSurvivesKeyChange", ReferenceEquals(border.Background, recordBrush),
                $"bg={ColorOf(border.Background)}");

            setPhase!(3);
            await Harness.Render();
            H.Check("ThemeResourceBinding_HandOver_RecordSurvivesRemoval", ReferenceEquals(border.Background, recordBrush),
                $"bg={ColorOf(border.Background)}");
        });
    }

    /// <summary>
    /// An explicit brush modifier added next to a ThemeRef that was already bound keeps the
    /// property through a key change and a resource refresh.
    /// </summary>
    internal sealed class ModifierAddedOverBoundThemeRefWins(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrbAddedBrush"], "TrbAddedOther", async (host, _) =>
        {
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            // Theme.NotifyResourcesChanged re-renders every live host; keep it to this one.
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                var tb = TextBlock("trb-added");
                return VStack(phase == 0
                    ? tb.Foreground(new ThemeRef("TrbAddedBrush"))
                    : tb.Foreground(explicitBrush).Foreground(new ThemeRef("TrbAddedOther")))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var tb = H.FindText("trb-added");
            H.Check("ThemeResourceBinding_ModifierAdded_Bound", ColorOf(tb?.Foreground) == LightColor);

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeResourceBinding_ModifierAdded_WinsOverKeyChange", ReferenceEquals(tb?.Foreground, explicitBrush),
                $"fg={ColorOf(tb?.Foreground)}");

            Theme.NotifyResourcesChanged();
            await host.WaitForIdleAsync();
            H.Check("ThemeResourceBinding_ModifierAdded_WinsOverRefresh", ReferenceEquals(tb?.Foreground, explicitBrush),
                $"fg={ColorOf(tb?.Foreground)}");
        });
    }

    /// <summary>
    /// A key that can't be resolved is logged and skipped: the property stays unset, the rest of
    /// the render goes on, and a later render binds it once the key exists.
    /// </summary>
    internal sealed class UnknownKeyIsSkippedAndRetried(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrbKnownBrush"], null, async (host, light) =>
        {
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, setter) = ctx.UseState(0);
                setTick = setter;
                return VStack(
                    TextBlock($"trb-unknown-{tick}").Foreground(new ThemeRef("TrbLateBrush")),
                    TextBlock("trb-known").Foreground(new ThemeRef("TrbKnownBrush"))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var unknown = H.FindText("trb-unknown-0");
            H.Check("ThemeResourceBinding_Unknown_Mounted", unknown is not null);
            H.Check("ThemeResourceBinding_Unknown_LeftUnset", unknown is not null && !HasLocal(unknown, WinUI.TextBlock.ForegroundProperty));
            H.Check("ThemeResourceBinding_Unknown_SiblingBound", ColorOf(H.FindText("trb-known")?.Foreground) == LightColor);

            light["TrbLateBrush"] = new SolidColorBrush(OtherLightColor);
            setTick!(1);
            await Harness.Render();
            var retried = H.FindText("trb-unknown-1");
            H.Check("ThemeResourceBinding_Unknown_BoundOnRetry",
                ReferenceEquals(retried, unknown) && ColorOf(retried?.Foreground) == OtherLightColor,
                $"fg={ColorOf(retried?.Foreground)}");
        });
    }

    /// <summary>The Foreground counterpart of <see cref="InteractionStatesRestoreKeepsBindingLive"/>.</summary>
    internal sealed class InteractionStatesRestoreKeepsForegroundLive(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbHoverFgBrush", async host =>
        {
            var hover = new SolidColorBrush(HoverColor);
            host.Mount(_ => VStack(
                TextBlock("trb-hover-fg")
                    .Foreground(new ThemeRef("TrbHoverFgBrush"))
                    .InteractionStates(s => s.PointerOver(foreground: hover))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var tb = H.FindText("trb-hover-fg");
            H.Check("ThemeResourceBinding_HoverFg_NormalIsTheme", ColorOf(tb?.Foreground) == LightColor);
            if (tb is null) return;
            Reconciler.SimulatePointerOverForTests(tb, isOver: true);
            H.Check("ThemeResourceBinding_HoverFg_HoverBrush", ReferenceEquals(tb.Foreground, hover));
            Reconciler.SimulatePointerOverForTests(tb, isOver: false);
            H.Check("ThemeResourceBinding_HoverFg_NormalRestored", ColorOf(tb.Foreground) == LightColor);

            ((FrameworkElement)VisualTreeHelper.GetParent(tb))!.RequestedTheme = ElementTheme.Dark;
            H.Check("ThemeResourceBinding_HoverFg_BindingLiveAfterRestore",
                await Harness.WaitFor(() => ColorOf(tb.Foreground) == DarkColor),
                $"fg={ColorOf(tb.Foreground)}");
        });
    }

    /// <summary>
    /// Dropping ThemeRefs while an InteractionStates hover holds those properties keeps the
    /// hover brushes, and leaving the hover clears them instead of writing back the brushes the
    /// removed bindings had resolved.
    /// </summary>
    internal sealed class RemovalDuringHoverDoesNotResurrect(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbHoverRemoveBrush", async host =>
        {
            var hoverBg = new SolidColorBrush(HoverColor);
            var hoverBorder = new SolidColorBrush(ExplicitColor);
            Action<bool>? setThemed = null;
            host.Mount(ctx =>
            {
                var (themed, setter) = ctx.UseState(true);
                setThemed = setter;
                var border = Border(TextBlock("trb-hover-remove")).BorderThickness(2)
                    .InteractionStates(s => s.PointerOver(background: hoverBg, borderBrush: hoverBorder));
                return VStack(themed
                    ? border.Background(new ThemeRef("TrbHoverRemoveBrush")).BorderBrush(new ThemeRef("TrbHoverRemoveBrush"))
                    : border).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-hover-remove" });
            H.Check("ThemeResourceBinding_HoverRemove_Bound",
                ColorOf(border?.Background) == LightColor && ColorOf(border?.BorderBrush) == LightColor);
            if (border is null) return;

            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            setThemed!(false);
            await Harness.Render();
            H.Check("ThemeResourceBinding_HoverRemove_HoverKept",
                ReferenceEquals(border.Background, hoverBg) && ReferenceEquals(border.BorderBrush, hoverBorder),
                $"bg={ColorOf(border.Background)} border={ColorOf(border.BorderBrush)}");

            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeResourceBinding_HoverRemove_ClearedOnExit",
                !HasLocal(border, WinUI.Border.BackgroundProperty) && !HasLocal(border, WinUI.Border.BorderBrushProperty),
                $"bg={ColorOf(border.Background)} border={ColorOf(border.BorderBrush)}");

            // A second hover cycle must not bring the removed binding's brushes back either.
            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeResourceBinding_HoverRemove_SecondCycleStaysCleared",
                !HasLocal(border, WinUI.Border.BackgroundProperty) && !HasLocal(border, WinUI.Border.BorderBrushProperty),
                $"bg={ColorOf(border.Background)} border={ColorOf(border.BorderBrush)}");
        });
    }

    /// <summary>
    /// An explicit brush modifier on a wrapper layer that shares the control (a keyed Memo)
    /// beats the inner element's ThemeRef, including through a resource refresh: the inner
    /// update can't see the wrapper's modifier, so the wrapper takes ownership when it writes.
    /// </summary>
    internal sealed class WrapperModifierBeatsInnerThemeRefOnRefresh(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrbWrapperBrush"], null, async (host, light) =>
        {
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            // Theme.NotifyResourcesChanged re-renders every live host; keep it to this one.
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            host.Mount(_ => VStack(
                Memo("trb-wrapper", () => TextBlock("trb-wrapper").Foreground(new ThemeRef("TrbWrapperBrush")))
                    .Foreground(explicitBrush),
                TextBlock("trb-wrapper-probe").Foreground(new ThemeRef("TrbWrapperBrush"))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();
            var tb = H.FindText("trb-wrapper");
            H.Check("ThemeResourceBinding_Wrapper_ExplicitWins", ReferenceEquals(tb?.Foreground, explicitBrush),
                $"fg={ColorOf(tb?.Foreground)}");

            var edited = Color.FromArgb(255, 250, 200, 0);
            light["TrbWrapperBrush"] = new SolidColorBrush(edited);
            Theme.NotifyResourcesChanged();
            await host.WaitForIdleAsync();
            H.Check("ThemeResourceBinding_Wrapper_RefreshRan",
                await Harness.WaitFor(() => ColorOf(H.FindText("trb-wrapper-probe")?.Foreground) == edited));
            H.Check("ThemeResourceBinding_Wrapper_ExplicitSurvivesRefresh", ReferenceEquals(tb?.Foreground, explicitBrush),
                $"fg={ColorOf(tb?.Foreground)}");
        });
    }

    /// <summary>
    /// A resource refresh or a ThemeRef key change while an InteractionStates hover holds the
    /// property keeps the hover brush, and leaving the hover installs the binding for the
    /// current key.
    /// </summary>
    internal sealed class HoverSurvivesRefreshAndKeyChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrbHoverKeyA"], "TrbHoverKeyB", async (host, _) =>
        {
            var hover = new SolidColorBrush(HoverColor);
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            Action<bool>? setSecondKey = null;
            host.Mount(ctx =>
            {
                var (second, setter) = ctx.UseState(false);
                setSecondKey = setter;
                return VStack(
                    Border(TextBlock("trb-hover-key"))
                        .Background(new ThemeRef(second ? "TrbHoverKeyB" : "TrbHoverKeyA"))
                        .InteractionStates(s => s.PointerOver(background: hover))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-hover-key" });
            H.Check("ThemeResourceBinding_HoverKey_Bound", ColorOf(border?.Background) == LightColor);
            if (border is null) return;

            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            Theme.NotifyResourcesChanged();
            await host.WaitForIdleAsync();
            H.Check("ThemeResourceBinding_HoverKey_HoverSurvivesRefresh", ReferenceEquals(border.Background, hover),
                $"bg={ColorOf(border.Background)}");

            setSecondKey!(true);
            await Harness.Render();
            H.Check("ThemeResourceBinding_HoverKey_HoverSurvivesKeyChange", ReferenceEquals(border.Background, hover),
                $"bg={ColorOf(border.Background)}");

            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeResourceBinding_HoverKey_RestoresCurrentKey", ColorOf(border.Background) == OtherLightColor,
                $"bg={ColorOf(border.Background)}");
        });
    }

    /// <summary>
    /// Replacing a border ThemeRef with an explicit brush during a hover makes that brush what
    /// the hover restores on exit.
    /// </summary>
    internal sealed class ExplicitBorderBrushReplacingThemeRefDuringHover(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbHoverBorderBrush", async host =>
        {
            var hover = new SolidColorBrush(HoverColor);
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            Action<bool>? setExplicit = null;
            host.Mount(ctx =>
            {
                var (useExplicit, setter) = ctx.UseState(false);
                setExplicit = setter;
                var border = Border(TextBlock("trb-hover-border")).BorderThickness(2)
                    .InteractionStates(s => s.PointerOver(borderBrush: hover));
                return VStack(useExplicit ? border.BorderBrush(explicitBrush) : border.BorderBrush(new ThemeRef("TrbHoverBorderBrush")))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-hover-border" });
            H.Check("ThemeResourceBinding_HoverBorder_Bound", ColorOf(border?.BorderBrush) == LightColor);
            if (border is null) return;

            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            setExplicit!(true);
            await Harness.Render();
            H.Check("ThemeResourceBinding_HoverBorder_HoverKept", ReferenceEquals(border.BorderBrush, hover));
            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeResourceBinding_HoverBorder_ExplicitRestored", ReferenceEquals(border.BorderBrush, explicitBrush),
                $"border={ColorOf(border.BorderBrush)}");
        });
    }

    /// <summary>
    /// A setter that takes a property over in the same render as the control's own
    /// <c>RequestedTheme</c> and its ThemeRef key change keeps the property: the theme change
    /// doesn't hide the takeover.
    /// </summary>
    internal sealed class SetterTakeoverWithOwnThemeChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrbTakeoverA"], "TrbTakeoverB", async (host, _) =>
        {
            var setterBrush = new SolidColorBrush(ExplicitColor);
            Action<bool>? setTakeover = null;
            host.Mount(ctx =>
            {
                var (takeover, setter) = ctx.UseState(false);
                setTakeover = setter;
                var border = Border(TextBlock("trb-takeover"));
                return VStack(takeover
                    ? border.Set(b => b.Background = setterBrush).Background(new ThemeRef("TrbTakeoverB")).RequestedTheme(ElementTheme.Dark)
                    : border.Background(new ThemeRef("TrbTakeoverA")).RequestedTheme(ElementTheme.Light));
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-takeover" });
            H.Check("ThemeResourceBinding_Takeover_Bound", ColorOf(border?.Background) == LightColor);

            setTakeover!(true);
            await Harness.Render();
            H.Check("ThemeResourceBinding_Takeover_SetterWins", ReferenceEquals(border?.Background, setterBrush),
                $"bg={ColorOf(border?.Background)}");
        });
    }

    /// <summary>
    /// A state that never touches Background must not write back a brush the removed
    /// Background binding resolved to when the states were set up.
    /// </summary>
    internal sealed class UnrelatedStateDoesNotResurrectRemovedBinding(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrbUnrelatedStateBrush", async host =>
        {
            Action<bool>? setThemed = null;
            host.Mount(ctx =>
            {
                var (themed, setter) = ctx.UseState(true);
                setThemed = setter;
                var border = Border(TextBlock("trb-unrelated")).InteractionStates(s => s.PointerOver(opacity: 0.8f));
                return VStack(themed ? border.Background(new ThemeRef("TrbUnrelatedStateBrush")) : border)
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();
            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trb-unrelated" });
            H.Check("ThemeResourceBinding_Unrelated_Bound", ColorOf(border?.Background) == LightColor);
            if (border is null) return;

            setThemed!(false);
            await Harness.Render();
            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeResourceBinding_Unrelated_StaysCleared", !HasLocal(border, WinUI.Border.BackgroundProperty),
                $"bg={ColorOf(border.Background)}");
        });
    }
}