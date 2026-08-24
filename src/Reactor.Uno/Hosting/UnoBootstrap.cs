// Uno host-builder bootstrap + code-only Application subclass.
//
// Uno 6 unifies hosting behind UnoPlatformHostBuilder (Uno.UI.Hosting). The
// platform providers are compiled per-TFM: __WASM__ gets UseWebAssembly(); the
// desktop TFM gets the X11/Framebuffer/macOS/Win32 providers (the builder picks
// the one available at runtime).

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Reactor.Hosting;
using Uno.UI.Hosting;

namespace Microsoft.UI.Reactor;

internal static class UnoBootstrap
{
    public static void Run() => BuildHost().Run();

    public static Task RunAsync() => BuildHost().RunAsync();

    private static UnoPlatformHost BuildHost()
    {
        var builder = UnoPlatformHostBuilder.Create()
            .App(() => new ReactorApplication());

#if __WASM__
        builder = builder.UseWebAssembly();
#elif __IOS__ || __MACCATALYST__ || __TVOS__
        // Apple heads DO start from a real Main, and Uno gives them the same
        // host-builder shape as desktop — so ReactorApp.Run works there
        // unchanged, with UIKit as the platform provider.
        builder = builder.UseAppleUIKit();
#elif __ANDROID__
        // Android is the one target with no console entry point: the OS starts
        // an Activity, so the head calls ReactorApp.CreateApplication<TRoot>()
        // and this builder path is never reached.
#else
        builder = builder
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32();
#endif

        return builder.Build();
    }
}

/// <summary>
/// The Uno <see cref="Application"/> that hosts a Reactor tree. Created by
/// <see cref="UnoBootstrap"/>; reads startup config from <see cref="ReactorApp.Options"/>.
/// </summary>
internal sealed class ReactorApplication : Application
{
    private ReactorWindow? _primary;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Process-wide unhandled-exception routing. Mirrors the Windows host:
        // log, then let an app-supplied hook decide whether to swallow. Unknown
        // exceptions are left unhandled so the app crashes with a useful error
        // rather than limping along corrupt.
        UnhandledException += (_, e) =>
        {
            ReactorApp.AppLogger?.LogError(
                e.Exception,
                "UnhandledException: {ExceptionType}: {ExceptionMessage}",
                e.Exception.GetType().Name, e.Exception.Message);
            if (ReactorApp.OnUnhandledException is not null)
                e.Handled = ReactorApp.OnUnhandledException(e.Exception);
        };

        // Marshal cross-thread setState callbacks back onto the UI thread.
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq is not null)
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dq));
        }

        // WinUI control templates/styles.
        try { Resources.MergedDictionaries.Add(new XamlControlsResources()); }
        catch { /* resources already present / headless */ }

        var opts = ReactorApp.Options;

        var spec = new WindowSpec
        {
            Title = opts.WindowTitle,
            Width = opts.WindowWidth,
            Height = opts.WindowHeight,
            FullScreen = opts.FullScreen,
        };

        // The primary window goes through the same construction path as every
        // secondary window opened later via ReactorApp.OpenWindow / UseOpenWindow.
        _primary = ReactorApp.OpenWindowCore(
            spec, opts.RootFactory, opts.RootRenderFunc, opts.Configure);
    }
}
