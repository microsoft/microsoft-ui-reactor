using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Keeps the "All rules" table on the Analyzers page
/// (<c>docs/_pipeline/templates/analyzers.md.dt</c>) in step with the analyzer
/// release files, which RS2008 already forces every new rule into. The page
/// promises to list every rule with a stable anchor, and it had fallen behind:
/// <c>REACTOR_ANIM_004</c> and <c>REACTOR_PERF_001</c> shipped without a row.
/// </summary>
public class AnalyzerDocsTableTests
{
    private static readonly Regex TableRow = new(
        @"^\| \[(?<id>REACTOR\w+)\]\(#(?<anchor>[a-z0-9_]+)\) \| (?<category>[^|]+?) \| (?<severity>\w+) \|",
        RegexOptions.CultureInvariant);

    private static readonly Regex ReleaseRow = new(
        @"^\s*(?<id>REACTOR\w+)\s*\|(?<rest>.*)$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Every_released_rule_has_a_row_and_every_row_is_a_released_rule()
    {
        var released = ReadReleasedRules();
        var documented = ReadTableRows();

        // Positive control: both parsers really found rules, so an empty diff
        // below is agreement and not two empty sets.
        Assert.True(released.Count >= 50, $"Parsed only {released.Count} rules from AnalyzerReleases.*.md.");
        Assert.True(documented.Count >= 50, $"Parsed only {documented.Count} rows from the analyzers.md.dt table.");

        var missing = released.Keys.Except(documented.Keys).Order(StringComparer.Ordinal).ToList();
        var extra = documented.Keys.Except(released.Keys).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "Add a row to the 'All rules' table in docs/_pipeline/templates/analyzers.md.dt for: " + string.Join(", ", missing));
        Assert.True(extra.Count == 0,
            "The analyzers.md.dt table lists rules that are not in AnalyzerReleases.*.md: " + string.Join(", ", extra));
    }

    [Fact]
    public void Each_row_matches_its_released_category_and_severity()
    {
        var released = ReadReleasedRules();
        var mismatches = ReadTableRows()
            .Where(row => released.TryGetValue(row.Key, out var rule) && rule != row.Value.Rule)
            .Select(row => $"{row.Key}: page says {row.Value.Rule}, release file says {released[row.Key]}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    [Fact]
    public void Each_row_links_to_its_own_rule_reference_section()
    {
        var lines = global::System.IO.File.ReadAllLines(TemplatePath);
        var headings = lines
            .Where(l => l.StartsWith("#### REACTOR", StringComparison.Ordinal))
            .Select(l => l[5..].Trim())
            .ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (id, row) in ReadTableRows())
        {
            if (row.Anchor != id.ToLowerInvariant())
                problems.Add($"{id} links to #{row.Anchor}, not #{id.ToLowerInvariant()}");
            if (!headings.Contains(id))
                problems.Add($"{id} has no '#### {id}' section");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static string TemplatePath =>
        global::System.IO.Path.Join(TestRepo.Root, "docs", "_pipeline", "templates", "analyzers.md.dt");

    private static Dictionary<string, (ReleasedRule Rule, string Anchor)> ReadTableRows()
    {
        var rows = new Dictionary<string, (ReleasedRule, string)>(StringComparer.Ordinal);
        foreach (var line in global::System.IO.File.ReadAllLines(TemplatePath))
        {
            var m = TableRow.Match(line);
            if (!m.Success)
                continue;
            var id = m.Groups["id"].Value;
            Assert.True(rows.TryAdd(id, (new ReleasedRule(m.Groups["category"].Value, m.Groups["severity"].Value), m.Groups["anchor"].Value)),
                $"{id} appears twice in the analyzers.md.dt table.");
        }
        return rows;
    }

    /// <summary>
    /// Replays the Roslyn release-tracking files: Shipped in release order, then
    /// Unshipped. "New Rules" adds, "Changed Rules" replaces the category and
    /// severity, and "Removed Rules" drops the rule.
    /// </summary>
    private static Dictionary<string, ReleasedRule> ReadReleasedRules()
    {
        var rules = new Dictionary<string, ReleasedRule>(StringComparer.Ordinal);
        foreach (var file in new[] { "AnalyzerReleases.Shipped.md", "AnalyzerReleases.Unshipped.md" })
        {
            string? section = null;
            foreach (var line in global::System.IO.File.ReadAllLines(global::System.IO.Path.Join(TestRepo.Root, "src", "Reactor.Analyzers", file)))
            {
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    section = line[4..].Trim();
                    continue;
                }

                var m = ReleaseRow.Match(line);
                if (!m.Success)
                    continue;

                var id = m.Groups["id"].Value;
                var cells = m.Groups["rest"].Value.Split('|').Select(c => c.Trim()).ToArray();
                switch (section)
                {
                    case "New Rules":
                    case "Changed Rules":
                        rules[id] = new ReleasedRule(cells[0], cells[1]);
                        break;
                    case "Removed Rules":
                        rules.Remove(id);
                        break;
                    default:
                        Assert.Fail($"{file}: {id} is outside a New, Changed or Removed Rules section.");
                        break;
                }
            }
        }
        return rules;
    }

    private sealed record ReleasedRule(string Category, string Severity)
    {
        public override string ToString() => $"{Category} / {Severity}";
    }
}
