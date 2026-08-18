using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Pins the <c>REACTOR_MOD_003</c> gate list in the shipped
/// <c>reactor-build-and-check</c> skill to <see cref="ModifierTable"/>. Issue #1062.
/// </summary>
/// <remarks>
/// <para>
/// That row states the receiver gates in prose — "<c>Background</c> → Panel/Control/Border", and
/// four more. It is shipped to end users as agent guidance, and until this fact existed nothing
/// tied it to the table it describes, so widening a gate in <c>ModifierTable.cs</c> left the skill
/// quietly wrong. This is the concrete text consumer issue #1062 was filed about.
/// </para>
/// <para>
/// Note what this fact does <b>not</b> need: the gate group identifiers. The prose names receiver
/// <em>types</em>, so the comparison is set-vs-set against the typed
/// <see cref="ModifierInfo.ControlGate"/> — the rule the issue lands on, applied. A check phrased
/// over gate <em>names</em> would have been the exposed one; see
/// <see cref="ModifierGateIdentifierTests"/> for the matcher that makes name-based matching safe
/// when an artifact leaves no alternative.
/// </para>
/// </remarks>
public class ModifierGateProseParityTests
{
    private const string SkillPath = "plugins/reactor/skills/reactor-build-and-check/SKILL.md";

    /// <summary>Prose spelling → the type name <see cref="ModifierTable"/> uses.</summary>
    private static readonly Dictionary<string, string> ProseTypeNames = new(StringComparer.Ordinal)
    {
        ["Panel"] = "Panel",
        ["Control"] = "Control",
        ["Border"] = "Border",
        ["Grid"] = "Grid",
        ["StackPanel"] = "StackPanel",
        ["RelativePanel"] = "RelativePanel",
        ["TextBlock"] = "TextBlock",
    };

    [Fact]
    public void Skill_Prose_Gate_List_Matches_The_Table()
    {
        var repoRoot = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(repoRoot);

        var relativeSkillPath = SkillPath.Replace('/', Path.DirectorySeparatorChar);

        // Path.Join, not Path.Combine: Combine silently discards repoRoot if the second argument is
        // rooted, which would make this fact read some unrelated file instead of failing. Join has
        // no such behavior. The assertion keeps the intent explicit either way.
        Assert.False(
            Path.IsPathRooted(relativeSkillPath),
            $"SkillPath must stay repo-relative; '{relativeSkillPath}' is rooted.");

        var file = Path.Join(repoRoot!, relativeSkillPath);
        Assert.True(File.Exists(file), $"{SkillPath} not found at {file}");

        var text = File.ReadAllText(file);

        // The clause runs from "Gates:" to the end of that sentence.
        var clause = Regex.Match(text, @"Gates:\s*(?<body>[^.|]+)");
        Assert.True(
            clause.Success,
            $"No 'Gates: …' clause found in {SkillPath}. The REACTOR_MOD_003 row used to state the " +
            "receiver gates in prose; if that was intentionally removed, delete this fact with it — " +
            "do not leave a parity gate that silently measures nothing.");

        // "Background → Panel/Control/Border; Foreground and fonts → Control/TextBlock; …"
        var stated = new Dictionary<string, ISet<string>>(StringComparer.Ordinal);
        var arrows = clause.Groups["body"].Value
            .Split(';')
            .Select(segment => segment.Split('→'))
            .Where(arrow => arrow.Length == 2);

        foreach (var arrow in arrows)
        {

            var types = new HashSet<string>(
                arrow[1].Split('/').Select(t => t.Trim()).Where(t => t.Length > 0),
                StringComparer.Ordinal);

            // Backticked property names on the left: `BorderBrush`/`BorderThickness`, and prose
            // like "Foreground and fonts" which the font properties are resolved from below.
            foreach (Match property in Regex.Matches(arrow[0], @"`(?<name>\w+)`"))
                stated[property.Groups["name"].Value] = types;

            if (arrow[0].Contains("fonts", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var font in new[] { "FontFamily", "FontSize", "FontWeight" })
                    stated[font] = types;
            }
        }

        // Floor: the five gate statements in that row cover 9 properties once fonts are expanded.
        // A parser that silently stopped matching reads the same as a table with no gates.
        Assert.True(
            stated.Count >= 9,
            $"Only {stated.Count} gate statements were parsed out of the {SkillPath} " +
            "REACTOR_MOD_003 row; expected at least 9. The parser has probably stopped matching the " +
            "prose shape — fix it rather than lowering this floor.");

        var problems = new List<string>();

        foreach (var (property, prose) in stated.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var unknown = prose.Where(t => !ProseTypeNames.ContainsKey(t)).ToList();
            if (unknown.Count > 0)
            {
                problems.Add(
                    $"{property}: the skill names receiver type(s) [{string.Join("|", unknown)}] that " +
                    "this fact does not know how to map to ModifierTable's spelling");
                continue;
            }

            if (!ModifierTable.Properties.TryGetValue(property, out var info))
            {
                problems.Add($"{property}: stated in the skill but absent from ModifierTable.Properties");
                continue;
            }

            if (info.ControlGate is not { } gate)
            {
                problems.Add(
                    $"{property}: the skill states a gate but ModifierInfo.ControlGate is null, so " +
                    "ApplyModifiers writes it unconditionally");
                continue;
            }

            var expected = new HashSet<string>(prose.Select(t => ProseTypeNames[t]), StringComparer.Ordinal);
            if (!expected.SetEquals(gate))
            {
                problems.Add(
                    $"{property}: the skill says [{Join(expected)}] but ModifierTable's controlGate is " +
                    $"[{Join(gate)}]");
            }
        }

        Assert.True(
            problems.Count == 0,
            $"The REACTOR_MOD_003 gate prose in {SkillPath} has drifted from ModifierTable. That row " +
            "ships to end users as agent guidance, so a stale gate there is advice to write code the " +
            "analyzer will reject:\n  " + string.Join("\n  ", problems));
    }

    private static string Join(IEnumerable<string> values) =>
        string.Join("|", values.OrderBy(value => value, StringComparer.Ordinal));
}
