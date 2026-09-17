using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — argument-position stamping (mechanism 2).
///
/// <para>An argument that reaches an <c>Element</c> parameter through an implicit
/// <em>user-defined</em> conversion is built by the conversion operator, whose body is
/// framework (or library) code. That call site is unreachable on purpose: per the
/// interceptors specification, "interception can only occur for calls to ordinary member
/// methods — not constructors, delegates, properties, local functions, <em>operators</em>",
/// and the operator's body is already compiled into the referenced assembly. The
/// enclosing factory call is intercepted, though, so its interceptor stamps the converted
/// argument on the way past — using the argument expression's own line, not the
/// container's.</para>
///
/// <para><b>Oracles.</b> Every line assertion here is checked against
/// <see cref="At(string, out int, int)"/>, which captures <c>[CallerLineNumber]</c> at the
/// argument's own physical line. That is an independent measurement rather than an offset
/// from the container's line, so a mechanism that silently collapsed every argument onto
/// the <c>VStack(</c> line could not pass.</para>
///
/// <para>Shares the "SourceMap" collection because
/// <see cref="ReactorSourceMap.Enabled"/> is process-global mutable state.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class ImplicitConversionArgumentTests : IDisposable
{
    public ImplicitConversionArgumentTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    /// <summary>
    /// Passes <paramref name="value"/> through unchanged while reporting the line it was
    /// written on. Lets a per-argument oracle live INSIDE an argument list, where a bare
    /// <c>Line()</c> statement cannot go — so a multi-argument call can be checked
    /// argument by argument without hard-coding offsets.
    /// </summary>
    private static string At(string value, out int line, [CallerLineNumber] int captured = 0)
    {
        line = captured;
        return value;
    }

    /// <summary>
    /// The same idea as <see cref="At(string, out int, int)"/> for a non-string
    /// conversion source, so the "any user-defined operator" test gets a real
    /// per-argument oracle instead of an offset from a nearby line.
    /// </summary>
    private static RawBadge Raw(string text, out int line, [CallerLineNumber] int captured = 0)
    {
        line = captured;
        return new RawBadge(text);
    }

    // ── params, expanded form ─────────────────────────────────────────────

    [Fact]
    public void BareStringChild_TakesTheStringsOwnLine()
    {
        var stack = VStack(At("only", out var stringLine));

        var child = global::System.Linq.Enumerable.Single(stack.Children);

        Assert.Equal(stringLine, child.CallSite!.Value.LineNumber);
        Assert.EndsWith(
            "ImplicitConversionArgumentTests.cs", child.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void EachConvertedArgument_TakesItsOwnLine()
    {
        // The whole point of stamping per ARGUMENT rather than per call: two children of
        // one container, written on different lines, must not collapse together.
        var stack = VStack(
            At("first", out var firstLine),
            At("second", out var secondLine));

        var children = global::System.Linq.Enumerable.ToList(stack.Children);

        // Guards the oracle itself: if At() ever reported the call's line instead of the
        // argument's, both expectations would be the same number and the two assertions
        // below would agree no matter what the product did.
        Assert.NotEqual(firstLine, secondLine);

        Assert.Equal(firstLine, children[0].CallSite!.Value.LineNumber);
        Assert.Equal(secondLine, children[1].CallSite!.Value.LineNumber);
    }

    [Fact]
    public void ConvertedArgument_DoesNotTakeTheContainersLine()
    {
        // Stated separately from the test above, because "each argument differs from the
        // other" and "neither is the container's line" are independent failures: an
        // implementation that stamped argument i with the container line + i would pass
        // the first and fail this one.
        var containerLine = Line();
        var stack = VStack(
            At("below the container", out var argumentLine));

        var child = global::System.Linq.Enumerable.Single(stack.Children);

        Assert.NotEqual(containerLine, argumentLine);
        Assert.Equal(argumentLine, child.CallSite!.Value.LineNumber);
        Assert.NotEqual(containerLine, child.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void MixedChildren_StampConvertedOnesWithoutDisturbingExplicitOnes()
    {
        var stack = VStack(
            TextBlock(At("explicit", out var explicitLine)), // its own interceptor stamps this
            At("converted", out var convertedLine));

        var children = global::System.Linq.Enumerable.ToList(stack.Children);

        Assert.NotEqual(explicitLine, convertedLine);
        Assert.Equal(explicitLine, children[0].CallSite!.Value.LineNumber);
        Assert.Equal(convertedLine, children[1].CallSite!.Value.LineNumber);
    }

    [Fact]
    public void NullChildAmongConvertedOnes_IsStillFilteredOut()
    {
        // Behaviour preservation. The interceptor writes into the compiler-built params
        // array before forwarding it, so a bug there would show up as a changed child
        // count or a reordering rather than as a wrong line.
        var stack = VStack(At("a", out var aLine), null, At("b", out var bLine));

        var children = global::System.Linq.Enumerable.ToList(stack.Children);

        Assert.Equal(2, children.Count);
        Assert.Equal(aLine, children[0].CallSite!.Value.LineNumber);
        Assert.Equal(bLine, children[1].CallSite!.Value.LineNumber);
    }

    // ── params, normal form: the caller's array must not be touched ───────

    [Fact]
    public void ParamsInNormalForm_LeavesTheCallersArrayAlone()
    {
        // `VStack(array)` passes an array the CALLER owns. Its elements were converted
        // where the array was built, not at this call, so there is nothing here to
        // attribute — and writing into it would be a visible side effect on someone
        // else's object. The generator emits no stamping for this shape at all.
        var array = new Element?[] { "built elsewhere" };
        var before = array[0];

        var stack = VStack(array);

        Assert.Same(before, array[0]);
        Assert.Null(array[0]!.CallSite);
        Assert.Same(before, global::System.Linq.Enumerable.Single(stack.Children));
    }

    [Fact]
    public void ExplicitlyWrittenArrayArgument_IsAlsoLeftAlone()
    {
        // The same protection as above, but with the array written INLINE at the call
        // site, where the conversions really are lexically part of this call. It is still
        // normal form — an `Element?[]` argument binding to an `Element?[]` parameter — so
        // the compiler never synthesizes a params array and the generator must not treat
        // the one it can see as its own to write into. Distinct from the test above in
        // what it can catch: a generator that keyed off "the argument is an array
        // creation" instead of "the compiler built this array" would pass that test and
        // fail this one.
        var stack = VStack(new Element?[] { "inline", "array" });

        var children = global::System.Linq.Enumerable.ToList(stack.Children);

        Assert.Equal(2, children.Count);
        Assert.Null(children[0].CallSite);
        Assert.Null(children[1].CallSite);
    }

    [Fact]
    public void ExpandedFormOnTheSameLinesStillStamps_PositiveControlForNormalForm()
    {
        // Positive control for the nulls above: same file, same flag, same factory. Without
        // it, a generator that had silently stopped emitting argument stamps entirely would
        // make ParamsInNormalForm_LeavesTheCallersArrayAlone pass for the wrong reason.
        var stack = VStack(At("expanded", out var argumentLine));

        Assert.Equal(argumentLine, global::System.Linq.Enumerable.Single(stack.Children).CallSite!.Value.LineNumber);
    }

    // ── Ordinary (non-params) Element parameters ──────────────────────────

    [Fact]
    public void NullableElementParameter_IsStamped()
    {
        var border = Border(At("in a border", out var argumentLine));

        Assert.Equal(argumentLine, border.Child!.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void NonNullableElementParameter_IsStamped()
    {
        // Distinct from the nullable case in the EMITTED code: writing an `Element?` back
        // into a non-nullable `Element` parameter needs the null-forgiving operator, and
        // getting that wrong is a Release build break (warnings are errors there) rather
        // than a test failure. Covered so the shape is exercised at all.
        var scroller = ScrollViewer(At("in a scroll viewer", out var argumentLine));

        Assert.Equal(argumentLine, scroller.Child.CallSite!.Value.LineNumber);
    }

    // ── Precedence: first stamp wins ──────────────────────────────────────

    [Fact]
    public void AlreadyStampedArgument_KeepsItsOriginalLocation()
    {
        // An element created on one line and passed to a container on another must keep
        // the line that CREATED it. Argument stamping is a fallback for elements nothing
        // else could reach, never a relabelling pass.
        var child = TextBlock("created here"); var creationLine = Line();

        var stack = VStack(child);

        Assert.Same(child, global::System.Linq.Enumerable.Single(stack.Children));
        Assert.Equal(creationLine, child.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void OperatorThatReturnsAStampedElement_KeepsTheOperatorsLine()
    {
        // StampedBadge's operator builds its element with a Factories call, and that call
        // site is in THIS assembly, so it is intercepted and stamped with the operator's
        // own line. First-stamp-wins then leaves the argument path with nothing to do.
        //
        // Differential oracle: run the SAME conversion twice, from two different lines —
        // once outside any argument position, once inside one. Both must report the
        // operator's line, so they must agree with each other. If the argument path had
        // relabelled the second one it would report the `Border(` line instead, and these
        // two would differ. No literal line number is written down, so the test cannot be
        // repaired by editing a magic number.
        Element converted = new StampedBadge("outside");

        var border = Border(new StampedBadge("inside"));

        Assert.Equal(converted.CallSite!.Value.LineNumber, border.Child!.CallSite!.Value.LineNumber);

        // Guards the oracle: the two conversions really are on different source lines, so
        // "they agree" is a claim about the operator winning rather than a tautology.
        Assert.NotEqual(Line(), border.Child!.CallSite!.Value.LineNumber);
        Assert.NotEqual(Line(), converted.CallSite!.Value.LineNumber);
    }

    // ── Not string-specific ───────────────────────────────────────────────

    [Fact]
    public void AnyUserDefinedConversionToElement_IsStamped()
    {
        // The conversion set is open: any consumer type may declare
        // `implicit operator Element`. RawBadge's operator constructs its element
        // directly, so nothing else can have stamped it, and the argument path is the
        // only thing that could produce a location here.
        var border = Border(Raw("raw", out var badgeLine));

        Assert.Equal(badgeLine, border.Child!.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void ConversionThatYieldsTheEmptySingleton_IsNotCloned()
    {
        // EmptyElement.Instance is shared process-wide. Stamping it would hand a DIFFERENT
        // instance to code that compares by reference, and would materialize an extras
        // bucket on a sentinel whose location can never be read back (Mount filters it out
        // before it becomes a control). Reachable from consumer code precisely because the
        // conversion set is open, which is what makes the guard worth having.
        var border = Border(new BlankBadge());

        Assert.Same(Empty(), border.Child);
        Assert.Null(border.Child!.CallSite);
        Assert.Null(border.Child!.Extensions);
    }

    // ── The runtime flag gates argument stamping too ──────────────────────

    [Fact]
    public void FlagOff_LeavesConvertedArgumentsUnstamped()
    {
        ReactorSourceMap.Enabled = false;
        try
        {
            var stack = VStack(At("while off", out _));
            Assert.Null(global::System.Linq.Enumerable.Single(stack.Children).CallSite);
        }
        finally
        {
            ReactorSourceMap.Enabled = true;
        }

        // Positive control: the same probe with the flag on DOES stamp, so the null above
        // cannot be the generator having emitted nothing for this file.
        var control = VStack(At("while on", out var onLine));
        Assert.Equal(onLine, global::System.Linq.Enumerable.Single(control.Children).CallSite!.Value.LineNumber);
    }
}

/// <summary>
/// A conversion whose operator builds its element with a <c>Factories</c> call, so the
/// element arrives at the argument position ALREADY stamped with the operator's line.
/// </summary>
internal sealed class StampedBadge
{
    public StampedBadge(string text) => Text = text;

    public string Text { get; }

    public static implicit operator Element(StampedBadge badge) => Factories.TextBlock(badge.Text);
}

/// <summary>
/// A conversion whose operator constructs its element directly. A constructor is never
/// intercepted, so the result reaches the argument position with no location and the
/// argument path is the only thing that can supply one.
/// </summary>
internal sealed class RawBadge
{
    public RawBadge(string text) => Text = text;

    public string Text { get; }

    public static implicit operator Element(RawBadge badge) => new TextBlockElement(badge.Text);
}

/// <summary>
/// A conversion that yields the shared empty sentinel, which the argument path must hand
/// through untouched rather than clone.
/// </summary>
internal sealed class BlankBadge
{
    public static implicit operator Element(BlankBadge _) => Factories.Empty();
}
