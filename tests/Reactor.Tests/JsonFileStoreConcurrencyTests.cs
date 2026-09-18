using Microsoft.UI.Reactor.Hosting.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 063 §5 — <see cref="JsonFileStore"/> writes are read-merge-write, so two
/// writers that both read before either commits will each write a document missing
/// the other's entry and the second rename wins. Entries vanish even though the
/// writers touched <i>different</i> ids.
/// </summary>
/// <remarks>
/// <para>This is why the store takes a named cross-process guard, not just an
/// in-process lock. Real-world shape: two instances of the same app, or two
/// executables sharing one publisher/product root under
/// <see cref="UnpackagedAppDataStore"/>.</para>
/// <para>The oracle is a <b>count of surviving ids</b>, not a round-trip: a
/// round-trip passes whenever the last writer wins, which is exactly the broken
/// behaviour. Removing the guard from <c>JsonFileStore.Write</c> reddens these.</para>
/// </remarks>
public sealed class JsonFileStoreConcurrencyTests : IDisposable
{
    private readonly string _path = global::System.IO.Path.Join(
        global::System.IO.Path.GetTempPath(),
        $"reactor-concurrency-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try
        {
            foreach (var f in global::System.IO.Directory.EnumerateFiles(
                         global::System.IO.Path.GetDirectoryName(_path)!,
                         global::System.IO.Path.GetFileNameWithoutExtension(_path) + "*"))
            {
                global::System.IO.File.Delete(f);
            }
        }
        catch (global::System.IO.IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    [Fact]
    public void Concurrent_writers_in_one_process_all_survive()
    {
        const int writers = 8;

        // Separate instances, so the per-instance _ioLock cannot help — only the named
        // cross-process guard can. That is the same mutual-exclusion gap two processes
        // hit, exercised without the fragility of spawning child processes from an MTP
        // test host: distinct instances are exactly as blind to each other as distinct
        // processes are.
        using var start = new Barrier(writers);
        var threads = new Thread[writers];
        for (var i = 0; i < writers; i++)
        {
            var id = $"window-{i}";
            threads[i] = new Thread(() =>
            {
                var store = new JsonFileStore(_path);
                start.SignalAndWait();
                store.Write(id, new byte[] { (byte)id.Length, 7, 7 });
            });
            threads[i].Start();
        }
        foreach (var t in threads) t.Join(TimeSpan.FromSeconds(30));

        var reader = new JsonFileStore(_path);
        var survived = Enumerable.Range(0, writers)
            .Count(i => reader.TryRead($"window-{i}", out var d) && d is not null);

        Assert.True(
            survived == writers,
            $"Expected all {writers} ids to survive concurrent writes, but {survived} did. "
                + "Read-merge-write lost entries — the cross-process write guard is not "
                + "serializing separate JsonFileStore instances.");
    }

    [Fact]
    public void Guard_names_are_per_path_so_unrelated_stores_do_not_serialize()
    {
        // A guard keyed too coarsely (one global name) would be correct but would make
        // every Reactor app on the machine contend on one mutex. Distinct paths must
        // therefore produce distinct names, and one path must be stable across calls.
        var a = global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), "reactor-guard-a.json");
        var b = global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), "reactor-guard-b.json");

        using (var g1 = CrossProcessWriteGuard.Acquire(a))
        using (var g2 = CrossProcessWriteGuard.Acquire(b))
        {
            // Both acquired concurrently => different kernel objects.
            Assert.NotNull(g1);
            Assert.NotNull(g2);
        }

        // Same path, sequentially, must be re-acquirable (i.e. released correctly).
        using (var g3 = CrossProcessWriteGuard.Acquire(a)) { Assert.NotNull(g3); }
        using (var g4 = CrossProcessWriteGuard.Acquire(a)) { Assert.NotNull(g4); }
    }
}
