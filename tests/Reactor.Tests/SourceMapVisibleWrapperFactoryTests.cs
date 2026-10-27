using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — a wrapper-generated factory in a compilation where it IS visible.
///
/// <para>Normally no generator can see another's output, so the wrapper generator stamps
/// calls to its own factories (see <c>WrapperFactoryInterceptionTests</c> in
/// Reactor.SourceMap.Tests). A project shape that compiles twice and feeds the first pass's
/// generated files to the second would make the factory visible here instead — and then the
/// wrapper generator, which only claims calls that bind to nothing, leaves the call alone.
/// The source-map generator accepts it in that case, so coverage does not depend on the
/// project shape. The snippet hand-writes the factory to put the generator in exactly that
/// position.</para>
/// </summary>
public sealed class SourceMapVisibleWrapperFactoryTests
{
    private const string Snippet = """
        using Microsoft.UI.Reactor.Core;
        using static Microsoft.UI.Reactor.Factories;

        namespace App
        {
            public class Gauge { }

            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Gauge))]
            public partial record GaugeElement : Element
            {
                public static GaugeElement Gauge(double value = 0) => new GaugeElement();
                public static GaugeElement Make() => new GaugeElement();
            }

            public static class Screen
            {
                public static Element A() => GaugeElement.Gauge(3);
                public static Element B() => VStack(GaugeElement.Gauge());
                public static Element C() => GaugeElement.Make();
            }
        }
        """;

    [Fact]
    public void VisibleWrapperFactory_IsIntercepted()
    {
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(Snippet);

        Assert.Equal(2, Count(generated, "var __e = global::App.GaugeElement.Gauge(__a0);"));
        Assert.Contains("var __e = global::Microsoft.UI.Reactor.Factories.VStack(__a0);", generated);
    }

    [Fact]
    public void OtherStaticsOnTheWrapperElement_AreNot()
    {
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(Snippet);

        // Positive control in the same output: the factory beside it IS intercepted.
        Assert.Contains("global::App.GaugeElement.Gauge(", generated);
        Assert.DoesNotContain("global::App.GaugeElement.Make(", generated);
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        for (int i = text.IndexOf(needle, global::System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, global::System.StringComparison.Ordinal))
            count++;
        return count;
    }
}