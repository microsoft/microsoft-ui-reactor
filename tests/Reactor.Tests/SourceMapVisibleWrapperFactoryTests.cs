using System.Collections.Generic;
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
/// project shape. The factory is placed in a <c>.Wrapper.g.cs</c> tree to stand in for that
/// fed-back output.</para>
/// </summary>
public sealed class SourceMapVisibleWrapperFactoryTests
{
    private const string Record = """
        namespace App
        {
            public class Gauge { }

            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Gauge))]
            public partial record GaugeElement
            {
                public static GaugeElement Make() => new GaugeElement();
            }
        }
        """;

    // What the wrapper generator emitted on the first pass.
    private const string Generated = """
        namespace App
        {
            partial record GaugeElement : Microsoft.UI.Reactor.Core.Element
            {
                public static GaugeElement Gauge(double value = 0) => new GaugeElement();
            }
        }
        """;

    private const string Screen = """
        using Microsoft.UI.Reactor.Core;
        using static Microsoft.UI.Reactor.Factories;

        namespace App
        {
            public static class Screen
            {
                public static Element A() => GaugeElement.Gauge(3);
                public static Element B() => VStack(GaugeElement.Gauge());
                public static Element C() => GaugeElement.Make();
            }
        }
        """;

    private static string Run(string generatedPath)
        => SourceMapTransparentGeneratorTests.Run(
            Screen,
            extraFiles: new List<(string, string)>
            {
                ("Record.cs", Record),
                (generatedPath, Generated),
            }).GeneratedSource;

    [Fact]
    public void VisibleGeneratedWrapperFactory_IsIntercepted()
    {
        var generated = Run(@"obj\generated\GaugeElement.Wrapper.g.cs");

        Assert.Equal(2, Count(generated, "var __e = global::App.GaugeElement.Gauge(__a0);"));
        Assert.Contains("var __e = global::Microsoft.UI.Reactor.Factories.VStack(__a0);", generated);
    }

    [Fact]
    public void OtherStaticsOnTheWrapperElement_AreNot()
    {
        var generated = Run(@"obj\generated\GaugeElement.Wrapper.g.cs");

        // Positive control in the same output: the factory beside it IS intercepted.
        Assert.Contains("global::App.GaugeElement.Gauge(", generated);
        Assert.DoesNotContain("global::App.GaugeElement.Make(", generated);
    }

    [Fact]
    public void HandWrittenStaticNamedLikeTheFactory_IsNot()
    {
        // The same method in an ordinary source file is the author's own, and the generated
        // overloads beside it are invisible in a normal build, so binding to it is a guess.
        var generated = Run("GaugeHelpers.cs");

        Assert.DoesNotContain("global::App.GaugeElement.Gauge(", generated);
        // Positive control: the generator ran and intercepted the container.
        Assert.Contains("global::Microsoft.UI.Reactor.Factories.VStack(", generated);
    }

    [Theory]
    [InlineData(SourceMapTransparentGeneratorTests.SurfaceShape.MetadataReference)]
    [InlineData(SourceMapTransparentGeneratorTests.SurfaceShape.ProjectReference)]
    public void WrapperFactoryFromAReferencedLibrary_IsIntercepted(SourceMapTransparentGeneratorTests.SurfaceShape shape)
    {
        // A wrapper package: the record is complete there, both halves are visible, so the
        // call binds exactly and this generator stamps it (the wrapper generator in the
        // consumer only claims calls that bind to nothing). The hand-written Make() beside
        // the factory is still not a factory.
        const string library = """
            namespace Microsoft.UI.Reactor.Wrappers
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                public sealed class GenerateReactorWrapperAttribute(global::System.Type controlType) : global::System.Attribute
                {
                    public global::System.Type ControlType { get; } = controlType;
                }
            }
            namespace App
            {
                public class Gauge { }

                [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Gauge))]
                public partial record GaugeElement : Microsoft.UI.Reactor.Core.Element
                {
                    public static GaugeElement Gauge(double value = 0) => new GaugeElement();
                    public static GaugeElement Make() => new GaugeElement();
                }
            }
            """;
        const string user = """
            namespace User
            {
                public static class Screen
                {
                    public static Microsoft.UI.Reactor.Core.Element A() => App.GaugeElement.Gauge(3);
                    public static Microsoft.UI.Reactor.Core.Element B() => App.GaugeElement.Make();
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(user, surface: shape, librarySource: library);

        Assert.Equal(1, Count(generated, "var __e = global::App.GaugeElement.Gauge(__a0);"));
        Assert.DoesNotContain("global::App.GaugeElement.Make(", generated);
    }

    [Fact]
    public void WrapperThatSpellsItsOwnBase_IsStillAnalysed()
    {
        // The only wrapper here declares `: Element` itself: complete, but its factory is
        // still generated and invisible, so the containing calls and the declared name
        // depend on the wrapper analysis running at all.
        const string record = """
            namespace Controls
            {
                public class Gauge { }
            }

            namespace App
            {
                [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Controls.Gauge))]
                public partial record GaugeElement : Microsoft.UI.Reactor.Core.Element;
            }
            """;
        const string screen = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;
            using static App.GaugeElement;

            namespace App
            {
                public static class Screen
                {
                    public static Element A() { var dial = Gauge(3); return VStack(dial); }
                    public static Element B() => VStack(Gauge());
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(
            screen,
            extraFiles: new List<(string, string)> { ("Record.cs", record) },
            completeAfterGeneration: "namespace App { partial record GaugeElement { public static GaugeElement Gauge(double value = 0) => new GaugeElement(); } }");

        Assert.Equal(2, Count(generated, "var __e = global::Microsoft.UI.Reactor.Factories.VStack(__a0);"));
        Assert.Contains("@\"dial\");", generated);
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        for (int i = text.IndexOf(needle, global::System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, global::System.StringComparison.Ordinal))
            count++;
        return count;
    }
}