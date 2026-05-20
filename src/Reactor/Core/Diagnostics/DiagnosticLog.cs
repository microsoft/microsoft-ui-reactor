using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Thin call-site helper that routes the two dominant <c>Debug.WriteLine</c>
/// patterns — swallowed exceptions and bare HRESULT codes — to
/// <see cref="ReactorEventSource"/> (release-visible, keyword-gated) and
/// additionally mirrors a richer line to <c>Debug.WriteLine</c> in DEBUG
/// for the contributor's Output window.
///
/// <para>
/// The public helpers are intentionally <b>not</b> <see cref="ConditionalAttribute"/> —
/// the whole point of this helper is that the diagnostic is emitted in
/// Release. Only the DEBUG mirror (<see cref="DebugSwallowedError"/> /
/// <see cref="DebugHResult"/>), which can safely include the raw
/// <see cref="Exception.Message"/> because it lands in the dev's local
/// Output window, is marked <c>[Conditional("DEBUG")]</c>.
/// </para>
///
/// <para>
/// PII discipline (spec 044 §6.2.1): the ETW payload carries the exception
/// <i>type</i> only. <see cref="Exception.Message"/> is never emitted on
/// the trace because messages can carry paths, env values, partial form
/// values, and other user data. Apps that want richer diagnostics should
/// attach an in-process <c>EventListener</c> (or use the
/// <c>Microsoft.UI.Reactor.Diagnostics.ReactorTrace.Subscribe</c> helper
/// once it lands) and capture the type-only payload there.
/// </para>
/// </summary>
internal static class DiagnosticLog
{
    /// <summary>
    /// Logs a swallowed exception the framework chose not to propagate.
    /// Always emits to <c>Microsoft-UI-Reactor</c>'s <c>SwallowedError</c>
    /// event under the <see cref="ReactorEventSource.Keywords.Errors"/>
    /// keyword; additionally mirrors a richer line including
    /// <see cref="Exception.Message"/> to <c>Debug.WriteLine</c> in DEBUG.
    /// </summary>
    /// <param name="category">Subsystem label — used as the logger /
    /// trace category so a consumer can filter by area.</param>
    /// <param name="operation">Short, stable identifier for the operation
    /// inside the <c>try</c> block (e.g. <c>"AppWindow.Close"</c>,
    /// <c>"JsonFileStore.SaveAsync"</c>). Developer-authored; safe for
    /// ETW. May be <see langword="null"/> at defensive call sites.</param>
    /// <param name="ex">The swallowed exception. Only its
    /// <see cref="Exception.GetType"/> name reaches the ETW payload.
    /// May be <see langword="null"/> at defensive call sites.</param>
    // <snippet:swallowed-error-shape>
    public static void SwallowedError(LogCategory category, string? operation, Exception? ex)
    {
        // Cost-of-disabled: when no consumer enables Keywords.Errors at
        // Warning the entire branch is skipped — no enum-to-string, no
        // type-name materialization, no WriteEvent dispatch.
        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
        {
            ReactorEventSource.Log.SwallowedError(
                category.ToString(),
                operation ?? string.Empty,
                ex?.GetType().Name ?? string.Empty);
        }

        DebugSwallowedError(category, operation, ex);
    }
    // </snippet:swallowed-error-shape>

    /// <summary>
    /// Logs a bare HRESULT / Win32 code that the framework chose to
    /// continue past rather than throw. Always emits to
    /// <c>Microsoft-UI-Reactor</c>'s <c>HResultFailed</c> event under the
    /// <see cref="ReactorEventSource.Keywords.Errors"/> keyword;
    /// additionally mirrors to <c>Debug.WriteLine</c> in DEBUG.
    /// </summary>
    /// <param name="category">Subsystem label.</param>
    /// <param name="operation">Short, stable identifier for the operation
    /// that returned the HR. May be <see langword="null"/> at defensive
    /// call sites.</param>
    /// <param name="hr">The HRESULT or Win32 error code as
    /// <see cref="Exception.HResult"/> exposes it (signed int).</param>
    public static void HResultFailed(LogCategory category, string? operation, int hr)
    {
        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
        {
            ReactorEventSource.Log.HResultFailed(
                category.ToString(),
                operation ?? string.Empty,
                hr);
        }

        DebugHResult(category, operation, hr);
    }

    [Conditional("DEBUG")]
    private static void DebugSwallowedError(LogCategory category, string? operation, Exception? ex)
    {
        var typeName = ex?.GetType().Name ?? "<null>";
        var message = ex?.Message ?? string.Empty;
        Debug.WriteLine($"[{category}] {operation} failed: {typeName}: {message}");
    }

    [Conditional("DEBUG")]
    private static void DebugHResult(LogCategory category, string? operation, int hr)
        => Debug.WriteLine($"[{category}] {operation} HR=0x{hr:X8}");
}
