using YamlDotNet.Serialization;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Static (reflection-free / NativeAOT-safe) YamlDotNet context for the CLI's
/// doc-pipeline YAML surface. Source-generated at build time by
/// <c>Vecc.YamlDotNet.Analyzers.StaticGenerator</c>: the generator consumes the
/// <c>[YamlSerializable(typeof(T))]</c> attributes below and emits a sibling
/// <c>YamlStaticContext.g.cs</c> so <see cref="StaticDeserializerBuilder"/> looks
/// up type handlers from compile-time tables instead of reflection — clearing the
/// IL3050 (dynamic-code) warnings from <c>new DeserializerBuilder()</c>.
/// </summary>
/// <remarks>
/// Only leaf DTO classes are registered; collection types (<c>List&lt;T&gt;</c>)
/// are discovered transitively by the analyzer and must NOT be registered
/// explicitly (the analyzer crashes on nested-generic registrations). The
/// class is <c>public partial</c> without a base clause because the generated
/// half is hard-coded to <c>public partial class YamlStaticContext :
/// YamlDotNet.Serialization.StaticContext</c>.
/// </remarks>
[YamlStaticContext]
// mur docs manifest (ManifestParser.Parse)
[YamlSerializable(typeof(DocManifest))]
[YamlSerializable(typeof(AppConfig))]
[YamlSerializable(typeof(ScreenshotConfig))]
[YamlSerializable(typeof(BoundsConfig))]
[YamlSerializable(typeof(SnippetSettings))]
// reference-map.yaml (ReferenceMap.Parse)
[YamlSerializable(typeof(ReferenceMap.FileShape))]
[YamlSerializable(typeof(ReferenceMap.DefaultEntry))]
[YamlSerializable(typeof(ReferenceMap.OverrideEntry))]
public partial class YamlStaticContext
{
}
