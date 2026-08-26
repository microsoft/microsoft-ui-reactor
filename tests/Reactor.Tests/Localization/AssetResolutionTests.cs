using Microsoft.UI.Reactor.Localization;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class AssetResolutionTests
{
    private static IntlAccessor CreateAccessor(string locale)
    {
        return new IntlAccessor(locale, new InMemoryResourceProvider(), new MessageCache(), "en-US");
    }

    [Fact]
    public void Asset_NoLocaleFile_ReturnsOriginalPath()
    {
        var t = CreateAccessor("en-US");
        // No locale-specific file exists on disk
        var result = t.Asset("Assets/hero-banner.png");
        Assert.Equal("Assets/hero-banner.png", result);
    }

    [Fact]
    public void Asset_WithLocaleFile_ReturnsLocalePath()
    {
        // Create temp directory structure for the test
        var tempDir = Path.Combine(Path.GetTempPath(), "reactor-asset-test-" + Guid.NewGuid().ToString("N")[..8]);
        var localeDir = Path.Combine(tempDir, "Assets", "ja-JP");
        Directory.CreateDirectory(localeDir);
        File.WriteAllText(Path.Combine(localeDir, "hero-banner.png"), "test");

        try
        {
            var t = CreateAccessor("ja-JP");
            var result = t.Asset(Path.Combine(tempDir, "Assets", "hero-banner.png"));

            Assert.Equal(Path.Combine(tempDir, "Assets", "ja-JP", "hero-banner.png"), result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Asset_FallsBackToBaseLanguage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "reactor-asset-test-" + Guid.NewGuid().ToString("N")[..8]);
        var baseDir = Path.Combine(tempDir, "Assets", "fr");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "banner.png"), "test");

        try
        {
            // fr-CA has no specific asset, but base "fr" does
            var t = CreateAccessor("fr-CA");
            var result = t.Asset(Path.Combine(tempDir, "Assets", "banner.png"));

            Assert.Equal(Path.Combine(tempDir, "Assets", "fr", "banner.png"), result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
