using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using AP = Microsoft.UI.Xaml.Automation.AutomationProperties;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Exercises panel borders across updates, interaction states, and theme changes (#1039).</summary>
internal sealed class PanelBorderFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        using var host = H.CreateHost();
        var physical = new Thickness(2, 3, 4, 5);
        const string themeKey = "Issue1039PanelBorderBrush";
        var themeBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
        var darkBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
        var hoverBrush = new SolidColorBrush(Microsoft.UI.Colors.Green);
        var resources = new ResourceDictionary();
        resources.ThemeDictionaries["Light"] = new ResourceDictionary { [themeKey] = themeBrush };
        resources.ThemeDictionaries["Dark"] = new ResourceDictionary { [themeKey] = darkBrush };
        Application.Current.Resources.MergedDictionaries.Add(resources);
        try
        {
            Action<int>? setPhase = null;
            host.Mount(ctx =>
            {
                var (phase, setter) = ctx.UseState(0);
                setPhase = setter;
                Element Configure(Element panel, string id)
                {
                    panel = panel.AutomationId(id);
                    return phase switch
                    {
                        0 => panel.BorderThickness(physical).BorderBrush("#FF3366CC")
                            .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                        1 => panel.BorderThickness(physical).BorderBrush("#FFCC6633")
                            .BorderInlineStart(new Thickness(9))
                            .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                        3 => panel.BorderInlineStart(new Thickness(9)),
                        5 => panel.BorderBrush(new ThemeRef(themeKey)).RequestedTheme(ElementTheme.Light),
                        7 => panel.BorderThickness(physical)
                            .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                        8 or 10 or 12 => panel.BorderThickness(physical).BorderBrush(darkBrush)
                            .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                        9 => panel.BorderThickness(physical).BorderBrush(themeBrush)
                            .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                        11 => panel.BorderThickness(physical)
                            .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                        13 => panel.BorderThickness(physical).BorderBrush(themeBrush),
                        _ => panel,
                    };
                }
                return VStack(
                    Configure(Grid([GridSize.Star()], [GridSize.Auto], TextBlock("grid")), "panel-border-grid"),
                    Configure(VStack(TextBlock("stack")), "panel-border-stack"),
                    Configure(RelativePanel(TextBlock("relative")), "panel-border-relative"));
            });

            await Harness.Render();
            var panels = new[]
            {
                H.FindControl<Grid>(p => AP.GetAutomationId(p) == "panel-border-grid"),
                (Panel?)H.FindControl<StackPanel>(p => AP.GetAutomationId(p) == "panel-border-stack"),
                H.FindControl<RelativePanel>(p => AP.GetAutomationId(p) == "panel-border-relative"),
            };
            H.Check("PanelBorder_Present", panels.All(p => p is not null));
            if (panels.Any(p => p is null)) return;
            var thicknessProperties = new[]
            {
                WinUI.Grid.BorderThicknessProperty, StackPanel.BorderThicknessProperty, WinUI.RelativePanel.BorderThicknessProperty,
            };
            var brushProperties = new[]
            {
                WinUI.Grid.BorderBrushProperty, StackPanel.BorderBrushProperty, WinUI.RelativePanel.BorderBrushProperty,
            };
            string[] phases = ["Mount", "UpdateRtl", "Unset", "InlineNativeBase", "UnsetInline", "Theme", "UnsetTheme", "NullNormal",
                "UpdateDuringHover", "SeedNormal", "ChangeDuringHover", "ClearDuringHover", "SeedBeforeRemoval", "RemoveStateAndChangeBrush"];

            for (int phase = 0; phase < phases.Length; phase++)
            {
                if (phase != 0)
                {
                    if (phase is 8 or 10 or 13)
                        for (int i = 0; i < panels.Length; i++)
                        {
                            Reconciler.SimulatePointerOverForTests(panels[i]!, isOver: true);
                            H.Check($"PanelBorder_{panels[i]!.GetType().Name}_{phases[phase]}_HoverBeforeUpdate",
                                ReferenceEquals(panels[i]!.GetValue(brushProperties[i]), hoverBrush));
                        }
                    foreach (var panel in panels)
                        panel!.FlowDirection = phase == 1 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                    // Seed an asymmetric native value with no physical modifier in the old
                    // or new bag. The inline-only write must preserve the other three edges.
                    if (phase == 3)
                        for (int i = 0; i < panels.Length; i++)
                            panels[i]!.SetValue(thicknessProperties[i], physical);
                    setPhase!(phase);
                    await Harness.Render();
                }

                for (int i = 0; i < panels.Length; i++)
                {
                    var panel = panels[i]!;
                    if (phase == 1 || phase >= 7)
                        H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}_ReusesControl",
                            ReferenceEquals(panel, H.FindControl<FrameworkElement>(p => AP.GetAutomationId(p) == AP.GetAutomationId(panel))));
                    var thickness = (Thickness)panel.GetValue(thicknessProperties[i]);
                    var brush = panel.GetValue(brushProperties[i]) as SolidColorBrush;
                    bool cleared = panel.ReadLocalValue(thicknessProperties[i]) == DependencyProperty.UnsetValue
                        && panel.ReadLocalValue(brushProperties[i]) == DependencyProperty.UnsetValue;
                    bool correct = phase switch
                    {
                        0 => thickness == physical && brush?.Color == global::Windows.UI.Color.FromArgb(255, 51, 102, 204),
                        1 => thickness == new Thickness(2, 3, 9, 5)
                            && brush?.Color == global::Windows.UI.Color.FromArgb(255, 204, 102, 51),
                        2 or 4 => cleared,
                        3 => thickness == new Thickness(9, 3, 4, 5),
                        5 => brush?.Color == themeBrush.Color && panel.Style is not null,
                        7 => thickness == physical && brush is null
                            && panel.ReadLocalValue(brushProperties[i]) == DependencyProperty.UnsetValue,
                        8 or 10 or 11 => thickness == physical && ReferenceEquals(brush, hoverBrush),
                        9 or 13 => thickness == physical && ReferenceEquals(brush, themeBrush),
                        12 => thickness == physical && ReferenceEquals(brush, darkBrush),
                        _ => brush is null && panel.Style is null,
                    };
                    H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}", correct);

                    if (phase is 0 or 1 or 7)
                    {
                        Reconciler.SimulatePointerOverForTests(panel, isOver: true);
                        H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}_HoverBrush",
                            ReferenceEquals(panel.GetValue(brushProperties[i]), hoverBrush));
                        Reconciler.SimulatePointerOverForTests(panel, isOver: false);
                        H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}_NormalBrushRestored",
                            ReferenceEquals(panel.GetValue(brushProperties[i]), brush));
                        if (phase == 7)
                            H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}_LocalBrushReleased",
                                panel.ReadLocalValue(brushProperties[i]) == DependencyProperty.UnsetValue);
                    }
                    if (phase is 8 or 11)
                    {
                        Reconciler.SimulatePointerOverForTests(panel, isOver: false);
                        H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}_LatestNormalRestored",
                            ReferenceEquals(panel.GetValue(brushProperties[i]), phase == 8 ? darkBrush : null));
                        H.Check($"PanelBorder_{panel.GetType().Name}_{phases[phase]}_LocalValueRestored",
                            phase == 8 ? ReferenceEquals(panel.ReadLocalValue(brushProperties[i]), darkBrush)
                                : panel.ReadLocalValue(brushProperties[i]) == DependencyProperty.UnsetValue);
                    }
                }

                if (phase == 5)
                {
                    // Change only the native theme: the existing ThemeResource style must
                    // resolve a different brush without another Reactor render/update.
                    foreach (var panel in panels)
                        panel!.RequestedTheme = ElementTheme.Dark;
                    for (int i = 0; i < panels.Length; i++)
                    {
                        var panel = panels[i]!;
                        var property = brushProperties[i];
                        H.Check($"PanelBorder_{panel.GetType().Name}_ThemeChanged",
                            await Harness.WaitFor(() => panel.ActualTheme == ElementTheme.Dark
                                && (panel.GetValue(property) as SolidColorBrush)?.Color == darkBrush.Color));
                    }
                }
            }

            host.Mount(_ => VStack(
                Button("Styled button").AutomationId("border-restore-button")
                    .BorderBrush(new ThemeRef(themeKey)).RequestedTheme(ElementTheme.Light)
                    .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush)),
                Border(TextBlock("Styled border")).AutomationId("border-restore-border")
                    .BorderBrush(new ThemeRef(themeKey)).RequestedTheme(ElementTheme.Light)
                    .InteractionStates(states => states.PointerOver(borderBrush: hoverBrush))));
            await Harness.Render();
            FrameworkElement?[] styledControls = [
                H.FindControl<WinUI.Button>(c => AP.GetAutomationId(c) == "border-restore-button"),
                H.FindControl<WinUI.Border>(c => AP.GetAutomationId(c) == "border-restore-border"),
            ];
            DependencyProperty[] styledProperties = [WinUI.Control.BorderBrushProperty, WinUI.Border.BorderBrushProperty];
            H.Check("BorderRestore_StyledControlsPresent", styledControls.All(c => c is not null));
            for (int i = 0; i < styledControls.Length; i++)
            {
                if (styledControls[i] is not { } control) continue;
                var property = styledProperties[i];
                var name = control.GetType().Name;
                H.Check($"BorderRestore_{name}_NormalComesFromStyle", control.Style is not null
                    && ReferenceEquals(control.GetValue(property), themeBrush)
                    && control.ReadLocalValue(property) == DependencyProperty.UnsetValue);
                Reconciler.SimulatePointerOverForTests(control, isOver: true);
                H.Check($"BorderRestore_{name}_HoverBrush", ReferenceEquals(control.GetValue(property), hoverBrush));
                Reconciler.SimulatePointerOverForTests(control, isOver: false);
                H.Check($"BorderRestore_{name}_NormalBrushRestored", ReferenceEquals(control.GetValue(property), themeBrush));
                H.Check($"BorderRestore_{name}_LocalBrushReleased", control.ReadLocalValue(property) == DependencyProperty.UnsetValue);
                control.RequestedTheme = ElementTheme.Dark;
                H.Check($"BorderRestore_{name}_ThemeStillResolves", await Harness.WaitFor(() =>
                    control.ActualTheme == ElementTheme.Dark && ReferenceEquals(control.GetValue(property), darkBrush)));
            }
        }
        finally
        {
            host.Dispose();
            Application.Current.Resources.MergedDictionaries.Remove(resources);
        }
    }
}
