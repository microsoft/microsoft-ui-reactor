using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Microsoft.UI.Reactor.Hosting.Etw;

/// <summary>
/// In-process ETW consumer for the <c>Microsoft-Windows-XAML</c> provider.
/// Starts a real-time session named <c>Reactor.LayoutCost.{pid}</c>,
/// decodes <c>MeasureElement*</c> / <c>ArrangeElement*</c> events into
/// <see cref="RawLayoutEvent"/> records, and raises
/// <see cref="EventReceived"/> on the ETW callback thread.
///
/// On session-start failure (non-admin, non-member of Performance Log Users,
/// etc.) the consumer sets <see cref="IsUnavailable"/> + <see cref="UnavailableReason"/>
/// and becomes a no-op; the host stays healthy and the overlay menu shows the
/// unavailable state.
/// </summary>
internal sealed class LayoutEtwConsumer : IDisposable
{
    /// <summary>
    /// Base prefix used for session names. The full name is
    /// <c>{SessionNamePrefix}{pid}</c>. Exposed as a const so the leak-guard on
    /// startup can match orphan sessions from crashed processes.
    /// </summary>
    public const string SessionNamePrefix = "Reactor.LayoutCost.";

    /// <summary>
    /// Microsoft-Windows-XAML provider GUID (from
    /// <c>dxaml/xcp/plat/win/desktop/Microsoft-Windows-XAML-ETW.man</c>).
    /// </summary>
    public static readonly Guid XamlProviderGuid =
        new("531A35AB-63CE-4BCF-AA98-F88C7A89E455");

    // We used to filter by numeric task/opcode from the WPF XAML manifest
    // (task 23 = MeasureElement, 24 = Arrange, opcodes 1/2 = Begin/End). On
    // lifted WinUI those numeric task IDs don't line up — task=23 events
    // flow but with empty payload, i.e. they're different events that
    // happen to share the ID. We now match on `data.EventName` (string) and
    // discover the actual name at runtime. See `_eventNameHistogram`.
    private const string MeasureEventNamePrefix = "MeasureElement";
    private const string ArrangeEventNamePrefix = "ArrangeElement";
    private const string StartOpcodeName = "Start";
    private const string StopOpcodeName  = "Stop";
    // Alt opcode suffixes some manifests use.
    private const string BeginOpcodeName = "Begin";
    private const string EndOpcodeName   = "End";

    private static int s_processExitRegistered;
    private static readonly List<WeakReference<LayoutEtwConsumer>> s_liveConsumers = new();

    private readonly int _processId = Environment.ProcessId;
    private readonly string _sessionName;
    private TraceEventSession? _session;
    private Thread? _consumerThread;
    private volatile bool _running;
    private volatile bool _disposed;
    /// <summary>
    /// Set once per instance the first time it registers with
    /// <see cref="s_liveConsumers"/>. Stop/Start cycles that bring the
    /// session back up don't add a second weak ref — without this guard
    /// the static list would grow unboundedly across toggles.
    /// </summary>
    private bool _registeredInLiveConsumers;

    // Diagnostic counters — written from the ETW callback thread, read from
    // the UI thread for debug logging. Volatile reads are good enough; exact
    // totals don't matter, trend does.
    private long _eventsSeenAll;
    private long _eventsSeenForUs;
    private long _eventsMatchedTaskOpcode;
    private long _eventsEmitted;
    private Stopwatch? _diagClock;

    /// <summary>Total ETW events the session has delivered (all providers/procs).</summary>
    public long EventsSeenAll => Interlocked.Read(ref _eventsSeenAll);
    /// <summary>Events whose ProcessID matches ours.</summary>
    public long EventsSeenForUs => Interlocked.Read(ref _eventsSeenForUs);
    /// <summary>Events with task + opcode in our Measure/Arrange filter.</summary>
    public long EventsMatchedTaskOpcode => Interlocked.Read(ref _eventsMatchedTaskOpcode);
    /// <summary>Events we successfully decoded + raised.</summary>
    public long EventsEmitted => Interlocked.Read(ref _eventsEmitted);

    public LayoutEtwConsumer()
    {
        _sessionName = SessionNamePrefix + _processId;
    }

    /// <summary>Fired once per decoded XAML layout event, on the ETW callback thread.</summary>
    public event Action<RawLayoutEvent>? EventReceived;

    public bool IsRunning => _running;

    /// <summary>
    /// True once <see cref="Start"/> has completed but found the session
    /// un-creatable (privileges, already-running orphan, etc.).
    /// </summary>
    public bool IsUnavailable { get; private set; }

    /// <summary>Human-readable reason matching <see cref="IsUnavailable"/>.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>
    /// Starts the ETW session. Idempotent: a second call on a running consumer
    /// is a no-op. On failure, sets <see cref="IsUnavailable"/> and returns
    /// without throwing.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        if (_running) return;
        IsUnavailable = false;
        UnavailableReason = null;

        RegisterProcessExitHookOnce();
        if (!_registeredInLiveConsumers)
        {
            lock (s_liveConsumers)
            {
                // Prune any GC'd weak refs while we're holding the lock.
                for (int i = s_liveConsumers.Count - 1; i >= 0; i--)
                {
                    if (!s_liveConsumers[i].TryGetTarget(out _))
                        s_liveConsumers.RemoveAt(i);
                }
                s_liveConsumers.Add(new WeakReference<LayoutEtwConsumer>(this));
            }
            _registeredInLiveConsumers = true;
        }

        try
        {
            CloseOrphanSessions();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor.LayoutCost] orphan session cleanup failed: {ex.Message}");
            // Non-fatal — continue trying to start.
        }

        try
        {
            _session = new TraceEventSession(_sessionName)
            {
                StopOnDispose = true,
            };
            _session.EnableProvider(
                XamlProviderGuid,
                TraceEventLevel.Verbose,
                ulong.MaxValue /* all keywords incl. Detailed */);
        }
        catch (UnauthorizedAccessException ex)
        {
            IsUnavailable = true;
            UnavailableReason = "Access denied — user is not a member of Performance Log Users and is not an administrator.";
            Debug.WriteLine($"[Reactor.LayoutCost] ETW session denied: {ex.Message}");
            SafeDisposeSession();
            return;
        }
        catch (Exception ex)
        {
            IsUnavailable = true;
            UnavailableReason = ex.Message;
            Debug.WriteLine($"[Reactor.LayoutCost] ETW session failed: {ex}");
            SafeDisposeSession();
            return;
        }

        try
        {
            var parser = new DynamicTraceEventParser(_session.Source);
            parser.All += OnEtwEvent;
        }
        catch (Exception ex)
        {
            IsUnavailable = true;
            UnavailableReason = ex.Message;
            Debug.WriteLine($"[Reactor.LayoutCost] ETW parser hookup failed: {ex}");
            SafeDisposeSession();
            return;
        }

        _running = true;
        _diagClock = Stopwatch.StartNew();
        _consumerThread = new Thread(ProcessLoop)
        {
            IsBackground = true,
            Name = "Reactor.LayoutCost.Etw",
        };
        _consumerThread.Start();
        Debug.WriteLine($"[Reactor.LayoutCost.Etw] session '{_sessionName}' started, provider={XamlProviderGuid:B}");
    }

    /// <summary>
    /// Stops the session and joins the consumer thread. Safe to call multiple
    /// times; never throws.
    /// </summary>
    public void Stop()
    {
        if (!_running && _session is null) return;
        _running = false;
        SafeDisposeSession();
        var t = _consumerThread;
        _consumerThread = null;
        if (t is not null && t.IsAlive)
        {
            try { t.Join(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void ProcessLoop()
    {
        try
        {
            _session?.Source?.Process();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor.LayoutCost] Source.Process() exited: {ex.Message}");
        }
        finally
        {
            _running = false;
        }
    }

    private readonly Dictionary<string, long> _eventNameHistogram = new(StringComparer.Ordinal);
    private const int EventNameHistogramCap = 64;

    private void OnEtwEvent(TraceEvent data)
    {
        Interlocked.Increment(ref _eventsSeenAll);

        // Filter to our own process — the XAML provider emits for every XAML app
        // on the machine (we don't get a per-process session here).
        if (data.ProcessID != _processId)
        {
            MaybeLogDiagnostics();
            return;
        }
        Interlocked.Increment(ref _eventsSeenForUs);

        // Record the event name in a histogram so we can see what the XAML
        // provider is actually emitting on this machine. Capped so a flood of
        // names from an unrelated provider can't bloat memory.
        var name = data.EventName ?? string.Empty;
        lock (_eventNameHistogram)
        {
            if (_eventNameHistogram.Count < EventNameHistogramCap
                || _eventNameHistogram.ContainsKey(name))
            {
                _eventNameHistogram.TryGetValue(name, out var prior);
                _eventNameHistogram[name] = prior + 1;
            }
        }

        // Match by event name: "MeasureElement/Start", "ArrangeElement/Stop",
        // etc. We accept either Start/Stop or Begin/End opcode suffixes.
        LayoutEventKind kind;
        LayoutEventPhase phase;
        if (name.StartsWith(MeasureEventNamePrefix, StringComparison.Ordinal))
            kind = LayoutEventKind.Measure;
        else if (name.StartsWith(ArrangeEventNamePrefix, StringComparison.Ordinal))
            kind = LayoutEventKind.Arrange;
        else
        {
            MaybeLogDiagnostics();
            return;
        }

        if (name.EndsWith('/' + StartOpcodeName, StringComparison.Ordinal)
            || name.EndsWith('/' + BeginOpcodeName, StringComparison.Ordinal))
            phase = LayoutEventPhase.Begin;
        else if (name.EndsWith('/' + StopOpcodeName, StringComparison.Ordinal)
                 || name.EndsWith('/' + EndOpcodeName, StringComparison.Ordinal))
            phase = LayoutEventPhase.End;
        else
        {
            MaybeLogDiagnostics();
            return;
        }

        Interlocked.Increment(ref _eventsMatchedTaskOpcode);

        // One-time schema probe: on the first matched event of each
        // (task, opcode) pair, dump the payload field names + types so we
        // can see what's actually on the wire. Manifest field names aren't
        // guaranteed to be "ElementId" — on some SDKs the XAML provider
        // names the handle field "Sender", "ObjectId", etc.
        LogPayloadSchemaOnce(data, kind, phase);

        ulong elementId = TryReadHandleField(data);
        if (elementId == 0)
        {
            MaybeLogDiagnostics();
            return; // Couldn't resolve a handle — skip this event.
        }

        float rx = 0, ry = 0, rw = 0, rh = 0;
        if (kind == LayoutEventKind.Arrange && phase == LayoutEventPhase.End)
        {
            // Lifted WinUI's ArrangeElement/Stop emits:
            //   VisualOffsetX / VisualOffsetY / RenderWidth / RenderHeight
            // Older manifests (and the WPF XAML one the spec referenced) use
            //   FinalRectX / FinalRectY / FinalRectWidth / FinalRectHeight
            // Try both so we're robust across SDK versions.
            rx = PayloadFloatSafe(data, "VisualOffsetX");
            ry = PayloadFloatSafe(data, "VisualOffsetY");
            rw = PayloadFloatSafe(data, "RenderWidth");
            rh = PayloadFloatSafe(data, "RenderHeight");
            if (rw == 0 && rh == 0)
            {
                rx = PayloadFloatSafe(data, "FinalRectX");
                ry = PayloadFloatSafe(data, "FinalRectY");
                rw = PayloadFloatSafe(data, "FinalRectWidth");
                rh = PayloadFloatSafe(data, "FinalRectHeight");
            }
        }
        else if (kind == LayoutEventKind.Arrange && phase == LayoutEventPhase.Begin)
        {
            // Arrange/Start has the proposed Left/Top/Width/Height — useful
            // as a fallback anchor when Stop's rect is absent.
            rx = PayloadFloatSafe(data, "Left");
            ry = PayloadFloatSafe(data, "Top");
            rw = PayloadFloatSafe(data, "Width");
            rh = PayloadFloatSafe(data, "Height");
        }

        var raw = new RawLayoutEvent(
            elementId,
            kind,
            phase,
            data.TimeStamp.Ticks,
            data.ThreadID,
            rx, ry, rw, rh);
        Interlocked.Increment(ref _eventsEmitted);
        EventReceived?.Invoke(raw);
        MaybeLogDiagnostics();
    }

    private void MaybeLogDiagnostics()
    {
        var clock = _diagClock;
        if (clock is null) return;
        if (clock.ElapsedMilliseconds < 2000) return;
        lock (clock)
        {
            if (clock.ElapsedMilliseconds < 2000) return;
            clock.Restart();
        }
        Debug.WriteLine(
            $"[Reactor.LayoutCost.Etw] all={EventsSeenAll} forUs={EventsSeenForUs} matched={EventsMatchedTaskOpcode} emitted={EventsEmitted}");

        // Top 15 event names so we can spot the actual Measure/Arrange names
        // the XAML provider emits on this machine.
        KeyValuePair<string, long>[] top;
        lock (_eventNameHistogram)
        {
            top = _eventNameHistogram
                .OrderByDescending(kv => kv.Value)
                .Take(15)
                .ToArray();
        }
        if (top.Length > 0)
        {
            var sb = new global::System.Text.StringBuilder("[Reactor.LayoutCost.Etw] top events: ");
            for (int i = 0; i < top.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(top[i].Key).Append('=').Append(top[i].Value);
            }
            Debug.WriteLine(sb.ToString());
        }
    }

    // Known candidate names for the "which element is this?" handle across
    // different XAML manifest versions. Checked in order; first non-zero
    // ulong-convertible value wins.
    private static readonly string[] HandleFieldCandidates =
    {
        "ElementId", "Sender", "Handle", "ObjectId", "pElement", "Element",
        "CUIElementId", "ElementHandle", "InstanceId",
    };

    private ulong TryReadHandleField(TraceEvent data)
    {
        var names = data.PayloadNames;
        if (names is null || names.Length == 0) return 0;

        foreach (var candidate in HandleFieldCandidates)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.Equals(names[i], candidate, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var v = data.PayloadValue(i);
                    if (v is null) continue;
                    return v switch
                    {
                        ulong u => u,
                        long l => (ulong)l,
                        uint ui => ui,
                        int i32 => (ulong)i32,
                        IntPtr ip => (ulong)ip.ToInt64(),
                        UIntPtr up => (ulong)up.ToUInt64(),
                        _ => 0UL,
                    };
                }
                catch { /* try next */ }
            }
        }
        return 0;
    }

    private readonly HashSet<(int task, int opcode)> _schemaLogged = new();
    private void LogPayloadSchemaOnce(TraceEvent data, LayoutEventKind kind, LayoutEventPhase phase)
    {
        var key = ((int)data.Task, (int)data.Opcode);
        lock (_schemaLogged)
        {
            if (!_schemaLogged.Add(key)) return;
        }
        var names = data.PayloadNames ?? Array.Empty<string>();
        var sb = new global::System.Text.StringBuilder(128);
        sb.Append($"[Reactor.LayoutCost.Etw] schema {kind}/{phase} task={key.Item1} opcode={key.Item2} fields=[");
        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(names[i]).Append(':');
            try
            {
                var v = data.PayloadValue(i);
                sb.Append(v?.GetType().Name ?? "null");
            }
            catch (Exception ex) { sb.Append("<err:").Append(ex.GetType().Name).Append('>'); }
        }
        sb.Append(']');
        Debug.WriteLine(sb.ToString());
    }

    private static float PayloadFloatSafe(TraceEvent data, string name)
    {
        try
        {
            var v = data.PayloadByName(name);
            return v switch
            {
                float f => f,
                double d => (float)d,
                _ => 0f,
            };
        }
        catch { return 0f; }
    }

    /// <summary>
    /// Stop any sessions named <c>{SessionNamePrefix}{pid}</c> whose pid
    /// suffix no longer corresponds to a running process. Sessions belonging
    /// to other live Reactor processes are left alone.
    /// </summary>
    private void CloseOrphanSessions()
    {
        foreach (var name in TraceEventSession.GetActiveSessionNames())
        {
            if (name is null) continue;
            if (!name.StartsWith(SessionNamePrefix, StringComparison.Ordinal)) continue;
            if (name.Equals(_sessionName, StringComparison.Ordinal)) continue;

            // Parse the pid suffix and only stop the session if that
            // process is gone. A live sibling Reactor process owns its
            // own session — leaving its overlay alone is the correct
            // behavior.
            var suffix = name.AsSpan(SessionNamePrefix.Length);
            if (!int.TryParse(suffix, out var ownerPid)) continue;
            if (IsProcessAlive(ownerPid)) continue;

            try
            {
                using var orphan = TraceEventSession.GetActiveSession(name);
                orphan.Stop(noThrow: true);
                Debug.WriteLine($"[Reactor.LayoutCost] closed orphan session '{name}' (pid {ownerPid} not running)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Reactor.LayoutCost] failed to close orphan session '{name}': {ex.Message}");
            }
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = global::System.Diagnostics.Process.GetProcessById(pid);
            // GetProcessById throws ArgumentException for non-existent pids;
            // if we got here, p represents a real process. HasExited may
            // throw on access-denied (e.g. system process); treat that as
            // "alive" — we'd rather leave the session than wrongly kill it.
            try { return !p.HasExited; }
            catch { return true; }
        }
        catch (ArgumentException) { return false; }
        catch (Exception) { return true; }
    }

    private void SafeDisposeSession()
    {
        try
        {
            _session?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor.LayoutCost] session dispose swallowed: {ex.Message}");
        }
        finally
        {
            _session = null;
        }
    }

    private static void RegisterProcessExitHookOnce()
    {
        if (Interlocked.Exchange(ref s_processExitRegistered, 1) == 1) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            List<LayoutEtwConsumer> consumers;
            lock (s_liveConsumers)
            {
                consumers = new List<LayoutEtwConsumer>(s_liveConsumers.Count);
                foreach (var wr in s_liveConsumers)
                    if (wr.TryGetTarget(out var c)) consumers.Add(c);
            }
            foreach (var c in consumers)
            {
                try { c.Stop(); } catch { /* best effort */ }
            }
        };
    }
}
