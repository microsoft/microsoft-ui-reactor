using System.Threading;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Minimal controllable <see cref="TimeProvider"/> for tests: time only moves when
/// <see cref="Advance"/> is called, and one-shot timers created via <see cref="CreateTimer"/>
/// fire synchronously inside <see cref="Advance"/> once their due time is reached. Enough to
/// drive the <see cref="Microsoft.UI.Reactor.Core.Command.DebounceMs"/> window deterministically
/// without depending on the wall clock.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now;
    private readonly List<FakeTimer> _timers = new();

    public FakeTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? DateTimeOffset.UnixEpoch;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    /// <summary>Number of timers created but not yet disposed — lets tests assert the debounce
    /// path disposes its re-enable timers (on unmount and when re-arming a window).</summary>
    public int ActiveTimerCount
    {
        get { lock (_gate) return _timers.Count; }
    }

    /// <summary>Moves the clock forward and fires any timers that come due.</summary>
    public void Advance(TimeSpan delta)
    {
        FakeTimer[] due;
        lock (_gate)
        {
            _now += delta;
            due = _timers.Where(t => t.IsDue(_now)).ToArray();
        }
        // Fire outside the lock — callbacks may schedule/dispose timers.
        foreach (var t in due) t.Fire();
    }

    /// <summary>Moves the clock forward WITHOUT firing any due timers — simulates a re-enable
    /// timer callback delayed past its deadline (threadpool starvation/suspension), so tests can
    /// prove acceptance is time-based rather than purely timer-callback-driven.</summary>
    public void AdvanceWithoutFiring(TimeSpan delta)
    {
        lock (_gate) _now += delta;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
            timer.Schedule(_now, dueTime, period);
        }
        return timer;
    }

    private void Remove(FakeTimer timer)
    {
        lock (_gate) _timers.Remove(timer);
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _fireAt;
        private TimeSpan _period;

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public void Schedule(DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            _fireAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
        }

        public bool IsDue(DateTimeOffset now) => _fireAt is { } at && at <= now;

        public void Fire()
        {
            // One-shot unless a finite period was supplied (not used by the debounce path).
            _fireAt = _period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero
                ? null
                : (_fireAt ?? _owner.GetUtcNow()) + _period;
            _callback(_state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Schedule(_owner.GetUtcNow(), dueTime, period);
            return true;
        }

        public void Dispose() => _owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            _owner.Remove(this);
            return ValueTask.CompletedTask;
        }
    }
}
