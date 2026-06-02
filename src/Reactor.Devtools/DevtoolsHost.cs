using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

internal sealed class DevtoolsHost : IReactorDevtoolsHost
{
    private const int DevtoolsReloadExitCode = 42;

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

        switch (options.Subverb)
        {
            case DevtoolsSubverb.List:
                return RunListSubverb(options);
            case DevtoolsSubverb.Run:
                ReactorApp.DevtoolsEnabled = true;
                return RunRunSubverb(options, request.Title, request.Width, request.Height, request.Configure, request.HostRoot, request.HostRootFactory);
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
    private static bool RunRunSubverb(DevtoolsCliOptions options, string title, double width, double height, Action<ReactorHost>? configure, Type? hostRoot = null, Func<Component>? hostRootFactory = null)
    {
        _ = title;

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
