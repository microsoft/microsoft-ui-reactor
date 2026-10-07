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
/// <para>Deliberately exposes no reconciler internals: the root is described by name and
/// call site, and reached on screen through <see cref="RootControl"/> — the same WinUI
/// control an inspector already walks. Everything else is an existing public WinUI or
/// Reactor hosting type.</para>
/// <para>A snapshot, not a live view: re-query after a render, window open, or close.
/// The snapshot itself can be taken from any thread, and every value in it is read
/// atomically (never half of one mount and half of another). A snapshot taken while the
/// UI thread is remounting may still pair the outgoing root's name with the incoming
/// root's site; take it on the UI thread when the values must agree with each other or
/// with the visual tree you are about to walk. Touching the returned WinUI objects is
/// subject to their usual UI-thread rules.</para>
/// <para>Holding a snapshot keeps its host, window and root control alive; the registry
/// behind <see cref="ReactorDiagnostics.GetHosts"/> does not.</para>
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
        UIElement? rootControl,
        string? rootComponentName,
        string? rootRenderFunctionName,
        SourceLocation? mountSite)
    {
        Kind = kind;
        Host = host;
        HostControl = hostControl;
        ReactorWindow = reactorWindow;
        Window = window;
        HostElement = hostElement;
        RootControl = rootControl;
        RootComponentName = rootComponentName;
        RootRenderFunctionName = rootRenderFunctionName;
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

    /// <summary>
    /// The control the root currently renders as, or null before the first render.
    /// This is the control the host places in <c>Window.Content</c> / its content slot;
    /// when a dev overlay is on, that slot holds an overlay wrapper and this control is
    /// its child. After a render error it is the error fallback panel.
    /// </summary>
    public UIElement? RootControl { get; }

    /// <summary>
    /// The root component's type name, namespace-qualified, with nested types joined by
    /// <c>.</c> and generic arguments spelled out (<c>MyApp.Pages.Dashboard</c>,
    /// <c>MyApp.ListPage&lt;MyApp.Order&gt;</c>). Null for a render-function root and before
    /// the first mount.
    /// </summary>
    public string? RootComponentName { get; }

    /// <summary>
    /// A readable name for a render-function root: the method that declares it, with
    /// compiler-generated names undone — <c>MyApp.Program.Main (lambda)</c>,
    /// <c>MyApp.Program.Main.Render</c> for a local function, <c>MyApp.Shell.Render</c> for
    /// a method group. Null for a component root, before the first mount, and when the
    /// runtime cannot describe the delegate (for example NativeAOT with stack-trace data
    /// disabled). For a lambda, <see cref="MountSite"/> is usually the better identity.
    /// </summary>
    public string? RootRenderFunctionName { get; }

    /// <summary>
    /// Where the root was mounted — the <c>ReactorApp.Run</c>, <c>ReactorApp.OpenWindow</c>,
    /// <c>ReactorWindow.Mount</c>, <c>ReactorHost.Mount</c> or <c>ReactorHostControl.Mount</c>
    /// call in app code. Null unless the calling project was built with source mapping
    /// (<c>ReactorSourceMap=true</c>, the Debug default) and
    /// <see cref="Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled"/> was true at mount
    /// time; also null for a root created from <see cref="ReactorHostControl.ComponentFactory"/>
    /// or <see cref="ReactorHostControl.ComponentType"/>, or mounted by framework code, none of
    /// which has a call site in app code.
    /// </summary>
    public SourceLocation? MountSite { get; }

    /// <summary>
    /// Builds the snapshot for a host's current root. A component root wins when both are set,
    /// matching what the hosts actually render.
    /// </summary>
    internal static ReactorHostInfo ForRoot(
        ReactorHostKind kind,
        ReactorHost? host,
        ReactorHostControl? hostControl,
        ReactorWindow? reactorWindow,
        Window? window,
        FrameworkElement? hostElement,
        UIElement? rootControl,
        Component? rootComponent,
        Func<RenderContext, Element>? rootRenderFunction,
        SourceLocation? mountSite)
        => new(
            kind, host, hostControl, reactorWindow, window, hostElement, rootControl,
            rootComponent is null ? null : RootNames.ForType(rootComponent.GetType()),
            rootComponent is null && rootRenderFunction is not null ? RootNames.ForDelegate(rootRenderFunction) : null,
            mountSite);
}

/// <summary>
/// Display names for diagnostics roots. Pure string work over runtime metadata that is
/// always present (a constructed type's name) or explicitly AOT-aware
/// (<see cref="global::System.Diagnostics.DiagnosticMethodInfo"/>), so it is trim- and AOT-safe.
/// </summary>
internal static class RootNames
{
    /// <summary>
    /// <c>Namespace.Outer.Inner&lt;Arg&gt;</c> for any type. Nested types are joined with
    /// <c>.</c>; a nested type of a generic outer lists all of its type arguments at the end
    /// (that is how the runtime stores them), which keeps the name readable.
    /// </summary>
    internal static string ForType(Type type)
    {
        if (type.IsGenericParameter) return type.Name;

        // Arrays (and pointers/by-refs) name their element type: format it recursively,
        // otherwise "List`1[]" loses both its type arguments and its rank.
        if (type.IsArray)
            return ForType(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if ((type.IsPointer || type.IsByRef) && type.GetElementType() is { } element)
            return ForType(element) + (type.IsPointer ? "*" : "&");

        // Outermost first: Namespace.Outer.Inner.
        var segments = new Stack<string>();
        for (var t = type; t is not null; t = t.DeclaringType)
            segments.Push(StripArity(t.Name));
        var name = string.Join(".", segments);
        if (!string.IsNullOrEmpty(type.Namespace))
            name = type.Namespace + "." + name;

        if (!type.IsGenericType) return name;
        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(ForType)) + ">";
    }

    /// <summary>A readable name for a delegate's target method, or null when unavailable.</summary>
    internal static string? ForDelegate(Delegate function)
    {
        global::System.Diagnostics.DiagnosticMethodInfo? info;
        try
        {
            info = global::System.Diagnostics.DiagnosticMethodInfo.Create(function);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
        return info is null ? null : ForMethod(info.DeclaringTypeName, info.Name);
    }

    /// <summary>
    /// Undoes the C# compiler's naming for lambdas, local functions and closure classes:
    /// <list type="bullet">
    ///   <item><c>Program+&lt;&gt;c</c>, <c>&lt;Main&gt;b__0_0</c> → <c>Program.Main (lambda)</c></item>
    ///   <item><c>&lt;Main&gt;g__Render|0_1</c> → <c>Program.Main.Render</c></item>
    ///   <item><c>&lt;&lt;Main&gt;$&gt;b__0_0</c> (top-level statements) → <c>Program.Main (lambda)</c></item>
    ///   <item>an ordinary method → <c>Type.Method</c></item>
    /// </list>
    /// </summary>
    internal static string ForMethod(string? declaringTypeName, string methodName)
    {
        var type = CleanTypeName(declaringTypeName);
        var method = DemangleMethod(methodName);
        return string.IsNullOrEmpty(type) ? method : type + "." + method;
    }

    private static string CleanTypeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // Drop any generic instantiation suffix ("Foo`1[[...]]") and the arity markers.
        var bracket = name.IndexOf('[');
        if (bracket >= 0) name = name.Substring(0, bracket);

        var parts = name.Split('+')
            .Where(static p => p.Length > 0 && p[0] != '<') // <>c, <>c__DisplayClass0_0, <Foo>d__3
            .Select(StripArity);
        return string.Join(".", parts);
    }

    private static string DemangleMethod(string name)
    {
        if (name.Length == 0 || name[0] != '<') return StripArity(name);

        var close = MatchingClose(name);
        if (close < 0) return name;

        var inner = DemangleMethod(name.Substring(1, close - 1));
        var rest = name.Substring(close + 1);

        if (rest.StartsWith("b__", StringComparison.Ordinal))
            return inner + " (lambda)";
        if (rest.StartsWith("g__", StringComparison.Ordinal))
        {
            var local = rest.Substring(3);
            var bar = local.IndexOf('|');
            if (bar >= 0) local = local.Substring(0, bar);
            return inner + "." + local;
        }

        // "<Main>$" (top-level statements entry point) and anything unrecognised.
        return inner;
    }

    private static int MatchingClose(string name)
    {
        var depth = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '<') depth++;
            else if (name[i] == '>' && --depth == 0) return i;
        }
        return -1;
    }

    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick >= 0 ? name.Substring(0, tick) : name;
    }
}
/// <summary>Implemented by every Reactor host so the diagnostics registry can describe it.</summary>
internal interface IReactorDiagnosticHost
{
    /// <summary>A snapshot of the host, or null once it has been disposed.</summary>
    ReactorHostInfo? CaptureDiagnosticInfo();

    /// <summary>
    /// Tags every already-mounted component boundary (see
    /// <c>Reconciler.TagComponentBoundaries</c>). Callable from any thread: the host runs it
    /// on its own UI thread. A disposed host does nothing.
    /// </summary>
    void TagComponentBoundaries();
}

/// <summary>
/// Weak, lazily allocated registry of live hosts. Hosts register in their constructor
/// and unregister on dispose; a host dropped without dispose simply ages out with the
/// garbage collector, because the registry never keeps one alive.
/// </summary>
internal static class ReactorHostRegistry
{
    private static readonly Lock s_gate = new();
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

    /// <summary>
    /// Called when source mapping turns on: every live host tags the component boundaries it
    /// already mounted, so an inspector that switches the flag on late still sees them.
    /// </summary>
    internal static void TagComponentBoundariesInAllHosts()
    {
        foreach (var host in Snapshot())
            host.TagComponentBoundaries();
    }

    /// <summary>Live hosts in registration order. Prunes entries whose host was collected.</summary>
    internal static IReactorDiagnosticHost[] Snapshot()
    {
        lock (s_gate)
        {
            if (s_hosts is null || s_hosts.Count == 0) return global::System.Array.Empty<IReactorDiagnosticHost>();
            var live = new List<IReactorDiagnosticHost>(s_hosts.Count);
            s_hosts.RemoveAll(weak =>
            {
                if (!weak.TryGetTarget(out var host)) return true;
                live.Add(host);
                return false;
            });
            return live.ToArray();
        }
    }

    internal static int EntryCountForTest
    {
        get { lock (s_gate) return s_hosts?.Count ?? 0; }
    }

    internal static int DeadEntryCountForTest
    {
        get { lock (s_gate) return s_hosts?.Count(static w => !w.TryGetTarget(out _)) ?? 0; }
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

        return hosts
            .Select(static host => host.CaptureDiagnosticInfo())
            .Where(static info => info is not null)
            .ToList()!;
    }
}
