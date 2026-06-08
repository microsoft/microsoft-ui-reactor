using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

internal sealed class DevtoolsHost : IReactorDevtoolsHost
{
    private const int DevtoolsReloadExitCode = 42;

    private readonly int _embedGeneration = 1;
    private readonly object _embedResizeLock = new();
    private (int W, int H) _latestEmbedResize;
    private int _embedResizePending;

    internal int EmbedGenerationForTests => _embedGeneration;

    public Element? BuildDevtoolsMenu(
        Func<IEnumerable<MenuFlyoutItemBase>>? items,
        string glyph,
        string toolTip,
        string? automationId) =>
        DevtoolsMenuFactory.Build(items, glyph, toolTip, automationId);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Optional devtools package implementation; invoked only through the devtools host gate.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Optional devtools package implementation; invoked only through the devtools host gate.")]
    public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
    {
        if (TryReportPackageMismatch())
            return true;

        var options = request.Options;

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

        var embedValidationError = GetOptionalStringProperty(options, "EmbedValidationError");
        if (!string.IsNullOrEmpty(embedValidationError))
        {
            Console.Error.WriteLine($"[reactor] {embedValidationError}");
            return true;
        }

        switch (options.Subverb)
        {
            case DevtoolsSubverb.List:
                return RunListSubverb(options);
            case DevtoolsSubverb.Run:
                ReactorApp.DevtoolsEnabled = true;
                return RunRunSubverb(options, request.Title, request.Width, request.Height, request.FullScreen, request.Configure, request.HostRoot, request.HostRootFactory);
            case DevtoolsSubverb.Screenshot:
                return RunScreenshotSubverb(options, request.Width, request.Height, request.Configure, request.HostRoot);
            case DevtoolsSubverb.Tree:
                Console.Error.WriteLine($"[devtools] '--devtools tree' (headless) is not implemented yet.");
                return true;
            case DevtoolsSubverb.App:
                ReactorApp.DevtoolsEnabled = true;
                return false;
            default:
                return false;
        }
    }

    private static bool TryReportPackageMismatch()
    {
        var coreVersion = GetInformationalVersion(typeof(ReactorApp).Assembly);
        var devtoolsVersion = GetInformationalVersion(typeof(DevtoolsHost).Assembly);
        if (string.Equals(coreVersion, devtoolsVersion, StringComparison.Ordinal))
        {
            return false;
        }

        Console.Error.WriteLine("[reactor] Microsoft.UI.Reactor and Microsoft.UI.Reactor.Devtools package payloads do not match.");
        Console.Error.WriteLine($"[reactor]   Microsoft.UI.Reactor:          {coreVersion}");
        Console.Error.WriteLine($"[reactor]   Microsoft.UI.Reactor.Devtools: {devtoolsVersion}");
        Console.Error.WriteLine("[reactor] This usually means NuGet restored stale 0.0.0-local package contents. Run `mur pack-local`, delete bin/obj for this app, then restore/build again.");
        return true;
    }

    private static string GetInformationalVersion(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "<unknown>";
    }

    private static string? GetOptionalStringProperty<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T instance, string propertyName)
    {
        return typeof(T).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance) as string;
    }

    [RequiresUnreferencedCode("Devtools component discovery uses Assembly.GetTypes().")]
    private static bool RunScreenshotSubverb(DevtoolsCliOptions options, double width, double height, Action<ReactorHost>? configure, Type? hostRoot = null)
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

        ReactorApp.RunOnSta(() =>
        {
            ReactorApp.InitProcess();

            ReactorApp.Options = new ReactorAppOptions(
                RootFactory: () => (Component)Activator.CreateInstance(type)!,
                Configure: host =>
                {
                    configure?.Invoke(host);
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
    private bool RunRunSubverb(DevtoolsCliOptions options, string title, double width, double height, bool fullScreen, Action<ReactorHost>? configure, Type? hostRoot = null, Func<Component>? hostRootFactory = null)
    {
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
        else if (hostRoot != null && typeof(Component).IsAssignableFrom(hostRoot) && !hostRoot.IsAbstract)
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

        ReactorApp.RunOnSta(() =>
        {
            ReactorApp.InitProcess();

            Action<ReactorHost> combinedConfigure = host =>
            {
                configure?.Invoke(host);

                bool SwitchComponentCore(string name)
                {
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
                        var instance = (Component)Activator.CreateInstance(type)!;
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

                    if (options.EmbedRequested)
                    {
                        server.EmbedMode = true;
                        server.Generation = _embedGeneration;
                        server.GetHwnd = () => GetHostHwnd(host);
                        server.AckEmbed = (parent, w, h, generation) => ApplyEmbedAck(options, host, parent, w, h, generation);
                        server.ResizeEmbed = (w, h) => ApplyEmbedResize(host, w, h);
                        server.MoveEmbed = options.EmbedStyle == WindowEmbedStyle.Owner
                            ? (x, y) => ApplyEmbedMove(host, x, y)
                            : null;
                        server.ReleaseEmbed = () => ApplyEmbedRelease(options, host);

                        if (options.EmbedAutoEnabledVsCode)
                            Console.Error.WriteLine("[reactor] --embed implies --vscode; enabling VsCode mode");
                    }

                    server.Start();
                    host.Window.Closed += (_, _) => server.Dispose();
                }

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
                EventHandler<ReactorWindow> onOpened = (_, w) =>
                {
                    bool isMain = ReferenceEquals(w, ReactorApp.PrimaryWindow);
                    windows.Attach(w, isMain: isMain, stableId: isMain ? "main" : null);
                };
                EventHandler<ReactorWindow> onClosed = (_, w) => windows.Detach(w);
                ReactorApp.WindowOpened += onOpened;
                ReactorApp.WindowClosed += onClosed;
                host.Window.Closed += (_, _) =>
                {
                    ReactorApp.WindowOpened -= onOpened;
                    ReactorApp.WindowClosed -= onClosed;
                };

                string? OpenWindowByAllowlistedComponentCore(WindowSpec spec, string componentName)
                {
                    var type = FindComponentType(componentName);
                    if (type is null) return null;

                    var opened = ReactorApp.OpenWindow(spec, () => (Component)Activator.CreateInstance(type)!);
                    return opened.Id;
                }

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
                    OpenWindowByAllowlistedComponent = OpenWindowByAllowlistedComponentCore,
                });
                DevtoolsUiaTools.RegisterUiaTools(mcp, nodes, windows);
                DevtoolsFireTool.Register(mcp, () => host.RootComponent);
                DevtoolsStateTool.Register(mcp, () => host.RootComponent);
                DevtoolsLogsTool.Register(mcp, () => LogCaptureInstall.Shared);
                DevtoolsDockingTools.Register(mcp);

                bool announced = false;
                _ = Task.Run(() =>
                {
                    try
                    {
                        mcp.Start();
                        host.Window.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (announced) return;
                            announced = true;
                            mcp.AnnounceReady();
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[devtools:mcp] Start failed: {ex}");
                    }
                });
                host.Window.Closed += (_, _) => mcp.Dispose();
            };

            ReactorApp.Options = new ReactorAppOptions(
                RootFactory: hostRootFactory ?? (() => (Component)Activator.CreateInstance(initialComponentType)!),
                Configure: combinedConfigure,
                WindowTitle: $"Preview — {initialComponentName}",
                WindowWidth: width,
                WindowHeight: height,
                FullScreen: fullScreen,
                InitialWindowSpec: options.EmbedRequested
                    ? BuildEmbedWindowSpec(options, $"Preview — {initialComponentName}", width, height)
                    : null);

            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new ReactorApplication();
            });
        });

        return true;
    }


    internal static WindowSpec BuildEmbedWindowSpec(DevtoolsCliOptions options, string baseTitle, double width, double height)
    {
        if (!options.EmbedRequested || options.EmbedHostPid is not { } hostPid)
            throw new ArgumentException("Embed options must include --embed and --embed-host-pid.", nameof(options));

        return new WindowSpec
        {
            Title = baseTitle,
            Width = width,
            Height = height,
            Presenter = PresenterKind.Overlapped,
            Embed = new EmbedRequest(options.EmbedStyle, hostPid, InitialVisibility: options.EmbedStyle == WindowEmbedStyle.Child),
            PersistPlacement = false,
        };
    }

    private static nint GetHostHwnd(ReactorHost host)
        => host.OwningWindow?.Hwnd ?? WinRT.Interop.WindowNative.GetWindowHandle(host.Window);

    internal static bool IsDpiCompatible(IntPtr parent, IntPtr child)
    {
        var parentContext = EmbedNative.GetWindowDpiAwarenessContext(parent);
        var childContext = EmbedNative.GetWindowDpiAwarenessContext(child);
        return parentContext != 0
            && childContext != 0
            && EmbedNative.AreDpiAwarenessContextsEqual(parentContext, childContext);
    }

    private EmbedAckResult ApplyEmbedAck(DevtoolsCliOptions options, ReactorHost host, IntPtr parent, int w, int h, int generation)
    {
        if (generation != _embedGeneration)
            return EmbedAckResult.GenerationMismatch;

        var hwnd = GetHostHwnd(host);
        if (!IsDpiCompatible(parent, hwnd))
            return EmbedAckResult.DpiMismatch;

        var completed = new ManualResetEventSlim(false);
        var result = EmbedAckResult.NotReady;
        var enqueued = host.Window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (options.EmbedStyle == WindowEmbedStyle.Owner)
                {
                    EmbedNative.SetWindowLongPtr(hwnd, EmbedNative.GWLP_HWNDPARENT, parent);
                }
                else
                {
                    EmbedNative.SetParent(hwnd, parent);
                    EmbedNative.SetWindowStyleForChildEmbed(hwnd);
                    if (EmbedNative.GetParent(hwnd) != parent)
                    {
                        Console.Error.WriteLine("[reactor] embed attach failed: SetParent did not attach the child window to the VS placeholder.");
                        result = EmbedAckResult.Rejected;
                        return;
                    }
                }

                if (!EmbedNative.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, w, h,
                    EmbedNative.SWP_NOZORDER | EmbedNative.SWP_NOACTIVATE | EmbedNative.SWP_SHOWWINDOW))
                {
                    Console.Error.WriteLine("[reactor] embed attach failed: SetWindowPos before activation failed.");
                    result = EmbedAckResult.Rejected;
                    return;
                }

                host.Window.Activate();

                if (!EmbedNative.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, w, h,
                    EmbedNative.SWP_NOZORDER | EmbedNative.SWP_NOACTIVATE | EmbedNative.SWP_SHOWWINDOW))
                {
                    Console.Error.WriteLine("[reactor] embed attach failed: SetWindowPos after activation failed.");
                    result = EmbedAckResult.Rejected;
                    return;
                }

                EmbedNative.ShowWindow(hwnd, EmbedNative.SW_SHOW);
                if (options.EmbedStyle == WindowEmbedStyle.Child && EmbedNative.GetParent(hwnd) != parent)
                {
                    Console.Error.WriteLine("[reactor] embed attach failed: child window parent changed during activation.");
                    result = EmbedAckResult.Rejected;
                    return;
                }

                EmbedNative.SetFocus(hwnd);
                result = EmbedAckResult.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[reactor] embed attach failed: {ex.GetType().Name}: {ex.Message}");
                result = EmbedAckResult.Rejected;
            }
            finally
            {
                completed.Set();
            }
        });

        if (!enqueued)
        {
            Console.Error.WriteLine("[reactor] embed attach failed: dispatcher queue rejected the attach operation.");
            return EmbedAckResult.NotReady;
        }

        if (!completed.Wait(TimeSpan.FromSeconds(2)))
        {
            Console.Error.WriteLine("[reactor] embed attach failed: timed out waiting for the UI thread to attach the child window.");
            return EmbedAckResult.NotReady;
        }

        return result;
    }

    private void ApplyEmbedResize(ReactorHost host, int w, int h)
    {
        lock (_embedResizeLock) _latestEmbedResize = (w, h);
        if (Interlocked.Exchange(ref _embedResizePending, 1) == 1) return;

        host.Window.DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _embedResizePending, 0);
            (int W, int H) size;
            lock (_embedResizeLock) size = _latestEmbedResize;
            EmbedNative.SetWindowPos(GetHostHwnd(host), IntPtr.Zero, 0, 0, size.W, size.H,
                EmbedNative.SWP_NOZORDER | EmbedNative.SWP_NOMOVE | EmbedNative.SWP_NOACTIVATE);
        });
    }

    private static void ApplyEmbedMove(ReactorHost host, int x, int y)
    {
        host.Window.DispatcherQueue.TryEnqueue(() =>
        {
            EmbedNative.SetWindowPos(GetHostHwnd(host), IntPtr.Zero, x, y, 0, 0,
                EmbedNative.SWP_NOZORDER | EmbedNative.SWP_NOSIZE | EmbedNative.SWP_NOACTIVATE);
        });
    }

    private static void ApplyEmbedRelease(DevtoolsCliOptions options, ReactorHost host)
    {
        void ExitNow(object? sender, EventArgs args) => Environment.Exit(0);
        if (host.OwningWindow is { } owning)
            owning.Closed += ExitNow;
        else
            host.Window.Closed += (_, _) => Environment.Exit(0);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            Environment.Exit(0);
        });

        host.Window.DispatcherQueue.TryEnqueue(() =>
        {
            var hwnd = GetHostHwnd(host);
            if (options.EmbedStyle == WindowEmbedStyle.Owner)
                EmbedNative.SetWindowLongPtr(hwnd, EmbedNative.GWLP_HWNDPARENT, IntPtr.Zero);
            else
                EmbedNative.SetParent(hwnd, IntPtr.Zero);
            if (host.OwningWindow is { } owningWindow)
                owningWindow.Close();
            else
                host.Window.Close();
        });
    }

    [RequiresUnreferencedCode("Devtools component discovery uses Assembly.GetTypes().")]
    private static Type? FindComponentType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (global::System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            var match = types.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) &&
                typeof(Component).IsAssignableFrom(t) &&
                !t.IsAbstract);
            if (match != null) return match;
        }
        return null;
    }

    [RequiresUnreferencedCode("Assembly.GetTypes() is incompatible with trimming. Devtools-only code path.")]
    private static IEnumerable<string> FindAllComponentNames()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch (global::System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; } catch { return []; } })
            .Where(t => typeof(Component).IsAssignableFrom(t!) && !t!.IsAbstract && !t.FullName!.StartsWith("Microsoft.UI.Reactor."))
            .Select(t => t!.Name)
            .Distinct()
            .OrderBy(n => n);
    }

    [RequiresUnreferencedCode("Assembly.GetTypes() is incompatible with trimming. Devtools-only code path.")]
    private static IEnumerable<ComponentInfo> FindAllComponentsDetailed()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch (global::System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; } catch { return []; } })
            .Where(t => typeof(Component).IsAssignableFrom(t!) && !t!.IsAbstract && !t.FullName!.StartsWith("Microsoft.UI.Reactor."))
            .Select(t => new ComponentInfo(
                Name: t!.Name,
                FullName: t.FullName ?? t.Name,
                IsNested: t.IsNested,
                IsPublic: t.IsPublic || t.IsNestedPublic,
                Namespace: t.Namespace))
            .GroupBy(c => c.Name)
            .Select(g => g.First());
    }

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

    private static void RequestDevtoolsReload(DevtoolsMcpServer mcp, ReactorHost host)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            try { mcp.Dispose(); } catch { }
            host.Window.DispatcherQueue.TryEnqueue(() =>
            {
                try { host.Window.Close(); } catch { }
                Environment.Exit(DevtoolsReloadExitCode);
            });
        });
    }

    private static void RequestDevtoolsShutdown(DevtoolsMcpServer mcp, ReactorHost host)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            try { mcp.Dispose(); } catch { }
            host.Window.DispatcherQueue.TryEnqueue(() =>
            {
                try { host.Window.Close(); } catch { }
                Environment.Exit(0);
            });
        });
    }
}
