using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The shallow-skip comparison behind <see cref="Element.ModifiersEqual"/> and
/// <see cref="Element.ModifiersVisiblyEqual"/>. Spec 067 layer 2: the method bodies are generated
/// from <c>ModifierSchema</c> into <c>Element.Modifiers.Generated.cs</c> by
/// <c>tools/Reactor.ModifierGen</c>. Each field's comparison is its slot's <c>SkipEquality</c>;
/// a new field is compared once it has a slot, with no edit here.
/// </summary>
/// <remarks>
/// <para><b>Layout</b> is entirely value-typed, so the synthesized record equality is exact
/// (NaN equals NaN) and covers every field, including the logical insets and RequestedTheme an
/// explicit list once forgot. The Layout/Visual reads are hoisted once (#159), and a null bucket
/// reads as the shared empty sentinel, so it compares like a bucket of nulls.</para>
/// <para><b>Visual</b> compares field by field: brushes by colour and opacity
/// (<see cref="BrushHelper.BrushesEqual"/>), because fluent helpers allocate a fresh
/// <see cref="SolidColorBrush"/> per render, and the rest with <c>==</c>.</para>
/// <para><b>Routed handlers</b> (FLAGSHIP-1) dispatch through the reconciler's per-element
/// <c>ModifierEventHandlerState.Current*</c> fields, refreshed only on the non-skip Update path,
/// so skipping is sound exactly when every handler is reference-equal: a stale <c>Current*</c>
/// that is the new delegate dispatches identically, and a fresh per-render closure forces Update
/// so its captures are wired. Gesture and drag-drop slots are deliberately not compared: they
/// dispatch through separate gesture/drag state, and comparing them would re-arm an in-flight
/// gesture mid-interaction. <see cref="GestureDispatch.RefreshGestureDragStateOnSkip"/> (#721)
/// refreshes their closures on the skip path instead.</para>
/// <para><b>Lifecycle callbacks.</b> <c>.Ref</c>, <c>OnUnmountAction</c> and
/// <c>OnUpdateAction</c> compare by reference. A <c>.Ref</c> swap must reach ApplyModifiers so it
/// clears the old cell and binds the new one, or the skip strands a stale <c>ElementRef</c>; and
/// ApplyModifiers re-registers the latest teardown and runs the update action on every non-skip
/// Update, so a changed closure must decline the skip.
/// <c>OnMountAction</c> fires once at mount (gated on <c>oldM is null</c>), so a later change is
/// inert and it is not compared.</para>
/// <para><b>Flyouts and the rich tooltip</b> decline the skip whenever set (full mode), and compare
/// by presence only in visible mode, where their content is reconciled when they open.</para>
/// <para><b>Accessibility</b> compares by record equality, because every fluent helper allocates
/// a fresh instance per render. Reference equality would fail for every element that sets one,
/// and the reconcile-highlight overlay would paint it as modified on every render.</para>
/// <para>In visible mode a side without modifiers shows the defaults, so a callback-only bag
/// added or removed is no visible change.</para>
/// </remarks>
internal static partial class ModifierEquality
{
    // Shared empty buckets so a null bucket compares field-for-field against a set one without
    // allocating.
    private static readonly LayoutModifiers EmptyLayout = new();
    private static readonly VisualModifiers EmptyVisual = new();
    private static readonly ElementModifiers EmptyModifiers = new();

    /// <summary>Full mode (<paramref name="visibleOnly"/> false) backs the shallow skip; visible mode, the perf provider's <c>changes</c> count (#1538).</summary>
    internal static partial bool ModifiersEqualCore(ElementModifiers? a, ElementModifiers? b, bool visibleOnly);

    private static partial bool VisualModifiersEqual(VisualModifiers? a, VisualModifiers? b);

    /// <summary>True when every routed-input handler slot is reference-equal.</summary>
    private static partial bool ModifierCallbacksEqual(ElementModifiers a, ElementModifiers b);

    private static bool AccessibilityEqual(AccessibilityModifiers? a, AccessibilityModifiers? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }
}
