using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest coverage for the specialized editor stack: mounts the editor
/// factories directly (outside a DataGrid) to confirm each one produces a
/// valid visual tree under real WinUI. Catches the "eager brush allocation"
/// class of bugs that unit tests can't see.
/// </summary>
internal static class SpecializedEditorsTests
{
    internal class CheckBoxEditorMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var factory = Editors.CheckBox();
                return factory(true, _ => { }).AutomationId("cb");
            });
            await Harness.Render();

            var control = H.FindControl<CheckBox>(c =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "cb");
            H.Check("Editors_CheckBox_Mounts", control is not null);
            H.Check("Editors_CheckBox_InitialValue", control?.IsChecked == true);
        }
    }

    internal class ToggleEditorMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var factory = Editors.Toggle();
                return factory(true, _ => { }).AutomationId("ts");
            });
            await Harness.Render();

            var control = H.FindControl<ToggleSwitch>(c =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "ts");
            H.Check("Editors_Toggle_Mounts", control is not null);
            H.Check("Editors_Toggle_InitialValue", control?.IsOn == true);
        }
    }

    internal class NumberEditorMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var factory = Editors.Number(typeof(int), min: 0, max: 100);
                return factory(42, _ => { }).AutomationId("nb");
            });
            await Harness.Render();

            var control = H.FindControl<NumberBox>(c =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "nb");
            H.Check("Editors_Number_Mounts", control is not null);
            H.Check("Editors_Number_InitialValue", control?.Value == 42);
            H.Check("Editors_Number_MinApplied", control?.Minimum == 0);
            H.Check("Editors_Number_MaxApplied", control?.Maximum == 100);
        }
    }

    internal class DateEditorMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var factory = Editors.Date();
                return factory(new DateTime(2026, 1, 15), _ => { }).AutomationId("dp");
            });
            await Harness.Render();

            var control = H.FindControl<DatePicker>(c =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "dp");
            H.Check("Editors_Date_Mounts", control is not null);
        }
    }

    internal class ColorEditorMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Standard tier now inlines a ColorPicker directly (avoids the
                // preview2 WinAppSDK crash with ColorPicker-in-Flyout).
                var factory = Editors.Color();
                return factory(global::Microsoft.UI.Colors.Crimson, _ => { }).AutomationId("cp");
            });
            await Harness.Render();

            var picker = H.FindControl<ColorPicker>(c =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "cp");
            H.Check("Editors_Color_PickerMounts", picker is not null);
        }
    }

    internal class ColorCompactEditorMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Compact tier (DataGrid cells): swatch + hex text box.
                var factory = Editors.ColorCompact();
                return factory(global::Microsoft.UI.Colors.Crimson, _ => { }).AutomationId("cc");
            });
            await Harness.Render();

            // Find the hex TextBox by its expected initial Crimson hex value —
            // proves the compact swatch+hex layout rendered and avoids matching
            // an unrelated TextBox elsewhere in the host window.
            var tb = H.FindControl<TextBox>(textBox =>
                textBox.Text is string s && s.Equals("#DC143C", StringComparison.OrdinalIgnoreCase));
            H.Check("Editors_ColorCompact_HexField", tb is not null);
        }
    }

    internal class HyperlinkRendererMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var renderer = CellRenderers.Hyperlink();
                return renderer(new Uri("https://example.com")).AutomationId("hl");
            });
            await Harness.Render();

            var link = H.FindControl<HyperlinkButton>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "hl");
            H.Check("Renderers_Hyperlink_Mounts", link is not null);
            H.Check("Renderers_Hyperlink_Href",
                link?.NavigateUri?.ToString() == "https://example.com/");
        }
    }

    internal class ColorSwatchRendererMounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var renderer = CellRenderers.ColorSwatch();
                return renderer(global::Microsoft.UI.Colors.DodgerBlue).AutomationId("sw");
            });
            await Harness.Render();

            // ColorSwatch returns HStack(Border, TextBlock). We'd find a TextBlock
            // holding "#FF1E90FF" (ARGB) or "#1E90FF" (RGB) — our helper emits RGB.
            var text = H.FindControl<TextBlock>(tb =>
                tb.Text is string s && s.Equals("#1E90FF", StringComparison.OrdinalIgnoreCase));
            H.Check("Renderers_ColorSwatch_HexLabel", text is not null);
        }
    }
}
