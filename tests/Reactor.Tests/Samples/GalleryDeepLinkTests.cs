using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WinUIGalleryReactor;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Samples;

/// <summary>
/// Contract tests for ReactorGallery's <c>reactor-gallery://</c> URI space
/// (samples/ReactorGallery/DeepLink/GalleryRoutes.cs, source-linked into this suite).
///
/// <para>Two things are being protected. First the *routing* — every pattern, the
/// reverse URI builder, and the round trip between them, so a renamed control or a
/// dropped pattern breaks the build rather than a user's bookmark. Second the *input
/// validation* — the resolver is handed URIs by the Windows shell, so the scheme check
/// and the registry allow-list are the boundary that stops an arbitrary string from
/// reaching the shell as a NavigationView tag.</para>
///
/// <para>Assertions are written differentially (a passing case paired with the failing
/// case that differs by exactly one thing) so deleting the code under test flips a
/// result rather than merely widening one.</para>
/// </summary>
public sealed class GalleryDeepLinkTests
{
    const string Prefix = "reactor-gallery:///";

    /// <summary>
    /// A real control tag, taken from the registry rather than hard-coded, for the
    /// cases where *which* control is irrelevant. Deleting or renaming a gallery
    /// sample should not fail a routing test that has nothing to do with it — the
    /// documented links are pinned separately, in
    /// <see cref="DocumentedExampleLinks_AllResolve"/>, where a failure means
    /// "README.md is now wrong".
    /// </summary>
    static readonly string AnyControlTag = ControlRegistry.All[0].Tag;

    static readonly string AnyCategorySlug = GalleryRoutes.CategorySlug(ControlRegistry.Categories[0]);

    // ── Patterns ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("reactor-gallery:///")]
    [InlineData("reactor-gallery:///home")]
    [InlineData("reactor-gallery:///HOME")]
    public void Resolve_HomeLinks_LandOnHome(string uri)
    {
        Assert.True(GalleryRoutes.TryResolve(uri, out var route), uri);
        Assert.Equal(GalleryRouteKind.Home, route.Kind);
        Assert.Equal("home", route.Tag);
    }

    [Fact]
    public void Resolve_SettingsLink_LandsOnSettingsTag()
    {
        Assert.True(GalleryRoutes.TryResolve(Prefix + "settings", out var route));
        Assert.Equal(GalleryRouteKind.Settings, route.Kind);
        Assert.Equal("settings", route.Tag);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("control")]
    public void Resolve_ControlLink_YieldsControlRouteUnderBothSegmentNames(string segment)
    {
        Assert.True(GalleryRoutes.TryResolve($"{Prefix}{segment}/{AnyControlTag}", out var route), segment);
        Assert.Equal(GalleryRouteKind.Control, route.Kind);
        Assert.Equal(AnyControlTag, route.Tag);
    }

    [Fact]
    public void Resolve_CategoryLink_YieldsCategoryRoute()
    {
        Assert.True(GalleryRoutes.TryResolve(Prefix + "category/" + AnyCategorySlug, out var route));
        Assert.Equal(GalleryRouteKind.Category, route.Kind);
        Assert.Equal(AnyCategorySlug, route.Tag);
    }

    [Fact]
    public void Resolve_SearchLink_CarriesTheDecodedQuery()
    {
        Assert.True(GalleryRoutes.TryResolve(Prefix + "search?q=toggle%20switch", out var route));
        Assert.Equal(GalleryRouteKind.Search, route.Kind);
        // The query — not merely "some search route" — is the payload the shell renders.
        Assert.Equal("toggle switch", route.Query);

        // A search link with no query is still a search route, with an empty query.
        // This is what separates "the ?q= parse ran" from "the parse echoed the URI".
        Assert.True(GalleryRoutes.TryResolve(Prefix + "search", out var bare));
        Assert.Equal(GalleryRouteKind.Search, bare.Kind);
        Assert.Equal(string.Empty, bare.Query);
    }

    [Fact]
    public void Resolve_TrailingSlashAndCasing_DoNotChangeTheRoute()
    {
        Assert.True(GalleryRoutes.TryResolve($"{Prefix}item/{AnyControlTag}/", out var trailing));
        Assert.True(GalleryRoutes.TryResolve($"{Prefix}Item/{AnyControlTag.ToUpperInvariant()}", out var cased));

        var expected = new GalleryRoute(GalleryRouteKind.Control, AnyControlTag);
        Assert.Equal(expected, trailing);
        Assert.Equal(expected, cased);
    }

    [Fact]
    public void Resolve_FoldsAuthorityBackIntoPath()
    {
        // `reactor-gallery://item/button` (two slashes) parses with Host="item" and
        // AbsolutePath="/button". Without the authority fold that is a miss, so this
        // pairing fails the moment the fold is removed.
        Assert.True(GalleryRoutes.TryResolve($"reactor-gallery://item/{AnyControlTag}", out var twoSlash));
        Assert.True(GalleryRoutes.TryResolve($"reactor-gallery:///item/{AnyControlTag}", out var threeSlash));
        Assert.Equal(threeSlash, twoSlash);
        Assert.Equal(AnyControlTag, twoSlash.Tag);
    }

    [Fact]
    public void Resolve_PercentEncodedCategoryName_MatchesTheHyphenatedSlug()
    {
        Assert.True(GalleryRoutes.TryResolve(Prefix + "category/Date%20and%20Time", out var encoded));
        Assert.True(GalleryRoutes.TryResolve(Prefix + "category/date-and-time", out var slug));
        Assert.Equal(slug, encoded);
        Assert.Equal("date-and-time", encoded.Tag);
    }

    // ── Validation boundary ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_UnknownControlTag_IsRejectedWhileAKnownOneResolves()
    {
        // Differential: identical shape, only the tag differs. Removing the
        // ControlRegistry allow-list check makes the second assert fail.
        Assert.True(GalleryRoutes.TryResolve($"{Prefix}item/{AnyControlTag}", out _));
        Assert.False(GalleryRoutes.TryResolve(Prefix + "item/not-a-real-control", out var rejected));
        Assert.Equal(GalleryRoutes.HomeRoute, rejected);
    }

    [Fact]
    public void Resolve_UnknownCategory_IsRejectedWhileAKnownOneResolves()
    {
        Assert.True(GalleryRoutes.TryResolve($"{Prefix}category/{AnyCategorySlug}", out _));
        Assert.False(GalleryRoutes.TryResolve(Prefix + "category/not-a-real-category", out _));
    }

    [Fact]
    public void Resolve_ControlSegmentCannotSmuggleAReservedTag()
    {
        // `settings` and `home` are real NavigationView tags but not controls, so they
        // must not be reachable through /item/. This is the concrete reason the
        // allow-list exists.
        Assert.False(GalleryRoutes.TryResolve(Prefix + "item/settings", out _));
        Assert.False(GalleryRoutes.TryResolve(Prefix + "item/home", out _));
    }

    [Theory]
    [InlineData("https://evil.example/item/button")]
    [InlineData("http://evil.example/item/button")]
    [InlineData("file:///item/button")]
    [InlineData("javascript:/item/button")]
    public void Resolve_ForeignScheme_IsRejected(string uri)
    {
        // Same path, wrong scheme. The reactor-gallery spelling below does resolve, so
        // a deleted scheme check turns these into passes and fails the test.
        Assert.False(GalleryRoutes.TryResolve(uri, out _), uri);
        Assert.True(GalleryRoutes.TryResolve(Prefix + "item/button", out _));
    }

    [Fact]
    public void Resolve_QueryValueContainingAColon_Survives()
    {
        // Guards against "reject anything with a colon" creeping back into the relative
        // arm as a scheme check: the scheme is already handled above, and a colon inside
        // a query value is ordinary data.
        Assert.True(GalleryRoutes.TryResolve(Prefix + "search?q=time%3Anow", out var route));
        Assert.Equal(GalleryRouteKind.Search, route.Kind);
        Assert.Equal("time:now", route.Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("reactor-gallery:///nope/button")]
    [InlineData("reactor-gallery:///item")]
    [InlineData("reactor-gallery:///item/button/extra")]
    public void Resolve_UnmatchedInput_ReturnsFalseAndHome(string? uri)
    {
        Assert.False(GalleryRoutes.TryResolve(uri, out var route), uri ?? "<null>");
        Assert.Equal(GalleryRoutes.HomeRoute, route);
        Assert.Equal(GalleryRoutes.HomeRoute, GalleryRoutes.ResolveOrHome(uri));
    }

    [Fact]
    public void Resolve_BarePathWithoutScheme_IsAccepted()
    {
        // The command-line fallback hands through raw argv entries, which may be a bare
        // path. Both spellings must land on the same route.
        Assert.True(GalleryRoutes.TryResolve($"/item/{AnyControlTag}", out var rooted));
        Assert.True(GalleryRoutes.TryResolve($"item/{AnyControlTag}", out var relative));
        Assert.Equal(rooted, relative);
        Assert.Equal(GalleryRouteKind.Control, rooted.Kind);
    }

    [Fact]
    public void ResolveOrHome_ReturnsTheMatchedRoute_NotAlwaysHome()
    {
        // Without this the helper could be replaced by `=> HomeRoute` and every other
        // ResolveOrHome assertion (all miss cases) would still pass.
        Assert.Equal(
            new GalleryRoute(GalleryRouteKind.Settings, "settings"),
            GalleryRoutes.ResolveOrHome(Prefix + "settings"));

        Assert.Equal(
            new GalleryRoute(GalleryRouteKind.Control, AnyControlTag),
            GalleryRoutes.ResolveOrHome($"{Prefix}item/{AnyControlTag}"));

        Assert.Equal(GalleryRoutes.HomeRoute, GalleryRoutes.ResolveOrHome(Prefix + "item/nope"));
    }

    // ── Reverse direction + round trip ──────────────────────────────────────

    [Fact]
    public void UriForTag_RoundTripsEveryRegisteredControl()
    {
        Assert.NotEmpty(ControlRegistry.All);

        foreach (var control in ControlRegistry.All)
        {
            var uri = GalleryRoutes.UriForTag(control.Tag);
            Assert.True(GalleryRoutes.TryResolve(uri, out var route), $"{control.Tag} → {uri}");
            Assert.Equal(GalleryRouteKind.Control, route.Kind);
            Assert.Equal(control.Tag, route.Tag);
        }
    }

    [Fact]
    public void UriForTag_RoundTripsEveryCategory()
    {
        Assert.NotEmpty(ControlRegistry.Categories);

        foreach (var category in ControlRegistry.Categories)
        {
            var slug = GalleryRoutes.CategorySlug(category);
            var uri = GalleryRoutes.UriForTag(slug);
            Assert.True(GalleryRoutes.TryResolve(uri, out var route), $"{category} → {uri}");
            Assert.Equal(GalleryRouteKind.Category, route.Kind);
            Assert.Equal(slug, route.Tag);
        }
    }

    /// <summary>
    /// The shell builds each nav group from <c>ControlRegistry.Categories</c> and then filters
    /// <c>All</c> by category, so a control whose category is missing from that list (a typo,
    /// or a new category never added to it) silently drops out of the navigation tree while
    /// search and deep links still reach it.
    /// </summary>
    [Fact]
    public void EveryControlCategory_IsListedInCategories()
    {
        Assert.NotEmpty(ControlRegistry.All);
        Assert.All(ControlRegistry.All, c => Assert.Contains(c.Category, ControlRegistry.Categories));
    }

    [Fact]
    public void CategorySlug_IsLowercaseAndHyphenated()
    {
        // The slug is simultaneously the NavigationView tag and the /category/ segment;
        // GalleryShell builds its tags through this same method. A slug that kept its
        // spaces or casing would still "work" inside the shell but break every category
        // link, so pin the exact shape.
        Assert.Equal("date-and-time", GalleryRoutes.CategorySlug("Date and Time"));
        Assert.Equal("basic-input", GalleryRoutes.CategorySlug("Basic Input"));
        // Surrounding whitespace is trimmed, not folded into leading/trailing hyphens.
        Assert.Equal("basic-input", GalleryRoutes.CategorySlug("  Basic Input  "));

        foreach (var slug in ControlRegistry.Categories.Select(GalleryRoutes.CategorySlug))
        {
            Assert.DoesNotContain(" ", slug, StringComparison.Ordinal);
            Assert.Equal(slug.ToLowerInvariant(), slug);
        }
    }

    [Fact]
    public void Resolve_TrimsSurroundingWhitespace()
    {
        // Argv entries and clipboard round-trips routinely carry stray whitespace,
        // inside the segment as well as around the whole URI.
        Assert.True(GalleryRoutes.TryResolve($"  {Prefix}item/{AnyControlTag}  ", out var padded));
        Assert.Equal(AnyControlTag, padded.Tag);

        Assert.True(GalleryRoutes.TryResolve($"{Prefix}item/%20{AnyControlTag}%20", out var paddedSegment));
        Assert.Equal(AnyControlTag, paddedSegment.Tag);
    }

    [Fact]
    public void UriForTag_EmitsTheExpectedShapePerTagKind()
    {
        Assert.Equal(Prefix + "home", GalleryRoutes.UriForTag("home"));
        Assert.Equal(Prefix + "settings", GalleryRoutes.UriForTag("settings"));
        Assert.Equal(Prefix + "item/" + AnyControlTag, GalleryRoutes.UriForTag(AnyControlTag));
        Assert.Equal(Prefix + "category/" + AnyCategorySlug, GalleryRoutes.UriForTag(AnyCategorySlug));

        // Unknown / empty degrade to home rather than emitting a dead link.
        Assert.Equal(Prefix + "home", GalleryRoutes.UriForTag("not-a-real-tag"));
        Assert.Equal(Prefix + "home", GalleryRoutes.UriForTag(null));
        Assert.Equal(Prefix + "home", GalleryRoutes.UriForTag("   "));
    }

    [Fact]
    public void UriForSearch_EscapesTheQueryAndRoundTrips()
    {
        var uri = GalleryRoutes.UriForSearch("toggle switch & more");
        // Raw spaces / ampersands in a query string truncate the term on the way back
        // in, so the escaping is load-bearing rather than cosmetic.
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
        Assert.True(GalleryRoutes.TryResolve(uri, out var route));
        Assert.Equal(GalleryRouteKind.Search, route.Kind);
        Assert.Equal("toggle switch & more", route.Query);

        Assert.Equal(Prefix + "home", GalleryRoutes.UriForSearch("   "));
    }

    [Fact]
    public void UriForCurrentView_PrefersTheLiveSearchQueryOverTheSelectedTag()
    {
        // The shell shows search results whenever the box is non-empty, regardless of
        // the selected tag — the copied link has to match what's on screen.
        Assert.Equal(
            GalleryRoutes.UriForSearch("button"),
            GalleryRoutes.UriForCurrentView(AnyCategorySlug, "button"));

        Assert.Equal(
            GalleryRoutes.UriForTag(AnyCategorySlug),
            GalleryRoutes.UriForCurrentView(AnyCategorySlug, ""));

        // Whitespace is "no search", not a search for spaces: the shell doesn't show
        // results for it, so the link must still describe the selected tag. Weakening
        // the guard to IsNullOrEmpty would silently emit a home link here.
        Assert.Equal(
            GalleryRoutes.UriForTag(AnyCategorySlug),
            GalleryRoutes.UriForCurrentView(AnyCategorySlug, "   "));

        Assert.Equal(
            GalleryRoutes.UriForTag(AnyCategorySlug),
            GalleryRoutes.UriForCurrentView(AnyCategorySlug, null));

        Assert.NotEqual(
            GalleryRoutes.UriForCurrentView(AnyCategorySlug, ""),
            GalleryRoutes.UriForCurrentView(AnyCategorySlug, "button"));
    }

    [Fact]
    public void DocumentedExampleLinks_AllResolve()
    {
        // The exact links printed in samples/ReactorGallery/README.md and shown on the
        // Settings page. A failure here means the docs now advertise a dead link —
        // which is also why these, unlike the tests above, hard-code real tags.
        (string Uri, GalleryRouteKind Kind, string Tag)[] documented =
        [
            ("reactor-gallery:///", GalleryRouteKind.Home, "home"),
            ("reactor-gallery:///home", GalleryRouteKind.Home, "home"),
            ("reactor-gallery:///settings", GalleryRouteKind.Settings, "settings"),
            ("reactor-gallery:///search?q=toggle", GalleryRouteKind.Search, "home"),
            ("reactor-gallery:///category/basic-input", GalleryRouteKind.Category, "basic-input"),
            ("reactor-gallery:///item/button", GalleryRouteKind.Control, "button"),
        ];

        foreach (var (uri, kind, tag) in documented)
        {
            Assert.True(GalleryRoutes.TryResolve(uri, out var route), $"README documents {uri}, which no longer resolves");
            Assert.Equal(kind, route.Kind);
            Assert.Equal(tag, route.Tag);
        }

        // The Settings page renders this one, so it must round-trip too.
        Assert.Equal("reactor-gallery:///item/button", GalleryRoutes.UriForTag("button"));
    }

    [Fact]
    public void Resolve_NeverThrows_OnHostileInput()
    {
        // TryResolve is the boundary for strings the Windows shell hands the process, and
        // callers treat `false` as "show Home" — a throw would turn a malformed link into
        // a crash on launch. Each of these is either malformed percent-encoding, a lone
        // surrogate, a control character, or pathologically long.
        string[] hostile =
        [
            "reactor-gallery:///item/%ZZ",
            "reactor-gallery:///item/%",
            "reactor-gallery:///category/%E0%A4%A",
            "reactor-gallery:///search?q=%GG",
            "reactor-gallery:///item/\uD800",
            "reactor-gallery:///item/\0\u0001\u001f",
            "reactor-gallery:///item/" + new string('a', 200_000),
            "reactor-gallery:///" + new string('/', 5_000),
            "reactor-gallery:///search?" + string.Join("&", Enumerable.Repeat("q=x", 2_000)),
            "reactor-gallery://///////item/button",
            "\uFEFFreactor-gallery:///item/button",
        ];

        foreach (var input in hostile)
        {
            var resolved = GalleryRoutes.TryResolve(input, out var route);
            // Whatever the verdict, it must be a *verdict* — and a miss must hand back
            // Home rather than a half-built route.
            if (!resolved) Assert.Equal(GalleryRoutes.HomeRoute, route);
            Assert.Equal(resolved ? route : GalleryRoutes.HomeRoute, GalleryRoutes.ResolveOrHome(input));
        }

        // Differential: the same shape without the mangling still resolves, so the loop
        // above is not passing merely because everything returns false.
        Assert.True(GalleryRoutes.TryResolve($"{Prefix}item/{AnyControlTag}", out _));
    }

    [Fact]
    public void UriBuilders_NeverThrow_OnHostileInput()
    {
        // These take the shell's own selected tag and search text, but both originate in
        // resolved links, so keep them total too.
        foreach (var input in new[] { "%ZZ", "\uD800", new string('a', 100_000), "\0" })
        {
            Assert.StartsWith(GalleryRoutes.UriPrefix, GalleryRoutes.UriForTag(input), StringComparison.Ordinal);
            Assert.StartsWith(GalleryRoutes.UriPrefix, GalleryRoutes.UriForSearch(input), StringComparison.Ordinal);
            Assert.StartsWith(GalleryRoutes.UriPrefix, GalleryRoutes.UriForCurrentView(input, input), StringComparison.Ordinal);
            Assert.Equal(GalleryRoutes.CategorySlug(input), GalleryRoutes.CategorySlug(input));
        }
    }

    [Fact]
    public void Scheme_MatchesThePackageManifestDeclaration()
    {
        // The packaged flavour declares the scheme in Package.appxmanifest and the
        // unpackaged one registers this same constant at runtime; if they drift, links
        // open the wrong app (or nothing) depending on how the gallery was installed.
        // Read the manifest for real — asserting the constant against itself would leave
        // a manifest-only rename green.
        var manifestPath = Path.Join(RepoRoot(), "samples", "ReactorGallery", "Package.appxmanifest");
        Assert.True(File.Exists(manifestPath), manifestPath);

        var manifest = XDocument.Load(manifestPath);
        XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
        var declared = manifest.Descendants(uap + "Protocol")
            .Select(p => (string?)p.Attribute("Name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();

        Assert.Equal(new[] { GalleryRoutes.Scheme }, declared);
        Assert.Equal("reactor-gallery:///", GalleryRoutes.UriPrefix);
        Assert.StartsWith(GalleryRoutes.UriPrefix, GalleryRoutes.UriForTag(AnyControlTag), StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repo root (Reactor.slnx) from " + AppContext.BaseDirectory);
    }
}
