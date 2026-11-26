using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Remembers the first render error shown since the preview last switched component, for
/// <c>/status</c>. The docs screenshot pipeline reads it after each capture and refuses to
/// write a frame that shows Reactor's error panel instead of the requested component.
/// </summary>
/// <remarks>
/// Fed by <see cref="RenderErrorDispatch.Observer"/>, which sees every error Reactor
/// displays whatever <see cref="RenderErrorHandler"/> the app configured. Recorded on the UI
/// thread, read on the capture server's request threads.
/// </remarks>
internal sealed class PreviewRenderErrorRecorder
{
    private string? _first;

    /// <summary>The first error since the last <see cref="Reset"/>, or <c>null</c>.</summary>
    public string? Current => Volatile.Read(ref _first);

    public void Record(RenderError error) =>
        Interlocked.CompareExchange(ref _first, Describe(error), null);

    public void Reset() => Volatile.Write(ref _first, null);

    internal static string Describe(RenderError error)
    {
        var where = error.ComponentName is { Length: > 0 } name ? $" in {name}" : "";
        return $"{error.Source}{where}: {error.Exception.GetType().Name}: {error.Exception.Message}";
    }
}
