using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — guards the source-map generator's hand-maintained list of pass-through
/// factories.
///
/// <para><c>When</c>, <c>If</c> and <c>Expr</c> return an element the CALLER built
/// (<c>then()</c> / <c>render()</c>), so the interceptor generator must not stamp them:
/// doing so names the <c>When(</c> line as the creator of something built elsewhere.
/// The generator identifies them by name, in
/// <c>SourceMapInterceptorGenerator.PassThroughFactories</c>.</para>
///
/// <para>A name list drifts silently, and the failure mode is a wrong-but-plausible
/// location rather than a crash. This test makes the drift loud: it derives the
/// pass-through set structurally — a factory that returns the BASE <see cref="Element"/>
/// type and accepts a delegate producing an Element is returning something it did not
/// construct — and asserts it still matches the list the generator hardcodes. Add a
/// fourth such factory and this reddens, pointing at the generator.</para>
///
/// <para>Reflection over <c>Factories</c> only reads metadata; it never invokes a
/// factory, so nothing here constructs a WinUI object and the test stays headless.</para>
/// </summary>
public class PassThroughFactoryDriftTests
{
    /// <summary>Mirrors SourceMapInterceptorGenerator.PassThroughFactories.</summary>
    private static readonly string[] GeneratorList = ["When", "If", "Expr"];

    private static bool ProducesElementWithNoInput(Type t)
        // `Func<Element>` / `Func<Element?>`: a NULLARY producer, so the element it
        // yields was decided entirely by the caller and is handed straight back.
        // Deliberately excludes `Func<T, Element>` (a projection, as in ForEach) —
        // ForEach builds `new GroupElement(...)` from the results, so it genuinely
        // creates the element it returns and must be stamped.
        => typeof(Delegate).IsAssignableFrom(t)
           && t.IsGenericType
           && t.GetGenericArguments().Length == 1
           && typeof(Element).IsAssignableFrom(t.GetGenericArguments()[0]);

    private static bool LooksLikePassThrough(MethodInfo m)
        // Returns the base Element rather than a concrete element record: the method is
        // handing back whatever it was given, so it cannot name a specific type...
        => m.ReturnType == typeof(Element)
           // ...and it takes a nullary delegate yielding an Element, which is the thing
           // it hands back.
           && m.GetParameters().Any(p => ProducesElementWithNoInput(p.ParameterType));

    private static string[] DiscoverPassThroughs()
        => typeof(Factories)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(LooksLikePassThrough)
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void GeneratorPassThroughListMatchesTheDslSurface()
    {
        var discovered = DiscoverPassThroughs();
        var expected = GeneratorList.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.True(
            discovered.SequenceEqual(expected, StringComparer.Ordinal),
            $"The set of pass-through DSL factories has changed.\n" +
            $"  discovered: [{string.Join(", ", discovered)}]\n" +
            $"  generator:  [{string.Join(", ", expected)}]\n" +
            "A factory that returns the base Element and takes a delegate producing an " +
            "Element hands back something it did not build, so the source-map interceptor " +
            "must not stamp it — otherwise it reports itself as the creator. Update " +
            "SourceMapInterceptorGenerator.PassThroughFactories (and this list) together.");
    }

    /// <summary>
    /// Positive control for the discovery predicate. If it silently matched nothing, the
    /// assertion above would still pass whenever the generator list was also emptied,
    /// and would prove nothing about the real DSL.
    /// </summary>
    [Fact]
    public void TheDiscoveryPredicateActuallyMatchesSomething()
    {
        Assert.NotEmpty(DiscoverPassThroughs());
    }

    /// <summary>
    /// Negative control: an ordinary creating factory must NOT be classified as a
    /// pass-through, or the predicate would be trivially true and the drift guard would
    /// flag every factory in the DSL.
    /// </summary>
    [Fact]
    public void OrdinaryFactoriesAreNotClassifiedAsPassThroughs()
    {
        var discovered = DiscoverPassThroughs();

        Assert.DoesNotContain("TextBlock", discovered);
        Assert.DoesNotContain("VStack", discovered);
        Assert.DoesNotContain("Button", discovered);
        // ForEach is the near miss that matters: it takes Func<T, Element> and returns
        // the base Element, but builds `new GroupElement(...)` from the projected
        // results, so the ForEach( line really is the creator. An earlier version of
        // the predicate matched it, which would have silently dropped every ForEach
        // from the source map.
        Assert.DoesNotContain("ForEach", discovered);
    }
}
