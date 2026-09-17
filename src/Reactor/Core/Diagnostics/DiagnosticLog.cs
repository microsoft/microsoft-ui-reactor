using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Thin call-site helper that routes the dominant <c>Debug.WriteLine</c>
/// patterns — swallowed exceptions, bare HRESULT codes, and framework
/// warnings — to
/// <see cref="ReactorEventSource"/> (release-visible, keyword-gated) and
/// additionally mirrors a richer line to <c>Debug.WriteLine</c> in DEBUG
/// for the contributor's Output window.
///
/// <para>
/// The public helpers are intentionally <b>not</b> <see cref="ConditionalAttribute"/> —
/// the whole point of this helper is that the diagnostic is emitted in
/// Release. Only the DEBUG mirrors (<see cref="DebugSwallowedError"/> /
/// <see cref="DebugHResult"/> / <see cref="DebugWarning"/>), which can
/// safely include the raw
/// <see cref="Exception.Message"/> because it lands in the dev's local
/// Output window, are marked <c>[Conditional("DEBUG")]</c>.
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

    /// <summary>
    /// Logs a framework-authored warning about a recoverable misconfiguration
    /// the framework chose to continue past. Always emits to
    /// <c>Microsoft-UI-Reactor</c>'s <c>Warning</c> event under the
    /// <see cref="ReactorEventSource.Keywords.Errors"/> keyword; additionally
    /// mirrors to <c>Debug.WriteLine</c> in DEBUG.
    /// </summary>
    /// <param name="category">Subsystem label.</param>
    /// <param name="operation">Short, stable identifier for the operation that
    /// produced the warning. May be <see langword="null"/>.</param>
    /// <param name="message">Framework-authored explanation. Must not embed
    /// user data — see the PII note on this class. Composing it from
    /// developer-authored identifiers (resource keys, property names) is fine.
    /// Callers that build this string with interpolation should first check
    /// <see cref="IsWarningEnabled"/> so the allocation is skipped when no
    /// consumer is listening.</param>
    public static void Warning(LogCategory category, string? operation, string? message)
    {
        // Cost-of-disabled: mirrors SwallowedError / HResultFailed — when no
        // consumer enables Keywords.Errors at Warning the whole branch is
        // skipped. Critically, this is NOT [Conditional("DEBUG")]: before
        // this event existed the entire helper compiled away in Release, so
        // every warning routed through it was invisible in shipped apps.
        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
        {
            ReactorEventSource.Log.Warning(
                category.ToString(),
                operation ?? string.Empty,
                message ?? string.Empty);
        }

        DebugWarning(category, operation, message);
    }

    /// <summary>
    /// Whether a <see cref="Warning"/> emitted now would reach any consumer —
    /// an enabled ETW listener, or the DEBUG <c>Debug.WriteLine</c> mirror.
    /// Call sites that must interpolate a message should gate on this so the
    /// string is not built and immediately discarded.
    /// </summary>
    public static bool IsWarningEnabled
    {
        get
        {
            if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
                return true;
#if DEBUG
            return true;
#else
            return false;
#endif
        }
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

    [Conditional("DEBUG")]
    private static void DebugWarning(LogCategory category, string? operation, string? message)
        => Debug.WriteLine($"[{category}] {operation} warning: {message}");
}
