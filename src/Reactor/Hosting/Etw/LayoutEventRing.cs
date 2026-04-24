using System.Threading;

namespace Microsoft.UI.Reactor.Hosting.Etw;

/// <summary>
/// Fixed-size single-producer / single-consumer ring buffer of
/// <see cref="PairedLayoutEvent"/>. Capacity is a power of two; the producer
/// drops the <em>oldest</em> event on overflow per spec §Event volume — never
/// the newest, because a stale overlay is worse than a slightly-dropped one.
/// </summary>
/// <remarks>
/// <para>Producer: the ETW callback thread (via <see cref="EventPairing"/>).
/// Consumer: the UI thread's per-window drain callback.</para>
/// <para>The implementation uses a lock to serialize publish / drain because the
/// hot path is not the bottleneck here — the ETW decode and dispatcher
/// scheduling dominate. A lock keeps the overflow-drops-oldest semantics
/// trivially correct; if profiling later shows this to be hot we can move to a
/// lock-free SPSC with explicit atomic indices.</para>
/// </remarks>
internal sealed class LayoutEventRing
{
    public const int DefaultCapacity = 65_536;

    private readonly PairedLayoutEvent[] _buffer;
    private readonly int _mask;
    private long _writeIndex;
    private long _readIndex;
    private long _dropped;
    private readonly object _sync = new();

    public LayoutEventRing(int capacity = DefaultCapacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a power of two.", nameof(capacity));
        _buffer = new PairedLayoutEvent[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    /// <summary>
    /// Count of events dropped since construction due to overflow. Monotonic.
    /// </summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Approximate number of unread events currently buffered.
    /// </summary>
    public int Depth
    {
        get
        {
            lock (_sync)
            {
                return (int)(_writeIndex - _readIndex);
            }
        }
    }

    /// <summary>
    /// Produce an event. If the buffer is full, the oldest event is overwritten
    /// and <see cref="DroppedCount"/> is incremented.
    /// </summary>
    public void Publish(in PairedLayoutEvent evt)
    {
        lock (_sync)
        {
            long w = _writeIndex;
            long r = _readIndex;
            if (w - r >= _buffer.Length)
            {
                // Full — drop the oldest by advancing the read pointer before we write.
                _readIndex = r + 1;
                _dropped++;
            }
            _buffer[(int)(w & _mask)] = evt;
            _writeIndex = w + 1;
        }
    }

    /// <summary>
    /// Copy as many events as fit into <paramref name="dest"/>, advancing the
    /// read index. Returns the number of events copied.
    /// </summary>
    public int Drain(Span<PairedLayoutEvent> dest)
    {
        lock (_sync)
        {
            long w = _writeIndex;
            long r = _readIndex;
            int available = (int)(w - r);
            int take = Math.Min(available, dest.Length);
            for (int i = 0; i < take; i++)
                dest[i] = _buffer[(int)((r + i) & _mask)];
            _readIndex = r + take;
            return take;
        }
    }
}
