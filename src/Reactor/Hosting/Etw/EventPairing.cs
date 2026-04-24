using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Etw;

/// <summary>
/// Pairs <c>*Begin</c>/<c>*End</c> layout events into
/// <see cref="PairedLayoutEvent"/> records using per-(thread,kind) stacks.
///
/// Layout is naturally recursive (a parent's measure spans its children's
/// measures). Self-time for a frame is its inclusive time minus the sum of its
/// children's inclusive times, so each frame also tracks the children's time
/// it has seen while open.
/// </summary>
/// <remarks>
/// All methods are expected to be called from a single producer thread (the
/// ETW callback thread). Mismatched <c>End</c>s are logged once and the
/// offending thread's stack is flushed — resilient to dropped events.
/// </remarks>
internal sealed class EventPairing
{
    private struct PairingFrame
    {
        public ulong ElementId;
        public long BeginTicks;
        public long ChildInclusiveTicks;
    }

    private readonly Dictionary<(int threadId, LayoutEventKind kind), Stack<PairingFrame>> _stacks = new();
    private bool _mismatchLogged;

    /// <summary>Raised on every successful pair. Fires on the ETW callback thread.</summary>
    public event Action<PairedLayoutEvent>? Paired;

    /// <summary>Feed a raw event. No-op for events that are not Measure/Arrange Begin/End.</summary>
    public void OnEvent(in RawLayoutEvent raw)
    {
        var key = (raw.ThreadId, raw.Kind);
        if (!_stacks.TryGetValue(key, out var stack))
        {
            stack = new Stack<PairingFrame>();
            _stacks[key] = stack;
        }

        if (raw.Phase == LayoutEventPhase.Begin)
        {
            stack.Push(new PairingFrame
            {
                ElementId = raw.ElementId,
                BeginTicks = raw.TimestampTicks,
                ChildInclusiveTicks = 0,
            });
            return;
        }

        // End event.
        if (stack.Count == 0)
        {
            // Unbalanced End — drop silently; worst case this is a late-arriving End
            // for a Begin that arrived before the session started.
            return;
        }

        var top = stack.Pop();
        if (top.ElementId != raw.ElementId)
        {
            // Mismatch implies we dropped an event somewhere. Flush this stack so
            // future pairs realign, and log once.
            if (!_mismatchLogged)
            {
                Debug.WriteLine(
                    $"[Reactor.LayoutCost] paired-event mismatch on thread {raw.ThreadId} kind {raw.Kind}: expected {top.ElementId:X} got {raw.ElementId:X}. Flushing stack.");
                _mismatchLogged = true;
            }
            stack.Clear();
            return;
        }

        long inclusive = raw.TimestampTicks - top.BeginTicks;
        if (inclusive < 0) inclusive = 0;
        long self = inclusive - top.ChildInclusiveTicks;
        if (self < 0) self = 0;

        // Add our inclusive duration to the parent's child-time accumulator, if any.
        if (stack.Count > 0)
        {
            var parent = stack.Pop();
            parent.ChildInclusiveTicks += inclusive;
            stack.Push(parent);
        }

        var paired = new PairedLayoutEvent(
            raw.ElementId,
            raw.Kind,
            inclusive,
            self,
            raw.RectX, raw.RectY, raw.RectW, raw.RectH);
        Paired?.Invoke(paired);
    }

    /// <summary>
    /// Drops any in-flight frames. Used when stopping the consumer to guarantee
    /// a clean slate on restart.
    /// </summary>
    public void Reset()
    {
        _stacks.Clear();
        _mismatchLogged = false;
    }
}
