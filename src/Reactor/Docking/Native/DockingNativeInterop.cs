using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.16 / §2.19 — opt-in registration for the Reactor-native
//  docking renderer.
//
//  Replaces the P1 wrapper (Reactor.Docking.Xaml.DockingXamlInterop). An
//  app picks one of:
//
//     Microsoft.UI.Reactor.Docking.DockingXamlInterop.Register(reconciler);
//     Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(reconciler);
//
//  Both register the same DockManager element type; the last call wins.
//  Phase 2 ships both side by side so apps can A/B; §2.19 removes the
//  XAML chrome project once parity is verified.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Registers the docking element types with a Reactor <see cref="Reconciler"/>
/// using the Phase-2 native renderer (no WinUI.Dock XAML dependency).
/// </summary>
/// <remarks>
/// Spec 045 §2.16. Mount creates a <see cref="Border"/> whose <c>Child</c>
/// is reconciled from a <see cref="DockHostNativeComponent"/> wrapping the
/// <see cref="DockManager"/> element. The component owns ratio state via
/// hooks; reconciler preserves it across updates because the
/// <c>ComponentElement</c> type stays the same at the same tree position.
/// </remarks>
public static class DockingNativeInterop
{
    /// <summary>
    /// Registers the <see cref="DockManager"/> element type with the given
    /// reconciler using the native renderer. Idempotent — calling twice
    /// re-registers the same handler.
    /// </summary>
    public static void Register(Reconciler reconciler)
    {
        ArgumentNullException.ThrowIfNull(reconciler);

        DockSplitterReconcilerRegistration.Register(reconciler);
        DockDropTargetReconcilerRegistration.Register(reconciler);

        reconciler.RegisterType<DockManager, Border>(
            mount: static (rec, element, rerender) =>
            {
                var host = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };

                var content = BuildContent(element);
                var realized = rec.Reconcile(null, content, null, rerender);
                host.Child = realized;

                var state = new NativeHostState
                {
                    LastElement = element,
                    LastContent = content,
                };
                NativeHostState.SetAttached(host, state);
                // Live model integration (§2.16 "reconciler reads from
                // model") lands separately — the immutable element is the
                // source of truth in this first cut; DockHooks resolve to
                // defaults until the model is wired through.
                return host;
            },
            update: static (rec, oldEl, newEl, host, rerender) =>
            {
                var state = NativeHostState.GetAttached(host)
                    ?? new NativeHostState();

                var newContent = BuildContent(newEl);
                var newChild = rec.Reconcile(state.LastContent, newContent, host.Child, rerender);
                host.Child = newChild;

                state.LastElement = newEl;
                state.LastContent = newContent;
                NativeHostState.SetAttached(host, state);
                return null;
            },
            unmount: static (rec, host) =>
            {
                var state = NativeHostState.GetAttached(host);
                if (state?.LastContent is not null && host.Child is UIElement realized)
                {
                    try { rec.Reconcile(state.LastContent, null, realized, static () => { }); }
                    catch { /* best-effort unmount cleanup */ }
                }
                host.Child = null;
                NativeHostState.SetAttached(host, null);
            });
    }

    private static Element BuildContent(DockManager element)
    {
        var component = new ComponentElement<DockHostNativeProps>(
            typeof(DockHostNativeComponent),
            new DockHostNativeProps(element));
        return component;
    }

    /// <summary>Per-Border state attached to the native dock host control.</summary>
    private sealed class NativeHostState
    {
        public DockManager? LastElement { get; set; }
        public Element? LastContent { get; set; }

        private static readonly ConditionalWeakTable<Border, NativeHostState> _table = new();

        public static NativeHostState? GetAttached(Border host) =>
            _table.TryGetValue(host, out var state) ? state : null;

        public static void SetAttached(Border host, NativeHostState? state)
        {
            _table.Remove(host);
            if (state is not null) _table.Add(host, state);
        }
    }
}
