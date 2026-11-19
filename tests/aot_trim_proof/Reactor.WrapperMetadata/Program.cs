using System.Runtime.CompilerServices;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using NativeTextBlock = Microsoft.UI.Xaml.Controls.TextBlock;

namespace WrapperMetadataProof;

[GenerateReactorWrapper(typeof(SettingsCard))]
[WrapElementSlot("Header")]
[WrapElementSlot("Description")]
internal partial record CardElement;

[GenerateReactorWrapper(typeof(SettingsExpander))]
[WrapElementSlot("Header")]
internal partial record ExpanderElement;

[GenerateReactorWrapper(typeof(HeaderedContentControl))]
[WrapElementSlot("Header")]
internal partial record HeaderedElement;

[GenerateReactorWrapper(typeof(CodeOnlyControl))]
internal partial record CodeOnlyElement;

// Neither this wrapper nor its provider should survive the native publish.
[GenerateReactorWrapper(typeof(RadialGauge))]
internal partial record UnusedGaugeElement;

public sealed partial class CodeOnlyControl : Microsoft.UI.Xaml.Controls.ContentControl;

internal sealed class Probe
{
    internal Action<int> SetVersion = null!;
}

internal sealed class Root : Component<Probe>
{
    public override Element Render()
    {
        var (version, setVersion) = UseState(0);
        Props.SetVersion = setVersion;
        return VStack(
            new CardElement
            {
                Header = TextBlock($"card-{version}"),
                Description = TextBlock($"description-{version}"),
            },
            new ExpanderElement
            {
                Header = TextBlock($"expander-{version}"),
                IsExpanded = true,
                Items = [new CardElement { Header = TextBlock($"item-{version}") }],
            },
            new HeaderedElement { Header = TextBlock($"library-two-{version}"), Content = TextBlock("content") },
            new CodeOnlyElement { Content = TextBlock($"code-only-{version}") });
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Console.WriteLine($"START dynamic-code={RuntimeFeature.IsDynamicCodeSupported}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(45));
            Console.Error.WriteLine("FAIL: realization/update timed out");
            Environment.Exit(2);
        });
        var probe = new Probe();
        ReactorApp.Run("Wrapper metadata proof", _ => Component<Root, Probe>(probe), width: 720, height: 640, configure: host =>
        {
            host.RenderErrorHandler = error =>
            {
                Console.Error.WriteLine(error.Exception);
                Environment.Exit(3);
                return null;
            };
            var timer = host.Window.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            NativeTextBlock? originalHeader = null;
            var phase = 0;
            timer.Tick += (_, _) =>
            {
                try
                {
                    var tree = Descendants(host.Window.Content).ToArray();
                    var card = tree.OfType<SettingsCard>().FirstOrDefault(c => c.Header is NativeTextBlock t && t.Text.StartsWith("card-", StringComparison.Ordinal));
                    var expander = tree.OfType<SettingsExpander>().SingleOrDefault();
                    var headered = tree.OfType<HeaderedContentControl>().SingleOrDefault();
                    if (card is null || expander is null || headered is null ||
                        !(card.ActualHeight > 0) || !(expander.ActualHeight > 0) || !(headered.ActualHeight > 0))
                        return;
                    var texts = tree.OfType<NativeTextBlock>().Select(t => t.Text).ToHashSet();
                    if (!new[] { $"card-{phase}", $"description-{phase}", $"expander-{phase}",
                            $"item-{phase}", $"library-two-{phase}", $"code-only-{phase}" }.All(texts.Contains))
                        return;
                    if (phase == 0)
                    {
                        originalHeader = (NativeTextBlock)card.Header;
                        Console.WriteLine($"REALIZED card={card.ActualWidth}x{card.ActualHeight} expander={expander.ActualWidth}x{expander.ActualHeight} items={expander.Items.Count}");
                        phase = 1;
                        probe.SetVersion(1);
                        return;
                    }
                    if (!ReferenceEquals(originalHeader, card.Header))
                        throw new InvalidOperationException("Header was replaced instead of updated.");
                    Console.WriteLine("PASS updated six texts; header-identity-preserved=True; two metadata libraries + code-only control");
                    timer.Stop();
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Environment.Exit(4);
                }
            };
            timer.Start();
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}
