// Uno hosting entry point for Reactor.
//
// Replaces the Windows framework's src/Reactor/Hosting/ReactorApp.cs (which is
// built on Application.Start + Win32/DWM/shell P/Invoke). Here, startup goes
// through Uno's UnoPlatformHostBuilder (Uno 6 unified Skia hosting) and a
// code-only Application subclass. Only the static surface the shared core reads
// (ActiveHostInternal, UIDispatcher, …) plus the public Run/OpenWindow entry points
// are provided. Multi-window is real on the desktop heads (every window gets its own
// ReactorHost); tray icons and window persistence remain stubs.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Windowing;

namespace Microsoft.UI.Reactor;

/// <summary>Startup configuration captured before the Uno host bootstraps.</summary>
internal sealed record ReactorAppOptions(
    Func<Component>? RootFactory = null,
    Func<RenderContext, Element>? RootRenderFunc = null,
    Action<ReactorHost>? Configure = null,
    string WindowTitle = "Reactor App",
    double? WindowWidth = null,
    double? WindowHeight = null,
    bool FullScreen = false);

/// <summary>
/// Application entry point for Reactor apps running on Uno Platform Skia targets
/// (desktop + WebAssembly, and theoretically mobile).
/// </summary>
public static partial class ReactorApp
{
    private static ReactorAppOptions _options = new();
    internal static ReactorAppOptions Options
    {
        get => Volatile.Read(ref _options);
        set => Volatile.Write(ref _options, value);
    }

    private static ReactorHost? _activeHost;

    /// <summary>The host of the active Reactor window (single-window model).</summary>
    public static ReactorHost? ActiveHost => Volatile.Read(ref _activeHost);

    internal static ReactorHost? ActiveHostInternal
    {
        get => Volatile.Read(ref _activeHost);
        set => Volatile.Write(ref _activeHost, value);
    }

    private static DispatcherQueue? _uiDispatcher;

    /// <summary>The UI dispatcher captured at launch (null before the window exists).</summary>
    public static DispatcherQueue? UIDispatcher
    {
        get => Volatile.Read(ref _uiDispatcher);
        internal set => Volatile.Write(ref _uiDispatcher, value);
    }

    /// <summary>Optional process-wide logger snapshotted by each host at construction.</summary>
    public static ILogger? AppLogger { get; set; }

    /// <summary>
    /// Optional process-wide unhandled-exception hook. Return true to mark the
    /// exception handled; return false (or leave null) to let it crash. Mirrors
    /// the Windows framework's <c>ReactorApp.OnUnhandledException</c>.
    /// </summary>
    public static Func<Exception, bool>? OnUnhandledException { get; set; }

    /// <summary>Devtools are not available in the Uno port.</summary>
    public static bool DevtoolsEnabled => false;

    // ── window topology (tray icons remain a Windows-shell stub) ──
    private static readonly List<ReactorWindow> _windows = new();
    private static readonly List<ReactorTrayIcon> _trayIcons = new();

    /// <summary>Snapshot of open Reactor windows.</summary>
    public static IReadOnlyList<ReactorWindow> Windows
    {
        get { lock (_windows) { return _windows.ToArray(); } }
    }

    /// <summary>The primary (first) window, or null before launch.</summary>
    public static ReactorWindow? PrimaryWindow { get; internal set; }

    internal static void RegisterWindow(ReactorWindow w)
    {
        lock (_windows) { if (!_windows.Contains(w)) _windows.Add(w); }
        PrimaryWindow ??= w;
    }

    internal static void UnregisterWindow(ReactorWindow w)
    {
        lock (_windows)
        {
            _windows.Remove(w);
            if (ReferenceEquals(PrimaryWindow, w))
                PrimaryWindow = _windows.Count > 0 ? _windows[0] : null;
        }
    }

    /// <summary>Finds an open window by its <see cref="WindowSpec.Key"/>.</summary>
    public static ReactorWindow? FindWindow(WindowKey key)
    {
        lock (_windows)
        {
            foreach (var w in _windows)
                if (w.Spec.Key == key) return w;
        }
        return null;
    }

    /// <summary>
    /// Opens a window hosting its own Reactor tree. Signature-compatible with the
    /// Windows framework's <c>ReactorApp.OpenWindow</c>.
    /// </summary>
    /// <remarks>
    /// <para>Real secondary windows work on every Uno <b>desktop</b> head
    /// (X11 / Win32 / macOS / FrameBuffer). Android and iOS do not support secondary
    /// windows: Uno throws <see cref="InvalidOperationException"/> from the
    /// <c>Window</c> constructor, which <c>UseOpenWindow</c> catches and degrades to
    /// a null handle rather than failing the render.</para>
    /// <para>Must be called on the UI thread.</para>
    /// </remarks>
    public static ReactorWindow OpenWindow(
        WindowSpec spec,
        Func<Component> root,
        Action<ReactorHost>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(root);
        return OpenWindowCore(spec, root, renderFunc: null, configure);
    }

    /// <summary>
    /// Opens a window with a render-function root. See the <see cref="Component"/>
    /// overload for platform support and <paramref name="configure"/> semantics.
    /// </summary>
    public static ReactorWindow OpenWindow(
        WindowSpec spec,
        Func<RenderContext, Element> render,
        Action<ReactorHost>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(render);
        return OpenWindowCore(spec, rootFactory: null, render, configure);
    }

    // Single construction path for every Reactor window — the primary one that
    // UnoBootstrap opens at launch and every secondary window opened later.
    internal static ReactorWindow OpenWindowCore(
        WindowSpec spec,
        Func<Component>? rootFactory,
        Func<RenderContext, Element>? renderFunc,
        Action<ReactorHost>? configure)
    {
        // On Android/iOS this throws InvalidOperationException for anything after
        // the first window. Deliberately not caught here: UseOpenWindow handles it.
        var native = new Microsoft.UI.Xaml.Window();
        var window = new ReactorWindow(native, spec);
        var host = new ReactorHost(native) { OwningWindow = window };
        window.Host = host;

        configure?.Invoke(host);
        RegisterWindow(window);
        native.Closed += (_, _) => UnregisterWindow(window);

        try
        {
            if (rootFactory is not null)
                host.Mount(rootFactory());
            else if (renderFunc is not null)
                host.Mount(renderFunc);

            ApplyChrome(native, spec);
            native.Activate();
            // DIP->physical sizing needs a live RasterizationScale, so it can
            // only run once the window has a XamlRoot. Attempt it here and let
            // ReactorWindow.OnContentAttached retry if that hasn't happened yet.
            window.ApplyInitialSize();
        }
        catch
        {
            UnregisterWindow(window);
            try { host.Dispose(); } catch { /* best effort */ }
            throw;
        }

        return window;
    }

    // Title + caption height. AppWindow is only partially supported across Skia
    // heads, so every step is best-effort.
    private static void ApplyChrome(Microsoft.UI.Xaml.Window native, WindowSpec spec)
    {
        try { native.Title = spec.Title; } catch { /* best effort */ }

        if (spec.TitleBarHeight is { } height)
        {
            try
            {
                var titleBar = native.AppWindow?.TitleBar;
                if (titleBar is not null)
                {
                    titleBar.PreferredHeightOption = height switch
                    {
                        WindowTitleBarHeight.Tall => TitleBarHeightOption.Tall,
                        WindowTitleBarHeight.Collapsed => TitleBarHeightOption.Collapsed,
                        _ => TitleBarHeightOption.Standard,
                    };
                }
            }
            catch { /* caption sizing unsupported on this head */ }
        }
    }

    /// <summary>Tray icons are a Windows shell feature; returns a stub handle.</summary>
    public static ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec)
    {
        var icon = new ReactorTrayIcon(spec);
        lock (_trayIcons) { _trayIcons.Add(icon); }
        return icon;
    }

    /// <summary>Finds a registered tray-icon stub by key.</summary>
    public static ReactorTrayIcon? FindTrayIcon(WindowKey key)
    {
        lock (_trayIcons)
        {
            foreach (var t in _trayIcons)
                if (t.Spec.Key == key) return t;
        }
        return null;
    }

    /// <summary>
    /// Bulk-registers the built-in control catalog. Factory methods already
    /// self-register on first use, so this is only needed for the direct
    /// element-record idiom. The Uno port relies on factory self-registration,
    /// so this is currently a no-op kept for API parity.
    /// </summary>
    public static void RegisterAllBuiltIns() { /* factories self-register */ }

    // ── public Run entry points ───────────────────────────────────────────

    /// <summary>
    /// Launches a Reactor app whose root is a <see cref="Component"/> subclass.
    /// Blocks until the window closes (desktop). For WebAssembly use
    /// <see cref="RunAsync{TRoot}"/>.
    /// </summary>
    public static void Run<TRoot>(
        string title = "Reactor App",
        double? width = null,
        double? height = null,
        bool fullScreen = false,
        Action<ReactorHost>? configure = null)
        where TRoot : Component, new()
    {
        Options = new ReactorAppOptions(
            RootFactory: static () => new TRoot(),
            Configure: configure,
            WindowTitle: title,
            WindowWidth: width,
            WindowHeight: height,
            FullScreen: fullScreen);
        UnoBootstrap.Run();
    }

    /// <summary>
    /// Launches a Reactor app from a render function (no Component subclass).
    /// Blocks until the window closes (desktop).
    /// </summary>
    public static void Run(
        string title,
        Func<RenderContext, Element> rootRender,
        double? width = null,
        double? height = null,
        bool fullScreen = false,
        Action<ReactorHost>? configure = null)
    {
        Options = new ReactorAppOptions(
            RootRenderFunc: rootRender,
            Configure: configure,
            WindowTitle: title,
            WindowWidth: width,
            WindowHeight: height,
            FullScreen: fullScreen);
        UnoBootstrap.Run();
    }

    /// <summary>
    /// Async launch for WebAssembly (the browser thread cannot block). Mirror of
    /// <see cref="Run{TRoot}"/>; await it from a file-based app's top-level statements.
    /// </summary>
    public static Task RunAsync<TRoot>(
        string title = "Reactor App",
        double? width = null,
        double? height = null,
        bool fullScreen = false,
        Action<ReactorHost>? configure = null)
        where TRoot : Component, new()
    {
        Options = new ReactorAppOptions(
            RootFactory: static () => new TRoot(),
            Configure: configure,
            WindowTitle: title,
            WindowWidth: width,
            WindowHeight: height,
            FullScreen: fullScreen);
        return UnoBootstrap.RunAsync();
    }

    /// <summary>Async launch (render function) for WebAssembly.</summary>
    public static Task RunAsync(
        string title,
        Func<RenderContext, Element> rootRender,
        double? width = null,
        double? height = null,
        bool fullScreen = false,
        Action<ReactorHost>? configure = null)
    {
        Options = new ReactorAppOptions(
            RootRenderFunc: rootRender,
            Configure: configure,
            WindowTitle: title,
            WindowWidth: width,
            WindowHeight: height,
            FullScreen: fullScreen);
        return UnoBootstrap.RunAsync();
    }

    // ── mobile / native-head entry point ──────────────────────────────────

    /// <summary>
    /// Builds the Uno <see cref="Microsoft.UI.Xaml.Application"/> that hosts
    /// <typeparamref name="TRoot"/>, without starting a host builder.
    /// </summary>
    /// <remarks>
    /// <para>Android and iOS do not start from a console <c>Main</c> — the OS
    /// owns the entry point (an <c>Activity</c> / <c>AppDelegate</c>) and the
    /// native head hands Uno an application factory. <see cref="Run{TRoot}"/>
    /// therefore cannot be used there; call this from the head instead:</para>
    /// <code>
    /// public class Application : Microsoft.UI.Xaml.NativeApplication
    /// {
    ///     public Application(IntPtr javaReference, JniHandleOwnership transfer)
    ///         : base(() => ReactorApp.CreateApplication&lt;CounterApp&gt;("My App"),
    ///                javaReference, transfer) { }
    /// }
    /// </code>
    /// <para>Everything above the hosting layer — components, hooks, the
    /// reconciler — is identical to desktop and WebAssembly.</para>
    /// </remarks>
    public static Microsoft.UI.Xaml.Application CreateApplication<TRoot>(
        string title = "Reactor App",
        Action<ReactorHost>? configure = null)
        where TRoot : Component, new()
    {
        Options = new ReactorAppOptions(
            RootFactory: static () => new TRoot(),
            Configure: configure,
            WindowTitle: title);
        return new ReactorApplication();
    }

    /// <summary>
    /// Render-function overload of <see cref="CreateApplication{TRoot}"/> for
    /// native heads.
    /// </summary>
    public static Microsoft.UI.Xaml.Application CreateApplication(
        string title,
        Func<RenderContext, Element> rootRender,
        Action<ReactorHost>? configure = null)
    {
        Options = new ReactorAppOptions(
            RootRenderFunc: rootRender,
            Configure: configure,
            WindowTitle: title);
        return new ReactorApplication();
    }
}
