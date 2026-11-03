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
            return VStack(state.ShowBorder ? pill : null, TextBlock($"overview {state.Revision}")).RequestedTheme(state.Theme);
        }
    }
}
