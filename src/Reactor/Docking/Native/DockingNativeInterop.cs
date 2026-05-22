using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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
                // Spec 045 §2.22 — DockHost exposes the docking subtree as
                // a `Custom` landmark region so AT walkers identify it as
                // a distinct functional area. The localized name comes
                // from `Docking.DockHost.Landmark`; apps wire their
                // resolver to translate (defaults to "Docking area").
                AutomationProperties.SetLandmarkType(host, AutomationLandmarkType.Custom);
                AutomationProperties.SetLocalizedLandmarkType(host,
                    DockingStrings.Get(DockingStringKeys.DockHostLandmark));
                AutomationProperties.SetName(host,
                    DockingStrings.Get(DockingStringKeys.DockHostLandmark));

                var content = BuildContent(element);
                var realized = rec.Reconcile(null, content, null, rerender);
                host.Child = realized;

                var state = new NativeHostState
                {
                    LastElement = element,
                    LastContent = content,
                };
                NativeHostState.SetAttached(host, state);

                // Spec 045 §2.10 — register the host Border with the
                // live-region announcer so layout-state transitions
                // (Close, Float, Pin, Dock) raise UIA notifications
                // against this element. Re-registers on each
                // DockManager-element instance change in `update`.
                DockHostLiveAnnouncer.Register(element, host);
                // Spec 045 §2.26 — register the host in the process-
                // wide registry so MCP tools + devtools introspection
                // can enumerate live hosts. The registry weak-refs the
                // element so it doesn't keep mounted layouts alive
                // after unmount.
                DockHostRegistry.Register(element);

                // Spec 045 §2.10 — wire keyboard chord accelerators once
                // at mount. Each accelerator's Invoked handler resolves the
                // current chord delegates via DockChordBridge keyed by the
                // *current* DockManager element (NativeHostState.LastElement).
                // The component registers fresh delegates each render; the
                // bridge entry's identity tracks the live element ref.
                AttachChordAccelerators(host);
                return host;
            },
            update: static (rec, oldEl, newEl, host, rerender) =>
            {
                var state = NativeHostState.GetAttached(host)
                    ?? new NativeHostState();

                var newContent = BuildContent(newEl);
                var newChild = rec.Reconcile(state.LastContent, newContent, host.Child, rerender);
                // Only reassign Border.Child when the reconciler swapped the
                // realized control out. Reassigning to the same UIElement
                // looks like a no-op, but WinUI's logical-tree machinery
                // can still cycle through detach→reattach internally,
                // which steals keyboard focus from any descendant
                // TextBox mid-edit (the keystroke-loses-focus bug).
                if (!ReferenceEquals(host.Child, newChild))
                    host.Child = newChild;

                // Refresh live-region binding to point at the new element
                // ref. We do NOT clear the old ref's entry — apps that
                // rebuild `new DockManager` each render leave a chain of
                // refs that the ConditionalWeakTable reclaims as the GC
                // collects each old element. Mirrors DockChordBridge /
                // DockHostModelBridge so callers holding any past element
                // ref can still resolve the host (matches the bridge
                // contract sibling fixtures rely on).
                DockHostLiveAnnouncer.Register(newEl, host);
                DockHostRegistry.Register(newEl);

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
                    rec.Reconcile(state.LastContent, null, realized, static () => { });
                }
                if (state?.LastElement is { } el)
                {
                    // Spec 045 §2.25 — close floating windows opened by
                    // this host so they don't outlive their DockManager.
                    // Close fires asynchronously on the OS side; we also
                    // clear the per-host tracker eagerly so the host can
                    // be reused / re-mounted without stale references.
                    foreach (var floating in DockFloatingTracker.SnapshotFor(el))
                    {
                        floating.Close();
                        DockFloatingTracker.UnregisterFor(el, floating);
                    }
                    DockChordBridge.Clear(el);
                    DockHostLiveAnnouncer.Clear(el);
                    DockHostRegistry.Unregister(el);
                    // §2.10 — close any in-flight Ctrl+Tab navigator. The
                    // popup's global KeyUp/KeyDown handlers are attached
                    // to XamlRoot.Content and root the host Border via
                    // the captured `_host` field; without this cleanup an
                    // unmount-during-chord leaks the entire subtree.
                    DockNavigatorPopup.CleanupFor(host);
                }
                host.Child = null;
                NativeHostState.SetAttached(host, null);
            });
    }

    private static void AttachChordAccelerators(Border host)
    {
        // Suppress WinUI's chord tooltip propagation (matches CommandHost).
        host.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        AddAccel(host, VirtualKey.PageDown, VirtualKeyModifiers.Control,
            (h) => h.NextTab());
        AddAccel(host, VirtualKey.PageUp, VirtualKeyModifiers.Control,
            (h) => h.PrevTab());
        AddAccel(host, VirtualKey.F4, VirtualKeyModifiers.Control,
            (h) => h.CloseActive());
        AddAccel(host, VirtualKey.W, VirtualKeyModifiers.Control,
            (h) => h.CloseActive());
        AddAccel(host, VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            (h) => h.EnterDropMode());
        // Spec 045 §2.10 — Ctrl+Tab opens the VS-style pane navigator.
        // Successive presses while it's open cycle ±1; Ctrl release
        // commits the selection (DockNavigatorPopup owns that state
        // machine). Ctrl+Shift+Tab seeds the cycle in reverse.
        AddAccel(host, VirtualKey.Tab, VirtualKeyModifiers.Control,
            (h) => h.OpenNavigator?.Invoke(+1));
        AddAccel(host, VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            (h) => h.OpenNavigator?.Invoke(-1));
        // Spec 045 §2.10 — Alt+F7 opens the hidden-pane picker.
        AddAccel(host, VirtualKey.F7, VirtualKeyModifiers.Menu,
            (h) => h.OpenHiddenPicker?.Invoke());
    }

    private static void AddAccel(Border host, VirtualKey key, VirtualKeyModifiers mods,
        Action<DockChordBridge.Handlers> invoke)
    {
        var ka = new KeyboardAccelerator { Key = key, Modifiers = mods };
        ka.Invoked += (s, e) =>
        {
            var state = NativeHostState.GetAttached(host);
            var handlers = DockChordBridge.Get(state?.LastElement);
            if (handlers is null) return;
            e.Handled = true;
            invoke(handlers);
        };
        host.KeyboardAccelerators.Add(ka);
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
