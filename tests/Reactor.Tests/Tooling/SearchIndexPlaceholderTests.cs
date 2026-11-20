using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.SearchIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

public sealed class SearchIndexPlaceholderTests
{
    [Theory]
    [InlineData("// Pick a colour, ... then bind\nAlpha();")]
    [InlineData("// see Dsl.cs (...)\nAlpha();")]
    [InlineData("Alpha(); // see Dsl.cs (...)")]
    [InlineData("/* Pick a colour, ... then bind */\nAlpha();")]
    [InlineData("/// see Dsl.cs (...)\nAlpha();")]
    [InlineData("// Configure <your-key> or see .../docs\nAlpha();")]
    [InlineData("TextBlock(\"Loading, ...\");")]
    [InlineData("TextBlock(\"Please wait (...)\");")]
    [InlineData("TextBlock(\"Type here...\");")]
    [InlineData("TextBlock(@\"Loading, ...\");")]
    [InlineData("TextBlock(\"\"\"Loading, ...\"\"\");")]
    [InlineData("TextBlock(\"\"\"\nLoading, ...\n\"\"\");")]
    [InlineData("TextBlock($\"Loading, ... {state}\");")]
    [InlineData("TextBlock($@\"Loading, ... {state}\");")]
    [InlineData("TextBlock($\"\"\"Loading, ... {state}\"\"\");")]
    [InlineData("TextBlock($\"{state:(...)}\");")]
    [InlineData("var text = \"Loading, ...\"u8;")]
    [InlineData("var text = \"\"\"Loading, ...\"\"\"u8;")]
    [InlineData("TextBlock(\"https://example.com/path // Loading, ...\");")]
    [InlineData("TextBlock(\"Continue.../stop\");")]
    [InlineData("TextBlock(\"Scanning /...\");")]
    [InlineData("TextBlock(\"Example: https://example.com/media/( ... )\");")]
    [InlineData("TextBlock(\"Show <your.azure.maps.key> in the help panel\");")]
    [InlineData("var label = \"Scanning /...\"; TextBlock(label);")]
    [InlineData("TextBlock(\"Show the tag <yourself>\");")]
    [InlineData("Button(\"Run\").Click(() => { /* No-op: see Run(...) for the real command. */ });")]
    [InlineData("var options = [.. otherOptions];")]
    [InlineData("var items = new List<yourType>();")]
    [InlineData("var items = new List<your_api_key>();")]
    [InlineData("new Uri(\"https://example.com/docs?q=/...\");")]
    [InlineData("new Uri(\"https://example.com/docs/page#archive/...\");")]
    [InlineData("new Uri(\"https://example.com/docs/page\");")]
    [InlineData("Image(\"ms-appx:///Assets/photos/photo.png\");")]
    [InlineData("MediaPlayerElement(\"https://example.com/clip.mp4\");")]
    [InlineData("TextBlock(\"https://example.com/photo.png\");")]
    [InlineData("TextBlock(\"https://example.com/photo.png?caption=(...)#.../\");")]
    [InlineData("TextBlock(\"Example: https://example.com/.../photo.png\");")]
    [InlineData("TextBlock(\"Status: .../loading\");")]
    [InlineData("TextBlock(\"Browse https://<your-host>/clip.mp4\");")]
    [InlineData("TextBlock(\"1https://example.com/.../photo.png\");")]
    [InlineData("TextBlock(\"https_demo://example.com/.../photo.png\");")]
    [InlineData("MapControl(\"configured-key\");")]
    [InlineData("Factories.MapControl(\"configured-key\");")]
    [InlineData("MapControl(\"configured-key\", \"<your-note>\");")]
    [InlineData("Factories.MapControl(\"configured-key\", \"<your-note>\");")]
    [InlineData("new global::System.Uri(\"Assets/photo.png\", UriKind.Relative);")]
    [InlineData("Factories . MapControl(\"configured-key\");")]
    [InlineData("Factories /* maps */ . MapControl(\"configured-key\", \"<your-note>\");")]
    [InlineData("new System /* note */ . Uri(\"Assets/photo.png\", UriKind.Relative);")]
    [InlineData("new global /* alias */ :: System . Uri(\"Assets/photo.png\", UriKind.Relative);")]
    [InlineData("Other /* note */ . MapControl(\"<your-key>\");")]
    [InlineData("new Other /* note */ . Uri(\".../photo.png\", UriKind.Relative);")]
    public void CompleteCards_WithProseEllipses_AreRetainedInSourceOrder(string code)
    {
        using var gallery = new MiniGallery(betaRouted: true);
        gallery.OverwriteAlphaPage(PageWithCards(code));
        var editorial = gallery.WriteEditorial(Editorial);

        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorial).Json);
        var samples = index.RootElement.GetProperty("controls")[0].GetProperty("samples");

        // Pin both the leading card and a later card: a first-card-only gate misses later drops.
        Assert.Equal(new[] { "First complete", "Unrelated", "Later complete" },
            samples.EnumerateArray().Select(s => s.GetProperty("header").GetString()).ToArray());
        Assert.Equal(code, samples[0].GetProperty("code").GetString());
        Assert.Equal(code, samples[2].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Alpha(a, ...);")]
    [InlineData("Alpha(...);")]
    [InlineData("Alpha(\n...\n);")]
    [InlineData("Alpha(a, /* guidance */ ...);")]
    [InlineData("Button(\"Run\").Click(...);")]
    [InlineData("TextBlock($\"{Alpha(...)}\");")]
    [InlineData("TextBlock($\"\"\"{Alpha(...)}\"\"\");")]
    [InlineData("MapControl(\"<your-key>\");")]
    [InlineData("MapControl(@\"<your-key>\");")]
    [InlineData("MapControl(\"\"\"<your-key>\"\"\");")]
    [InlineData("MapControl(\"<yourApiKey>\");")]
    [InlineData("MapControl(<yourApiKey>);")]
    [InlineData("MapControl(mapServiceToken: \"<your.azure.maps.key>\");")]
    [InlineData("MapControl(\"<yourapikey>\");")]
    [InlineData("Factories.MapControl(\"<your-key>\");")]
    [InlineData("Factories.MapControl(mapServiceToken: \"<your-key>\");")]
    [InlineData("new global::System.Uri(\".../photo.png\", UriKind.Relative);")]
    [InlineData("new Uri(\"https://<your-host>/x\");")]
    [InlineData("Factories . MapControl(\"<your-key>\");")]
    [InlineData("Factories /* maps */ . MapControl(\"<your-key>\");")]
    [InlineData("Factories . MapControl(mapServiceToken: \"<your-key>\");")]
    [InlineData("new System . Uri(\".../photo.png\", UriKind.Relative);")]
    [InlineData("new System /* note */ . Uri(\".../photo.png\", UriKind.Relative);")]
    [InlineData("new global /* alias */ :: System /* note */ . Uri(\".../photo.png\", UriKind.Relative);")]
    [InlineData("var uri = new Uri(\"ms-appx:///.../image.png\");")]
    [InlineData("var uri = new Uri(\"https://example.com/media/(...)\");")]
    [InlineData("var uri = new Uri(\"https://example.com/media/( ... )\");")]
    [InlineData("var uri = new System.Uri(uriString: \"https://example.com/.../page\");")]
    [InlineData("var uri = new Uri(\"https://example.com/docs/\", \".../page\");")]
    [InlineData("Image(\"ms-appx:///Assets/.../photo.png\");")]
    [InlineData("MediaPlayerElement(\"https://<your-host>/clip.mp4\");")]
    [InlineData("TextBlock(\"https://example.com/.../photo.png\");")]
    [InlineData("var source = \"https://example.com/.../photo.png\";")]
    [InlineData("Image(@\"ms-appx:///Assets/.../photo.png\");")]
    [InlineData("Image(\"\"\"ms-appx:///Assets/.../photo.png\"\"\");")]
    [InlineData("var source = \"https://example.com/.../photo.png\"u8;")]
    [InlineData("var source = \"\"\"https://example.com/.../photo.png\"\"\"u8;")]
    [InlineData("HyperlinkButton(\"Open\", new(\"https://example.com/.../page\"));")]
    [InlineData("Image(\"asset:.../photo.png\");")]
    [InlineData("Image(\"APP+demo.v1-test://assets/.../photo.png\");")]
    [InlineData("HyperlinkButton(\"Email\", \"mailto:<your-email>\");")]
    [InlineData("Button(\"Run\").Click(() => { /* Do work, ... */ });")]
    [InlineData("Button(\"Run\").Click(() => { /* ... omitted (...), fill in callback */ });")]
    [InlineData("Alpha();\n// omitted: configure the handler\nBeta();")]
    [InlineData("Button(\"Run\").Click(() => { /* ... */ });")]
    [InlineData("Alpha();\n// ...\nBeta();")]
    [InlineData("var data = ...;")]
    [InlineData("var options = [ ... ];")]
    [InlineData("Button(\"Run\").Click(() => { ... });")]
    [InlineData("// Guidance only, ...")]
    [InlineData("/* Guidance only, ... */")]
    [InlineData("// omitted: replace the token\nvar token = \"<your-key>\"; MapControl(token);")]
    public void AbbreviatedOrCommentOnlyCards_AreSkipped(string code)
    {
        using var gallery = new MiniGallery(betaRouted: true);
        gallery.OverwriteAlphaPage(PageWithCards(code));
        var editorial = gallery.WriteEditorial(Editorial);

        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorial).Json);
        var samples = index.RootElement.GetProperty("controls")[0].GetProperty("samples");

        Assert.Equal("Unrelated", Assert.Single(samples.EnumerateArray()).GetProperty("header").GetString());
    }

    const string Editorial = """
        { "alpha": { "keywords": ["alpha", "probe", "sample"] },
          "beta": { "keywords": ["beta", "probe", "sample"] } }
        """;

    static string PageWithCards(string code)
    {
        var literal = SyntaxFactory.Literal(code).Text;
        return $$"""
            class AlphaPage
            {
                object Render() => VStack(
                    SampleCard("First complete", null, {{literal}}),
                    SampleCard("Unrelated", null, "Alpha();"),
                    SampleCard("Later complete", null, {{literal}}));
            }
            """;
    }
}
