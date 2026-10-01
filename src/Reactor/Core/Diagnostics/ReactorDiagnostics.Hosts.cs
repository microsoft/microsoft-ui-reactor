using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>Which kind of Reactor host a <see cref="ReactorHostInfo"/> describes.</summary>
public enum ReactorHostKind
{
    /// <summary>
    /// A <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/> rendering into a WinUI
    /// <see cref="Microsoft.UI.Xaml.Window"/> — either a window opened through
    /// <c>ReactorApp.Run</c> / <c>ReactorApp.OpenWindow</c> (see
    /// <see cref="ReactorHostInfo.ReactorWindow"/>) or a <c>new ReactorHost(window)</c>
    /// on any window, optionally redirected into a
    /// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost.ContentTarget"/>.
    /// </summary>
    WindowHost,

    /// <summary>
    /// A <see cref="Microsoft.UI.Reactor.Hosting.ReactorHostControl"/> embedded in a XAML
    /// tree (a hybrid "island").
    /// </summary>
    HostControl,
}

/// <summary>
/// Point-in-time description of one live Reactor host: where it renders, what its root
/// is, and where that root was mounted. Returned by <see cref="ReactorDiagnostics.GetHosts"/>.
/// </summary>
/// <remarks>
/// <para>Provisional diagnostics API for external inspectors (for example
/// <c>winapp devtools</c>); shape may change while Reactor is in preview.</para>
/// <para>A snapshot, not a live view: re-query after a render, window open, or close.
/// Every value is a plain reference read captured when the snapshot was taken, so the
/// snapshot itself can be taken from any thread; touching the returned WinUI objects
/// is subject to their usual UI-thread rules. Take it on the UI thread when the
/// values must be consistent with the visual tree you are about to walk.</para>
/// <para>Holding a snapshot keeps its host, window and root objects alive; the
/// registry behind <see cref="ReactorDiagnostics.GetHosts"/> does not.</para>
/// </remarks>
public sealed class ReactorHostInfo
{
    internal ReactorHostInfo(
        ReactorHostKind kind,
        ReactorHost? host,
        ReactorHostControl? hostControl,
        ReactorWindow? reactorWindow,
        Window? window,
        FrameworkElement? hostElement,
        Reconciler reconciler,
        UIElement? rootControl,
        Component? rootComponent,
        Func<RenderContext, Element>? rootRenderFunction,
        SourceLocation? mountSite)
    {
        Kind = kind;
        Host = host;
        HostControl = hostControl;
        ReactorWindow = reactorWindow;
        Window = window;
        HostElement = hostElement;
        Reconciler = reconciler;
        RootControl = rootControl;
        RootComponent = rootComponent;
        RootRenderFunction = rootRenderFunction;
        MountSite = mountSite;
    }

    /// <summary>Which host shape this is.</summary>
    public ReactorHostKind Kind { get; }

    /// <summary>The window host, when <see cref="Kind"/> is <see cref="ReactorHostKind.WindowHost"/>.</summary>
    public ReactorHost? Host { get; }

    /// <summary>The embedded host control, when <see cref="Kind"/> is <see cref="ReactorHostKind.HostControl"/>.</summary>
    public ReactorHostControl? HostControl { get; }

    /// <summary>
    /// The <c>ReactorApp</c> window that owns the host (its <c>Id</c>, <c>Key</c> and
    /// <c>NativeWindow</c> identify it), or null for a host constructed directly on a
    /// window and for every <see cref="ReactorHostKind.HostControl"/>.
    /// </summary>
    public ReactorWindow? ReactorWindow { get; }

    /// <summary>
    /// The WinUI window a <see cref="ReactorHostKind.WindowHost"/> renders into. Null for
    /// <see cref="ReactorHostKind.HostControl"/>, which does not own a window; use
    /// <see cref="HostElement"/>'s <c>XamlRoot</c> to place it.
    /// </summary>
    public Window? Window { get; }

    /// <summary>
    /// The element whose content is the Reactor root: the <see cref="ReactorHostControl"/>
    /// itself, a window host's <see cref="ReactorHost.ContentTarget"/>, or null when a
    /// window host renders straight into <c>Window.Content</c>.
    /// </summary>
    public FrameworkElement? HostElement { get; }

    /// <summary>The host's reconciler. Each host owns exactly one.</summary>
    public Reconciler Reconciler { get; }

    /// <summary>
    /// The control the root currently renders as, or null before the first render.
    /// This is the control the host places in <c>Window.Content</c> / its content slot;
    /// when a dev overlay is on, that slot holds an overlay wrapper and this control is
    /// its child. After a render error it is the error fallback panel.
    /// </summary>
    public UIElement? RootControl { get; }

    /// <summary>The root component instance, or null for a render-function root or before mount.</summary>
    public Component? RootComponent { get; }

    /// <summary>The root component's runtime type (<c>RootComponent?.GetType()</c>), for display.</summary>
    public Type? RootComponentType => RootComponent?.GetType();

    /// <summary>The root render function, or null for a component root or before mount.</summary>
    public Func<RenderContext, Element>? RootRenderFunction { get; }

    /// <summary>
    /// Where the root was mounted — the <c>ReactorApp.Run</c>, <c>ReactorApp.OpenWindow</c>,
    /// <c>ReactorHost.Mount</c> or <c>ReactorHostControl.Mount</c> call in app code. Null
    /// unless the calling project was built with source mapping (<c>ReactorSourceMap=true</c>,
    /// the Debug default) and <see cref="Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled"/>
    /// was true at mount time; also null for a root created from
    /// <see cref="ReactorHostControl.ComponentFactory"/>, which has no call site.
    /// </summary>
    public SourceLocation? MountSite { get; }
}

/// <summary>Implemented by every Reactor host so the diagnostics registry can describe it.</summary>
internal interface IReactorDiagnosticHost
{
    /// <summary>A snapshot of the host, or null once it has been disposed.</summary>
    ReactorHostInfo? CaptureDiagnosticInfo();
}

/// <summary>
/// Weak, lazily allocated registry of live hosts. Hosts register in their constructor
/// and unregister on dispose; a host dropped without dispose simply ages out with the
/// garbage collector, because the registry never keeps one alive.
/// </summary>
internal static class ReactorHostRegistry
{
    private static readonly object s_gate = new();
    private static List<WeakReference<IReactorDiagnosticHost>>? s_hosts;

    internal static void Register(IReactorDiagnosticHost host)
    {
        lock (s_gate)
        {
            var hosts = s_hosts ??= new List<WeakReference<IReactorDiagnosticHost>>();
            hosts.RemoveAll(static w => !w.TryGetTarget(out _));
            hosts.Add(new WeakReference<IReactorDiagnosticHost>(host));
        }
    }

    internal static void Unregister(IReactorDiagnosticHost host)
    {
        lock (s_gate)
        {
            s_hosts?.RemoveAll(w => !w.TryGetTarget(out var target) || ReferenceEquals(target, host));
        }
    }

    /// <summary>Live hosts in registration order.</summary>
    internal static IReactorDiagnosticHost[] Snapshot()
    {
        lock (s_gate)
        {
            if (s_hosts is null || s_hosts.Count == 0) return global::System.Array.Empty<IReactorDiagnosticHost>();
            var live = new List<IReactorDiagnosticHost>(s_hosts.Count);
            foreach (var weak in s_hosts)
            {
                if (weak.TryGetTarget(out var host)) live.Add(host);
            }
            return live.ToArray();
        }
    }
}

public static partial class ReactorDiagnostics
{
    /// <summary>
    /// Snapshot of every live Reactor host in the process, in creation order: windows
    /// opened through <c>ReactorApp</c> (including ones not yet mounted), hosts created
    /// with <c>new ReactorHost(window)</c>, and <see cref="ReactorHostControl"/> islands.
    /// Disposed hosts are excluded. Safe to call from any thread; see
    /// <see cref="ReactorHostInfo"/> for consistency rules.
    /// </summary>
    /// <remarks>
    /// Provisional diagnostics API for external inspectors. A
    /// <see cref="ReactorHostControl"/> that was removed from the tree but never disposed
    /// stays listed until it is collected — Reactor cannot tell a transient reparent from
    /// a removal (issue #344); check its <c>XamlRoot</c> to skip detached islands.
    /// </remarks>
    public static IReadOnlyList<ReactorHostInfo> GetHosts()
    {
        var hosts = ReactorHostRegistry.Snapshot();
        if (hosts.Length == 0) return global::System.Array.Empty<ReactorHostInfo>();

        var infos = new List<ReactorHostInfo>(hosts.Length);
        foreach (var host in hosts)
        {
            if (host.CaptureDiagnosticInfo() is { } info) infos.Add(info);
        }
        return infos;
    }
}
