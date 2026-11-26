using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

// Other tests must not enable allocating diagnostic listeners during measurement.
[Collection(Diagnostics.ReactorPerfTraceCollection.Name)]
public class HookInvocationAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("state", false, false)]
    [InlineData("state", false, true)]
    [InlineData("state", true, false)]
    [InlineData("state", true, true)]
    [InlineData("updater", false, false)]
    [InlineData("updater", false, true)]
    [InlineData("updater", true, false)]
    [InlineData("updater", true, true)]
    [InlineData("dispatch", false, false)]
    [InlineData("dispatch", false, true)]
    [InlineData("dispatch", true, false)]
    [InlineData("dispatch", true, true)]
    [InlineData("persisted", false, false)]
    [InlineData("persisted", false, true)]
    public void InlineInvocations_DoNotAllocateMarshalClosures(string hook, bool threadSafe, bool changed)
    {
        const int warmup = 512, iterations = 4096;
        var ctx = new RenderContext();
        int notifications = 0, written = 0;
        ctx.BeginRender(() => notifications++);
        var (_, set) = ctx.UseState(0, threadSafe);
        var (_, update) = ctx.UseReducer(0, threadSafe);
        var (_, dispatch) = ctx.UseReducer<int, int>(static (s, a) => s + a, 0, threadSafe);
        var (_, setPersisted) = ctx.UsePersisted("invocation-alloc-int", 0, PersistedScope.Application);
        Func<int, int> reducer = changed ? static v => v + 1 : static v => v;
        Action invoke = hook switch
        {
            "state" => () => set(changed ? ++written : 0),
            "updater" => () => update(reducer),
            "dispatch" => () => dispatch(changed ? 1 : 0),
            "persisted" => () => setPersisted(changed ? ++written : 0),
            _ => throw new ArgumentOutOfRangeException(nameof(hook))
        };

        for (int i = 0; i < warmup; i++) invoke();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) invoke();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{hook}, threadSafe={threadSafe}, changed={changed}: {allocated} bytes / {iterations} calls");

        // Allow one byte/call of runtime noise, not the former 32/136-byte closures.
        Assert.InRange(allocated, 0, iterations);
        int expected = changed ? warmup + iterations : 0;
        Assert.Equal(expected, notifications);
        ctx.BeginRender(static () => { });
        Assert.Equal(hook == "state" ? expected : 0, ctx.UseState(0, threadSafe).Value);
        Assert.Equal(hook == "updater" ? expected : 0, ctx.UseReducer(0, threadSafe).Value);
        Assert.Equal(hook == "dispatch" ? expected : 0,
            ctx.UseReducer<int, int>(static (s, a) => s + a, 0, threadSafe).Value);
        Assert.Equal(hook == "persisted" ? expected : 0,
            ctx.UsePersisted("invocation-alloc-int", 0, PersistedScope.Application).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistedReferenceInvocations_DoNotAllocateMarshalClosures(bool changed)
    {
        const int warmup = 512, iterations = 4096;
        var ctx = new RenderContext();
        int notifications = 0;
        ctx.BeginRender(() => notifications++);
        var (_, set) = ctx.UsePersisted<string?>("invocation-alloc-ref", null, PersistedScope.Application);

        // Pre-existing values isolate setter costs from caller/string allocations.
        for (int i = 0; i < warmup; i++) set(changed && (i & 1) == 0 ? "value" : null);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) set(changed && (i & 1) == 0 ? "value" : null);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"persisted reference, changed={changed}: {allocated} bytes / {iterations} calls");

        Assert.InRange(allocated, 0, iterations);
        Assert.Equal(changed ? warmup + iterations : 0, notifications);
        set("last");
        ctx.BeginRender(static () => { });
        Assert.Equal("last", ctx.UsePersisted<string?>("invocation-alloc-ref", null, PersistedScope.Application).Value);
    }
}
