using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Analyzers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Guards the one way a check is allowed to read <see cref="ModifierTable"/>'s receiver gates as
/// text. Issue #1062.
/// </summary>
/// <remarks>
/// <para>
/// The gate groups are named by concatenating their receiver list, which makes <b>set inclusion
/// equivalent to string prefixing</b>: <c>ControlBorder</c> ⊏ <c>ControlBorderGridStack</c> ⊏
/// <c>ControlBorderGridStackRelative</c> ⊏ <c>…RelativeText</c>. A superset gate cannot help but
/// have a superset name, so the collision set grows with every gate added and a fix applied to one
/// pair falls behind on the next.
/// </para>
/// <para>
/// <see cref="ModifierTableIntegrityTests"/> is immune to all of it, because it reads the typed
/// <see cref="ModifierInfo.ControlGate"/> and compares type <em>sets</em> — which is the pattern to
/// copy. These facts exist for the consumer that has no such option: a <c>mur check</c> rule, a
/// docs or prose-parity gate, a review script. For those, matching must be anchored on the
/// delimiter the C# syntax already guarantees, and this pins that matcher to the typed authority
/// so it cannot quietly regress to a substring.
/// </para>
/// </remarks>
public class ModifierGateIdentifierTests
{
    /// <summary>
    /// Positive control for the reader itself: a broken reader and a table with no gates read the
    /// same on screen, so pin the syntax reader to the typed table in both directions before
    /// anything else measures with it.
    /// </summary>
    [Fact]
    public void Gate_Reader_Agrees_With_The_Typed_Table()
    {
        var groups = ModifierGateSource.DeclaredGroups;
        var slots = ModifierGateSource.GateSlots;

        // Floors measured on this table: 9 declared string[] groups, 11 gate arguments across
        // 7 distinct identifiers. They are a non-vacuity guard, not a target — raise them when the
        // table grows, never lower them to make a stalled reader pass.
        Assert.True(
            groups.Count >= 9,
            $"Only {groups.Count} string[] gate groups were read out of ModifierTable.cs; expected at " +
            "least 9. The declaration reader has probably stopped matching — fix it rather than " +
            "lowering this floor.");
        Assert.True(
            slots.Count >= 11,
            $"Only {slots.Count} controlGate/poolResetGate arguments were read out of " +
            "ModifierTable.Properties; expected at least 11. The entry reader has probably stopped " +
            "matching — fix it rather than lowering this floor.");
        Assert.True(
            slots.Select(slot => slot.Identifier).Distinct(StringComparer.Ordinal).Count() >= 7,
            "Fewer than 7 distinct gate identifiers were read; the reader has probably stopped " +
            "matching.");

        // Set-equality is how a name is resolved back to a gate everywhere below, so it has to
        // identify one unambiguously.
        var ambiguous = (from a in groups
                         from b in groups
                         where StringComparer.Ordinal.Compare(a.Key, b.Key) < 0
                         where SameSet(a.Value, b.Value)
                         select $"{a.Key} and {b.Key} declare the same types").ToList();

        Assert.True(
            ambiguous.Count == 0,
            "Two gate groups now declare identical type sets, so a gate can no longer be resolved " +
            "from its contents and the matcher tests below would compare against the wrong one. " +
            "Merge them, or give this file a different identity function:\n  " +
            string.Join("\n  ", ambiguous));

        var problems = new List<string>();

        foreach (var slot in slots)
        {
            if (!groups.TryGetValue(slot.Identifier, out var declared))
            {
                problems.Add(
                    $"{slot.Property}: {slot.Slot} names '{slot.Identifier}', which is not a declared " +
                    "string[] group in ModifierTable");
                continue;
            }

            var typed = TypedGate(slot.Property, slot.Slot);
            if (typed is null)
            {
                problems.Add(
                    $"{slot.Property}: the source has {slot.Slot}: {slot.Identifier}, but the typed " +
                    $"ModifierInfo.{TypedMemberName(slot.Slot)} is null");
            }
            else if (!SameSet(typed, declared))
            {
                problems.Add(
                    $"{slot.Property}: {slot.Slot} names '{slot.Identifier}' " +
                    $"([{string.Join("|", declared)}]) but the typed value is " +
                    $"[{string.Join("|", typed)}]");
            }
        }

        // The other direction: a gate the typed table carries but the reader did not see.
        foreach (var property in ModifierTable.Properties.Keys)
        {
            var typedSlots = ModifierGateSource.SlotNames
                .Where(slotName => TypedGate(property, slotName) is not null);

            foreach (var slotName in typedSlots)
            {
                if (slots.Any(slot => slot.Property == property && slot.Slot == slotName))
                    continue;

                problems.Add(
                    $"{property}: the typed ModifierInfo.{TypedMemberName(slotName)} is set, but the " +
                    $"reader found no '{slotName}:' argument for it — the entry is probably written " +
                    "in a shape the reader does not handle (a positional argument, or a group built " +
                    "inline).");
            }
        }

        Assert.True(
            problems.Count == 0,
            "The ModifierTable.cs gate reader has drifted from the typed table it is supposed to " +
            "mirror, so every text-matching fact below would be measuring the wrong thing:\n  " +
            string.Join("\n  ", problems));
    }

    /// <summary>
    /// The invariant: text matching, done with the delimiter, agrees exactly with the typed gate.
    /// </summary>
    [Fact]
    public void Boundary_Matching_Selects_Exactly_The_Properties_The_Typed_Gate_Names()
    {
        var problems = new List<string>();
        var selected = 0;

        foreach (var (name, types) in ModifierGateSource.DeclaredGroups
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var union = new HashSet<string>(StringComparer.Ordinal);

            foreach (var slot in ModifierGateSource.SlotNames)
            {
                var matched = ModifierGateSource.MatchAnchored(slot, name);
                var typed = PropertiesCarrying(slot, types);
                selected += matched.Count;
                union.UnionWith(typed);

                if (!matched.SetEquals(typed))
                {
                    problems.Add(
                        $"{slot} '{name}': anchored matching selects " +
                        $"[{Join(matched)}] but the typed gate is carried by [{Join(typed)}]");
                }
            }

            // The alternation the issue's suggested fix is written with, over both slots at once.
            var either = ModifierGateSource.MatchAnchored(ModifierGateSource.AnySlot, name);
            if (!either.SetEquals(union))
            {
                problems.Add(
                    $"either-slot '{name}': anchored matching selects [{Join(either)}] but the typed " +
                    $"gate is carried by [{Join(union)}]");
            }
        }

        Assert.True(
            problems.Count == 0,
            "Anchored gate matching disagrees with ModifierTable's typed ControlGate/PoolResetGate, " +
            "which is the authority. Either the matcher is wrong, or an entry is written in a shape " +
            "it cannot see (a positional argument, a trailing comment inside the call):\n  " +
            string.Join("\n  ", problems));

        // Every real gate argument must be reachable by the matcher, or the agreement above could
        // be two empty sets agreeing with each other.
        Assert.True(
            selected == ModifierGateSource.GateSlots.Count,
            $"Anchored matching selected {selected} gate slots but the syntax tree has " +
            $"{ModifierGateSource.GateSlots.Count}. The matcher is missing real slots (or matching " +
            "text outside the table), so its agreement with the typed gate above is not a measurement.");
    }

    /// <summary>
    /// The differential oracle: on the real collision pairs, substring matching accepts a property
    /// whose gate is <em>wider</em> than the one asked about, and anchored matching does not.
    /// </summary>
    /// <remarks>
    /// Relax <c>ModifierGateSource.AnchoredPattern</c> to a substring and the first assertion
    /// reddens — that is what makes it a guard rather than a restatement. The two floors under it
    /// keep the guard from passing vacuously once the naming stops colliding, and
    /// <see cref="Hazard_Matchers_Select_Exactly_What_The_Syntax_Model_Predicts"/> keeps the
    /// matchers those floors are measured with honest.
    /// </remarks>
    [Fact]
    public void Substring_Matching_Accepts_A_Wider_Gate_Where_Boundary_Matching_Does_Not()
    {
        var identifiers = ModifierGateSource.GateSlots
            .Select(slot => slot.Identifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var anchoredFalseAccepts = new List<string>();
        var prefixFalseAccepts = new List<string>();
        var substringOnlyFalseAccepts = new List<string>();

        foreach (var narrow in identifiers)
        {
            foreach (var wide in identifiers)
            {
                if (StringComparer.Ordinal.Equals(narrow, wide)
                    || !wide.Contains(narrow, StringComparison.Ordinal))
                {
                    continue;
                }

                // `PanelControlBorder` contains `ControlBorder` without starting with it, so it is
                // invisible to a prefix-only fix but not to a bare Contains. The issue enumerates
                // both classes; both are floored below.
                var isPrefix = wide.StartsWith(narrow, StringComparison.Ordinal);

                foreach (var slot in ModifierGateSource.SlotNames)
                {
                    // Properties carrying the WIDE gate in this slot. A check asking "does this
                    // property have the NARROW gate?" must answer no for every one of them.
                    var exposed = PropertiesCarrying(slot, ModifierGateSource.DeclaredGroups[wide])
                        .Except(
                            PropertiesCarrying(slot, ModifierGateSource.DeclaredGroups[narrow]),
                            StringComparer.Ordinal)
                        .ToList();

                    if (exposed.Count == 0)
                        continue;

                    var anchored = ModifierGateSource.MatchAnchored(slot, narrow);
                    var naive = ModifierGateSource.Hazard.SlotPrefixed(slot, narrow);
                    var bare = ModifierGateSource.Hazard.Bare(narrow);

                    foreach (var property in exposed)
                    {
                        var where = $"{property}/{slot}: asked for '{narrow}', actually carries '{wide}'";

                        if (anchored.Contains(property))
                            anchoredFalseAccepts.Add(where);

                        if (isPrefix && naive.Contains(property))
                            prefixFalseAccepts.Add(where);

                        if (!isPrefix && bare.Contains(property))
                            substringOnlyFalseAccepts.Add(where);
                    }
                }
            }
        }

        Assert.True(
            anchoredFalseAccepts.Count == 0,
            "Anchored gate matching accepted a property whose gate is WIDER than the one asked " +
            "about — the exact false PASS issue #1062 is about, now in the matcher that is supposed " +
            "to prevent it:\n  " + string.Join("\n  ", anchoredFalseAccepts));

        Assert.True(
            prefixFalseAccepts.Count > 0,
            "No gate identifier is a strict prefix of another any more, so the assertion above " +
            "passes without being tested and this guard has outlived its reason. If the cumulative " +
            "naming was deliberately abandoned, retire these facts with it — do not leave a matcher " +
            "guard that measures nothing.");

        Assert.True(
            substringOnlyFalseAccepts.Count > 0,
            "No gate identifier CONTAINS another without also starting with it (it was " +
            "PanelControlBorder ⊃ ControlBorder), so the harder half of issue #1062 — the one a " +
            "prefix-only fix misses — is no longer exercised. Confirm that is deliberate before " +
            "dropping this floor.");
    }

    /// <summary>
    /// Positive control for the hazard matchers themselves: they must select exactly what the
    /// syntax model predicts, not merely "at least the properties fact C looks at".
    /// </summary>
    /// <remarks>
    /// Fact C only inspects the sites where a false accept is expected, so a hazard matcher that
    /// stopped discriminating — <c>Select(_ =&gt; true)</c> is the degenerate case — would satisfy
    /// its floors while proving nothing. A broken instrument is trusted by default, so pin it:
    /// the expectation here is computed from the Roslyn argument list, independently of the text
    /// matching under test.
    /// </remarks>
    [Fact]
    public void Hazard_Matchers_Select_Exactly_What_The_Syntax_Model_Predicts()
    {
        var identifiers = ModifierGateSource.GateSlots
            .Select(slot => slot.Identifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var problems = new List<string>();

        foreach (var identifier in identifiers)
        {
            foreach (var slot in ModifierGateSource.SlotNames)
            {
                // The needle is the slot and identifier with no closing delimiter, so it accepts any
                // gate name in that slot that STARTS WITH the identifier — the hazard, stated
                // positively. Whitespace between the two is tolerated, so this prediction holds
                // regardless of how the table is formatted.
                var predicted = PropertiesWhere(argument =>
                    argument.Slot == slot
                    && argument.Identifier.StartsWith(identifier, StringComparison.Ordinal));

                var actual = ModifierGateSource.Hazard.SlotPrefixed(slot, identifier);

                if (!actual.SetEquals(predicted))
                {
                    problems.Add(
                        $"Hazard.SlotPrefixed({slot}, '{identifier}') selected [{Join(actual)}] but the " +
                        $"argument list predicts [{Join(predicted)}]");
                }
            }

            // Slot-blind: any referenced group name that contains the identifier anywhere, in any
            // argument — including elementTypes, which the gate slots exclude.
            var predictedBare = PropertiesWhere(argument =>
                argument.Identifier.Contains(identifier, StringComparison.Ordinal));

            var actualBare = ModifierGateSource.Hazard.Bare(identifier);

            if (!actualBare.SetEquals(predictedBare))
            {
                problems.Add(
                    $"Hazard.Bare('{identifier}') selected [{Join(actualBare)}] but the argument list " +
                    $"predicts [{Join(predictedBare)}]");
            }
        }

        Assert.True(
            problems.Count == 0,
            "A hazard matcher no longer selects what ModifierTable's argument list predicts, so the " +
            "non-vacuity floors in " +
            nameof(Substring_Matching_Accepts_A_Wider_Gate_Where_Boundary_Matching_Does_Not) +
            " are not measuring the hazard they claim to. Both matchers are whitespace-tolerant, so " +
            "reformatting the table is not an explanation — a matcher is broken:\n  " +
            string.Join("\n  ", problems));

        // Negative control: an identifier that appears nowhere must select nothing in all three
        // matchers. Without this, a matcher that returns everything could still agree above if the
        // prediction were equally broken.
        const string Absent = "NoSuchGateIdentifier1062";
        Assert.Empty(ModifierGateSource.Hazard.Bare(Absent));
        foreach (var slot in ModifierGateSource.SlotNames)
        {
            Assert.Empty(ModifierGateSource.Hazard.SlotPrefixed(slot, Absent));
            Assert.Empty(ModifierGateSource.MatchAnchored(slot, Absent));
        }
    }

    /// <summary>Properties with at least one entry argument satisfying <paramref name="predicate"/>.</summary>
    private static ISet<string> PropertiesWhere(Func<ModifierGateArgument, bool> predicate) =>
        new HashSet<string>(
            ModifierGateSource.EntryArguments
                .Where(entry => entry.Value.Any(predicate))
                .Select(entry => entry.Key),
            StringComparer.Ordinal);

    private static string TypedMemberName(string slot) =>
        slot == ModifierGateSource.ControlGate ? nameof(ModifierInfo.ControlGate) : nameof(ModifierInfo.PoolResetGate);

    private static string[]? TypedGate(string property, string slot) =>
        !ModifierTable.Properties.TryGetValue(property, out var info) ? null
            : slot == ModifierGateSource.ControlGate ? info.ControlGate
            : info.PoolResetGate;

    /// <summary>Properties whose typed gate in <paramref name="slot"/> is exactly these types.</summary>
    private static ISet<string> PropertiesCarrying(string slot, IReadOnlyList<string> types) =>
        new HashSet<string>(
            ModifierTable.Properties
                .Where(entry => TypedGate(entry.Key, slot) is { } gate && SameSet(gate, types))
                .Select(entry => entry.Key),
            StringComparer.Ordinal);

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right) =>
        new HashSet<string>(left, StringComparer.Ordinal).SetEquals(right);

    private static string Join(IEnumerable<string> values) =>
        string.Join("|", values.OrderBy(value => value, StringComparer.Ordinal));
}
