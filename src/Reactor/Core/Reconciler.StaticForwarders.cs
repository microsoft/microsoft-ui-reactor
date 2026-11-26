using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: public static entry points kept on Reconciler for API compatibility.
// Each forwards to the internal static class that owns the behavior.
public sealed partial class Reconciler
{
    /// <summary>
    /// Applies <see cref="ThemeRef"/> bindings with
    /// <see cref="FrameworkElement.SetThemeResourceBinding"/>, which installs the same live
    /// binding as a <c>{ThemeResource key}</c> markup extension. WinUI re-resolves it when the
    /// control's effective theme changes (the per-element/inherited <c>ActualTheme</c>, an
    /// ancestor <c>RequestedTheme</c> override included), on a high-contrast change, and when
    /// the control is parented, so a binding installed at mount on a control that is not in
    /// the tree yet picks up its ancestors' theme once it joins. That native self-heal is why
    /// a ThemeBindings-only element is safe to structurally skip (#758; verified in
    /// <c>ThemeBindingsSkipSelfHealFixtures</c> across immediate and inherited ancestor
    /// <c>RequestedTheme</c> toggles). Contrast a <c>ResourceOverrides.ThemeRef</c>, which
    /// resolves to a CONCRETE brush at reconcile and does NOT self-heal (so it stays
    /// theme-sensitive for the skip gate).
    /// </summary>
    /// <remarks>
    /// <para>Spec 047 §14 Phase 1 (1.3) — promoted from private (per audit
    /// existing-api-surface.md). Provisional API; see <c>REACTOR_V1_PREVIEW</c>.</para>
    /// <para>The binding sits at local-value precedence, where earlier releases used a
    /// synthesized Style. To keep the old winner, a property that already holds a value Reactor
    /// did not bind (a <c>.Set</c> setter, an element property, app code) is left alone, and
    /// <c>fe.Style</c> is never touched.</para>
    /// <para>Only the properties Reactor maps are bound: <c>Background</c>,
    /// <c>Foreground</c>, <c>BorderBrush</c> (on the control types that declare them) and
    /// <c>SplitView.PaneBackground</c>. A key that cannot be resolved from the control at call
    /// time is logged and skipped, and a property Reactor had bound to an earlier key is
    /// released rather than left on that key's brush.</para>
    /// </remarks>
    public static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
        => ThemeBindingApplier.ApplyThemeBindings(fe, bindings);

    /// <summary>
    /// Applies per-control resource overrides (lightweight styling) to a
    /// <see cref="FrameworkElement"/>. Literal values are set directly;
    /// <see cref="ThemeRef"/>-based values are resolved from
    /// <c>Application.Current.Resources</c> using the element's own effective theme.
    /// </summary>
    /// <remarks>
    /// Binary-compatible original 3-arg surface (Spec 047 §14 Phase 1 (1.3), promoted
    /// from private per audit existing-api-surface.md). Delegates to the 4-arg overload
    /// with <see cref="ElementTheme.Default"/>; the reconciler uses the 4-arg form to
    /// pass an explicit ancestor-aware theme (needed at mount, before the control is
    /// parented). Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </remarks>
    public static void ApplyResourceOverrides(
        FrameworkElement fe,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? oldOverrides,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? newOverrides)
        => ThemeBindingApplier.ApplyResourceOverrides(fe, oldOverrides, newOverrides);

    /// <summary>
    /// Applies per-control resource overrides (lightweight styling), resolving
    /// <see cref="ThemeRef"/>-backed values against an explicit
    /// <paramref name="effectiveTheme"/>. Provisional API (Spec 047 §14 Phase 1);
    /// see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    /// <param name="fe">The control whose <c>Resources</c> receive the overrides.</param>
    /// <param name="oldOverrides">The previously-applied overrides, or null at mount.</param>
    /// <param name="newOverrides">The overrides to apply, or null to clear managed keys.</param>
    /// <param name="effectiveTheme">
    /// The ancestor-aware effective theme threaded by the reconciler. When non-Default,
    /// ThemeRef-backed overrides resolve their concrete brush against it via
    /// <see cref="ThemeRef.Resolve(string, bool)"/> — necessary at mount, where the
    /// element is not yet parented and so an ancestor <c>RequestedTheme</c> is
    /// unreachable through <paramref name="fe"/>'s own visual-tree walk. Pass
    /// <see cref="ElementTheme.Default"/> to fall back to fe-based resolution
    /// (for example, callers of the 3-arg overload).
    /// </param>
    public static void ApplyResourceOverrides(
        FrameworkElement fe,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? oldOverrides,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? newOverrides,
        ElementTheme effectiveTheme)
        => ThemeBindingApplier.ApplyResourceOverrides(fe, oldOverrides, newOverrides, effectiveTheme);

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Mirrors a control's
    /// caption into <c>AutomationProperties.Name</c> when the author has not set
    /// one explicitly, so UIA clients that read the attached property directly
    /// (not via the peer) still find the name. Provisional API;
    /// see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void ApplyDefaultAutomationName(FrameworkElement fe, string? caption)
        => AccessibilityApplier.ApplyDefaultAutomationName(fe, caption);

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Update-time pair
    /// for <see cref="ApplyDefaultAutomationName"/>; overwrites the
    /// automation Name only when it matches the previous caption (i.e. the
    /// author hasn't intervened). Clears that default when the new content has no
    /// caption. Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void UpdateDefaultAutomationName(FrameworkElement fe, string? oldCaption, string? newCaption)
        => AccessibilityApplier.UpdateDefaultAutomationName(fe, oldCaption, newCaption);
}
