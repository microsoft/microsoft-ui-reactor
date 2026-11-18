using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml.Controls;

namespace Reactor.External.TestControl;

public sealed class ForwardedEventArgs(int sequence) : EventArgs
{
    public int Sequence { get; } = sequence;
}

public sealed partial class ForwardedEventProbe : Control
{
    private int _value;
    private int _other;
    private EventHandler<ForwardedEventArgs>? _changed;
    private EventHandler<ForwardedEventArgs>? _pinged;
    private readonly Border _child = new();

    public int ChangedSubscriptions { get; private set; }
    public int PingSubscriptions { get; private set; }
    public object Child => _child;

    public int Value
    {
        get => _value;
        set { _value = value; _changed?.Invoke(_child, new(-1)); }
    }

    public int Other
    {
        get => _other;
        set { _other = value; _changed?.Invoke(_child, new(-1)); }
    }

    public event EventHandler<ForwardedEventArgs> Changed
    {
        add { _changed += value; ChangedSubscriptions++; }
        remove { _changed -= value; ChangedSubscriptions--; }
    }

    public event EventHandler<ForwardedEventArgs> Pinged
    {
        add { _pinged += value; PingSubscriptions++; }
        remove { _pinged -= value; PingSubscriptions--; }
    }

    public void Edit(int value, object? sender, ForwardedEventArgs args)
    {
        _value = value;
        _other = value + 100;
        _changed?.Invoke(sender, args);
    }

    public void Ping(object? sender, ForwardedEventArgs args) => _pinged?.Invoke(sender, args);

    public Delegate[] SnapshotSubscriptions() =>
        (_changed?.GetInvocationList() ?? []).Concat(_pinged?.GetInvocationList() ?? []).ToArray();
}

[GenerateReactorWrapper(typeof(ForwardedEventProbe), AutoDiscover = false,
    Include = new[] { "Value" }, RegisterAssembly = false)]
[WrapControlled("Value", ChangedEvent = "Changed")]
public partial record ForwardedSingleElement;

[GenerateReactorWrapper(typeof(ForwardedEventProbe), AutoDiscover = false,
    Include = new[] { "Value" }, RegisterAssembly = false)]
[WrapControlled("Value", ChangedEvent = "Changed", Deferred = true)]
public partial record ForwardedDeferredElement;

[GenerateReactorWrapper(typeof(ForwardedEventProbe),
    Exclude = new[] { "Value", "Other" }, RegisterAssembly = false)]
public partial record ForwardedOrdinaryElement;

[GenerateReactorWrapper(typeof(ForwardedEventProbe), RegisterAssembly = false)]
[WrapControlled("Value", ChangedEvent = "Changed")]
[WrapControlled("Other", ChangedEvent = "Changed")]
public partial record ForwardedCombinedElement;
