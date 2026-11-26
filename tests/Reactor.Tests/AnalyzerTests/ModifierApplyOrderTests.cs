using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 §5 / §10.1, layer 0: the order of the modifier arms in <c>ApplyModifiers</c> is
/// behaviour, so it is frozen here as a literal list before anything is generated. The list is
/// the first read of each modifier field (<c>m.X</c>) walking <c>ApplyModifiers</c> and its
/// family helpers in call order, with a <c>→Name</c> marker where the bag, or a local read from
/// it, is handed to a method outside the family (accessibility, events, gestures, drag-drop,
/// reference edges). <c>ApplyAccessibilityModifiers</c> gets its own frozen list.
/// </summary>
/// <remarks>
/// If you reorder arms on purpose, update the frozen list in the same PR so the change is
/// visible in review, and keep <see cref="Spec067_Section5_Constraints_Hold"/> green. Layer 1
/// checks the modifier schema's slot order against these lists.
/// </remarks>
public class ModifierApplyOrderTests
{
    internal static readonly string[] FrozenOrder =
    [
        "RequestedTheme",
        "Margin", "MarginInlineStart", "MarginInlineEnd",
        "Padding", "PaddingInlineStart", "PaddingInlineEnd",
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
        "Opacity", "Scale", "Rotation", "Translation", "TransformOrigin", "CenterPoint",
        "IsVisible",
        "RichToolTip", "ToolTip", "ToolTipPlacement", "AttachedFlyout", "ContextFlyout",
        "IsEnabled",
        "CornerRadius", "BorderBrush", "BorderThickness", "BorderInlineStart",
        "Background", "Foreground",
        "AutomationName", "AutomationId", "IsDragRegion", "ElementSoundMode", "HeadingLevel",
        "IsTabStop", "IsHitTestVisible", "TabIndex", "AccessKey", "XYFocusKeyboardNavigation",
        "Accessibility",
        "→ApplyAccessibilityModifiers",
        "FontFamily", "FontSize", "FontWeight",
        "→ApplyEventHandlers",
        "→ApplyGestureHandlers",
        "→ApplyDragDropHandlers",
        "OnMountAction", "OnUpdateAction", "OnUnmountAction",
        "Ref",
        "→ApplyModifierReferenceEdges",
    ];

    /// <summary>The arm order inside <c>AccessibilityApplier.ApplyAccessibilityModifiers</c> (bag <c>a</c>).</summary>
    internal static readonly string[] FrozenAccessibilityOrder =
    [
        "HelpText", "FullDescription", "LandmarkType", "AccessibilityView", "IsRequiredForForm",
        "LiveSetting", "PositionInSet", "SizeOfSet", "Level", "ItemStatus", "TabFocusNavigation",
        "LabeledBy",
    ];

    // Lifecycle and ref slots are the only non-marker entries allowed after the handler wiring.
    private static readonly string[] CallbackSlots = ["OnMountAction", "OnUpdateAction", "OnUnmountAction", "Ref"];

    [Fact]
    public void ApplyModifiers_Arm_Order_Matches_The_Frozen_List()
    {
        var actual = ReadApplyOrder();
        Assert.True(actual.SequenceEqual(FrozenOrder), OrderChanged(nameof(FrozenOrder), actual));
    }

    [Fact]
    public void ApplyAccessibilityModifiers_Arm_Order_Matches_The_Frozen_List()
    {
        var actual = ReadAccessibilityApplyOrder();
        Assert.True(actual.SequenceEqual(FrozenAccessibilityOrder), OrderChanged(nameof(FrozenAccessibilityOrder), actual));
    }

    private static string OrderChanged(string list, List<string> actual) =>
        $"Modifier arm order changed. If that is intentional, update {nameof(ModifierApplyOrderTests)}.{list} "
        + $"and check {nameof(Spec067_Section5_Constraints_Hold)} still passes. Actual:\n"
        + string.Join(",\n", actual.Select(a => $"\"{a}\""));

    [Fact]
    public void Spec067_Section5_Constraints_Hold()
    {
        var order = ReadApplyOrder();
        int At(string name)
        {
            var i = order.IndexOf(name);
            Assert.True(i >= 0, $"'{name}' not found in the ApplyModifiers walk: [{string.Join(", ", order)}]");
            return i;
        }

        // RequestedTheme first: ThemeRef bindings and theme-resolved writes depend on it.
        Assert.Equal("RequestedTheme", order[0]);
        // TransformOrigin before CenterPoint on apply (spec 066 §8.2).
        Assert.True(At("TransformOrigin") < At("CenterPoint"));
        // Event, gesture and drag-drop wiring after every property write, accessibility included.
        var handlers = At("→ApplyEventHandlers");
        var propertyWrites = order.Where(o => !o.StartsWith('→') && !CallbackSlots.Contains(o)).ToList();
        Assert.True(propertyWrites.Count > 40, $"Only {propertyWrites.Count} property writes found.");
        var afterHandlers = propertyWrites.Where(o => At(o) > handlers).ToList();
        Assert.True(afterHandlers.Count == 0, $"Property writes after the handler wiring: [{string.Join(", ", afterHandlers)}]");
        Assert.True(At("→ApplyAccessibilityModifiers") < handlers);
        Assert.True(handlers < At("→ApplyGestureHandlers"));
        Assert.True(At("→ApplyGestureHandlers") < At("→ApplyDragDropHandlers"));
        // OnMount / OnUpdate after every property write and the handler wiring.
        Assert.True(At("→ApplyDragDropHandlers") < At("OnMountAction"));
        Assert.True(At("→ApplyDragDropHandlers") < At("OnUpdateAction"));
        // Ref, then reference edges last.
        Assert.True(At("OnMountAction") < At("Ref") && At("OnUpdateAction") < At("Ref"));
        Assert.Equal("→ApplyModifierReferenceEdges", order[^1]);
    }

    [Fact]
    public void The_Walk_Sees_Every_Family_Helper()
    {
        // Positive control: every family helper contributes at least one field, so an empty or
        // short walk can't pass the frozen comparison by accident.
        var family = ReconcilerSource.ApplyModifiersFamily();
        Assert.True(family.Count >= 10, $"Only {family.Count} family methods found.");
        foreach (var helper in family.Where(h => h.Identifier.Text != ReconcilerSource.ApplyModifiersName))
            Assert.True(ReadFields(helper, "m").Count > 0, $"{helper.Identifier.Text} reads no m.X field.");
        Assert.True(ReadAccessibilityApplyOrder().Count >= 12);
    }

    internal static List<string> ReadApplyOrder()
    {
        // ApplyModifiers has a mount overload without oldM that forwards to the main one.
        var family = ReconcilerSource.ApplyModifiersFamily()
            .Where(m => m.ParameterList.Parameters.Any(p => p.Identifier.Text == "oldM"))
            .GroupBy(m => m.Identifier.Text)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var order = new List<string>();
        Walk(family[ReconcilerSource.ApplyModifiersName], "m", family, order, new HashSet<string>(StringComparer.Ordinal));
        return order;
    }

    internal static List<string> ReadAccessibilityApplyOrder() =>
        ReadFields(ReconcilerSource.Methods("ApplyAccessibilityModifiers").Single(), "a");

    private static List<string> ReadFields(MethodDeclarationSyntax method, string bag)
    {
        var order = new List<string>();
        Walk(method, bag, new Dictionary<string, MethodDeclarationSyntax>(), order, new HashSet<string>(StringComparer.Ordinal));
        return order.Where(o => !o.StartsWith('→')).ToList();
    }

    private static void Walk(MethodDeclarationSyntax method, string bag, IReadOnlyDictionary<string, MethodDeclarationSyntax> family,
        List<string> order, HashSet<string> seen)
    {
        SyntaxNode body = (SyntaxNode?)method.Body ?? method.ExpressionBody!;
        bool ShouldDescend(SyntaxNode node) => !DeclaresParameter(node, bag);

        // Locals ApplyModifiers itself initialises from the bag (`var a11y = m.Accessibility;`):
        // handing one of them to a method outside the family is a handoff just like passing `m`.
        // Only for the entry method, where each call is an arm; inside a family helper the same
        // rule would record the helper's own plumbing calls.
        var derived = method.Identifier.Text != ReconcilerSource.ApplyModifiersName
            ? new HashSet<string>(StringComparer.Ordinal)
            : body.DescendantNodes(ShouldDescend)
                .OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer is { } init && init.Value.DescendantNodesAndSelf().Any(n => IsBagRead(n, bag) || IsBagName(n, "oldM")))
                .Select(v => v.Identifier.Text)
                .ToHashSet(StringComparer.Ordinal);

        foreach (var node in body.DescendantNodes(ShouldDescend))
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax access when IsBagRead(access, bag):
                    if (seen.Add(access.Name.Identifier.Text)) order.Add(access.Name.Identifier.Text);
                    break;
                case InvocationExpressionSyntax invocation when ReconcilerSource.InvokedName(invocation) is { } name:
                    if (family.TryGetValue(name, out var helper))
                        Walk(helper, bag, family, order, seen);
                    else if ((ReconcilerSource.IsBagHandoff(invocation)
                              || invocation.ArgumentList.Arguments.Any(a => a.Expression is IdentifierNameSyntax id && derived.Contains(id.Identifier.Text)))
                             && seen.Add("→" + name))
                        order.Add("→" + name);
                    break;
            }
        }
    }

    private static bool IsBagRead(SyntaxNode node, string bag) =>
        node is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } && id.Identifier.Text == bag;

    private static bool IsBagName(SyntaxNode node, string name) =>
        node is IdentifierNameSyntax id && id.Identifier.Text == name;

    // Don't read a lambda's or local function's own parameter named like the bag
    // (e.g. FindRemainingModifierOwner(static m => m.X)) as the modifier bag being applied.
    private static bool DeclaresParameter(SyntaxNode node, string name) => node switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text == name,
        ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters.Any(p => p.Identifier.Text == name),
        AnonymousMethodExpressionSyntax anon => anon.ParameterList?.Parameters.Any(p => p.Identifier.Text == name) == true,
        LocalFunctionStatementSyntax local => local.ParameterList.Parameters.Any(p => p.Identifier.Text == name),
        _ => false,
    };
}
