using System.Diagnostics.Tracing;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// ETW/EventPipe provider for Reactor internals. Emits reconcile, render,
/// state-change, lifecycle, and MCP-dispatch events.
///
/// This provider is a managed <see cref="EventSource"/>, so it surfaces on
/// both EventPipe (<c>dotnet-trace</c>) and classic ETW (xperf / PerfView /
/// WPA). <c>Microsoft-Windows-XAML</c>, however, is a <b>native</b> ETW
/// provider emitted from WinUI's C++ code — it does not flow through
/// EventPipe, so correlated Reactor + XAML captures require an ETW-based
/// tool. Use <c>dotnet-trace</c> for Reactor-only traces, or xperf / PerfView
/// when you want the full render pipeline on one timeline.
///
/// EventPipe example (Reactor only):
///   dotnet-trace collect --process-id &lt;pid&gt; \
///       --providers Microsoft-UI-Reactor
///
/// ETW example (Reactor + WinUI correlated):
///   xperf -start ReactorSession \
///     -on "&lt;Reactor GUID&gt;:0x3F:5+531A35AB-63CE-4BCF-AA98-F88C7A89E455:0x9240:5" \
///     -f trace.etl
///
/// Keywords (see <see cref="Keywords"/>) let consumers pick subsets without
/// paying for the rest. Emit sites at hot paths (reconcile, render, state
/// writes, MCP dispatch) call <see cref="EventSource.IsEnabled(EventLevel, EventKeywords)"/>
/// at the call site before computing payloads (timestamps, type names) so
/// the disabled path avoids allocation and Stopwatch overhead. The
/// <c>WriteEvent</c> overloads are additionally guarded inside this class
/// for defense in depth.
/// </summary>
[EventSource(Name = "Microsoft-UI-Reactor")]
internal sealed class ReactorEventSource : EventSource
{
    public static readonly ReactorEventSource Log = new();

    private ReactorEventSource() { }

    // <snippet:etw-keywords>
    public static class Keywords
    {
        public const EventKeywords Reconcile = (EventKeywords)0x1;
        public const EventKeywords Render = (EventKeywords)0x2;
        public const EventKeywords State = (EventKeywords)0x4;
        public const EventKeywords Mcp = (EventKeywords)0x8;
        public const EventKeywords Lifecycle = (EventKeywords)0x10;
        public const EventKeywords Errors = (EventKeywords)0x20;
        public const EventKeywords EventDispatch = (EventKeywords)0x40;
        // Spec 044 — subsystem coverage gaps. Each gets its own bit so a
        // consumer (dotnet-trace, EventListener, ReactorTrace.Subscribe) can
        // pick exactly the area it cares about without paying for the rest.
        public const EventKeywords Hosting = (EventKeywords)0x80;       // Window/HWND/DPI/Backdrop
        public const EventKeywords Persistence = (EventKeywords)0x100;  // settings store, placement
        public const EventKeywords Navigation = (EventKeywords)0x200;   // route push, cache, transitions
        public const EventKeywords Intl = (EventKeywords)0x400;         // missing keys, fallback, format
        public const EventKeywords Theme = (EventKeywords)0x800;        // theme apply, bindings
        public const EventKeywords Shell = (EventKeywords)0x1000;       // JumpList/Tray/ThumbnailToolbar
    }
    // </snippet:etw-keywords>

    public static class Tasks
    {
        public const EventTask Reconcile = (EventTask)1;
        public const EventTask ComponentRender = (EventTask)2;
        public const EventTask McpCall = (EventTask)3;
        public const EventTask EffectsFlush = (EventTask)4;
        public const EventTask ChildReconcile = (EventTask)5;
    }

    // ── Reconcile pass boundaries ────────────────────────────────────────

    // <snippet:isenabled-gate>
    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Reconcile,
        Task = Tasks.Reconcile, Opcode = EventOpcode.Start,
        Message = "Reconcile start (root={rootElementType})")]
    public void ReconcileStart(string rootElementType)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Reconcile))
            WriteEvent(1, rootElementType ?? string.Empty);
    }
    // </snippet:isenabled-gate>

    // <snippet:reconcile-stop-event>
    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Reconcile,
        Task = Tasks.Reconcile, Opcode = EventOpcode.Stop,
        Message = "Reconcile stop (diffed={elementsDiffed}, skipped={elementsSkipped}, created={uiElementsCreated}, modified={uiElementsModified})")]
    public void ReconcileStop(int elementsDiffed, int elementsSkipped, int uiElementsCreated, int uiElementsModified)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Reconcile))
            WriteEvent(2, elementsDiffed, elementsSkipped, uiElementsCreated, uiElementsModified);
    }
    // </snippet:reconcile-stop-event>

    // ── Component render boundaries ──────────────────────────────────────

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Render,
        Task = Tasks.ComponentRender, Opcode = EventOpcode.Start,
        Message = "Render start (component={componentName}, trigger={trigger})")]
    public void ComponentRenderStart(string componentName, string trigger)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Render))
            WriteEvent(3, componentName ?? string.Empty, trigger ?? string.Empty);
    }

    [Event(4, Level = EventLevel.Informational, Keywords = Keywords.Render,
        Task = Tasks.ComponentRender, Opcode = EventOpcode.Stop,
        Message = "Render stop (component={componentName}, elapsedUs={elapsedMicroseconds})")]
    public void ComponentRenderStop(string componentName, long elapsedMicroseconds)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Render))
            WriteEvent(4, componentName ?? string.Empty, elapsedMicroseconds);
    }

    // ── State writes ─────────────────────────────────────────────────────

    [Event(5, Level = EventLevel.Verbose, Keywords = Keywords.State,
        Message = "State change (hook={hookKind}, type={valueType}, changed={changed})")]
    public void StateChange(string hookKind, string valueType, bool changed)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.State))
            WriteEvent(5, hookKind ?? string.Empty, valueType ?? string.Empty, changed);
    }

    // ── Component lifecycle ──────────────────────────────────────────────

    [Event(6, Level = EventLevel.Informational, Keywords = Keywords.Lifecycle,
        Message = "Component unmount (component={componentName})")]
    public void ComponentUnmount(string componentName)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Lifecycle))
            WriteEvent(6, componentName ?? string.Empty);
    }

    // ── MCP dispatch ─────────────────────────────────────────────────────

    [Event(7, Level = EventLevel.Informational, Keywords = Keywords.Mcp,
        Task = Tasks.McpCall, Opcode = EventOpcode.Start,
        Message = "MCP call start (tool={toolName}, selector={selector})")]
    public void McpCallStart(string toolName, string selector)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Mcp))
            // SECURITY (TASK-065): selectors carry user-bound strings via text
            // predicates ([Text*='user@email']). Replace with a SHA-1 prefix
            // so any same-UID dotnet-trace consumer sees a fingerprint, not
            // PII. Tool name itself is not user-controlled.
            WriteEvent(7, toolName ?? string.Empty, HashSelectorForEtw(selector));
    }

    private static string HashSelectorForEtw(string? selector)
    {
        if (string.IsNullOrEmpty(selector)) return string.Empty;
        var bytes = global::System.Security.Cryptography.SHA1.HashData(
            global::System.Text.Encoding.UTF8.GetBytes(selector));
        var sb = new global::System.Text.StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return "sha1:" + sb;
    }

    [Event(8, Level = EventLevel.Informational, Keywords = Keywords.Mcp,
        Task = Tasks.McpCall, Opcode = EventOpcode.Stop,
        Message = "MCP call stop (tool={toolName}, success={success}, resultCode={resultCode}, elapsedMs={elapsedMilliseconds})")]
    public void McpCallStop(string toolName, bool success, int resultCode, long elapsedMilliseconds)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Mcp))
            WriteEvent(8, toolName ?? string.Empty, success, resultCode, elapsedMilliseconds);
    }

    // ── Effects flush (UseEffect callbacks after render) ────────────────

    [Event(10, Level = EventLevel.Informational, Keywords = Keywords.Render,
        Task = Tasks.EffectsFlush, Opcode = EventOpcode.Start,
        Message = "Effects flush start (component={componentName})")]
    public void EffectsFlushStart(string componentName)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Render))
            WriteEvent(10, componentName ?? string.Empty);
    }

    [Event(11, Level = EventLevel.Informational, Keywords = Keywords.Render,
        Task = Tasks.EffectsFlush, Opcode = EventOpcode.Stop,
        Message = "Effects flush stop (component={componentName}, elapsedUs={elapsedMicroseconds})")]
    public void EffectsFlushStop(string componentName, long elapsedMicroseconds)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Render))
            WriteEvent(11, componentName ?? string.Empty, elapsedMicroseconds);
    }

    // ── Child reconciliation (keyed LIS over element arrays) ────────────

    [Event(12, Level = EventLevel.Informational, Keywords = Keywords.Reconcile,
        Task = Tasks.ChildReconcile, Opcode = EventOpcode.Start,
        Message = "Children reconcile start (oldCount={oldCount}, newCount={newCount})")]
    public void ChildReconcileStart(int oldCount, int newCount)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Reconcile))
            WriteEvent(12, oldCount, newCount);
    }

    [Event(13, Level = EventLevel.Informational, Keywords = Keywords.Reconcile,
        Task = Tasks.ChildReconcile, Opcode = EventOpcode.Stop,
        Message = "Children reconcile stop")]
    public void ChildReconcileStop()
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Reconcile))
            WriteEvent(13);
    }

    // ── Event trampoline lifecycle ───────────────────────────────────────

    [Event(14, Level = EventLevel.Verbose, Keywords = Keywords.EventDispatch,
        Message = "Event trampoline attached (event={eventName}, controlType={controlType})")]
    public void EventTrampolineAttached(string eventName, string controlType)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.EventDispatch))
            WriteEvent(14, eventName ?? string.Empty, controlType ?? string.Empty);
    }

    [Event(15, Level = EventLevel.Verbose, Keywords = Keywords.EventDispatch,
        Message = "Event trampoline dispatched (event={eventName})")]
    public void EventTrampolineDispatch(string eventName)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.EventDispatch))
            WriteEvent(15, eventName ?? string.Empty);
    }

    // ── Errors ───────────────────────────────────────────────────────────

    [Event(9, Level = EventLevel.Error, Keywords = Keywords.Errors,
        Message = "Render error (component={componentName}, exception={exceptionType}: {message})")]
    public void RenderError(string componentName, string exceptionType, string message)
    {
        if (IsEnabled(EventLevel.Error, Keywords.Errors))
            // SECURITY (TASK-064): ex.Message can contain absolute paths,
            // env values, query strings, and partial form values that would
            // leak to any same-UID dotnet-trace consumer. Drop the raw
            // message; the exception type is enough to triage class. Apps
            // that want richer diagnostics should opt in through the
            // app-level logger pipeline, which goes to ETL/disk under their
            // own ACL, not the Microsoft-UI-Reactor provider.
            WriteEvent(9, componentName ?? string.Empty, exceptionType ?? string.Empty, string.Empty);
    }

    // Spec 044 §6.1 — generic swallowed-exception event used by
    // DiagnosticLog.SwallowedError. PII: exception type only; the message
    // is intentionally never on the payload.
    [Event(16, Level = EventLevel.Warning, Keywords = Keywords.Errors,
        Message = "Swallowed error (category={category}, op={operation}, exception={exceptionType})")]
    public void SwallowedError(string category, string operation, string exceptionType)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Errors))
            WriteEvent(16, category ?? string.Empty, operation ?? string.Empty, exceptionType ?? string.Empty);
    }

    // Spec 044 §6.1 — generic HRESULT-failed event used by
    // DiagnosticLog.HResultFailed. HRESULT is signed (matches Exception.HResult).
    // <snippet:hresult-failed-event>
    [Event(17, Level = EventLevel.Warning, Keywords = Keywords.Errors,
        Message = "HResult failed (category={category}, op={operation}, hr=0x{hr:X8})")]
    public void HResultFailed(string category, string operation, int hr)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Errors))
            WriteEvent(17, category ?? string.Empty, operation ?? string.Empty, hr);
    }
    // </snippet:hresult-failed-event>

    // ── Hosting (spec 044 Phase B §2.1) ─────────────────────────────────
    //
    // PII: windowType is the C# type name (developer-authored, OK).
    //      Window titles are NEVER emitted on this surface (spec §6.2.1).
    //      HWND is emitted as a long; not user data.

    [Event(18, Level = EventLevel.Informational, Keywords = Keywords.Hosting,
        Message = "Window opened (type={windowType}, hwnd=0x{hwnd:X16})")]
    public void WindowOpened(string windowType, long hwnd)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Hosting))
            WriteEvent(18, windowType ?? string.Empty, hwnd);
    }

    [Event(19, Level = EventLevel.Informational, Keywords = Keywords.Hosting,
        Message = "Window closed (type={windowType}, hwnd=0x{hwnd:X16})")]
    public void WindowClosed(string windowType, long hwnd)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Hosting))
            WriteEvent(19, windowType ?? string.Empty, hwnd);
    }

    [Event(20, Level = EventLevel.Informational, Keywords = Keywords.Hosting,
        Message = "Window DPI changed (type={windowType}, old={oldDpi}, new={newDpi})")]
    public void WindowDpiChanged(string windowType, int oldDpi, int newDpi)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Hosting))
            WriteEvent(20, windowType ?? string.Empty, oldDpi, newDpi);
    }

    [Event(21, Level = EventLevel.Warning, Keywords = Keywords.Hosting,
        Message = "Backdrop materialization failed (kind={kind}, exception={exceptionType})")]
    public void BackdropMaterializationFailed(string kind, string exceptionType)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Hosting))
            // PII: exception type only — same discipline as RenderError.
            WriteEvent(21, kind ?? string.Empty, exceptionType ?? string.Empty);
    }

    // ── Persistence (spec 044 Phase B §2.2) ─────────────────────────────
    //
    // PII: file paths NEVER emitted; storeKind is a short label
    //      ("settings", "placement", etc.). sizeBytes is an int; not PII.

    [Event(22, Level = EventLevel.Informational, Keywords = Keywords.Persistence,
        Message = "Persistence read (store={storeKind}, size={sizeBytes})")]
    public void PersistenceRead(string storeKind, int sizeBytes)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Persistence))
            WriteEvent(22, storeKind ?? string.Empty, sizeBytes);
    }

    [Event(23, Level = EventLevel.Informational, Keywords = Keywords.Persistence,
        Message = "Persistence write (store={storeKind}, size={sizeBytes})")]
    public void PersistenceWrite(string storeKind, int sizeBytes)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Persistence))
            WriteEvent(23, storeKind ?? string.Empty, sizeBytes);
    }

    [Event(24, Level = EventLevel.Warning, Keywords = Keywords.Persistence,
        Message = "Persistence rejected (store={storeKind}, reason={reason})")]
    public void PersistenceRejected(string storeKind, string reason)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Persistence))
            // PII: reason is a short, developer-authored label
            // ("oversize", "corrupt", "schema-mismatch"), NEVER a path
            // or raw error message.
            WriteEvent(24, storeKind ?? string.Empty, reason ?? string.Empty);
    }

    // ── Navigation (spec 044 Phase B §2.3) ──────────────────────────────
    //
    // PII: route TEMPLATE only ("/users/{id}"). The instantiated path is
    //      NEVER emitted; instantiated path parameter values are user
    //      data per §6.2.1.

    [Event(25, Level = EventLevel.Informational, Keywords = Keywords.Navigation,
        Message = "Navigation requested (route={routeTemplate})")]
    public void NavigationRequested(string routeTemplate)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Navigation))
            WriteEvent(25, routeTemplate ?? string.Empty);
    }

    [Event(26, Level = EventLevel.Informational, Keywords = Keywords.Navigation,
        Message = "Navigation completed (route={routeTemplate}, durationMs={durationMs})")]
    public void NavigationCompleted(string routeTemplate, double durationMs)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Navigation))
            WriteEvent(26, routeTemplate ?? string.Empty, durationMs);
    }

    [Event(27, Level = EventLevel.Informational, Keywords = Keywords.Navigation,
        Message = "Navigation cancelled (route={routeTemplate}, reason={reason})")]
    public void NavigationCancelled(string routeTemplate, string reason)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Navigation))
            WriteEvent(27, routeTemplate ?? string.Empty, reason ?? string.Empty);
    }

    [Event(28, Level = EventLevel.Verbose, Keywords = Keywords.Navigation,
        Message = "Navigation cache hit (route={routeTemplate})")]
    public void NavigationCacheHit(string routeTemplate)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Navigation))
            WriteEvent(28, routeTemplate ?? string.Empty);
    }

    [Event(29, Level = EventLevel.Verbose, Keywords = Keywords.Navigation,
        Message = "Navigation cache miss (route={routeTemplate})")]
    public void NavigationCacheMiss(string routeTemplate)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Navigation))
            WriteEvent(29, routeTemplate ?? string.Empty);
    }

    [Event(30, Level = EventLevel.Verbose, Keywords = Keywords.Navigation,
        Message = "Navigation cache evict (route={routeTemplate}, reason={reason})")]
    public void NavigationCacheEvict(string routeTemplate, string reason)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Navigation))
            WriteEvent(30, routeTemplate ?? string.Empty, reason ?? string.Empty);
    }

    // Transition + DeepLink (spec 044 Phase C §4.2 follow-on). The
    // transition events log the transition's *type name* (e.g.
    // "FadeNavigationTransition") and the navigation mode, both of which
    // are framework-defined identifiers — never user data. DeepLink does
    // NOT include the input path (it is attacker-controllable per §6.2.1);
    // only the resolution outcome and candidate-route count.

    [Event(33, Level = EventLevel.Verbose, Keywords = Keywords.Navigation,
        Message = "Navigation transition started (type={transitionType}, mode={mode})")]
    public void NavigationTransitionStarted(string transitionType, string mode)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Navigation))
            WriteEvent(33, transitionType ?? string.Empty, mode ?? string.Empty);
    }

    [Event(34, Level = EventLevel.Verbose, Keywords = Keywords.Navigation,
        Message = "Navigation transition completed (type={transitionType}, mode={mode})")]
    public void NavigationTransitionCompleted(string transitionType, string mode)
    {
        if (IsEnabled(EventLevel.Verbose, Keywords.Navigation))
            WriteEvent(34, transitionType ?? string.Empty, mode ?? string.Empty);
    }

    [Event(35, Level = EventLevel.Informational, Keywords = Keywords.Navigation,
        Message = "Deep link resolved (matched={matched}, routeCount={routeCount})")]
    public void NavigationDeepLinkResolved(bool matched, int routeCount)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Navigation))
            WriteEvent(35, matched, routeCount);
    }

    // ── Intl (spec 044 Phase B §2.4) ────────────────────────────────────
    //
    // PII: keys are developer-authored static identifiers ("Settings.Title",
    //      "Errors.NetworkOffline"). Locale tags are BCP-47 strings, also OK.

    [Event(31, Level = EventLevel.Warning, Keywords = Keywords.Intl,
        Message = "Intl missing key (key={key}, locale={locale}, fellBack={fellBack})")]
    public void IntlMissingKey(string key, string locale, bool fellBack)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Intl))
            WriteEvent(31, key ?? string.Empty, locale ?? string.Empty, fellBack);
    }

    // ── Theme (spec 044 Phase B §2.5) ───────────────────────────────────

    [Event(32, Level = EventLevel.Warning, Keywords = Keywords.Theme,
        Message = "Theme apply failed (target={targetType}, exception={exceptionType})")]
    public void ThemeApplyFailed(string targetType, string exceptionType)
    {
        if (IsEnabled(EventLevel.Warning, Keywords.Theme))
            // PII: exception type only.
            WriteEvent(32, targetType ?? string.Empty, exceptionType ?? string.Empty);
    }

    // ── EventId allocation ──────────────────────────────────────────────
    //
    // Used: 1-15 (original surface), 16-17 (spec 044 Phase A generics),
    //       18-32 (spec 044 Phase B subsystem coverage),
    //       33-35 (spec 044 Phase C §4.2 navigation transition + deep link).
    // Next free EventId: 36.
}
