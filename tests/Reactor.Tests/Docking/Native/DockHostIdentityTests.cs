using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Spec 045 §2.25 — the per-host docking tables are keyed by the host a
/// <see cref="DockManager"/> element belongs to (<see cref="DockHostIdentity"/>),
/// not by the element instance. Apps build a new <c>DockManager</c> on every
/// render and the host's unmount only knows the last one, so an entry filed
/// under an earlier instance used to outlive the host. The live paths (the
/// interop binding each element, unmount closing the floating windows) are
/// covered by the <c>NativeDocking_Reliability_FloatingWindowCloses*</c>
/// selftests; these pin the keying itself.
/// </summary>
[Collection("DockingGlobals")]
public sealed class DockHostIdentityTests : IDisposable
{
    public DockHostIdentityTests() => DockHostRegistry.ResetForTest();

    public void Dispose() => DockHostRegistry.ResetForTest();

    [Fact]
    public void KeyFor_UnboundElement_IsTheElementItself()
    {
        var element = new DockManager();

        Assert.Same(element, DockHostIdentity.KeyFor(element));
    }

    [Fact]
    public void KeyFor_EveryElementOfOneHost_IsTheHost()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var later = new DockManager();

        host.Bind(first);
        host.Bind(later);

        Assert.Same(host, DockHostIdentity.KeyFor(first));
        Assert.Same(host, DockHostIdentity.KeyFor(later));
    }

    [Fact]
    public void Bind_ElementRenderedByANewHost_MovesToThatHost()
    {
        var element = new DockManager();
        var unmounted = new DockHostIdentity();
        var remounted = new DockHostIdentity();

        unmounted.Bind(element);
        remounted.Bind(element);

        Assert.Same(remounted, DockHostIdentity.KeyFor(element));
    }

    [Fact]
    public void ChordBridge_EntryFromAnEarlierElement_ClearsThroughTheLastOne()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var last = new DockManager();
        host.Bind(first);
        var handlers = Handlers();
        DockChordBridge.Set(first, handlers);
        host.Bind(last);

        Assert.Same(handlers, DockChordBridge.Get(last));

        DockChordBridge.Clear(last);

        Assert.Null(DockChordBridge.Get(first));
    }

    [Fact]
    public void ChordBridge_LatestSet_IsWhatEveryElementOfTheHostResolves()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var last = new DockManager();
        host.Bind(first);
        DockChordBridge.Set(first, Handlers());
        host.Bind(last);
        var latest = Handlers();

        DockChordBridge.Set(last, latest);

        Assert.Same(latest, DockChordBridge.Get(first));
    }

    [Fact]
    public void ChordBridge_UnboundElements_KeepEntriesOfTheirOwn()
    {
        var a = new DockManager();
        var b = new DockManager();
        var handlersA = Handlers();
        DockChordBridge.Set(a, handlersA);

        Assert.Null(DockChordBridge.Get(b));

        DockChordBridge.Clear(b);

        Assert.Same(handlersA, DockChordBridge.Get(a));
        DockChordBridge.Clear(a);
    }

    [Fact]
    public void ModelBridgeAndDragGate_EntriesFromAnEarlierElement_ClearThroughTheLastOne()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var last = new DockManager();
        host.Bind(first);
        var model = new DockHostModel();
        DockDragGateBridge.TryStartDrag gate = static (_, _) => true;
        DockHostModelBridge.Set(first, model);
        DockDragGateBridge.Set(first, gate);
        host.Bind(last);

        Assert.Same(model, DockHostModelBridge.Get(last));
        Assert.Same(gate, DockDragGateBridge.Get(last));

        DockHostModelBridge.Clear(last);
        DockDragGateBridge.Clear(last);

        Assert.Null(DockHostModelBridge.Get(first));
        Assert.Null(DockDragGateBridge.Get(first));
    }

    [Fact]
    public void Registry_HostThatRendersNewElements_KeepsOneRecordAndItsId()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var last = new DockManager();
        host.Bind(first);
        var record = DockHostRegistry.Register(first);
        host.Bind(last);

        Assert.Same(record, DockHostRegistry.Register(last));
        Assert.Same(last, record.Manager);
        Assert.Same(record, Assert.Single(DockHostRegistry.Snapshot()));
        Assert.Same(record, DockHostRegistry.Get(record.Id));
    }

    [Fact]
    public void Registry_Unregister_ThroughAnEarlierElement_RemovesTheHost()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var last = new DockManager();
        host.Bind(first);
        DockHostRegistry.Register(first);
        host.Bind(last);
        DockHostRegistry.Register(last);

        DockHostRegistry.Unregister(first);

        Assert.Empty(DockHostRegistry.Snapshot());
    }

    [Fact]
    public void Registry_TwoHosts_KeepSeparateRecords()
    {
        var hostA = new DockHostIdentity();
        var hostB = new DockHostIdentity();
        var a = new DockManager();
        var b = new DockManager();
        hostA.Bind(a);
        hostB.Bind(b);
        var recordA = DockHostRegistry.Register(a);
        var recordB = DockHostRegistry.Register(b);

        Assert.NotSame(recordA, recordB);

        DockHostRegistry.Unregister(a);

        Assert.Same(recordB, Assert.Single(DockHostRegistry.Snapshot()));
    }

    [Fact]
    public void Registry_UnregisterHost_LeavesTheRecordOfAHostThatTookTheElementOver()
    {
        var oldHost = new DockHostIdentity();
        var newHost = new DockHostIdentity();
        var element = new DockManager();
        oldHost.Bind(element);
        var record = DockHostRegistry.Register(element);
        newHost.Bind(element);
        Assert.Same(record, DockHostRegistry.Register(element));

        DockHostRegistry.Unregister(oldHost);

        Assert.Same(record, Assert.Single(DockHostRegistry.Snapshot()));

        DockHostRegistry.Unregister(newHost);

        Assert.Empty(DockHostRegistry.Snapshot());
    }

    [Fact]
    public void Bridges_ClearHost_RemovesWhatEveryElementOfTheHostSet()
    {
        var host = new DockHostIdentity();
        var first = new DockManager();
        var last = new DockManager();
        host.Bind(first);
        DockChordBridge.Set(first, Handlers());
        DockHostModelBridge.Set(first, new DockHostModel());
        DockDragGateBridge.Set(first, static (_, _) => true);
        host.Bind(last);
        var latest = Handlers();
        DockChordBridge.Set(last, latest);

        Assert.Same(latest, DockChordBridge.Get(host));

        DockChordBridge.Clear(host);
        DockHostModelBridge.Clear(host);
        DockDragGateBridge.Clear(host);

        Assert.Null(DockChordBridge.Get(first));
        Assert.Null(DockChordBridge.Get(last));
        Assert.Null(DockHostModelBridge.Get(first));
        Assert.Null(DockDragGateBridge.Get(last));
    }

    [Fact]
    public void Bridges_ClearHost_LeavesAnotherHostAlone()
    {
        var oldHost = new DockHostIdentity();
        var newHost = new DockHostIdentity();
        var element = new DockManager();
        oldHost.Bind(element);
        DockChordBridge.Set(element, Handlers());
        newHost.Bind(element);
        var newHandlers = Handlers();
        DockChordBridge.Set(element, newHandlers);

        DockChordBridge.Clear(oldHost);

        Assert.Same(newHandlers, DockChordBridge.Get(element));
        DockChordBridge.Clear(newHost);
    }

    private static DockChordBridge.Handlers Handlers() =>
        new(NextTab: () => { }, PrevTab: () => { }, CloseActive: () => { }, EnterDropMode: () => { });
}
