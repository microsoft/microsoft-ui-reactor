namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Storage adapter for <see cref="WindowSpec.PersistenceId"/>. Implementations
/// retain opaque per-id byte payloads; Reactor handles the serialization shape
/// (<c>WINDOWPLACEMENT</c> + monitor-layout fingerprint). (spec 036 §8)
/// </summary>
/// <remarks>
/// <para>Implementations must be thread-safe for concurrent reads from
/// arbitrary threads (Reactor reads on the UI thread today, but production
/// callers may share a store across multiple processes / consoles). Writes
/// happen on the UI thread on close.</para>
/// <para>Implementations must never throw into the caller. Read failures
/// return <c>false</c> with the out-parameter <c>data</c> set to <c>null</c>;
/// write failures are swallowed with a diagnostic log line. Reactor treats
/// the store as best-effort — a corrupt or missing entry simply falls back
/// to the spec's default placement.</para>
/// </remarks>
public interface IWindowPersistenceStore
{
    /// <summary>
    /// Try to read the payload stored under <paramref name="id"/>. Returns
    /// <c>true</c> when an entry exists and was deserialized successfully.
    /// </summary>
    bool TryRead(string id, out byte[]? data);

    /// <summary>
    /// Persist <paramref name="data"/> under <paramref name="id"/>. Best
    /// effort — implementations must not throw.
    /// </summary>
    void Write(string id, byte[] data);
}
