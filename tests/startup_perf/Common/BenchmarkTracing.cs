using System;
using System.Diagnostics.Tracing;
using System.Threading;

namespace BenchmarkCommon;

// Manifest-based EventSource using WriteEvent() with primitive parameters.
// AOT-safe: avoids EventSource.Write() + [EventData] payload reflection; works under NativeAOT when EventSourceSupport is enabled.
//
// Provider GUID + event names + AppName payload field match -lift exactly.
// Method names match the event names the run_startup_bench.ps1 parser and
// -lift Regions XML expect (wWinMainEntry, XamlAppLoaded, etc.).
//
// Previous implementation used EventSource.Write() with [EventData] structs
// which NativeAOT trims — producing zero ETW events under PublishAot=true.
[EventSource(Name = "BenchmarkSyntheticApps", Guid = "FD80D616-E92B-4B2B-9BED-131ADA36A8FD")]
internal sealed class BenchmarkTracing : EventSource
{
    public static readonly BenchmarkTracing Log = new();

    private string _appName = "Unknown";
    private long _seq;

    private const EventKeywords MeasuresKeyword = (EventKeywords)0x0000400000000000; // MICROSOFT_KEYWORD_MEASURES (bit 46)

    [NonEvent]
    public void SetAppName(string appName) => _appName = appName ?? "Unknown";

    [NonEvent] public void TraceWinMainEntry() => wWinMainEntry(_appName, NextSeq(), Pid());
    [NonEvent] public void TraceXamlAppLoaded() => XamlAppLoaded(_appName, NextSeq(), Pid());
    [NonEvent] public void TraceWindowLoaded() => WindowLoaded(_appName, NextSeq(), Pid());
    [NonEvent] public void TraceFirstRender() => FirstRender(_appName, NextSeq(), Pid());
    [NonEvent] public void TraceFirstIdle() => FirstIdle(_appName, NextSeq(), Pid());
    [NonEvent] public void TraceProcessStop() => ProcessStop(_appName, NextSeq(), Pid());

    [Event(1, Level = EventLevel.Informational, Keywords = MeasuresKeyword)]
    private void wWinMainEntry(string AppName, long Seq, int Pid) => WriteEvent(1, AppName, Seq, Pid);

    [Event(2, Level = EventLevel.Informational, Keywords = MeasuresKeyword)]
    private void XamlAppLoaded(string AppName, long Seq, int Pid) => WriteEvent(2, AppName, Seq, Pid);

    [Event(3, Level = EventLevel.Informational, Keywords = MeasuresKeyword)]
    private void WindowLoaded(string AppName, long Seq, int Pid) => WriteEvent(3, AppName, Seq, Pid);

    [Event(4, Level = EventLevel.Informational, Keywords = MeasuresKeyword)]
    private void FirstRender(string AppName, long Seq, int Pid) => WriteEvent(4, AppName, Seq, Pid);

    [Event(5, Level = EventLevel.Informational, Keywords = MeasuresKeyword)]
    private void FirstIdle(string AppName, long Seq, int Pid) => WriteEvent(5, AppName, Seq, Pid);

    [Event(6, Level = EventLevel.Informational, Keywords = MeasuresKeyword)]
    private void ProcessStop(string AppName, long Seq, int Pid) => WriteEvent(6, AppName, Seq, Pid);

    private long NextSeq() => Interlocked.Increment(ref _seq) - 1;
    private static int Pid() => Environment.ProcessId;
}
