using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 — Expander handler (V1-owned).
//
// Expander needs callback/template wiring that the generic descriptor path does
// not cover: the IsExpanded-changed callback added/changed on UPDATE, plus
// HeaderTemplate (Element header wins over the string slot), ContentTransitions,
// and ExpandDirection. Expander has Header + Content child elements;
// ContinueDefaultTraversal lets the engine's default unmount recursion run. The
// unregistered ExpanderDescriptor is retained for isolated selftests.

/// <summary>§14 — Expander (header/content + IsExpanded callback wiring).</summary>
internal sealed class ExpanderHandler : IDecoratorElementHandler<ExpanderElement>
{
    public UIElement Mount(MountContext ctx, ExpanderElement el)
    {
        var requestRerender = ctx.RequestRerender;
        var expander = new WinUI.Expander
        {
            IsExpanded = el.IsExpanded,
            ExpandDirection = el.ExpandDirection,
        };
        // Element header wins over the string slot (matches the spec
        // "HeaderTemplate" slot semantics — strings are still supported as
        // the default header content).
        expander.Header = el.HeaderTemplate is not null
            ? ctx.Reconciler.Mount(el.HeaderTemplate, requestRerender)
            : el.Header;
        if (el.ContentTransitions is not null) expander.ContentTransitions = el.ContentTransitions;
        expander.Content = ctx.Reconciler.Mount(el.Content, requestRerender);
        Reconciler.SetElementTag(expander, el);
        if (el.OnIsExpandedChanged is not null)
        {
            expander.Expanding += (s, _) => (Reconciler.GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(true);
            expander.Collapsed += (s, _) => (Reconciler.GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(false);
        }
        Reconciler.ApplySetters(el.Setters, expander);
        return expander;
    }

    public UIElement Update(UpdateContext ctx, ExpanderElement oldEl, ExpanderElement newEl, UIElement control)
    {
        var reconciler = ctx.Reconciler;
        var requestRerender = ctx.RequestRerender;
        var exp = (WinUI.Expander)control;
        exp.IsExpanded = newEl.IsExpanded;
        exp.ExpandDirection = newEl.ExpandDirection;

        // Element header wins over the string slot. Reconcile via ReconcileChild
        // when both old and new use HeaderTemplate; otherwise swap modes.
        if (newEl.HeaderTemplate is not null)
        {
            reconciler.ReconcileChild(oldEl.HeaderTemplate, newEl.HeaderTemplate,
                () => exp.Header as UIElement,
                c => exp.Header = c,
                () => exp.Header = newEl.Header,
                requestRerender);
        }
        else
        {
            if (exp.Header is UIElement headerCtrl) reconciler.UnmountChild(headerCtrl);
            exp.Header = newEl.Header;
        }

        if (!ReferenceEquals(oldEl.ContentTransitions, newEl.ContentTransitions))
            exp.ContentTransitions = newEl.ContentTransitions;

        // Reconcile content child
        if (exp.Content is UIElement existingContent && reconciler.CanUpdate(oldEl.Content, newEl.Content))
        {
            var replacement = reconciler.UpdateChild(oldEl.Content, newEl.Content, existingContent, requestRerender);
            if (replacement is not null && !ReferenceEquals(exp.Content, replacement))
                exp.Content = replacement;
        }
        else
        {
            if (exp.Content is UIElement oldContent)
                reconciler.UnmountChild(oldContent);
            exp.Content = reconciler.Mount(newEl.Content, requestRerender);
        }

        Reconciler.SetElementTag(exp, newEl);
        if (oldEl.OnIsExpandedChanged is null && newEl.OnIsExpandedChanged is not null)
        {
            exp.Expanding += (s, _) => (Reconciler.GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(true);
            exp.Collapsed += (s, _) => (Reconciler.GetElementTag((UIElement)s!) as ExpanderElement)?.OnIsExpandedChanged?.Invoke(false);
        }
        Reconciler.ApplySetters(newEl.Setters, exp);
        return control;
    }

    public V1UnmountDisposition Unmount(UnmountContext ctx, ExpanderElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
