using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — the source-map generator's per-member fast path in a compilation that declares
/// <c>[GenerateReactorWrapper]</c> records. A member none of whose identifiers can reach a
/// wrapper record skips the wrapper analysis; these tests pin that every way of naming one
/// keeps a member OUT of the fast path. Each is checked against the compilation the
/// interceptor will really be part of: the record completed with the <c>: Element</c> base
/// the wrapper generator adds, where binding to the wrong overload is a CS9144.
/// </summary>
public sealed class SourceMapWrapperFastPathTests
{
    /// <summary>An interceptor for <c>Show(object?)</c>, as emitted (<c>__Reactor_Show_0(object? __a0)</c>).</summary>
    private const string ObjectOverloadInterceptor = @"__Reactor_Show_\d+\(object\? __a0\)";

    private const string Completion = """
        namespace App { partial record GaugeElement : Microsoft.UI.Reactor.Core.Element; }
        """;

    private static string Snippet(string usings, string body) => usings + """

        namespace App
        {
            public class Gauge { }

            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Gauge))]
            public partial record GaugeElement;

            public static class Helpers
            {
                [Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparent]
                public static Microsoft.UI.Reactor.Core.Element Show(object? value) => new Microsoft.UI.Reactor.Core.EmptyElement();

                [Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparent]
                public static Microsoft.UI.Reactor.Core.Element Show(Microsoft.UI.Reactor.Core.Element? child) => child ?? new Microsoft.UI.Reactor.Core.EmptyElement();

                public static GaugeElement? Stored;
            }

            public static class Screen
            {
                public static Microsoft.UI.Reactor.Core.Element Render() => BODY;
            }
        }
        """.Replace("BODY", body, global::System.StringComparison.Ordinal);

    [Theory]
    [InlineData("using GaugeAlias = App.GaugeElement;", "Helpers.Show(default(GaugeAlias))")]
    [InlineData("", "Helpers.Show(default(GaugeElement))")]
    [InlineData("", "Helpers.Show(Helpers.Stored)")]
    public void AnyWayOfNamingTheRecord_KeepsTheMemberOutOfTheFastPath(string usings, string body)
    {
        // Show(object?) is all that binds before the record is completed; Show(Element?) after.
        // Run() fails if the emitted interceptor does not compile against the completed record.
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(Snippet(usings, body), completeAfterGeneration: Completion);

        Assert.DoesNotMatch(ObjectOverloadInterceptor, generated);
    }

    /// <summary>Declarations shared by <see cref="EachRouteToTheRecord_IsAnalysed"/>, in their own file.</summary>
    private const string Common = """
        using Microsoft.UI.Reactor.Core;

        namespace App
        {
            public class Gauge { }

            [Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Gauge))]
            public partial record GaugeElement;

            public static class Helpers
            {
                [Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparent]
                public static Element Show(object? value) => new EmptyElement();

                [Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparent]
                public static Element Show(Element? child) => child ?? new EmptyElement();
            }

            public abstract class Base<T> { public T Value = default!; }

            EXTRA
        }
        """;

    public static TheoryData<string, string, string, OutputKind> Routes => new()
    {
        {
            "top-level statements share one scope",
            "using App;\nGaugeElement child = new();\nvar view = Helpers.Show(child);\n",
            "",
            OutputKind.ConsoleApplication
        },
        {
            "inherited member of a record-closed base",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public class Screen : Base<GaugeElement> { public Element R() => Helpers.Show(Value); }\n}\n",
            "",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "a type with a record-closed base, used elsewhere",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public static class Other { public static Element R() => Helpers.Show(new Screen().Value); }\n}\n",
            "public class Screen : Base<GaugeElement> { }",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "primary-constructor parameter",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public class Screen(GaugeElement child) { public Element R() => Helpers.Show(child); }\n}\n",
            "",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "deconstruction",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public static class Use { public static Element R() { var (c, _) = new Provider(); return Helpers.Show(c); } }\n}\n",
            "public class Provider { public void Deconstruct(out GaugeElement c, out int i) { c = null!; i = 0; } }",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "foreach over a record-producing enumerator",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public static class Use { public static Element? R() { foreach (var x in new Bag()) return Helpers.Show(x); return null; } }\n}\n",
            "public class Bag { public System.Collections.Generic.IEnumerator<GaugeElement> GetEnumerator() { yield break; } }",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "this inside the record's own body",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public partial record GaugeElement { public Element AsView() => Helpers.Show(this); }\n}\n",
            "",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "primary-constructor parameter used from another partial declaration",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public partial class Screen { public Element R() => Helpers.Show(child); }\n}\n",
            "public partial class Screen(GaugeElement child) { }",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "type-parameter constraint declared in another partial declaration",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public partial class Screen<T> { public Element R(T child) => Helpers.Show(child); }\n}\n",
            "public partial class Screen<T> where T : GaugeElement { }",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "using static of a record-closed generic type",
            "using Microsoft.UI.Reactor.Core;\nusing static App.Cache<App.GaugeElement>;\nnamespace App {\n public static class Use { public static Element R() => Helpers.Show(Cached); }\n}\n",
            "public static class Cache<T> { public static T Cached = default!; }",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "positional-record property",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public static class Screen { public static Element R(ViewModel model) => Helpers.Show(model.Child); }\n}\n",
            "public record ViewModel(GaugeElement Child, int Index);",
            OutputKind.DynamicallyLinkedLibrary
        },
        {
            "delegate returning the record",
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public static class Use { public static Element R() => Helpers.Show(Cache.Make()); }\n}\n",
            "public delegate GaugeElement ChildFactory(); public static class Cache { public static ChildFactory Make = () => null!; }",
            OutputKind.DynamicallyLinkedLibrary
        },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRouteToTheRecord_IsAnalysed(string route, string user, string extra, OutputKind outputKind)
    {
        // Show(object?) is all that binds before the record is completed; Show(Element?) after.
        // Run() fails if an emitted interceptor does not compile against the completed record.
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(
            user,
            extraFiles: new List<(string, string)> { ("Common.cs", Common.Replace("EXTRA", extra, global::System.StringComparison.Ordinal)) },
            completeAfterGeneration: Completion,
            outputKind: outputKind);

        Assert.True(!global::System.Text.RegularExpressions.Regex.IsMatch(generated, ObjectOverloadInterceptor), route);
    }

    [Fact]
    public void ExpandingGenericType_DoesNotRecurseForever()
    {
        // Loop<int> : IMarker<Loop<Loop<int>>> : … never repeats a constructed type, so only a
        // depth bound ends the taint walk. Run() compiling the output is the oracle; before the
        // bound this overflowed the stack.
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(
            "using Microsoft.UI.Reactor.Core;\nnamespace App {\n public static class Use { public static Element R(GaugeElement g) => Helpers.Show(new Loop<int>()); }\n}\n",
            extraFiles: new List<(string, string)>
            {
                ("Common.cs", Common.Replace("EXTRA", "public interface IMarker<T> { } public class Loop<T> : IMarker<Loop<Loop<T>>> { }", global::System.StringComparison.Ordinal)),
            },
            completeAfterGeneration: Completion);

        Assert.NotNull(generated);
    }

    [Fact]
    public void MethodGroupThatAHiddenFactoryWouldShadow_IsNotTrusted()
    {
        // The generated Gauge(double?) is imported in an inner scope, so once it exists it
        // shadows the file-level rival Gauge(double?) returning int: Apply infers T as the
        // record, not int, and Show(Element?) runs instead of Show(object?) (checked against
        // real C# binding). Only the control, which names the rival outright, may get the object interceptor.
        const string user = """
            using Microsoft.UI.Reactor.Core;
            using static Rivals.GaugeRival;

            namespace Rivals
            {
                public static class GaugeRival
                {
                    public static int Gauge(double? value) => 0;
                }
            }

            namespace User
            {
                using static App.GaugeElement;

                public static class Use
                {
                    public static T Apply<T>(System.Func<double?, T> make) => make(1);

                    public static Element R() => App.Helpers.Show(Apply(Gauge));

                    // Positive control: the rival named outright is never shadowed.
                    public static Element Control() => App.Helpers.Show(Apply(Rivals.GaugeRival.Gauge));
                }
            }
            """;
        const string completion = """
            namespace App
            {
                partial record GaugeElement : Microsoft.UI.Reactor.Core.Element
                {
                    public static GaugeElement Gauge(double? value = default) => new GaugeElement();
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(
            user,
            extraFiles: new List<(string, string)> { ("Common.cs", Common.Replace("EXTRA", "", global::System.StringComparison.Ordinal)) },
            completeAfterGeneration: completion);

        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(generated, ObjectOverloadInterceptor));
    }

    [Fact]
    public void WrapperAttributeThroughAnAlias_IsStillAWrapper()
    {
        // The wrapper generator matches the attribute semantically, so an alias of it still
        // produces the `: Element` half; this generator has to recognise the record the same way.
        const string code = """
            using Wrap = Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapperAttribute;
            using Microsoft.UI.Reactor.Core;

            namespace App
            {
                public class Gauge { }

                [Wrap(typeof(Gauge))]
                public partial record GaugeElement;

                public static class Helpers
                {
                    [Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparent]
                    public static Element Show(object? value) => new EmptyElement();

                    [Microsoft.UI.Reactor.Diagnostics.ReactorSourceTransparent]
                    public static Element Show(Element? child) => child ?? new EmptyElement();
                }

                public static class Screen
                {
                    public static Element R(GaugeElement child) => Helpers.Show(child);
                }
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code, completeAfterGeneration: Completion);

        Assert.DoesNotMatch(ObjectOverloadInterceptor, generated);
    }

    [Fact]
    public void MemberThatCannotReachTheRecord_IsInterceptedAsUsual()
    {
        // Positive control: the same overload pair, no record anywhere in the member. An int,
        // because a string would pick Show(Element?) through Element's implicit operator.
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(Snippet("", "Helpers.Show(42)"), completeAfterGeneration: Completion);

        Assert.Matches(ObjectOverloadInterceptor, generated);
    }
}