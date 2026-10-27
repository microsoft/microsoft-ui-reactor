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

    [Fact]
    public void MemberThatCannotReachTheRecord_IsInterceptedAsUsual()
    {
        // Positive control: the same overload pair, no record anywhere in the member. An int,
        // because a string would pick Show(Element?) through Element's implicit operator.
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(Snippet("", "Helpers.Show(42)"), completeAfterGeneration: Completion);

        Assert.Matches(ObjectOverloadInterceptor, generated);
    }
}