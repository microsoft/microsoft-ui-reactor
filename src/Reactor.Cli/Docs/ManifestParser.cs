using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.UI.Reactor.Cli.Docs;

internal class DocManifest
{
    public AppConfig App { get; set; } = new();
    public List<ScreenshotConfig> Screenshots { get; set; } = [];
    public SnippetSettings? Snippets { get; set; }
}

internal class AppConfig
{
    public string Title { get; set; } = "";
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public string Theme { get; set; } = "light";
    public int StartupDelay { get; set; } = 2000;
}

internal class ScreenshotConfig
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string Region { get; set; } = "client";
    public string Format { get; set; } = "png";
    public string? Component { get; set; }
    public string? Theme { get; set; }
    public BoundsConfig? Bounds { get; set; }

    /// <summary>
    /// Capture kind. Defaults to <c>screenshot</c> (full-size, border + drop shadow).
    /// <c>catalog-thumb</c> downscales the captured frame to 320×240 with high-quality
    /// interpolation and writes <c>&lt;id&gt;-thumb.png</c> instead of <c>&lt;id&gt;.png</c>.
    /// Used by the controls-catalog index page (spec 041 §6.3 + §12 Q7).
    /// </summary>
    public string Kind { get; set; } = "screenshot";

    /// <summary>Target width in pixels for <c>kind: catalog-thumb</c>. Defaults to 320.</summary>
    public int ThumbWidth { get; set; } = 320;

    /// <summary>Target height in pixels for <c>kind: catalog-thumb</c>. Defaults to 240.</summary>
    public int ThumbHeight { get; set; } = 240;
}

internal class BoundsConfig
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

internal class SnippetSettings
{
    public bool TrimNamespaceUsings { get; set; }
    public int MaxLinesWarning { get; set; } = 30;
}

internal static class ManifestParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static DocManifest Parse(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        return Deserializer.Deserialize<DocManifest>(yaml) ?? new DocManifest();
    }
}
