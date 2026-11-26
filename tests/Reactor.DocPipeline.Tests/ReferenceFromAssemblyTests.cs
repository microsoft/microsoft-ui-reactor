using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Microsoft.UI.Reactor.Cli.Docs.Tests.ApiFixture;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// <see cref="ApiModel.FromAssembly"/> against real metadata: the types in
/// <c>ApiFixture.cs</c> are compiled into this test assembly, and the reader
/// formats them from the DLL the same way it formats <c>Microsoft.UI.Reactor.dll</c>.
/// </summary>
public sealed class ReferenceFromAssemblyTests
{
    private static readonly Lazy<ApiModel> Model = new(() => ApiModel.FromAssembly(
        Path.Combine(AppContext.BaseDirectory, typeof(FixtureApi).Assembly.GetName().Name + ".dll")));

    private static string DocIdOf<T>() => "T:" + typeof(T).FullName!.Replace('+', '.');

    private static ApiType TypeOf<T>() => Model.Value.TypesByDocId[DocIdOf<T>()];

    private static ApiMember Member<T>(string name) => Assert.Single(TypeOf<T>().Members, m => m.Name == name);

    [Fact]
    public void Declarations_KeepModifiersAndKind()
    {
        Assert.Equal("public class FixtureApi", TypeOf<FixtureApi>().Declaration);
        Assert.Equal("public sealed class SealedFixture", TypeOf<SealedFixture>().Declaration);
        Assert.Equal("public enum FixtureMode", TypeOf<FixtureMode>().Declaration);
    }

    /// <summary>Every kind of default value is written as C# source, and a
    /// <c>float</c> keeps its <c>f</c> suffix and its exact value.</summary>
    [Fact]
    public void MethodSignature_WritesDefaultsAsSource()
    {
        var curve = Member<FixtureApi>(nameof(FixtureApi.Curve));
        Assert.Equal(
            "public static float Curve(float tension = 0.8f, double scale = 1.5, FixtureMode mode = FixtureMode.On, "
            + "string label = \"x\", string? missing = null, CancellationToken token = default)",
            curve.Signature);
        Assert.Equal("Curve(float, double, FixtureMode, string, string?, CancellationToken)", curve.ShortSignature);
        Assert.Equal(
            new[] { "0.8f", "1.5", "FixtureMode.On", "\"x\"", "null", "default" },
            curve.Params.Select(p => p.Default).ToArray());
        Assert.True(curve.IsStatic);
    }

    [Fact]
    public void Constant_IsWrittenWithItsValue()
    {
        Assert.Equal("public const double Ratio = 0.25", Member<FixtureApi>(nameof(FixtureApi.Ratio)).Signature);
    }

    /// <summary>A constructed generic base is a TypeSpecification in metadata. It
    /// still shows in the declaration and links to the generic definition.</summary>
    [Fact]
    public void ConstructedGenericBase_IsDeclaredAndLinked()
    {
        var derived = TypeOf<DerivedFromGenericFixture>();
        Assert.Equal("public class DerivedFromGenericFixture : GenericBaseFixture<string>", derived.Declaration);
        Assert.Equal("T:" + typeof(FixtureApi).Namespace + ".GenericBaseFixture`1", derived.BaseDocId);
    }

    /// <summary><c>default</c> of a type parameter is written as <c>default</c>, not
    /// <c>null</c>: <c>T</c> may be a value type.</summary>
    [Fact]
    public void GenericParameterDefault_IsDefault()
    {
        var generic = Model.Value.TypesByDocId["T:" + typeof(FixtureApi).Namespace + ".GenericBaseFixture`1"];
        var pick = Assert.Single(generic.Members, m => m.Name == "Pick");
        Assert.Equal("public static T Pick(T fallback = default)", pick.Signature);
        Assert.Equal("default", Assert.Single(pick.Params).Default);
    }

    /// <summary><c>notnull</c> is written where the source has it, on a type and on a
    /// method, and not on an unconstrained, <c>class</c> or interface-constrained
    /// parameter, which carry the same metadata flag for a different reason.</summary>
    [Fact]
    public void NotNullConstraint_IsWritten()
    {
        var type = Model.Value.TypesByDocId["T:" + typeof(FixtureApi).Namespace + ".NotNullFixture`1"];
        Assert.Equal("public class NotNullFixture<TRoute> where TRoute : notnull", type.Declaration);
        string Sig(string name) => Assert.Single(type.Members, m => m.Name == name).Signature;
        Assert.Equal("public static TKey Keyed<TKey>(TKey key) where TKey : notnull", Sig("Keyed"));
        Assert.Equal("public static T Free<T>(T value)", Sig("Free"));
        Assert.Equal("public static T Reference<T>(T value) where T : class", Sig("Reference"));
        Assert.Equal("public static T Typed<T>(T value) where T : IComparable", Sig("Typed"));
    }

    /// <summary>A <c>required</c> member keeps its modifier, and the <c>[Obsolete]</c>
    /// Roslyn adds to the constructor for old compilers is not a deprecation. An
    /// <c>[Obsolete]</c> the author wrote still is.</summary>
    [Fact]
    public void RequiredMembers_KeepTheModifierWithoutAFalseDeprecation()
    {
        Assert.Equal("public required string Key  // get, init", Member<RequiredFixture>(nameof(RequiredFixture.Key)).Signature);
        Assert.Equal("public string? Note  // get, init", Member<RequiredFixture>(nameof(RequiredFixture.Note)).Signature);
        Assert.Null(Member<RequiredFixture>(nameof(RequiredFixture)).ObsoleteMessage);

        Assert.Equal("Use the factory.",
            Member<RequiredWithObsoleteCtorFixture>(nameof(RequiredWithObsoleteCtorFixture)).ObsoleteMessage);
    }

    /// <summary>Nullable annotations land on the right type argument, tuple elements
    /// keep their names, and a value type never gets a stray <c>?</c>.</summary>
    [Fact]
    public void NullableAndTupleAnnotations_LandOnTheRightTypes()
    {
        var find = Member<FixtureApi>(nameof(FixtureApi.Find));
        Assert.Equal("public (int Index, string? Name) Find(IReadOnlyList<string?> items, int? start = null)", find.Signature);
        Assert.Equal(new string?[] { null, "null" }, find.Params.Select(p => p.Default).ToArray());

        Assert.Equal("public string? Name  // get, set", Member<FixtureApi>(nameof(FixtureApi.Name)).Signature);
        Assert.Equal("public int? Count  // get, init", Member<FixtureApi>(nameof(FixtureApi.Count)).Signature);
        Assert.Equal("public (int Index, string? Label) Pair  // get", Member<FixtureApi>(nameof(FixtureApi.Pair)).Signature);

        Assert.Equal("(int? Index, string? Label)", Member<FixtureApi>(nameof(FixtureApi.Mixed)).ReturnType);
        Assert.Equal("KeyValuePair<string?, int[]?>", Member<FixtureApi>(nameof(FixtureApi.Entry)).ReturnType);
        Assert.Equal("Dictionary<int, string?>", Member<FixtureApi>(nameof(FixtureApi.Lookup)).ReturnType);
        Assert.Equal("(string? First, int Second)?", Member<FixtureApi>(nameof(FixtureApi.MaybePair)).ReturnType);
    }

    [Fact]
    public void EnumValues_KeepTheirNumbers()
    {
        Assert.Equal(
            new[] { "Off=0", "On=1", "Auto=4" },
            TypeOf<FixtureMode>().EnumValues.Select(v => v.Name + "=" + v.ConstantValue).ToArray());
        Assert.Equal(
            new[] { "None=0", "Left=1", "Right=2", "Both=3" },
            TypeOf<FixtureFlags>().EnumValues.Select(v => v.Name + "=" + v.ConstantValue).ToArray());
    }

    /// <summary>Public and protected members of an inheritable type are documented;
    /// <c>private protected</c>, <c>internal</c>, private and internal types are not.</summary>
    [Fact]
    public void Visibility_MatchesWhatACallerCanSee()
    {
        var names = TypeOf<FixtureApi>().Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("FixtureApi", names);
        Assert.Equal("protected void ForDerived()", Member<FixtureApi>("ForDerived").Signature);
        Assert.Equal("protected void ForDerivedOrAssembly()", Member<FixtureApi>("ForDerivedOrAssembly").Signature);
        Assert.DoesNotContain("PrivateProtected", names);
        Assert.DoesNotContain("Hidden", names);
        Assert.DoesNotContain("_secret", names);

        Assert.Equal(
            new[] { "SealedFixture", "Shown" },
            TypeOf<SealedFixture>().Members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        Assert.False(Model.Value.TypesByDocId.ContainsKey("T:" + typeof(FixtureApi).Namespace + ".InternalFixture"));
    }
}
