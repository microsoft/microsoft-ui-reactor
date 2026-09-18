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
/// privilege. A holder that is killed releases it when the OS closes its handle, so a
/// crash cannot wedge the store. The empty lock file is left on disk; only the handle
/// is released. See <see cref="Acquire"/> for why <c>DeleteOnClose</c> is deliberately
/// not used.</para>
/// <para><b>On timeout the write is abandoned, not forced.</b> Proceeding unguarded
/// would reintroduce exactly the lost update this type exists to prevent — the blocked
/// writer would merge a stale document and its commit would drop the peer's entry.
/// Skipping costs at most this window's placement, which the store's best-effort
/// contract already permits; proceeding would corrupt another window's. The wait is
/// kept short because <c>SavePlacement</c> runs on the UI thread at window close, so
/// the bound is user-visible.</para>
/// </remarks>
internal sealed class CrossProcessWriteGuard : IDisposable
{
    /// <summary>
    /// How long to wait for a peer's merge-and-commit before abandoning the write.
    /// </summary>
    /// <remarks>
    /// Deliberately short. <c>SavePlacement</c> runs on the UI thread on window close,
    /// so this wait is user-visible: a long timeout would trade a rare lost placement
    /// for a common visible freeze. A merge is sub-millisecond on a document of ~80
    /// bytes per window, so real contention clears in single-digit milliseconds and
    /// anything approaching this bound means a wedged peer, where waiting longer would
    /// not have helped anyway.
    /// </remarks>
    private const int AcquireTimeoutMs = 250;

    private const int RetryDelayMs = 5;

    /// <summary>
    /// Test-only override for <see cref="AcquireTimeoutMs"/>. The production bound is
    /// deliberately short because it is paid on the UI thread, which makes a
    /// many-writer stress test load-dependent: with enough contenders on a saturated
    /// CPU some writers legitimately time out and skip, so an "all writers land"
    /// assertion would flake for a reason unrelated to the mutual exclusion it is
    /// testing. Tests that measure the *exclusion* raise this; the skip-on-timeout
    /// behaviour is covered separately by its own test.
    /// </summary>
    internal static int? AcquireTimeoutOverrideMs { get; set; }

    /// <summary>ERROR_SHARING_VIOLATION — the peer holds the lock.</summary>
    private const int HResultSharingViolation = unchecked((int)0x80070020);

    /// <summary>ERROR_LOCK_VIOLATION — transient, seen while a peer closes.</summary>
    private const int HResultLockViolation = unchecked((int)0x80070021);

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
        var timeoutMs = AcquireTimeoutOverrideMs ?? AcquireTimeoutMs;
        var deadline = Environment.TickCount64 + timeoutMs;

        while (true)
        {
            try
            {
                var dir = global::System.IO.Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // FileShare.None is the exclusion. The lock file is deliberately left
                // on disk rather than opened with FileOptions.DeleteOnClose: a
                // delete-pending file makes Windows fail other opens with
                // ERROR_ACCESS_DENIED instead of a sharing violation, which is
                // indistinguishable from a real ACL failure and so cannot be retried
                // safely. Measured: with DeleteOnClose, 8 contending writers lost 2
                // writes to spurious "access denied" give-ups. The residue is one
                // empty file beside the store.
                var fs = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1);
                return new CrossProcessWriteGuard(fs);
            }
            catch (IOException ex) when (ex.HResult == HResultSharingViolation
                                            || ex.HResult == HResultLockViolation)
            {
                // Held by a peer, or the peer is mid-close. Only these are worth
                // retrying: a path-too-long, missing-drive or failing-disk IOException
                // will not resolve by waiting, and retrying it would burn the whole
                // timeout on the UI thread before abandoning the write anyway.
                if (Environment.TickCount64 >= deadline)
                {
                    DiagnosticLog.SwallowedError(
                        LogCategory.Persistence,
                        "JsonFileStore.CrossProcessWriteGuard.timeout",
                        new TimeoutException(
                            $"Timed out after {timeoutMs} ms waiting for the persistence "
                            + "write lock; skipping this write rather than risking a lost update."));
                    return new CrossProcessWriteGuard(null);
                }
                Thread.Sleep(RetryDelayMs);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Non-retryable: a directory at the lock path, a read-only or missing
                // location, an ACL denial, a path length problem, a disk fault.
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
            // Releasing the handle is what frees the lock; the empty lock file itself
            // is left behind deliberately (see Acquire). Releasing must never throw
            // out of a store write.
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.CrossProcessWriteGuard.release", ex);
        }
    }
}
