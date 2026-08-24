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
}
