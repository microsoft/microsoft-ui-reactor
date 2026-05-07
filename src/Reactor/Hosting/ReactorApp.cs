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
    int WindowWidth = 1024,
    int WindowHeight = 768,
    bool FullScreen = false);

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
    public static ReactorHost? ActiveHost
    {
        get => Volatile.Read(ref _activeHost);
        internal set => Volatile.Write(ref _activeHost, value);
    }

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

    private static int _previewParamDeprecationWarned;

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
    /// library). Call before <see cref="Run{TRoot}(string, int, int, bool, bool, bool, Action{ReactorHost}?)"/>.
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
    // subverb (--devtools app / --devtools run) AND the developer passed
    // devtools: true to Run. Frozen after startup; read by UseDevtools() and
    // by the DevtoolsMenu component to decide whether to render themselves.
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
    /// Launches the app. Set <c>devtools: true</c> in DEBUG builds to enable the
    /// <c>mur devtools</c> / <c>--devtools</c> surface: component switching via VS Code,
    /// MCP agent tools (Phase 2+), and component listing.
    /// </summary>
    /// <remarks>
    /// The <c>preview</c> parameter is deprecated and is kept for one release. When both are
    /// passed, <c>devtools</c> wins.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Devtools uses Assembly.GetTypes(); non-devtools code paths are trim-safe.")]
    public static void Run<TRoot>(
        string title = "Reactor App",
        int width = 1024,
        int height = 768,
        bool fullScreen = false,
        bool devtools = false,
        // DEPRECATED: use 'devtools:'. Kept for one release. The runtime emits a
        // one-shot stderr warning when this is set without 'devtools:'.
        bool preview = false,
        Action<ReactorHost>? configure = null)
        where TRoot : Component, new()
    {
        var effectiveDevtools = ResolveDevtoolsParam(devtools, preview);
        if (effectiveDevtools && TryRunDevtools(title, width, height, configure, hostRoot: typeof(TRoot))) return;

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
    /// See the generic overload for <c>devtools</c> semantics.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Devtools uses Assembly.GetTypes(); non-devtools code paths are trim-safe.")]
    public static void Run(
        string title,
        Func<RenderContext, Element> rootRender,
        int width = 1024,
        int height = 768,
        bool fullScreen = false,
        bool devtools = false,
        // DEPRECATED: use 'devtools:'. Kept for one release. The runtime emits a
        // one-shot stderr warning when this is set without 'devtools:'.
        bool preview = false,
        Action<ReactorHost>? configure = null)
    {
        var effectiveDevtools = ResolveDevtoolsParam(devtools, preview);
        if (effectiveDevtools && TryRunDevtools(title, width, height, configure)) return;

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
    /// Reconciles the deprecated <c>preview:</c> parameter with the new <c>devtools:</c>.
    /// If only <c>preview</c> is set, emit a one-time deprecation warning to stderr.
    /// </summary>
    internal static bool ResolveDevtoolsParam(bool devtools, bool preview)
    {
        if (preview && !devtools && Interlocked.Exchange(ref _previewParamDeprecationWarned, 1) == 0)
        {
            Console.Error.WriteLine("[reactor] 'preview:' is deprecated; use 'devtools:'.");
        }
        return devtools || preview;
    }

    /// <summary>
    /// Checks the process command-line for <c>--devtools</c> or the deprecated <c>--preview</c>.
    /// If a devtools subverb is selected, launches the corresponding flow (list, run, etc.).
    /// With <c>--vscode</c>, starts the capture server for the VS Code preview panel. Only
    /// active when the caller passes <c>devtools: true</c>.
    /// </summary>
    [RequiresUnreferencedCode("Devtools uses Assembly.GetTypes() for component discovery.")]
    private static bool TryRunDevtools(string title, int width, int height, Action<ReactorHost>? configure, Type? hostRoot = null)
    {
        var args = Environment.GetCommandLineArgs();
        var options = DevtoolsCliParser.Parse(args);

        if (options.PreviewAndDevtoolsConflict)
        {
            Console.Error.WriteLine("[devtools] Error: pass either --devtools or --preview, not both.");
            return true;
        }

        if (options.Subverb is null) return false;

        // Install log capture as the very first side-effect after we know
        // devtools is active. Runs before component reflection, before any
        // Application.Start, so startup Debug/Trace/Console output is caught
        // even when the agent attaches late. Skipped when `--devtools-logs off`
        // is set. In stdio transport we must NOT forward Console.Out (that's
        // the JSON-RPC frame) — writes still land in the buffer, just not
        // passed through to the parent process.
        if (options.Subverb == DevtoolsSubverb.Run && !options.LogsDisabled)
        {
            var capBytes = options.LogsCapacityMb is { } mb
                ? (long)mb * 1024 * 1024
                : LogCaptureBuffer.DefaultCapacityBytes;
            var forwardOut = options.Transport != McpTransport.Stdio;
            LogCaptureInstall.Install(capBytes, forwardConsole: forwardOut);
        }

        if (options.UsedDeprecatedPreview)
            Console.Error.WriteLine("[reactor] '--preview' is deprecated; use '--devtools run'.");

        switch (options.Subverb)
        {
            case DevtoolsSubverb.List:
                return RunListSubverb(options);
            case DevtoolsSubverb.Run:
                DevtoolsEnabled = true;
                return RunRunSubverb(options, title, width, height, configure, hostRoot);
            case DevtoolsSubverb.Screenshot:
                return RunScreenshotSubverb(options, width, height, configure, hostRoot);
            case DevtoolsSubverb.Tree:
                Console.Error.WriteLine($"[devtools] '--devtools tree' (headless) is not implemented yet.");
                return true;
            case DevtoolsSubverb.App:
                // Pass-through mode: enable the in-app dev UI flag and let the
                // caller's normal run loop proceed (returning false skips the
                // short-circuit in Run<TRoot>).
                DevtoolsEnabled = true;
                return false;
            default:
                return false;
        }
    }

    [RequiresUnreferencedCode("Devtools component discovery uses Assembly.GetTypes().")]
    private static bool RunScreenshotSubverb(DevtoolsCliOptions options, int width, int height, Action<ReactorHost>? configure, Type? hostRoot = null)
    {
        if (string.IsNullOrEmpty(options.ScreenshotOutputPath))
        {
            Console.Error.WriteLine("[devtools] '--devtools screenshot' requires --out <path.png>.");
            return true;
        }

        var componentName = options.ComponentName ?? hostRoot?.Name ?? FindAllComponentNames().FirstOrDefault();
        if (componentName == null)
        {
            Console.Error.WriteLine("[devtools] No Component subclasses found.");
            return true;
        }
        var type = FindComponentType(componentName);
        if (type == null)
        {
            Console.Error.WriteLine($"[devtools] Component '{componentName}' not found.");
            return true;
        }

        string outPath = options.ScreenshotOutputPath!;

        RunOnSta(() =>
        {
            InitProcess();

            Options = new ReactorAppOptions(
                RootFactory: () => (Core.Component)Activator.CreateInstance(type)!,
                Configure: host =>
                {
                    configure?.Invoke(host);
                    // Capture once after first render, then exit. UpdateLayout flushes
                    // pending measure/arrange so the first frame is stable.
                    host.Window.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            if (host.Window.Content is FrameworkElement fe) fe.UpdateLayout();
                            var capture = ScreenshotCapture.CaptureWindow(host.Window, includeChrome: false);
                            File.WriteAllBytes(outPath, capture.Png);
                            Console.WriteLine($"[devtools] Wrote {capture.Width}x{capture.Height} PNG to {outPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[devtools] Screenshot failed: {ex.Message}");
                        }
                        finally
                        {
                            Environment.Exit(0);
                        }
                    });
                },
                WindowTitle: $"Screenshot — {componentName}",
                WindowWidth: width,
                WindowHeight: height);

            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new ReactorApplication();
            });
        });

        return true;
    }

    [RequiresUnreferencedCode("Devtools component listing uses Assembly.GetTypes().")]
    private static bool RunListSubverb(DevtoolsCliOptions options)
    {
        var names = FindAllComponentNames().ToList();
        foreach (var name in names)
            Console.WriteLine(name);
        Console.Out.Flush();
        if (!string.IsNullOrEmpty(options.ListOutputPath))
            File.WriteAllLines(options.ListOutputPath, names);
        return true;
    }

    [RequiresUnreferencedCode("Devtools component discovery uses Assembly.GetTypes() and Activator.CreateInstance.")]
    private static bool RunRunSubverb(DevtoolsCliOptions options, string title, int width, int height, Action<ReactorHost>? configure, Type? hostRoot = null)
    {
        _ = title;

        // Resolve the initial component type. Precedence:
        //   1. Explicit --component on the command line — the user asked.
        //   2. The TRoot type that the host passed to Run<TRoot> — matches their
        //      intent and avoids "first-alphabetical" surprises where a nested
        //      helper component wins over the real app root.
        //   3. Fallback to the first component the reflection scan finds.
        string? componentName = options.ComponentName;
        Type? componentType = null;
        if (componentName != null)
        {
            componentType = FindComponentType(componentName);
            if (componentType == null)
            {
                Console.Error.WriteLine($"[devtools] Component '{componentName}' not found.");
                Console.Error.WriteLine($"[devtools] Available components: {string.Join(", ", FindAllComponentNames())}");
                return true;
            }
        }
        else if (hostRoot != null && typeof(Core.Component).IsAssignableFrom(hostRoot) && !hostRoot.IsAbstract)
        {
            componentType = hostRoot;
            componentName = hostRoot.Name;
        }
        else
        {
            var firstName = FindAllComponentNames().FirstOrDefault();
            if (firstName == null)
            {
                Console.Error.WriteLine("[devtools] No Component subclasses found.");
                return true;
            }
            componentType = FindComponentType(firstName)!;
            componentName = firstName;
            Console.Error.WriteLine(
                $"[devtools] No --component passed and Run<T> not detected; defaulting to '{firstName}' (alphabetical). " +
                $"Pass --component to pick another.");
        }

        bool vscodeMode = options.VsCodeMode;
        int captureFps = options.Fps;

        Console.WriteLine($"[devtools] Previewing {componentType.FullName}");
        Console.WriteLine($"[devtools] Hot reload active — edit and save to see changes instantly");
        if (vscodeMode) Console.WriteLine($"[devtools] VS Code mode enabled (capture @ {captureFps} fps)");

        var initialComponentType = componentType;
        var initialComponentName = componentName;

        RunOnSta(() =>
        {
            InitProcess();

            Action<ReactorHost> combinedConfigure = host =>
            {
                configure?.Invoke(host);

                // Shared switch-component callback — reused by both the VS Code
                // capture server and the MCP devtools server so they agree on
                // the active component.
                bool SwitchComponentCore(string name)
                {
                    // SECURITY (TASK-021): only allow switching to a type
                    // already present in the announced component list. Without
                    // this, the loopback /preview endpoint becomes a primitive
                    // for activating arbitrary Component subclasses (including
                    // ones the dev never intended to expose).
                    var allowed = FindAllComponentNames();
                    bool ok = false;
                    foreach (var n in allowed)
                    {
                        if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    }
                    if (!ok) return false;

                    var type = FindComponentType(name);
                    if (type == null) return false;

                    host.Window.DispatcherQueue.TryEnqueue(() =>
                    {
                        var instance = (Core.Component)Activator.CreateInstance(type)!;
                        host.Mount(instance);
                        host.Window.Title = $"Preview — {name}";
                    });

                    initialComponentName = name;
                    Console.WriteLine($"[devtools] Switched to {type.FullName}");
                    return true;
                }

                if (vscodeMode)
                {
                    var server = new PreviewCaptureServer(
                        host.Window.DispatcherQueue,
                        host.Window,
                        captureFps);

                    server.GetComponents = () => FindAllComponentNames().ToList();
                    server.GetCurrentComponent = () => initialComponentName;
                    server.SwitchComponent = SwitchComponentCore;

                    server.Start();
                    host.Window.Closed += (_, _) => server.Dispose();
                }

                // MCP devtools server — always on when --devtools run is active.
                // Port pinned by --mcp-port for the supervisor reload loop.
                // Log level pinned by --devtools-log-level (default: call).
                var logger = new DevtoolsLogger(
                    DevtoolsLogger.DefaultDirectory(),
                    global::System.Diagnostics.Process.GetCurrentProcess().Id,
                    DevtoolsLogger.ParseLevel(options.LogLevel));
                var projectId = options.ProjectIdentifier ?? DeriveProjectIdentifier();
                if (projectId is not null && DevtoolsMcpServer.IsAnotherSessionActive(projectId, out var existing))
                {
                    Console.Error.WriteLine(
                        $"[devtools] another session for this project is active at {existing!.Endpoint} (pid {existing.Pid}); stop it first");
                    Environment.Exit(3);
                    return;
                }

                var mcp = new DevtoolsMcpServer(
                    host.Window.DispatcherQueue,
                    host.Window,
                    preferredPort: options.McpPort,
                    logger: logger,
                    transport: options.Transport,
                    projectIdentifier: projectId);

                var windows = new WindowRegistry(mcp.BuildTag);
                var nodes = new NodeRegistry();
                // Pin the primary devtools window to "main" so the handle
                // doesn't drift when switchComponent updates the title.
                windows.Attach(host.Window, isMain: true, stableId: "main");

                DevtoolsTools.RegisterCore(mcp, new DevtoolsTools.ToolHostContext
                {
                    GetComponents = () => FindAllComponentNames().ToList(),
                    GetComponentsDetailed = () => FindAllComponentsDetailed().ToList(),
                    GetCurrentComponent = () => initialComponentName,
                    SwitchComponent = SwitchComponentCore,
                    RequestReload = () => RequestDevtoolsReload(mcp, host),
                    RequestShutdown = () => RequestDevtoolsShutdown(mcp, host),
                    Windows = windows,
                    Nodes = nodes,
                });
                DevtoolsUiaTools.RegisterUiaTools(mcp, nodes, windows);
                DevtoolsFireTool.Register(mcp, () => host.RootComponent);
                DevtoolsStateTool.Register(mcp, () => host.RootComponent);
                DevtoolsLogsTool.Register(mcp, () => LogCaptureInstall.Shared);

                mcp.Start();
                // Ready line fires after the first render — subscribe once to the host.
                bool announced = false;
                host.Window.DispatcherQueue.TryEnqueue(() =>
                {
                    if (announced) return;
                    announced = true;
                    mcp.AnnounceReady();
                });
                host.Window.Closed += (_, _) => mcp.Dispose();
            };

            Options = new ReactorAppOptions(
                RootFactory: () => (Core.Component)Activator.CreateInstance(initialComponentType)!,
                Configure: combinedConfigure,
                WindowTitle: $"Preview — {initialComponentName}",
                WindowWidth: width,
                WindowHeight: height);

            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new ReactorApplication();
            });
        });

        return true;
    }

    /// <summary>
    /// Finds a Component type by name across all loaded assemblies (case-insensitive).
    /// </summary>
    [RequiresUnreferencedCode("Devtools component discovery uses Assembly.GetTypes().")]
    internal static Type? FindComponentType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (global::System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            var match = types.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) &&
                typeof(Core.Component).IsAssignableFrom(t) &&
                !t.IsAbstract);
            if (match != null) return match;
        }
        return null;
    }

    [RequiresUnreferencedCode("Assembly.GetTypes() is incompatible with trimming. Devtools-only code path.")]
    internal static IEnumerable<string> FindAllComponentNames()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch (global::System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; } catch { return []; } })
            .Where(t => typeof(Core.Component).IsAssignableFrom(t!) && !t!.IsAbstract && !t.FullName!.StartsWith("Microsoft.UI.Reactor."))
            .Select(t => t!.Name)
            .Distinct()
            .OrderBy(n => n);
    }

    [RequiresUnreferencedCode("Assembly.GetTypes() is incompatible with trimming. Devtools-only code path.")]
    internal static IEnumerable<Hosting.Devtools.ComponentInfo> FindAllComponentsDetailed()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch (global::System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; } catch { return []; } })
            .Where(t => typeof(Core.Component).IsAssignableFrom(t!) && !t!.IsAbstract && !t.FullName!.StartsWith("Microsoft.UI.Reactor."))
            .Select(t => new Hosting.Devtools.ComponentInfo(
                Name: t!.Name,
                FullName: t.FullName ?? t.Name,
                IsNested: t.IsNested,
                IsPublic: t.IsPublic || t.IsNestedPublic,
                Namespace: t.Namespace))
            .GroupBy(c => c.Name)
            .Select(g => g.First());
    }

    /// <summary>
    /// Identifier used to hash this session's lockfile path when the supervisor
    /// didn't pass <c>--devtools-project</c>. Falls back to the entry assembly
    /// location — stable per build output, sufficient for single-instance.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3000", Justification = "Assembly.Location used for diagnostic project identifier.")]
    private static string? DeriveProjectIdentifier()
    {
        try
        {
            var asm = global::System.Reflection.Assembly.GetEntryAssembly();
            var loc = asm?.Location;
            if (!string.IsNullOrEmpty(loc)) return loc;
        }
        catch { }
        return null;
    }

    internal static void ResetDeprecationWarningForTests()
    {
        Interlocked.Exchange(ref _previewParamDeprecationWarned, 0);
    }

    internal static void ResetDevtoolsEnabledForTests()
    {
        Interlocked.Exchange(ref _devtoolsEnabled, 0);
    }

    /// <summary>
    /// Sentinel exit code consumed by the `mur devtools` supervisor to mean
    /// "rebuild and respawn". Any other exit code propagates.
    /// </summary>
    internal const int DevtoolsReloadExitCode = 42;

    private static void RequestDevtoolsReload(DevtoolsMcpServer mcp, ReactorHost host)
    {
        // Response flush happens before shutdown — the tool returns first, then the
        // UI thread disposes the listener and closes the window. Exit 42 tells the
        // supervisor to rebuild and relaunch with the same pinned MCP port.
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // Let the HTTP response flush.
            try { mcp.Dispose(); } catch { }
            host.Window.DispatcherQueue.TryEnqueue(() =>
            {
                try { host.Window.Close(); } catch { }
                Environment.Exit(DevtoolsReloadExitCode);
            });
        });
    }

    /// <summary>
    /// Same shape as the reload path, but exits with code 0 so the `mur devtools`
    /// supervisor returns cleanly without rebuilding.
    /// </summary>
    private static void RequestDevtoolsShutdown(DevtoolsMcpServer mcp, ReactorHost host)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // Let the HTTP response flush.
            try { mcp.Dispose(); } catch { }
            host.Window.DispatcherQueue.TryEnqueue(() =>
            {
                try { host.Window.Close(); } catch { }
                Environment.Exit(0);
            });
        });
    }

    private static void InitProcess()
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
    private static void RunOnSta(Action action)
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

        var opts = ReactorApp.Options;
        var window = new Window { Title = opts.WindowTitle };
        if (opts.FullScreen)
            window.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        else
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(opts.WindowWidth, opts.WindowHeight));

        var host = new ReactorHost(window);

        opts.Configure?.Invoke(host);

        if (opts.RootFactory is not null)
        {
            host.Mount(opts.RootFactory());
        }
        else if (opts.RootRenderFunc is not null)
        {
            host.Mount(opts.RootRenderFunc);
        }

        window.Activate();
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
