#if COMPATIBILITY_NATIVE_BASELINE
using System.Collections.ObjectModel;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class NativeBaseline
{
    internal static void Register()
    {
        ReactorApp.RegisterControlAssembly(new Wct.SettingsControlsRns.
            CommunityToolkit_WinUI_Controls_SettingsControls_XamlTypeInfo.XamlMetaDataProvider());
        ReactorApp.RegisterControlAssembly(new Wct.ColorPickerRns.
            CommunityToolkit_WinUI_Controls_ColorPicker_XamlTypeInfo.XamlMetaDataProvider());
        ReactorApp.RegisterControlAssembly(new Wct.TabbedCommandBarRns.
            CommunityToolkit_WinUI_Controls_TabbedCommandBar_XamlTypeInfo.XamlMetaDataProvider());
        ReactorApp.RegisterControlAssembly(new Wct.RichSuggestBoxRns.
            CommunityToolkit_WinUI_Controls_RichSuggestBox_XamlTypeInfo.XamlMetaDataProvider());
        ReactorApp.RegisterControlAssembly(new Wct.RadialGaugeRns.
            CommunityToolkit_WinUI_Controls_RadialGauge_XamlTypeInfo.XamlMetaDataProvider());
    }

    internal static Scenario Replace(string name, Scenario mapped)
    {
        Func<FrameworkElement> create = name switch
        {
            "SettingsCard" => () => new Wct.SettingsCard(),
            "SettingsExpander" => () => new Wct.SettingsExpander(),
            "ColorPicker" => () => new Wct.ColorPicker(),
            "TabbedCommandBar" => () => new Wct.TabbedCommandBar(),
            "RichSuggestBox" => () => new Wct.RichSuggestBox(),
            "RadialGauge" => () => new Wct.RadialGauge(),
            _ => throw new InvalidOperationException($"No equivalent direct-native baseline for {name}.")
        };
        void Set(FrameworkElement element, int phase)
        {
            string label = phase == 0 ? "initial-label" : "updated-label";
            switch (element)
            {
                case Wct.SettingsCard card:
                    card.Width = 500;
                    card.Header = label;
                    card.Description = new Native.TextBlock { Text = $"description-{phase}" };
                    card.Content = new Native.TextBlock { Text = label };
                    card.HeaderIcon = new Native.FontIcon { Glyph = "\uE701" };
                    break;
                case Wct.SettingsExpander expander:
                    expander.Width = 500;
                    expander.Header = label;
                    expander.IsExpanded = true;
                    expander.Items.Clear();
                    expander.Items.Add(new Wct.SettingsCard { Header = label });
                    if (phase == 1) expander.Items.Add(new Wct.SettingsCard { Header = "added" });
                    break;
                case Wct.ColorPicker picker:
                    picker.Width = 400;
                    picker.Color = phase == 0 ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Blue;
                    picker.IsAlphaEnabled = phase == 1;
                    break;
                case Wct.TabbedCommandBar bar:
                    TabbedCommandBarChecks.Configure(bar, phase);
                    break;
                case Wct.RichSuggestBox suggestions:
                    suggestions.Width = 400;
                    suggestions.Height = 180;
                    suggestions.PlaceholderText = label;
                    suggestions.Prefixes = phase == 0 ? "@" : "#";
                    suggestions.ItemsSource = new ObservableCollection<string>(
                        ["Adele Vance", "Alex Wilber", "Diego Siciliani", "Grady Archie",
                            "Isaiah Langer", "Lee Gu", "Megan Bowen", "Patti Fernandez"]);
                    break;
                case Wct.RadialGauge gauge:
                    gauge.Width = 180;
                    gauge.Height = 180;
                    gauge.Minimum = 0;
                    gauge.Maximum = 100;
                    gauge.Value = phase == 0 ? 25 : 65;
                    gauge.Unit = phase == 0 ? "%" : "rpm";
                    break;
                default:
                    throw new InvalidOperationException("Unexpected direct-native control.");
            }
        }
        return new Scenario(
            phase => XamlHost(() =>
            {
                var control = create();
                Set(control, phase);
                return control;
            }, control => Set(control, phase), typeKey: name),
            (control, phase) =>
            {
                mapped.Check(control, phase);
                if (control is Wct.RadialGauge gauge)
                {
                    string supplied = $"Explicit native gauge name {phase}";
                    AutomationProperties.SetName(gauge, supplied);
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(gauge)!;
                    Program.Check(AutomationProperties.GetName(gauge) == supplied &&
                        peer.GetName() == $"radial gauge. unit {gauge.Unit}, {gauge.Value}" &&
                        peer.GetName() != supplied, "Native gauge name limitation did not reproduce.");
                    Program.Results.Note($"native gauge supplied='{supplied}'; peer='{peer.GetName()}'; role={peer.GetAutomationControlType()}");
                }
            }, mapped.Wrap);
    }
}
#endif
