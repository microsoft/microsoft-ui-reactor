using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;
using Color = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class BorderThemeBackgroundFixture(Harness h, bool reuse) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var prefix = reuse ? "BorderThemeBackground_Pooled" : "BorderThemeBackground_Fresh";
        var key = prefix + "Brush";
        var light = Color.FromArgb(255, 220, 230, 240);
        var dark = Color.FromArgb(255, 40, 50, 60);
        var direct = new SolidColorBrush(Color.FromArgb(255, 200, 30, 40));
        var resources = new ResourceDictionary();
        resources.ThemeDictionaries["Light"] = new ResourceDictionary { [key] = new SolidColorBrush(light) };
        resources.ThemeDictionaries["Dark"] = new ResourceDictionary { [key] = new SolidColorBrush(dark) };
        Application.Current.Resources.MergedDictionaries.Add(resources);
        ThemeRef.InvalidateResolutionCache();
        var host = H.CreateHost();
        try
        {
            var native = new WinUI.Border
            {
                Style = (Style)XamlReader.Load(
                    $"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Border'>" +
                    $"<Setter Property='Background' Value='{{ThemeResource {key}}}'/></Style>"),
            };
            H.SetContent(native);
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                native.RequestedTheme = theme;
                await Harness.Render();
                var expected = theme == ElementTheme.Light ? light : dark;
                H.Check(prefix + "_Native_" + theme, ColorOf(native) == expected
                    && ReferenceEquals(native.ReadLocalValue(WinUI.Border.BackgroundProperty), DependencyProperty.UnsetValue));
            }
            H.SetContent(null);
            await Harness.Render();

            var seed = new WinUI.Border { Background = direct };
            if (reuse)
            {
                host.Reconciler.ReturnControl(seed);
                // Exercise a local null left on a reused control, not just a clean pool return.
                seed.Background = null;
                H.Check(prefix + "_SeedHasLocalNull", seed.Background is null
                    && !ReferenceEquals(seed.ReadLocalValue(WinUI.Border.BackgroundProperty), DependencyProperty.UnsetValue));
                Console.WriteLine($"# Pooled Border local Background: {seed.ReadLocalValue(WinUI.Border.BackgroundProperty) ?? "null"}");
            }
            var state = new ScenarioState();
            Action<ScenarioState>? setState = null;
            host.Mount(_ => Component<Scenario, ScenarioProps>(new(key, state, setter => setState = setter)));
            await Harness.Render();
            var original = Find();
            if (reuse)
                H.Check(prefix + "_UsesReturnedBorder", ReferenceEquals(original, seed));
            Check("Mount", light);

            state = state with { Revision = 1 };
            setState!(state);
            await Harness.Render();
            H.Check(prefix + "_EquivalentRenderExecuted", H.FindText("overview 1") is not null);
            Check("EquivalentUpdate", light);

            state = state with { Text = "4" };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_ChangedChildUpdated", (Find().Child as WinUI.TextBlock)?.Text == "4");
            Check("ChangedChildUpdate", light);

            state = state with { Theme = ElementTheme.Dark };
            setState(state);
            await Harness.Render();
            await Harness.WaitFor(() => ColorOf(Find()) == dark);
            Check("NativeThemeChange", dark);

            state = state with { DirectBackground = direct };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_DirectValueWins", ReferenceEquals(Find(), original)
                && ReferenceEquals(Find().Background, direct)
                && ReferenceEquals(Find().ReadLocalValue(WinUI.Border.BackgroundProperty), direct));

            state = state with { DirectBackground = null };
            setState(state);
            await Harness.Render();
            Check("RemovingDirectValueRestoresTheme", dark);

            var modifier = new SolidColorBrush(Color.FromArgb(255, 30, 180, 90));
            state = state with { ModifierBackground = modifier, DirectBackground = direct };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_ConcreteModifierWinsOnChange", ReferenceEquals(Find().Background, modifier));

            var changedRecord = new SolidColorBrush(Color.FromArgb(255, 80, 90, 200));
            state = state with { DirectBackground = changedRecord };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_RecordUpdateWithUnchangedModifierWins",
                ReferenceEquals(Find().Background, changedRecord));

            state = state with { DirectBackground = null };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_RemovingRecordValuePreservesConcreteModifier",
                ReferenceEquals(Find(), original) && ReferenceEquals(Find().Background, modifier)
                && ReferenceEquals(Find().ReadLocalValue(WinUI.Border.BackgroundProperty), modifier));

            state = state with { ModifierBackground = null };
            setState(state);
            await Harness.Render();
            Check("RemovingConcreteModifierRestoresTheme", dark);

            // Mirror case: removing the modifier while the record brush stays set must restore
            // the record brush, not fall back to the theme.
            state = state with { DirectBackground = changedRecord, ModifierBackground = modifier };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_ModifierAddedOverRecordWins", ReferenceEquals(Find().Background, modifier));

            state = state with { ModifierBackground = null };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_RemovingConcreteModifierRestoresRecordValue",
                ReferenceEquals(Find(), original) && ReferenceEquals(Find().Background, changedRecord)
                && ReferenceEquals(Find().ReadLocalValue(WinUI.Border.BackgroundProperty), changedRecord));

            state = state with { DirectBackground = null };
            setState(state);
            await Harness.Render();
            Check("RemovingRecordValueAfterModifierRestoresTheme", dark);

            state = state with { SetterBackground = direct };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_NativeSetterWins", ReferenceEquals(Find().Background, direct));

            // Native setters keep precedence over a record brush, including on the render that
            // removes a concrete modifier (the modifier clear runs after the setters).
            state = state with { DirectBackground = changedRecord };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_NativeSetterWinsOverRecord", ReferenceEquals(Find().Background, direct));

            state = state with { ModifierBackground = modifier };
            setState(state);
            await Harness.Render();
            state = state with { ModifierBackground = null };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_RemovingModifierKeepsNativeSetterOverRecord",
                ReferenceEquals(Find().Background, direct)
                && ReferenceEquals(Find().ReadLocalValue(WinUI.Border.BackgroundProperty), direct));

            state = state with { DirectBackground = null, ModifierBackground = modifier };
            setState(state);
            await Harness.Render();
            state = state with { ModifierBackground = null };
            setState(state);
            await Harness.Render();
            H.Check(prefix + "_RemovingModifierKeepsNativeSetter",
                ReferenceEquals(Find().Background, direct)
                && ReferenceEquals(Find().ReadLocalValue(WinUI.Border.BackgroundProperty), direct));

            // Removing the Background modifier must not re-run setters over other modifiers that
            // changed in the same render.
            state = state with { ModifierBackground = modifier, ModifierWidth = 200, SetterWidth = 100 };
            setState(state);
            await Harness.Render();
            state = state with { ModifierBackground = null, ModifierWidth = 300 };
            setState(state);
            await Harness.Render();
            Console.WriteLine($"# {prefix}_RemovingModifierKeepsChangedWidth: width={Find().Width}");
            H.Check(prefix + "_RemovingModifierKeepsChangedWidth",
                global::System.Math.Abs(Find().Width - 300d) < 0.001d && ReferenceEquals(Find().Background, direct));

            state = state with { ModifierWidth = null, SetterWidth = null };
            setState(state);
            await Harness.Render();

            if (reuse)
            {
                state = state with { SetterBackground = null, DirectBackground = direct };
                setState(state);
                await Harness.Render();
                H.Check(prefix + "_RealPoolCycleStartsWithDirectBrush",
                    ReferenceEquals(Find(), original) && ReferenceEquals(Find().Background, direct));

                state = state with { ShowBorder = false };
                setState(state);
                await Harness.Render();
                H.Check(prefix + "_RealPoolCycleUnmounted",
                    H.FindControl<WinUI.Border>(border => ReferenceEquals(border, original)) is null);
                H.Check(prefix + "_PoolReturnClearsLocalBackground",
                    ReferenceEquals(original.ReadLocalValue(WinUI.Border.BackgroundProperty), DependencyProperty.UnsetValue));

                state = state with { ShowBorder = true, DirectBackground = null };
                setState(state);
                await Harness.Render();
                Check("RealPoolCycleRestoresThemeOnSameInstance", dark);
            }

            WinUI.Border Find() => H.FindControl<WinUI.Border>(border =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(border) == prefix)
                ?? throw new InvalidOperationException("Themed Border is missing.");

            void Check(string phase, Color expected)
            {
                var current = Find();
                var local = current.ReadLocalValue(WinUI.Border.BackgroundProperty);
                Console.WriteLine($"# {prefix}_{phase}: background={ColorOf(current)}; local={local ?? "null"}; theme={current.ActualTheme}");
                H.Check(prefix + "_" + phase, ReferenceEquals(current, original)
                    && ColorOf(current) == expected && current.Style is not null
                    && ReferenceEquals(local, DependencyProperty.UnsetValue));
            }
        }
        finally
        {
            host.Dispose();
            H.SetContent(null);
            Application.Current.Resources.MergedDictionaries.Remove(resources);
            ThemeRef.InvalidateResolutionCache();
            await Harness.Render();
        }
    }

    private static Color? ColorOf(WinUI.Border border) => (border.Background as SolidColorBrush)?.Color;

    private sealed record ScenarioState(
        int Revision = 0,
        string Text = "3",
        ElementTheme Theme = ElementTheme.Light,
        Brush? DirectBackground = null,
        Brush? SetterBackground = null,
        Brush? ModifierBackground = null,
        double? ModifierWidth = null,
        double? SetterWidth = null,
        bool ShowBorder = true);

    private sealed record ScenarioProps(string Key, ScenarioState Initial, Action<Action<ScenarioState>> Capture);

    private sealed class Scenario : Component<ScenarioProps>
    {
        public override Element Render()
        {
            var (state, setState) = UseState(Props.Initial);
            Props.Capture(setState);
            var name = Props.Key[..^"Brush".Length];
            var pill = Border(TextBlock(state.Text)).Padding(8, 2).MinWidth(24).MinHeight(24)
                .CornerRadius(12).Background(Theme.Ref(Props.Key)).VAlign(VerticalAlignment.Center)
                .AutomationName(name) with { Background = state.DirectBackground };
            if (state.ModifierBackground is { } modifier)
                pill = pill.Background(modifier);
            if (state.SetterBackground is { } brush)
                pill = pill.Set(border => border.Background = brush);
            if (state.ModifierWidth is { } width)
                pill = pill.Width(width);
            if (state.SetterWidth is { } setterWidth)
                pill = pill.Set(border => border.Width = setterWidth);
            return VStack(state.ShowBorder ? pill : null, TextBlock($"overview {state.Revision}")).RequestedTheme(state.Theme);
        }
    }
}

/// <summary>
/// A per-host handler override for <see cref="BorderElement"/> bypasses the built-in descriptor,
/// so removing a concrete <c>.Background(brush)</c> must still clear the brush through the
/// reconciler's own modifier path.
/// </summary>
internal sealed class BorderCustomHandlerBackgroundFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "BorderCustomHandlerBackground";

    public override async Task RunAsync()
    {
        using var host = H.CreateHost();
        try
        {
            host.Reconciler.RegisterType<BorderElement, WinUI.Border>(
                static (_, _, _) => new WinUI.Border(),
                static (_, _, _, _, _) => null);
            var brush = new SolidColorBrush(Color.FromArgb(255, 200, 30, 40));
            Action<bool>? setShowBackground = null;
            host.Mount(_ => Component<Toggle, ToggleProps>(new(brush, setter => setShowBackground = setter)));
            await Harness.Render();

            var border = H.FindControl<WinUI.Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == Name)
                ?? throw new InvalidOperationException("Custom-handled Border is missing.");
            // The custom handler never mounts the child, which proves it replaced the descriptor.
            H.Check(Name + "_CustomHandlerUsed", border.Child is null);
            H.Check(Name + "_ModifierApplied", ReferenceEquals(border.Background, brush));

            setShowBackground!(false);
            await Harness.Render();
            var local = border.ReadLocalValue(WinUI.Border.BackgroundProperty);
            Console.WriteLine($"# {Name}_RemovedModifierCleared: local={local ?? "null"}");
            H.Check(Name + "_RemovedModifierCleared", ReferenceEquals(local, DependencyProperty.UnsetValue));
        }
        finally
        {
            H.SetContent(null);
            await Harness.Render();
        }
    }

    private sealed record ToggleProps(Brush Brush, Action<Action<bool>> Capture);

    private sealed class Toggle : Component<ToggleProps>
    {
        public override Element Render()
        {
            var (showBackground, setShowBackground) = UseState(true);
            Props.Capture(setShowBackground);
            var border = Border(TextBlock("custom")).AutomationName(Name);
            return showBackground ? border.Background(Props.Brush) : border;
        }
    }
}

/// <summary>
/// A native setter that throws on the update that removes a concrete <c>.Background(brush)</c>
/// must not leave the descriptor's removal mark holding the discarded Border.
/// </summary>
internal sealed class BorderBackgroundRemovalThrowFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "BorderBackgroundRemovalThrow";

    public override async Task RunAsync()
    {
        using var host = H.CreateHost();
        try
        {
            var brush = new SolidColorBrush(Color.FromArgb(255, 200, 30, 40));
            Action<bool>? setRemove = null;
            host.Mount(_ => ErrorBoundary(
                Component<Thrower, ThrowerProps>(new(brush, setter => setRemove = setter)),
                _ => TextBlock(Name + " caught")));
            await Harness.Render();
            H.Check(Name + "_Mounted", H.FindText(Name + " caught") is null);

            setRemove!(true);
            await Harness.Render();
            // Positive control: the setter really threw during this update.
            H.Check(Name + "_SetterThrew", H.FindText(Name + " caught") is not null);
            Console.WriteLine($"# {Name}_NoMarkRetained: marks={host.Reconciler.BackgroundRemovalMarkCount}");
            H.Check(Name + "_NoMarkRetained", host.Reconciler.BackgroundRemovalMarkCount == 0);
        }
        finally
        {
            H.SetContent(null);
            await Harness.Render();
        }
    }

    private sealed record ThrowerProps(Brush Brush, Action<Action<bool>> Capture);

    private sealed class Thrower : Component<ThrowerProps>
    {
        public override Element Render()
        {
            var (remove, setRemove) = UseState(false);
            Props.Capture(setRemove);
            var border = Border(TextBlock("throw")).AutomationName(Name);
            return remove
                ? border.Set(static _ => throw new InvalidOperationException("setter boom"))
                : border.Background(Props.Brush);
        }
    }
}
