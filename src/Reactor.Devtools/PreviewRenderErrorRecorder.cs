using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Tracks, for <c>/status</c>, whether the previewed component rendered cleanly since the
/// preview last switched component. The docs screenshot pipeline waits for a switch to stop
/// being pending, captures, then reads this again and refuses to write a frame that shows
/// Reactor's error panel instead of the requested component.
/// </summary>
/// <remarks>
/// <para>Fed by <see cref="RenderErrorDispatch.Observer"/>, which sees every error that
/// replaces content whatever <see cref="RenderErrorHandler"/> the app configured. Switches
/// start on the capture server's request thread; errors and renders arrive on the UI
/// thread.</para>
/// <para>A switch goes through three steps. <see cref="Reset"/> marks it pending before
/// <c>/preview</c> answers, so no status read can miss it. <see cref="Arm"/> runs on the UI
/// thread immediately before the new root is mounted. Only after that does a completed
/// render (<see cref="Settle"/>) or an error count: until then the window still shows, and
/// still renders, the outgoing component.</para>
/// </remarks>
internal sealed class PreviewRenderErrorRecorder
{
    internal const string PendingDescription = "Pending: the component switch has not rendered yet.";

    private readonly Lock _gate = new();
    private string? _error;
    private bool _pending;
    private bool _armed;
    private int _generation;

    /// <summary>
    /// The first render error since the last switch, <see cref="PendingDescription"/> while
    /// that switch has not rendered yet, or <c>null</c> when the current component rendered
    /// cleanly.
    /// </summary>
    public string? Current
    {
        get
        {
            lock (_gate)
                return _error ?? (_pending ? PendingDescription : null);
        }
    }

    /// <summary>Creates a recorder and installs it as the process-wide observer.</summary>
    public static PreviewRenderErrorRecorder Install()
    {
        var recorder = new PreviewRenderErrorRecorder();
        RenderErrorDispatch.Observer = recorder.Record;
        return recorder;
    }

    /// <summary>Reports this recorder on <paramref name="server"/>'s <c>/status</c>.</summary>
    public void AttachTo(PreviewCaptureServer server) => server.GetRenderError = () => Current;

    /// <summary>
    /// Starts a switch: clears the previous component's error and marks the switch pending.
    /// Returns the switch's generation, for <see cref="Arm"/>.
    /// </summary>
    public int Reset()
    {
        lock (_gate)
        {
            _error = null;
            _pending = true;
            _armed = false;
            return ++_generation;
        }
    }

    /// <summary>
    /// Called on the UI thread immediately before the switch mounts its root. Returns
    /// <c>false</c>, arming nothing, when a later switch has started since: that callback
    /// must not mount, or the window would show a superseded component while this recorder
    /// tracks the newer one.
    /// </summary>
    public bool Arm(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation) return false;
            if (_pending) _armed = true;
            return true;
        }
    }

    /// <summary>Keeps the first error, ignoring the outgoing component's.</summary>
    public void Record(RenderError error)
    {
        var description = Describe(error);
        lock (_gate)
        {
            if (_pending && !_armed) return;
            _error ??= description;
        }
    }

    /// <summary>Called after each completed host render; ends an armed switch.</summary>
    public void Settle()
    {
        lock (_gate)
        {
            if (_armed) _pending = false;
        }
    }

    internal static string Describe(RenderError error)
    {
        var where = error.ComponentName is { Length: > 0 } name ? $" in {name}" : "";
        return $"{error.Source}{where}: {error.Exception.GetType().Name}: {MessageOf(error.Exception)}";
    }

    // A message getter that throws must not lose the error: the observer swallows observer
    // exceptions, so /status would report a clean render while the fallback is on screen.
    private static string MessageOf(Exception exception)
    {
        try
        {
            return exception.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return "(message unavailable)";
        }
    }
}
