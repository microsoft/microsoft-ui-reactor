using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 3 prelude — CheckBox value-control closure.
//
// CheckBoxDescriptor uses the generic `.Controlled` write path, whose
// suppress-token bookkeeping differs from the hand-coded MountCheckBox/
// UpdateCheckBox value-control pattern (ChangeEchoSuppressor + SetElementTag-
// first). The descriptor calls the suppressed setter whenever the element's
// old != new value, even when the control already holds the new value (a
// no-op same-value write). When WinUI raises no event for that no-op, the
// suppress token is stranded and swallows the user's *next* real toggle.
// Legacy UpdateCheckBox only begins suppression when cb.IsChecked != target,
// avoiding the stranded token. The descriptor also documents gaps for
// three-state mode + OnCheckedStateChanged. Symptom under V1 ON:
// ConditionalRendering_Toggle_HiddenAgain — the second checkbox toggle is
// swallowed, state stays true, and the conditional content never hides.
//
// Fix: Path B delegate to the complete legacy MountCheckBox/UpdateCheckBox
// bodies (identical to V1 OFF). CheckBox is a ContentControl (Label -> Content)
// with no Reactor child elements; ContinueDefaultTraversal keeps unmount/pool
// behavior identical to V1 OFF (mirror ButtonHandler). Descriptor retained for
// isolated selftests.

/// <summary>§14 prelude — CheckBox (value control; legacy echo-suppression).</summary>
internal sealed class CheckBoxHandler : IDecoratorElementHandler<CheckBoxElement>
{
    public UIElement Mount(MountContext ctx, CheckBoxElement el)
        => ctx.Reconciler.MountCheckBox(el);

    public UIElement Update(UpdateContext ctx, CheckBoxElement oldEl, CheckBoxElement newEl, UIElement control)
        => ctx.Reconciler.UpdateCheckBox(oldEl, newEl, (WinUI.CheckBox)control) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, CheckBoxElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
