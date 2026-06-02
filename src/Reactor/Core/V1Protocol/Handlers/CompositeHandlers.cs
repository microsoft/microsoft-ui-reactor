using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 4 — CommandHost + the three Validation composites ported
// off the legacy composition-primitive switch onto V1 decorator handlers.
//
// Unlike the genuine composition primitives (Component/Func/Memo/ErrorBoundary)
// that orchestrate child reconciliation without producing a control of their
// own, each of these builds a single root WinUI control (Grid / StackPanel) and
// mounts Reactor children into it — the decorator shape. Mount/Update delegate
// to CompositeLifecycle; Unmount returns ContinueDefaultTraversal so the engine's
// generic Panel branch in UnmountRecursive recurses the root's Children and tears
// the mounted Reactor subtrees down exactly as the legacy switch arms did (which
// had no special unmount). The substitution path (FormField/ValidationVisualizer
// Update can return a fresh control) is preserved: returning a control != the
// existing one makes the adapter install the new one and unmount the old via
// parent reconcile.

/// <summary>§14 — CommandHost (Grid hosting one child + keyboard accelerators).</summary>
internal sealed class CommandHostHandler : IDecoratorElementHandler<CommandHostElement>
{
    public UIElement Mount(MountContext ctx, CommandHostElement el)
        => CompositeLifecycle.MountCommandHost(ctx.Reconciler, el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, CommandHostElement oldEl, CommandHostElement newEl, UIElement control)
        => control is WinUI.Grid host
            ? CompositeLifecycle.UpdateCommandHost(ctx.Reconciler, oldEl, newEl, host, ctx.RequestRerender) ?? control
            : ctx.Reconciler.Mount(newEl, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CommandHostElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 — FormField (3-child StackPanel: label / content / description).</summary>
internal sealed class FormFieldHandler : IDecoratorElementHandler<FormFieldElement>
{
    public UIElement Mount(MountContext ctx, FormFieldElement el)
        => CompositeLifecycle.MountFormField(ctx.Reconciler, el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, FormFieldElement oldEl, FormFieldElement newEl, UIElement control)
        => control is WinUI.StackPanel panel
            ? CompositeLifecycle.UpdateFormField(ctx.Reconciler, oldEl, newEl, panel, ctx.RequestRerender) ?? control
            : ctx.Reconciler.Mount(newEl, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, FormFieldElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 — ValidationVisualizer (StackPanel; Update always remounts).</summary>
internal sealed class ValidationVisualizerHandler : IDecoratorElementHandler<ValidationVisualizerElement>
{
    public UIElement Mount(MountContext ctx, ValidationVisualizerElement el)
        => CompositeLifecycle.MountValidationVisualizer(ctx.Reconciler, el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, ValidationVisualizerElement oldEl, ValidationVisualizerElement newEl, UIElement control)
        => control is WinUI.StackPanel panel
            ? CompositeLifecycle.UpdateValidationVisualizer(ctx.Reconciler, oldEl, newEl, panel, ctx.RequestRerender) ?? control
            : ctx.Reconciler.Mount(newEl, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, ValidationVisualizerElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}

/// <summary>§14 — ValidationRule (collapsed placeholder; evaluates the rule).</summary>
internal sealed class ValidationRuleHandler : IDecoratorElementHandler<ValidationRuleElement>
{
    public UIElement Mount(MountContext ctx, ValidationRuleElement el)
        => CompositeLifecycle.MountValidationRule(ctx.Reconciler, el);

    public UIElement Update(UpdateContext ctx, ValidationRuleElement oldEl, ValidationRuleElement newEl, UIElement control)
        => CompositeLifecycle.UpdateValidationRule(ctx.Reconciler, newEl) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, ValidationRuleElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
