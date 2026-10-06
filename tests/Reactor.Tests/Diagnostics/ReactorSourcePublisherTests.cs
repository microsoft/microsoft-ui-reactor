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
// Mutates ReactorSourcePublisher statics (IsEnabled / NoManagedAgent).
[Collection("ReactorSourcePublisherGlobals")]
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

    [Theory]
    [InlineData("plain")]
    [InlineData("keyed")]
    [InlineData("escaped")]
    [InlineData("nosite")]
    public void WithRoot_AddsTheSameFieldsFormatWouldForARoot(string shape)
    {
        // A Memo(key, …) root: its output published itself; the host adds root= (and hooks=)
        // without re-running the factory. The result must be what Format gives a root.
        var (element, _) = ShapeOf(shape);
        var plain = ReactorSourcePublisher.Format(element, "Ro|ot");
        var expected = ReactorSourcePublisher.Format(element, "Ro|ot", component: null, root: "Ro|ot", rootHooks: "0:n@3;1:x%y@4");

        Assert.Equal(expected, ReactorSourcePublisher.WithRoot(plain, "Ro|ot", "0:n@3;1:x%y@4"));
        // Idempotent: a re-render that republishes the root does not stack fields.
        Assert.Equal(expected, ReactorSourcePublisher.WithRoot(expected, "Ro|ot", "0:n@3;1:x%y@4"));
        Assert.Null(ReactorSourcePublisher.WithRoot(null, "Root", null));
    }
    [Fact]
    public void Format_UnkeyedSameShape_SharesOneString()
    {
        var a = ReactorSourcePublisher.Format(TextBlock("one") with { CallSite = new SourceLocation("Share.cs", 1, 1) }, "Card");
        var b = ReactorSourcePublisher.Format(TextBlock("two") with { CallSite = new SourceLocation("Share.cs", 1, 1) }, "Card");

        Assert.Same(a, b);
        Assert.NotSame(a, ReactorSourcePublisher.Format(TextBlock("one") with { CallSite = new SourceLocation("Share.cs", 1, 1) }, "Other"));
    }

    // ── Native AOT: the published value stands in for the call-site-only tag ─────

    [Theory]
    [InlineData("v=1|at=App.cs:3:4|owner=A|element=TextBlock", "at=App.cs:3:4")]
    [InlineData("v=1|at=App.cs:3:4|rel=root|owner=A|element=TextBlock", "at=App.cs:3:4|rel=root")]
    [InlineData("v=1|at=File.cs:9|rel=0", "at=File.cs:9|rel=0")]
    [InlineData("v=1|at=A%7CB.cs:1:1|element=TextBlock", "at=A%7CB.cs:1:1")]
    [InlineData("v=1|at=/_/src/X.cs:5:2", "at=/_/src/X.cs:5:2")]
    [InlineData("v=1|owner=A|element=Button", null)]
    public void AtKey_IsTheAtFieldWithItsRelMarker(string value, string? expected)
        => Assert.Equal(expected, ReactorSourcePublisher.AtKey(value));

    [Fact]
    public void PublishedValue_ResolvesToTheFullCallSite()
    {
        // The same SourceLocation GetSource reads off a tag: absolute path, line, column.
        var full = new SourceLocation(@"C:\outside\Probe\Resolve.cs", 41, 7);
        var keyed = ReactorSourcePublisher.Format(TextBlock("x") with { CallSite = full, Key = "row|3" }, "Card");
        var plain = ReactorSourcePublisher.Format(Button("y") with { CallSite = full }, null);

        Assert.DoesNotContain(@"C:\outside", keyed, StringComparison.Ordinal);
        Assert.Equal(full, ReactorSourcePublisher.ResolvePublishedValue(keyed));
        Assert.Equal(full, ReactorSourcePublisher.ResolvePublishedValue(plain));
        Assert.Equal(full.ColumnNumber, ReactorSourcePublisher.ResolvePublishedValue(plain)!.Value.ColumnNumber);
        Assert.Null(ReactorSourcePublisher.ResolvePublishedValue("v=1|at=Never.cs:1:1|element=TextBlock"));
        Assert.Null(ReactorSourcePublisher.ResolvePublishedValue(ReactorSourcePublisher.Format(TextBlock("z"), "Card")));
    }

    [Fact]
    public void SameAtTextFromTwoCallSites_IsAmbiguous_FirstSiteStillResolves()
    {
        // Two files with the same name outside every known root publish the same at= text.
        var first = new SourceLocation(@"C:\one\Ambig\Same.cs", 5, 3);
        var second = new SourceLocation(@"D:\two\Ambig\Same.cs", 5, 3);
        var a = ReactorSourcePublisher.Format(TextBlock("a") with { CallSite = first }, "Card");
        Assert.False(ReactorSourcePublisher.IsAmbiguous(a));
        Assert.Equal(first, ReactorSourcePublisher.ResolvePublishedValue(a));

        var b = ReactorSourcePublisher.Format(TextBlock("b") with { CallSite = second }, "Other");
        Assert.Equal(ReactorSourcePublisher.AtKey(a), ReactorSourcePublisher.AtKey(b));
        // From now on controls with this text keep their tag (the reconciler checks this)...
        Assert.True(ReactorSourcePublisher.IsAmbiguous(a));
        Assert.True(ReactorSourcePublisher.IsAmbiguous(b));
        // ...so an untagged one was published before the collision, for the first site:
        // that mapping must survive the collision.
        Assert.Equal(first, ReactorSourcePublisher.ResolvePublishedValue(a));

        // Re-publishing either site changes nothing.
        ReactorSourcePublisher.Format(TextBlock("c") with { CallSite = first }, "Third");
        ReactorSourcePublisher.Format(TextBlock("d") with { CallSite = second }, "Fourth");
        Assert.Equal(first, ReactorSourcePublisher.ResolvePublishedValue(a));
        Assert.True(ReactorSourcePublisher.IsAmbiguous(b));
    }

    [Fact]
    public void SkipsCallSiteOnlyTags_NeedsPublishingAndNoManagedAgent()
    {
        var (enabled, noAgent) = (ReactorSourcePublisher.IsEnabled, ReactorSourcePublisher.NoManagedAgent);
        try
        {
            // This test host is JIT: a managed agent could load, so the default keeps tags.
            Assert.False(ReactorSourcePublisher.NoManagedAgent);
            foreach (var (on, aot, expected) in new[] { (false, false, false), (true, false, false), (false, true, false), (true, true, true) })
            {
                ReactorSourcePublisher.IsEnabled = on;
                ReactorSourcePublisher.NoManagedAgent = aot;
                Assert.Equal(expected, ReactorSourcePublisher.SkipsCallSiteOnlyTags);
            }
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = enabled;
            ReactorSourcePublisher.NoManagedAgent = noAgent;
        }
    }
}
