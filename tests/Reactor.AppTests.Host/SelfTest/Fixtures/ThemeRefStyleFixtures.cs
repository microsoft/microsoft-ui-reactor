using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;
using Color = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <see cref="ThemeRef"/> modifiers applied through the synthesized <c>{ThemeResource}</c> Style:
/// <c>SplitView.PaneBackground</c>, InteractionStates restoring the themed value rather than
/// pinning a brush, an app's own Style kept as the themed Style's <c>BasedOn</c>, and resource
/// keys that need quoting in the markup extension. Each scenario uses its own brush key with
/// distinct Light and Dark values, so a color names exactly which source produced it, and a
/// native <c>RequestedTheme</c> flip (no Reactor render) proves the value is still live.
/// </summary>
internal static class ThemeRefStyleFixtures
{
    private static readonly Color LightColor = Color.FromArgb(255, 210, 220, 230);
    private static readonly Color DarkColor = Color.FromArgb(255, 30, 40, 50);
    private static readonly Color HoverColor = Color.FromArgb(255, 0, 160, 0);
    private static readonly Color ExplicitColor = Color.FromArgb(255, 200, 30, 40);
    private static readonly Color OtherExplicitColor = Color.FromArgb(255, 40, 30, 200);

    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static Color? ColorOf(object? brush) => (brush as SolidColorBrush)?.Color;

    private static bool HasLocal(DependencyObject? d, DependencyProperty p) =>
        d is not null && !ReferenceEquals(d.ReadLocalValue(p), DependencyProperty.UnsetValue);

    /// <summary>
    /// Runs <paramref name="body"/> with each of <paramref name="keys"/> defined in the app's theme
    /// dictionaries (Light/Dark colors). The body receives the merged dictionary so it can add
    /// other resources, such as a keyed Style.
    /// </summary>
    private static async Task WithThemeKeys(Harness h, string[] keys, Func<ReactorHost, ResourceDictionary, Task> body)
    {
        var resources = new ResourceDictionary();
        var lightDict = new ResourceDictionary();
        var darkDict = new ResourceDictionary();
        foreach (var key in keys)
        {
            lightDict[key] = new SolidColorBrush(LightColor);
            darkDict[key] = new SolidColorBrush(DarkColor);
        }
        resources.ThemeDictionaries["Light"] = lightDict;
        resources.ThemeDictionaries["Dark"] = darkDict;
        Application.Current.Resources.MergedDictionaries.Add(resources);
        using var host = h.CreateHost();
        try
        {
            await body(host, resources);
        }
        finally
        {
            h.SetContent(null);
            Application.Current.Resources.MergedDictionaries.Remove(resources);
            await Harness.Render();
        }
    }

    private static Task WithThemeKey(Harness h, string key, Func<ReactorHost, Task> body)
        => WithThemeKeys(h, [key], (host, _) => body(host));

    private static async Task<bool> FlipToDark(FrameworkElement control, Func<bool> isDark)
    {
        ((FrameworkElement)VisualTreeHelper.GetParent(control))!.RequestedTheme = ElementTheme.Dark;
        return await Harness.WaitFor(isDark);
    }

    /// <summary>
    /// <c>SplitView.PaneBackground(ThemeRef)</c> themes the pane background (it had no Style
    /// mapping and silently did nothing), including when it replaces an explicit
    /// <c>.PaneBackground(brush)</c> on the same control, whose stale local value would hide it.
    /// Writing the themed Style to the already-loaded SplitView, and taking it off again, must
    /// not fail: a loaded SplitView rejects a Style not built on its default style.
    /// </summary>
    internal sealed class SplitViewPaneBackground(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsPaneBrush", async host =>
        {
            var explicitBrush = new SolidColorBrush(ExplicitColor);
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                var sv = SplitView(TextBlock("trs-pane"), TextBlock("trs-content")).Height(80);
                return VStack(phase switch
                {
                    0 => sv.PaneBackground(explicitBrush),
                    1 => sv.PaneBackground(new ThemeRef("TrsPaneBrush")),
                    _ => sv,
                }).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var sv = H.FindControl<WinUI.SplitView>(_ => true);
            H.Check("ThemeRefStyle_Pane_ExplicitBrush", ReferenceEquals(sv?.PaneBackground, explicitBrush));

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_Pane_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeRefStyle_Pane_ExplicitCleared", !HasLocal(sv, WinUI.SplitView.PaneBackgroundProperty));
            H.Check("ThemeRefStyle_Pane_Themed", ColorOf(sv?.PaneBackground) == LightColor,
                $"pane={ColorOf(sv?.PaneBackground)}");
            if (sv is null) return;
            H.Check("ThemeRefStyle_Pane_FollowsTheme",
                await FlipToDark(sv, () => ColorOf(sv.PaneBackground) == DarkColor),
                $"pane={ColorOf(sv.PaneBackground)}");

            setPhase!(2);
            await Harness.Render();
            H.Check("ThemeRefStyle_Pane_Removed_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeRefStyle_Pane_Removed_NotThemed",
                ColorOf(sv.PaneBackground) != LightColor && ColorOf(sv.PaneBackground) != DarkColor,
                $"pane={ColorOf(sv.PaneBackground)}");
        });
    }    /// <summary>
    /// InteractionStates writes its hover brush over a ThemeRef Background/Foreground; leaving the
    /// hover must drop that local value so the {ThemeResource} Style setter applies again, rather
    /// than write back a brush captured earlier, which would pin the theme it had then.
    /// </summary>
    internal sealed class InteractionStatesRestoreKeepsThemeLive(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsHoverBrush", async host =>
        {
            var hover = new SolidColorBrush(HoverColor);
            var key = new ThemeRef("TrsHoverBrush");
            host.Mount(_ => VStack(
                VStack(Border(TextBlock("trs-hover-bg")).Background(key)
                    .InteractionStates(s => s.PointerOver(background: hover))),
                VStack(TextBlock("trs-hover-fg").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover))),
                VStack(Button("trs-hover-button").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover)))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-hover-bg" });
            var text = H.FindText("trs-hover-fg");
            var button = H.FindControl<WinUI.Button>(b => b.Content is "trs-hover-button");
            H.Check("ThemeRefStyle_Hover_Present", border is not null && text is not null && button is not null);
            if (border is null || text is null || button is null) return;

            (FrameworkElement Control, DependencyProperty Property, string Name)[] cases =
            [
                (border, WinUI.Border.BackgroundProperty, "BorderBackground"),
                (text, WinUI.TextBlock.ForegroundProperty, "TextForeground"),
                (button, WinUI.Control.ForegroundProperty, "ButtonForeground"),
            ];
            foreach (var (control, property, name) in cases)
            {
                H.Check($"ThemeRefStyle_Hover_{name}_NormalIsTheme", ColorOf(control.GetValue(property)) == LightColor,
                    $"value={ColorOf(control.GetValue(property))}");
                Reconciler.SimulatePointerOverForTests(control, isOver: true);
                H.Check($"ThemeRefStyle_Hover_{name}_HoverBrush", ReferenceEquals(control.GetValue(property), hover));
                Reconciler.SimulatePointerOverForTests(control, isOver: false);
                H.Check($"ThemeRefStyle_Hover_{name}_LocalReleased", !HasLocal(control, property),
                    $"local={control.ReadLocalValue(property)}");
                H.Check($"ThemeRefStyle_Hover_{name}_NormalRestored", ColorOf(control.GetValue(property)) == LightColor);
                H.Check($"ThemeRefStyle_Hover_{name}_ThemeLiveAfterRestore",
                    await FlipToDark(control, () => ColorOf(control.GetValue(property)) == DarkColor),
                    $"value={ColorOf(control.GetValue(property))}");
            }
        });
    }

    /// <summary>
    /// A state that sets no brush (opacity only) leaves Background alone: a transition used to
    /// write the brush captured at setup back as a local value, pinning the ThemeRef.
    /// </summary>
    internal sealed class InteractionStatesOpacityOnlyDoesNotPin(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsOpacityBrush", async host =>
        {
            host.Mount(_ => VStack(
                VStack(Border(TextBlock("trs-opacity")).Background(new ThemeRef("TrsOpacityBrush"))
                    .InteractionStates(s => s.PointerOver(opacity: 0.5f)))).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-opacity" });
            H.Check("ThemeRefStyle_Opacity_NormalIsTheme", ColorOf(border?.Background) == LightColor);
            if (border is null) return;
            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeRefStyle_Opacity_NoLocalBackground", !HasLocal(border, WinUI.Border.BackgroundProperty));
            H.Check("ThemeRefStyle_Opacity_ThemeLive",
                await FlipToDark(border, () => ColorOf(border.Background) == DarkColor),
                $"bg={ColorOf(border.Background)}");
        });
    }

    /// <summary>
    /// An explicit <c>.Background(brush)</c> is still put back after a hover, and it is the brush
    /// the element carries when the hover starts, not the one it had when InteractionStates was
    /// first set up.
    /// </summary>
    internal sealed class InteractionStatesRestoresCurrentExplicitBrush(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hover = new SolidColorBrush(HoverColor);
            var first = new SolidColorBrush(ExplicitColor);
            var second = new SolidColorBrush(OtherExplicitColor);
            Action<bool>? setSecond = null;
            using var host = H.CreateHost();
            try
            {
                host.Mount(ctx =>
                {
                    var (useSecond, setter) = ctx.UseState(false);
                    setSecond = setter;
                    return VStack(Border(TextBlock("trs-explicit")).Background(useSecond ? second : first)
                        .InteractionStates(s => s.PointerOver(background: hover)));
                });
                await Harness.Render();

                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-explicit" });
                H.Check("ThemeRefStyle_Explicit_Present", border is not null);
                if (border is null) return;
                Reconciler.SimulatePointerOverForTests(border, isOver: true);
                Reconciler.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_FirstRestored", ReferenceEquals(border.ReadLocalValue(WinUI.Border.BackgroundProperty), first));

                setSecond!(true);
                await Harness.Render();
                H.Check("ThemeRefStyle_Explicit_SecondApplied", ReferenceEquals(border.Background, second));
                Reconciler.SimulatePointerOverForTests(border, isOver: true);
                H.Check("ThemeRefStyle_Explicit_HoverBrush", ReferenceEquals(border.Background, hover));
                Reconciler.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_SecondRestored", ReferenceEquals(border.ReadLocalValue(WinUI.Border.BackgroundProperty), second),
                    $"bg={ColorOf(border.Background)}");
            }
            finally
            {
                H.SetContent(null);
                await Harness.Render();
            }
        }
    }

    /// <summary>
    /// A ThemeRef keeps a Style the app set (<c>.Set</c> on every render, <c>.ApplyStyle</c> once at
    /// mount): the themed Style is built BasedOn it, re-renders don't chain another level, and
    /// dropping the ThemeRef hands the control back to the app's Style.
    /// </summary>
    internal sealed class AuthorStyleKept(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrsStyleBrush"], async (host, resources) =>
        {
            var setStyle = new Style(typeof(WinUI.TextBlock));
            setStyle.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 31d));
            var namedStyle = new Style(typeof(WinUI.TextBlock));
            namedStyle.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 33d));
            resources["TrsNamedTextStyle"] = namedStyle;
            var key = new ThemeRef("TrsStyleBrush");
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                // Phase 1 re-renders with a changed text, so both elements take the update path.
                var suffix = phase == 1 ? "!" : "";
                var viaSet = TextBlock("trs-style-set" + suffix).Set(t => t.Style = setStyle);
                var viaNamed = TextBlock("trs-style-named" + suffix).ApplyStyle("TrsNamedTextStyle");
                return VStack(
                    phase < 2 ? viaSet.Foreground(key) : viaSet,
                    phase < 2 ? viaNamed.Foreground(key) : viaNamed).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var bySet = H.FindText("trs-style-set");
            var byNamed = H.FindText("trs-style-named");
            H.Check("ThemeRefStyle_Author_Present", bySet is not null && byNamed is not null);
            if (bySet is null || byNamed is null) return;

            void CheckThemed(string phase)
            {
                foreach (var (tb, style, size, name) in new[] { (bySet, setStyle, 31d, "Set"), (byNamed, namedStyle, 33d, "Named") })
                {
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_StyleKept", Math.Abs(tb.FontSize - size) < 0.01,
                        $"fontSize={tb.FontSize}");
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_Themed", ColorOf(tb.Foreground) == LightColor,
                        $"fg={ColorOf(tb.Foreground)}");
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_BasedOnAuthor",
                        tb.Style is { } themed && !ReferenceEquals(themed, style)
                        && ReferenceEquals(themed.BasedOn, style) && themed.BasedOn?.BasedOn is null);
                }
            }

            CheckThemed("Mount");
            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_Author_Update_SameControls",
                ReferenceEquals(H.FindText("trs-style-set!"), bySet) && ReferenceEquals(H.FindText("trs-style-named!"), byNamed));
            CheckThemed("Update");

            setPhase!(2);
            await Harness.Render();
            foreach (var (tb, style, size, name) in new[] { (bySet, setStyle, 31d, "Set"), (byNamed, namedStyle, 33d, "Named") })
            {
                H.Check($"ThemeRefStyle_Author_{name}_Removed_AuthorStyleRestored", ReferenceEquals(tb.Style, style)
                    && Math.Abs(tb.FontSize - size) < 0.01, $"fontSize={tb.FontSize}");
                H.Check($"ThemeRefStyle_Author_{name}_Removed_NotThemed", ColorOf(tb.Foreground) != LightColor
                    && !HasLocal(tb, WinUI.TextBlock.ForegroundProperty), $"fg={ColorOf(tb.Foreground)}");
            }
        });
    }

    /// <summary>
    /// The named-style fluents and typography factories keep their style under a ThemeRef:
    /// <c>Title(...)</c> keeps the title font size and <c>.AccentButton()</c> its accent background.
    /// </summary>
    internal sealed class NamedStyleKeptWithThemeRef(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsNamedBrush", async host =>
        {
            var key = new ThemeRef("TrsNamedBrush");
            host.Mount(_ => VStack(
                Title("trs-title-themed").Foreground(key),
                Title("trs-title-plain"),
                TextBlock("trs-text-plain"),
                Button("trs-accent-themed").AccentButton().Foreground(key),
                Button("trs-accent-plain").AccentButton(),
                Button("trs-button-plain")).RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var titleThemed = H.FindText("trs-title-themed");
            var titlePlain = H.FindText("trs-title-plain");
            var textPlain = H.FindText("trs-text-plain");
            H.Check("ThemeRefStyle_Named_TitleDiffersFromText",
                titlePlain is not null && textPlain is not null && Math.Abs(titlePlain.FontSize - textPlain.FontSize) > 1,
                $"title={titlePlain?.FontSize} text={textPlain?.FontSize}");
            H.Check("ThemeRefStyle_Named_TitleKept",
                titleThemed is not null && titlePlain is not null && Math.Abs(titleThemed.FontSize - titlePlain.FontSize) < 0.01,
                $"themed={titleThemed?.FontSize} plain={titlePlain?.FontSize}");
            H.Check("ThemeRefStyle_Named_TitleThemed", ColorOf(titleThemed?.Foreground) == LightColor);

            WinUI.Button? FindButton(string label) => H.FindControl<WinUI.Button>(b => b.Content is string s && s == label);
            var accentThemed = FindButton("trs-accent-themed");
            var accentPlain = FindButton("trs-accent-plain");
            var buttonPlain = FindButton("trs-button-plain");
            H.Check("ThemeRefStyle_Named_AccentDiffersFromDefault",
                accentPlain is not null && buttonPlain is not null && ColorOf(accentPlain.Background) != ColorOf(buttonPlain.Background),
                $"accent={ColorOf(accentPlain?.Background)} default={ColorOf(buttonPlain?.Background)}");
            H.Check("ThemeRefStyle_Named_AccentKept",
                accentThemed is not null && accentPlain is not null && ColorOf(accentThemed.Background) == ColorOf(accentPlain.Background),
                $"themed={ColorOf(accentThemed?.Background)} plain={ColorOf(accentPlain?.Background)}");
            H.Check("ThemeRefStyle_Named_AccentThemed", ColorOf(accentThemed?.Foreground) == LightColor,
                $"fg={ColorOf(accentThemed?.Foreground)}");
        });
    }

    /// <summary>
    /// BasedOn needs the base's TargetType to be the derived Style's type or a base of it. A
    /// <c>TargetType=CheckBox</c> author Style must still get its ThemeRef, which the loose
    /// target mapping (CheckBox → ContentControl) cannot build on.
    /// </summary>
    internal sealed class AuthorStyleDerivedTargetType(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsCheckBrush", async host =>
        {
            var style = new Style(typeof(WinUI.CheckBox));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 177d));
            host.Mount(_ => VStack(
                CheckBox(label: "trs-check").Set(c => c.Style = style).Background(new ThemeRef("TrsCheckBrush")))
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var check = H.FindControl<WinUI.CheckBox>(c => c.Content is "trs-check");
            H.Check("ThemeRefStyle_CheckBox_StyleKept", check is not null && Math.Abs(check.MinWidth - 177) < 0.01,
                $"minWidth={check?.MinWidth}");
            H.Check("ThemeRefStyle_CheckBox_Themed", ColorOf(check?.Background) == LightColor,
                $"bg={ColorOf(check?.Background)}");
        });
    }

    /// <summary>
    /// Re-applying the themed Style on a re-render must not rebuild a Template the author Style
    /// sets: stepping through no Style at all would swap the template to the default and back.
    /// </summary>
    internal sealed class AuthorTemplateNotRebuilt(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsTemplateBrush", async host =>
        {
            var style = (Style)XamlReader.Load(
                $"<Style xmlns='{XamlNs}' TargetType='Button'><Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}'>" +
                "<ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>");
            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, setter) = ctx.UseState(0);
                setCount = setter;
                return VStack(Button($"trs-template-{count}").Set(b => b.Style = style)
                    .Foreground(new ThemeRef("TrsTemplateBrush"))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var button = H.FindControl<WinUI.Button>(b => b.Content is "trs-template-0");
            var root = button is not null && VisualTreeHelper.GetChildrenCount(button) > 0 ? VisualTreeHelper.GetChild(button, 0) : null;
            H.Check("ThemeRefStyle_Template_Applied", root is WinUI.Border && ReferenceEquals(button!.Style?.BasedOn, style),
                $"root={root?.GetType().Name}");
            H.Check("ThemeRefStyle_Template_Themed", ColorOf(button?.Foreground) == LightColor);

            setCount!(1);
            await Harness.Render();
            var after = button is not null && VisualTreeHelper.GetChildrenCount(button) > 0 ? VisualTreeHelper.GetChild(button, 0) : null;
            H.Check("ThemeRefStyle_Template_SameControl", ReferenceEquals(H.FindControl<WinUI.Button>(b => b.Content is "trs-template-1"), button));
            H.Check("ThemeRefStyle_Template_RootKept", root is not null && ReferenceEquals(after, root),
                $"after={after?.GetType().Name}");
        });
    }

    /// <summary>
    /// Resource keys with characters that are special in a markup extension (comma, brace, '=',
    /// quote, space, backslash) are quoted in the <c>{ThemeResource}</c> setter, so they apply and
    /// follow the theme instead of failing to parse.
    /// </summary>
    internal sealed class ThemeRefKeyNeedsQuoting(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly string[] Keys =
            ["Trs,Comma", "Trs}Brace", "Trs=Equals", "Trs'Quote", "Trs Space", "Trs\\Slash", "Trs\"DoubleQuote"];

        public override Task RunAsync() => WithThemeKeys(H, Keys, async (host, _) =>
        {
            host.Mount(_ => VStack(Keys.Select((key, i) =>
                (Element)VStack(Border(TextBlock($"trs-key-{i}")).Background(new ThemeRef(key)))).ToArray())
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            for (int i = 0; i < Keys.Length; i++)
            {
                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock t && t.Text == $"trs-key-{i}");
                H.Check($"ThemeRefStyle_Key{i}_Themed", ColorOf(border?.Background) == LightColor,
                    $"key={Keys[i]} bg={ColorOf(border?.Background)}");
                if (border is null) continue;
                H.Check($"ThemeRefStyle_Key{i}_FollowsTheme",
                    await FlipToDark(border, () => ColorOf(border.Background) == DarkColor),
                    $"key={Keys[i]} bg={ColorOf(border.Background)}");
            }
        });
    }
}
