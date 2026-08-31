using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 Route B — measurement 1 (coverage).
///
/// <para>Every line-number assertion is checked against an INDEPENDENT oracle
/// (<c>[CallerLineNumber]</c> captured on the same physical line) rather than a
/// hard-coded literal, so the tests survive edits above them and so a wrong
/// interceptor line cannot be papered over by updating a magic number.</para>
///
/// <para>All tests live in one class on purpose: <see cref="ReactorSourceMap.Enabled"/>
/// is process-global mutable state and xUnit runs tests within a class serially.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class SourceMapInterceptorTests : IDisposable
{
    public SourceMapInterceptorTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    private static string File([CallerFilePath] string file = "") => file;

    // ── Path parity with the CallerInfo route ─────────────────────────────

    [Fact]
    public void InterceptorPath_MatchesWhatCallerFilePathWouldProduce()
    {
        // The two routes must agree on the path string, or a "go to source"
        // consumer would behave differently depending on which provider is
        // wired in. This matters most under a deterministic build
        // (Directory.Build.props sets DeterministicSourcePaths when CI=true),
        // where the compiler rewrites [CallerFilePath] through the PathMap but
        // does NOT rewrite a string literal a generator emitted — the generator
        // has to apply the same map itself.
        var element = TextBlock("hi"); var expected = File();

        Assert.Equal(expected, element.CallSite!.Value.FilePath);
    }

    // ── Coverage: non-params factory ──────────────────────────────────────

    [Fact]
    public void NonParamsFactory_ReportsItsOwnCallSite()
    {
        var element = TextBlock("hello"); var expected = Line();

        Assert.NotNull(element.CallSite);
        Assert.Equal(expected, element.CallSite!.Value.LineNumber);
        Assert.EndsWith("SourceMapInterceptorTests.cs", element.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }

    // ── Coverage: params factory — the case CallerInfo cannot reach ────────

    [Fact]
    public void ParamsFactory_ReportsItsOwnCallSite()
    {
        var element = VStack(TextBlock("a"), TextBlock("b")); var expected = Line();

        Assert.NotNull(element.CallSite);
        Assert.Equal(expected, element.CallSite!.Value.LineNumber);
        Assert.EndsWith("SourceMapInterceptorTests.cs", element.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ParamsFactory_StillFiltersAndForwardsChildren()
    {
        // The interceptor must be behaviour-preserving: it forwards the same
        // expanded array to the real factory, including the null-filtering the
        // factory does. If the interceptor dropped or reordered arguments this
        // count would move.
        var element = VStack(TextBlock("a"), null, TextBlock("b"));

        Assert.Equal(2, global::System.Linq.Enumerable.Count(element.Children));
    }

    // ── Two call sites on different lines must not collapse ───────────────

    [Fact]
    public void DistinctCallSites_GetDistinctLines()
    {
        var first = TextBlock("one"); var firstLine = Line();
        var second = TextBlock("two"); var secondLine = Line();

        Assert.NotEqual(firstLine, secondLine);
        Assert.Equal(firstLine, first.CallSite!.Value.LineNumber);
        Assert.Equal(secondLine, second.CallSite!.Value.LineNumber);
    }

    // ── The runtime flag actually gates the stamp ─────────────────────────

    [Fact]
    public void FlagOff_LeavesSourceNull()
    {
        ReactorSourceMap.Enabled = false;
        try
        {
            var element = TextBlock("hello");
            Assert.Null(element.CallSite);
        }
        finally
        {
            ReactorSourceMap.Enabled = true;
        }

        // Positive control for the assertion above: the SAME probe, in the same
        // file, with the flag on, DOES produce a stamp. Without this, a generator
        // that silently emitted nothing would pass the null check for the wrong
        // reason.
        var control = TextBlock("hello");
        Assert.NotNull(control.CallSite);
    }

    // ── Route-independent coverage hole: the string → Element operator ────

    [Fact]
    public void BareStringChild_IsNotAttributedToUserCode()
    {
        // Element.cs:383 declares
        //     public static implicit operator Element(string text) => Factories.TextBlock(text);
        // so a bare-string child's TextBlock call site lives inside Element.cs,
        // in the Reactor assembly, NOT in user code. The generator only sees
        // invocations in the CONSUMER's syntax trees, so that call site is never
        // intercepted at all.
        var stack = VStack("bare string child"); var thisLine = Line();

        var child = global::System.Linq.Enumerable.Single(stack.Children);

        // Positive control: the VStack call site itself IS attributed here, so a
        // null child stamp is a real hole and not the generator having silently
        // emitted nothing for this file.
        Assert.Equal(thisLine, stack.CallSite!.Value.LineNumber);

        // The hole: no stamp at all. Route B yields null rather than a
        // confidently-wrong location inside framework source.
        Assert.Null(child.CallSite);
    }

    [Fact]
    public void ExplicitTextBlockChild_IsAttributed_PositiveControlForTheHole()
    {
        // Same shape, but the child is written explicitly. This is the control
        // that proves the null above is caused by the implicit operator and not
        // by children being unreachable in general.
        var stack = VStack(TextBlock("explicit child")); var thisLine = Line();

        var child = global::System.Linq.Enumerable.Single(stack.Children);

        Assert.Equal(thisLine, child!.CallSite!.Value.LineNumber);
    }

    // ── Pass-through factories must not steal the creator's location ──────

    [Fact]
    public void PassThroughWhen_PreservesTheInnerElementsCallSite()
    {
        // When/If/Expr return their inner element verbatim, and are themselves
        // intercepted. An unconditional stamp would clone the TextBlock and
        // replace its location with the `When(` line, so GetSource would name the
        // wrapper rather than the factory that created the control.
        var inner = TextBlock("kept"); var innerLine = Line();

        var wrapped = When(true, () => inner);

        Assert.Same(inner, wrapped);
        Assert.Equal(innerLine, wrapped.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void PassThroughWhen_MultilineFormIsAttributedToTheInnerFactory()
    {
        // The shape the reviewer described: the inner factory is on a different
        // physical line from the wrapper, so a stolen stamp is observable as a
        // concrete wrong line rather than a coincidence.
        var wrapperLine = Line();
        var wrapped = When(
            true,
            () => TextBlock("multiline"));

        // The TextBlock call is two lines below the When call.
        Assert.NotEqual(wrapperLine, wrapped.CallSite!.Value.LineNumber);
        Assert.Equal(wrapperLine + 3, wrapped.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void PassThroughWhen_DoesNotClaimAnUnstampedInnerElement()
    {
        // The case the first-stamp-wins guard alone cannot cover: the inner element
        // was built while mapping was OFF, so it carries no location. A guard that
        // only defers to an EXISTING stamp finds none and lets `When` write its own,
        // reporting the wrapper as the creator of something built elsewhere — a
        // confident wrong answer, which is worse than no answer. Pass-throughs are
        // therefore not intercepted at all.
        ReactorSourceMap.Enabled = false;
        var inner = TextBlock("built-while-off");
        ReactorSourceMap.Enabled = true;

        var whenLine = Line();
        var wrapped = When(true, () => inner);

        Assert.Same(inner, wrapped);
        Assert.Null(wrapped.CallSite);
        _ = whenLine;
    }

    [Fact]
    public void PassThroughIfAndExpr_AlsoDoNotClaimAnUnstampedElement()
    {
        // If and Expr are the other two pass-throughs. Covered explicitly so the fix
        // is not silently specific to When.
        ReactorSourceMap.Enabled = false;
        var a = TextBlock("a");
        var b = TextBlock("b");
        ReactorSourceMap.Enabled = true;

        var viaIf = If(true, () => a);
        var viaExpr = Expr(() => b);

        Assert.Same(a, viaIf);
        Assert.Same(b, viaExpr);
        Assert.Null(viaIf.CallSite);
        Assert.Null(viaExpr.CallSite);
    }

    [Fact]
    public void CreatingFactoryStillStampsWhenReEnabled()
    {
        // Positive control for the three tests above. Toggling the flag off and on is
        // the shared setup; if that sequence left interception broken, the nulls above
        // would be vacuous. A factory that really creates an element must still stamp.
        ReactorSourceMap.Enabled = false;
        _ = TextBlock("ignored");
        ReactorSourceMap.Enabled = true;

        var fresh = TextBlock("fresh"); var freshLine = Line();

        Assert.NotNull(fresh.CallSite);
        Assert.Equal(freshLine, fresh.CallSite!.Value.LineNumber);
    }

    // ── The empty sentinel must survive interception untouched ────────────

    [Fact]
    public void EmptyFactory_PreservesTheSingletonIdentity()
    {
        // Empty() returns the shared EmptyElement.Instance, and Mount filters
        // EmptyElement out before it becomes a control - so a stamp here could never
        // be read back. Cloning it via `with` would break reference identity AND
        // allocate a 152-byte extras bucket on every conditional-empty render.
        var a = Empty();
        var b = Empty();

        Assert.Same(a, b);
        Assert.Null(a.CallSite);
    }

    [Fact]
    public void EmptyFactory_AllocatesNoExtrasBucket()
    {
        // The allocation half, stated separately: identity could be preserved while
        // still paying for a bucket, and vice versa.
        var empty = Empty();

        Assert.Null(empty.Extensions);
    }

    [Fact]
    public void ANonEmptyFactoryOnTheSameLinesStillStamps()
    {
        // Positive control: the guard above must be specific to EmptyElement, not a
        // blanket "stop stamping" that would make the two tests above vacuous.
        var el = TextBlock("not-empty"); var line = Line();

        Assert.NotNull(el.CallSite);
        Assert.Equal(line, el.CallSite!.Value.LineNumber);
    }
    [Fact]
    public void NonPassThroughFactory_StillTakesItsOwnCallSite()
    {
        // Positive control for the two tests above: a factory that genuinely
        // CREATES an element must still stamp itself. If the coalescing logic
        // were inverted (or stamped nothing), this would catch it.
        var stack = VStack(TextBlock("child")); var stackLine = Line();

        Assert.Equal(stackLine, stack.CallSite!.Value.LineNumber);
    }

    // ── Helper-method attribution (reported, not aspirational) ────────────

    private static TextBlockElement MyHeader() => TextBlock("header");

    [Fact]
    public void HelperMethod_AttributesToTheHelperNotItsCaller()
    {
        var element = MyHeader(); var callerLine = Line();

        // Interceptors replace the CALL SITE, and the call site of TextBlock is
        // inside MyHeader. So Route B reports the helper's own line — exactly the
        // same limitation CallerInfo has. Asserted rather than assumed.
        Assert.NotEqual(callerLine, element.CallSite!.Value.LineNumber);
        Assert.True(element.CallSite!.Value.LineNumber < callerLine);
    }

    // ── Stamp survives the fluent modifier chain ──────────────────────────

    [Fact]
    public void Source_SurvivesFluentModifiers()
    {
        var element = TextBlock("hi").Margin(8).Bold(); var expected = Line();

        Assert.Equal(expected, element.CallSite!.Value.LineNumber);
    }

    // ── Formatting ────────────────────────────────────────────────────────

    [Fact]
    public void ToShortString_DropsTheDirectory()
    {
        var element = TextBlock("hi");
        var text = element.CallSite!.Value.ToShortString();

        Assert.StartsWith("SourceMapInterceptorTests.cs:", text, StringComparison.Ordinal);
        Assert.DoesNotContain(global::System.IO.Path.DirectorySeparatorChar, text);
    }
}


