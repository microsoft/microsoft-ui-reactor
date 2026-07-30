using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    public void PublicTypes_Surfaces_ControlRegistry_Registration_Entry_Points()
    {
        // ControlRegistry is a public *static* class, and static classes used to be
        // excluded from the index wholesale — so the frozen registration seam (and the
        // API the unregistered-mount throw tells you to call) returned zero hits.
        var block = TypeBlock(Generate(), "ControlRegistry");

        Assert.Contains("Register<TElement, TControl>(", block);
        Assert.Contains("RegisterDecorator<TElement>(", block);
        Assert.Contains("RegisterForDerivedTypes<TBase, TControl>(", block);
        Assert.Contains("RegisterDecoratorForDerivedTypes<TBase>(", block);
    }

    [Fact]
    public void PublicTypes_Surfaces_ReactorApp_Entry_Points()
    {
        var block = TypeBlock(Generate(), "ReactorApp");

        Assert.Contains("RegisterAllBuiltIns() → void", block);
        Assert.Contains("Run(Action<ReactorAppContext> startup) → void", block);
    }

    [Fact]
    public void PublicTypes_Does_Not_Duplicate_Theme_Tokens()
    {
        // Theme is a static class, so it now reaches Public types — but its ThemeRef
        // tokens have their own dedicated section that also resolves each resource key.
        // Emitting them twice would add ~36 strictly-less-informative lines.
        var output = Generate();
        var split = output.IndexOf("## Public types", StringComparison.Ordinal);
        Assert.True(split > 0, "## Public types not found");

        var beforePublicTypes = output[..split];
        var publicTypes = output[split..];

        Assert.Contains("Theme.SolidBackground", beforePublicTypes);
        Assert.DoesNotContain("SolidBackground", publicTypes);

        // The non-token member of Theme is still surfaced, so the type isn't just skipped.
        Assert.Contains("Ref(string resourceKey) → ThemeRef", TypeBlock(output, "Theme"));
    }

    [Fact]
    public void PublicTypes_Does_Not_Duplicate_Element_Modifier_Extensions()
    {
        // Extension methods can only live on a static class. Now that static classes are
        // indexed, an unfiltered pass would emit every modifier a second time — byte
        // identical, since both paths run through FormatMethod. Structural oracle: no
        // line emitted by ## Modifiers or ## Hooks may reappear under ## Public types.
        var output = Generate();

        var modifierAndHookLines = SectionLines(output, "## Modifiers", "## Reference builders")
            .Concat(SectionLines(output, "## Hooks", "## Theme tokens"))
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(modifierAndHookLines);

        var publicTypeLines = SectionLines(output, "## Public types", null)
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
            .ToList();

        var duplicated = publicTypeLines.Where(modifierAndHookLines.Contains).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(
            duplicated.Count == 0,
            "Lines emitted by ## Modifiers / ## Hooks reappear under ## Public types:\n  " +
            string.Join("\n  ", duplicated.Take(10)));
    }

    [Fact]
    public void PublicTypes_Surfaces_Extensions_No_Other_Section_Owns()
    {
        // ## Modifiers only claims Element receivers and ## Hooks only RenderContext /
        // Component ones. An extension whose receiver is neither — e.g. the RichText
        // builders — is owned by nobody, so ## Public types has to carry it or it is
        // absent from the index entirely.
        //
        // The assertions must be RECEIVER-QUALIFIED: a bare "TextIndent(" also matches
        // RichTextBlockElement.TextIndent, which ## Modifiers emits either way, so it
        // would pass even with these extensions filtered out.
        var output = Generate();

        Assert.Contains("RichTextParagraph.TextIndent(double indent) → RichTextParagraph", output);
        Assert.Contains("RichTextParagraph.Margin(double uniform) → RichTextParagraph", output);
        Assert.Contains("RichTextRun.", output);
        Assert.Contains("RichTextHyperlink.", output);

        // Cross-check against the declaring source so a rename fails loudly here rather
        // than silently weakening the assertions above.
        Assert.Contains("Margin(this RichTextParagraph", SourceOfRichTextExtensions());
        Assert.Contains("TextIndent(this RichTextParagraph", SourceOfRichTextExtensions());
    }

    // Reads the declaring source so the test fails loudly if these extensions are ever
    // renamed or moved, rather than silently passing against a stale expectation.
    // Path.Join rather than Path.Combine: Combine discards everything before a segment
    // that looks rooted, which is a silent-wrong-path hazard even when the segments are
    // constants (CodeQL flags it).
    static string SourceOfRichTextExtensions() =>
        File.ReadAllText(Path.Join(RepoRoot(), "src", "Reactor", "Elements", "ElementExtensions.cs"));

    // Lines strictly between `start` and the next `## ` heading (or `end` when given).
    static IEnumerable<string> SectionLines(string output, string start, string? end)
    {
        var from = output.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        from += start.Length;

        var to = end is null ? -1 : output.IndexOf(end, from, StringComparison.Ordinal);
        if (to < 0) to = output.Length;

        return output[from..to].Split('\n').Select(l => l.TrimEnd('\r'));
    }

    [Fact]
    public void Signatures_Render_Char_Defaults_As_Pasteable_CSharp()
    {
        // The index exists to be copy-pasted, so a char parameter has to render as the
        // C# keyword with a quoted default — `char placeholder = '_'`, never the raw
        // metadata form `Char placeholder = _`, which does not compile.
        var output = Generate();

        Assert.Contains("char placeholder = '_'", output);
        Assert.DoesNotContain("Char placeholder = _", output);
    }

    [Fact]
    public void Signatures_Render_Value_Type_And_Generic_Defaults_As_Default_Not_Null()
    {
        // Reflection reports `= default` on a struct or generic parameter as a null
        // constant. Emitting `null` there does not compile: CancellationToken is a
        // struct, and `T x = null` is invalid for an unconstrained T.
        var output = Generate();

        Assert.DoesNotContain("CancellationToken cancellationToken = null", output);
        Assert.Contains("CancellationToken cancellationToken = default", output);
        Assert.Contains("T defaultValue = default", output);

        // Reference-type defaults must still read `null`.
        Assert.Contains("= null) →", output);
    }

    [Fact]
    public void Numeric_Defaults_Are_Invariant_And_Carry_Their_Literal_Suffix()
    {
        // The index is committed twice and byte-compared by CI, so a culture-sensitive
        // ToString would corrupt it on a comma-decimal machine. And `float x = 0.6` is
        // not valid C# without the `f`.
        var output = Generate();

        Assert.Contains("float dampingRatio = 0.6f", output);
        Assert.DoesNotContain("float dampingRatio = 0.6,", output);
        Assert.DoesNotMatch(new global::System.Text.RegularExpressions.Regex(@"= \d+,\d+[,)]"), output);
    }

    [Fact]
    public void Generator_Output_Is_Culture_Invariant()
    {
        // Directly pins the determinism property rather than inferring it from the text:
        // regenerating under a comma-decimal culture must produce identical bytes.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var underGerman = Generate();

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var underInvariant = Generate();

            Assert.Equal(underInvariant, underGerman);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
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

    [Fact]
    public void Every_Committed_Copy_Matches_The_Generator()
    {
        // The index is committed TWICE — `skills/` for `mur --api` / the agentkit NuGet
        // layout, and `plugins/.../references/` for the reactor-dsl skill. Both are packed
        // independently, and Index_IsUpToDate only ever compared the first, so the mirror
        // could drift silently and still ship.
        var generated = Generate();

        foreach (var path in OutputPaths())
        {
            Assert.True(File.Exists(path), $"Committed API index copy is missing: {path}");
            if (File.ReadAllText(path) != generated)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{path} is stale. Regenerate with the UPDATE_API_INDEX=1 command in Index_IsUpToDate.\n" +
                    "First diff: " + FirstDiffPreview(File.ReadAllText(path), generated));
            }
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
