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
/// family helpers in call order, with <c>→Name</c> markers where the bags are handed to a method
/// outside the family (events, gestures, drag-drop, reference edges).
/// </summary>
/// <remarks>
/// If you reorder arms on purpose, update <see cref="FrozenOrder"/> in the same PR so the
/// change is visible in review, and keep <see cref="Spec067_Section5_Constraints_Hold"/> green.
/// Layer 1 checks the modifier schema's slot order against the same list.
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
        "FontFamily", "FontSize", "FontWeight",
        "→ApplyEventHandlers",
        "→ApplyGestureHandlers",
        "→ApplyDragDropHandlers",
        "OnMountAction", "OnUpdateAction", "OnUnmountAction",
        "Ref",
        "→ApplyModifierReferenceEdges",
    ];

    [Fact]
    public void ApplyModifiers_Arm_Order_Matches_The_Frozen_List()
    {
        var actual = ReadApplyOrder();
        Assert.True(actual.SequenceEqual(FrozenOrder),
            "ApplyModifiers arm order changed. Actual:\n" + string.Join(",\n", actual.Select(a => $"\"{a}\"")));
    }

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
        // Event, gesture and drag-drop wiring after every property write.
        var lastPropertyWrite = new[] { "FontWeight", "FontFamily", "FontSize", "Background", "Foreground", "BorderBrush" }.Max(At);
        Assert.True(lastPropertyWrite < At("→ApplyEventHandlers"));
        Assert.True(At("→ApplyEventHandlers") < At("→ApplyGestureHandlers"));
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
            Assert.True(ReadFields(helper).Count > 0, $"{helper.Identifier.Text} reads no m.X field.");
    }

    internal static List<string> ReadApplyOrder()
    {
        // ApplyModifiers has a mount overload without oldM that forwards to the main one.
        var family = ReconcilerSource.ApplyModifiersFamily()
            .Where(m => m.ParameterList.Parameters.Any(p => p.Identifier.Text == "oldM"))
            .GroupBy(m => m.Identifier.Text)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(family[ReconcilerSource.ApplyModifiersName], family, order, seen);
        return order;
    }

    private static List<string> ReadFields(MethodDeclarationSyntax method)
    {
        var order = new List<string>();
        Walk(method, new Dictionary<string, MethodDeclarationSyntax>(), order, new HashSet<string>(StringComparer.Ordinal));
        return order.Where(o => !o.StartsWith('→')).ToList();
    }

    private static void Walk(MethodDeclarationSyntax method, IReadOnlyDictionary<string, MethodDeclarationSyntax> family,
        List<string> order, HashSet<string> seen)
    {
        SyntaxNode body = (SyntaxNode?)method.Body ?? method.ExpressionBody!;
        foreach (var node in body.DescendantNodes(ShouldDescend))
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "m" } } access:
                    if (seen.Add(access.Name.Identifier.Text)) order.Add(access.Name.Identifier.Text);
                    break;
                case InvocationExpressionSyntax invocation when InvokedName(invocation) is { } name:
                    if (family.TryGetValue(name, out var helper))
                        Walk(helper, family, order, seen);
                    else if (invocation.ArgumentList.Arguments.Any(a => a.Expression is IdentifierNameSyntax { Identifier.Text: "m" or "oldM" })
                             && seen.Add("→" + name))
                        order.Add("→" + name);
                    break;
            }
        }
    }

    // Don't read a lambda's own `m` parameter (e.g. FindRemainingModifierOwner(static m => m.X))
    // as the modifier bag being applied.
    private static bool ShouldDescend(SyntaxNode node) => node switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text != "m",
        ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters.All(p => p.Identifier.Text != "m"),
        _ => true,
    };

    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        _ => null,
    };
}
