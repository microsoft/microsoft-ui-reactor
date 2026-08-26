using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §7.1.2 — cref → display signature. Overload headings are the
/// only thing distinguishing sections on a merged reference page, so the
/// rendering has to survive generics, arrays, nested generic arguments and
/// by-ref parameters without collapsing two overloads onto one heading.
/// </summary>
public class CrefSignatureTests
{
    private static MemberDoc M(string cref, params string[] paramNames) => new(
        Cref: cref,
        Kind: MemberKind.Method,
        Summary: string.Empty,
        Params: paramNames.Select(n => new ParamDoc(n, string.Empty)).ToList(),
        Returns: string.Empty,
        Remarks: string.Empty,
        Examples: Array.Empty<string>(),
        Caveats: Array.Empty<string>(),
        SeeAlsos: Array.Empty<string>());

    [Theory]
    // Non-generic, params array.
    [InlineData("M:Ns.C.UseEffect(System.Action,System.Object[])", "UseEffect(Action, object[])")]
    // Nested generic argument.
    [InlineData("M:Ns.C.UseEffect(System.Func{System.Action},System.Object[])", "UseEffect(Func<Action>, object[])")]
    // Method type parameters, undocumented → positional placeholders.
    [InlineData("M:Ns.C.UseEffect``2(System.Action,``0,``1)", "UseEffect<T1, T2>(Action, T1, T2)")]
    [InlineData("M:Ns.C.UseEffect``3(System.Func{System.Action},``0,``1,``2)", "UseEffect<T1, T2, T3>(Func<Action>, T1, T2, T3)")]
    // Primitive aliases.
    [InlineData("M:Ns.C.SetValue(System.Int32,System.String,System.Boolean)", "SetValue(int, string, bool)")]
    // Nullable shorthand.
    [InlineData("M:Ns.C.Wait(System.Nullable{System.TimeSpan})", "Wait(TimeSpan?)")]
    // By-ref.
    [InlineData("M:Ns.C.TryGet(System.String,System.Int32@)", "TryGet(string, ref int)")]
    // Multi-dimensional array rank.
    [InlineData("M:Ns.C.Fill(System.Int32[0:,0:])", "Fill(int[,])")]
    // No parameter list at all.
    [InlineData("M:Ns.C.Clear", "Clear()")]
    // Property and type crefs render bare.
    [InlineData("P:Ns.C.IsEnabled", "IsEnabled")]
    [InlineData("T:Ns.Options`2", "Options<T1, T2>")]
    public void Format_RendersReadableSignature(string cref, string expected) =>
        Assert.Equal(expected, CrefSignature.Format(M(cref)));

    [Fact]
    public void Format_UsesDocumentedTypeParameterNames()
    {
        var member = M("M:Ns.C.UseMemo``1(System.Func{``0},System.Object[])") with
        {
            TypeParams = new[] { new ParamDoc("TValue", "The memoised value.") },
        };
        Assert.Equal("UseMemo<TValue>(Func<TValue>, object[])", CrefSignature.Format(member));
    }

    /// <summary>
    /// Signatures are headings, and their slugs are linkable anchors, so
    /// they must depend only on the API — not on whether someone got round
    /// to writing a <c>&lt;param&gt;</c> doc. Names belong in the body.
    /// </summary>
    [Fact]
    public void Format_IgnoresParameterNames_SoAnchorsSurviveDocEdits()
    {
        var cref = "M:Ns.C.SetValue(System.Int32,System.String)";
        var undocumented = CrefSignature.Format(M(cref));
        var documented = CrefSignature.Format(M(cref, "value", "label"));

        Assert.Equal("SetValue(int, string)", undocumented);
        Assert.Equal(undocumented, documented);
    }

    [Fact]
    public void Format_UsesDeclaringTypeParameterNames_ForClassGenerics()
    {
        var member = M("M:Ns.Options`2.Apply(`0,`1)");
        Assert.Equal("Apply(TInput, TResult)",
            CrefSignature.Format(member, new[] { "TInput", "TResult" }));
    }

    [Fact]
    public void Format_ConstructorUsesDeclaringTypeName()
    {
        var member = M("M:Ns.Options`2.#ctor(System.String)");
        Assert.Equal("Options(string)", CrefSignature.Format(member));
    }

    [Fact]
    public void Parse_SplitsDeclaringTypeAndName()
    {
        var p = CrefSignature.Parse("M:Microsoft.UI.Reactor.Core.RenderContext.UseEffect``2(System.Action,``0,``1)");
        Assert.Equal("M", p.Kind);
        Assert.Equal("Microsoft.UI.Reactor.Core.RenderContext", p.DeclaringName);
        Assert.Equal("UseEffect", p.Name);
        Assert.Equal(2, p.GenericArity);
        Assert.Equal(3, p.ParameterTypes.Count);
        Assert.True(p.HasParameterList);
    }

    [Fact]
    public void Parse_KeepsCommasInsideGenericArgumentsTogether()
    {
        var p = CrefSignature.Parse("M:Ns.C.Run(System.Func{System.Int32,System.String},System.Int32)");
        Assert.Equal(2, p.ParameterTypes.Count);
        Assert.Equal("System.Func{System.Int32,System.String}", p.ParameterTypes[0]);
    }

    [Fact]
    public void Parse_DropsConversionOperatorReturnType()
    {
        var p = CrefSignature.Parse("M:Ns.C.op_Implicit(Ns.C)~System.String");
        Assert.Equal("op_Implicit", p.Name);
        Assert.Single(p.ParameterTypes);
    }

    [Theory]
    // Anchors follow GitHub's slug rules: lowercase, punctuation dropped,
    // spaces to hyphens.
    [InlineData("UseEffect(Action, object[])", "useeffectaction-object")]
    [InlineData("UseEffect<T1, T2>(Func<Action>, T1, T2)", "useeffectt1-t2funcaction-t1-t2")]
    [InlineData("Clear()", "clear")]
    public void Anchor_MatchesGitHubSlugRules(string heading, string expected) =>
        Assert.Equal(expected, CrefSignature.Anchor(heading));

    [Fact]
    public void Anchor_DistinguishesOverloadsThatDifferOnlyByParameterType()
    {
        var a = CrefSignature.Anchor(CrefSignature.Format(M("M:Ns.C.UseEffect(System.Action,System.Object[])")));
        var b = CrefSignature.Anchor(CrefSignature.Format(M("M:Ns.C.UseEffect(System.Func{System.Action},System.Object[])")));
        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// &lt;typeparam&gt; tags are in XML comment order and omit undocumented
    /// parameters, so the list is not indexed by declaration ordinal. A partial
    /// set must not be read positionally — documenting only the second
    /// parameter would otherwise label ``0 with the second one's name.
    /// </summary>
    [Fact]
    public void Format_UsesPlaceholders_WhenTypeParamDocsAreIncomplete()
    {
        var partial = M("M:Ns.C.UseMemoCells``2(System.Object)") with
        {
            TypeParams = new List<ParamDoc> { new("TValue", string.Empty) },
        };

        var formatted = CrefSignature.Format(partial);

        Assert.Contains("<T1, T2>", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("TValue", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A complete set is <b>not</b> sufficient: XML preserves authoring order,
    /// not declaration order, so a method declared <c>&lt;TKey, TValue&gt;</c>
    /// whose <c>TValue</c> tag happens to be written first would render
    /// <c>Method&lt;TValue, TKey&gt;</c> — a complete set of real names in the
    /// wrong slots, which then propagates into parameter types and the anchor.
    /// Nothing in the cref carries declaration ordinals, so the order cannot be
    /// recovered; placeholders are used from arity 2 upward.
    /// </summary>
    [Fact]
    public void Format_UsesPlaceholders_ForMultipleTypeParams_BecauseOrderIsUnprovable()
    {
        var complete = M("M:Ns.C.UseMemoCells``2(System.Object)") with
        {
            TypeParams = new List<ParamDoc> { new("TKey", string.Empty), new("TValue", string.Empty) },
        };

        var formatted = CrefSignature.Format(complete);

        Assert.Contains("<T1, T2>", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("TKey", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("TValue", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// The swapped-order case the rule exists for. It must render identically to
    /// the in-order one — if these two ever differ, order is leaking through.
    /// </summary>
    [Fact]
    public void Format_IsOrderInsensitive_WhenTypeParamTagsAreAuthoredOutOfOrder()
    {
        var inOrder = M("M:Ns.C.UseMemoCells``2(System.Object)") with
        {
            TypeParams = new List<ParamDoc> { new("TKey", string.Empty), new("TValue", string.Empty) },
        };
        var swapped = M("M:Ns.C.UseMemoCells``2(System.Object)") with
        {
            TypeParams = new List<ParamDoc> { new("TValue", string.Empty), new("TKey", string.Empty) },
        };

        Assert.Equal(CrefSignature.Format(inOrder), CrefSignature.Format(swapped));
    }

    /// <summary>
    /// At arity 1 there is nothing to order, so the documented name is safe and
    /// is used — the rule gives up only where it genuinely cannot know.
    /// </summary>
    [Fact]
    public void Format_UsesTheDocumentedName_ForASingleTypeParam()
    {
        var one = M("M:Ns.C.UseResource``1(System.Object)") with
        {
            TypeParams = new List<ParamDoc> { new("TResult", string.Empty) },
        };

        Assert.Contains("<TResult>", CrefSignature.Format(one), StringComparison.Ordinal);
    }
}
