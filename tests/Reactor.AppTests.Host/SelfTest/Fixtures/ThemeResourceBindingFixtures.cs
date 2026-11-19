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

    /// <summary>Runs <paramref name="body"/> with <paramref name="key"/> defined in the app's theme dictionaries.</summary>
    private static async Task WithThemeKey(Harness h, string key, Func<ReactorHost, Task> body)
    {
        var resources = new ResourceDictionary();
        resources.ThemeDictionaries["Light"] = new ResourceDictionary { [key] = new SolidColorBrush(LightColor) };
        resources.ThemeDictionaries["Dark"] = new ResourceDictionary { [key] = new SolidColorBrush(DarkColor) };
        Application.Current.Resources.MergedDictionaries.Add(resources);
        var host = h.CreateHost();
        try
        {
            await body(host);
        }
        finally
        {
            host.Dispose();
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
                ReferenceEquals(tb?.Style, style) && Math.Abs((tb?.FontSize ?? 0) - 31) < 0.01,
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
}
