namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Subsystem labels used by <see cref="DiagnosticLog"/> and by the ETW
/// payloads on <see cref="ReactorEventSource.SwallowedError"/> /
/// <see cref="ReactorEventSource.HResultFailed"/>.
///
/// Replaces the historical stringly-typed <c>[Reactor.X]</c> prefixes that
/// were attached to <c>Debug.WriteLine</c> calls. Compile-time-checked so a
/// typo in a catch block doesn't silently land in the trace as <c>[reactr]</c>.
///
/// New subsystems should add a value here rather than passing a free-form
/// string — keeps the dotnet-trace consumer filterable by enum.
/// </summary>
internal enum LogCategory
{
    /// <summary>Reactor core (reconciler, render context, hooks, element pooling).</summary>
    Reactor,

    /// <summary>App / window hosting: <c>ReactorApp</c>, <c>ReactorWindow</c>, AppWindow, DPI, backdrop.</summary>
    Hosting,

    /// <summary>Settings, window-placement, JSON file store, packaged settings store.</summary>
    Persistence,

    /// <summary>Navigation router: route push/pop, page cache, transition lifecycle.</summary>
    Navigation,

    /// <summary>Localization / internationalization: missing resource key, locale fallback, format errors.</summary>
    Intl,

    /// <summary>Theme application: light/dark switch, brush bindings, theme-aware controls.</summary>
    Theme,

    /// <summary>Windows shell integration: JumpList, ThumbnailToolbar, tray icon, taskbar.</summary>
    Shell,

    /// <summary>Layout-cost overlay pipeline (spec 032) — ETW consumer, attribution.</summary>
    LayoutCost,

    /// <summary>Devtools surface: MCP server, log capture, preview pipe.</summary>
    Devtools,

    /// <summary>Markdown rendering: md4c parser, builder, list of inline children.</summary>
    Markdown,
}
