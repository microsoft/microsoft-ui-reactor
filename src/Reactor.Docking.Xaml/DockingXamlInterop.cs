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

        // Register the vendored library's XAML metadata provider with the
        // Reactor application so that types referenced from Themes/Generic.xaml
        // (dock:Preview, dock:DockTargetButton, …) can be resolved when the
        // control template is applied. Without this, the XAML loader can't
        // find `using:WinUI.Dock` types and the template-apply pass crashes
        // with 0xC000027B inside Microsoft.UI.Xaml.dll. Spec 045 §4.4.
        //
        // The selftest harness doesn't crash without this because its host
        // project happens to discover a XamlMetaDataProvider that aggregates
        // WinUI.Dock types via the entry-assembly scan in
        // ReactorApplication.DiscoverHostAppProvider — but apps using
        // ReactorApp.Run<TRoot> don't get that aggregation for transitive
        // control-library references, so we register explicitly.
        try { ReactorApp.RegisterControlAssembly(typeof(WinUIDock.DockManager).Assembly); }
        catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor.Docking] RegisterControlAssembly failed: {ex.Message}"); }

        // Merge the vendored library's theme resources into the
        // Application's resource dictionary up front so the first
        // DockManager mount doesn't trigger a parse of Themes/Generic.xaml
        // while those resources are unresolvable.
        EnsureDockResourcesMergedIntoApplication();

        reconciler.RegisterType<DockManager, WinUIDock.DockManager>(
            mount: MountDockManager,
            update: UpdateDockManager,
            unmount: UnmountDockManager);
    }

    // Idempotent — track whether we've already merged the WinUI.Dock theme
    // resources into Application.Current.Resources. Without this, control
    // templates in Themes/Generic.xaml that reference {ThemeResource
    // DockStrokeActiveBrush} et al. fail to resolve (WinUI walks UP the
    // visual tree for resources, so brushes attached to the manager itself
    // can't satisfy lookups inside its own template). The Application-level
    // merge is the same pattern upstream's Example.WinUI uses via
    // <dock:WinUIDockResources/> in App.xaml.
    private static bool _resourcesMerged;
    private static readonly object _resourcesLock = new();

    private static void EnsureDockResourcesMergedIntoApplication()
    {
        if (_resourcesMerged) return;
        lock (_resourcesLock)
        {
            if (_resourcesMerged) return;
            try
            {
                var app = Application.Current;
                if (app?.Resources is null) return; // host has no app — selftest harness sets one up before Register
                app.Resources.MergedDictionaries.Add(new WinUIDock.WinUIDockResources());
                _resourcesMerged = true;
            }
            catch (Exception ex)
            {
                global::System.Diagnostics.Debug.WriteLine(
                    $"[Reactor.Docking] Failed to merge WinUIDockResources into Application.Resources: {ex.Message}");
            }
        }
    }

    // ── Mount ─────────────────────────────────────────────────────────────

    private static WinUIDock.DockManager MountDockManager(
        Reconciler reconciler,
        DockManager element,
        Action rerender)
    {
        var manager = new WinUIDock.DockManager();

        // Themes (Generic + Styles + Themes) are merged once into
        // Application.Current.Resources in Register() — see
        // EnsureDockResourcesMergedIntoApplication. Resources are looked up
        // by walking UP the visual tree, so a per-manager merge cannot
        // satisfy lookups inside the manager's own template.

        // Re-attempt the app-level merge if Register was called before
        // Application.Current existed (e.g., the test harness creates the
        // app after Register).
        EnsureDockResourcesMergedIntoApplication();

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
