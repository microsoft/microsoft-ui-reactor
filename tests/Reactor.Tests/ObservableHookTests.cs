using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Feature 6: Observable/Binding Interop Hooks.
/// Tests the hook behavior via RenderContext directly (no UI thread needed).
/// </summary>
public class ObservableHookTests
{
    private sealed class ExternalStore<T>
    {
        private T _snapshot;

        public ExternalStore(T initialSnapshot)
        {
            _snapshot = initialSnapshot;
        }

        public event Action? Changed;

        public T Snapshot => _snapshot;

        public void SetSnapshot(T value, bool notify = true)
        {
            _snapshot = value;
            if (notify)
                Changed?.Invoke();
        }

        public Action Subscribe(Action onChanged)
        {
            Changed += onChanged;
            return () => Changed -= onChanged;
        }
    }

    private class NotifyModel : INotifyPropertyChanged
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        private int _count;
        public int Count
        {
            get => _count;
            set
            {
                if (_count != value)
                {
                    _count = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RaiseAllPropertiesChanged()
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    // ── UseExternalStore ──────────────────────────────────────────

    [Fact]
    public void UseExternalStore_Returns_Current_Snapshot()
    {
        var ctx = new RenderContext();
        var store = new ExternalStore<string>("Alice");

        ctx.BeginRender(() => { });
        var result = ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        Assert.Equal("Alice", result);
    }

    [Fact]
    public void UseExternalStore_Triggers_Rerender_When_Snapshot_Changes()
    {
        var ctx = new RenderContext();
        var store = new ExternalStore<string>("Alice");
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        store.SetSnapshot("Bob");

        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseExternalStore_Does_Not_Rerender_When_Snapshot_Is_Equal()
    {
        var ctx = new RenderContext();
        var store = new ExternalStore<string>("Alice");
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        store.SetSnapshot("Alice");

        Assert.Equal(0, rerenderCount);
    }

    [Fact]
    public void UseExternalStore_Cleanup_Unsubscribes()
    {
        var ctx = new RenderContext();
        var store = new ExternalStore<string>("Alice");
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        ctx.RunCleanups();
        store.SetSnapshot("Bob");

        Assert.Equal(0, rerenderCount);
    }

    [Fact]
    public void UseExternalStore_Uses_Custom_Comparer()
    {
        var ctx = new RenderContext();
        var store = new ExternalStore<string>("Alice");
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot, StringComparer.OrdinalIgnoreCase);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store.Subscribe, () => store.Snapshot, StringComparer.OrdinalIgnoreCase);
        ctx.FlushEffects();

        store.SetSnapshot("alice");

        Assert.Equal(0, rerenderCount);
    }

    [Fact]
    public void UseExternalStore_Source_Changes_Between_Renders_Resubscribes()
    {
        var ctx = new RenderContext();
        var store1 = new ExternalStore<string>("First");
        var store2 = new ExternalStore<string>("Second");
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store1.Subscribe, () => store1.Snapshot);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseExternalStore(store2.Subscribe, () => store2.Snapshot);
        ctx.FlushEffects();

        store1.SetSnapshot("Changed");
        Assert.Equal(0, rerenderCount);

        store2.SetSnapshot("Updated");
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseExternalStore_Returns_Updated_Snapshot_On_Next_Render()
    {
        var ctx = new RenderContext();
        var store = new ExternalStore<string>("Alice");

        ctx.BeginRender(() => { });
        var first = ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        store.SetSnapshot("Bob");

        ctx.BeginRender(() => { });
        var second = ctx.UseExternalStore(store.Subscribe, () => store.Snapshot);
        ctx.FlushEffects();

        Assert.Equal("Alice", first);
        Assert.Equal("Bob", second);
    }

    // ── UseObservable ──────────────────────────────────────────────

    [Fact]
    public void UseObservable_Returns_Source()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };

        ctx.BeginRender(() => { });
        var result = ctx.UseObservable(model);
        ctx.FlushEffects();

        Assert.Same(model, result);
    }

    [Fact]
    public void UseObservable_Triggers_Rerender_On_PropertyChanged()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };
        int rerenderCount = 0;

        // First render
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        Assert.Equal(0, rerenderCount);

        // Simulate property change
        // Need to begin a new render first so the rerender callback is fresh
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        model.Name = "Bob";
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseObservable_Cleanup_Unsubscribes()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        // Run cleanups (simulating unmount)
        ctx.RunCleanups();

        // Changes should no longer trigger rerender
        model.Name = "Charlie";
        Assert.Equal(0, rerenderCount);
    }

    // ── UseObservableProperty ──────────────────────────────────────

    [Fact]
    public void UseObservableProperty_Returns_Property_Value()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };

        ctx.BeginRender(() => { });
        var name = ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        Assert.Equal("Alice", name);
    }

    [Fact]
    public void UseObservableProperty_Only_Rerenders_For_Matching_Property()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice", Count = 0 };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        // Changing a different property should NOT trigger rerender
        model.Count = 42;
        Assert.Equal(0, rerenderCount);

        // Changing the watched property SHOULD trigger rerender
        model.Name = "Bob";
        Assert.Equal(1, rerenderCount);
    }

    // ── UseCollection ──────────────────────────────────────────────

    [Fact]
    public void UseCollection_Returns_Collection_As_ReadOnlyList()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A", "B", "C" };

        ctx.BeginRender(() => { });
        var result = ctx.UseCollection(items);
        ctx.FlushEffects();

        Assert.Equal(3, result.Count);
        Assert.Equal("A", result[0]);
        Assert.Equal("B", result[1]);
        Assert.Equal("C", result[2]);
    }

    [Fact]
    public void UseCollection_Triggers_Rerender_On_Add()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        items.Add("B");
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseCollection_Triggers_Rerender_On_Remove()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A", "B" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        items.Remove("A");
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseCollection_Cleanup_Unsubscribes()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        ctx.RunCleanups();

        items.Add("B");
        Assert.Equal(0, rerenderCount);
    }

    // ── Multi-property change scenarios ──────────────────────────

    [Fact]
    public void UseObservableProperty_Multiple_Properties_Independent()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice", Count = 0 };
        int rerenderCount = 0;

        // Watch both Name and Count via separate hooks
        ctx.BeginRender(() => rerenderCount++);
        var name = ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        var count = ctx.UseObservableProperty(model, m => m.Count, nameof(NotifyModel.Count));
        ctx.FlushEffects();

        Assert.Equal("Alice", name);
        Assert.Equal(0, count);

        // Second render to update callback
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.UseObservableProperty(model, m => m.Count, nameof(NotifyModel.Count));
        ctx.FlushEffects();

        // Changing Name should trigger rerender
        model.Name = "Bob";
        Assert.Equal(1, rerenderCount);

        // Third render
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.UseObservableProperty(model, m => m.Count, nameof(NotifyModel.Count));
        ctx.FlushEffects();

        // Changing Count should also trigger rerender
        model.Count = 42;
        Assert.Equal(2, rerenderCount);
    }

    [Fact]
    public void UseObservable_Source_Changes_Between_Renders()
    {
        var ctx = new RenderContext();
        var model1 = new NotifyModel { Name = "First" };
        var model2 = new NotifyModel { Name = "Second" };
        int rerenderCount = 0;

        // First render: watch model1
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model1);
        ctx.FlushEffects();

        // Second render: switch to model2
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model2);
        ctx.FlushEffects();

        // Changes to old model should NOT trigger rerender
        model1.Name = "Changed";
        Assert.Equal(0, rerenderCount);

        // Changes to new model SHOULD trigger rerender
        model2.Name = "Updated";
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseCollection_Source_Changes_Between_Renders()
    {
        var ctx = new RenderContext();
        var items1 = new ObservableCollection<string> { "A" };
        var items2 = new ObservableCollection<string> { "X" };
        int rerenderCount = 0;

        // First render: watch items1
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items1);
        ctx.FlushEffects();

        // Second render: switch to items2
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items2);
        ctx.FlushEffects();

        // Changes to old collection should NOT trigger rerender
        items1.Add("B");
        Assert.Equal(0, rerenderCount);

        // Changes to new collection SHOULD trigger rerender
        items2.Add("Y");
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseObservable_Multiple_Hooks_Same_Source()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };
        int rerenderCount = 0;

        // Two hooks watching the same model
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        // A single property change should trigger at least one rerender
        model.Name = "Bob";
        Assert.True(rerenderCount >= 1);
    }

    // ── UseObservable edge cases ──────────────────────────────────

    [Fact]
    public void UseObservable_No_Rerender_Before_PropertyChanged()
    {
        // Verifies that simply calling UseObservable doesn't trigger a rerender —
        // only an actual PropertyChanged event should.
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        // No property change — no rerender
        Assert.Equal(0, rerenderCount);
    }

    [Fact]
    public void UseObservable_Multiple_Rapid_Changes_Each_Triggers()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "A", Count = 0 };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        // Multiple rapid changes
        model.Name = "B";
        model.Count = 1;
        model.Name = "C";
        Assert.Equal(3, rerenderCount);
    }

    [Fact]
    public void UseObservable_Same_Value_No_PropertyChanged()
    {
        // NotifyModel only fires PropertyChanged when value actually changes.
        // Setting the same value should NOT trigger rerender.
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(model);
        ctx.FlushEffects();

        model.Name = "Alice"; // same value — NotifyModel won't fire
        Assert.Equal(0, rerenderCount);
    }

    // ── UseObservableProperty edge cases ──────────────────────────

    [Fact]
    public void UseObservableProperty_Null_PropertyName_Triggers_All()
    {
        // When PropertyChangedEventArgs.PropertyName is null or empty,
        // it signals "all properties changed" and should trigger any watcher.
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice", Count = 0 };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        // Fire with null property name — signals all properties changed
        model.RaiseAllPropertiesChanged();
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseObservableProperty_Source_Changes_Resubscribes()
    {
        var ctx = new RenderContext();
        var model1 = new NotifyModel { Name = "First" };
        var model2 = new NotifyModel { Name = "Second" };
        int rerenderCount = 0;

        // First render: watch model1.Name
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model1, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        // Second render: switch to model2.Name
        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model2, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        // Old source should NOT trigger
        model1.Name = "Changed";
        Assert.Equal(0, rerenderCount);

        // New source SHOULD trigger
        model2.Name = "Updated";
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseObservableProperty_Cleanup_Unsubscribes()
    {
        var ctx = new RenderContext();
        var model = new NotifyModel { Name = "Alice" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservableProperty(model, m => m.Name, nameof(NotifyModel.Name));
        ctx.FlushEffects();

        ctx.RunCleanups();

        model.Name = "Bob";
        Assert.Equal(0, rerenderCount);
    }

    // ── UseCollection edge cases ──────────────────────────────────

    [Fact]
    public void UseCollection_Triggers_Rerender_On_Replace()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A", "B", "C" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        items[1] = "X"; // Replace
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseCollection_Triggers_Rerender_On_Clear()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A", "B", "C" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        items.Clear();
        Assert.Equal(1, rerenderCount);
    }

    [Fact]
    public void UseCollection_Triggers_Rerender_On_Move()
    {
        var ctx = new RenderContext();
        var items = new ObservableCollection<string> { "A", "B", "C" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseCollection(items);
        ctx.FlushEffects();

        items.Move(0, 2);
        Assert.Equal(1, rerenderCount);
    }

    // ── Nested INPC (documents current limitation) ────────────────

    [Fact]
    public void UseObservable_Does_NOT_Observe_Nested_INPC()
    {
        // This test documents the current limitation: UseObservable only
        // subscribes to the top-level object, not nested INPC objects.
        // UseObservableTree (Phase 1 of PropertyGrid) will address this.
        var ctx = new RenderContext();
        var child = new NotifyModel { Name = "Child" };
        var parent = new ParentModel { Child = child, Label = "Parent" };
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(parent);
        ctx.FlushEffects();

        ctx.BeginRender(() => rerenderCount++);
        ctx.UseObservable(parent);
        ctx.FlushEffects();

        // Changing the child's property does NOT trigger rerender
        child.Name = "Updated Child";
        Assert.Equal(0, rerenderCount);

        // But changing the parent's own property DOES
        parent.Label = "Updated Parent";
        Assert.Equal(1, rerenderCount);
    }

    // ── Test models ───────────────────────────────────────────────

    private class ParentModel : INotifyPropertyChanged
    {
        private NotifyModel _child = new();
        public NotifyModel Child
        {
            get => _child;
            set
            {
                if (_child != value)
                {
                    _child = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Child)));
                }
            }
        }

        private string _label = "";
        public string Label
        {
            get => _label;
            set
            {
                if (_label != value)
                {
                    _label = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
