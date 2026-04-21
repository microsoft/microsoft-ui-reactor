using System.Diagnostics;

namespace Microsoft.UI.Reactor.Input;

/// <summary>
/// The drag payload. Phase 6a exposes the typed-payload path only — text/URI/HTML/
/// files/bitmap accessors land in Phase 6b. Typed payloads round-trip inside the
/// same process via a custom format identifier and an in-memory store keyed by a
/// per-drag GUID written into <c>DataPackage.Properties</c>; cross-process drops
/// are rejected automatically when the stored process-id marker doesn't match the
/// current process.
/// </summary>
public sealed class DragData
{
    internal const string ProcIdFormatId = "reactor/proc-id";
    internal const string TransferIdFormatId = "reactor/transfer-id";

    private readonly Dictionary<string, object?> _typedPayloads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _formats = new(StringComparer.Ordinal) { ProcIdFormatId };
    private readonly int _originProcessId = Process.GetCurrentProcess().Id;

    internal DragData() { }

    /// <summary>Origin process id — stored so cross-process drops can be detected/rejected.</summary>
    public int OriginProcessId => _originProcessId;

    /// <summary>Format identifiers this <see cref="DragData"/> advertises.</summary>
    public IReadOnlyCollection<string> AvailableFormats => _formats;

    /// <summary>Returns true when the specified format id is present.</summary>
    public bool HasFormat(string formatId) => _formats.Contains(formatId);

    /// <summary>Factory for a typed-payload-only <see cref="DragData"/>.</summary>
    public static DragData Typed<T>(T payload) => new DragData().WithTypedPayload(payload);

    /// <summary>Attaches a typed payload under the canonical typed-format key for <typeparamref name="T"/>.</summary>
    public DragData WithTypedPayload<T>(T payload)
    {
        var key = TypedFormatId<T>();
        _typedPayloads[key] = payload;
        _formats.Add(key);
        return this;
    }

    /// <summary>Attempts to retrieve a typed payload. Returns false if the format isn't present
    /// or the stored payload can't be cast to <typeparamref name="T"/>.</summary>
    public bool TryGetTypedPayload<T>(out T payload)
    {
        var key = TypedFormatId<T>();
        if (_typedPayloads.TryGetValue(key, out var v) && v is T cast)
        {
            payload = cast;
            return true;
        }
        payload = default!;
        return false;
    }

    /// <summary>Canonical format id used for a typed payload of <typeparamref name="T"/>.</summary>
    public static string TypedFormatId<T>() => "reactor/typed/" + typeof(T).FullName;

    // ── In-memory transfer registry (per-drag GUID → DragData) ───────────
    //
    // Same-process DnD stores the DragData here at DragStarting time; the target
    // pulls it out via the GUID written into DataPackage.Properties[TransferIdFormatId].
    // Entries are removed on DropCompleted (success or cancel). If DropCompleted is
    // never fired (rare WinUI edge case), the entry stays until the next process-wide
    // GC pass clears it — we don't want to hold arbitrary CLR payloads forever.

    private static readonly Dictionary<Guid, WeakReference<DragData>> _transfers = new();
    private static readonly object _transfersLock = new();

    internal static Guid Register(DragData data)
    {
        var id = Guid.NewGuid();
        lock (_transfersLock)
            _transfers[id] = new WeakReference<DragData>(data);
        return id;
    }

    internal static DragData? Resolve(Guid id)
    {
        lock (_transfersLock)
        {
            if (_transfers.TryGetValue(id, out var weak) && weak.TryGetTarget(out var data))
                return data;
        }
        return null;
    }

    internal static void Unregister(Guid id)
    {
        lock (_transfersLock)
            _transfers.Remove(id);
    }
}
