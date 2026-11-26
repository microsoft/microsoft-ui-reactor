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
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: resolves the receiver and DP-owner type names the schema declares with Assembly.GetType. JIT-only host, never trimmed.")]
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
    public void Declarations_Are_Internally_Consistent()
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

            // Each shape carries the data a generator needs for it.
            switch (slot.Shape)
            {
                case ApplyShape.Converted:
                    Assert.True(slot.Converter is not null, $"{slot.Name}: a Converted slot needs a Converter.");
                    break;
                case ApplyShape.Attached:
                    Assert.True(slot.AttachedOwner is not null, $"{slot.Name}: an Attached slot needs an AttachedOwner.");
                    break;
                case ApplyShape.Composite:
                    Assert.True(slot.CompositeFamily is not null && slot.HandCodedMethod is not null, $"{slot.Name}: a Composite slot needs its family and method.");
                    break;
                case ApplyShape.HandCoded:
                    Assert.True(slot.HandCodedMethod is not null, $"{slot.Name}: a HandCoded slot needs its method.");
                    break;
                case ApplyShape.ReferenceEdge:
                    Assert.Equal("ApplyModifierReferenceEdges", slot.HandCodedMethod);
                    break;
                case ApplyShape.Behavior:
                    Assert.True(slot.Receivers.Count == 0, $"{slot.Name}: a Behavior slot writes no property, so it has no receivers.");
                    break;
            }
            if (slot.Shape is ApplyShape.Simple or ApplyShape.Converted or ApplyShape.Attached or ApplyShape.Composite)
                Assert.True(slot.ReceiversWith(ReceiverRole.Apply).Any(), $"{slot.Name}: a {slot.Shape} slot needs at least one Apply receiver.");
            if (slot.Converter is not null) Assert.Equal(ApplyShape.Converted, slot.Shape);
        }

        // Slots of one composite family write the same DP on the same receivers.
        foreach (var family in ModifierSchema.Slots.Where(s => s.CompositeFamily is not null).GroupBy(s => s.CompositeFamily))
        {
            var lists = family.Select(s => string.Join(",", s.Receivers.Select(r => r.Type + "." + r.Property))).Distinct().ToList();
            Assert.True(lists.Count == 1, $"Composite family {family.Key} has differing receivers: {string.Join(" / ", lists)}");
        }
    }

    [Fact]
    public void Behavior_Slots_Write_No_Property_The_Reconciler_Gates()
    {
        // A field the reconciler writes behind a type gate is a property write; tagging it Behavior
        // would exempt it from the order and fluent checks below.
        var gates = ModifierTableIntegrityTests.ReadApplyModifierControlGates();
        var mislabelled = ModifierSchema.Slots.Where(s => s.Shape == ApplyShape.Behavior && gates.ContainsKey(s.Name)).Select(s => s.Name);
        Assert.Empty(mislabelled);

        // In the frozen order, the only non-marker entries after the handler wiring are lifecycle/ref slots.
        var order = ModifierApplyOrderTests.FrozenOrder;
        var afterHandlers = order.Skip(Array.IndexOf(order, "→ModifierEvents.ApplyEventHandlers")).Where(o => !o.StartsWith('→'));
        Assert.All(afterHandlers, name => Assert.Equal(ApplyShape.Behavior, ModifierSchema.Find(name)!.Shape));
    }

    [Fact]
    public void Write_Equality_Matches_The_Apply_Arm_Guards()
    {
        // Brush and FontFamily arms guard with ReferenceEquals (spec 067 §6.6); everything else
        // with value comparison. Read the guards out of the apply family, the accessibility arms
        // and ForegroundModifier.Apply (#1687), following locals seeded from `m.X`.
        var root = Microsoft.UI.Reactor.Cli.Pack.RepoRootFinder.FindRepoRoot()!;
        var foreground = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
            .ParseText(global::System.IO.File.ReadAllText(global::System.IO.Path.Join(root, "src", "Reactor", "Core", "ForegroundModifier.cs")),
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.Text == "Apply");
        var methods = ReconcilerSource.ApplyModifiersFamily()
            .Concat(ReconcilerSource.Methods("ApplyAccessibilityModifiers"))
            .Concat(foreground);

        static string? Field(ExpressionSyntax e) =>
            e is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "m" } } access ? access.Name.Identifier.Text : null;

        var byReference = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var locals = method.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer is not null && Field(v.Initializer.Value) is not null)
                .ToDictionary(v => v.Identifier.Text, v => Field(v.Initializer!.Value)!, StringComparer.Ordinal);
            foreach (var argument in method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                         .Where(i => ReconcilerSource.InvokedName(i) == "ReferenceEquals")
                         .SelectMany(i => i.ArgumentList.Arguments))
            {
                var name = Field(argument.Expression)
                    ?? (argument.Expression is IdentifierNameSyntax id && locals.TryGetValue(id.Identifier.Text, out var seeded) ? seeded : null);
                if (name is not null) byReference.Add(name);
            }
        }
        Assert.Contains("Background", byReference);   // positive controls: direct, and through a local
        Assert.Contains("Foreground", byReference);

        // Arms that always reconcile (flyouts, rich tooltip) have no change guard, and behaviour /
        // reference-edge slots are not property arms.
        var guarded = ModifierSchema.Slots.Where(s =>
            s.Shape is not (ApplyShape.Behavior or ApplyShape.ReferenceEdge) && s.Skip != SkipEquality.BlocksSkipWhenSet);
        foreach (var slot in guarded)
            Assert.True((slot.Write == WriteEquality.Reference) == byReference.Contains(slot.Name),
                $"{slot.Name}: schema Write is {slot.Write}, but the apply arm {(byReference.Contains(slot.Name) ? "uses" : "does not use")} ReferenceEquals.");
    }

    [Fact]
    public void Clr_Types_Match_The_Record_Properties()
    {
        foreach (var slot in ModifierSchema.Slots)
        {
            var record = slot.Bucket switch
            {
                ModifierBucket.Layout => typeof(LayoutModifiers),
                ModifierBucket.Visual => typeof(VisualModifiers),
                ModifierBucket.Accessibility => typeof(AccessibilityModifiers),
                _ => typeof(ElementModifiers),
            };
            var property = record.GetProperty(slot.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(property is not null, $"{slot.Name}: no such property on {record.Name}.");
            var type = Nullable.GetUnderlyingType(property!.PropertyType) ?? property.PropertyType;
            Assert.Equal(CSharpName(type), slot.ClrType);
        }
    }

    private static string CSharpName(Type type)
    {
        if (type == typeof(object)) return "object";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (!type.IsGenericType) return type.FullName!;
        var name = type.GetGenericTypeDefinition().FullName!;
        return name[..name.IndexOf('`')] + "<" + string.Join(", ", type.GetGenericArguments().Select(CSharpName)) + ">";
    }

    // ── Order (spec 067 §5) ─────────────────────────────────────────────

    [Fact]
    public void Slot_Order_Matches_The_Frozen_Apply_Order()
    {
        // Property slots of the main bag, in schema order, against the arms ApplyModifiers reads. A
        // handoff marker stands for the slot whose hand-coded method it calls
        // (→ForegroundModifier.Apply is the Foreground arm since #1687).
        var byMethod = ModifierSchema.Slots.Where(s => s.HandCodedMethod?.Contains('.') == true)
            .ToDictionary(s => "→" + s.HandCodedMethod, s => s.Name, StringComparer.Ordinal);
        var frozenMain = ModifierApplyOrderTests.FrozenOrder
            .Select(o => byMethod.TryGetValue(o, out var slot) ? slot : o)
            .Where(o => !o.StartsWith('→') && o != "Accessibility").ToList();
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
        var declared = ModifierTableIntegrityTests.DeclaredGenericModifiers.Value;
        Assert.Contains("Padding", declared);   // positive control for the reflection

        var missing = ModifierSchema.Slots
            .Where(s => s.Shape is not ApplyShape.Behavior)
            .SelectMany(s => s.FluentNames.Select(name => (s.Name, Fluent: name)))
            .Where(pair => !declared.Contains(pair.Fluent))
            .Select(pair => $"{pair.Name} → .{pair.Fluent}<T>(this T …)")
            .ToList();
        Assert.True(missing.Count == 0,
            "Slots whose fluent modifier doesn't exist as a generic extension. If the public modifier has a "
            + "different name, declare it with .Fluent(\"<Name>\", …) on the slot in ModifierSchema.cs; otherwise "
            + "add the generic extension:\n  " + string.Join("\n  ", missing));

        var nameless = ModifierSchema.Slots.Where(s => s.Shape is not ApplyShape.Behavior && s.FluentNames.Length == 0).Select(s => s.Name);
        Assert.Empty(nameless);
    }

    [Fact]
    public void Reference_Edge_Order_Matches_ApplyModifierReferenceEdges()
    {
        // First mention of each reference-edge field in the wiring method, in source order.
        var edges = ModifierSchema.Slots.Where(s => s.Shape == ApplyShape.ReferenceEdge).Select(s => s.Name).ToList();
        var body = ReconcilerSource.Methods("ApplyModifierReferenceEdges").Single();
        var wired = body.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .Where(edges.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(9, wired.Count);
        Assert.Equal(wired, edges);
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
