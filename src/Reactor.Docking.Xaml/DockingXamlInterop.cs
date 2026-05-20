using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDock = WinUI.Dock;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Entry point that registers the docking element types with a Reactor
/// <see cref="Reconciler"/>. Apps call <see cref="Register"/> once during
/// host initialization (same pattern as
/// <c>Microsoft.UI.Reactor.Hosting.XamlInterop.Register</c>); from then on,
/// a <see cref="DockManager"/> element in any Reactor render is reconciled
/// to a vendored <c>WinUI.Dock.DockManager</c> XAML control under the hood.
/// </summary>
/// <remarks>
/// Spec 045 §4.1, §4.4. The wrapper is the "leaf-wrapper" pattern called
/// out in §1.4 of the implementation tracking doc — the reconciler treats
/// the <see cref="DockManager"/> element as opaque and delegates to the
/// custom mount/update/unmount handlers registered here.
///
/// <para>
/// In Phase 2 (Reactor-native rewrite) the registration target is the same
/// element type but its mount produces a Reactor-native pane stack instead
/// of a vendored XAML control (spec 045 §5.1, §5.6). Apps don't change.
/// </para>
/// </remarks>
public static class DockingXamlInterop
{
    /// <summary>
    /// Pluggable resolver that recovers a Reactor-side pane key from a
    /// vendored <c>WinUI.Dock.Document</c> instance. Default behavior: looks
    /// up the pane in the host's <see cref="HostState.PanesByKey"/> map by
    /// identity and returns the key it was registered under.
    /// </summary>
    internal delegate object? PaneKeyResolver(WinUIDock.Document document);

    /// <summary>
    /// Registers the <see cref="DockManager"/> element type with the given
    /// reconciler. Call once at host init (the same place
    /// <c>XamlInterop.Register</c> is called).
    /// </summary>
    public static void Register(Reconciler reconciler)
    {
        ArgumentNullException.ThrowIfNull(reconciler);

        reconciler.RegisterType<DockManager, WinUIDock.DockManager>(
            mount: MountDockManager,
            update: UpdateDockManager,
            unmount: UnmountDockManager);
    }

    // ── Mount ─────────────────────────────────────────────────────────────

    private static WinUIDock.DockManager MountDockManager(
        Reconciler reconciler,
        DockManager element,
        Action rerender)
    {
        var manager = new WinUIDock.DockManager();

        // Themes/Generic.xaml + Themes/Styles.xaml + Themes/Themes.xaml need to
        // be merged into the manager's resources so the control template +
        // theme brushes resolve. The upstream WinUIDockResources helper does
        // this for us; merging at the control level (instead of Application)
        // keeps the rest of the Reactor host's styles isolated and lets two
        // managers coexist with different theme overrides.
        if (manager.Resources is not null)
        {
            try
            {
                manager.Resources.MergedDictionaries.Add(new WinUIDock.WinUIDockResources());
            }
            catch (Exception ex)
            {
                // Themes may already be merged at the Application level — log
                // and proceed; the styles are idempotent under normal use.
                global::System.Diagnostics.Debug.WriteLine($"[Reactor.Docking] Failed to merge WinUIDockResources: {ex.Message}");
            }
        }

        var state = new HostState
        {
            Reconciler = reconciler,
            RequestRerender = rerender,
        };
        HostState.SetAttached(manager, state);

        WireAdapter(manager, state, element);
        WireBehavior(manager, state, element);

        var trackedKeys = new HashSet<object>();
        DockTreeBuilder.ApplyLayout(manager, state, element.Layout);
        DockTreeBuilder.ApplySides(manager, state,
            element.LeftSide, element.TopSide, element.RightSide, element.BottomSide, trackedKeys);

        ApplyActiveDocument(manager, state, element.ActiveDocument);

        state.LastElement = element;

        // P1: Persistence is wired through WindowPersistedScope. For the
        // smoke fixture and showcase, callers can call SaveLayout/LoadLayout
        // explicitly. Per-mount auto-restore wiring lands when DockManager
        // grows a PersistenceId-honoring callback in §1.4 follow-up.

        Reconciler.SetElementTag(manager, element);
        return manager;
    }

    // ── Update ────────────────────────────────────────────────────────────

    private static UIElement? UpdateDockManager(
        Reconciler reconciler,
        DockManager oldEl,
        DockManager newEl,
        WinUIDock.DockManager manager,
        Action rerender)
    {
        var state = HostState.GetAttached(manager);
        if (state is null)
        {
            // Defensive — should never happen because mount installs it. Fall
            // back to a full remount.
            state = new HostState { Reconciler = reconciler, RequestRerender = rerender };
            HostState.SetAttached(manager, state);
        }

        if (!ReferenceEquals(oldEl.Adapter, newEl.Adapter))
        {
            WireAdapter(manager, state, newEl);
        }
        if (!ReferenceEquals(oldEl.Behavior, newEl.Behavior))
        {
            WireBehavior(manager, state, newEl);
        }

        // For P1 we re-apply the layout when the tree shape has changed. The
        // PaneState map preserves vendored Document instances (and the
        // ContentControl hosts inside them) keyed by DockableContent.Key, so
        // content subtrees survive the rebuild. Container instances (Panel,
        // Group) are rebuilt — they hold no Reactor-side state we need to
        // preserve. This is the pragmatic P1 implementation called out in
        // spec 045 §4.4; P2's native renderer can do a smarter container diff.
        if (!ReferenceEquals(oldEl.Layout, newEl.Layout))
        {
            DockTreeBuilder.ApplyLayout(manager, state, newEl.Layout);
        }
        else
        {
            // Layout reference identical — still reconcile content subtrees
            // so a state change inside a pane re-renders even when the dock
            // shape is stable. The PaneState map already knows every pane;
            // walk the new tree and reconcile content per leaf.
            ReconcileContentsInPlace(state, newEl.Layout);
        }

        var trackedKeys = new HashSet<object>(state.PanesByKey.Keys);
        if (!ReferenceEquals(oldEl.LeftSide,   newEl.LeftSide)
            || !ReferenceEquals(oldEl.TopSide,    newEl.TopSide)
            || !ReferenceEquals(oldEl.RightSide,  newEl.RightSide)
            || !ReferenceEquals(oldEl.BottomSide, newEl.BottomSide))
        {
            DockTreeBuilder.ApplySides(manager, state,
                newEl.LeftSide, newEl.TopSide, newEl.RightSide, newEl.BottomSide, trackedKeys);
        }

        if (!Equals(oldEl.ActiveDocument?.Key, newEl.ActiveDocument?.Key))
        {
            ApplyActiveDocument(manager, state, newEl.ActiveDocument);
        }

        state.LastElement = newEl;
        Reconciler.SetElementTag(manager, newEl);
        return null; // updated in place
    }

    // ── Unmount ───────────────────────────────────────────────────────────

    private static void UnmountDockManager(Reconciler reconciler, WinUIDock.DockManager manager)
    {
        var state = HostState.GetAttached(manager);
        if (state is null) return;

        // Persistence on detach — spec 045 §1.4 item 7. The wrapper stores
        // the layout JSON under WindowPersistedScope["docking:<PersistenceId>"]
        // if the most recent element carries a non-null PersistenceId. The
        // actual scope write is deferred to ReactorWindow's persisted-scope
        // service via a future hook; for P1 we just call SaveLayout() so
        // apps that supply a Behavior with a Save handler can intercept.
        if (state.LastElement?.PersistenceId is { Length: > 0 } pid)
        {
            try
            {
                _ = manager.SaveLayout();
                // The persisted-scope wiring is intentionally minimal in P1;
                // the showcase sample exercises explicit Save/Load buttons
                // (spec §4.5 Scene E). Auto-save-on-unmount wiring is a
                // tracking-doc §1.4 follow-up.
                _ = pid;
            }
            catch (Exception ex)
            {
                global::System.Diagnostics.Debug.WriteLine($"[Reactor.Docking] SaveLayout on unmount failed: {ex.Message}");
            }
        }

        // Reconcile every pane's content subtree to null so useEffect cleanups
        // fire and we don't leak per-pane Reactor state across unmount.
        foreach (var pane in state.PanesByKey.Values)
        {
            try
            {
                reconciler.Reconcile(
                    oldElement: pane.ContentElement,
                    newElement: null,
                    existingControl: pane.ContentControl_Realized,
                    requestRerender: state.RequestRerender);
            }
            catch
            {
                // Swallow — best-effort cleanup; the unmount must complete.
            }
        }

        state.PanesByKey.Clear();
        manager.ClearLayout();
        HostState.SetAttached(manager, null);
        Reconciler.DetachReactorState(manager);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void WireAdapter(WinUIDock.DockManager manager, HostState state, DockManager element)
    {
        if (element.Adapter is null)
        {
            manager.Adapter = null;
            state.AdapterBridge = null;
            return;
        }

        state.AdapterBridge = new AdapterBridge(element.Adapter, state, ResolveKeyForDocument(state));
        manager.Adapter = state.AdapterBridge;
    }

    private static void WireBehavior(WinUIDock.DockManager manager, HostState state, DockManager element)
    {
        if (element.Behavior is null)
        {
            manager.Behavior = null;
            state.BehaviorBridge = null;
            return;
        }

        state.BehaviorBridge = new BehaviorBridge(element.Behavior, state, ResolveKeyForDocument(state));
        manager.Behavior = state.BehaviorBridge;
    }

    private static PaneKeyResolver ResolveKeyForDocument(HostState state) => document =>
    {
        foreach (var kv in state.PanesByKey)
        {
            if (ReferenceEquals(kv.Value.Document, document))
                return kv.Key;
        }
        return null;
    };

    private static void ApplyActiveDocument(WinUIDock.DockManager manager, HostState state, DockableContent? active)
    {
        if (active is null)
        {
            manager.ActiveDocument = null;
            return;
        }

        var key = active.Key;
        if (key is null) return;

        if (state.PanesByKey.TryGetValue(key, out var paneState))
        {
            manager.ActiveDocument = paneState.Document;
        }
    }

    private static void ReconcileContentsInPlace(HostState state, DockNode? root)
    {
        if (root is null) return;
        Walk(root);

        void Walk(DockNode node)
        {
            switch (node)
            {
                case DockSplit split:
                    foreach (var child in split.Children) Walk(child);
                    break;
                case DockTabGroup grp:
                    foreach (var pane in grp.Documents) Walk(pane);
                    break;
                case DockableContent leaf:
                    var k = leaf.Key;
                    if (k is null) return;
                    if (state.PanesByKey.TryGetValue(k, out var ps))
                    {
                        var oldRealized = ps.ContentControl_Realized ?? (UIElement?)ps.ContentHost.Content;
                        var realized = state.Reconciler.Reconcile(
                            oldElement: ps.ContentElement,
                            newElement: leaf.Content,
                            existingControl: oldRealized,
                            requestRerender: state.RequestRerender);
                        if (!ReferenceEquals(realized, oldRealized))
                            ps.ContentHost.Content = realized;
                        ps.ContentElement = leaf.Content;
                        ps.ContentControl_Realized = realized;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Internal helper for <see cref="AdapterBridge.OnCreated(WinUIDock.Document)"/>
    /// — gets-or-creates the ContentControl host inside a Document so the
    /// adapter can pour Reactor content into it.
    /// </summary>
    internal static ContentControl EnsureContentHost(WinUIDock.Document document)
    {
        if (document.Content is ContentControl existing) return existing;

        var host = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        document.Content = host;
        return host;
    }
}
