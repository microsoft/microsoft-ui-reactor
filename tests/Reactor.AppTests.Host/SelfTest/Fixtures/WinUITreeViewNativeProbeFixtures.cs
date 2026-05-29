using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// EMPIRICAL PROBE (issue #447 design spike) — NOT a Reactor test.
///
/// Builds a *native* WinUI <see cref="TreeView"/> in node mode
/// (<c>RootNodes</c>) and asks one question three ways: can a rich
/// <see cref="Button"/> placed directly into <see cref="TreeViewNode.Content"/>
/// actually render? This settles whether WinUI's ContentPresenter hosts a
/// live UIElement in node mode, or whether that's a WPF-ism.
///
/// Reports each variant's result via H.Check so it shows up in TAP output;
/// no Reactor reconciler is involved at all.
/// </summary>
internal sealed class WinUITreeViewNativeProbe(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        // ── Variant 1: node.Content = Button, NO ItemTemplate ──────────────
        // (the pure "default template stringifies the node" path)
        {
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            var tree = new TreeView();
            var btn = new Button { Content = "PROBE_V1_BUTTON" };
            tree.RootNodes.Add(new TreeViewNode { Content = btn, IsExpanded = true });
            parent.Children.Add(tree);
            await Harness.Render();

            bool buttonInTree = H.FindControl<Button>(b => b.Content is string s && s == "PROBE_V1_BUTTON") is not null;
            bool labelVisible = H.FindText("PROBE_V1_BUTTON") is not null;
            // EXPECTED native behavior: node-mode default template stringifies the
            // node and CANNOT host a live UIElement — so the Button must NOT appear.
            // (If this ever flips, WinUI changed; revisit TreeView<T> hosting.)
            H.Check("Probe_V1_NoTemplate_ButtonNotHosted", !buttonInTree);
            H.Check("Probe_V1_NoTemplate_LabelNotVisible", !labelVisible);

            H.SetContent(null);
            await Harness.Render();
        }

        // ── Variant 2: node.Content = Button, ItemTemplate = ContentControl
        //    binding {Binding Content} (DataContext = TreeViewNode) ──────────
        {
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            var tree = new TreeView
            {
                ItemTemplate = (DataTemplate)XamlReader.Load(
                    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                    "<ContentControl Content='{Binding Content}' " +
                    "HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>" +
                    "</DataTemplate>"),
            };
            var btn = new Button { Content = "PROBE_V2_BUTTON" };
            tree.RootNodes.Add(new TreeViewNode { Content = btn, IsExpanded = true });
            parent.Children.Add(tree);
            await Harness.Render();

            bool buttonInTree = H.FindControl<Button>(b => b.Content is string s && s == "PROBE_V2_BUTTON") is not null;
            bool labelVisible = H.FindText("PROBE_V2_BUTTON") is not null;
            H.Check("Probe_V2_BindContentTemplate_ButtonHosted", buttonInTree);
            H.Check("Probe_V2_BindContentTemplate_LabelVisible", labelVisible);

            H.SetContent(null);
            await Harness.Render();
        }

        // ── Variant 3 (control): node.Content = string, NO ItemTemplate ────
        // Sanity: confirms the default node-mode path renders text.
        {
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            var tree = new TreeView();
            tree.RootNodes.Add(new TreeViewNode { Content = "PROBE_V3_STRING", IsExpanded = true });
            parent.Children.Add(tree);
            await Harness.Render();

            bool textVisible = H.FindTextContaining("PROBE_V3_STRING") is not null;
            H.Check("Probe_V3_StringContent_TextVisible", textVisible);

            H.SetContent(null);
            await Harness.Render();
        }
    }
}
