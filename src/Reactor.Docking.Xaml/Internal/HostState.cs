using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Docking.Internal;

/// <summary>
/// Per-DockManager host state attached to the realized
/// <c>WinUI.Dock.DockManager</c> control via a private DependencyProperty.
/// Carries:
///   <list type="bullet">
///     <item>The most recent Reactor <see cref="DockManager"/> element (for
///     diff'ing on the next update).</item>
///     <item>The pane-key → <see cref="PaneState"/> map (for keyed
///     reconciliation of content subtrees — spec 045 §4.4).</item>
///     <item>The adapter / behavior bridges (forward upstream callbacks to
///     the Reactor-side interfaces).</item>
///   </list>
/// </summary>
internal sealed class HostState
{
    public required Reconciler Reconciler { get; init; }

    public required Action RequestRerender { get; init; }

    public DockManager? LastElement { get; set; }

    public Dictionary<object, PaneState> PanesByKey { get; } = new();

    public AdapterBridge? AdapterBridge { get; set; }

    public BehaviorBridge? BehaviorBridge { get; set; }

    /// <summary>
    /// DependencyProperty used to stash the HostState on the realized
    /// <c>WinUI.Dock.DockManager</c>. Kept here (not on the upstream control)
    /// so the wrapper assembly owns the type — no public surface leaks.
    /// </summary>
    public static readonly DependencyProperty AttachedStateProperty =
        DependencyProperty.RegisterAttached(
            "AttachedState",
            typeof(HostState),
            typeof(HostState),
            new PropertyMetadata(null));

    public static HostState? GetAttached(DependencyObject d) =>
        (HostState?)d.GetValue(AttachedStateProperty);

    public static void SetAttached(DependencyObject d, HostState? value) =>
        d.SetValue(AttachedStateProperty, value);
}
