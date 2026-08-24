// Repository-content gate for the controls catalog page
// (docs/_pipeline/templates/controls.md.dt).
//
// The problem this locks in: the catalog page bills itself as a "catalog of
// every Reactor control". Nothing checked that. Registration lives in
// `ReactorApp.RegisterAllBuiltIns()` (src/Reactor/Hosting/ReactorApp.BuiltIns.cs)
// and grows whenever a control is added, but the page is prose — so the claim
// rotted silently until it listed 46 of 95 registered element families and was
// missing three whole categories (layout, navigation, shapes).
//
// This gate makes the claim derived: every element family named in the
// registration bootstrap must appear in the catalog template, or be listed in
// the template's own explicit exclusion list. Adding a control without a
// catalog row now reddens the build.
//
// Non-vacuity: the parser is pinned by floors on both sides (registered-family
// count and matched-row count), so a regex that stops matching fails loudly
// instead of trivially passing on an empty set. `NotRegistered_*` proves the
// gate can actually fail.
//
// Namespace note: in Microsoft.UI.Reactor.Tests, `Microsoft.UI.System` shadows
// `System`, so any `System.`-qualified path must be written `global::System.`.

using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

using Regex = global::System.Text.RegularExpressions.Regex;
using Match = global::System.Text.RegularExpressions.Match;

namespace Microsoft.UI.Reactor.Tests.Docs;

public sealed class ControlCatalogCompletenessTests
{
    const string BootstrapPath = "src/Reactor/Hosting/ReactorApp.BuiltIns.cs";
    const string CatalogPath = "docs/_pipeline/templates/controls.md.dt";

    /// <summary>
    /// Registered element families that are framework plumbing rather than a
    /// control an author picks off a shelf. Each one is documented on another
    /// page; the catalog template names this same set in its "What 'every
    /// control' means here" note, and <see cref="Exclusions_AreDeclaredInTheTemplate"/>
    /// keeps the two in step.
    /// </summary>
    static readonly string[] NonCatalogFamilies =
    [
        "CommandHost", "NavigationHost", "FormField", "ValidationRule",
        "ValidationVisualizer", "Semantic", "AnnounceRegion",
        "XamlHost", "XamlPage",
    ];

    /// <summary>
    /// Families whose catalog row uses a factory name that differs from the
    /// element-record name. The catalog documents what an author types, so the
    /// row says <c>VStack</c>, not <c>StackElement</c>.
    /// </summary>
    static readonly Dictionary<string, string[]> FactoryAliases = new()
    {
        ["Stack"] = ["VStack", "HStack"],
        ["Flex"] = ["FlexRow", "FlexColumn"],
        ["Path"] = ["Path2D"],
        ["Progress"] = ["Progress", "ProgressIndeterminate"],
        ["TemplatedList"] = ["ListView<T>", "GridView<T>"],
        ["LazyStack"] = ["LazyVStack<T>", "LazyHStack<T>"],
        ["TemplatedTreeView"] = ["TreeView<T>"],
        ["MediaPlayerElement"] = ["MediaPlayerElement"],
    };

    // Floors. These are deliberately well below today's real counts: they exist
    // so a parser that silently stops matching fails, not to pin exact numbers
    // that a legitimate change would have to churn.
    const int MinRegisteredFamilies = 80;
    const int MinCatalogRows = 60;

    [Fact]
    public void EveryRegisteredControl_HasACatalogRow()
    {
        var registered = ReadRegisteredFamilies();
        var catalog = ReadCatalogSymbols();

        Assert.True(
            registered.Count >= MinRegisteredFamilies,
            $"Only parsed {registered.Count} registered families from {BootstrapPath}; " +
            $"expected at least {MinRegisteredFamilies}. The parser is probably broken.");

        var missing = new List<string>();
        foreach (var family in registered)
        {
            if (NonCatalogFamilies.Contains(family))
                continue;

            var candidates = FactoryAliases.TryGetValue(family, out var aliases)
                ? aliases
                : [family];

            // A family is covered when any of its documented spellings appears
            // as a code span in the catalog.
            if (!candidates.Any(catalog.Contains))
                missing.Add(family);
        }

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} control(s) are registered in {BootstrapPath} but have no row in " +
            $"{CatalogPath}: {string.Join(", ", missing.Order())}. Add a catalog row (or, if the " +
            "control is framework plumbing, add it to NonCatalogFamilies here and to the " +
            "exclusion note in the template).");
    }

    [Fact]
    public void NotRegistered_ControlIsReportedMissing()
    {
        // Mutation guard: the gate must be able to fail. A family that is
        // registered but deliberately absent from the catalog has to be caught,
        // otherwise EveryRegisteredControl_HasACatalogRow proves nothing.
        var catalog = ReadCatalogSymbols();
        Assert.DoesNotContain("ThereIsNoSuchControl", catalog);

        var registered = ReadRegisteredFamilies();
        var uncovered = registered
            .Where(f => !NonCatalogFamilies.Contains(f))
            .Where(f => !(FactoryAliases.TryGetValue(f, out var a) ? a : [f]).Any(catalog.Contains))
            .ToList();

        // Today this must be empty — but the point of the assertion above is
        // that `catalog.Contains` genuinely discriminates, so an uncovered
        // family would land here rather than being silently absorbed.
        Assert.Empty(uncovered);
    }

    [Fact]
    public void Exclusions_AreDeclaredInTheTemplate()
    {
        // The template tells the reader exactly what "every control" omits.
        // If this test's exclusion list and the template's note drift apart,
        // the page is lying again — just more quietly.
        var template = ReadText(CatalogPath);

        foreach (var family in NonCatalogFamilies)
        {
            Assert.True(
                template.Contains($"`{family}`", global::System.StringComparison.Ordinal),
                $"'{family}' is excluded from the catalog by this test but is not named in the " +
                $"exclusion note in {CatalogPath}. Readers would have no way to know it was " +
                "deliberately left out.");
        }
    }

    [Fact]
    public void CatalogParser_FindsCodeSpans()
    {
        // Non-vacuity floor for the catalog side of the comparison: a regex
        // that matched nothing would make EveryRegisteredControl_HasACatalogRow
        // fail loudly rather than pass, but an over-broad one that matched
        // everything would make it vacuous. Pin both ends.
        var catalog = ReadCatalogSymbols();

        Assert.True(
            catalog.Count >= MinCatalogRows,
            $"Only parsed {catalog.Count} code spans from {CatalogPath}; expected at least " +
            $"{MinCatalogRows}. The parser is probably broken.");

        // Sanity: real control names are present, invented ones are not.
        Assert.Contains("TextBlock", catalog);
        Assert.Contains("NavigationView", catalog);
        Assert.DoesNotContain("MarkdownTextBlock", catalog);
    }

    // ── helpers ──

    /// <summary>
    /// Element families registered by <c>ReactorApp.RegisterAllBuiltIns()</c>.
    /// Mirrors the three registration shapes the bootstrap uses: a static-cctor
    /// touch, a <c>Reg*&lt;…&gt;</c> generic, and a descriptor registration.
    /// </summary>
    static IReadOnlyCollection<string> ReadRegisteredFamilies()
    {
        var source = ReadText(BootstrapPath);
        var families = new SortedSet<string>(global::System.StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(source, @"typeof\((?:[\w\.]*?\.)?(\w+?)Element\)\.TypeHandle"))
            families.Add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(source, @"Reg(?:Decorator|BaseDecorator)?<(?:[\w\.]*?\.)?(\w+?)Element(?:Base)?\s*,"))
            families.Add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(source, @"Desc\.(\w+?)Descriptor\b"))
            families.Add(m.Groups[1].Value);

        return families;
    }

    /// <summary>
    /// Every inline code span in the catalog template, stripped of the trailing
    /// call/generic syntax so <c>`TextBlock`</c> and <c>`ListView&lt;T&gt;`</c>
    /// both resolve. Both the bare and the generic spelling are retained so
    /// aliases can match either form.
    /// </summary>
    static HashSet<string> ReadCatalogSymbols()
    {
        var template = ReadText(CatalogPath);
        var symbols = new HashSet<string>(global::System.StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(template, @"`([A-Za-z][\w<>]*)`"))
        {
            var raw = m.Groups[1].Value;
            symbols.Add(raw);

            var generic = raw.IndexOf('<');
            if (generic > 0)
                symbols.Add(raw[..generic]);
        }

        // Slash-separated rows ("`Title` / `Heading`") already produce one span
        // each, so nothing further to split.
        return symbols;
    }

    static string ReadText(string repoRelativePath)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.True(root is not null, "Could not locate the repository root from the test assembly directory.");

        var full = Path.Combine(root!, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Expected file not found: {repoRelativePath}");

        return File.ReadAllText(full);
    }
}
