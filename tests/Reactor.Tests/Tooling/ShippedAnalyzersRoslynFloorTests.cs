using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// The analyzers and source generators packed into the Microsoft.UI.Reactor package load
/// into the consumer's compiler, which rejects (CS8032) any analyzer built against a newer
/// Roslyn than its own. So the Roslyn they compile against is the minimum Visual Studio /
/// .NET SDK for every Reactor app. The Microsoft.CodeAnalysis.CSharp version in
/// <c>Directory.Packages.props</c> pins it; this guards that pin against a Roslyn bump
/// silently raising the floor.
/// </summary>
public sealed class ShippedAnalyzersRoslynFloorTests
{
    // Roslyn 5.0 = Visual Studio 2026 18.0 / .NET 10.0.100 SDK. Raising it is a
    // consumer-facing change: update the comment in Directory.Packages.props and CHANGELOG.md.
    private static readonly global::System.Version RoslynFloor = new(5, 0, 0, 0);

    [Theory]
    [InlineData("Reactor.Analyzers.dll")]
    [InlineData("Reactor.Localization.Generator.dll")]
    [InlineData("Reactor.Wrappers.Generator.dll")]
    [InlineData("Reactor.SourceMap.Generator.dll")]
    public void Shipped_analyzer_does_not_require_a_newer_Roslyn_than_the_floor(string fileName)
    {
        var path = global::System.IO.Path.Join(AppContext.BaseDirectory, fileName);
        Assert.True(global::System.IO.File.Exists(path), $"{fileName} was not copied next to the test assembly.");

        using var stream = global::System.IO.File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var roslynRefs = md.AssemblyReferences
            .Select(md.GetAssemblyReference)
            .Where(r => md.GetString(r.Name).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(r => (Name: md.GetString(r.Name), r.Version))
            .ToList();

        Assert.NotEmpty(roslynRefs);
        Assert.All(roslynRefs, r => Assert.True(
            r.Version <= RoslynFloor,
            $"{fileName} references {r.Name} {r.Version}, above the consumer floor {RoslynFloor}. "
            + "Keep Microsoft.CodeAnalysis.CSharp(.Workspaces) at the floor in Directory.Packages.props."));
    }
}
