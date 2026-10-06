using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// <see cref="ReactorSourcePublisher.IdentityChanged"/> decides whether an in-place update
/// re-publishes <c>ReactorSource</c>. It must say "unchanged" for an ordinary re-render (so
/// the update path builds nothing) and "changed" for every input of the published value.
/// </summary>
public sealed class ReactorSourcePublisherTests
{
    private static readonly SourceLocation SiteA = new("App.cs", 10, 5);
    private static readonly SourceLocation SiteB = new("App.cs", 11, 5);

    private sealed class Alpha : Component
    {
        public override Element Render() => TextBlock("a");
    }

    private sealed class Beta : Component
    {
        public override Element Render() => TextBlock("b");
    }

    [Fact]
    public void ReRenderWithNewPropsButSameSiteAndKey_IsUnchanged()
    {
        var before = TextBlock("count 1") with { CallSite = SiteA, Key = "k" };
        var after = TextBlock("count 2") with { CallSite = new SourceLocation("App.cs", 10, 5), Key = "k" };

        Assert.False(ReactorSourcePublisher.IdentityChanged(before, after));
    }

    [Fact]
    public void NewCallSite_IsChanged()
    {
        Assert.True(ReactorSourcePublisher.IdentityChanged(
            TextBlock("x") with { CallSite = SiteA },
            TextBlock("x") with { CallSite = SiteB }));
        Assert.True(ReactorSourcePublisher.IdentityChanged(
            TextBlock("x"),
            TextBlock("x") with { CallSite = SiteA }));
    }

    [Fact]
    public void NewKey_IsChanged()
    {
        Assert.True(ReactorSourcePublisher.IdentityChanged(
            TextBlock("x") with { CallSite = SiteA, Key = "a" },
            TextBlock("x") with { CallSite = SiteA, Key = "b" }));
    }

    [Fact]
    public void NewKind_IsChanged()
    {
        Assert.True(ReactorSourcePublisher.IdentityChanged(
            TextBlock("x") with { CallSite = SiteA },
            Button("x") with { CallSite = SiteA }));
    }

    [Fact]
    public void NewComponentType_IsChanged()
    {
        Assert.False(ReactorSourcePublisher.IdentityChanged(
            Component<Alpha>() with { CallSite = SiteA },
            Component<Alpha>() with { CallSite = SiteA }));
        Assert.True(ReactorSourcePublisher.IdentityChanged(
            Component<Alpha>() with { CallSite = SiteA },
            Component<Beta>() with { CallSite = SiteA }));
    }

    [Fact]
    public void ModifiedElement_ComparesTheInnerElement()
    {
        var modifiers = new ElementModifiers();
        Assert.False(ReactorSourcePublisher.IdentityChanged(
            new ModifiedElement(TextBlock("1") with { CallSite = SiteA }, modifiers),
            new ModifiedElement(TextBlock("2") with { CallSite = SiteA }, modifiers)));
        Assert.True(ReactorSourcePublisher.IdentityChanged(
            new ModifiedElement(TextBlock("1") with { CallSite = SiteA }, modifiers),
            new ModifiedElement(TextBlock("1") with { CallSite = SiteB }, modifiers)));
    }

    // ── Format (cached per shape) is the v1 grammar, byte for byte ──────────

    /// <summary>The uncached formula the cache replaced.</summary>
    private static string Direct(Element element, string? owner, string? mounts = null, string? root = null, string? hooks = null)
    {
        var site = element.CallSite;
        string? rel = null;
        SourceLocation? published = site is { } full && !string.IsNullOrEmpty(full.FilePath)
            ? new SourceLocation(
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath(full.FilePath, out rel),
                full.LineNumber, full.ColumnNumber)
            : site;
        return ReactorSourceFormat.Build(
            published, owner, ReactorSourceFormat.KindOf(element.GetType()), mounts, root,
            ReactorSourceFormat.KeyText(element.Key), site?.DeclaredName, hooks, rel);
    }

    public static TheoryData<string> Shapes() => new() { "plain", "keyed", "escaped", "nosite", "noowner", "column0", "absolute" };

    private static (Element Element, string? Owner) ShapeOf(string shape) => shape switch
    {
        "plain" => (TextBlock("x") with { CallSite = SiteA }, "Card"),
        "keyed" => (TextBlock("x") with { CallSite = SiteA, Key = "row-7" }, "Card"),
        "escaped" => (TextBlock("x") with { CallSite = new SourceLocation("A|B%.cs", 3, 0), Key = "k|1%" }, "Own|er"),
        "nosite" => (Button("x"), "Card"),
        "noowner" => (TextBlock("x") with { CallSite = SiteB }, null),
        "column0" => (TextBlock("x") with { CallSite = new SourceLocation("App.cs", 9, 0) }, "Card"),
        "absolute" => (TextBlock("x") with { CallSite = new SourceLocation(@"C:\elsewhere\Deep\File.cs", 4, 2) }, "Card"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Format_MatchesTheDirectBuild(string shape)
    {
        var (element, owner) = ShapeOf(shape);
        var expected = Direct(element, owner);

        Assert.Equal(expected, ReactorSourcePublisher.Format(element, owner));
        // Second call is served from the cache and must not drift.
        Assert.Equal(expected, ReactorSourcePublisher.Format(element, owner));
    }

    [Fact]
    public void Format_RootAndRootHooks_MatchTheDirectBuild()
    {
        var element = TextBlock("x") with { CallSite = SiteA, Key = "r" };
        Assert.Equal(
            Direct(element, "App", root: "App", hooks: "0:count@3;1:name@4"),
            ReactorSourcePublisher.Format(element, "App", component: null, root: "App", rootHooks: "0:count@3;1:name@4"));
    }

    [Fact]
    public void Format_UnkeyedSameShape_SharesOneString()
    {
        var a = ReactorSourcePublisher.Format(TextBlock("one") with { CallSite = new SourceLocation("Share.cs", 1, 1) }, "Card");
        var b = ReactorSourcePublisher.Format(TextBlock("two") with { CallSite = new SourceLocation("Share.cs", 1, 1) }, "Card");

        Assert.Same(a, b);
        Assert.NotSame(a, ReactorSourcePublisher.Format(TextBlock("one") with { CallSite = new SourceLocation("Share.cs", 1, 1) }, "Other"));
    }
}
