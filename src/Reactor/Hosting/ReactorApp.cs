using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Configuration for ReactorApp.Run. Scoped as a single record to avoid scattered static fields.
/// </summary>
internal record ReactorAppOptions(
    Func<Component>? RootFactory = null,
    Func<RenderContext, Element>? RootRenderFunc = null,
    Action<ReactorHost>? Configure = null,
    string WindowTitle = "Reactor App",
    double WindowWidth = 1024,
    double WindowHeight = 768,
    bool FullScreen = false,
    Action<ReactorAppContext>? Startup = null,
    WindowSpec? InitialWindowSpec = null);

public static class ReactorApp
{
    // Application.Start blocks and creates ReactorApplication via parameterless constructor,
    // so we must communicate config through a static. Using a single record keeps this scoped.
    private static ReactorAppOptions _options = new();
    internal static ReactorAppOptions Options
    {
        get => Volatile.Read(ref _options);
        set => Volatile.Write(ref _options, value);
    }
    private static ReactorHost? _activeHost;
    /// <summary>
    /// Legacy alias for the host of the first window opened in this process.
    /// (spec 036 §4.3 / §12.4)
    /// </summary>
    [Obsolete("Use ReactorApp.PrimaryWindow.Host or ReactorApp.Windows.")]
    public static ReactorHost? ActiveHost
    {
        get => Volatile.Read(ref _activeHost);
        internal set => Volatile.Write(ref _activeHost, value);
    }

    // Internal setter that bypasses the obsolete shim — used by ReactorHost
    // and the Window primitive. Treated as the single source of truth for the
    // legacy alias. Phase 4 deletes this once consumers migrate.
    internal static ReactorHost? ActiveHostInternal
    {
        get => Volatile.Read(ref _activeHost);
        set => Volatile.Write(ref _activeHost, value);
    }

    // ── Spec 036: process-wide window topology ─────────────────────────────
    private static ReactorWindow[] _windows = global::System.Array.Empty<ReactorWindow>();
    private static ReactorTrayIcon[] _trayIcons = global::System.Array.Empty<ReactorTrayIcon>();
    private static ReactorWindow? _primaryWindow;
    private static int _shutdownPolicy = (int)ShutdownPolicy.OnPrimaryWindowClosed;
    private static Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;
    private static ReactorAppContext? _appContext;

    /// <summary>
    /// Snapshot of every open <see cref="ReactorWindow"/>. Copy-on-write; safe
    /// to enumerate from any thread.
    /// </summary>
    public static IReadOnlyList<ReactorWindow> Windows => Volatile.Read(ref _windows);

    /// <summary>
    /// The first window opened during this process's startup callback, or
    /// <c>null</c> when none has been opened (tray-only / pre-startup).
    /// </summary>
    public static ReactorWindow? PrimaryWindow
    {
        get => Volatile.Read(ref _primaryWindow);
        internal set => Volatile.Write(ref _primaryWindow, value);
    }

    /// <summary>
    /// The UI <see cref="Microsoft.UI.Dispatching.DispatcherQueue"/> captured
    /// at <c>OnLaunched</c> — null until the first window has been bootstrapped.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue? UIDispatcher
    {
        get => Volatile.Read(ref _uiDispatcher);
        internal set => Volatile.Write(ref _uiDispatcher, value);
    }

    private static Hosting.Persistence.IWindowPersistenceStore? _windowPersistenceStore;
    private static int _persistenceStoreLocked;

    /// <summary>
    /// Process-wide store backing <see cref="WindowSpec.PersistenceId"/>.
    /// Defaults are picked lazily on first window open: packaged apps get
    /// <see cref="Hosting.Persistence.PackagedSettingsStore"/>; unpackaged apps
    /// get <see cref="Hosting.Persistence.JsonFileStore"/>. Setting this
    /// property after the first <c>OpenWindow</c> throws — windows that
    /// already loaded their placement from the previous store would get a
    /// half-populated state. (spec 036 §8)
    /// </summary>
    public static Hosting.Persistence.IWindowPersistenceStore? WindowPersistenceStore
    {
        get => Volatile.Read(ref _windowPersistenceStore);
        set
        {
            if (Volatile.Read(ref _persistenceStoreLocked) != 0)
                throw new InvalidOperationException(
                    "ReactorApp.WindowPersistenceStore can only be set before the first OpenWindow. (spec 036 §8)");
            Volatile.Write(ref _windowPersistenceStore, value);
        }
    }

    internal static IDisposable UseWindowPersistenceStoreForTests(Hosting.Persistence.IWindowPersistenceStore? store)
    {
        var previous = Volatile.Read(ref _windowPersistenceStore);
        var previousLocked = Volatile.Read(ref _persistenceStoreLocked);
        Volatile.Write(ref _windowPersistenceStore, store);
        return new TestPersistenceStoreScope(previous, previousLocked);
    }

    private sealed class TestPersistenceStoreScope : IDisposable
    {
        private Hosting.Persistence.IWindowPersistenceStore? _previous;
        private readonly int _previousLocked;
        public TestPersistenceStoreScope(Hosting.Persistence.IWindowPersistenceStore? previous, int previousLocked)
        {
            _previous = previous;
            _previousLocked = previousLocked;
        }
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref _previous, null);
            Volatile.Write(ref _windowPersistenceStore, previous);
            Volatile.Write(ref _persistenceStoreLocked, _previousLocked);
        }
    }

    internal static Hosting.Persistence.IWindowPersistenceStore? ResolvePersistenceStore()
    {
        // Snapshot once and lock subsequent assignments. Idempotent; the
        // CompareExchange flips 0→1 on the first window open per process.
        Interlocked.CompareExchange(ref _persistenceStoreLocked, 1, 0);
        var store = Volatile.Read(ref _windowPersistenceStore);
        if (store is not null) return store;

        // Auto-pick: packaged apps get the WinRT settings store; unpackaged
        // apps get the JSON file store.
        try
        {
            store = Hosting.Persistence.PackagedSettingsStore.IsAvailable()
                ? new Hosting.Persistence.PackagedSettingsStore()
                : new Hosting.Persistence.JsonFileStore();
            Volatile.Write(ref _windowPersistenceStore, store);
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[Reactor] ResolvePersistenceStore failed: {ex.GetType().Name}: {ex.Message}");
        }
        return store;
    }

    /// <summary>Process-shutdown policy. Defaults to <see cref="ShutdownPolicy.OnPrimaryWindowClosed"/>.</summary>
    public static ShutdownPolicy ShutdownPolicy
    {
        get => (ShutdownPolicy)Volatile.Read(ref _shutdownPolicy);
        set => Volatile.Write(ref _shutdownPolicy, (int)value);
    }

    /// <summary>Fires on the UI thread when a <see cref="ReactorWindow"/> opens.</summary>
    public static event EventHandler<ReactorWindow>? WindowOpened;

    /// <summary>Fires on the UI thread when a <see cref="ReactorWindow"/> closes.</summary>
    public static event EventHandler<ReactorWindow>? WindowClosed;

    /// <summary>
    /// Snapshot of every open <see cref="ReactorTrayIcon"/>. Copy-on-write;
    /// safe to enumerate from any thread. (spec 036 §11.4)
    /// </summary>
    public static IReadOnlyList<ReactorTrayIcon> TrayIcons => Volatile.Read(ref _trayIcons);

    /// <summary>Fires on the UI thread when a <see cref="ReactorTrayIcon"/> opens.</summary>
    public static event EventHandler<ReactorTrayIcon>? TrayIconOpened;

    /// <summary>Fires on the UI thread when a <see cref="ReactorTrayIcon"/> closes.</summary>
    public static event EventHandler<ReactorTrayIcon>? TrayIconClosed;

    // Process-wide ILogger picked up by ReactorHost / ReactorHostControl when
    // the caller doesn't pass one explicitly. Null by default. Apps that want
    // a unified fallback should set this BEFORE creating the first host —
    // hosts snapshot the value at construction, so a later set won't retro-
    // actively wire up already-running hosts. Also routes ReactorApplication's
    // unhandled-exception handler when devtools is off.
    //
    // Cold-path note: leaving this null keeps Microsoft.Extensions.Logging
    // resolution off the JIT critical path entirely — none of the LoggerExtensions
    // call sites get walked when the field is null.
    private static ILogger? _appLogger;
    public static ILogger? AppLogger
    {
        get => Volatile.Read(ref _appLogger);
        set => Volatile.Write(ref _appLogger, value);
    }

    // ── XAML control-assembly registration ─────────────────────────────────
    //
    // The lifted XAML loader resolves `local:` namespaces and Generic.xaml type
    // references through Application.Current's IXamlMetadataProvider chain.
    // ReactorApplication auto-discovers the *entry assembly's* compiler-generated
    // provider, but that breaks down for third-party control libraries when the
    // consuming Reactor app has no XAML files of its own — in that case the
    // app's compiler-generated provider doesn't exist, so referenced libraries
    // never get chained. Registered providers fill that gap.
    //
    // CopyOnWrite snapshot semantics so reads from GetXamlType (called on the UI
    // thread, hot path) need no locking.
    private static IXamlMetadataProvider[] _registeredXamlMetadataProviders = [];
    private static readonly object _registeredXamlMetadataProvidersLock = new();

    /// <summary>
    /// Registers a XAML metadata provider so its types are visible to the WinUI
    /// XAML loader for this process. Required when a third-party control library
    /// is referenced from a Reactor app that has no XAML files of its own (and
    /// therefore no compiler-generated provider that would auto-chain to the
    /// library). Call before <see cref="Run{TRoot}(string, double, double, bool, Action{ReactorHost}?)"/>.
    /// Idempotent (same instance is added at most once) and thread-safe.
    /// See https://github.com/microsoft/microsoft-ui-reactor/issues/142.
    /// </summary>
    public static void RegisterControlAssembly(IXamlMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_registeredXamlMetadataProvidersLock)
        {
            var current = _registeredXamlMetadataProviders;
            if (Array.IndexOf(current, provider) >= 0) return;
            var next = new IXamlMetadataProvider[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = provider;
            Volatile.Write(ref _registeredXamlMetadataProviders, next);
        }
    }

    /// <summary>
    /// Convenience overload that locates the XAML-compiler-generated
    /// <c>IXamlMetadataProvider</c> in <paramref name="assembly"/> (the type the
    /// XAML compiler emits when the project has at least one XAML file) and
    /// registers it. Throws if no such provider is found — pass the
    /// <see cref="IXamlMetadataProvider"/> instance directly if your library
    /// uses a non-standard provider type.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Caller-supplied assembly's XAML metadata provider is preserved by the XAML compiler that emits it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Parameterless ctor invoked on a freshly-discovered IXamlMetadataProvider type.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection over caller-supplied assembly types; XAML compiler preserves IXamlMetadataProvider implementations.")]
    public static void RegisterControlAssembly(global::System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var provider = FindXamlMetadataProviderInAssembly(assembly)
            ?? throw new InvalidOperationException(
                $"No IXamlMetadataProvider found in {assembly.GetName().Name}. " +
                "The XAML compiler only generates one when the project has at least one XAML file. " +
                "If you have a hand-written provider, pass the instance directly to RegisterControlAssembly.");
        RegisterControlAssembly(provider);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "See RegisterControlAssembly(Assembly).")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "See RegisterControlAssembly(Assembly).")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "See RegisterControlAssembly(Assembly).")]
    internal static IXamlMetadataProvider? FindXamlMetadataProviderInAssembly(global::System.Reflection.Assembly assembly)
    {
        global::System.Type[] types;
        try { types = assembly.GetTypes(); }
        catch (global::System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.OfType<global::System.Type>().ToArray(); }

        foreach (var t in types)
        {
            if (!typeof(IXamlMetadataProvider).IsAssignableFrom(t)) continue;
            if (t.IsAbstract || t.IsInterface) continue;
            if (t.GetConstructor(global::System.Type.EmptyTypes) is null) continue;
            try { return (IXamlMetadataProvider)global::System.Activator.CreateInstance(t)!; }
            catch { /* keep scanning — a broken candidate must not deny a valid one */ }
        }
        return null;
    }

    internal static IXamlMetadataProvider[] RegisteredControlAssemblyProviders
        => Volatile.Read(ref _registeredXamlMetadataProviders);

    // Session-scoped flag. True iff the process was launched with a devtools
    // subverb (--devtools app / --devtools run) and the binary was built with
    // Reactor.DevtoolsSupport enabled. Frozen after startup; read by UseDevtools()
    // and by the DevtoolsMenu component to decide whether to render themselves.
    private static int _devtoolsEnabled;
    public static bool DevtoolsEnabled
    {
        get => Volatile.Read(ref _devtoolsEnabled) != 0;
        internal set => Volatile.Write(ref _devtoolsEnabled, value ? 1 : 0);
    }

    // Unpackaged WinUI apps (WindowsPackageType=None) don't inherit DPI awareness from an
    // MSIX manifest, so the process defaults to DPI-unaware and Windows applies blurry bitmap
    // scaling. Setting PerMonitorV2 awareness before any window is created tells the OS the
    // app will handle DPI itself, producing crisp rendering at any scale factor.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    private static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    /// <summary>
    /// Launches the app. Enable the <c>Reactor.DevtoolsSupport</c> runtime host
    /// configuration switch in DEBUG builds to allow the <c>mur devtools</c> /
    /// <c>--devtools</c> surface: component switching via VS Code, MCP agent tools,
    /// and component listing.
    /// </summary>
    public static void Run<TRoot>(
        string title = "Reactor App",
        double width = 1024,
        double height = 768,
        bool fullScreen = false,
        Action<ReactorHost>? configure = null)
        where TRoot : Component, new()
    {
        EmitDipBehaviorChangeNoticeOnce();
        if (TryRunDevtools(title, width, height, fullScreen, configure, hostRoot: typeof(TRoot), hostRootFactory: static () => new TRoot())) return;

        RunOnSta(() =>
        {
            InitProcess();
            Options = new ReactorAppOptions(
                RootFactory: () => new TRoot(),
                Configure: configure,
                WindowTitle: title,
                WindowWidth: width,
                WindowHeight: height,
                FullScreen: fullScreen);

            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new ReactorApplication();
            });
        });
    }

    /// <summary>
    /// Launches the app with a render function instead of a Component subclass.
    /// See the generic overload for devtools activation semantics.
    /// </summary>
    public static void Run(
        string title,
        Func<RenderContext, Element> rootRender,
        double width = 1024,
        double height = 768,
        bool fullScreen = false,
        Action<ReactorHost>? configure = null)
    {
        EmitDipBehaviorChangeNoticeOnce();
        if (TryRunDevtools(title, width, height, fullScreen, configure, rootRenderFunc: rootRender)) return;

        RunOnSta(() =>
        {
            InitProcess();
            Options = new ReactorAppOptions(
                RootRenderFunc: rootRender,
                Configure: configure,
                WindowTitle: title,
                WindowWidth: width,
                WindowHeight: height,
                FullScreen: fullScreen);

            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new ReactorApplication();
            });
        });
    }

    /// <summary>
    /// Multi-window startup entry. The <paramref name="startup"/> callback runs
    /// on the UI thread after WinUI bootstraps and before any default window is
    /// opened. Open windows or tray icons (Phase 8) directly from inside the
    /// callback. (spec 036 §4.3 / §6.1)
    /// </summary>
    public static void Run(Action<ReactorAppContext> startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        EmitDipBehaviorChangeNoticeOnce();
        RunOnSta(() =>
        {
            InitProcess();
            Options = new ReactorAppOptions(Startup: startup);
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new ReactorApplication();
            });
        });
    }

    // ── window topology ───────────────────────────────────────────────────

    /// <summary>
    /// Open a window with a <see cref="Component"/> root. UI-thread only.
    /// (spec 036 §4.3)
    /// </summary>
    /// <param name="spec">Declarative description of the window's chrome.</param>
    /// <param name="root">Factory invoked once to materialize the root component.</param>
    /// <param name="configure">
    /// Optional callback invoked on the UI thread <b>before</b> the root mounts.
    /// Use it for pre-mount setup that needs the live <see cref="ReactorHost"/>
    /// (logger wiring, custom reconciler registrations, host-level event
    /// hooks). Mirror of the <c>configure</c> parameter on the legacy
    /// <see cref="Run{TRoot}(string, double, double, bool, Action{ReactorHost}?)"/>
    /// entry — secondary windows now have the same escape hatch as the
    /// primary.
    /// </param>
    public static ReactorWindow OpenWindow(
        WindowSpec spec,
        Func<Component> root,
        Action<ReactorHost>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(root);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(OpenWindow));
        return OpenWindowCore(spec, root, renderFunc: null, configure: configure);
    }

    /// <summary>
    /// Open a window with a render-function root. UI-thread only. See the
    /// <see cref="OpenWindow(WindowSpec, Func{Component}, Action{ReactorHost}?)"/>
    /// overload for <paramref name="configure"/> semantics.
    /// </summary>
    public static ReactorWindow OpenWindow(
        WindowSpec spec,
        Func<RenderContext, Element> render,
        Action<ReactorHost>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(render);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(OpenWindow));
        return OpenWindowCore(spec, rootFactory: null, render, configure: configure);
    }

    // Internal overload used by the legacy Run<TRoot>/Run(string, Func) bridges
    // so they can plug in their pre-mount Configure callback in the slot the
    // public OpenWindow path doesn't expose.
    internal static ReactorWindow OpenWindowCore(
        WindowSpec spec,
        Func<Component>? rootFactory,
        Func<RenderContext, Element>? renderFunc,
        Action<ReactorHost>? configure)
    {
        Console.Error.WriteLine("[embed:trace] OpenWindowCore enter (embed=" + (spec.Embed is not null) + ")");
        ReactorWindow window;
        try
        {
            window = new ReactorWindow(spec);
            Console.Error.WriteLine("[embed:trace] OpenWindowCore: ReactorWindow ctor ok");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[embed:trace] ReactorWindow ctor THREW: " + ex);
            throw;
        }
        try { configure?.Invoke(window.Host); Console.Error.WriteLine("[embed:trace] OpenWindowCore: configure ok"); }
        catch (Exception ex) { Console.Error.WriteLine("[embed:trace] configure THREW: " + ex); throw; }
        RegisterWindow(window);
        try
        {
            window.MountAndActivate(rootFactory, renderFunc);
            Console.Error.WriteLine("[embed:trace] OpenWindowCore: MountAndActivate ok");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[embed:trace] MountAndActivate THREW: " + ex);
            UnregisterWindow(window);
            try { window.Dispose(); } catch { /* best effort */ }
            throw;
        }
        return window;
    }

    /// <summary>
    /// Open a system-tray icon. UI-thread only. The returned handle stays
    /// alive until <see cref="ReactorTrayIcon.Close"/> / <see cref="ReactorTrayIcon.Dispose"/>
    /// is called or the process exits. (spec 036 §11.4)
    /// </summary>
    public static ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(OpenTrayIcon));
        var icon = new ReactorTrayIcon(spec);
        RegisterTrayIcon(icon);
        try
        {
            icon.RegisterWithShell();
        }
        catch
        {
            UnregisterTrayIcon(icon);
            try { icon.Dispose(); } catch { /* best effort */ }
            throw;
        }
        return icon;
    }

    /// <summary>Look up an open tray icon by <see cref="WindowKey"/>.</summary>
    public static ReactorTrayIcon? FindTrayIcon(WindowKey key)
    {
        var snapshot = TrayIcons;
        for (int i = 0; i < snapshot.Count; i++)
        {
            var t = snapshot[i];
            if (t.Key is { } k && k.Equals(key)) return t;
        }
        return null;
    }

    /// <summary>Look up an open window by <see cref="WindowKey"/>.</summary>
    public static ReactorWindow? FindWindow(WindowKey key)
    {
        var snapshot = Windows;
        for (int i = 0; i < snapshot.Count; i++)
        {
            var w = snapshot[i];
            if (w.Key is { } k && k.Equals(key)) return w;
        }
        return null;
    }

    /// <summary>
    /// Exit the process. UI-thread only. With <paramref name="exitCode"/> set
    /// to its default (<c>0</c>), routes through
    /// <see cref="Application.Exit"/> so WinUI gets a clean unwind. With a
    /// non-zero exit code, calls <see cref="Application.Exit"/> first to
    /// release resources, then <see cref="Environment.Exit(int)"/> with the
    /// requested code so the parent process sees it (assigning
    /// <see cref="Environment.ExitCode"/> would only take effect on a
    /// natural managed-entry-point return, which an app under
    /// <c>Application.Start</c> does not perform).
    /// </summary>
    public static void Exit(int exitCode = 0)
    {
        ThreadAffinity.ThrowIfNotOnUIThread(nameof(Exit));
        try { Application.Current?.Exit(); }
        catch { /* best effort */ }
        if (exitCode != 0)
            Environment.Exit(exitCode);
    }

    // Copy-on-write add. UI-thread only — reads can happen anywhere.
    internal static void RegisterWindow(ReactorWindow window)
    {
        var current = Volatile.Read(ref _windows);
        var next = new ReactorWindow[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[^1] = window;
        Volatile.Write(ref _windows, next);

        if (PrimaryWindow is null)
            PrimaryWindow = window;

        ReactorDisplay.RegisterWindowMonitor(window, window.MessageMonitor);

        try { WindowOpened?.Invoke(null, window); }
        catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] WindowOpened threw: {ex.Message}"); }
    }

    // Copy-on-write remove. Idempotent — removing an already-removed window
    // (Phase 1 path runs Dispose → close cascade twice in some failure modes)
    // is a no-op.
    internal static void UnregisterWindow(ReactorWindow window)
    {
        var current = Volatile.Read(ref _windows);
        int idx = Array.IndexOf(current, window);
        if (idx < 0) return;

        var next = new ReactorWindow[current.Length - 1];
        if (idx > 0) Array.Copy(current, 0, next, 0, idx);
        if (idx < current.Length - 1) Array.Copy(current, idx + 1, next, idx, current.Length - idx - 1);
        Volatile.Write(ref _windows, next);

        // Capture whether this window was the primary BEFORE we re-elect, so
        // ShutdownPolicy.OnPrimaryWindowClosed can distinguish "primary just
        // died" from "secondary closed while primary still alive."
        bool wasPrimary = ReferenceEquals(PrimaryWindow, window);
        if (wasPrimary)
            PrimaryWindow = next.Length > 0 ? next[0] : null;

        ReactorDisplay.UnregisterWindowMonitor(window);

        try { WindowClosed?.Invoke(null, window); }
        catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] WindowClosed threw: {ex.Message}"); }

        EvaluateShutdownPolicy(closedWasPrimary: wasPrimary);
    }

    // OnPrimaryWindowClosed: exit when the just-closed window was the primary.
    // OnLastSurfaceClosed: exit when both windows and tray icons are gone.
    // Explicit: never exit from a surface-close event — the app drives Exit().
    internal static void EvaluateShutdownPolicy(bool closedWasPrimary)
    {
        var policy = ShutdownPolicy;
        var snapshot = Windows;
        switch (policy)
        {
            case ShutdownPolicy.OnPrimaryWindowClosed:
                if (closedWasPrimary)
                    SafeExit();
                break;
            case ShutdownPolicy.OnLastSurfaceClosed:
                if (snapshot.Count == 0 && TrayIconCount == 0)
                    SafeExit();
                break;
            case ShutdownPolicy.Explicit:
                break;
        }
    }

    // Phase-8: real tray-icon registry. Counts the COW snapshot so the
    // OnLastSurfaceClosed branch agrees with the rest of the surface.
    internal static int TrayIconCount => Volatile.Read(ref _trayIcons).Length;

    // Copy-on-write add. UI-thread only — reads can happen anywhere. (spec 036 §11.4)
    internal static void RegisterTrayIcon(ReactorTrayIcon icon)
    {
        var current = Volatile.Read(ref _trayIcons);
        var next = new ReactorTrayIcon[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[^1] = icon;
        Volatile.Write(ref _trayIcons, next);

        try { TrayIconOpened?.Invoke(null, icon); }
        catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] TrayIconOpened threw: {ex.Message}"); }
    }

    // Copy-on-write remove. Idempotent.
    internal static void UnregisterTrayIcon(ReactorTrayIcon icon)
    {
        var current = Volatile.Read(ref _trayIcons);
        int idx = Array.IndexOf(current, icon);
        if (idx < 0) return;

        var next = new ReactorTrayIcon[current.Length - 1];
        if (idx > 0) Array.Copy(current, 0, next, 0, idx);
        if (idx < current.Length - 1) Array.Copy(current, idx + 1, next, idx, current.Length - idx - 1);
        Volatile.Write(ref _trayIcons, next);

        try { TrayIconClosed?.Invoke(null, icon); }
        catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] TrayIconClosed threw: {ex.Message}"); }

        // OnLastSurfaceClosed: closing the final tray icon when no windows
        // remain should exit just like closing the final window.
        if (ShutdownPolicy == ShutdownPolicy.OnLastSurfaceClosed
            && Windows.Count == 0
            && TrayIconCount == 0)
        {
            SafeExit();
        }
    }

    private static void SafeExit()
    {
        try { Application.Current?.Exit(); }
        catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] Application.Exit threw: {ex.Message}"); }
    }

    // Internal accessor for ReactorApplication.OnLaunched and tests.
    internal static ReactorAppContext? AppContext
    {
        get => Volatile.Read(ref _appContext);
        set => Volatile.Write(ref _appContext, value);
    }

    private static int _dipBehaviorChangeNoticeEmitted;

    /// <summary>
    /// Emit one stderr <c>[reactor]</c> info-line per process the first time
    /// any <c>Run</c> overload is invoked, describing the DIP-vs-pixel size
    /// behavior change. (spec 036 §12.1) The Phase-2 layer adds the actual
    /// DIP→pixel conversion; the message is wired now so the diagnostic
    /// surface lands in the same release.
    /// </summary>
    internal static void EmitDipBehaviorChangeNoticeOnce()
    {
        if (Interlocked.CompareExchange(ref _dipBehaviorChangeNoticeEmitted, 1, 0) != 0) return;
        Console.Error.WriteLine(
            "[reactor] WindowSpec.Width / Height and ReactorApp.Run<T>(width, height) are now DIPs. " +
            "On a 100% display this is unchanged; on 200% the window is twice as large in physical pixels. (spec 036 §12.1)");
    }

    /// <summary>
    /// Checks the process command-line for <c>--devtools</c> or the deprecated <c>--preview</c>.
    /// If a devtools subverb is selected, launches the corresponding flow (list, run, etc.).
    /// With <c>--vscode</c>, starts the capture server for the VS Code preview panel. Devtools
    /// dispatch requires the build-time <c>Reactor.DevtoolsSupport</c> switch.
    /// </summary>
    private static bool TryRunDevtools(string title, double width, double height, bool fullScreen, Action<ReactorHost>? configure, Type? hostRoot = null, Func<Component>? hostRootFactory = null, Func<RenderContext, Element>? rootRenderFunc = null)
    {
        return TryRunDevtoolsCore(Environment.GetCommandLineArgs(), title, width, height, fullScreen, configure, hostRoot, hostRootFactory, rootRenderFunc, exitOnUnavailable: true);
    }

    internal static bool TryRunDevtoolsForTest(string[] args, string title, double width, double height, Action<ReactorHost>? configure = null, Type? hostRoot = null)
    {
        return TryRunDevtoolsCore(args, title, width, height, fullScreen: false, configure, hostRoot, hostRootFactory: null, rootRenderFunc: null, exitOnUnavailable: false);
    }

    private static bool TryRunDevtoolsCore(string[] args, string title, double width, double height, bool fullScreen, Action<ReactorHost>? configure, Type? hostRoot = null, Func<Component>? hostRootFactory = null, Func<RenderContext, Element>? rootRenderFunc = null, bool exitOnUnavailable = false)
    {
        var options = DevtoolsCliParser.Parse(args);

        if (options.PreviewAndDevtoolsConflict)
        {
            Console.Error.WriteLine("[devtools] Error: pass either --devtools or --preview, not both.");
            return true;
        }

        if (!string.IsNullOrEmpty(options.EmbedValidationError))
        {
            Console.Error.WriteLine($"[reactor] {options.EmbedValidationError}");
            Environment.ExitCode = 2;
            if (exitOnUnavailable) Environment.Exit(2);
            return true;
        }

        if (options.Subverb is null) return false;

        if (ReactorFeatures.IsDevtoolsSupported && ReactorDevtoolsBootstrap.Current is { } host)
        {
            var request = new ReactorDevtoolsBootRequest(
                options,
                title,
                width,
                height,
                fullScreen,
                hostRoot,
                hostRootFactory,
                rootRenderFunc,
                configure);
            return host.TryHandleCommandLine(request);
        }

        EmitDevtoolsNotAvailableMessage();
        Environment.ExitCode = 2;
        if (exitOnUnavailable) Environment.Exit(2);
        return true;
    }

    private static void EmitDevtoolsNotAvailableMessage()
    {
        Console.Error.WriteLine(
            "[reactor] --devtools requested but this binary was built without Reactor.DevtoolsSupport or without the Microsoft.UI.Reactor.Devtools package. " +
            "Devtools must be enabled at build time. Add the following to your csproj and rebuild:");
        Console.Error.WriteLine(
            "  <RuntimeHostConfigurationOption Include=\"Reactor.DevtoolsSupport\" Value=\"true\" Trim=\"true\" />");
    }

    internal static void ResetDevtoolsEnabledForTests()
    {
        Interlocked.Exchange(ref _devtoolsEnabled, 0);
    }

    internal static void ResetDipBehaviorChangeNoticeForTests()
    {
        Interlocked.Exchange(ref _dipBehaviorChangeNoticeEmitted, 0);
    }

    internal static void ResetRegisteredControlAssembliesForTests()
    {
        lock (_registeredXamlMetadataProvidersLock)
            Volatile.Write(ref _registeredXamlMetadataProviders, []);
    }

    internal static void InitProcess()
    {
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            global::System.Diagnostics.Debug.WriteLine($"SetProcessDpiAwarenessContext failed: {Marshal.GetLastWin32Error()}");
        WinRT.ComWrappersSupport.InitializeComWrappers();
    }

    /// <summary>
    /// Ensures the action runs on an STA thread. WinUI 3's DesktopChildSiteBridge requires
    /// STA for UI Automation (screen readers, test tools) to traverse into the XAML island.
    /// Top-level statements and async Main produce MTA threads where [STAThread] cannot be
    /// applied, so we re-launch on a dedicated STA thread when needed.
    /// </summary>
    internal static void RunOnSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        // Current thread is MTA — spawn a new STA thread and run there.
        Exception? caught = null;
        var staThread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        if (caught is not null)
            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }
}

/// <summary>
/// Application subclass that implements IXamlMetadataProvider so the native XAML
/// schema context can resolve managed types from XBF theme resources.
/// No App.xaml needed — XamlControlsResources are loaded programmatically.
/// The IXamlMetadataProvider implementation delegates to the WinUI controls'
/// built-in provider so that custom control types (TextCommandBarFlyout, etc.)
/// can be instantiated from XBF theme resources.
/// </summary>
public partial class ReactorApplication : Application, IXamlMetadataProvider
{
    // The Reactor library's XAML build pipeline generates
    // Microsoft.UI.Reactor.Reactor_XamlTypeInfo.XamlMetaDataProvider — a full provider
    // that covers ReactorDefaultResources, XamlControlsResources, ResourceDictionary,
    // system primitives, and chains to XamlControlsXamlMetaDataProvider for control
    // types. That generated provider is the right primary delegate: it's AOT-safe,
    // preserves type registration via compile-time code rather than runtime reflection,
    // and correctly handles the schema-only lookups WinUI performs during Application
    // startup when theme dictionaries load.
    //
    // We resolve the generated type at runtime because referencing the generated name
    // directly would make the C# pre-compile (run by the XAML compiler itself) fail with
    // CS0246 — the generated class doesn't exist yet when that check runs. The
    // DynamicDependency keeps the type alive under AOT trimming.
    private IXamlMetadataProvider? _reactorProvider;

    private IXamlMetadataProvider ReactorProvider => _reactorProvider ??= CreateReactorProvider();

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors,
        "Microsoft.UI.Reactor.Reactor_XamlTypeInfo.XamlMetaDataProvider", "Reactor")]
    private static IXamlMetadataProvider CreateReactorProvider()
    {
        var t = global::System.Type.GetType("Microsoft.UI.Reactor.Reactor_XamlTypeInfo.XamlMetaDataProvider, Reactor", throwOnError: false);
        return t is null
            ? new Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider()
            : (IXamlMetadataProvider)global::System.Activator.CreateInstance(t)!;
    }

    // Fallback provider covering types WinUI may look up by-string that are not in the
    // generated library provider (e.g. user-defined types in the consuming project
    // referenced by ResourceDictionary keys). Additive safety net — in the normal path
    // the Reactor provider already satisfies queries.
    private IXamlMetadataProvider? _coreProvider;
    private IXamlMetadataProvider CoreProvider => _coreProvider ??= new Hosting.ReactorCoreXamlMetadataProvider();

    // Provider for the consuming app's own XAML-compiler-generated metadata. Without this,
    // a custom Control declared in the user's project crashes when WinUI loads its
    // Themes/Generic.xaml because `local:` namespace lookups go through Application.Current
    // (which is this ReactorApplication) and our chain only knew about Reactor's own types.
    // Empty providers (apps with no custom XAML types) collapse to a no-op stub and cost
    // one reflection scan of the entry assembly at startup.
    // See https://github.com/microsoft/microsoft-ui-reactor/issues/142.
    private IXamlMetadataProvider? _hostAppProvider;
    private IXamlMetadataProvider HostAppProvider => _hostAppProvider ??= DiscoverHostAppProvider();

    private static IXamlMetadataProvider DiscoverHostAppProvider()
    {
        // The XAML compiler emits one IXamlMetadataProvider per project that has any XAML
        // file (typically named `<Sanitized(AssemblyName)>_XamlTypeInfo.XamlMetaDataProvider`,
        // but the exact name varies). Scanning the entry assembly is robust to that drift.
        var entry = global::System.Reflection.Assembly.GetEntryAssembly();
        if (entry is null) return EmptyXamlMetadataProvider.Instance;

        var found = ReactorApp.FindXamlMetadataProviderInAssembly(entry);
        // Reactor's own generated provider lives in the Reactor assembly; if the entry
        // assembly happens to BE Reactor (unit-test hosting), ReactorProvider already
        // covers it and we'd just be re-finding the same type.
        if (found is not null && found.GetType().FullName != "Microsoft.UI.Reactor.Reactor_XamlTypeInfo.XamlMetaDataProvider")
            return found;
        return EmptyXamlMetadataProvider.Instance;
    }

    private sealed partial class EmptyXamlMetadataProvider : IXamlMetadataProvider
    {
        public static readonly EmptyXamlMetadataProvider Instance = new();
        public IXamlType? GetXamlType(Type type) => null;
        public IXamlType? GetXamlType(string fullName) => null;
        public XmlnsDefinition[] GetXmlnsDefinitions() => [];
    }

    /// <summary>
    /// Optional callback for unhandled exceptions. If set, called before deciding whether to handle.
    /// Return true to mark the exception as handled; return false (or leave null) to let it crash.
    /// </summary>
    public static Func<Exception, bool>? OnUnhandledException { get; set; }

    public ReactorApplication()
    {
        // Loads ReactorApplication.xaml (which references XamlControlsResources) via the
        // XAML-compiled, XBF-deserialized path. Under native AOT, constructing
        // XamlControlsResources programmatically crashes — putting it in an Application-level
        // XAML and letting the XAML runtime activate it through LoadComponent during
        // Application construction matches what App.xaml-based projects do and is AOT-safe.
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            ReactorApp.AppLogger?.LogError(e.Exception, "UnhandledException: {ExceptionType}: {ExceptionMessage}", e.Exception.GetType().Name, e.Exception.Message);
            if (OnUnhandledException is not null)
                e.Handled = OnUnhandledException(e.Exception);
            // Don't set e.Handled = true for unknown exceptions — let the app crash
            // with a useful error rather than silently running in a corrupt state.
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Capture the UI dispatcher first thing so anything below — including
        // a startup callback that itself opens windows — sees the right
        // process-wide UI thread reference. (spec 036 §4.3 / §6.1)
        ReactorApp.UIDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        var opts = ReactorApp.Options;
        var activation = ParseLaunchActivation(args);
        var ctx = new ReactorAppContext(activation);
        ReactorApp.AppContext = ctx;

        // ── Path 1: explicit Run(Action<ReactorAppContext>) startup callback.
        if (opts.Startup is not null)
        {
            opts.Startup(ctx);

            // Spec 036 §6.2: with the default OnPrimaryWindowClosed policy, a
            // startup that opens zero windows must exit immediately — that's
            // the only sane default for "I forgot to OpenWindow." Apps that
            // want zero-window startup pick Explicit or OnLastSurfaceClosed
            // before returning from the callback. OnLastSurfaceClosed exits
            // here only if NO tray icon was opened either; otherwise the tray
            // keeps the process alive.
            var noWindows = ReactorApp.Windows.Count == 0;
            var policy = ReactorApp.ShutdownPolicy;
            if (noWindows && policy == ShutdownPolicy.OnPrimaryWindowClosed)
            {
                try { Application.Current?.Exit(); } catch { /* best effort */ }
            }
            else if (noWindows && policy == ShutdownPolicy.OnLastSurfaceClosed && ReactorApp.TrayIconCount == 0)
            {
                try { Application.Current?.Exit(); } catch { /* best effort */ }
            }
            return;
        }

        // ── Path 2: legacy Run<TRoot> / Run(string, Func) bridge — synthesize
        //   a WindowSpec and route through OpenWindowCore so devtools / sample
        //   call sites continue to see one host, one window, with the same
        //   pre-mount Configure callback timing.
        //
        // When neither RootFactory nor RootRenderFunc is set, the
        // ReactorApplication was constructed without going through
        // ReactorApp.Run — the canonical case is the self-test host harness,
        // which constructs the Application directly and owns its own Window
        // creation. Skip the bridge so we don't try to open a window with
        // nothing to mount (which would otherwise cascade into shutdown
        // during OnLaunched). (spec 036 §4.3)
        if (opts.RootFactory is null && opts.RootRenderFunc is null) return;

        var spec = opts.InitialWindowSpec ?? new WindowSpec
        {
            Title = opts.WindowTitle,
            Width = opts.WindowWidth,
            Height = opts.WindowHeight,
            Presenter = opts.FullScreen ? PresenterKind.FullScreen : PresenterKind.Overlapped,
        };

        ReactorApp.OpenWindowCore(spec, opts.RootFactory, opts.RootRenderFunc, opts.Configure);
    }

    /// <summary>
    /// Maps the WinUI <see cref="LaunchActivatedEventArgs"/> + the richer
    /// <c>Microsoft.Windows.AppLifecycle.AppInstance.GetActivatedEventArgs</c>
    /// onto Reactor's <see cref="Microsoft.UI.Reactor.LaunchActivation"/>
    /// shape. Best-effort — every WinRT failure logs to <c>Debug.WriteLine</c>
    /// and falls back to <see cref="Microsoft.UI.Reactor.LaunchActivation.Normal"/>
    /// so a malformed activation never breaks startup. (spec 036 §11.6)
    /// </summary>
    private static Microsoft.UI.Reactor.LaunchActivation ParseLaunchActivation(LaunchActivatedEventArgs args)
    {
        // SECURITY (spec 036 §0.5): never log Arguments / Files at default
        // verbosity — they may carry user paths or untrusted shell strings.
        // Trace-only logging happens in the Reactor.AppLogger sink controlled
        // by the host app, not here.

        try
        {
            // The AppLifecycle activation args are richer than the WinUI Xaml
            // ones — Protocol / File / Toast surface here. Available for both
            // packaged and unpackaged starting in WinAppSDK 1.0.
            global::Microsoft.Windows.AppLifecycle.AppActivationArguments? appArgs = null;
            try { appArgs = global::Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs(); }
            catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] AppInstance activation lookup failed: {ex.Message}"); }

            if (appArgs is not null)
            {
                switch (appArgs.Kind)
                {
                    case global::Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File:
                        {
                            var paths = new List<string>();
                            if (appArgs.Data is global::Windows.ApplicationModel.Activation.IFileActivatedEventArgs fa && fa.Files is not null)
                            {
                                foreach (var f in fa.Files)
                                {
                                    if (f is global::Windows.Storage.IStorageItem item && !string.IsNullOrEmpty(item.Path))
                                        paths.Add(item.Path);
                                }
                            }
                            return new Microsoft.UI.Reactor.LaunchActivation(LaunchKind.File, null, paths);
                        }
                    case global::Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol:
                        {
                            string? uri = null;
                            if (appArgs.Data is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs pa)
                                uri = pa.Uri?.ToString();
                            return new Microsoft.UI.Reactor.LaunchActivation(LaunchKind.Protocol, uri, Array.Empty<string>());
                        }
                    case global::Microsoft.Windows.AppLifecycle.ExtendedActivationKind.ToastNotification:
                        {
                            string? toastArg = null;
                            if (appArgs.Data is global::Windows.ApplicationModel.Activation.IToastNotificationActivatedEventArgs ta)
                                toastArg = ta.Argument;
                            return new Microsoft.UI.Reactor.LaunchActivation(LaunchKind.Toast, toastArg, Array.Empty<string>());
                        }
                }
            }

            // Default: a Launch-kind activation. WinUI's Microsoft.UI.Xaml
            // LaunchActivatedEventArgs exposes the argument string directly.
            string? launchArgs = null;
            try { launchArgs = args?.Arguments; } catch { }
            if (string.IsNullOrEmpty(launchArgs))
                launchArgs = TryReadCommandLineArguments();

            // Heuristic: a non-empty argument string from a regular Launch
            // arrives here only when the user activated a jump-list entry,
            // a tray "Open" command, or a thumbnail-toolbar button — all
            // re-launch the process with the same exe and the entry's args.
            // We can't tell those three apart from the WinUI surface alone,
            // so we tag them all as JumpList. Apps that need finer detail
            // can inspect the argument shape themselves.
            var kind = string.IsNullOrEmpty(launchArgs) ? LaunchKind.Normal : LaunchKind.JumpList;
            return new Microsoft.UI.Reactor.LaunchActivation(kind, launchArgs, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[Reactor] ParseLaunchActivation failed: {ex.GetType().Name}: {ex.Message}");
            return Microsoft.UI.Reactor.LaunchActivation.Normal;
        }
    }

    private static string? TryReadCommandLineArguments()
    {
        try
        {
            var cli = Environment.GetCommandLineArgs();
            // Skip args[0] (the exe path); join the rest. The convention is
            // a single deep-link URI per launch.
            if (cli.Length <= 1) return null;
            return cli.Length == 2 ? cli[1] : string.Join(' ', cli, 1, cli.Length - 1);
        }
        catch { return null; }
    }

    // IXamlMetadataProvider — delegate to the library's generated provider (which already
    // chains to XamlControlsXamlMetaDataProvider internally) and fall back to the core
    // provider for any schema-only types the generated one doesn't carry. Returning null
    // here is the WinUI convention for "unknown type" even though the WinRT interface
    // types it as non-nullable.
    public IXamlType GetXamlType(Type type)
    {
        var t = ReactorProvider.GetXamlType(type);
        if (t is not null) return t;
        t = HostAppProvider.GetXamlType(type);
        if (t is not null) return t;
        foreach (var p in ReactorApp.RegisteredControlAssemblyProviders)
        {
            t = p.GetXamlType(type);
            if (t is not null) return t;
        }
        return CoreProvider.GetXamlType(type)!;
    }

    public IXamlType GetXamlType(string fullName)
    {
        var t = ReactorProvider.GetXamlType(fullName);
        if (t is not null) return t;
        t = HostAppProvider.GetXamlType(fullName);
        if (t is not null) return t;
        foreach (var p in ReactorApp.RegisteredControlAssemblyProviders)
        {
            t = p.GetXamlType(fullName);
            if (t is not null) return t;
        }
        return CoreProvider.GetXamlType(fullName)!;
    }

    public XmlnsDefinition[] GetXmlnsDefinitions()
    {
        var reactor = ReactorProvider.GetXmlnsDefinitions();
        var host = HostAppProvider.GetXmlnsDefinitions();
        var registered = ReactorApp.RegisteredControlAssemblyProviders;
        var registeredCount = 0;
        var registeredDefs = new XmlnsDefinition[registered.Length][];
        for (var i = 0; i < registered.Length; i++)
        {
            registeredDefs[i] = registered[i].GetXmlnsDefinitions() ?? [];
            registeredCount += registeredDefs[i].Length;
        }
        if (host.Length == 0 && registeredCount == 0) return reactor;
        var combined = new XmlnsDefinition[reactor.Length + host.Length + registeredCount];
        var offset = 0;
        global::System.Array.Copy(reactor, 0, combined, offset, reactor.Length); offset += reactor.Length;
        global::System.Array.Copy(host, 0, combined, offset, host.Length); offset += host.Length;
        for (var i = 0; i < registeredDefs.Length; i++)
        {
            global::System.Array.Copy(registeredDefs[i], 0, combined, offset, registeredDefs[i].Length);
            offset += registeredDefs[i].Length;
        }
        return combined;
    }
}
