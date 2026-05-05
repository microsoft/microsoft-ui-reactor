using System;
using System.Diagnostics.Tracing;
using System.Threading;

namespace BenchmarkCommon;

// Self-describing TraceLogging via EventSource.Write(). Event names ride
// along with each event so tracerpt can resolve them without a manifest
// (matches how the C++ TraceLoggingProvider macros emit, so RNW and our
// C# variants render identically in WPA / tracerpt).
//
// Provider GUID + event names + AppName payload field match -lift exactly.
// Note: requires PublishAot=false. With NativeAOT, the Write() reflection
// path over [EventData] structs is trimmed and no events are emitted.
[EventSource(Name = "BenchmarkSyntheticApps", Guid = "FD80D616-E92B-4B2B-9BED-131ADA36A8FD")]
internal sealed class BenchmarkTracing : EventSource
{
    public static readonly BenchmarkTracing Log = new();

    private string _appName = "Unknown";
    private long _seq;

    private static readonly EventSourceOptions s_infoMeasures = new()
    {
        Level = EventLevel.Informational,
        Keywords = (EventKeywords)0x0000400000000000 // MICROSOFT_KEYWORD_MEASURES (bit 46)
    };

    [NonEvent]
    public void SetAppName(string appName) => _appName = appName ?? "Unknown";

    [NonEvent] public void TraceWinMainEntry() => Write("wWinMainEntry", s_infoMeasures, new TracePayload(_appName, NextSeq(), Pid()));
    [NonEvent] public void TraceXamlAppLoaded() => Write("XamlAppLoaded", s_infoMeasures, new TracePayload(_appName, NextSeq(), Pid()));
    [NonEvent] public void TraceWindowLoaded() => Write("WindowLoaded", s_infoMeasures, new TracePayload(_appName, NextSeq(), Pid()));
    [NonEvent] public void TraceFirstRender() => Write("FirstRender", s_infoMeasures, new TracePayload(_appName, NextSeq(), Pid()));
    [NonEvent] public void TraceFirstIdle() => Write("FirstIdle", s_infoMeasures, new TracePayload(_appName, NextSeq(), Pid()));
    [NonEvent] public void TraceProcessStop() => Write("ProcessStop", s_infoMeasures, new TracePayload(_appName, NextSeq(), Pid()));

    private long NextSeq() => Interlocked.Increment(ref _seq) - 1;
    private static int Pid() => Environment.ProcessId;

    [EventData]
    private struct TracePayload
    {
        public string AppName { get; set; }
        public long Seq { get; set; }
        public int Pid { get; set; }

        public TracePayload(string appName, long seq, int pid)
        {
            AppName = appName;
            Seq = seq;
            Pid = pid;
        }
    }
}
