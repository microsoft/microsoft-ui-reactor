using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Reactor's performance spans, written as TraceLogging events through a raw ETW provider
/// (<c>EventRegister</c> / <c>EventWriteTransfer</c>), so a non-elevated <b>private</b> ETW
/// session — the kind <c>winapp perf</c> attaches with — can record them.
/// </summary>
/// <remarks>
/// <para><b>Why not <see cref="ReactorEventSource"/>.</b> A managed <c>EventSource</c> only
/// turns on for the ETW sessions it finds through <c>EnumerateTraceGuidsEx</c>, which does not
/// list private loggers, so it stays disabled under a private session even though ETW enabled
/// it. A provider registered with <c>EventRegister</c> sees the enable callback directly. The
/// EventSource is kept for its in-process consumers (EventListener, dotnet-trace).</para>
///
/// <para><b>Wire format.</b> Each event carries its TraceLogging metadata in-band, so any
/// TDH-based decoder (WPA, TraceEvent, <c>TdhGetEventInformation</c>) reads names and field
/// types without a manifest. Event ids, opcodes and fields are a stable contract documented
/// in <c>docs/reference/perf-trace-events.md</c>; never renumber or retype a field — add a
/// new event instead.</para>
///
/// <para><b>Cost.</b> The provider registers once per process (first
/// <see cref="EnsureRegistered"/>). With no session listening, every emit site is one
/// <see cref="IsEnabled"/> check: a static field read and compare. Payloads are only computed
/// and written after that check, and writing allocates nothing.</para>
///
/// <para><b>PII.</b> Only framework tokens, developer-authored type names, counts and
/// process-local ids — never props, state values, keys, text or paths
/// (spec 044 §6.2.1).</para>
/// </remarks>
internal static unsafe partial class ReactorPerfTrace
{
    /// <summary>The provider name. Its GUID is the EventSource-style name hash of it.</summary>
    internal const string ProviderName = "Microsoft-UI-Reactor-Perf";

    /// <summary>
    /// <see cref="ProviderName"/> hashed the way <c>EventSource</c>, <c>xperf</c> and
    /// <c>tracelog</c> derive a GUID from <c>*Microsoft-UI-Reactor-Perf</c>.
    /// </summary>
    internal static readonly Guid ProviderId = new(0x0902a96c, 0x4e92, 0x5b91, 0x4d, 0x2b, 0x5a, 0x80, 0x58, 0x05, 0x5c, 0x78);

    /// <summary>Keyword bits. A consumer enables any subset; <c>0xF</c> is everything.</summary>
    internal static class Keywords
    {
        /// <summary>RenderPass, Reconcile and Effects spans.</summary>
        public const ulong Pass = 0x1;
        /// <summary>ComponentRender spans.</summary>
        public const ulong Component = 0x2;
        /// <summary>ChildReconcile spans and KeyedListDiagnostic.</summary>
        public const ulong Children = 0x4;
        /// <summary>StateChange.</summary>
        public const ulong State = 0x8;
    }

    /// <summary>ETW levels the events are written at.</summary>
    internal static class Levels
    {
        public const byte Informational = 4;
        public const byte Verbose = 5;
    }

    /// <summary>ETW opcodes: spans are a Start/Stop pair, the rest are Info.</summary>
    internal static class Opcodes
    {
        public const byte Info = 0;
        public const byte Start = 1;
        public const byte Stop = 2;
    }

    /// <summary>Stable event ids. Start and Stop of a span are separate ids (their fields differ).</summary>
    internal static class EventIds
    {
        public const ushort RenderPassStart = 1;
        public const ushort RenderPassStop = 2;
        public const ushort ComponentRenderStart = 3;
        public const ushort ComponentRenderStop = 4;
        public const ushort ReconcileStart = 5;
        public const ushort ReconcileStop = 6;
        public const ushort EffectsStart = 7;
        public const ushort EffectsStop = 8;
        public const ushort ChildReconcileStart = 9;
        public const ushort ChildReconcileStop = 10;
        public const ushort StateChange = 11;
        public const ushort KeyedListDiagnostic = 12;
        // Next free id: 13.
    }

    /// <summary>Values of the Reconcile span's <c>entry</c> field.</summary>
    internal static class ReconcileEntries
    {
        /// <summary>An outermost <c>Reconciler.Reconcile</c> (a host pass, or a row update during layout).</summary>
        public const string Reconcile = "reconcile";
        /// <summary>An outermost <c>Reconciler.Mount</c> (typically a virtualized row realized during layout).</summary>
        public const string Mount = "mount";
    }

    // ── Enable state ────────────────────────────────────────────────────
    // Written only by the ETW enable callback. No initializers: the callback can run inside
    // EventRegister, and nothing here may be reset after it. 0 = no session listening.
    private static int s_levelPlus1;
    private static ulong s_anyKeyword;
    private static ulong s_allKeyword;
    private static long s_regHandle;
    private static int s_registerState; // 0 = not yet, 1 = done (successfully or not)
    private static long s_nextHostId;

    /// <summary>
    /// Whether a session wants events at <paramref name="level"/> with <paramref name="keyword"/>.
    /// Mirrors TraceLogging's <c>TraceLoggingProviderEnabled</c>, except that a session that
    /// asked for no keywords (<c>MatchAnyKeyword == 0</c>) gets every keyword, as EventSource
    /// does — ETW itself still drops what a session's own mask excludes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsEnabled(byte level, ulong keyword)
        => level < s_levelPlus1 && KeywordOn(keyword);

    private static bool KeywordOn(ulong keyword)
    {
        ulong any = s_anyKeyword;
        ulong all = s_allKeyword;
        return any == 0 || ((keyword & any) != 0 && (keyword & all) == all);
    }

    internal static bool PassEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsEnabled(Levels.Informational, Keywords.Pass);
    }

    internal static bool ComponentEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsEnabled(Levels.Informational, Keywords.Component);
    }

    internal static bool ChildrenEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsEnabled(Levels.Verbose, Keywords.Children);
    }

    internal static bool StateEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsEnabled(Levels.Verbose, Keywords.State);
    }

    /// <summary>Process-unique host id for <c>RenderPass</c>; never 0.</summary>
    internal static ulong NextHostId() => (ulong)Interlocked.Increment(ref s_nextHostId);

    /// <summary>
    /// Registers the provider once per process. Called when a reconciler is created, so the
    /// provider exists before the first render and any session (already running or started
    /// later) enables it. Registration failure leaves tracing off; it never throws.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (Volatile.Read(ref s_registerState) != 0) return;
        if (Interlocked.CompareExchange(ref s_registerState, 1, 0) != 0) return;
        Register();
    }

    private static void Register()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Guid id = ProviderId;
            long handle;
            uint status = EventRegister(&id, (nint)(delegate* unmanaged[Stdcall]<Guid*, uint, byte, ulong, ulong, void*, void*, void>)&OnEnable, null, &handle);
            if (status != 0) return;
            // Provider traits carry the name, so TDH shows "Microsoft-UI-Reactor-Perf". Setting
            // traits also turns on the descriptor Type field the metadata blobs rely on.
            var traits = Metadata.Provider;
            EventSetInformation(handle, EventProviderSetTraits, traits.Pointer, traits.Size);
            Volatile.Write(ref s_regHandle, handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No ETW (not Windows, or a stripped-down OS): tracing stays off.
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void OnEnable(Guid* sourceId, uint controlCode, byte level, ulong matchAnyKeyword, ulong matchAllKeyword, void* filterData, void* context)
        => ApplyControlCode(controlCode, level, matchAnyKeyword, matchAllKeyword);

    /// <summary>The enable-callback body, separated so it is unit-testable without ETW.</summary>
    internal static void ApplyControlCode(uint controlCode, byte level, ulong matchAnyKeyword, ulong matchAllKeyword)
    {
        switch (controlCode)
        {
            case ControlCodeEnable:
                s_anyKeyword = matchAnyKeyword;
                s_allKeyword = matchAllKeyword;
                // Level 0 means "every level", as in TraceLoggingProvider.h.
                Volatile.Write(ref s_levelPlus1, level != 0 ? level + 1 : 256);
                break;
            case ControlCodeDisable:
                Volatile.Write(ref s_levelPlus1, 0);
                s_anyKeyword = 0;
                s_allKeyword = 0;
                break;
            // ControlCodeCaptureState: nothing to rundown.
        }
    }

    internal static bool IsRegisteredForTests => Volatile.Read(ref s_regHandle) != 0;

    private static long s_writeCount;

    /// <summary>Events handed to ETW so far (see <see cref="Write"/>).</summary>
    internal static long WriteCountForTests => Interlocked.Read(ref s_writeCount);

    /// <summary>
    /// A started <c>ComponentRender</c> span (or an inactive one when the keyword is off);
    /// <see cref="Dispose"/> writes the Stop exactly when a Start was written.
    /// </summary>
    internal readonly struct ComponentSpan : IDisposable
    {
        private readonly ulong _id;
        private readonly bool _active;

        private ComponentSpan(ulong id)
        {
            _id = id;
            _active = true;
        }

        /// <summary>Writes the Start. Callers check <see cref="ComponentEnabled"/> first.</summary>
        internal static ComponentSpan Begin(ulong componentId, string name, string reason)
        {
            ComponentRenderStart(componentId, name, reason);
            return new ComponentSpan(componentId);
        }

        internal bool IsActive => _active;

        public void Dispose()
        {
            if (_active) ComponentRenderStop(_id);
        }
    }

    /// <summary>A started <c>Effects</c> span (or an inactive one when the keyword is off).</summary>
    internal readonly struct EffectsSpan : IDisposable
    {
        private readonly ulong _id;
        private readonly bool _active;

        private EffectsSpan(ulong id)
        {
            _id = id;
            _active = true;
        }

        /// <summary>Writes the Start. Callers check <see cref="PassEnabled"/> first.</summary>
        internal static EffectsSpan Begin(long componentId)
        {
            EffectsStart((ulong)componentId);
            return new EffectsSpan((ulong)componentId);
        }

        public void Dispose()
        {
            if (_active) EffectsStop(_id);
        }
    }

    // ── Events ──────────────────────────────────────────────────────────
    // Every method writes unconditionally: callers check IsEnabled (or the per-keyword
    // properties) first, so a disabled emit site computes nothing. Start and Stop are paired
    // by the caller: a Stop is written whenever its Start was, even if the session went away
    // in between (ETW drops it), so a consumer never sees a Start without its Stop.

    internal static void RenderPassStart(ulong hostId)
        => WriteU64(EventIds.RenderPassStart, Levels.Informational, Opcodes.Start, Keywords.Pass, Metadata.RenderPassStart, hostId);

    internal static void RenderPassStop(ulong hostId)
        => WriteU64(EventIds.RenderPassStop, Levels.Informational, Opcodes.Stop, Keywords.Pass, Metadata.RenderPassStop, hostId);

    internal static void ComponentRenderStart(ulong componentId, string name, string reason)
    {
        fixed (char* pName = name)
        fixed (char* pReason = reason)
        {
            EventData* data = stackalloc EventData[5];
            SetMetadata(data, Metadata.ComponentRenderStart);
            data[2] = EventData.Of(&componentId, sizeof(ulong));
            data[3] = EventData.Of(pName, (name.Length + 1) * sizeof(char));
            data[4] = EventData.Of(pReason, (reason.Length + 1) * sizeof(char));
            Write(EventIds.ComponentRenderStart, Levels.Informational, Opcodes.Start, Keywords.Component, data, 5);
        }
    }

    internal static void ComponentRenderStop(ulong componentId)
        => WriteU64(EventIds.ComponentRenderStop, Levels.Informational, Opcodes.Stop, Keywords.Component, Metadata.ComponentRenderStop, componentId);

    internal static void ReconcileStart(string entry, string element)
    {
        fixed (char* pEntry = entry)
        fixed (char* pElement = element)
        {
            EventData* data = stackalloc EventData[4];
            SetMetadata(data, Metadata.ReconcileStart);
            data[2] = EventData.Of(pEntry, (entry.Length + 1) * sizeof(char));
            data[3] = EventData.Of(pElement, (element.Length + 1) * sizeof(char));
            Write(EventIds.ReconcileStart, Levels.Informational, Opcodes.Start, Keywords.Pass, data, 4);
        }
    }

    internal static void ReconcileStop()
    {
        EventData* data = stackalloc EventData[2];
        SetMetadata(data, Metadata.ReconcileStop);
        Write(EventIds.ReconcileStop, Levels.Informational, Opcodes.Stop, Keywords.Pass, data, 2);
    }

    internal static void EffectsStart(ulong componentId)
        => WriteU64(EventIds.EffectsStart, Levels.Informational, Opcodes.Start, Keywords.Pass, Metadata.EffectsStart, componentId);

    internal static void EffectsStop(ulong componentId)
        => WriteU64(EventIds.EffectsStop, Levels.Informational, Opcodes.Stop, Keywords.Pass, Metadata.EffectsStop, componentId);

    internal static void ChildReconcileStart(uint oldCount, uint newCount)
    {
        EventData* data = stackalloc EventData[4];
        SetMetadata(data, Metadata.ChildReconcileStart);
        data[2] = EventData.Of(&oldCount, sizeof(uint));
        data[3] = EventData.Of(&newCount, sizeof(uint));
        Write(EventIds.ChildReconcileStart, Levels.Verbose, Opcodes.Start, Keywords.Children, data, 4);
    }

    internal static void ChildReconcileStop(uint inserted, uint removed, uint moved, uint replaced)
    {
        EventData* data = stackalloc EventData[6];
        SetMetadata(data, Metadata.ChildReconcileStop);
        data[2] = EventData.Of(&inserted, sizeof(uint));
        data[3] = EventData.Of(&removed, sizeof(uint));
        data[4] = EventData.Of(&moved, sizeof(uint));
        data[5] = EventData.Of(&replaced, sizeof(uint));
        Write(EventIds.ChildReconcileStop, Levels.Verbose, Opcodes.Stop, Keywords.Children, data, 6);
    }

    internal static void StateChange(ulong componentId, string hook, bool changed)
    {
        int changed32 = changed ? 1 : 0;
        fixed (char* pHook = hook)
        {
            EventData* data = stackalloc EventData[5];
            SetMetadata(data, Metadata.StateChange);
            data[2] = EventData.Of(&componentId, sizeof(ulong));
            data[3] = EventData.Of(pHook, (hook.Length + 1) * sizeof(char));
            data[4] = EventData.Of(&changed32, sizeof(int));
            Write(EventIds.StateChange, Levels.Verbose, Opcodes.Info, Keywords.State, data, 5);
        }
    }

    internal static void KeyedListDiagnostic(string kind, bool bailedOut)
    {
        int bailedOut32 = bailedOut ? 1 : 0;
        fixed (char* pKind = kind)
        {
            EventData* data = stackalloc EventData[4];
            SetMetadata(data, Metadata.KeyedListDiagnostic);
            data[2] = EventData.Of(pKind, (kind.Length + 1) * sizeof(char));
            data[3] = EventData.Of(&bailedOut32, sizeof(int));
            Write(EventIds.KeyedListDiagnostic, Levels.Verbose, Opcodes.Info, Keywords.Children, data, 4);
        }
    }

    private static void WriteU64(ushort id, byte level, byte opcode, ulong keyword, Blob metadata, ulong value)
    {
        EventData* data = stackalloc EventData[3];
        SetMetadata(data, metadata);
        data[2] = EventData.Of(&value, sizeof(ulong));
        Write(id, level, opcode, keyword, data, 3);
    }

    private static void SetMetadata(EventData* data, Blob eventMetadata)
    {
        var provider = Metadata.Provider;
        data[0] = new EventData { Ptr = (ulong)provider.Pointer, Size = provider.Size, Reserved = DescriptorTypeProviderMetadata };
        data[1] = new EventData { Ptr = (ulong)eventMetadata.Pointer, Size = eventMetadata.Size, Reserved = DescriptorTypeEventMetadata };
    }

    private static void Write(ushort id, byte level, byte opcode, ulong keyword, EventData* data, uint count)
    {
        long handle = Volatile.Read(ref s_regHandle);
        if (handle == 0) return;
        // Only reached on an enabled path, so the count costs nothing when nobody listens.
        // Tests compare it with what a session recorded: a write the session did not ask for
        // means an emit site skipped its enable check.
        Interlocked.Increment(ref s_writeCount);
        var descriptor = new EventDescriptor
        {
            Id = id,
            Version = 0,
            Channel = TraceLoggingChannel,
            Level = level,
            Opcode = opcode,
            Task = 0,
            Keyword = keyword,
        };
        EventWriteTransfer(handle, &descriptor, null, null, count, data);
    }

    // ── TraceLogging metadata ───────────────────────────────────────────

    /// <summary>
    /// A metadata blob in a pinned array. The blob holds the array itself, not just its
    /// address: a pinned (POH) array is never moved but is still collected once unreachable,
    /// and ETW reads it by pointer on every write. The static fields that hold the blobs
    /// root the arrays for the process lifetime.
    /// </summary>
    internal readonly struct Blob
    {
        private readonly byte[] _pinned;

        internal Blob(byte[] pinned) => _pinned = pinned;

        public byte* Pointer => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_pinned));
        public uint Size => (uint)_pinned.Length;
        public ReadOnlySpan<byte> Span => _pinned;
    }

    /// <summary>TraceLogging field in-types used by this provider (TraceLoggingProvider.h <c>TlgIn_t</c>).</summary>
    internal static class InTypes
    {
        public const byte UnicodeString = 1; // nul-terminated UTF-16
        public const byte UInt32 = 8;
        public const byte UInt64 = 10;
        public const byte Bool32 = 13;
    }

    /// <summary>
    /// The in-band metadata, built once on first use into pinned arrays. Kept in its own class
    /// so the hot <see cref="IsEnabled"/> path never waits on (or runs) this initializer.
    /// </summary>
    internal static class Metadata
    {
        public static readonly Blob Provider = BuildProvider(ProviderName);

        public static readonly Blob RenderPassStart = BuildEvent("RenderPass", ("hostId", InTypes.UInt64));
        public static readonly Blob RenderPassStop = BuildEvent("RenderPass", ("hostId", InTypes.UInt64));
        public static readonly Blob ComponentRenderStart = BuildEvent("ComponentRender",
            ("componentId", InTypes.UInt64), ("name", InTypes.UnicodeString), ("reason", InTypes.UnicodeString));
        public static readonly Blob ComponentRenderStop = BuildEvent("ComponentRender", ("componentId", InTypes.UInt64));
        public static readonly Blob ReconcileStart = BuildEvent("Reconcile",
            ("entry", InTypes.UnicodeString), ("element", InTypes.UnicodeString));
        public static readonly Blob ReconcileStop = BuildEvent("Reconcile");
        public static readonly Blob EffectsStart = BuildEvent("Effects", ("componentId", InTypes.UInt64));
        public static readonly Blob EffectsStop = BuildEvent("Effects", ("componentId", InTypes.UInt64));
        public static readonly Blob ChildReconcileStart = BuildEvent("ChildReconcile",
            ("oldCount", InTypes.UInt32), ("newCount", InTypes.UInt32));
        public static readonly Blob ChildReconcileStop = BuildEvent("ChildReconcile",
            ("inserted", InTypes.UInt32), ("removed", InTypes.UInt32), ("moved", InTypes.UInt32), ("replaced", InTypes.UInt32));
        public static readonly Blob StateChange = BuildEvent("StateChange",
            ("componentId", InTypes.UInt64), ("hook", InTypes.UnicodeString), ("changed", InTypes.Bool32));
        public static readonly Blob KeyedListDiagnostic = BuildEvent("KeyedListDiagnostic",
            ("kind", InTypes.UnicodeString), ("bailedOut", InTypes.Bool32));

        // Provider traits: UINT16 size (counting itself), then the nul-terminated UTF-8 name.
        private static Blob BuildProvider(string name)
        {
            var bytes = new List<byte>(name.Length + 3) { 0, 0 };
            AppendString(bytes, name);
            return Pin(bytes);
        }

        // Event metadata: UINT16 size (counting itself), a tag byte (0), the nul-terminated
        // UTF-8 event name, then per field its nul-terminated name and in-type byte.
        internal static Blob BuildEvent(string name, params (string Name, byte InType)[] fields)
        {
            var bytes = new List<byte>(64) { 0, 0, 0 };
            AppendString(bytes, name);
            foreach (var (fieldName, inType) in fields)
            {
                AppendString(bytes, fieldName);
                bytes.Add(inType);
            }
            return Pin(bytes);
        }

        private static void AppendString(List<byte> bytes, string value)
        {
            bytes.AddRange(global::System.Text.Encoding.UTF8.GetBytes(value));
            bytes.Add(0);
        }

        private static Blob Pin(List<byte> bytes)
        {
            ushort size = checked((ushort)bytes.Count);
            bytes[0] = (byte)size;
            bytes[1] = (byte)(size >> 8);
            var array = GC.AllocateUninitializedArray<byte>(size, pinned: true);
            bytes.CopyTo(array);
            PinnedForTests?.Invoke(array);
            return new Blob(array);
        }

        /// <summary>Observes each metadata array as it is allocated, so a test can watch its lifetime.</summary>
        internal static Action<byte[]>? PinnedForTests { get; set; }
    }

    // ── ETW interop ─────────────────────────────────────────────────────

    private const uint ControlCodeDisable = 0;
    private const uint ControlCodeEnable = 1;
    private const int EventProviderSetTraits = 2;
    private const uint DescriptorTypeEventMetadata = 1;
    private const uint DescriptorTypeProviderMetadata = 2;
    private const byte TraceLoggingChannel = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventDescriptor
    {
        public ushort Id;
        public byte Version;
        public byte Channel;
        public byte Level;
        public byte Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventData
    {
        public ulong Ptr;
        public uint Size;
        public uint Reserved;

        public static EventData Of(void* pointer, int size) => new() { Ptr = (ulong)pointer, Size = (uint)size };
    }

    [LibraryImport("advapi32.dll")]
    private static partial uint EventRegister(Guid* providerId, nint enableCallback, void* callbackContext, long* regHandle);

    [LibraryImport("advapi32.dll")]
    private static partial uint EventSetInformation(long regHandle, int informationClass, void* information, uint informationLength);

    [LibraryImport("advapi32.dll")]
    private static partial uint EventWriteTransfer(long regHandle, EventDescriptor* eventDescriptor, Guid* activityId, Guid* relatedActivityId, uint userDataCount, EventData* userData);
}
