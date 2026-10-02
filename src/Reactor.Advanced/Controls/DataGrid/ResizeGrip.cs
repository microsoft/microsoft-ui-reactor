using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// A Reactor element that renders as a panel with a west-east resize cursor.
/// Used as the drag handle for column resizing in the DataGrid header.
/// Background is set once at mount time for hit-testing and is NOT updated
/// by the reconciler — this lets event handlers change the background
/// (hover/drag feedback) without being overwritten on re-render.
/// </summary>
internal record ResizeGripElement(Element? Child = null) : Element;

/// <summary>
/// Grid subclass that exposes ProtectedCursor (which is protected on UIElement).
/// WinUI's Border is sealed so we can't subclass it. Grid supports Background
/// natively and is not sealed, making it ideal for cursor customization.
/// </summary>
internal sealed partial class ResizeGripControl : Grid
{
    public ResizeGripControl()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}

/// <summary>
/// V1 element handler that mounts and updates <see cref="ResizeGripElement"/>.
/// Spec 062 §7 Track B (B3): when the data grid moved into Reactor.Advanced this
/// replaced the former per-host <c>ResizeGripRegistration</c> that core's
/// <c>ReactorHost</c>/<c>ReactorHostControl</c> eagerly called via
/// <c>reconciler.RegisterType</c> — an eager core→data-grid reference that would
/// have become a core→Advanced cycle once the grip moved out. The mount/update
/// delegates capture no per-host state (they use only their arguments), so the
/// logic wraps cleanly as a stateless handler registered lazily and GLOBALLY via
/// <see cref="ResizeGripRegistration"/> at the <see cref="DataGridComponent{T}"/>
/// emit site. Global registration is
/// process-wide and synchronous on the touch, so the grip is registered before it
/// mounts in that same render — no timing risk and no consumer registration call.
/// </summary>
internal sealed class ResizeGripHandler : IElementHandler<ResizeGripElement, ResizeGripControl>
{
    public ResizeGripControl Mount(MountContext ctx, ResizeGripElement el)
    {
        var panel = new ResizeGripControl();
        // Transparent background enables hit-testing. Set once at mount;
        // event handlers (hover/drag) mutate it directly without reconciler interference.
        panel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.Colors.Transparent);
        if (el.Child is not null)
        {
            var child = ctx.MountChild(el.Child);
            if (child is not null) panel.Children.Add(child);
        }
        Reconciler.SetElementTag(panel, el);
        return panel;
    }

    public void Update(UpdateContext ctx, ResizeGripElement oldEl, ResizeGripElement newEl, ResizeGripControl panel)
    {
        // Deliberately do NOT touch panel.Background here — it is managed
        // by pointer event handlers (hover/drag) attached via OnMount.
        // Re-setting it would overwrite the hover/drag visual state.

        // ReconcileChild patches the child in place, or unmounts it and mounts the new
        // element (a changed element type, a removed child, or an update that built a
        // new control), and returns the control the grip should hold.
        var existing = panel.Children.Count > 0 ? panel.Children[0] : null;
        var next = ctx.ReconcileChild(oldEl.Child, newEl.Child, existing);
        if (next is null)
        {
            if (existing is not null) panel.Children.RemoveAt(0);
        }
        else if (existing is null)
            panel.Children.Insert(0, next);
        else if (!ReferenceEquals(next, existing))
            panel.Children[0] = next;

        Reconciler.SetElementTag(panel, newEl);
    }
}

/// <summary>
/// Spec 062 §7 — lazy, once-per-process registration of the resize grip through the
/// PUBLIC <see cref="ControlRegistry"/> seam (the same entry point third-party control
/// libraries use), rather than core's internal <c>Reg&lt;&gt;</c> shorthand. The explicit
/// static constructor suppresses <c>beforefieldinit</c> so the CLR runs <see cref="Init"/>
/// precisely on the first read of <see cref="Done"/>, preserving the "first factory touch
/// registers" guarantee. ControlRegistry.Register is first-wins (TryAdd), so repeat
/// activation is a no-op.
/// </summary>
internal static class ResizeGripRegistration
{
    static ResizeGripRegistration() { }

    internal static readonly bool Done = Init();

    private static bool Init()
    {
        ControlRegistry.Register<ResizeGripElement, ResizeGripControl>(
            static () => new ResizeGripHandler());
        return true;
    }
}