global using Microsoft.UI.Reactor;
global using Microsoft.UI.Reactor.Core;
global using Microsoft.UI.Reactor.Layout;
global using Microsoft.UI.Xaml;
global using WidgetCreator;
global using WidgetCreator.Services;

using static Microsoft.UI.Reactor.Factories;

// Widget Creator — an app that creates apps.
//
// Type a prompt, the GitHub Copilot SDK generates a single-file Reactor app,
// we build it, and launch the result inside an MXC sandbox with UI + remote
// network but NO local filesystem access (a web-like experience).

SessionLog.Init();
SessionLog.Write($"[Program] launched args={string.Join(' ', System.Environment.GetCommandLineArgs())}");

ReactorApp.Run<WidgetCreatorShell>(
    "Widget Creator",
    width: 1180,
    height: 820,
    configure: host => Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(host.Reconciler));
