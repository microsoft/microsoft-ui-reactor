using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Serializes <see cref="JsonFileStore"/>'s read-merge-write across processes and
/// logon sessions. (spec 063 §5)
/// </summary>
/// <remarks>
/// <para>The store merges the existing document with the incoming id and replaces the
/// original. Two writers that both read before either commits will each write a
/// document missing the other's id, and the second commit wins — so entries disappear
/// even though the writers touched <i>different</i> ids. An in-process lock cannot see
/// the other process, so the exclusion has to be a kernel object.</para>
/// <para><b>Why a lock file rather than a named mutex.</b> A mutex name carries a
/// scope: <c>Local\</c> is per logon session, so the same user in two sessions
/// (concurrent RDP, or a service alongside a desktop logon) would take <i>different</i>
/// mutexes for one file and race anyway. <c>Global\</c> spans sessions but creating one
/// needs <c>SeCreateGlobalPrivilege</c>, which a standard-user process may not hold. A
/// lock file inherits the scope of the thing it protects: it sits beside the store, so
/// any principal that can write the store can take it, across sessions, with no
/// privilege.</para>
/// <para><b>On timeout the write is abandoned, not forced.</b> Proceeding unguarded
/// would reintroduce exactly the lost update this type exists to prevent — the blocked
/// writer would merge a stale document and its commit would drop the peer's entry.
/// Skipping costs at most this window's placement, which the store's best-effort
/// contract already permits; proceeding would corrupt another window's.</para>
/// </remarks>
internal sealed class CrossProcessWriteGuard : IDisposable
{
    /// <summary>
    /// How long to wait for a peer's merge-and-commit. Generous relative to the
    /// operation (a sub-millisecond merge of a ~80-byte-per-window document) and short
    /// relative to a user noticing a delay on window close, which is when placement is
    /// saved.
    /// </summary>
    private const int AcquireTimeoutMs = 5_000;

    private const int RetryDelayMs = 15;

    private readonly FileStream? _lockFile;

    private CrossProcessWriteGuard(FileStream? lockFile) => _lockFile = lockFile;

    /// <summary>
    /// Whether exclusive access was actually obtained. When false the caller must
    /// <b>not</b> write — see the timeout rationale on the type.
    /// </summary>
    internal bool IsHeld => _lockFile is not null;

    /// <summary>The lock-file path used for a given store path. Exposed for tests.</summary>
    internal static string LockPathFor(string storePath) => storePath + ".lock";

    /// <summary>
    /// Try to take the write lock for <paramref name="path"/>. Never throws; a caller
    /// that cannot take it is told so via <see cref="IsHeld"/> rather than by an
    /// exception, because persistence failures must not surface into app shutdown.
    /// </summary>
    public static CrossProcessWriteGuard Acquire(string path)
    {
        var lockPath = LockPathFor(path);
        var deadline = Environment.TickCount64 + AcquireTimeoutMs;

        while (true)
        {
            try
            {
                var dir = global::System.IO.Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // FileShare.None is the exclusion. DeleteOnClose keeps the directory
                // tidy, and means a lock abandoned by a killed process is released by
                // the OS when its handle closes — a crash cannot wedge the store.
                var fs = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
                return new CrossProcessWriteGuard(fs);
            }
            catch (IOException)
            {
                // Held by a peer (sharing violation), or the peer is mid-close, which
                // on Windows can briefly surface the same way.
                if (Environment.TickCount64 >= deadline)
                {
                    DiagnosticLog.SwallowedError(
                        LogCategory.Persistence,
                        "JsonFileStore.CrossProcessWriteGuard.timeout",
                        new TimeoutException(
                            $"Timed out after {AcquireTimeoutMs} ms waiting for the persistence "
                            + "write lock; skipping this write rather than risking a lost update."));
                    return new CrossProcessWriteGuard(null);
                }
                Thread.Sleep(RetryDelayMs);
            }
            catch (UnauthorizedAccessException ex)
            {
                // A directory at the lock path, a read-only location, or an ACL denial.
                // Not retryable.
                DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.CrossProcessWriteGuard", ex);
                return new CrossProcessWriteGuard(null);
            }
        }
    }

    public void Dispose()
    {
        if (_lockFile is null) return;
        try
        {
            _lockFile.Dispose();
        }
        catch (IOException ex)
        {
            // DeleteOnClose can fail if the file was removed underneath us; releasing
            // must never throw out of a store write.
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.CrossProcessWriteGuard.release", ex);
        }
    }
}
