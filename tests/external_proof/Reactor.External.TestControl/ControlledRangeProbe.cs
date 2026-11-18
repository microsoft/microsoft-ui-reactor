using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml.Controls;

namespace Reactor.External.TestControl;

public sealed class RangeProbeChangedEventArgs : EventArgs
{
    public string Endpoint { get; }
    public RangeProbeChangedEventArgs(string endpoint) => Endpoint = endpoint;
}

public enum RangeProbeEcho { Synchronous, Deferred, Silent }

// An explicit positive-control source: unlike Toolkit property assignments,
// MoveStart/MoveEnd always raise the same events as real native input.
public sealed partial class ControlledRangeProbe : Control
{
    private double _start;
    private double _end = 100;
    private int _count;
    private readonly Queue<Action> _echoes = new();
    private EventHandler<RangeProbeChangedEventArgs>? _valueChanged;
    private EventHandler<EventArgs>? _startChanged;
    private EventHandler<RangeProbeChangedEventArgs>? _endChanged;
    private EventHandler<EventArgs>? _countChanged;
    private EventHandler<EventArgs>? _pinged;

    public RangeProbeEcho Echo { get; set; }
    public bool CoerceSibling { get; set; }
    public int ValueSubscriptions { get; private set; }
    public int StartSubscriptions { get; private set; }
    public int EndSubscriptions { get; private set; }
    public int CountSubscriptions { get; private set; }
    public int PingSubscriptions { get; private set; }

    public event EventHandler<RangeProbeChangedEventArgs> ValueChanged
    {
        add { _valueChanged += value; ValueSubscriptions++; }
        remove { _valueChanged -= value; ValueSubscriptions--; }
    }

    public event EventHandler<EventArgs> StartChanged
    {
        add { _startChanged += value; StartSubscriptions++; }
        remove { _startChanged -= value; StartSubscriptions--; }
    }

    public event EventHandler<RangeProbeChangedEventArgs> EndChanged
    {
        add { _endChanged += value; EndSubscriptions++; }
        remove { _endChanged -= value; EndSubscriptions--; }
    }

    public event EventHandler<EventArgs> CounterChanged
    {
        add { _countChanged += value; CountSubscriptions++; }
        remove { _countChanged -= value; CountSubscriptions--; }
    }

    public event EventHandler<EventArgs> Pinged
    {
        add { _pinged += value; PingSubscriptions++; }
        remove { _pinged -= value; PingSubscriptions--; }
    }

    public double RangeStart
    {
        get => _start;
        set
        {
            _start = Math.Clamp(value, 0, 100);
            if (CoerceSibling && _end < _start) _end = _start;
            EchoChange(() => RaiseRange("Start"));
        }
    }

    public double RangeEnd
    {
        get => _end;
        set { _end = Math.Clamp(value, 0, 100); EchoChange(() => RaiseRange("End")); }
    }

    public int Count
    {
        get => _count;
        set { _count = Math.Clamp(value, 0, 100); EchoChange(() => _countChanged?.Invoke(this, EventArgs.Empty)); }
    }

    public void MoveStart(double value) { _start = value; RaiseRange("Start"); }
    public void MoveEnd(double value) { _end = value; RaiseRange("End"); }
    public void MoveBoth(double start, double end) { _start = start; _end = end; RaiseRange("Both"); }
    public void MoveCount(int value)
    {
        _count = value;
        _countChanged?.Invoke(this, EventArgs.Empty);
        RaiseRange("Count");
    }
    public void Ping() => _pinged?.Invoke(this, EventArgs.Empty);
    public void FlushEchoes() { while (_echoes.TryDequeue(out var echo)) echo(); }

    private void EchoChange(Action raise)
    {
        if (Echo == RangeProbeEcho.Synchronous) raise();
        else if (Echo == RangeProbeEcho.Deferred) _echoes.Enqueue(raise);
    }

    private void RaiseRange(string endpoint)
    {
        var args = new RangeProbeChangedEventArgs(endpoint);
        _valueChanged?.Invoke(this, args);
        if (endpoint != "End") _startChanged?.Invoke(this, args);
        if (endpoint != "Start") _endChanged?.Invoke(this, args);
    }
}

[GenerateReactorWrapper(typeof(ControlledRangeProbe), AutoDiscover = false,
    Include = new[] { "RangeStart", "RangeEnd" }, RegisterAssembly = false)]
[WrapControlled("RangeStart", ChangedEvent = "ValueChanged")]
[WrapControlled("RangeEnd", ChangedEvent = "ValueChanged")]
public partial record BareRangeProbeElement;

[GenerateReactorWrapper(typeof(ControlledRangeProbe),
    Exclude = new[] { "Echo", "CoerceSibling" }, RegisterAssembly = false)]
[WrapControlled("RangeStart", ChangedEvent = "ValueChanged")]
[WrapControlled("RangeEnd", ChangedEvent = "ValueChanged")]
[WrapControlled("Count", ChangedEvent = "CounterChanged")]
public partial record SharedRangeProbeElement;

[GenerateReactorWrapper(typeof(ControlledRangeProbe),
    Exclude = new[] { "Echo", "CoerceSibling" }, RegisterAssembly = false)]
[WrapControlled("RangeStart", ChangedEvent = "ValueChanged")]
[WrapControlled("RangeEnd", ChangedEvent = "ValueChanged")]
[WrapControlled("Count", ChangedEvent = "ValueChanged")]
public partial record MixedRangeProbeElement;

[GenerateReactorWrapper(typeof(ControlledRangeProbe),
    Exclude = new[] { "Echo", "CoerceSibling" }, RegisterAssembly = false)]
[WrapControlled("RangeStart", ChangedEvent = "StartChanged")]
[WrapControlled("RangeEnd", ChangedEvent = "EndChanged")]
[WrapControlled("Count", ChangedEvent = "CounterChanged")]
public partial record DistinctRangeProbeElement;

[GenerateReactorWrapper(typeof(ControlledRangeProbe),
    Exclude = new[] { "Echo", "CoerceSibling", "Count" }, RegisterAssembly = false)]
[WrapControlled("RangeStart", Events = new[] { "ValueChanged", "StartChanged" }, Deferred = true)]
[WrapControlled("RangeEnd", Events = new[] { "ValueChanged", "EndChanged" }, Deferred = true)]
public partial record DeferredRangeProbeElement;

[GenerateReactorWrapper(typeof(ControlledRangeProbe),
    Exclude = new[] { "Echo", "CoerceSibling", "RangeEnd", "Count" }, RegisterAssembly = false)]
[WrapControlled("RangeStart", ChangedEvent = "ValueChanged")]
public partial record SingleRangeWithEventElement;
