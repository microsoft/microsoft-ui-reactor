using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Manages recursive INPC subscriptions for a single UseObservableTree call.
/// Walks the object graph from a root, subscribes to PropertyChanged on every
/// reachable INotifyPropertyChanged object, and re-renders on any change.
/// Automatically handles cycle detection, nested object replacement, and cleanup.
/// </summary>
internal class ObservableTreeTracker : IDisposable
{
    private readonly Action _requestRerender;
    private readonly Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler> _subscriptions = new();
    // #6: the per-node PropertyChanged handler is the same method for every
    // subscription, so bind the delegate once instead of allocating a fresh one
    // for each newly-subscribed INPC node.
    private readonly PropertyChangedEventHandler _onNestedPropertyChanged;

    // #5: scratch containers reused across the common (non-re-entrant)
    // SyncSubscriptions path so a steady stream of PropertyChanged fires doesn't
    // allocate two HashSets + a List every time. Cleared before and after each
    // top-level sync. _visiting was historically a stack-local (TASK-062) so
    // re-entrant Walks couldn't share state — the _syncing guard below preserves
    // exactly that: a synchronous re-entrant sync (a side-effecting getter
    // firing PropertyChanged mid-Walk) falls back to fresh allocations and never
    // touches these fields.
    private readonly HashSet<INotifyPropertyChanged> _desiredSetScratch = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyPropertyChanged> _visitingScratch = new(ReferenceEqualityComparer.Instance);
    private readonly List<INotifyPropertyChanged> _toRemoveScratch = new();
    private bool _syncing;

    private INotifyPropertyChanged? _root;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _inpcPropertyCache = new();

    public ObservableTreeTracker(Action requestRerender)
    {
        _requestRerender = requestRerender;
        _onNestedPropertyChanged = OnNestedPropertyChanged;
        try { _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
        catch { /* No WinUI runtime (e.g. unit tests) */ }
    }

    /// <summary>
    /// Per-type cache of properties that could hold INPC values.
    /// Filters to: public instance properties, getter accessible,
    /// property type is class or interface (value types can't be INPC).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "CreateInpcCandidateProperties has DynamicallyAccessedMembers; ConcurrentDictionary.GetOrAdd resolves it via delegate.")]
    internal static PropertyInfo[] GetInpcCandidateProperties(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
        => _inpcPropertyCache.GetOrAdd(type, CreateInpcCandidateProperties);

    private static PropertyInfo[] CreateInpcCandidateProperties(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Where(p => p.CanRead && !p.PropertyType.IsValueType)
               .ToArray();

    /// <summary>
    /// Synchronize subscriptions to match the current object graph.
    /// Called on mount and whenever the source reference changes.
    /// </summary>
    public void SyncSubscriptions(INotifyPropertyChanged root)
    {
        _root = root;

        // #5: re-entrant (synchronous) sync — a side-effecting property getter
        // fired PropertyChanged during the outer Walk and re-entered here on the
        // same stack. Don't touch the scratch fields the outer call is using;
        // allocate fresh, exactly like the original always-allocate path.
        if (_syncing)
        {
            SyncCore(
                root,
                new HashSet<INotifyPropertyChanged>(ReferenceEqualityComparer.Instance),
                new HashSet<INotifyPropertyChanged>(ReferenceEqualityComparer.Instance),
                new List<INotifyPropertyChanged>());
            return;
        }

        _syncing = true;
        try
        {
            _desiredSetScratch.Clear();
            _visitingScratch.Clear();
            _toRemoveScratch.Clear();
            SyncCore(root, _desiredSetScratch, _visitingScratch, _toRemoveScratch);
        }
        finally
        {
            // Drop references so the tracker doesn't pin the walked graph between
            // fires; HashSet/List capacity is retained for reuse.
            _desiredSetScratch.Clear();
            _visitingScratch.Clear();
            _toRemoveScratch.Clear();
            _syncing = false;
        }
    }

    private void SyncCore(
        INotifyPropertyChanged root,
        HashSet<INotifyPropertyChanged> desiredSet,
        HashSet<INotifyPropertyChanged> visiting,
        List<INotifyPropertyChanged> toRemove)
    {
        Walk(root, desiredSet, visiting);

        // Unsubscribe from objects no longer in the graph
        foreach (var kvp in _subscriptions)
        {
            if (!desiredSet.Contains(kvp.Key))
            {
                kvp.Key.PropertyChanged -= kvp.Value;
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var obj in toRemove)
            _subscriptions.Remove(obj);

        // Subscribe to new objects in the graph (#6: shared cached delegate)
        foreach (var obj in desiredSet)
        {
            if (!_subscriptions.ContainsKey(obj))
            {
                obj.PropertyChanged += _onNestedPropertyChanged;
                _subscriptions[obj] = _onNestedPropertyChanged;
            }
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _subscriptions)
            kvp.Key.PropertyChanged -= kvp.Value;
        _subscriptions.Clear();
    }

    /// <summary>
    /// Hard cap on nodes visited per <see cref="SyncSubscriptions"/>. TASK-062.
    /// Without this, a cyclic or extremely fan-out INPC graph would walk
    /// every reachable property-changed node on every property change.
    /// </summary>
    private const int MaxNodesPerWalk = 1024;

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Object.GetType() does not carry DynamicallyAccessedMembers; INPC types are preserved because they implement INotifyPropertyChanged.")]
    private void Walk(INotifyPropertyChanged? node, HashSet<INotifyPropertyChanged> desiredSet, HashSet<INotifyPropertyChanged> visiting)
    {
        // SECURITY (TASK-062): bound the walk so a hostile or accidentally
        // huge INPC graph can't burn the UI thread on every property change.
        if (desiredSet.Count >= MaxNodesPerWalk) return;
        if (node is null || !visiting.Add(node))
            return; // null or cycle detected

        desiredSet.Add(node);

        foreach (var prop in GetInpcCandidateProperties(node.GetType()))
        {
            if (desiredSet.Count >= MaxNodesPerWalk) break;
            try
            {
                var value = prop.GetValue(node);
                if (value is INotifyPropertyChanged inpc)
                    Walk(inpc, desiredSet, visiting);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.WriteLine($"[Reactor.ObservableTreeTracker] Walk: property {prop.Name} threw: {ex.Message}");
            }
        }

        // SECURITY (TASK-062): use a stack-local visiting set rather than the
        // shared instance field so re-entrant calls (e.g., a property-change
        // handler that triggers another sync) can't corrupt each other's
        // cycle-detection state.
        visiting.Remove(node);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ObservableTreeTracker uses reflection to inspect property changes.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "ObservableTreeTracker uses reflection to inspect property changes.")]
    private void OnNestedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _requestRerender();

        if (sender is null || string.IsNullOrEmpty(e.PropertyName))
            return;

        var senderType = sender.GetType();
        var prop = senderType.GetProperty(e.PropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null || prop.PropertyType.IsValueType)
            return;

        // SyncSubscriptions mutates non-thread-safe _subscriptions and _visiting.
        // PropertyChanged can fire from any thread, so marshal to the UI thread.
        Microsoft.UI.Dispatching.DispatcherQueue? currentDispatcher = null;
        try { currentDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
        catch { /* No WinUI runtime (e.g. unit tests) */ }
        if (currentDispatcher is not null || _dispatcherQueue is null)
        {
            // Already on the UI thread, or no dispatcher available (test environment) — sync directly
            SyncFromRoot();
        }
        else
        {
            // Background thread — enqueue on the UI dispatcher
            _dispatcherQueue.TryEnqueue(() => SyncFromRoot());
        }

        void SyncFromRoot()
        {
            try
            {
                var root = FindRoot();
                if (root is not null)
                    SyncSubscriptions(root);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.WriteLine($"[Reactor.ObservableTreeTracker] OnNestedPropertyChanged: property access failed: {ex.Message}");
            }
        }
    }

    private INotifyPropertyChanged? FindRoot() => _root;
}
