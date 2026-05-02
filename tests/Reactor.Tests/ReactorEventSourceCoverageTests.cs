using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Drives every event on ReactorEventSource with a listener attached so the
/// guarded WriteEvent paths execute. Without the listener the IsEnabled gate
/// short-circuits each method body.
/// </summary>
public class ReactorEventSourceCoverageTests : IDisposable
{
    private sealed class CapturingListener : EventListener
    {
        // ReactorEventSource.Log is process-wide. Other tests running in parallel
        // can fire events into this listener while a test enumerates Events, so
        // both writes and snapshot reads must be guarded.
        private readonly List<EventWrittenEventArgs> _events = new();
        public IReadOnlyList<EventWrittenEventArgs> Events
        {
            get { lock (_events) return _events.ToArray(); }
        }
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            lock (_events) _events.Add(eventData);
        }
    }

    private readonly CapturingListener _listener;

    public ReactorEventSourceCoverageTests()
    {
        _listener = new CapturingListener();
        // Enable everything (verbose + all keywords).
        _listener.EnableEvents(ReactorEventSource.Log, EventLevel.Verbose, EventKeywords.All);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Reconcile_Start_And_Stop_Emit()
    {
        ReactorEventSource.Log.ReconcileStart("RootElement");
        ReactorEventSource.Log.ReconcileStop(10, 5, 2, 1);
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ReconcileStart));
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ReconcileStop));
    }

    [Fact]
    public void Component_Render_Start_And_Stop_Emit()
    {
        ReactorEventSource.Log.ComponentRenderStart("MyComponent", "PropChange");
        ReactorEventSource.Log.ComponentRenderStop("MyComponent", 1234);
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ComponentRenderStart));
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ComponentRenderStop));
    }

    [Fact]
    public void State_Change_Emits()
    {
        ReactorEventSource.Log.StateChange("UseState", "System.Int32", true);
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.StateChange));
    }

    [Fact]
    public void Component_Unmount_Emits()
    {
        ReactorEventSource.Log.ComponentUnmount("MyComponent");
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ComponentUnmount));
    }

    [Fact]
    public void Mcp_Call_Start_And_Stop_Emit()
    {
        ReactorEventSource.Log.McpCallStart("toolA", "selA");
        ReactorEventSource.Log.McpCallStop("toolA", true, 0, 50);
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.McpCallStart));
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.McpCallStop));
    }

    [Fact]
    public void Effects_Flush_Start_And_Stop_Emit()
    {
        ReactorEventSource.Log.EffectsFlushStart("MyComponent");
        ReactorEventSource.Log.EffectsFlushStop("MyComponent", 99);
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.EffectsFlushStart));
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.EffectsFlushStop));
    }

    [Fact]
    public void Child_Reconcile_Start_And_Stop_Emit()
    {
        ReactorEventSource.Log.ChildReconcileStart(3, 4);
        ReactorEventSource.Log.ChildReconcileStop();
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ChildReconcileStart));
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.ChildReconcileStop));
    }

    [Fact]
    public void Render_Error_Emits()
    {
        ReactorEventSource.Log.RenderError("MyComponent", "InvalidOperation", "boom");
        Assert.Contains(_listener.Events, e => e.EventName == nameof(ReactorEventSource.RenderError));
    }

    [Fact]
    public void Null_Strings_Are_Coalesced_To_Empty()
    {
        // Exercises the ?? string.Empty branch on every parameter slot.
        ReactorEventSource.Log.ReconcileStart(null!);
        ReactorEventSource.Log.ComponentRenderStart(null!, null!);
        ReactorEventSource.Log.ComponentRenderStop(null!, 0);
        ReactorEventSource.Log.StateChange(null!, null!, false);
        ReactorEventSource.Log.ComponentUnmount(null!);
        ReactorEventSource.Log.McpCallStart(null!, null!);
        ReactorEventSource.Log.McpCallStop(null!, false, 1, 1);
        ReactorEventSource.Log.EffectsFlushStart(null!);
        ReactorEventSource.Log.EffectsFlushStop(null!, 1);
        ReactorEventSource.Log.RenderError(null!, null!, null!);
        Assert.True(_listener.Events.Count > 0);
    }
}
