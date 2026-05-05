using System.Diagnostics;

namespace BenchmarkCommon;

// Mirrors microsoft-ui-xaml-lift/.../Common/BlankPerfMetrics.cs.
internal sealed class BlankPerfMetrics
{
    private long _appStartTicks;
    private long _firstFrameTicks;
    private long _interactiveTicks;
    private bool _firstFrameRecorded;
    private bool _finalized;

    public void RecordAppStart()
    {
        _appStartTicks = Stopwatch.GetTimestamp();
    }

    public void RecordFirstFrame()
    {
        if (!_firstFrameRecorded)
        {
            _firstFrameTicks = Stopwatch.GetTimestamp();
            _firstFrameRecorded = true;
            BenchmarkTracing.Log.TraceFirstRender();
        }
    }

    public void RecordInteractive()
    {
        if (!_finalized && _firstFrameRecorded)
        {
            _interactiveTicks = Stopwatch.GetTimestamp();
            _finalized = true;
            BenchmarkTracing.Log.TraceFirstIdle();
        }
    }

    public bool IsFinalized => _finalized;
    public bool IsFirstFrameRecorded => _firstFrameRecorded;
    public long FirstFrameMs => ElapsedMs(_appStartTicks, _firstFrameTicks);
    public long InteractiveMs => ElapsedMs(_appStartTicks, _interactiveTicks);
    public string Summary => $"First Frame: {FirstFrameMs} ms  |  Interactive: {InteractiveMs} ms";

    private static long ElapsedMs(long from, long to)
    {
        return (to - from) * 1000 / Stopwatch.Frequency;
    }
}
