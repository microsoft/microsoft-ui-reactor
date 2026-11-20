// Records ETW events the way `winapp perf` does in its default, non-elevated mode, then
// decodes them with TDH.
//
// Why this exists
// ---------------
// Reactor's perf spans (Microsoft-UI-Reactor-Perf) exist because a managed EventSource never
// turns on under a *private* ETW session, which is the only kind a non-elevated tool can
// attach to a running app with. A test that used a normal (system) session, or an in-process
// EventListener, would pass for the EventSource too and prove nothing. So this starts the
// same session winapp starts — EVENT_TRACE_PRIVATE_LOGGER_MODE with a PID scope filter, no
// elevation — writes an ETL file, and reads it back through TdhGetEventInformation, the API
// winapp's reader uses for TraceLogging events.
//
// Shared by the headless unit tests (provider + wire schema) and the selftests (spans from a
// live render), so both tiers measure through the same instrument.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Reactor.Tests.Shared;

/// <summary>One event read back from an ETL file, decoded through TDH.</summary>
internal sealed record CapturedEtwEvent(
    Guid Provider,
    ushort Id,
    byte Opcode,
    byte Level,
    ulong Keyword,
    int ProcessId,
    int ThreadId,
    long Timestamp,
    string? Name,
    bool IsTraceLogging,
    IReadOnlyList<(string Name, int InType)> Schema,
    IReadOnlyDictionary<string, object> Fields,
    string? DecodeError);

/// <summary>A private, PID-scoped ETW file session, like <c>winapp perf</c>'s attach mode.</summary>
internal sealed unsafe class EtwPrivateCapture : IDisposable
{
    private const uint PrivateLoggerMode = 0x800;
    private const uint SequentialFileMode = 0x1;
    private const uint PidFilterType = 0x80000004;
    private const uint WnodeTracedGuid = 0x00020000;
    private const uint WnodeVersionedProperties = 0x00800000;
    private const uint ControlStop = 1;
    private const int StringBytes = 2048;
    private const int PropertiesSize = 144; // sizeof(EVENT_TRACE_PROPERTIES_V2), x64

    private readonly string _name;
    private readonly uint _processId;
    private byte* _properties;
    private uint* _pid;
    private EventFilterDescriptor* _filter;
    private ulong _handle;
    private bool _active;

    public string EtlPath { get; }

    private EtwPrivateCapture(string name, string etlPath, int processId)
    {
        _name = name;
        EtlPath = etlPath;
        _processId = (uint)processId;
    }

    /// <summary>
    /// Starts a private logger scoped to <paramref name="processId"/> (the current process by
    /// default), writing to <paramref name="etlPath"/>. Throws <see cref="Win32Exception"/>
    /// when ETW refuses (for instance a user outside Performance Log Users and not elevated).
    /// </summary>
    public static EtwPrivateCapture Start(string etlPath, int? processId = null)
    {
        var capture = new EtwPrivateCapture(
            "ReactorPerfTest-" + Guid.NewGuid().ToString("N"), etlPath, processId ?? Environment.ProcessId);
        try
        {
            capture.StartCore();
            return capture;
        }
        catch
        {
            capture.Free();
            throw;
        }
    }

    private void StartCore()
    {
        _pid = (uint*)NativeMemory.Alloc(sizeof(uint));
        *_pid = _processId;
        _filter = (EventFilterDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(EventFilterDescriptor));
        _properties = (byte*)NativeMemory.AllocZeroed((nuint)(PropertiesSize + 2 * StringBytes));
        WriteProperties(_properties);
        ulong handle;
        fixed (char* name = _name)
            Check(StartTraceW(&handle, name, _properties), "StartTrace");
        _handle = handle;
        _active = true;
    }

    private void WriteProperties(byte* p)
    {
        *(uint*)(p + 0) = (uint)(PropertiesSize + 2 * StringBytes);   // Wnode.BufferSize
        *(Guid*)(p + 24) = Guid.NewGuid();                             // Wnode.Guid
        *(uint*)(p + 40) = 1;                                          // Wnode.ClientContext: QPC
        *(uint*)(p + 44) = WnodeTracedGuid | WnodeVersionedProperties; // Wnode.Flags
        *(uint*)(p + 48) = 64;                                         // BufferSize (KB)
        *(uint*)(p + 52) = 8;                                          // MinimumBuffers
        *(uint*)(p + 56) = 64;                                         // MaximumBuffers
        *(uint*)(p + 64) = PrivateLoggerMode | SequentialFileMode;     // LogFileMode
        *(uint*)(p + 68) = 1;                                          // FlushTimer (s)
        *(uint*)(p + 112) = PropertiesSize + StringBytes;              // LogFileNameOffset
        *(uint*)(p + 116) = PropertiesSize;                            // LoggerNameOffset
        *(uint*)(p + 120) = 2;                                         // V2Control
        // A private logger runs inside the target process, so its PID scope is part of the
        // session itself (as in winapp's PrivateEtwSession).
        *_filter = new EventFilterDescriptor { Ptr = (ulong)_pid, Size = sizeof(uint), Type = PidFilterType };
        *(uint*)(p + 124) = 1;                                         // FilterDescCount
        *(ulong*)(p + 128) = (ulong)_filter;                           // FilterDesc
        var path = new Span<char>(p + PropertiesSize + StringBytes, StringBytes / 2);
        EtlPath.AsSpan().CopyTo(path);
        path[EtlPath.Length] = '\0';
    }

    /// <summary>Enables <paramref name="provider"/> in this session, scoped to the target PID.</summary>
    public void Enable(Guid provider, byte level, ulong matchAnyKeyword)
    {
        if (!_active) throw new ObjectDisposedException(nameof(EtwPrivateCapture));
        var filter = new EventFilterDescriptor { Ptr = (ulong)_pid, Size = sizeof(uint), Type = PidFilterType };
        var parameters = new EnableTraceParameters
        {
            Version = 2,
            EnableFilterDesc = &filter,
            FilterDescCount = 1,
        };
        Check(EnableTraceEx2(_handle, &provider, 1, level, matchAnyKeyword, 0, 5000, &parameters),
            $"EnableTraceEx2({provider})");
    }

    /// <summary>Stops the session, flushing every buffer to <see cref="EtlPath"/>.</summary>
    public void Stop()
    {
        if (!_active) return;
        _active = false;
        fixed (char* name = _name)
            Check(ControlTraceW(_handle, name, _properties, ControlStop), "ControlTrace(STOP)");
    }

    public void Dispose()
    {
        try { Stop(); }
        catch (Win32Exception ex)
        {
            // Dispose must not throw; the session dies with the process anyway (private logger).
            global::System.Diagnostics.Trace.WriteLine("EtwPrivateCapture: stop failed: " + ex.Message);
        }
        finally { Free(); }
    }

    private void Free()
    {
        NativeMemory.Free(_properties);
        NativeMemory.Free(_pid);
        NativeMemory.Free(_filter);
        _properties = null;
        _pid = null;
        _filter = null;
    }

    private static void Check(uint status, string operation)
    {
        if (status != 0)
            throw new Win32Exception((int)status, $"{operation} failed ({status}).");
    }

    // ── Reading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every event of <paramref name="provider"/> from <paramref name="etlPath"/>,
    /// decoding fields through <c>TdhGetEventInformation</c>.
    /// </summary>
    public static List<CapturedEtwEvent> Read(string etlPath, Guid provider)
    {
        var state = new ReadState(provider);
        var handle = GCHandle.Alloc(state);
        var logfile = (byte*)NativeMemory.AllocZeroed(LogfileSize);
        try
        {
            fixed (char* path = etlPath)
            {
                *(char**)(logfile + 0) = path;                                             // LogFileName
                *(uint*)(logfile + 28) = ProcessTraceModeEventRecord;                    // ProcessTraceMode
                *(nint*)(logfile + 424) = (nint)(delegate* unmanaged<EventRecord*, void>)&OnEvent; // EventRecordCallback
                *(nint*)(logfile + 440) = GCHandle.ToIntPtr(handle);                    // Context
                ulong trace = OpenTraceW(logfile);
                if (trace == InvalidProcessTraceHandle)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"OpenTrace({etlPath}) failed.");
                try
                {
                    Check(ProcessTrace(&trace, 1, null, null), "ProcessTrace");
                }
                finally
                {
                    CloseTrace(trace);
                }
            }
            return state.Events;
        }
        finally
        {
            NativeMemory.Free(logfile);
            handle.Free();
        }
    }

    private sealed class ReadState(Guid provider)
    {
        public Guid Provider { get; } = provider;
        public List<CapturedEtwEvent> Events { get; } = new();
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(EventRecord* record)
    {
        var state = (ReadState)GCHandle.FromIntPtr(record->UserContext).Target!;
        if (record->Header.ProviderId != state.Provider) return;
        state.Events.Add(Decode(record));
    }

    private static CapturedEtwEvent Decode(EventRecord* record)
    {
        string? name = null;
        string? error = null;
        bool traceLogging = false;
        var schema = new List<(string, int)>();
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        uint size = 0;
        uint status = TdhGetEventInformation(record, 0, null, null, &size);
        if (status == 122 /* ERROR_INSUFFICIENT_BUFFER */)
        {
            var info = (byte*)NativeMemory.Alloc(size);
            try
            {
                status = TdhGetEventInformation(record, 0, null, info, &size);
                if (status == 0)
                {
                    traceLogging = *(int*)(info + 48) == DecodingSourceTlg;
                    uint nameOffset = *(uint*)(info + 92);
                    name = nameOffset != 0 ? new string((char*)(info + nameOffset)) : null;
                    uint count = *(uint*)(info + 104);
                    byte* user = (byte*)record->UserData;
                    int position = 0;
                    for (int i = 0; i < count; i++)
                    {
                        byte* property = info + 112 + i * 24;
                        string fieldName = new((char*)(info + *(uint*)(property + 4)));
                        int inType = *(ushort*)(property + 8);
                        schema.Add((fieldName, inType));
                        if (error is not null) continue;
                        try
                        {
                            fields[fieldName] = ReadValue(user, record->UserDataLength, ref position, inType);
                        }
                        catch (InvalidOperationException ex)
                        {
                            error = ex.Message;
                        }
                    }
                    if (error is null && position != record->UserDataLength)
                        error = $"payload has {record->UserDataLength - position} trailing bytes";
                }
            }
            finally
            {
                NativeMemory.Free(info);
            }
        }
        if (status != 0) error ??= $"TdhGetEventInformation failed ({status})";
        var header = record->Header;
        return new CapturedEtwEvent(
            header.ProviderId, header.Descriptor.Id, header.Descriptor.Opcode, header.Descriptor.Level,
            header.Descriptor.Keyword, (int)header.ProcessId, (int)header.ThreadId, header.TimeStamp,
            name, traceLogging, schema, fields, error);
    }

    private static object ReadValue(byte* data, int length, ref int position, int inType)
    {
        switch (inType)
        {
            case 1: // UNICODESTRING, nul-terminated
            {
                int start = position;
                while (true)
                {
                    if (position + 2 > length) throw new InvalidOperationException("unterminated string");
                    char c = *(char*)(data + position);
                    position += 2;
                    if (c == '\0') break;
                }
                return new string((char*)(data + start), 0, (position - start) / 2 - 1);
            }
            case 7: // INT32
                Need(position, 4, length);
                position += 4;
                return *(int*)(data + position - 4);
            case 8: // UINT32
                Need(position, 4, length);
                position += 4;
                return *(uint*)(data + position - 4);
            case 10: // UINT64
                Need(position, 8, length);
                position += 8;
                return *(ulong*)(data + position - 8);
            case 13: // BOOLEAN (4 bytes)
                Need(position, 4, length);
                position += 4;
                return *(int*)(data + position - 4) != 0;
            default:
                throw new InvalidOperationException($"unsupported in-type {inType}");
        }
    }

    private static void Need(int position, int size, int length)
    {
        if (position + size > length) throw new InvalidOperationException("payload too short");
    }

    // ── Interop ─────────────────────────────────────────────────────────

    private const int LogfileSize = 448; // sizeof(EVENT_TRACE_LOGFILEW), x64
    private const uint ProcessTraceModeEventRecord = 0x10000000;
    private const ulong InvalidProcessTraceHandle = ulong.MaxValue;
    private const int DecodingSourceTlg = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventFilterDescriptor
    {
        public ulong Ptr;
        public uint Size;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnableTraceParameters
    {
        public uint Version;
        public uint EnableProperty;
        public uint ControlFlags;
        public Guid SourceId;
        public EventFilterDescriptor* EnableFilterDesc;
        public uint FilterDescCount;
    }

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
    private struct EventHeader
    {
        public ushort Size;
        public ushort HeaderType;
        public ushort Flags;
        public ushort EventProperty;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public EventDescriptor Descriptor;
        public ulong ProcessorTime;
        public Guid ActivityId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventRecord
    {
        public EventHeader Header;
        public uint BufferContext;
        public ushort ExtendedDataCount;
        public ushort UserDataLength;
        public void* ExtendedData;
        public void* UserData;
        public nint UserContext;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(ulong* handle, char* name, byte* properties);

    [DllImport("advapi32.dll")]
    private static extern uint EnableTraceEx2(ulong handle, Guid* provider, uint controlCode, byte level,
        ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, EnableTraceParameters* parameters);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(ulong handle, char* name, byte* properties, uint controlCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(byte* logfile);

    [DllImport("advapi32.dll")]
    private static extern uint ProcessTrace(ulong* handles, uint count, void* startTime, void* endTime);

    [DllImport("advapi32.dll")]
    private static extern uint CloseTrace(ulong handle);

    [DllImport("tdh.dll")]
    private static extern uint TdhGetEventInformation(EventRecord* record, uint contextCount, void* context, byte* buffer, uint* bufferSize);
}
