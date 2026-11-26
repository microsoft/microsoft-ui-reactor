using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Analyzers.Modifiers;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 §10.1, layer 1: the invariants <c>ModifierSchema</c> keeps about itself, each checked
/// against an independent source (reflection over the records and WinUI, or a Roslyn read of the
/// hand-written reconciler) rather than against another copy of the schema.
/// </summary>
/// <remarks>
/// Completeness, skip equality and merge are checked by <see cref="ModifierFieldContractTests"/>,
/// which reads its field table from the schema. The analyzer side is pinned by
/// <see cref="ModifierTableParitySnapshotTests"/>.
/// </remarks>
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: enumerates the Reactor and WinUI assemblies' exported types. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Test-only: reflects static members of the receiver and DP-owner types the schema names. JIT-only host, never trimmed.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: reflects static members of the receiver and DP-owner types the schema names. JIT-only host, never trimmed.")]
public class ModifierSchemaTests
{
    [Fact]
    public void Slot_Names_Are_Unique_Across_Buckets()
    {
        var duplicates = ModifierSchema.Slots.GroupBy(s => s.Name).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
        Assert.All(ModifierSchema.Slots, slot => Assert.Same(slot, ModifierSchema.Find(slot.Name)));
    }

    [Fact]
    public void Reasons_Are_Recorded_Where_Required()
    {
        foreach (var slot in ModifierSchema.Slots)
        {
            Assert.True((slot.Skip == SkipEquality.NotCompared) == !string.IsNullOrWhiteSpace(slot.SkipReason),
                $"{slot.Name}: a SkipReason is required exactly when Skip is NotCompared.");
            Assert.True(slot.GateOnlyInReconcilerReason is null || slot.GateOnlyInReconcilerReason.Trim().Length > 0,
                $"{slot.Name}: an empty GateOnlyInReconciler reason.");
            Assert.False(slot.GateOnlyInReconcilerReason is not null && slot.AnalyzerControlGate is not null,
                $"{slot.Name}: declares an analyzer control gate AND a GateOnlyInReconciler reason. The gate wins, so the reason is dead.");
            if (slot.TakesVisualAtMount)
                Assert.True(slot.RestorableOwner && slot.IdentityValue is not null, $"{slot.Name}: TakesVisualAtMount needs RestorableOwner and an IdentityValue.");
        }
    }

    // ── Order (spec 067 §5) ─────────────────────────────────────────────

    [Fact]
    public void Slot_Order_Matches_The_Frozen_Apply_Order()
    {
        // Property slots of the main bag, in schema order, against the arms ApplyModifiers reads.
        var frozenMain = ModifierApplyOrderTests.FrozenOrder.Where(o => !o.StartsWith('→') && o != "Accessibility").ToList();
        var schemaMain = ModifierSchema.Slots.Where(s => frozenMain.Contains(s.Name)).Select(s => s.Name).ToList();
        Assert.True(schemaMain.SequenceEqual(frozenMain),
            "ModifierSchema.Slots order differs from ApplyModifiers:\n  schema: " + string.Join(", ", schemaMain)
            + "\n  frozen: " + string.Join(", ", frozenMain));

        var frozenA11y = ModifierApplyOrderTests.FrozenAccessibilityOrder;
        var schemaA11y = ModifierSchema.Slots.Where(s => frozenA11y.Contains(s.Name)).Select(s => s.Name).ToList();
        Assert.True(schemaA11y.SequenceEqual(frozenA11y),
            "Accessibility slot order differs from ApplyAccessibilityModifiers:\n  schema: " + string.Join(", ", schemaA11y));

        // The accessibility arms sit where ApplyModifiers hands off to AccessibilityApplier.
        int Index(string name) => Array.FindIndex(ModifierSchema.Slots, s => s.Name == name);
        Assert.True(Index("XYFocusKeyboardNavigation") < Index(frozenA11y[0]) && Index(frozenA11y[^1]) < Index("FontFamily"));

        // Every property-writing slot is pinned by one of the two frozen lists, so none can move unnoticed.
        var unpinned = ModifierSchema.Slots
            .Where(s => s.Shape is not (ApplyShape.Behavior or ApplyShape.ReferenceEdge))
            .Where(s => !frozenMain.Contains(s.Name) && !frozenA11y.Contains(s.Name))
            .Select(s => s.Name)
            .ToList();
        Assert.True(unpinned.Count == 0, "Property slots with no position in a frozen apply order: " + string.Join(", ", unpinned));
    }

    // ── Receivers ───────────────────────────────────────────────────────

    [Fact]
    public void Every_Receiver_Type_And_Dependency_Property_Exists()
    {
        var problems = new List<string>();
        var checkedCount = 0;
        foreach (var slot in ModifierSchema.Slots)
        {
            foreach (var receiver in slot.Receivers)
            {
                checkedCount++;
                if (ResolveType(receiver.Type) is null)
                    problems.Add($"{slot.Name}: receiver type {receiver.Type} does not exist");
                if (ResolveType(receiver.DpOwner) is not { } owner)
                {
                    problems.Add($"{slot.Name}: DP owner {receiver.DpOwner} does not exist");
                    continue;
                }
                if (!HasDependencyProperty(owner, receiver.Property))
                    problems.Add($"{slot.Name}: {receiver.DpOwner} has no {receiver.Property}Property");
            }
        }
        Assert.True(checkedCount > 60, $"Only {checkedCount} receivers checked.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Apply_Receivers_Match_The_Type_Gates_ApplyModifiers_Writes_To()
    {
        // The reconciler is the runtime authority. Its gates are read with Roslyn
        // (ModifierTableIntegrityTests), independently of the schema.
        var gates = ModifierTableIntegrityTests.ReadApplyModifierControlGates();
        Assert.True(gates.Count >= 8, $"Only {gates.Count} gates read out of ApplyModifiers.");

        // The content-alignment pair is gated by a bare `if (fe is Control …)` the reader can't
        // attribute to a property (see ModifierTable.GateOnlyInReconciler's remarks).
        var unattributable = new HashSet<string>(StringComparer.Ordinal) { "HorizontalContentAlignment", "VerticalContentAlignment" };

        var problems = new List<string>();
        foreach (var slot in ModifierSchema.Slots.Where(s => s.Bucket != ModifierBucket.Accessibility))
        {
            var apply = slot.ReceiversWith(ReceiverRole.Apply).Select(r => r.SimpleType).ToHashSet(StringComparer.Ordinal);
            var gated = apply.Count > 0 && !apply.SetEquals(["FrameworkElement"]);
            if (gates.TryGetValue(slot.Name, out var actual))
            {
                if (!apply.SetEquals(actual))
                    problems.Add($"{slot.Name}: schema [{Join(apply)}], ApplyModifiers [{Join(actual)}]");
            }
            else if (gated && !unattributable.Contains(slot.Name))
            {
                problems.Add($"{slot.Name}: schema gates it on [{Join(apply)}] but ApplyModifiers has no type gate for it");
            }
        }
        foreach (var name in gates.Keys.Where(n => ModifierSchema.Find(n) is null))
            problems.Add($"{name}: ApplyModifiers gates it, but the schema has no slot");

        Assert.True(problems.Count == 0, "ModifierSchema receivers disagree with ApplyModifiers:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Theme_Receivers_Match_GetDependencyPropertyName()
    {
        var themeGates = ModifierTableIntegrityTests.ReadGetDependencyPropertyNameGates();
        Assert.True(themeGates.Count >= 3);
        themeGates.Remove("PaneBackground");   // SplitView's own property, not a common modifier

        var schema = ModifierSchema.Slots
            .Where(s => s.ReceiversWith(ReceiverRole.Theme).Any())
            .ToDictionary(s => s.Name, s => s.ReceiversWith(ReceiverRole.Theme).Select(r => r.SimpleType).ToHashSet(StringComparer.Ordinal));

        Assert.Equal(themeGates.Keys.Order(StringComparer.Ordinal), schema.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, actual) in themeGates)
            Assert.True(schema[name].SetEquals(actual), $"{name}: schema theme receivers [{Join(schema[name])}], GetDependencyPropertyName [{Join(actual)}]");
    }

    // ── Composition (spec 066) ──────────────────────────────────────────

    [Fact]
    public void Composition_Flags_Match_The_Reconciler()
    {
        // HasRestorableModifier's `Declares` reads `v.X.HasValue`; DeclaresTransform reads `m.X is { } …`.
        var restorable = ReconcilerSource.Methods("HasRestorableModifier").Single()
            .DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(a => a.Expression is IdentifierNameSyntax { Identifier.Text: "v" })
            .Select(a => a.Name.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);
        var transform = ReconcilerSource.Methods("DeclaresTransform").Single()
            .DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(a => a.Expression is IdentifierNameSyntax { Identifier.Text: "m" })
            .Select(a => a.Name.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(6, restorable.Count);
        Assert.Equal(5, transform.Count);
        Assert.Equal(restorable.Order(StringComparer.Ordinal), ModifierSchema.Slots.Where(s => s.RestorableOwner).Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal(transform.Order(StringComparer.Ordinal), ModifierSchema.Slots.Where(s => s.TakesVisualAtMount).Select(s => s.Name).Order(StringComparer.Ordinal));
    }

    // ── Fluent API (spec 067 §4.4) ──────────────────────────────────────

    [Fact]
    public void Every_Property_Slot_Has_Its_Generic_Fluent_Modifiers()
    {
        var declared = GenericFluentModifiers();
        Assert.Contains("Padding", declared);   // positive control for the reflection

        var missing = ModifierSchema.Slots
            .Where(s => s.Shape is not ApplyShape.Behavior)
            .SelectMany(s => s.FluentNames.Select(name => (s.Name, Fluent: name)))
            .Where(pair => !declared.Contains(pair.Fluent))
            .Select(pair => $"{pair.Name} → .{pair.Fluent}<T>(this T …)")
            .ToList();
        Assert.True(missing.Count == 0, "Slots whose fluent modifier doesn't exist as a generic extension:\n  " + string.Join("\n  ", missing));

        var nameless = ModifierSchema.Slots.Where(s => s.Shape is not ApplyShape.Behavior && s.FluentNames.Length == 0).Select(s => s.Name);
        Assert.Empty(nameless);
    }

    private static HashSet<string> GenericFluentModifiers()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(Element).Assembly.GetExportedTypes().Where(t => t.IsAbstract && t.IsSealed))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.IsGenericMethodDefinition) continue;
                var typeArguments = method.GetGenericArguments();
                var parameters = method.GetParameters();
                if (typeArguments.Length == 1 && parameters.Length > 0 && parameters[0].ParameterType == typeArguments[0])
                    names.Add(method.Name);
            }
        }
        return names;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static readonly Assembly[] SearchAssemblies =
    [
        typeof(Microsoft.UI.Xaml.FrameworkElement).Assembly,
        typeof(Element).Assembly,
    ];

    private static Type? ResolveType(string fullName) =>
        SearchAssemblies.Select(a => a.GetType(fullName)).FirstOrDefault(t => t is not null);

    private static bool HasDependencyProperty(Type owner, string property)
    {
        // DeclaredOnly: the owner must declare the identifier the reconciler names, not inherit it.
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var dp = typeof(Microsoft.UI.Xaml.DependencyProperty);
        return owner.GetProperty(property + "Property", Flags)?.PropertyType == dp
            || owner.GetField(property + "Property", Flags)?.FieldType == dp;
    }

    private static string Join(IEnumerable<string> values) => string.Join("|", values.Order(StringComparer.Ordinal));
}
