using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Locating and parsing the ReactorGallery page sources, shared by the lints that read them.
/// Roslyn parses the pages directly — no gallery build, no WinUI objects — so every lint built on
/// this stays in the headless unit tier.
/// </summary>
/// <remarks>
/// Two lints read these files (<see cref="GallerySampleLintTests"/> and
/// <see cref="GallerySnippetAgreementTests"/>) and a third would make three, so the loader lives
/// here rather than being copied. Copies would drift: a page directory added to one and not the
/// other would silently stop being linted by whichever was missed.
/// </remarks>
static class GallerySources
{
    public static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repo root (Reactor.slnx) from " + AppContext.BaseDirectory);
    }

    public static string GalleryDir() => Path.Join(RepoRoot(), "samples", "ReactorGallery");

    public static string ControlPagesDir() => Path.Join(GalleryDir(), "ControlPages");

    /// <summary>Every gallery control page, parsed, in a stable order.</summary>
    public static IReadOnlyList<(string Path, SyntaxNode Root)> Pages() => ReadSources(ControlPagesDir());

    /// <summary>Includes supporting source files so constants and partial types bind across files.</summary>
    public static IReadOnlyList<(string Path, SyntaxNode Root)> AllSources() => ReadSources(GalleryDir());

    static IReadOnlyList<(string Path, SyntaxNode Root)> ReadSources(string directory)
    {
        Assert.True(Directory.Exists(directory), $"gallery source directory not found at {directory}");

        var pages = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part.Equals("bin", global::System.StringComparison.OrdinalIgnoreCase)
                             || part.Equals("obj", global::System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p, global::System.StringComparer.Ordinal)
            .Select(p => (Path: p, Root: CSharpSyntaxTree.ParseText(File.ReadAllText(p)).GetRoot()))
            .ToList();

        Assert.NotEmpty(pages);
        return pages;
    }

    public static string Rel(string absolute) =>
        Path.GetRelativePath(RepoRoot(), absolute).Replace('\\', '/');

    public static string Where(string path, SyntaxNode node) =>
        $"{Rel(path)}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";

    /// <summary>Simple name of an invocation: <c>Foo(...)</c> and <c>x.Foo(...)</c> both yield "Foo".</summary>
    public static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => null,
    };
}
