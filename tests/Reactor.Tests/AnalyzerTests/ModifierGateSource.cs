using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// One <c>controlGate:</c> / <c>poolResetGate:</c> argument exactly as written in
/// <c>ModifierTable.Properties</c> — the ground truth a text matcher is measured against.
/// </summary>
internal readonly record struct ModifierGateSlot(string Property, string Slot, string Identifier);

/// <summary>
/// Reads gate facts out of <c>src/Reactor.Analyzers/ModifierTable.cs</c> <em>as text</em>, and
/// supplies the one matcher that is safe to do it with.
/// </summary>
/// <remarks>
/// <para>
/// The gate groups are named by concatenating their receiver list, so a gate that is a superset of
/// another <b>necessarily</b> has a name that is a superset string —
/// <c>ControlBorder</c> ⊏ <c>ControlBorderGridStack</c> ⊏ <c>ControlBorderGridStackRelative</c> ⊏
/// <c>…RelativeText</c>. Any check that asserts "modifier X has gate Y" by substring therefore
/// passes when X carries a different, <em>wider</em> gate: a silent false PASS on precisely the
/// assertion meant to catch a mis-widened gate. Issue #1062.
/// </para>
/// <para>
/// <b>Prefer the typed property.</b> <see cref="ModifierInfo.ControlGate"/> is the authority and
/// comparing its <em>contents</em> is immune to the naming structure — that is what
/// <see cref="ModifierTableIntegrityTests"/> does, and why those 20 facts were never exposed.
/// Reach for this reader only when the artifact really is text (a <c>mur check</c> rule, a docs or
/// prose-parity gate, a review script), and then match with
/// <see cref="AnchoredPattern(string, string)"/>, never a bare <c>Contains</c>.
/// </para>
/// <para>
/// Roslyn supplies the entry boundaries rather than a line scan: an entry's extent is a brace-depth
/// question, and guessing at it textually would make the reader fail on reformatting instead of on
/// real drift. The <em>matching decision</em> stays textual on purpose — being a text matcher is the
/// thing under test.
/// </para>
/// </remarks>
internal static class ModifierGateSource
{
    public const string ControlGate = "controlGate";
    public const string PoolResetGate = "poolResetGate";

    /// <summary>Matches either gate slot, as the fix suggested on issue #1062 is written.</summary>
    public const string AnySlot = "(?:" + ControlGate + "|" + PoolResetGate + ")";

    /// <summary>The two real slot names, for tests that sweep both.</summary>
    public static IReadOnlyList<string> SlotNames { get; } = new[] { ControlGate, PoolResetGate };

    private static readonly Lazy<TableModel> Model = new(Load);

    /// <summary>
    /// Every <c>private static readonly string[] NAME = { … }</c> group in the table, mapped to the
    /// type names it declares. Includes groups used only as <c>elementTypes</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> DeclaredGroups => Model.Value.Groups;

    /// <summary>Every gate argument in the table, read off the syntax tree.</summary>
    public static IReadOnlyList<ModifierGateSlot> GateSlots => Model.Value.Slots;

    /// <summary>Property name → the exact source text of that entry in <c>Properties</c>.</summary>
    public static IReadOnlyDictionary<string, string> EntryText => Model.Value.Entries;

    /// <summary>
    /// The safe matcher: the gate identifier bounded by the delimiters the C# syntax already
    /// guarantees, so a superset name cannot satisfy a subset's pattern.
    /// </summary>
    /// <param name="slot">
    /// <see cref="ControlGate"/>, <see cref="PoolResetGate"/>, or <see cref="AnySlot"/>.
    /// </param>
    public static string AnchoredPattern(string slot, string identifier) =>
        slot + @":\s*" + Regex.Escape(identifier) + @"\s*[,)]";

    /// <summary>Properties whose entry carries <paramref name="identifier"/> in that slot.</summary>
    public static ISet<string> MatchAnchored(string slot, string identifier)
    {
        var pattern = AnchoredPattern(slot, identifier);
        return Select(text => Regex.IsMatch(text, pattern));
    }

    /// <summary>
    /// The charitable naive matcher — slot-aware, but with no closing delimiter. Used only to
    /// demonstrate the hazard in <see cref="ModifierGateIdentifierTests"/>; never to assert a fact
    /// about the table.
    /// </summary>
    public static ISet<string> MatchNaive(string slot, string identifier)
    {
        Assert.Contains(slot, SlotNames);
        var needle = slot + ": " + identifier;
        return Select(text => text.Contains(needle, StringComparison.Ordinal));
    }

    /// <summary>
    /// The bare <c>Contains</c> the issue reports on — slot-blind, so it also accepts a name that
    /// merely <em>contains</em> the identifier without starting with it
    /// (<c>PanelControlBorder</c> ⊃ <c>ControlBorder</c>).
    /// </summary>
    public static ISet<string> MatchNaiveBare(string identifier) =>
        Select(text => text.Contains(identifier, StringComparison.Ordinal));

    private static ISet<string> Select(Func<string, bool> predicate) =>
        new HashSet<string>(
            EntryText.Where(entry => predicate(entry.Value)).Select(entry => entry.Key),
            StringComparer.Ordinal);

    private sealed record TableModel(
        IReadOnlyDictionary<string, string[]> Groups,
        IReadOnlyList<ModifierGateSlot> Slots,
        IReadOnlyDictionary<string, string> Entries);

    private static TableModel Load()
    {
        var repoRoot = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(repoRoot);
        var file = Path.Join(repoRoot!, "src", "Reactor.Analyzers", "ModifierTable.cs");
        Assert.True(File.Exists(file), $"ModifierTable.cs not found at {file}");

        var table = CSharpSyntaxTree.ParseText(File.ReadAllText(file))
            .GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == "ModifierTable");

        Assert.True(table is not null, $"No 'ModifierTable' class declaration found in {file}");

        var groups = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var field in table!.Members.OfType<FieldDeclarationSyntax>())
        {
            if (field.Declaration.Type is not ArrayTypeSyntax { ElementType: PredefinedTypeSyntax element }
                || element.Keyword.Text != "string")
            {
                continue;
            }

            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not InitializerExpressionSyntax initializer)
                    continue;

                var types = initializer.Expressions
                    .OfType<LiteralExpressionSyntax>()
                    .Select(literal => literal.Token.ValueText)
                    .ToArray();

                if (types.Length > 0)
                    groups[variable.Identifier.Text] = types;
            }
        }

        var properties = table.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .FirstOrDefault(variable => variable.Identifier.Text == "Properties");

        Assert.True(properties is not null, "No 'Properties' field found on ModifierTable");
        Assert.True(
            properties!.Initializer?.Value is ObjectCreationExpressionSyntax { Initializer: not null },
            "ModifierTable.Properties is no longer a collection-initialized dictionary; the entry " +
            "reader needs updating.");

        var entriesSyntax = ((ObjectCreationExpressionSyntax)properties.Initializer!.Value).Initializer!;

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var slots = new List<ModifierGateSlot>();

        foreach (var entry in entriesSyntax.Expressions.OfType<InitializerExpressionSyntax>())
        {
            if (entry.Expressions.Count != 2
                || entry.Expressions[0] is not LiteralExpressionSyntax key)
            {
                continue;
            }

            var property = key.Token.ValueText;
            entries[property] = entry.ToString();

            if (entry.Expressions[1] is not ObjectCreationExpressionSyntax { ArgumentList: not null } info)
                continue;

            foreach (var argument in info.ArgumentList.Arguments)
            {
                var slot = argument.NameColon?.Name.Identifier.Text;
                if (slot is not (ControlGate or PoolResetGate))
                    continue;

                if (argument.Expression is IdentifierNameSyntax identifier)
                    slots.Add(new ModifierGateSlot(property, slot, identifier.Identifier.Text));
            }
        }

        return new TableModel(groups, slots, entries);
    }
}
