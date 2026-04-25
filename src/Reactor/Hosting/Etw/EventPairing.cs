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
        /// <summary>
        /// Root-relative origin of this element's content area, in DIPs.
        /// Computed at Begin time as (parent.OriginX + thisProposedLeft).
        /// Children of this element will use this as their parent origin.
        /// Tracked on the Arrange stack only; Measure events don't carry
        /// placement data.
        /// </summary>
        public float RootOriginX;
        public float RootOriginY;
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
            // Compose root-relative origin by accumulating parent-relative
            // offsets up the stack. ETW gives us Left/Top in the parent's
            // coord space on Arrange/Begin; without this composition,
            // attribution sees small offsets and lands events on whichever
            // Component happens to sit at the top-left of the screen.
            float parentX = 0, parentY = 0;
            if (stack.Count > 0)
            {
                var parentTop = stack.Peek();
                parentX = parentTop.RootOriginX;
                parentY = parentTop.RootOriginY;
            }
            stack.Push(new PairingFrame
            {
                ElementId = raw.ElementId,
                BeginTicks = raw.TimestampTicks,
                ChildInclusiveTicks = 0,
                // For Arrange, raw.RectX/Y is the proposed Left/Top in parent
                // coords. For Measure, raw.RectX/Y is 0 (no placement on
                // Measure) — the inherited parent origin is the best we have.
                RootOriginX = parentX + raw.RectX,
                RootOriginY = parentY + raw.RectY,
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

        // Compose root-relative rect for downstream attribution. On
        // Arrange/End, raw.RectX/Y is VisualOffset in the parent's coord
        // space — add it to the parent's accumulated root origin (which
        // is on top of the stack now, after we popped this element).
        // Without this, attribution sees small parent-relative offsets
        // and lands events on whichever Component happens to sit at the
        // top-left of the screen.
        float parentRootX = 0, parentRootY = 0;
        if (stack.Count > 0)
        {
            var parentTop = stack.Peek();
            parentRootX = parentTop.RootOriginX;
            parentRootY = parentTop.RootOriginY;

            // Bubble inclusive time into the parent's child-time accumulator.
            var parent = stack.Pop();
            parent.ChildInclusiveTicks += inclusive;
            stack.Push(parent);
        }
        float rootX = parentRootX + raw.RectX;
        float rootY = parentRootY + raw.RectY;

        var paired = new PairedLayoutEvent(
            raw.ElementId,
            raw.Kind,
            inclusive,
            self,
            rootX, rootY, raw.RectW, raw.RectH);
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
