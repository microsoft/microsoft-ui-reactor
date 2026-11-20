using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

internal sealed class DevtoolsHost : IReactorDevtoolsHost
{
    private readonly int _embedGeneration = 1;
    private readonly Lock _embedResizeLock = new();
    private (int W, int H) _latestEmbedResize;
    private int _embedResizePending;

    internal int EmbedGenerationForTests => _embedGeneration;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Optional devtools package implementation; invoked only through the devtools host gate.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Optional devtools package implementation; invoked only through the devtools host gate.")]
    public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
    {
        if (TryReportPackageMismatch())
            return true;

        var options = request.Options;

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
                return RunRunSubverb(options, request.Title, request.Width, request.Height, request.FullScreen, request.Configure, request.HostRoot, request.HostRootFactory, request.RootMountSite);
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
    private bool RunRunSubverb(DevtoolsCliOptions options, string title, double? width, double? height, bool fullScreen, Action<ReactorHost>? configure, Type? hostRoot = null, Func<Component>? hostRootFactory = null, SourceLocation? rootMountSite = null)
    {
        string? componentName = options.ComponentName;
        bool componentNamed = componentName != null;
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
            };

            ReactorApp.Options = new ReactorAppOptions(
                RootFactory: PreviewRootFactory(hostRoot, hostRootFactory, componentNamed, initialComponentType),
                Configure: combinedConfigure,
                WindowTitle: $"Preview — {initialComponentName}",
                WindowWidth: width,
                WindowHeight: height,
                FullScreen: fullScreen,
                RootMountSite: PreviewsAppRoot(hostRoot, hostRootFactory, componentNamed, initialComponentType) ? rootMountSite : null,
                InitialWindowSpec: options.EmbedRequested
                    ? BuildEmbedWindowSpec(options, $"Preview — {initialComponentName}", width, height)
                    : BuildPositionedWindowSpec(options, $"Preview — {initialComponentName}", width, height, fullScreen));

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
    /// Whether the preview's first root is the app's own <c>Run</c> root: the component type
    /// <c>Run</c> was given, or (when no component was named) the root its factory builds. A
    /// named component that isn't that type is a different root, which the <c>Run</c> call never
    /// mounted, so it gets neither the factory nor <c>Run</c>'s mount site.
    /// </summary>
    internal static bool PreviewsAppRoot(Type? hostRoot, Func<Component>? hostRootFactory, bool componentNamed, Type previewedType)
        => previewedType == hostRoot || (hostRootFactory is not null && !componentNamed);

    /// <summary>
    /// The root the preview mounts first. <c>Run&lt;T&gt;</c> always passes its own factory, so
    /// using it unconditionally would mount <c>T</c> for <c>--preview &lt;Name&gt;</c> while the
    /// window title said <c>&lt;Name&gt;</c>.
    /// </summary>
    [RequiresUnreferencedCode("Devtools component discovery uses Activator.CreateInstance.")]
    internal static Func<Component> PreviewRootFactory(Type? hostRoot, Func<Component>? hostRootFactory, bool componentNamed, Type previewedType)
        => hostRootFactory is not null && PreviewsAppRoot(hostRoot, hostRootFactory, componentNamed, previewedType)
            ? hostRootFactory
            : () => (Component)Activator.CreateInstance(previewedType)!;

    /// <summary>
    /// Builds the initial spec for a non-embedded preview window when <c>--x</c>/<c>--y</c>
    /// placed it explicitly, and <c>null</c> otherwise so the OS keeps choosing the position.
    /// </summary>
    /// <remarks>
    /// The docs screenshot harness uses this to capture on a monitor whose scale matches the
    /// repo's 150% capture convention when that monitor is not the primary display. It matters
    /// because capture is of the live window in *physical* pixels, so the
    /// captured size follows the DPI of whichever monitor the window sits on.
    /// <para>Both axes are required together: a half-specified origin has no sensible meaning
    /// (the other axis would fall back to an OS-chosen value on a possibly different monitor),
    /// so a lone <c>--x</c> is ignored rather than guessed at.</para>
    /// <para>Returning a non-null spec makes <see cref="ReactorApp.BuildInitialWindowSpec"/>
    /// take its pass-through branch, so every flat option this path still cares about has to be
    /// restated here — <paramref name="fullScreen"/> in particular, which would otherwise be
    /// silently dropped for a preview launched with an explicit origin.</para>
    /// </remarks>
    internal static WindowSpec? BuildPositionedWindowSpec(
        DevtoolsCliOptions options,
        string baseTitle,
        double? width,
        double? height,
        bool fullScreen = false)
    {
        if (options.WindowX is not { } x || options.WindowY is not { } y) return null;

        return new WindowSpec
        {
            Title = baseTitle,
            Width = width,
            Height = height,
            Presenter = fullScreen ? PresenterKind.FullScreen : PresenterKind.Overlapped,
            StartPosition = WindowStartPosition.Manual,
            ManualPosition = (x, y),
            // Placement persistence would restore a previously saved rect and
            // silently override the position just requested.
            PersistPlacement = false,
        };
    }

    internal static WindowSpec BuildEmbedWindowSpec(DevtoolsCliOptions options, string baseTitle, double? width, double? height)    {
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
}
