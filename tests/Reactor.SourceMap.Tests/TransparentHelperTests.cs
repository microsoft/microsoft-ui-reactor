using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hooks;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — <c>[ReactorSourceTransparent]</c> helper attribution (mechanism 1).
///
/// <para>An interceptor replaces a CALL SITE, so a helper that forwards to a factory is
/// attributed to its own body and every one of its call sites collapses onto one line.
/// The attribute flips that: the generator emits no interceptor for factory calls inside
/// an annotated method (rule 1), and instead intercepts calls TO it (rule 2). Annotated
/// calling annotated keeps deferring outward (rule 3).</para>
///
/// <para><b>The controls matter more than usual here</b>, because both halves of this
/// feature can fail silently in opposite directions. If rule 2 stopped working, elements
/// would go from "the helper's line" to "no line" — so every positive test is paired
/// against <see cref="PlainField"/>, an identically-shaped helper WITHOUT the attribute,
/// which must keep reporting its own body line. And if the attribute had accidentally
/// become a blanket rule for helpers, that control reddens.</para>
///
/// <para>Shares the "SourceMap" collection because
/// <see cref="ReactorSourceMap.Enabled"/> is process-global mutable state.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class TransparentHelperTests : IDisposable
{
    public TransparentHelperTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    // ── Helpers under test ────────────────────────────────────────────────
    //
    // `internal`, not `private`: the emitted interceptor lives in a generated file and
    // has to be able to name the method it forwards to. A private helper is unreachable
    // from there, which is what PrivateField below is for.

    [ReactorSourceTransparent]
    internal static Element TransparentField(string label) => HStack(TextBlock(label));

    /// <summary>The control. Identical body, no attribute.</summary>
    private static Element PlainField(string label) => HStack(TextBlock(label));

    [ReactorSourceTransparent]
    internal static Element TransparentInner(string label) => TextBlock(label);

    [ReactorSourceTransparent]
    internal static Element TransparentOuter(string label) => TransparentInner(label);

    /// <summary>
    /// A conditional helper declared to return <c>Element?</c> — the shape that exposed a
    /// latent bug in the shared "does this return an Element" test. See
    /// <see cref="NullableElementReturningHelper_IsStillDeferredToTheCaller"/>.
    /// </summary>
    [ReactorSourceTransparent]
    internal static Element? TransparentMaybeField(bool show, string label)
        => show ? TextBlock(label) : null;

    /// <summary>Returns whatever it is handed, so first-stamp-wins can be observed.</summary>
    [ReactorSourceTransparent]
    internal static Element TransparentPassThrough(Element given) => given;

    /// <summary>Builds its element inside a lambda, to check the rule-1 walk crosses one.</summary>
    [ReactorSourceTransparent]
    internal static Element TransparentViaLambda(string label)
    {
        global::System.Func<Element> build = () => TextBlock(label);
        return build();
    }

    /// <summary>
    /// A generic transparent helper, explicitly instantiated at <c>Element</c> so its
    /// constructed parameter is an <c>Element</c> a <c>string</c> converts to.
    /// </summary>
    [ReactorSourceTransparent]
    internal static Element TransparentWrap<T>(T value) => (Element)(object)value!;

    [Fact]
    public void GenericHelperInstantiatedAtElement_IsStampedAtTheCallerAndStillCompiles()
    {
        // A PR reviewer read the argument-stamp filter's use of the OPEN definition as a
        // missed case: `TransparentWrap<Element>("converted")` genuinely does have a
        // constructed `Element` parameter and a genuine user-defined conversion. Switching
        // the filter to the constructed method was measured, and it makes the consumer's
        // build fail — the emitted interceptor declares the parameter as `T __a0`, so
        // writing an `Element?` into it is CS1503. Declining to stamp the argument is what
        // keeps generated code compiling.
        //
        // The fact that this test COMPILES AT ALL is therefore half its value: this file is
        // compiled with interception on, so a generator that stamped here would break the
        // build rather than fail an assertion. The other half is that the element still
        // gets a location — from rule 2's return-path stamp — so the guard costs nothing
        // an author can observe here.
        var element = TransparentWrap<Element>("converted"); var callerLine = Line();

        Assert.Equal(callerLine, element.CallSite!.Value.LineNumber);
    }

    // Deliberately unusable: a private method cannot be named from a generated file, so
    // the generator reports REACTOR_SOURCEMAP_001 and leaves attribution exactly as it
    // would be with no attribute at all. Suppressed locally because this file is compiled
    // in Release too, where warnings are errors — and suppressibility is itself part of
    // the contract being tested.
#pragma warning disable REACTOR_SOURCEMAP_001
    [ReactorSourceTransparent]
    private static Element PrivateField(string label) => HStack(TextBlock(label));
#pragma warning restore REACTOR_SOURCEMAP_001

    // ── Rule 2: the caller's line ─────────────────────────────────────────

    [Fact]
    public void TransparentHelper_IsAttributedToItsCaller()
    {
        var element = TransparentField("Name"); var callerLine = Line();

        Assert.Equal(callerLine, element.CallSite!.Value.LineNumber);
        Assert.EndsWith("TransparentHelperTests.cs", element.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void UnannotatedHelper_StillReportsItsOwnBodyLine()
    {
        // The control for every test in this file, in the same file and the same run.
        // Without it, a generator that had stopped emitting anything at all would make
        // "the transparent helper reports the caller's line" unfalsifiable.
        var element = PlainField("Name"); var callerLine = Line();

        Assert.NotEqual(callerLine, element.CallSite!.Value.LineNumber);
        Assert.True(element.CallSite!.Value.LineNumber < callerLine);
    }

    [Fact]
    public void TwoCallSitesOfOneTransparentHelper_DoNotCollapse()
    {
        // The payoff. Two calls, two lines.
        var first = TransparentField("Name"); var firstLine = Line();
        var second = TransparentField("Email"); var secondLine = Line();

        Assert.NotEqual(firstLine, secondLine);
        Assert.Equal(firstLine, first.CallSite!.Value.LineNumber);
        Assert.Equal(secondLine, second.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void TwoCallSitesOfTheUnannotatedHelper_DoCollapse()
    {
        // The differential control for the test above: same two-call shape, attribute
        // removed. These two MUST land on the same line, which is exactly the problem the
        // attribute exists to solve. Together the pair proves the attribute is what moves
        // attribution — not the file, the flag, or the factory.
        var first = PlainField("Name");
        var second = PlainField("Email");

        Assert.Equal(first.CallSite!.Value.LineNumber, second.CallSite!.Value.LineNumber);
    }

    // ── Rule 3: recursion ─────────────────────────────────────────────────

    [Fact]
    public void NestedTransparentHelpers_DeferToTheOutermostNonTransparentCaller()
    {
        // TransparentOuter calls TransparentInner calls TextBlock. Every link is either
        // annotated or inside something annotated, so the walk keeps going outward until
        // it reaches this test method, which is not annotated.
        var element = TransparentOuter("Nested"); var callerLine = Line();

        Assert.Equal(callerLine, element.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void InnerTransparentHelper_IsAlsoTransparentWhenCalledDirectly()
    {
        // Proves the nested result above came from deferral at each level rather than
        // TransparentInner happening to be unreachable: called directly, it also reports
        // its caller.
        var element = TransparentInner("Direct"); var callerLine = Line();

        Assert.Equal(callerLine, element.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void FactoryCallInsideALambdaInATransparentHelper_IsAlsoDeferred()
    {
        // Rule 1 walks the enclosing SYMBOL chain, so a factory call nested inside a
        // lambda still counts as "inside" the transparent method. A syntax-only walk that
        // stopped at the lambda would stamp the helper's body line here.
        var element = TransparentViaLambda("Lambda"); var callerLine = Line();

        Assert.Equal(callerLine, element.CallSite!.Value.LineNumber);
    }

    // ── The Element? return shape ─────────────────────────────────────────

    [Fact]
    public void NullableElementReturningHelper_IsStillDeferredToTheCaller()
    {
        // Regression test for a latent bug in the shared Element-returning check. It
        // compared rendered type names, and the default display format renders
        // nullability — so a method declared to return exactly `Element?` rendered as
        // "…Element?", failed the comparison, and (because Element's base is object) the
        // base-type walk went straight past the answer. Such a helper was silently
        // skipped: not intercepted, and not suppressed either, so it reported its own body
        // line. A conditional helper returning Element? is an entirely natural shape, so
        // this had to be fixed before the attribute could be trusted.
        var element = TransparentMaybeField(true, "Maybe"); var callerLine = Line();

        Assert.NotNull(element);
        Assert.Equal(callerLine, element!.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void NullableElementReturningHelper_StillReturnsNullWhenItShould()
    {
        // Behaviour preservation: the interceptor forwards, it does not change what the
        // helper decides. Also exercises the emitted null guard for a nullable return.
        Assert.Null(TransparentMaybeField(false, "Maybe"));
    }

    /// <summary>
    /// A transparent helper whose body uses a bare-string child, so rule 1's reach over
    /// argument-position stamping is observable.
    /// </summary>
    [ReactorSourceTransparent]
    internal static Element TransparentWithConvertedChild(string label) => VStack(label);

    [Fact]
    public void RuleOne_AlsoSuppressesArgumentStampsInsideATransparentHelper()
    {
        // Rule 1 returns before argument analysis runs, so a converted child written inside
        // an annotated helper gets no location at all — not the helper's line, and not the
        // caller's. That follows from the model rather than being an oversight: the element
        // belongs to whoever called the helper, and stamping the helper's own line is
        // exactly what the attribute exists to stop. The RESULT still defers to the caller.
        //
        // Pinned because it is the one place the two mechanisms interact, and because
        // silence here is otherwise indistinguishable from argument stamping being broken.
        var stack = TransparentWithConvertedChild("child"); var callerLine = Line();

        Assert.Equal(callerLine, stack.CallSite!.Value.LineNumber);

        var child = global::System.Linq.Enumerable.Single(((StackElement)stack).Children);
        Assert.Null(child.CallSite);
    }

    [Fact]
    public void ConvertedChildOutsideATransparentHelper_IsStamped_ControlForRuleOneReach()
    {
        // The control that makes the null above mean something: the identical bare-string
        // child, written at an ordinary call site in the same file, IS stamped. Without
        // this, a generator that had stopped emitting argument stamps entirely would make
        // the test above pass for the wrong reason.
        var stack = VStack("child"); var line = Line();

        var child = global::System.Linq.Enumerable.Single(stack.Children);
        Assert.Equal(line, child.CallSite!.Value.LineNumber);
    }

    // ── Precedence and gating ─────────────────────────────────────────────

    [Fact]
    public void TransparentHelper_DoesNotRelabelAnElementItWasGiven()
    {
        // First stamp wins. The element was created on its own line and merely passed
        // through, so the creating line is the right answer and the helper's call site is
        // not. The same rule that keeps pass-through factories honest.
        var given = TextBlock("created here"); var creationLine = Line();

        var returned = TransparentPassThrough(given);

        Assert.Same(given, returned);
        Assert.Equal(creationLine, returned.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void PrivateAnnotatedHelper_BehavesExactlyLikeAnUnannotatedOne()
    {
        // An annotation the generator cannot honour must never make attribution WORSE
        // than no annotation. Rule 1 is therefore conditioned on the enclosing method
        // being forwardable, not merely annotated: suppressing the body stamp with no
        // rule-2 interceptor to replace it would trade "the helper's line" for nothing at
        // all. The generator warns (REACTOR_SOURCEMAP_001, suppressed at the declaration
        // above) instead of failing silently.
        var element = PrivateField("Name"); var callerLine = Line();

        Assert.NotNull(element.CallSite);
        Assert.NotEqual(callerLine, element.CallSite!.Value.LineNumber);
        Assert.True(element.CallSite!.Value.LineNumber < callerLine);
    }

    [Fact]
    public void FlagOff_LeavesTransparentHelperResultsUnstamped()
    {
        ReactorSourceMap.Enabled = false;
        try
        {
            Assert.Null(TransparentField("off").CallSite);
        }
        finally
        {
            ReactorSourceMap.Enabled = true;
        }

        // Positive control: same probe, flag on.
        var control = TransparentField("on"); var controlLine = Line();
        Assert.Equal(controlLine, control.CallSite!.Value.LineNumber);
    }

    // ── The metadata path: an annotation in a REFERENCED assembly ─────────

    [Fact]
    public void TransparentAnnotationInAReferencedAssembly_IsHonoured()
    {
        // `PendingFactory.Pending` is annotated inside Reactor itself, which this suite
        // consumes as a compiled reference. That is the shipping shape — a consumer
        // calling an annotated framework method — and it is the only case here that
        // proves the generator reads the attribute from METADATA rather than from source
        // in its own compilation. It also requires the attribute type to be public;
        // an internal one would be invisible across the boundary and every in-compilation
        // test here would still pass.
        //
        // Before this annotation, Pending's element carried no location at all: it builds
        // itself by calling Factories.Component<,> from inside Reactor's assembly, where
        // there is no consumer call site to intercept.
        var element = PendingFactory.Pending(TextBlock("fallback"), TextBlock("child")); var callerLine = Line();

        // The LINE, not merely non-null. A null check alone would also pass if the inner
        // Component<,> call had stamped it — which is precisely what rule 1 prevents, and
        // which would point into Reactor's own source rather than at this line.
        Assert.Equal(callerLine, element.CallSite!.Value.LineNumber);
        Assert.EndsWith("TransparentHelperTests.cs", element.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }
}
