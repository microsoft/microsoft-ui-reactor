using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Which WinUI property each common modifier is written to — the read-side mirror of
/// <c>Reconciler.ApplyModifiers</c> / <c>ApplyAccessibilityModifiers</c> /
/// <c>ApplyModifierReferenceEdges</c>, for <see cref="ReactorDiagnostics.GetAppliedProperties"/>.
///
/// <para>Owner resolution follows the same type tests the apply path uses (Padding goes to
/// <c>Control</c>, <c>Border</c>, <c>StackPanel</c>, … in that order; IsEnabled only to a
/// <c>Control</c>), so a modifier the reconciler skips for a control type is not reported
/// for it. Type tests only: nothing here touches a live control, so the table is testable
/// headless and needs no reflection.</para>
///
/// <para>Deliberately absent (not dependency-property writes): the <c>Layout</c> /
/// <c>Visual</c> bucket records (their members are listed individually), event handlers,
/// gesture and drag configs, <c>OnMountAction</c>/<c>OnUnmountAction</c>, <c>Ref</c>,
/// <c>Backdrop</c> (a window property). <c>Scale</c>/<c>Rotation</c>/<c>Translation</c>/
/// <c>CenterPoint</c>/<c>TransformOrigin</c> aren't DPs: the reconciler writes them to the element's composition
/// visual (spec 066 layer 4; <c>Rotation</c> as <c>RotationAngle</c>, in radians), so they are mapped
/// to its properties. The <c>UIElement</c> facades read identity for them.
/// <c>AppliedModifierMap_CoversEveryModifier_OrExcludesItOnPurpose</c> fails when a new modifier is neither mapped nor listed
/// as excluded.</para>
///
/// <para>Spec 067 layer 2: <c>Entries</c> and the owner helpers are generated from
/// <c>ModifierSchema</c> into <c>AppliedModifierMap.Generated.cs</c> by
/// <c>tools/Reactor.ModifierGen</c>. Only the flyout-slot owner below stays hand-written.</para>
/// </summary>
/// <summary>One modifier-applied property with its live element-side value (internal; the public surface reports it as text).</summary>
internal sealed record AppliedModifier(string Modifier, string Property, object? Value);

internal static partial class AppliedModifierMap
{
    internal readonly record struct Entry(string Modifier, Func<ElementModifiers, object?> Get, Func<Type, string?> Target);

    private static bool Is<T>(Type t) => typeof(T).IsAssignableFrom(t);

    private static Func<Type, string?> Always(string property) => _ => property;

    // Mirrors Reconciler.ResolveFlyoutSlot.
    private static string? AttachedFlyoutOwner(Type t) =>
        Is<WinUI.SplitButton>(t) ? "SplitButton.Flyout"
        : Is<WinUI.Button>(t) ? "Button.Flyout"
        : "FlyoutBase.AttachedFlyout";

    internal static IReadOnlyList<AppliedModifier> Describe(ElementModifiers modifiers, Type controlType)
    {
        var result = Entries
            .Select(entry => new AppliedModifier(entry.Modifier, entry.Target(controlType) ?? "", entry.Get(modifiers)))
            .Where(applied => applied.Value is not null && applied.Property.Length > 0)
            .ToArray();
        return result.Length == 0 ? global::System.Array.Empty<AppliedModifier>() : result;
    }

    /// <summary>Modifier name reported for the caption-derived default name.</summary>
    internal const string DefaultAutomationNameModifier = "DefaultAutomationName";

    // MarginInlineStart/End, PaddingInlineStart/End, BorderInlineStart: overlaid onto the
    // property's existing value rather than replacing it (Reconciler.ApplyModifiers).
    private static bool IsEdgeOverlay(string modifier) => modifier.Contains("Inline", StringComparison.Ordinal);

    /// <summary>
    /// Merges the modifiers of a decorator chain (outermost first, innermost target last) in the
    /// order Reactor applies them — the target's own, then each decorator's outward — so an outer
    /// level's value replaces an inner one for the same property. Ends with the caption-derived
    /// default name when it applies and the name in effect (the outermost level's) is absent or empty.
    /// </summary>
    internal static IReadOnlyList<AppliedModifier> DescribeChain(IReadOnlyList<Element> chain, Type controlType, string? liveName)
    {
        var merged = new List<AppliedModifier>();
        // The name in effect is the outermost level's (applied last); an empty one counts as
        // none, so the caption default still applies even over an inner explicit name.
        string? effectiveName = null;
        for (int level = chain.Count - 1; level >= 0; level--)
        {
            if (chain[level].Modifiers is not { } modifiers) continue;
            if (modifiers.AutomationName is { } levelName) effectiveName = levelName;
            var levelRows = Describe(modifiers, controlType);
            // Several modifiers can compose one property (Margin plus MarginInlineStart), and a
            // level keeps all of its own contributors. Against inner levels: a modifier replaces
            // its own earlier value; a whole-value modifier (Margin) rewrites the property and
            // replaces every inner contributor, while an inline-edge overlay builds on it.
            foreach (var m in levelRows)
            {
                // CenterPoint and TransformOrigin both feed Visual.CenterPoint (pivot = size × origin +
                // offset, spec 066 §8.1), so neither replaces the other's contribution.
                bool wholeValue = !IsEdgeOverlay(m.Modifier) && m.Modifier is not ("CenterPoint" or "TransformOrigin");
                merged.RemoveAll(prior => prior.Modifier == m.Modifier
                    || (wholeValue && string.Equals(prior.Property, m.Property, StringComparison.Ordinal)));
            }
            merged.AddRange(levelRows);
        }

        if (effectiveName is not { Length: > 0 } && DescribeDefaultAutomationName(chain[^1], liveName) is { } defaultName)
            merged.Add(defaultName);
        return merged;
    }

    /// <summary>
    /// The caption-derived <c>AutomationProperties.Name</c> the reconciler writes when the
    /// author sets none (<c>Reconciler.ApplyDefaultAutomationName</c>), or null when it does
    /// not apply.
    ///
    /// <para>Reported only while <paramref name="liveName"/> still equals the default: the
    /// apply path yields to any existing name, so a different live value means the app (a
    /// <c>.Set(...)</c> setter or code-behind) wrote it. A value the app set to exactly the
    /// derived default (the caption as Reactor shortens it, not necessarily the full caption) is
    /// indistinguishable from it and is reported as the default.</para>
    /// </summary>
    internal static AppliedModifier? DescribeDefaultAutomationName(Element source, string? liveName)
    {
        // An explicit, non-empty .AutomationName(...) is reported as itself and suppresses the
        // default; an empty one does not, because the apply path treats an empty name as absent.
        if (source.Modifiers?.AutomationName is { Length: > 0 }) return null;
        if (AccessibilityApplier.DefaultAutomationNameFromCaption(AccessibilityApplier.ResolveCaptionForElement(source)) is not { } name)
            return null;
        if (!string.Equals(liveName, name, StringComparison.Ordinal)) return null;
        return new AppliedModifier(DefaultAutomationNameModifier, "AutomationProperties.Name", name);
    }
}
