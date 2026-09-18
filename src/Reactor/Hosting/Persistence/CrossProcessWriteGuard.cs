using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Serializes <see cref="JsonFileStore"/>'s read-merge-write across processes.
/// (spec 063 §5)
/// </summary>
/// <remarks>
/// <para>The store merges the existing document with the incoming id and renames a
/// temp file over the original. Two writers that both read before either renames will
/// each write a document missing the other's id, and the second rename wins — so
/// entries disappear even though the writers touched <i>different</i> ids. An
/// in-process lock cannot see the other process, so the mutual exclusion has to be
/// named and kernel-scoped.</para>
/// <para>Scoped <c>Local\</c> (per logon session) rather than <c>Global\</c>: the
/// store lives under a per-user path, so cross-session exclusion buys nothing and
/// <c>Global\</c> would need a privilege the app may not hold.</para>
/// <para>Acquisition is bounded. A mutex is only ever held for one merge-and-rename,
/// so a wait this long means a peer is wedged or died holding it; the write then
/// proceeds unguarded rather than hanging the UI thread on close, which is the moment
/// placement is saved. An abandoned mutex (holder crashed mid-write) is reported by
/// the runtime as <see cref="AbandonedMutexException"/> and is <i>acquired</i>, not
/// failed — the file it protects is either the pre-rename original or the fully
/// renamed replacement, never a torn document.</para>
/// </remarks>
internal sealed class CrossProcessWriteGuard : IDisposable
{
    /// <summary>
    /// How long to wait for a peer's merge-and-rename before giving up and writing
    /// anyway. Generous relative to the operation (a sub-millisecond merge of a
    /// ~80-byte-per-window document) and short relative to a user noticing a hang.
    /// </summary>
    private const int AcquireTimeoutMs = 5_000;

    private readonly Mutex? _mutex;
    private readonly bool _held;

    private CrossProcessWriteGuard(Mutex? mutex, bool held)
    {
        _mutex = mutex;
        _held = held;
    }

    /// <summary>
    /// Acquire the guard for <paramref name="path"/>. Never throws: a store that
    /// cannot take the mutex still writes, because losing a saved window position is
    /// strictly better than failing an app's shutdown path.
    /// </summary>
    public static CrossProcessWriteGuard Acquire(string path)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, NameFor(path));
            bool held;
            try
            {
                held = mutex.WaitOne(AcquireTimeoutMs, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                // The previous holder died between rename and release. We now own it,
                // and the protected file is intact by construction (rename is atomic).
                held = true;
            }

            if (!held)
            {
                DiagnosticLog.SwallowedError(
                    LogCategory.Persistence,
                    "JsonFileStore.CrossProcessWriteGuard.timeout",
                    new TimeoutException(
                        $"Timed out after {AcquireTimeoutMs} ms waiting for the persistence write lock; "
                        + "writing without it. A concurrent writer may lose an entry."));
            }

            return new CrossProcessWriteGuard(mutex, held);
        }
        catch (Exception ex) when (ex is IOException
                                     or UnauthorizedAccessException
                                     or global::System.Threading.WaitHandleCannotBeOpenedException)
        {
            // Named-object creation can fail under an unusual ACL or a name collision
            // with a non-mutex object. Degrade to in-process locking only.
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.CrossProcessWriteGuard", ex);
            mutex?.Dispose();
            return new CrossProcessWriteGuard(null, held: false);
        }
    }

    /// <summary>
    /// Kernel object name for a store path. Hashed because mutex names cannot contain
    /// <c>\</c> (beyond the scope prefix) and are capped at MAX_PATH, while store paths
    /// contain separators and are arbitrarily long. Upper-cased first so two casings of
    /// one path on a case-insensitive filesystem map to the same object.
    /// </summary>
    private static string NameFor(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return "Local\\Reactor.Persistence.JsonFileStore." + Convert.ToHexString(hash, 0, 16);
    }

    public void Dispose()
    {
        if (_mutex is null) return;
        try
        {
            if (_held) _mutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            // Not the owner — cannot happen on the single acquire/release path above,
            // but releasing must never throw out of a store write.
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.CrossProcessWriteGuard.release", ex);
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
