// Unit coverage for `mur pack-local --framework-version latest` version
// resolution (Pack/PackLocalCommand.cs). bootstrap.ps1 relies on this to stamp
// the newest *published* Microsoft.UI.Reactor into the scaffolded template, so
// the local `dotnet new reactorapp` default tracks releases instead of drifting
// behind them (the class of bug behind issue #866).
//
// The network fetch itself is not unit-tested (it hits nuget.org); these tests
// cover the pure, load-bearing pieces around it: parsing the flat-container
// index and picking the highest SemVer. The ordering test is the important one —
// a naive string sort ranks "preview.5" above "preview.10"/"preview.11", which
// would make the feature stamp an *older* version than the one it's meant to
// track.

using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public sealed class PackLocalFrameworkVersionTests
{
    [Fact]
    public void SelectLatestVersion_orders_prerelease_numerically_not_lexically()
    {
        // The exact published shape at the time of writing. A string sort would
        // pick "0.1.0-preview.5" (because '5' > '1'); the correct answer is
        // preview.11.
        var published = new[]
        {
            "0.1.0-preview.1",
            "0.1.0-preview.3",
            "0.1.0-preview.4",
            "0.1.0-preview.5",
            "0.1.0-preview.10",
            "0.1.0-preview.11",
        };

        var latest = PackLocalCommand.SelectLatestVersion(published);

        Assert.Equal("0.1.0-preview.11", latest);
        // Guard the specific failure mode this logic exists to prevent.
        Assert.NotEqual("0.1.0-preview.5", latest);
    }

    [Fact]
    public void SelectLatestVersion_is_order_independent()
    {
        var shuffled = new[]
        {
            "0.1.0-preview.10",
            "0.1.0-preview.1",
            "0.1.0-preview.11",
            "0.1.0-preview.4",
        };

        Assert.Equal("0.1.0-preview.11", PackLocalCommand.SelectLatestVersion(shuffled));
    }

    [Fact]
    public void SelectLatestVersion_prefers_release_over_prerelease_of_same_core()
    {
        // SemVer: 1.0.0 outranks 1.0.0-preview.99.
        var versions = new[] { "1.0.0-preview.99", "1.0.0" };
        Assert.Equal("1.0.0", PackLocalCommand.SelectLatestVersion(versions));
    }

    [Fact]
    public void SelectLatestVersion_orders_by_core_before_prerelease_state()
    {
        // A higher core wins even when it is a prerelease and the lower core is
        // stable — 2.0.0-preview.1 > 1.9.9.
        var versions = new[] { "1.9.9", "2.0.0-preview.1" };
        Assert.Equal("2.0.0-preview.1", PackLocalCommand.SelectLatestVersion(versions));
    }

    [Fact]
    public void SelectLatestVersion_compares_major_minor_patch_numerically()
    {
        var versions = new[] { "0.2.0", "0.10.0", "0.9.0" };
        Assert.Equal("0.10.0", PackLocalCommand.SelectLatestVersion(versions));
    }

    [Fact]
    public void SelectLatestVersion_ignores_unparseable_entries()
    {
        var versions = new[] { "not-a-version", "0.1.0-preview.4", "", "  ", "1.x" };
        Assert.Equal("0.1.0-preview.4", PackLocalCommand.SelectLatestVersion(versions));
    }

    [Fact]
    public void SelectLatestVersion_returns_null_when_nothing_parses()
    {
        Assert.Null(PackLocalCommand.SelectLatestVersion(new[] { "junk", "1", "1.2" }));
        Assert.Null(PackLocalCommand.SelectLatestVersion(global::System.Array.Empty<string>()));
    }

    [Fact]
    public void ParseFlatContainerVersions_extracts_versions_array()
    {
        // Shape of https://api.nuget.org/v3-flatcontainer/<id>/index.json
        const string json = """
        { "versions": ["0.1.0-preview.4", "0.1.0-preview.11"] }
        """;

        var versions = PackLocalCommand.ParseFlatContainerVersions(json);

        Assert.Equal(new[] { "0.1.0-preview.4", "0.1.0-preview.11" }, versions);
    }

    [Fact]
    public void ParseFlatContainerVersions_returns_empty_for_wrong_shape_or_malformed()
    {
        Assert.Empty(PackLocalCommand.ParseFlatContainerVersions("""{ "data": [] }"""));
        Assert.Empty(PackLocalCommand.ParseFlatContainerVersions("not json at all"));
        Assert.Empty(PackLocalCommand.ParseFlatContainerVersions("[]"));
    }

    [Fact]
    public void ParseFlatContainerVersions_end_to_end_picks_latest()
    {
        const string json = """
        { "versions": ["0.1.0-preview.5", "0.1.0-preview.10", "0.1.0-preview.11", "0.1.0-preview.4"] }
        """;

        var latest = PackLocalCommand.SelectLatestVersion(
            PackLocalCommand.ParseFlatContainerVersions(json));

        Assert.Equal("0.1.0-preview.11", latest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveTemplateFrameworkVersion_returns_null_for_no_flag(string? arg)
    {
        // No --framework-version → leave the csproj default in place (no network).
        Assert.Null(PackLocalCommand.ResolveTemplateFrameworkVersion(arg));
    }

    [Fact]
    public void ResolveTemplateFrameworkVersion_passes_explicit_version_through()
    {
        // An explicit version is used verbatim and must not trigger a NuGet lookup.
        Assert.Equal("0.1.0-preview.7",
            PackLocalCommand.ResolveTemplateFrameworkVersion("0.1.0-preview.7"));
        Assert.Equal("0.1.0-preview.7",
            PackLocalCommand.ResolveTemplateFrameworkVersion("  0.1.0-preview.7  "));
    }
}
