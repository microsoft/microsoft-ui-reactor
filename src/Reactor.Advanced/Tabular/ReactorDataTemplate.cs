using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Advanced.Tabular;

/// <summary>
/// A <see cref="DataTemplate"/> whose realized content is a Reactor element rendered from
/// the realized container's <c>DataContext</c>.
/// </summary>
/// <remarks>
/// Built in code with <c>new DataTemplate(factory)</c>, so no XAML is parsed and no
/// <c>{Binding}</c> is involved: each realized shell reconciles its element when its
/// <c>DataContext</c> changes (first realization and every recycle), and every live shell
/// re-renders when the render function changes (a parent re-render). Shells are tracked
/// weakly, so a shell the control drops is collected with its subtree.
/// </remarks>
internal sealed class ReactorDataTemplate
{
    private sealed class Slot
    {
        public Element? Element;
        public UIElement? Control;
    }

    private readonly Reconciler _reconciler;
    private readonly Action _requestRerender;
    private readonly ConditionalWeakTable<ContentControl, Slot> _slots = new();
    private Func<object?, Element?> _render;
    private object _renderIdentity;
    private bool _detached;

    internal ReactorDataTemplate(
        Reconciler reconciler,
        Action requestRerender,
        Func<object?, Element?> render,
        object renderIdentity)
    {
        _reconciler = reconciler;
        _requestRerender = requestRerender;
        _render = render;
        _renderIdentity = renderIdentity;
        Template = new DataTemplate(CreateShell);
    }

    internal DataTemplate Template { get; }

    /// <summary>
    /// Swaps the render function. When <paramref name="renderIdentity"/> differs from the
    /// current one, every realized shell re-renders against its current item.
    /// </summary>
    internal void SetRender(Func<object?, Element?> render, object renderIdentity)
    {
        if (ReferenceEquals(_renderIdentity, renderIdentity)) return;
        _render = render;
        _renderIdentity = renderIdentity;
        List<ContentControl>? dead = null;
        foreach (var (shell, slot) in _slots)
        {
            if (TryGetDataContext(shell, out var item))
                Render(shell, slot, item);
            else
                (dead ??= []).Add(shell);
        }
        Forget(dead);
    }

    /// <summary>
    /// Unmounts every realized subtree so descendant effect cleanups run, and stops reacting
    /// to the shells: the native control can release (and re-context) them after the table
    /// is gone, and nothing may re-enter the reconciler on its behalf from then on.
    /// </summary>
    internal void UnmountAll()
    {
        _detached = true;
        List<ContentControl>? dead = null;
        foreach (var (shell, slot) in _slots)
        {
            if (slot.Control is not null)
            {
                if (TryGetDataContext(shell, out _))
                {
                    shell.Content = null;
                    _reconciler.UnmountChild(slot.Control);
                }
                else
                {
                    (dead ??= []).Add(shell);
                    try { _reconciler.UnmountChild(slot.Control); }
                    catch (ObjectDisposedException) { /* the subtree was finalized with its shell */ }
                }
            }
            slot.Element = null;
            slot.Control = null;
        }
        Forget(dead);
    }

    // A shell the control has released can be finalized while the weak table still yields it
    // (its entries are cleared only after finalization), leaving a projection whose WinRT
    // reference is disposed. Such a shell is gone from the table's tree: skip and forget it.
    private static bool TryGetDataContext(ContentControl shell, out object? item)
    {
        try
        {
            item = shell.DataContext;
            return true;
        }
        catch (ObjectDisposedException)
        {
            item = null;
            return false;
        }
    }

    private void Forget(List<ContentControl>? dead)
    {
        if (dead is null) return;
        foreach (var shell in dead)
            _slots.Remove(shell);
    }

    private UIElement CreateShell()
    {
        var shell = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = false,
        };
        var slot = new Slot();
        _slots.AddOrUpdate(shell, slot);
        shell.DataContextChanged += (sender, args) => Render(sender as ContentControl ?? shell, slot, args.NewValue);
        // Content that does not depend on the item (an empty-state template) renders now;
        // DataContextChanged may never fire for it.
        Render(shell, slot, shell.DataContext);
        return shell;
    }

    private void Render(ContentControl shell, Slot slot, object? item)
    {
        if (_detached) return;
        var next = _render(item);
        var control = _reconciler.Reconcile(slot.Element, next, slot.Control, _requestRerender);
        slot.Element = next;
        slot.Control = control;
        if (!ReferenceEquals(shell.Content, control))
            shell.Content = control;
    }
}
