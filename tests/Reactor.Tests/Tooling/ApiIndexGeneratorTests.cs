using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.UI.Reactor.ApiIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

// Drives the api-index generator IN-PROCESS (the test host loads Reactor.dll fine
// on ARM64, where the SignaturesGen apphost crashes). The UPDATE_API_INDEX=1 arm is
// the ARM64-safe way to regenerate the two committed reactor.api.txt copies.
[Collection("ConsoleTests")]
public sealed class ApiIndexGeneratorTests
{
    static Assembly ReactorAssembly => typeof(Microsoft.UI.Reactor.Factories).Assembly;

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only api-index generator: reflects over the Reactor assembly's public surface (Assembly.GetTypes / member reflection) by design. This host is never trimmed. Behaviour-neutral.")]
    static string Generate() => ApiIndexGenerator.Generate(ReactorAssembly);

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "skills", "reactor.api.txt"))
                || File.Exists(Path.Combine(dir, "Reactor.slnx")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    static string CommittedIndexPath() => Path.Combine(RepoRoot(), "skills", "reactor.api.txt");

    static string[] OutputPaths()
    {
        var root = RepoRoot();
        return new[]
        {
            Path.Combine(root, "skills", "reactor.api.txt"),
            Path.Combine(root, "plugins", "reactor", "skills", "reactor-dsl", "references", "reactor.api.txt"),
        };
    }

    [Fact]
    public void PublicTypes_Section_IsPresent()
    {
        Assert.Contains("## Public types", Generate());
    }

    [Fact]
    public void PublicTypes_Surfaces_WindowSpec_Opacity()
    {
        var block = TypeBlock(Generate(), "WindowSpec");
        Assert.Contains("Opacity", block);
    }

    [Fact]
    public void PublicTypes_Surfaces_ReactorWindow_SetPosition()
    {
        var block = TypeBlock(Generate(), "ReactorWindow");
        Assert.Contains("SetPosition(double x, double y)", block);
    }

    [Fact]
    public void PublicTypes_Surfaces_Constructors_And_Events()
    {
        var output = Generate();
        var publicTypes = output[output.IndexOf("## Public types", StringComparison.Ordinal)..];
        Assert.Contains("\nnew(", "\n" + publicTypes);
        Assert.Contains("\nevent ", "\n" + publicTypes);
    }

    [Fact]
    public void ExistingSections_Unchanged()
    {
        var generated = Generate();
        var committed = File.ReadAllText(CommittedIndexPath());

        Assert.Equal(Span(committed), Span(generated));

        static string Span(string text)
        {
            var start = text.IndexOf("## Factories", StringComparison.Ordinal);
            Assert.True(start >= 0, "## Factories not found");
            var end = text.IndexOf("## Public types", StringComparison.Ordinal);
            // Pre-regen the committed copy has no "## Public types" marker yet — the
            // five sections still run to EOF, so compare against that.
            if (end < 0) end = text.Length;
            Assert.True(end > start, "## Public types not found after ## Factories");
            return text[start..end];
        }
    }

    [Fact]
    public void Index_IsUpToDate()
    {
        var generated = Generate();

        if (Environment.GetEnvironmentVariable("UPDATE_API_INDEX") == "1")
        {
            foreach (var path in OutputPaths())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, generated);
            }
            return;
        }

        var committed = File.ReadAllText(CommittedIndexPath());
        if (committed != generated)
        {
            throw new Xunit.Sdk.XunitException(
                "skills/reactor.api.txt is stale. Regenerate by running:\n" +
                "  $env:UPDATE_API_INDEX=1; dotnet test tests/Reactor.Tests --filter \"FullyQualifiedName~Tooling.ApiIndexGeneratorTests.Index_IsUpToDate\" -p:SkipSignaturesGen=true -p:SkipReactorApiGen=true -r win-arm64\n" +
                "First diff: " + FirstDiffPreview(committed, generated));
        }
    }

    // Returns a short snippet around the first character that differs between
    // `expected` (committed) and `actual` (generated) — up to ~200 chars total.
    static string FirstDiffPreview(string expected, string actual)
    {
        var min = Math.Min(expected.Length, actual.Length);
        var i = 0;
        while (i < min && expected[i] == actual[i]) i++;
        if (i == min && expected.Length == actual.Length) return "(no diff)";

        var start = Math.Max(0, i - 40);
        string Slice(string s) =>
            s.Substring(start, Math.Min(200, s.Length - start)).Replace("\r", "\\r").Replace("\n", "\\n");
        return $"at offset {i}\n  expected: …{Slice(expected)}…\n  actual:   …{Slice(actual)}…";
    }

    // Returns the lines of a `### <kind> <ShortName>` block up to the next `###`/`##`.
    static string TypeBlock(string output, string shortName)
    {
        var lines = output.Split('\n');
        var sb = new StringBuilder();
        var inBlock = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inBlock = line.EndsWith(" " + shortName, StringComparison.Ordinal);
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inBlock = false;
                continue;
            }
            if (inBlock) sb.AppendLine(line);
        }
        var result = sb.ToString();
        Assert.False(string.IsNullOrWhiteSpace(result), $"No '### ... {shortName}' block found in index.");
        return result;
    }
}
