using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <see cref="ReactorDiagnostics.SourceProperty"/> on live controls: every realized control
/// carries it in diagnostics mode (not only the ones Reactor tags), component wrappers say
/// which component they mount, children name their owner, keys and the root are reported,
/// and nothing is written when publishing is off.
/// </summary>
internal class ReactorSource_PublishedOnEveryControl(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_Plain", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            // ── Off: nothing published ───────────────────────────────────
            ReactorSourcePublisher.IsEnabled = false;
            var offHost = H.CreateHost();
            offHost.Mount(_ => TextBlock("source-off"));
            await Harness.Render();
            var off = H.FindControl<WinUI.TextBlock>(t => t.Text == "source-off");
            H.Check("ReactorSource_OffWritesNothing", off is not null && ReactorDiagnostics.GetSource(off) is null);

            // ── On ───────────────────────────────────────────────────────
            ReactorSourcePublisher.IsEnabled = true;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return VStack(4,
                    TextBlock("source-plain"),
                    TextBlock("source-keyed").WithKey("k|1"),
                    Component<SourceProbe, int>(n),
                    Button("source-bump", () => setN(n + 1)));
            });
            await Harness.Render();

            string? Of(DependencyObject? d) => d is null ? null : ReactorDiagnostics.GetSource(d);

            var plain = H.FindControl<WinUI.TextBlock>(t => t.Text == "source-plain");
            var plainValue = Of(plain);
            Console.WriteLine($"# plain: {plainValue}");
            // An untagged display leaf (no callbacks, key or extras) is published too.
            H.Check("ReactorSource_Plain",
                plainValue is not null && plainValue.StartsWith("v=1|", StringComparison.Ordinal)
                && plainValue.Contains("|element=TextBlock", StringComparison.Ordinal)
                && plainValue.Contains("|owner=FuncElement", StringComparison.Ordinal));

            var keyedValue = Of(H.FindControl<WinUI.TextBlock>(t => t.Text == "source-keyed"));
            Console.WriteLine($"# keyed: {keyedValue}");
            H.Check("ReactorSource_KeyEscaped", keyedValue?.Contains("|key=k%7C1", StringComparison.Ordinal) == true);

            var probeText = H.FindControl<WinUI.TextBlock>(t => t.Text.StartsWith("probe ", StringComparison.Ordinal));
            var probeValue = Of(probeText);
            var wrapperValue = Of(probeText is null ? null : VisualTreeHelper.GetParent(probeText));
            Console.WriteLine($"# probe child: {probeValue}");
            Console.WriteLine($"# probe wrapper: {wrapperValue}");
            H.Check("ReactorSource_ChildOwnedByComponent", probeValue?.Contains("|owner=SourceProbe", StringComparison.Ordinal) == true);
            H.Check("ReactorSource_WrapperMountsComponent",
                wrapperValue?.Contains("|element=Component", StringComparison.Ordinal) == true
                && wrapperValue.Contains("|mounts=SourceProbe", StringComparison.Ordinal)
                && wrapperValue.Contains("|owner=FuncElement", StringComparison.Ordinal));

            var rootValue = Of(plain is null ? null : VisualTreeHelper.GetParent(plain));
            Console.WriteLine($"# root: {rootValue}");
            H.Check("ReactorSource_RootNamed", rootValue?.Contains("|root=FuncElement", StringComparison.Ordinal) == true);

            // ── Re-render keeps every control described ─────────────────
            H.ClickButton("source-bump");
            await Harness.Render();
            var afterText = H.FindControl<WinUI.TextBlock>(t => t.Text == "probe 1");
            H.Check("ReactorSource_UpdatedChildStillDescribed",
                Of(afterText)?.Contains("|owner=SourceProbe", StringComparison.Ordinal) == true);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

internal sealed class SourceProbe : Component<int>
{
    public override Element Render() => TextBlock($"probe {Props}");
}
