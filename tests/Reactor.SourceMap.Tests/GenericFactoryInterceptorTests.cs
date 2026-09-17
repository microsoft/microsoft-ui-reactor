using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 Route B — generic-factory interception.
///
/// <para>The question is binary: can a correctly-binding interceptor be emitted
/// for a generic factory? The trap is that an interceptor which silently fails
/// to apply leaves <c>CallSite</c> null, which a weak test reads as "no crash,
/// fine". Every assertion below therefore separates THREE outcomes:</para>
/// <list type="bullet">
///   <item><b>applied and correct</b> — non-null AND equal to an independent
///   <c>[CallerLineNumber]</c> probe captured on the same physical line;</item>
///   <item><b>applied and wrong</b> — non-null but a different line;</item>
///   <item><b>never applied</b> — null.</item>
/// </list>
/// <para><see cref="PositiveControl_NonGenericFactoryInThisFileIsIntercepted"/>
/// proves the generator did emit for THIS file, so a null on a generic factory
/// is provably "the interceptor did not apply" and not "the test could not
/// tell".</para>
/// </summary>
[Collection("SourceMap")]
public sealed class GenericFactoryInterceptorTests : IDisposable
{
    private readonly ITestOutputHelper _out;

    public GenericFactoryInterceptorTests(ITestOutputHelper output)
    {
        _out = output;
        ReactorSourceMap.Enabled = true;
    }

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    private enum Outcome { NeverApplied, AppliedAndWrong, AppliedAndCorrect }

    private Outcome Classify(string label, Element? element, int expectedLine)
    {
        if (element is null) { _out.WriteLine($"{label,-22} : element was null"); return Outcome.NeverApplied; }
        var cs = element.CallSite;
        if (cs is null) { _out.WriteLine($"{label,-22} : NEVER APPLIED (CallSite null)"); return Outcome.NeverApplied; }
        if (cs.Value.LineNumber != expectedLine)
        {
            _out.WriteLine($"{label,-22} : APPLIED BUT WRONG (got {cs.Value.LineNumber}, expected {expectedLine})");
            return Outcome.AppliedAndWrong;
        }
        _out.WriteLine($"{label,-22} : APPLIED AND CORRECT (line {cs.Value.LineNumber})");
        return Outcome.AppliedAndCorrect;
    }

    // ── Positive control ──────────────────────────────────────────────────

    [Fact]
    public void PositiveControl_NonGenericFactoryInThisFileIsIntercepted()
    {
        // If this fails, every null below is uninterpretable — it would mean the
        // generator emitted nothing for this file at all.
        var element = TextBlock("control"); var expected = Line();

        Assert.Equal(Outcome.AppliedAndCorrect, Classify("control/TextBlock", element, expected));
    }

    // ── Risk 1+2: single type parameter with a base-type + new() constraint ─

    [Fact]
    public void Component_SingleTypeParameter_WithNewConstraint()
    {
        // public static ComponentElement Component<T>() where T : Component, new()
        var element = Component<ProbeComponent>(); var expected = Line();

        Assert.Equal(Outcome.AppliedAndCorrect, Classify("Component<T>", element, expected));
    }

    // ── Risk 2 crux: inter-parameter constraint (T constrained BY TProps) ──

    [Fact]
    public void Component_TwoTypeParameters_WithInterParameterConstraint()
    {
        // public static ComponentElement<TProps> Component<T, TProps>(TProps props)
        //     where T : Component<TProps>, new()
        // The interceptor must restate `where T : Component<TProps>` with TProps
        // resolving to its OWN second type parameter, not the original method's.
        var element = Component<ProbeTypedComponent, ProbeProps>(new ProbeProps("x")); var expected = Line();

        Assert.Equal(Outcome.AppliedAndCorrect, Classify("Component<T,TProps>", element, expected));
    }

    // ── Type parameter used inside parameter types (ordering matters) ──────

    [Fact]
    public void ForEach_TypeParameterInParameterTypes()
    {
        // public static Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render)
        // T appears in BOTH parameters; a mis-ordered or renamed type parameter
        // on the interceptor would fail to bind rather than bind wrongly.
        var element = ForEach(new[] { 1, 2, 3 }, i => TextBlock(i.ToString())); var expected = Line();

        Assert.Equal(Outcome.AppliedAndCorrect, Classify("ForEach<T>", element, expected));
    }

    [Fact]
    public void ForEach_ExplicitTypeArgument()
    {
        // Same factory, type argument written explicitly rather than inferred.
        var element = ForEach<int>(new[] { 1, 2 }, i => TextBlock(i.ToString())); var expected = Line();

        Assert.Equal(Outcome.AppliedAndCorrect, Classify("ForEach<int> explicit", element, expected));
    }

    // ── Unconstrained type parameter ──────────────────────────────────────

    [Fact]
    public void Memo_UnconstrainedTypeParameter()
    {
        // public static KeyedMemoElement Memo<TKey>(TKey key, Func<Element> factory)
        var element = Memo("k", () => TextBlock("memoized")); var expected = Line();

        Assert.Equal(Outcome.AppliedAndCorrect, Classify("Memo<TKey>", element, expected));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private sealed class ProbeComponent : Component
    {
        public override Element Render() => TextBlock("probe");
    }

    private sealed record ProbeProps(string Text);

    private sealed class ProbeTypedComponent : Component<ProbeProps>
    {
        public override Element Render() => TextBlock(Props.Text);
    }
}

