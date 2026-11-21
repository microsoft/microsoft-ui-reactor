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
/// <see cref="ThemeRef"/> modifiers bound with <c>SetThemeResourceBinding</c>:
/// <c>SplitView.PaneBackground</c>, InteractionStates restoring the live binding rather than
/// pinning a brush, an app's own Style left in place, and resource keys with characters that
/// are special in markup. Each scenario uses its own brush key with distinct Light and Dark
/// values, so a color names exactly which source produced it, and a native
/// <c>RequestedTheme</c> flip (no Reactor render) proves the value is still live.
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
    /// <c>SplitView.PaneBackground(ThemeRef)</c> themes the pane background (it was not mapped
    /// and silently did nothing), including when it replaces an explicit
    /// <c>.PaneBackground(brush)</c> on the same control, whose stale local value would hide it.
    /// Later renders on the loaded SplitView, on the update and the skip arm, keep it themed.
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
                // Phase 2 changes the SplitView itself (update arm); phase 3 changes only a sibling,
                // so the unchanged SplitView takes the skip arm. Both re-apply the ThemeRef to the
                // loaded control.
                var sv = SplitView(TextBlock("trs-pane"), TextBlock("trs-content")).Height(phase >= 2 ? 81 : 80);
                return VStack(
                    TextBlock(phase == 3 ? "trs-pane-sibling-3" : "trs-pane-sibling"),
                    phase switch
                    {
                        0 => sv.PaneBackground(explicitBrush),
                        1 or 2 or 3 => sv.PaneBackground(new ThemeRef("TrsPaneBrush")),
                        _ => sv,
                    }).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var sv = H.FindControl<WinUI.SplitView>(_ => true);
            H.Check("ThemeRefStyle_Pane_ExplicitBrush", ReferenceEquals(sv?.PaneBackground, explicitBrush));

            setPhase!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_Pane_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeRefStyle_Pane_ExplicitCleared",
                !ReferenceEquals(sv?.ReadLocalValue(WinUI.SplitView.PaneBackgroundProperty), explicitBrush));
            H.Check("ThemeRefStyle_Pane_Themed", ColorOf(sv?.PaneBackground) == LightColor,
                $"pane={ColorOf(sv?.PaneBackground)}");
            if (sv is null) return;
            H.Check("ThemeRefStyle_Pane_FollowsTheme",
                await FlipToDark(sv, () => ColorOf(sv.PaneBackground) == DarkColor),
                $"pane={ColorOf(sv.PaneBackground)}");

            foreach (var (phase, name) in new[] { (2, "Update"), (3, "Skip") })
            {
                setPhase!(phase);
                await Harness.Render();
                H.Check($"ThemeRefStyle_Pane_{name}_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
                H.Check($"ThemeRefStyle_Pane_{name}_StillThemed", ColorOf(sv.PaneBackground) is { } c && (c == DarkColor || c == LightColor),
                    $"pane={ColorOf(sv.PaneBackground)} height={sv.Height}");
            }
            H.Check("ThemeRefStyle_Pane_UpdateApplied", Math.Abs(sv.Height - 81) < 0.01 && H.FindText("trs-pane-sibling-3") is not null);

            setPhase!(4);
            await Harness.Render();
            H.Check("ThemeRefStyle_Pane_Removed_SameControl", ReferenceEquals(H.FindControl<WinUI.SplitView>(_ => true), sv));
            H.Check("ThemeRefStyle_Pane_Removed_NotThemed",
                ColorOf(sv.PaneBackground) != LightColor && ColorOf(sv.PaneBackground) != DarkColor,
                $"pane={ColorOf(sv.PaneBackground)}");
        });
    }    /// <summary>
    /// InteractionStates writes its hover brush over a ThemeRef Background/Foreground; leaving the
    /// hover must take that brush off again so the ThemeRef's binding applies, rather than write
    /// back a brush captured earlier, which would pin the theme it had then.
    /// </summary>
    internal sealed class InteractionStatesRestoreKeepsThemeLive(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsHoverBrush", async host =>
        {
            var hover = new SolidColorBrush(HoverColor);
            var pressed = new SolidColorBrush(ExplicitColor);
            var key = new ThemeRef("TrsHoverBrush");
            host.Mount(_ => VStack(
                VStack(Border(TextBlock("trs-hover-bg")).Background(key)
                    .InteractionStates(s => s.PointerOver(background: hover).Pressed(background: pressed))),
                VStack(TextBlock("trs-hover-fg").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover).Pressed(foreground: pressed))),
                VStack(Button("trs-hover-button").Foreground(key)
                    .InteractionStates(s => s.PointerOver(foreground: hover).Pressed(foreground: pressed)))).RequestedTheme(ElementTheme.Light));
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
                // Pressed sets its own brush: the press must not capture the hover brush as the
                // value to restore.
                Reconciler.SimulatePressedForTests(control, isPressed: true);
                H.Check($"ThemeRefStyle_Hover_{name}_PressedBrush", ReferenceEquals(control.GetValue(property), pressed));
                Reconciler.SimulatePressedForTests(control, isPressed: false);
                Reconciler.SimulatePointerOverForTests(control, isOver: false);
                H.Check($"ThemeRefStyle_Hover_{name}_LocalReleased",
                    control.ReadLocalValue(property) is var released
                    && !ReferenceEquals(released, hover) && !ReferenceEquals(released, pressed),
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
            var before = border.ReadLocalValue(WinUI.Border.BackgroundProperty);
            Reconciler.SimulatePointerOverForTests(border, isOver: true);
            Reconciler.SimulatePointerOverForTests(border, isOver: false);
            H.Check("ThemeRefStyle_Opacity_LocalUnchanged",
                ReferenceEquals(border.ReadLocalValue(WinUI.Border.BackgroundProperty), before));
            H.Check("ThemeRefStyle_Opacity_ThemeLive",
                await FlipToDark(border, () => ColorOf(border.Background) == DarkColor),
                $"bg={ColorOf(border.Background)}");
        });
    }

    /// <summary>
    /// An explicit <c>.Background(brush)</c> is put back after a hover, and it is the brush the
    /// element carries when the states end: not the one it had when InteractionStates was first
    /// set up, not the hover brush when a press follows the hover, and the brush a re-render set
    /// while the pointer was over it.
    /// </summary>
    internal sealed class InteractionStatesRestoresCurrentExplicitBrush(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hover = new SolidColorBrush(HoverColor);
            var pressed = new SolidColorBrush(DarkColor);
            var first = new SolidColorBrush(ExplicitColor);
            var second = new SolidColorBrush(OtherExplicitColor);
            var third = new SolidColorBrush(LightColor);
            Action<int>? setPhase = null;
            using var host = H.CreateHost();
            try
            {
                host.Mount(ctx =>
                {
                    var (phase, setter) = ctx.UseState(0);
                    setPhase = setter;
                    var brush = phase switch { 0 => first, 1 => second, _ => third };
                    return VStack(Border(TextBlock("trs-explicit")).Background(brush)
                        .InteractionStates(s => s.PointerOver(background: hover).Pressed(background: pressed)));
                });
                await Harness.Render();

                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-explicit" });
                H.Check("ThemeRefStyle_Explicit_Present", border is not null);
                if (border is null) return;
                object Local() => border.ReadLocalValue(WinUI.Border.BackgroundProperty);

                // Over → Pressed → Over → Normal: the press must not recapture the hover brush.
                Reconciler.SimulatePointerOverForTests(border, isOver: true);
                Reconciler.SimulatePressedForTests(border, isPressed: true);
                H.Check("ThemeRefStyle_Explicit_PressedBrush", ReferenceEquals(border.Background, pressed));
                Reconciler.SimulatePressedForTests(border, isPressed: false);
                Reconciler.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_FirstRestoredAfterPress", ReferenceEquals(Local(), first),
                    $"bg={ColorOf(border.Background)}");

                // A brush set between hovers is the one the next hover restores.
                setPhase!(1);
                await Harness.Render();
                H.Check("ThemeRefStyle_Explicit_SecondApplied", ReferenceEquals(border.Background, second));
                Reconciler.SimulatePointerOverForTests(border, isOver: true);
                H.Check("ThemeRefStyle_Explicit_HoverBrush", ReferenceEquals(border.Background, hover));

                // A re-render during the hover writes a new brush: it stands, and leaving the hover
                // keeps it instead of writing back the brush from when the hover started.
                setPhase!(2);
                await Harness.Render();
                H.Check("ThemeRefStyle_Explicit_ThirdAppliedDuringHover", ReferenceEquals(Local(), third));
                Reconciler.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_ThirdKeptOnExit", ReferenceEquals(Local(), third),
                    $"bg={ColorOf(border.Background)}");

                // And once the hover starts over that brush, a press and release still come back to it.
                Reconciler.SimulatePointerOverForTests(border, isOver: true);
                setPhase!(1);
                await Harness.Render();
                Reconciler.SimulatePressedForTests(border, isPressed: true);
                Reconciler.SimulatePressedForTests(border, isPressed: false);
                Reconciler.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Explicit_MidHoverBrushRestoredAfterPress", ReferenceEquals(Local(), second),
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
    /// A Background set by a binding comes back as that binding after a hover and a press, not as
    /// a cleared property, and keeps following its source afterwards.
    /// </summary>
    internal sealed class InteractionStatesRestoresBinding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hover = new SolidColorBrush(HoverColor);
            var pressed = new SolidColorBrush(DarkColor);
            var first = new SolidColorBrush(ExplicitColor);
            var second = new SolidColorBrush(OtherExplicitColor);
            var source = new WinUI.Border { Background = first };
            using var host = H.CreateHost();
            try
            {
                host.Mount(_ => VStack(Border(TextBlock("trs-bound"))
                    .Set(b => b.SetBinding(WinUI.Border.BackgroundProperty,
                        new Microsoft.UI.Xaml.Data.Binding { Source = source, Path = new PropertyPath("Background") }))
                    .InteractionStates(s => s.PointerOver(background: hover).Pressed(background: pressed))));
                await Harness.Render();

                var border = H.FindControl<WinUI.Border>(b => b.Child is WinUI.TextBlock { Text: "trs-bound" });
                H.Check("ThemeRefStyle_Bound_Initial", ReferenceEquals(border?.Background, first));
                if (border is null) return;

                Reconciler.SimulatePointerOverForTests(border, isOver: true);
                Reconciler.SimulatePressedForTests(border, isPressed: true);
                H.Check("ThemeRefStyle_Bound_PressedBrush", ReferenceEquals(border.Background, pressed));
                Reconciler.SimulatePressedForTests(border, isPressed: false);
                Reconciler.SimulatePointerOverForTests(border, isOver: false);
                H.Check("ThemeRefStyle_Bound_Restored", ReferenceEquals(border.Background, first),
                    $"bg={ColorOf(border.Background)}");

                source.Background = second;
                H.Check("ThemeRefStyle_Bound_FollowsSource",
                    await Harness.WaitFor(() => ReferenceEquals(border.Background, second)),
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
    /// mount): the binding is on the property, so <c>Style</c> stays the app's, and dropping the
    /// ThemeRef leaves the app's Style in place.
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
                    H.Check($"ThemeRefStyle_Author_{name}_{phase}_StyleIsAuthor", ReferenceEquals(tb.Style, style));
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
                H.Check($"ThemeRefStyle_Author_{name}_Removed_AuthorStyleKept", ReferenceEquals(tb.Style, style)
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
    /// A <c>TargetType=CheckBox</c> author Style keeps its setters under a ThemeRef, and the
    /// ThemeRef still applies (a themed Style built on it had to match its exact TargetType).
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
    /// Re-applying a ThemeRef on a re-render must not rebuild a Template the author Style sets:
    /// the author Style stays on the control, so the template root is the same object.
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
            H.Check("ThemeRefStyle_Template_Applied", root is WinUI.Border && ReferenceEquals(button!.Style, style),
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
    /// Resource keys with characters that are special in a markup extension or an XML attribute
    /// (comma, brace, '=', quotes, space, backslash, tab) bind and follow the theme. The key is
    /// passed to <c>SetThemeResourceBinding</c> as is, so no character needs quoting.
    /// </summary>
    internal sealed class ThemeRefKeyNeedsQuoting(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly string[] Keys =
            ["Trs,Comma", "Trs}Brace", "Trs=Equals", "Trs'Quote", "Trs Space", "Trs\\Slash", "Trs\"DoubleQuote",
             "Trs'Both\"Quotes", "Trs\tTab"];

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

    /// <summary>
    /// A ThemeRef key WinUI can't find skips only its own property (the element's other ThemeRefs
    /// still apply), and a key that changes to one WinUI can't find takes the old key's brush
    /// with it rather than leaving it on as if it were the new key's.
    /// </summary>
    internal sealed class MissingKeySkipsOnlyItsProperty(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsGoodBrush", async host =>
        {
            const string BadKey = "TrsNoSuchBrushAnywhere";
            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, setter) = ctx.UseState(0);
                setCount = setter;
                return VStack(
                    Button($"trs-bad-key-{count}")
                        .Background(new ThemeRef(BadKey))
                        .Foreground(new ThemeRef("TrsGoodBrush")),
                    Button("trs-bad-switch").Background(new ThemeRef(count == 0 ? "TrsGoodBrush" : BadKey)))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var button = H.FindControl<WinUI.Button>(b => b.Content is "trs-bad-key-0");
            H.Check("ThemeRefStyle_BadKey_Switch_GoodKeyThemed",
                ColorOf(H.FindControl<WinUI.Button>(b => b.Content is "trs-bad-switch")?.Background) == LightColor);
            H.Check("ThemeRefStyle_BadKey_OtherPropertyThemed", ColorOf(button?.Foreground) == LightColor,
                $"fg={ColorOf(button?.Foreground)}");
            H.Check("ThemeRefStyle_BadKey_OwnPropertyNotSet",
                button is not null && ColorOf(button.Background) != LightColor && !HasLocal(button, WinUI.Control.BackgroundProperty),
                $"bg={ColorOf(button?.Background)}");

            setCount!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_BadKey_ReRenderStillThemed", ColorOf(button?.Foreground) == LightColor);

            var switched = H.FindControl<WinUI.Button>(b => b.Content is "trs-bad-switch");
            H.Check("ThemeRefStyle_BadKey_Switch_BadKeyDropsOldBrush",
                switched is not null && ColorOf(switched.Background) != LightColor
                && !HasLocal(switched, WinUI.Control.BackgroundProperty),
                $"bg={ColorOf(switched?.Background)}");
        });
    }

    /// <summary>
    /// A ThemeRef key WinUI can't find says nothing about the app's Style, which stays. Once the
    /// key is corrected the property binds, with the app's Style still on the control.
    /// </summary>
    internal sealed class MissingKeyKeepsAuthorBase(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsMissingFixedBrush", async host =>
        {
            var style = new Style(typeof(WinUI.TextBlock));
            style.Setters.Add(new Setter(WinUI.TextBlock.FontSizeProperty, 29d));
            Action<bool>? setFixed = null;
            host.Mount(ctx =>
            {
                var (isFixed, setter) = ctx.UseState(false);
                setFixed = setter;
                return VStack(TextBlock("trs-missing").Set(t => t.Style = style)
                    .Foreground(new ThemeRef(isFixed ? "TrsMissingFixedBrush" : "TrsNoSuchBrushAnywhere")))
                    .RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var tb = H.FindText("trs-missing");
            H.Check("ThemeRefStyle_MissingKey_AuthorStyleKept", tb is not null && Math.Abs(tb.FontSize - 29) < 0.01,
                $"fontSize={tb?.FontSize}");

            setFixed!(true);
            await Harness.Render();
            H.Check("ThemeRefStyle_MissingKey_Fixed_Themed", ColorOf(tb?.Foreground) == LightColor,
                $"fg={ColorOf(tb?.Foreground)}");
            H.Check("ThemeRefStyle_MissingKey_Fixed_AuthorStyleKept",
                tb is not null && Math.Abs(tb.FontSize - 29) < 0.01 && ReferenceEquals(tb.Style, style),
                $"fontSize={tb?.FontSize}");
        });
    }

    /// <summary>
    /// A themed control returned to the pool goes back without the binding Reactor installed,
    /// so the next renter starts clean instead of showing the previous renter's themed brush.
    /// </summary>
    internal sealed class PoolReturnDropsThemedStyle(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsPoolBrush", async host =>
        {
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                return phase switch
                {
                    0 => VStack(Title("trs-pool-themed").Foreground(new ThemeRef("TrsPoolBrush"))).RequestedTheme(ElementTheme.Light),
                    1 => VStack().RequestedTheme(ElementTheme.Light),
                    _ => VStack(TextBlock("trs-pool-plain")).RequestedTheme(ElementTheme.Light),
                };
            });
            await Harness.Render();
            var themed = H.FindText("trs-pool-themed");
            H.Check("ThemeRefStyle_Pool_Themed", themed is not null && ColorOf(themed.Foreground) == LightColor
                && HasLocal(themed, WinUI.TextBlock.ForegroundProperty));

            setPhase!(1);
            await Harness.Render();
            setPhase!(2);
            await Harness.Render();

            var plain = H.FindText("trs-pool-plain");
            H.Check("ThemeRefStyle_Pool_SameInstanceRented", plain is not null && ReferenceEquals(plain, themed));
            H.Check("ThemeRefStyle_Pool_StyleCleared", plain is not null && plain.Style is null && ColorOf(plain.Foreground) != LightColor,
                $"fg={ColorOf(plain?.Foreground)}");
            H.Check("ThemeRefStyle_Pool_BindingCleared", plain is not null && !HasLocal(plain, WinUI.TextBlock.ForegroundProperty));
        });
    }

    /// <summary>
    /// A resource key containing '|' or '=' that spells another binding set
    /// (<c>"A|Foreground=B"</c>) binds only the property it is declared on, and an element that
    /// really declares that other set gets both of its own bindings.
    /// </summary>
    internal sealed class CacheKeyDoesNotCollide(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKeys(H, ["TrsColA|Foreground=TrsColB", "TrsColA", "TrsColB"], async (host, _) =>
        {
            host.Mount(_ => VStack(
                Button("trs-collide-one").Background(new ThemeRef("TrsColA|Foreground=TrsColB")),
                Button("trs-collide-two").Background(new ThemeRef("TrsColA")).Foreground(new ThemeRef("TrsColB")))
                .RequestedTheme(ElementTheme.Light));
            await Harness.Render();

            var one = H.FindControl<WinUI.Button>(b => b.Content is "trs-collide-one");
            var two = H.FindControl<WinUI.Button>(b => b.Content is "trs-collide-two");
            H.Check("ThemeRefStyle_Collide_FirstThemed", ColorOf(one?.Background) == LightColor
                && one is not null && !HasLocal(one, WinUI.Control.ForegroundProperty));
            H.Check("ThemeRefStyle_Collide_SecondHasOwnStyle",
                two is not null && ColorOf(two.Background) == LightColor && ColorOf(two.Foreground) == LightColor,
                $"fg={ColorOf(two?.Foreground)}");
        });
    }

    /// <summary>
    /// A Style for a third-party control type (one that ships XAML metadata) stays on the control
    /// under a ThemeRef, and the ThemeRef applies, on mount and on later renders. A themed Style
    /// could not be built on that Style and had to replace it.
    /// </summary>
    internal sealed class ThirdPartyStyleFallsBack(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithThemeKey(H, "TrsThirdPartyBrush", async host =>
        {
            ReactorApp.RegisterControlAssembly(typeof(global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp).Assembly);
            var style = (Style)XamlReader.Load(
                $"<Style xmlns='{XamlNs}' xmlns:tp='using:Reactor.AppTests.ThirdPartyControls' " +
                "TargetType='tp:ThirdPartyControlWithPrivateDp'><Setter Property='MinWidth' Value='177'/></Style>");

            Action<int>? setCount = null;
            host.Mount(ctx =>
            {
                var (count, setter) = ctx.UseState(0);
                setCount = setter;
                return VStack(
                    TextBlock($"trs-third-party-{count}"),
                    new XamlHostElement(() => new global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp { Style = style })
                        .Background(new ThemeRef("TrsThirdPartyBrush"))).RequestedTheme(ElementTheme.Light);
            });
            await Harness.Render();

            var control = H.FindControl<global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp>(_ => true);
            var errorText = H.FindControl<WinUI.TextBlock>(t => t.Text?.Contains("xception", StringComparison.Ordinal) == true)?.Text;
            H.Check("ThemeRefStyle_ThirdParty_Mounted", control is not null && errorText is null, $"error={errorText}");
            H.Check("ThemeRefStyle_ThirdParty_Themed", ColorOf(control?.Background) == LightColor,
                $"bg={ColorOf(control?.Background)} styleIsAuthor={ReferenceEquals(control?.Style, style)}");
            H.Check("ThemeRefStyle_ThirdParty_AuthorStyleKept", control is not null
                && ReferenceEquals(control.Style, style) && Math.Abs(control.MinWidth - 177) < 0.01,
                $"minWidth={control?.MinWidth}");

            setCount!(1);
            await Harness.Render();
            H.Check("ThemeRefStyle_ThirdParty_ReRender_SameControl",
                ReferenceEquals(H.FindControl<global::Reactor.AppTests.ThirdPartyControls.ThirdPartyControlWithPrivateDp>(_ => true), control)
                && H.FindText("trs-third-party-1") is not null);
            H.Check("ThemeRefStyle_ThirdParty_ReRender_Themed", ColorOf(control?.Background) == LightColor);
            H.Check("ThemeRefStyle_ThirdParty_ReRender_AuthorStyleKept", control is not null && ReferenceEquals(control.Style, style));
        });
    }
}
