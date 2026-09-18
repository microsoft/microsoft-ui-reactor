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

        // Raise the acquisition bound for this test only. Production keeps a short
        // bound because it is paid on the UI thread at window close; with 8 contenders
        // on a saturated CI runner some writers legitimately exhaust it and skip, so
        // asserting "all land" against the production bound would flake for a reason
        // that has nothing to do with the mutual exclusion under test. The
        // skip-on-timeout behaviour has its own test below.
        CrossProcessWriteGuard.AcquireTimeoutOverrideMs = 30_000;
        try
        {
            // Separate instances, so the per-instance _ioLock cannot help — only the
            // cross-process guard can. That is the same mutual-exclusion gap two
            // processes hit, exercised without spawning them: distinct instances are
            // exactly as blind to each other as distinct processes are.
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

            foreach (var t in threads)
            {
                Assert.True(
                    t.Join(TimeSpan.FromSeconds(60)),
                    "A writer thread did not finish. The survivor count below would be measured "
                        + "against an incomplete document, and a live thread would leak into later tests.");
            }

            var reader = new JsonFileStore(_path);
            var survived = Enumerable.Range(0, writers)
                .Count(i => reader.TryRead($"window-{i}", out var d) && d is not null);

            Assert.True(
                survived == writers,
                $"Expected all {writers} ids to survive concurrent writes, but {survived} did. "
                    + "Read-merge-write lost entries — the cross-process write guard is not "
                    + "serializing separate JsonFileStore instances.");
        }
        finally
        {
            CrossProcessWriteGuard.AcquireTimeoutOverrideMs = null;
        }
    }

    [Fact]
    public void Guard_names_are_per_path_so_unrelated_stores_do_not_serialize()
    {
        // A guard keyed too coarsely (one lock for the machine) would be correct but
        // would make every Reactor app contend on one object. Distinct paths must
        // therefore produce distinct locks.
        var a = global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), $"reactor-guard-a-{Guid.NewGuid():N}.json");
        var b = global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), $"reactor-guard-b-{Guid.NewGuid():N}.json");

        try
        {
            using (var g1 = CrossProcessWriteGuard.Acquire(a))
            {
                // IsHeld, not non-null: Acquire returns a guard either way, so
                // asserting non-null would pass for two paths that collided on one
                // lock — the second would simply block for the timeout and return an
                // unheld guard. This is the assertion that distinguishes them.
                Assert.True(g1.IsHeld, "First acquisition should own the lock.");

                // Ownership is the oracle, not elapsed time: a loaded CI runner can
                // take a while to open any file, so a timing bound here would be a
                // flake generator rather than a lock test. If the two paths DID
                // collide, g2 would exhaust the timeout and report IsHeld == false.
                using var g2 = CrossProcessWriteGuard.Acquire(b);
                Assert.True(g2.IsHeld, "A different path must map to a different lock.");
            }

            // Same path, sequentially: proves the first acquisition was released.
            using (var g3 = CrossProcessWriteGuard.Acquire(a)) Assert.True(g3.IsHeld);
            using (var g4 = CrossProcessWriteGuard.Acquire(a)) Assert.True(g4.IsHeld);
        }
        finally
        {
            foreach (var p in new[] { a, b, CrossProcessWriteGuard.LockPathFor(a), CrossProcessWriteGuard.LockPathFor(b) })
            {
                try { if (global::System.IO.File.Exists(p)) global::System.IO.File.Delete(p); }
                catch (global::System.IO.IOException) { /* best effort */ }
                catch (UnauthorizedAccessException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Guard_is_a_real_cross_process_lock_not_a_process_local_one()
    {
        // The same-process test above would also pass for a process-local lock (a
        // static SemaphoreSlim keyed on path, say), so it cannot by itself establish
        // the cross-process guarantee the IWindowPersistenceStore contract promises.
        // This does: it takes the guard's underlying kernel object from OUTSIDE the
        // guard — exactly as a second process would — and shows the guard then cannot
        // be acquired, and that a write made while it is held is refused rather than
        // corrupting the document.
        var store = new JsonFileStore(_path);
        store.Write("before", new byte[] { 1 });

        var lockPath = CrossProcessWriteGuard.LockPathFor(_path);
        using (var foreign = new global::System.IO.FileStream(
                   lockPath,
                   global::System.IO.FileMode.OpenOrCreate,
                   global::System.IO.FileAccess.ReadWrite,
                   global::System.IO.FileShare.None))
        {
            using var blocked = CrossProcessWriteGuard.Acquire(_path);
            Assert.False(blocked.IsHeld,
                "A peer holds the lock file, so the guard must not report ownership.");

            // And the store must decline to write rather than merge a stale document.
            store.Write("during", new byte[] { 2 });
        }

        var verify = new JsonFileStore(_path);
        Assert.True(verify.TryRead("before", out _), "The pre-existing entry was lost.");
        Assert.False(verify.TryRead("during", out _),
            "A write issued while a peer held the lock was applied anyway — on timeout the "
                + "store must skip the write, since merging a stale document is what drops "
                + "another writer's entry.");
    }

    [Fact]
    public void An_open_reader_from_the_production_read_path_does_not_break_a_writer()
    {
        // Holds a stream opened by JsonFileStore's OWN read seam, not one this test
        // opened with hand-picked flags: a test that picked its own FileShare would
        // keep passing if the production share mode regressed to FileShare.Read, which
        // is the regression that silently kills writes.
        var store = new JsonFileStore(_path);
        store.Write("first", new byte[] { 1 });

        using (var reader = JsonFileStore.OpenForRead(_path))
        {
            store.Write("second", new byte[] { 2 });
        }

        var verify = new JsonFileStore(_path);
        Assert.True(verify.TryRead("second", out var second) && second is not null,
            "The write issued while a production reader was open did not land — the read "
                + "path's share mode blocked the commit.");
        Assert.True(verify.TryRead("first", out _), "The earlier entry was lost.");
    }
}
