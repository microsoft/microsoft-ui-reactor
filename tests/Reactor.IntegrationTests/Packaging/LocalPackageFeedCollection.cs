using Xunit;

namespace Microsoft.UI.Reactor.IntegrationTests.Packaging;

/// <summary>
/// Shared by every test that consumes Reactor from a locally-packed nupkg.
///
/// <para>Two reasons this is a COLLECTION fixture rather than a per-class one. It packs
/// <c>src/Reactor</c>, and two classes packing that project concurrently race on
/// <c>obj/Release/.../input.json</c> — the XAML-markup/SignaturesGen race documented in
/// AGENTS.md, which surfaced as "The process cannot access the file ... because it is
/// being used by another process" the first time a second packaging class was added.
/// Sharing one fixture also means the ~2-minute pack happens once instead of per class.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalPackageFeedCollection : ICollectionFixture<TemplatePackageTestFixture>
{
    public const string Name = "LocalPackageFeed";
}
