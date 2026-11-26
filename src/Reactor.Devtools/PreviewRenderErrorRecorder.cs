using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Tracks, for <c>/status</c>, whether the previewed component rendered cleanly since the
/// preview last switched component. The docs screenshot pipeline reads it after each capture
/// and refuses to write a frame that shows Reactor's error panel instead of the requested
/// component.
/// </summary>
/// <remarks>
/// <para>Fed by <see cref="RenderErrorDispatch.Observer"/>, which sees every error that
/// replaces content whatever <see cref="RenderErrorHandler"/> the app configured. Written on
/// the UI thread, read on the capture server's request threads.</para>
/// <para>A switch is <em>pending</em> until the host completes a render (<see cref="Settle"/>):
/// the switch only requests a render, and until it happens the window still shows the
/// previous component, which may be an earlier error panel.</para>
/// </remarks>
internal sealed class PreviewRenderErrorRecorder
{
    internal const string PendingDescription = "Pending: the component switch has not rendered yet.";

    // A private instance, so no recorded description can be mistaken for it.
    private static readonly string s_pending = new(PendingDescription.AsSpan());

    private string? _state;

    /// <summary>
    /// The first render error since the last <see cref="Reset"/>, <see cref="PendingDescription"/>
    /// while a switch has not rendered yet, or <c>null</c> when the current component rendered
    /// cleanly.
    /// </summary>
    public string? Current => Volatile.Read(ref _state);

    /// <summary>Creates a recorder and installs it as the process-wide observer.</summary>
    public static PreviewRenderErrorRecorder Install()
    {
        var recorder = new PreviewRenderErrorRecorder();
        RenderErrorDispatch.Observer = recorder.Record;
        return recorder;
    }

    /// <summary>Reports this recorder on <paramref name="server"/>'s <c>/status</c>.</summary>
    public void AttachTo(PreviewCaptureServer server) => server.GetRenderError = () => Current;

    /// <summary>Keeps the first error; replaces a pending switch.</summary>
    public void Record(RenderError error)
    {
        var description = Describe(error);
        while (true)
        {
            var current = Volatile.Read(ref _state);
            if (current is not null && !ReferenceEquals(current, s_pending)) return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, description, current), current)) return;
        }
    }

    /// <summary>Called when the preview switches component, before the new one renders.</summary>
    public void Reset() => Volatile.Write(ref _state, s_pending);

    /// <summary>Called after each completed host render; ends a pending switch.</summary>
    public void Settle() => Interlocked.CompareExchange(ref _state, null, s_pending);

    internal static string Describe(RenderError error)
    {
        var where = error.ComponentName is { Length: > 0 } name ? $" in {name}" : "";
        return $"{error.Source}{where}: {error.Exception.GetType().Name}: {error.Exception.Message}";
    }
}
