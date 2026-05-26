using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 1 (1.14) — first container port. Exercises the
/// <see cref="SingleContent{TElement,TControl}"/> children strategy plus
/// the modifier-pipeline interaction (modifier-after-prop precedence per
/// §6.2 Q13 is honored by the engine — modifiers run after the handler's
/// Update body).
///
/// <para><b>Children strategy:</b> declared once as a static readonly
/// property instance so it doesn't reallocate per Mount; the
/// <see cref="V1HandlerAdapter{TElement,TControl}"/> dispatches through
/// it after Mount/Update returns.</para>
///
/// <para><b>Attached props:</b> per task 1.14, the
/// <see cref="AttachedPropWriter{TChildElement}"/> shape is validated by
/// the existence of the property — even though Border itself isn't a Grid
/// or Canvas, the shape gets exercised. The actual writes only fire when
/// a future Grid/Canvas port iterates the strategy's
/// <c>AttachedProps</c>; for now the shape exists as a compile-time
/// contract. Phase 3 picks this up when the additional containers port.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal sealed class BorderHandler : IElementHandler<BorderElement, WinUI.Border>
{
    private static readonly SingleContent<BorderElement, WinUI.Border> ChildrenStrategy =
        new SingleContent<BorderElement, WinUI.Border>(
            GetChild: el => el.Child,
            SetChild: (ctrl, ui) => ctrl.Child = ui)
        {
            GetCurrentChild = ctrl => ctrl.Child,
        };

    public WinUI.Border Mount(MountContext ctx, BorderElement el)
    {
        var ctrl = ctx.RentControl<WinUI.Border>();
        if (el.CornerRadius.HasValue)
            ctrl.CornerRadius = new CornerRadius(el.CornerRadius.Value);
        if (el.Background is not null) ctrl.Background = el.Background;
        if (el.BorderBrush is not null) ctrl.BorderBrush = el.BorderBrush;
        if (el.BorderThickness.HasValue)
            ctrl.BorderThickness = new Thickness(el.BorderThickness.Value);

        // Child mount is handled by the engine via the SingleContent strategy
        // after this Mount body returns (V1HandlerAdapter.DispatchChildrenMount).

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, BorderElement oldEl, BorderElement newEl, WinUI.Border ctrl)
    {
        // Child reconciliation is handled by the engine via the strategy after
        // this Update body returns. Note: the Phase 1 strategy dispatch is a
        // naive replace (V1HandlerAdapter.DispatchChildrenUpdate); legacy
        // UpdateBorder did a richer CanUpdate path, but the v1 dispatch site
        // is what Phase 3 will refine for keyed reconcile across all strategies.

        if (newEl.CornerRadius.HasValue)
            ctrl.CornerRadius = new CornerRadius(newEl.CornerRadius.Value);
        if (newEl.Background is not null) ctrl.Background = newEl.Background;
        if (newEl.BorderBrush is not null) ctrl.BorderBrush = newEl.BorderBrush;
        if (newEl.BorderThickness.HasValue)
            ctrl.BorderThickness = new Thickness(newEl.BorderThickness.Value);

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    public ChildrenStrategy<BorderElement, WinUI.Border>? Children => ChildrenStrategy;
}
