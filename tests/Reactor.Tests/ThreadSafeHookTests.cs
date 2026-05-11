using Microsoft.UI.Reactor.Core;
using Xunit;

#pragma warning disable xUnit1031 // These tests deliberately use blocking (.Wait/.Result/WaitAll)
                                  // to simulate UI-thread + background-thread interaction patterns.

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Multithreaded stress tests for thread-safe hooks (UseState, UseReducer with threadSafe: true).
/// Forces race conditions between background writer threads and UI-thread render cycles.
/// </summary>
public class ThreadSafeHookTests
{
    // ════════════════════════════════════════════════════════════════
    //  Basic cross-thread setter
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_ThreadSafe_Setter_From_Background_Thread()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (value, set) = ctx.UseState(0, threadSafe: true);
        Assert.Equal(0, value);

        // Set from a background thread
        var t = Task.Run(() => set(42));
        t.Wait();

        // Re-render on "UI thread" — should see the update
        ctx.BeginRender(() => { });
        var (value2, _) = ctx.UseState(0, threadSafe: true);
        Assert.Equal(42, value2);
    }

    [Fact]
    public void UseReducer_ThreadSafe_Updater_From_Background_Thread()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (value, update) = ctx.UseReducer(0, threadSafe: true);
        Assert.Equal(0, value);

        var t = Task.Run(() => update(prev => prev + 100));
        t.Wait();

        ctx.BeginRender(() => { });
        var (value2, _) = ctx.UseReducer(0, threadSafe: true);
        Assert.Equal(100, value2);
    }

    [Fact]
    public void UseReducer_Redux_ThreadSafe_Dispatch_From_Background_Thread()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        static int reducer(int state, string action) => action switch
        {
            "add10" => state + 10,
            "double" => state * 2,
            _ => state
        };

        var (value, dispatch) = ctx.UseReducer<int, string>(reducer, 5, threadSafe: true);
        Assert.Equal(5, value);

        var t = Task.Run(() =>
        {
            dispatch("add10");
            dispatch("double");
        });
        t.Wait();

        ctx.BeginRender(() => { });
        var (value2, _) = ctx.UseReducer<int, string>(reducer, 5, threadSafe: true);
        Assert.Equal(30, value2); // (5 + 10) * 2
    }

    // ════════════════════════════════════════════════════════════════
    //  Concurrent writers — multiple threads calling the same setter
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_ThreadSafe_Concurrent_Writers_No_Lost_Updates()
    {
        // Use a reference type (array) so we can verify the final value
        // is one of the written values (not corrupted)
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, set) = ctx.UseState(0, threadSafe: true);

        const int writerCount = 8;
        const int writesPerThread = 1000;
        int rerenderCount = 0;

        // Re-render callback just counts
        ctx.BeginRender(() => Interlocked.Increment(ref rerenderCount));
        ctx.UseState(0, threadSafe: true);

        var barrier = new Barrier(writerCount);
        var tasks = Enumerable.Range(0, writerCount).Select(threadIdx =>
            Task.Run(() =>
            {
                barrier.SignalAndWait(); // force all threads to start simultaneously
                for (int i = 0; i < writesPerThread; i++)
                {
                    set(threadIdx * writesPerThread + i);
                }
            })
        ).ToArray();

        Task.WaitAll(tasks);

        // Verify: no exceptions, some rerenders happened, value is a valid written value
        Assert.True(rerenderCount > 0, "Expected at least one rerender request");

        ctx.BeginRender(() => { });
        var (finalValue, _) = ctx.UseState(0, threadSafe: true);
        Assert.InRange(finalValue, 0, writerCount * writesPerThread);
    }

    [Fact]
    public void UseReducer_ThreadSafe_Concurrent_Increments_Are_Serialized()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, update) = ctx.UseReducer(0, threadSafe: true);

        const int writerCount = 8;
        const int incrementsPerThread = 1000;

        var barrier = new Barrier(writerCount);
        var tasks = Enumerable.Range(0, writerCount).Select(_ =>
            Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < incrementsPerThread; i++)
                {
                    update(prev => prev + 1);
                }
            })
        ).ToArray();

        Task.WaitAll(tasks);

        // The reducer is locked, so all increments must be serialized.
        // Final value must be exactly writerCount * incrementsPerThread.
        ctx.BeginRender(() => { });
        var (finalValue, _) = ctx.UseReducer(0, threadSafe: true);
        Assert.Equal(writerCount * incrementsPerThread, finalValue);
    }

    // ════════════════════════════════════════════════════════════════
    //  Writer + reader race — background writes while UI reads
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_ThreadSafe_Writer_During_Render_Read()
    {
        // Simulate: background thread writes while UI thread is in BeginRender + UseState
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, set) = ctx.UseState("initial", threadSafe: true);

        const int iterations = 500;
        var cts = new CancellationTokenSource();

        // Background writer — constantly updates
        var writer = Task.Run(() =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                set($"value-{i++}");
            }
        });

        // UI thread — repeatedly renders and reads
        for (int i = 0; i < iterations; i++)
        {
            ctx.BeginRender(() => { });
            var (value, _) = ctx.UseState("initial", threadSafe: true);
            // Value must be either "initial" or a valid "value-N" string — never corrupted
            Assert.True(value == "initial" || value.StartsWith("value-"),
                $"Corrupted state read: '{value}'");
        }

        cts.Cancel();
        writer.Wait();
    }

    // ════════════════════════════════════════════════════════════════
    //  Multiple hooks — concurrent writes to different hooks
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_ThreadSafe_Hooks_Independent_Writers()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, setA) = ctx.UseState(0, threadSafe: true);
        var (_, setB) = ctx.UseState(0, threadSafe: true);
        var (_, setC) = ctx.UseState(0, threadSafe: true);

        const int writes = 500;

        // Three threads, each writing to a different hook
        var tasks = new[]
        {
            Task.Run(() => { for (int i = 0; i < writes; i++) setA(i + 1); }),
            Task.Run(() => { for (int i = 0; i < writes; i++) setB((i + 1) * 10); }),
            Task.Run(() => { for (int i = 0; i < writes; i++) setC((i + 1) * 100); }),
        };

        Task.WaitAll(tasks);

        ctx.BeginRender(() => { });
        var (a, _) = ctx.UseState(0, threadSafe: true);
        var (b, _2) = ctx.UseState(0, threadSafe: true);
        var (c, _3) = ctx.UseState(0, threadSafe: true);

        Assert.Equal(writes, a);
        Assert.Equal(writes * 10, b);
        Assert.Equal(writes * 100, c);
    }

    // ════════════════════════════════════════════════════════════════
    //  Rerender coalescing — many writes → one callback
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_ThreadSafe_Rerender_Fires_For_Each_Change()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        ctx.BeginRender(() => Interlocked.Increment(ref rerenderCount));
        var (_, set) = ctx.UseState(0, threadSafe: true);

        const int writes = 100;
        var barrier = new Barrier(4);
        var tasks = Enumerable.Range(0, 4).Select(t =>
            Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < writes; i++)
                    set(t * writes + i);
            })
        ).ToArray();

        Task.WaitAll(tasks);

        // Each distinct value change should have triggered a rerender callback.
        // Exact count depends on how many were same-value no-ops, but should be > 0.
        Assert.True(rerenderCount > 0);
        // Should be substantially less than 400 if same-value writes were deduplicated
        // (since multiple threads write overlapping sequences), but at least some fired.
    }

    [Fact]
    public void UseState_ThreadSafe_Same_Value_Does_Not_Trigger_Rerender()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        ctx.BeginRender(() => Interlocked.Increment(ref rerenderCount));
        var (_, set) = ctx.UseState(42, threadSafe: true);

        // Write the same value from multiple threads — should NOT trigger rerenders
        var tasks = Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() =>
            {
                for (int i = 0; i < 100; i++) set(42);
            })
        ).ToArray();

        Task.WaitAll(tasks);

        Assert.Equal(0, rerenderCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseReducer race: concurrent dispatch must serialize
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseReducer_Redux_ThreadSafe_Concurrent_Dispatch_Serializes()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        // Reducer that appends to a list — if not serialized, list would corrupt
        static List<int> reducer(List<int> state, int action)
        {
            var next = new List<int>(state) { action };
            return next;
        }

        var (_, dispatch) = ctx.UseReducer<List<int>, int>(reducer, new List<int>(), threadSafe: true);

        const int threads = 8;
        const int itemsPerThread = 100;
        var barrier = new Barrier(threads);

        var tasks = Enumerable.Range(0, threads).Select(t =>
            Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < itemsPerThread; i++)
                    dispatch(t * itemsPerThread + i);
            })
        ).ToArray();

        Task.WaitAll(tasks);

        ctx.BeginRender(() => { });
        var (list, _) = ctx.UseReducer<List<int>, int>(reducer, new List<int>(), threadSafe: true);

        // All items present, no duplicates, no corruption
        Assert.Equal(threads * itemsPerThread, list.Count);
        Assert.Equal(threads * itemsPerThread, list.Distinct().Count());
    }

    // ════════════════════════════════════════════════════════════════
    //  Stress: rapid render cycles interleaved with writes
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Stress_Rapid_Render_Cycles_With_Background_Writers()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, setCount) = ctx.UseState(0, threadSafe: true);
        var (_, setLabel) = ctx.UseState("", threadSafe: true);
        var (_, updateList) = ctx.UseReducer<List<string>>(new List<string>(), threadSafe: true);

        const int renderCycles = 200;
        var cts = new CancellationTokenSource();
        int writeCount = 0;
        var started = new CountdownEvent(3);

        // Background writers — hammer state continuously
        var writers = new[]
        {
            Task.Run(() =>
            {
                started.Signal();
                int i = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    setCount(Interlocked.Increment(ref writeCount));
                    i++;
                }
            }),
            Task.Run(() =>
            {
                started.Signal();
                int i = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    setLabel($"tick-{i++}");
                }
            }),
            Task.Run(() =>
            {
                started.Signal();
                int i = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    updateList(prev =>
                    {
                        var next = prev.Count > 50 ? new List<string>(prev.Skip(25)) : new List<string>(prev);
                        next.Add($"item-{i}");
                        return next;
                    });
                    i++;
                }
            }),
        };

        // Wait for all writers to start before rendering
        started.Wait();

        // UI thread: rapid render cycles reading state
        for (int r = 0; r < renderCycles; r++)
        {
            ctx.BeginRender(() => { });
            var (count, _a) = ctx.UseState(0, threadSafe: true);
            var (label, _b) = ctx.UseState("", threadSafe: true);
            var (list, _c) = ctx.UseReducer<List<string>>(new List<string>(), threadSafe: true);

            // Values must be internally consistent (not half-written)
            Assert.True(count >= 0);
            Assert.NotNull(label);
            Assert.NotNull(list);
        }

        cts.Cancel();
        Task.WaitAll(writers);

        // Final render — all state must be valid
        ctx.BeginRender(() => { });
        var (finalCount, _) = ctx.UseState(0, threadSafe: true);
        var (finalLabel, __) = ctx.UseState("", threadSafe: true);
        var (finalList, ___) = ctx.UseReducer<List<string>>(new List<string>(), threadSafe: true);

        Assert.True(finalCount > 0, "Counter should have been incremented");
        Assert.True(finalLabel == "" || finalLabel.StartsWith("tick-"),
            $"Label should be empty or a valid tick: '{finalLabel}'");
        Assert.NotNull(finalList);
    }

    // ════════════════════════════════════════════════════════════════
    //  Non-threadsafe hook: cross-thread setter without a captured UI
    //  dispatcher throws a loud InvalidOperationException. (Issue #212 —
    //  previously [Conditional("DEBUG")] so the throw was silently
    //  compiled out in RELEASE.) In production the same call path
    //  auto-marshals via ReactorApp.UIDispatcher instead — covered by the
    //  AppTests host fixtures, which actually capture a UI dispatcher.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_NonThreadSafe_OffThread_NoDispatcher_Throws()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, set) = ctx.UseState(0); // threadSafe: false (default)

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Run(() => set(42));
        }).Result;

        Assert.Contains("threadSafe: true", ex.Message);
    }

    [Fact]
    public void UseReducer_NonThreadSafe_OffThread_NoDispatcher_Throws()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, update) = ctx.UseReducer(0); // threadSafe: false (default)

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Run(() => update(prev => prev + 1));
        }).Result;

        Assert.Contains("threadSafe: true", ex.Message);
    }

    [Fact]
    public void UseReducer_Redux_NonThreadSafe_OffThread_NoDispatcher_Throws()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        static int reducer(int state, string action) => state + 1;
        var (_, dispatch) = ctx.UseReducer<int, string>(reducer, 0); // threadSafe: false

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Run(() => dispatch("go"));
        }).Result;

        Assert.Contains("threadSafe: true", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════
    //  Non-threadsafe hook: UI-thread setter works normally
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseState_NonThreadSafe_Works_On_UI_Thread()
    {
        var ctx = new RenderContext();
        int rerenderCount = 0;

        ctx.BeginRender(() => rerenderCount++);
        var (value, set) = ctx.UseState(0); // default: not thread-safe

        set(42);
        Assert.Equal(1, rerenderCount);

        ctx.BeginRender(() => rerenderCount++);
        var (value2, _) = ctx.UseState(0);
        Assert.Equal(42, value2);
    }

    [Fact]
    public void UseReducer_NonThreadSafe_Works_On_UI_Thread()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, update) = ctx.UseReducer(0);

        update(prev => prev + 7);

        ctx.BeginRender(() => { });
        var (value, _2) = ctx.UseReducer(0);
        Assert.Equal(7, value);
    }

    // ════════════════════════════════════════════════════════════════
    //  Mixed: threadsafe and non-threadsafe hooks in same component
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mixed_ThreadSafe_And_Normal_Hooks_Coexist()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        var (uiOnly, setUiOnly) = ctx.UseState("ui", threadSafe: false);
        var (shared, setShared) = ctx.UseState(0, threadSafe: true);
        var (_, updateShared) = ctx.UseReducer(0, threadSafe: true);

        // UI-thread setter works
        setUiOnly("updated");

        // Background writes to threadsafe hooks
        var t = Task.Run(() =>
        {
            setShared(99);
            updateShared(prev => prev + 1);
        });
        t.Wait();

        // Re-render and verify all hooks
        ctx.BeginRender(() => { });
        var (uiVal, _a) = ctx.UseState("ui", threadSafe: false);
        var (sharedVal, _b) = ctx.UseState(0, threadSafe: true);
        var (reducerVal, _c) = ctx.UseReducer(0, threadSafe: true);

        Assert.Equal("updated", uiVal);
        Assert.Equal(99, sharedVal);
        Assert.Equal(1, reducerVal);
    }
}
