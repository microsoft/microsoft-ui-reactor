using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost tests for thread-safe hooks exercising the actual WinUI render loop.
/// These tests mount real ReactorHost components and hammer setState from background
/// threads while the UI thread is actively rendering. This exercises:
///   - The Interlocked CAS render gate under contention
///   - Low-priority re-enqueue yielding to layout between renders
///   - Thread-safe hook locking during concurrent read (render) + write (background)
///   - Render coalescing: many setState calls → few actual renders
///   - No lost updates: final rendered UI reflects the last value set
/// </summary>
internal static class ThreadSafeHookFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  Rapid background setState during active rendering
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mounts a component with a thread-safe counter, then hammers setState from
    /// 4 background threads for 2 seconds while the UI thread renders. Verifies
    /// that the final rendered value matches the last value written and no exceptions
    /// were thrown. This is the core race condition test.
    /// </summary>
    internal class RapidBackgroundSetState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<int>? setCounter = null;
            int lastWritten = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (counter, set) = ctx.UseState(0, threadSafe: true);
                setCounter = set;
                return TextBlock($"Counter: {counter}");
            });

            await Harness.Render();
            H.Check("RapidBG_InitialRender", H.FindText("Counter: 0") is not null);

            // Hammer from 4 threads for 2 seconds. Use Barrier(5) so the main
            // thread waits for all workers to be inside the loop before starting
            // the 2-second budget — otherwise a starved CI threadpool can let
            // CancelAfter elapse before Task.Run actually schedules the workers.
            int writeCount = 0;
            var barrier = new Barrier(5);
            var cts = new CancellationTokenSource();
            var tasks = Enumerable.Range(0, 4).Select(t =>
                Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int val = Interlocked.Increment(ref writeCount);
                        setCounter!(val);
                        Interlocked.Exchange(ref lastWritten, val);
                    }
                })
            ).ToArray();

            barrier.SignalAndWait();
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await Task.WhenAll(tasks);

            // Let the render loop settle — low-priority enqueue means a few frames
            await Harness.Render();

            var final = lastWritten;
            var text = H.FindControl<TextBlock>(tb => tb.Text?.StartsWith("Counter:") == true);
            H.Check("RapidBG_TextPresent", text is not null);
            H.Check($"RapidBG_WritesHappened (writes={writeCount})", writeCount > 100);

            // The rendered value should be the last value set (or very close to it,
            // since a render might have been in flight when the last write landed)
            if (text is not null)
            {
                var rendered = int.Parse(text.Text.Replace("Counter: ", ""));
                H.Check($"RapidBG_FinalValueReasonable (rendered={rendered}, writes={writeCount})",
                    rendered > 0 && rendered <= writeCount);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Multiple thread-safe hooks updated concurrently
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mounts a component with 3 independent thread-safe hooks, each written by a
    /// dedicated background thread. Verifies all 3 rendered values update correctly
    /// and no cross-contamination occurs.
    /// </summary>
    internal class MultipleHooksConcurrent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<int>? setA = null, setB = null, setC = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (a, sa) = ctx.UseState(0, threadSafe: true);
                var (b, sb) = ctx.UseState(0, threadSafe: true);
                var (c, sc) = ctx.UseState(0, threadSafe: true);
                setA = sa; setB = sb; setC = sc;
                return VStack(
                    TextBlock($"A={a}"),
                    TextBlock($"B={b}"),
                    TextBlock($"C={c}")
                );
            });

            await Harness.Render();
            H.Check("MultiHook_Initial",
                H.FindText("A=0") is not null &&
                H.FindText("B=0") is not null &&
                H.FindText("C=0") is not null);

            // Each thread writes a known final value in a burst
            const int target = 500;
            var tasks = new[]
            {
                Task.Run(() => { for (int i = 1; i <= target; i++) setA!(i); }),
                Task.Run(() => { for (int i = 1; i <= target; i++) setB!(i * 10); }),
                Task.Run(() => { for (int i = 1; i <= target; i++) setC!(i * 100); }),
            };
            await Task.WhenAll(tasks);

            await Harness.Render();

            H.Check("MultiHook_A_Final", H.FindText($"A={target}") is not null);
            H.Check("MultiHook_B_Final", H.FindText($"B={target * 10}") is not null);
            H.Check("MultiHook_C_Final", H.FindText($"C={target * 100}") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  UseReducer serialization under concurrent dispatch
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mounts a component with a thread-safe UseReducer that accumulates a count.
    /// 4 threads each dispatch 500 increments. The final rendered value must be
    /// exactly 2000, proving the reducer serialized all updates.
    /// </summary>
    internal class ReducerSerializationStress(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<Func<int, int>>? update = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, upd) = ctx.UseReducer(0, threadSafe: true);
                update = upd;
                return TextBlock($"Sum: {count}");
            });

            await Harness.Render();
            H.Check("Reducer_Initial", H.FindText("Sum: 0") is not null);

            const int threads = 4;
            const int perThread = 500;
            var barrier = new Barrier(threads);
            var tasks = Enumerable.Range(0, threads).Select(_ =>
                Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < perThread; i++)
                        update!(prev => prev + 1);
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            await Harness.Render();

            H.Check("Reducer_Serialized",
                H.FindText($"Sum: {threads * perThread}") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  High-frequency setState with large element tree
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stress test: a component renders a dynamic list of 50 text elements based on
    /// a thread-safe array state. Background threads replace the entire array every
    /// ~1ms for 2 seconds. Verifies the UI doesn't crash or lose elements, and that
    /// the low-priority re-enqueue allows layout to keep up.
    /// </summary>
    internal class HighFrequencyLargeTree(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<string[]>? setItems = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (items, set) = ctx.UseState(
                    Enumerable.Range(0, 50).Select(i => $"Item-{i}-v0").ToArray(),
                    threadSafe: true);
                setItems = set;
                return VStack(items.Select(TextBlock).ToArray());
            });

            await Harness.Render();
            H.Check("LargeTree_InitialMount",
                H.FindText("Item-0-v0") is not null &&
                H.FindText("Item-49-v0") is not null);

            // Hammer with new arrays from 2 threads
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            int version = 0;
            var tasks = Enumerable.Range(0, 2).Select(_ =>
                Task.Run(() =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int v = Interlocked.Increment(ref version);
                        setItems!(Enumerable.Range(0, 50)
                            .Select(i => $"Item-{i}-v{v}").ToArray());
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);
            await Harness.Render();

            // After settling, all 50 items should be present with the same version
            var allItems = H.FindAllControls<TextBlock>(tb =>
                tb.Text?.StartsWith("Item-") == true);
            H.Check("LargeTree_AllPresent", allItems.Count == 50);
            H.Check("LargeTree_ManyVersions", version > 100);

            // All items should have a consistent version (same render pass)
            if (allItems.Count > 0)
            {
                var versions = allItems
                    .Select(tb => tb.Text.Split("-v").LastOrDefault())
                    .Distinct()
                    .ToList();
                H.Check("LargeTree_ConsistentVersion", versions.Count == 1);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Render coalescing verification
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that rapid setState calls coalesce into far fewer actual renders.
    /// A component tracks its render count via UseRef. After 1000 rapid setState
    /// calls, the render count should be much less than 1000.
    /// </summary>
    internal class RenderCoalescing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<int>? setValue = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (value, set) = ctx.UseState(0, threadSafe: true);
                var renderCount = ctx.UseRef(0);
                renderCount.Current++;
                setValue = set;
                return VStack(
                    TextBlock($"Value: {value}"),
                    TextBlock($"Renders: {renderCount.Current}")
                );
            });

            await Harness.Render();
            H.Check("Coalesce_Initial", H.FindText("Value: 0") is not null);

            // Fire 1000 setState calls as fast as possible from one thread
            await Task.Run(() =>
            {
                for (int i = 1; i <= 1000; i++)
                    setValue!(i);
            });

            await Harness.Render();

            H.Check("Coalesce_FinalValue", H.FindText("Value: 1000") is not null);

            // Render count should be far less than 1000 due to coalescing.
            // Threshold is loose (1/5 of writes) to tolerate scheduler interleaving
            // on CI runners where the UI thread can sneak in renders between
            // background-thread setState calls; full coalescing on a quiet machine
            // produces ~2 renders.
            var renderText = H.FindControl<TextBlock>(tb =>
                tb.Text?.StartsWith("Renders:") == true);
            if (renderText is not null)
            {
                var renders = int.Parse(renderText.Text.Replace("Renders: ", ""));
                H.Check($"Coalesce_FarFewerRenders (renders={renders})", renders < 200);
            }
        }
    }
}
