using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.UI.Reactor.Cli.Docs;

internal class DocManifest
{
    public AppConfig App { get; set; } = new();
    public List<ScreenshotConfig> Screenshots { get; set; } = [];
}

internal class AppConfig
{
    /// <summary>
    /// Capture window size in DIPs, forwarded to the doc app as
    /// <c>--width</c>/<c>--height</c>. For a doc app hosted by
    /// <c>ReactorApp.Run</c> this is the sole declaration of its screenshot
    /// size — the app's own <c>Run</c> call omits it so a human running the app
    /// gets the OS-chosen extent. The interop doc apps
    /// (<c>winforms-interop</c>, <c>wpf-interop</c>) are the exception: they
    /// never call <c>ReactorApp.Run</c> and size their own host window in code,
    /// so these values do not reach them.
    /// </summary>
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public int StartupDelay { get; set; } = 2000;
}

internal class ScreenshotConfig
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string Format { get; set; } = "png";
    public string Crop { get; set; } = "content";
    public string? Component { get; set; }

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

internal static class ManifestParser
{
    private static readonly IDeserializer Deserializer = new StaticDeserializerBuilder(new YamlStaticContext())
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static DocManifest Parse(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        return Deserializer.Deserialize<DocManifest>(yaml) ?? new DocManifest();
    }
}
