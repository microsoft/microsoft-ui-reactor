using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 Phase 2.0: manifest parser must accept the new <c>kind</c> field on
/// screenshot entries so the catalog-thumb capture path can co-exist with the
/// default full-size screenshot path. Tests run against ad-hoc YAML written to
/// a temp file so the assertions remain hermetic.
/// </summary>
public class ManifestParserTests
{
    [Fact]
    public void Default_kind_is_screenshot()
    {
        var yaml = """
            app:
              title: Sample
              width: 800
              height: 600
            screenshots:
              - id: hello
                description: Hello.
            """;

        var manifest = ParseInline(yaml);

        var entry = Assert.Single(manifest.Screenshots);
        Assert.Equal("screenshot", entry.Kind);
        Assert.Equal(320, entry.ThumbWidth);
        Assert.Equal(240, entry.ThumbHeight);
    }

    [Fact]
    public void Catalog_thumb_kind_round_trips_with_defaults()
    {
        var yaml = """
            app:
              title: Catalog
            screenshots:
              - id: forms-thumb
                kind: catalog-thumb
                description: Forms group thumbnail.
            """;

        var manifest = ParseInline(yaml);

        var entry = Assert.Single(manifest.Screenshots);
        Assert.Equal("catalog-thumb", entry.Kind);
        Assert.Equal(320, entry.ThumbWidth);
        Assert.Equal(240, entry.ThumbHeight);
    }

    [Fact]
    public void Catalog_thumb_accepts_explicit_dimensions()
    {
        var yaml = """
            app:
              title: Catalog
            screenshots:
              - id: wide-thumb
                kind: catalog-thumb
                description: Wide thumb.
                thumb-width: 480
                thumb-height: 320
            """;

        var manifest = ParseInline(yaml);

        var entry = Assert.Single(manifest.Screenshots);
        Assert.Equal("catalog-thumb", entry.Kind);
        Assert.Equal(480, entry.ThumbWidth);
        Assert.Equal(320, entry.ThumbHeight);
    }

    private static DocManifest ParseInline(string yaml)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, yaml);
            return ManifestParser.Parse(path);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
