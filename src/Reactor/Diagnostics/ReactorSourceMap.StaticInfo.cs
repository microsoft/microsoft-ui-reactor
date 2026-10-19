using System.ComponentModel;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Static facts the source-map generator knows at compile time and the runtime cannot
/// discover: the identifier an element was assigned to (<c>var title = TextBlock(…)</c> →
/// <c>title</c>) and, per component, the variable each hook was stored in
/// (<c>var (count, setCount) = UseState(0)</c> → hook 0 is <c>count</c>).
///
/// <para><b>How it gets here.</b> In a source-mapped build the generator emits a module
/// initializer that hands <see cref="RegisterStaticInfo"/> a fill callback. Registering
/// stores the delegate and nothing else; the tables are built on the first lookup, which
/// only diagnostics consumers make. A build without source mapping emits nothing and
/// every lookup returns <c>null</c>.</para>
///
/// <para><b>Keys.</b> Names and render-function hooks are keyed by the same
/// (path, line, column) the generator stamps into <see cref="Element.CallSite"/>, with the
/// same <c>PathMap</c> applied, so a lookup with an element's call site finds its entry. When
/// two source-mapped assemblies claim one location with different facts (both mapping their
/// roots to <c>/_/</c>), the location is unknown rather than last-writer-wins.
/// Class-component hooks are keyed by the assembly and open generic type full name of the type declaring <c>Render()</c>.</para>
///
/// <para><b>Hot reload.</b> The tables describe the build that was compiled, and an edit can
/// move a call onto a location the table gives another element. After a hot-reload update every
/// lookup is unknown rather than wrong (<see cref="InvalidateStaticFactsForHotReload"/>), and
/// hosts drop the facts already published on their next render (<see cref="StaticFactsRevision"/>).</para>
///
/// <para><b>Unloadable plug-ins.</b> A source-mapped assembly in a collectible
/// <c>AssemblyLoadContext</c> registers like any other, and must still be able to unload.
/// Everything that would keep it alive (the pending fill callback, which is code in that
/// assembly, and its component-hook table) is owned weakly by the assembly through a
/// <c>ConditionalWeakTable</c>; the
/// location-keyed tables hold strings only.</para>
/// </summary>
public static partial class ReactorSourceMap
{
    private static readonly global::System.Threading.Lock s_staticGate = new();
    // Fill callbacks not yet applied, owned weakly by their assembly (a callback is code in
    // that assembly, so a strong list would keep a collectible plug-in from unloading).
    // s_pendingCount > 0 is the lock-free "something pending" check.
    private static global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Reflection.Assembly, List<Action<ReactorStaticInfoBuilder>>>? s_pendingStatic;
    private static int s_pendingCount;
    private static ReactorStaticInfoBuilder? s_static;
    private static int s_factsRevision;
    private static int s_hotReloaded;

    /// <summary>
    /// Bumped whenever facts already published on controls may have gone stale: a source-mapped
    /// assembly registered after the table was built (it can make another assembly's file
    /// unattributable), or a hot-reload update. Applies pending registrations first, so a
    /// reader always compares against the current table.
    /// </summary>
    internal static int StaticFactsRevision
    {
        get
        {
            _ = StaticInfo();
            return Volatile.Read(ref s_factsRevision);
        }
    }

    /// <summary>
    /// A hot-reload update was applied. The tables describe the compiled build; an edit can
    /// move a call onto a location the table assigns to another element, and can change a
    /// render's hooks, so from now on every name and hook list is unknown rather than wrong.
    /// </summary>
    internal static void InvalidateStaticFactsForHotReload()
    {
        if (Interlocked.Exchange(ref s_hotReloaded, 1) == 0)
            Interlocked.Increment(ref s_factsRevision);
    }

    /// <summary>Whether a hot-reload update made every static fact unknown.</summary>
    internal static bool StaticFactsInvalidatedByHotReload => Volatile.Read(ref s_hotReloaded) != 0;

    /// <summary>Test seam: undoes <see cref="InvalidateStaticFactsForHotReload"/>.</summary>
    internal static void ResetHotReloadInvalidationForTests()
    {
        if (Interlocked.Exchange(ref s_hotReloaded, 0) != 0)
            Interlocked.Increment(ref s_factsRevision);
    }

    /// <summary>Whether facts keyed by locations in <paramref name="filePath"/> can be attributed.</summary>
    internal static bool IsFileAttributable(string? filePath)
        => !StaticFactsInvalidatedByHotReload && (StaticInfo() is not { } table || table.IsAttributable(filePath));

    /// <summary>
    /// Infrastructure for the generated source-map module initializer; not intended to be
    /// called directly. Queues <paramref name="fill"/> for <paramref name="assembly"/> (the
    /// source-mapped assembly the facts describe); it runs on the first lookup.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterStaticInfo(global::System.Reflection.Assembly assembly, Action<ReactorStaticInfoBuilder> fill)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(fill);
        lock (s_staticGate)
        {
            (s_pendingStatic ??= new()).GetOrCreateValue(assembly).Add(fill);
            Volatile.Write(ref s_pendingCount, s_pendingCount + 1);
        }
    }

    /// <summary>
    /// The identifier the element created at <paramref name="site"/> was assigned to, or
    /// <c>null</c>. Prefer <see cref="SourceLocation.DeclaredName"/>.
    /// </summary>
    internal static string? GetDeclaredName(SourceLocation site)
        => !StaticFactsInvalidatedByHotReload && StaticInfo() is { } table && table.IsAttributable(site.FilePath)
            && table.NameTable.TryGetValue(site, out var name) ? name : null;

    /// <summary>
    /// The <c>hooks=</c> value for a class component, or <c>null</c> when unknown or when its
    /// <c>Render()</c> calls no hooks.
    /// </summary>
    /// <remarks>
    /// The table is keyed by the type that DECLARES a <c>Render()</c> override (and its
    /// assembly, since full names are only unique within one). A component that inherits
    /// <c>Render()</c> walks to its base types: the generator records every override, even a
    /// hook-free one, so the first recorded type is the <c>Render()</c> that runs. The walk
    /// stops (unknown) at a type whose assembly was not source-mapped, since nothing says
    /// whether that type overrides <c>Render()</c>.
    /// </remarks>
    internal static string? GetComponentHooks(Type componentType)
    {
        if (StaticFactsInvalidatedByHotReload) return null;
        var table = StaticInfo();
        if (table is null || !table.HasComponentHooks) return null;
        for (Type? type = componentType; type is not null; type = type.BaseType)
        {
            var open = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
            if (open == typeof(global::Microsoft.UI.Reactor.Core.Component) || open == typeof(global::Microsoft.UI.Reactor.Core.Component<>)
                || !table.TryGetAssemblyHooks(open.Assembly, out var assemblyHooks))
                return null;
            if (open.FullName is { } key && assemblyHooks.TryGetValue(key, out var hooks))
                return hooks.Length == 0 ? null : hooks;
        }
        return null;
    }

    /// <summary>
    /// The <c>hooks=</c> value for a render function passed to the call written at
    /// <paramref name="site"/> (<c>Memo(ctx =&gt; …)</c>, <c>RenderEachTime(ctx =&gt; …)</c>,
    /// a root <c>Mount(ctx =&gt; …)</c>), or <c>null</c>.
    /// </summary>
    internal static string? GetRenderFunctionHooks(SourceLocation site)
        => !StaticFactsInvalidatedByHotReload && StaticInfo() is { } table && table.IsAttributable(site.FilePath)
            && table.RenderFunctionHookTable.TryGetValue(site, out var hooks) ? hooks : null;

    /// <summary>
    /// The path published in <c>ReactorDiagnostics.SourceProperty</c>'s <c>at=</c> field —
    /// never an absolute developer path, mirroring XAML's runtime source info, which reports
    /// <c>ms-appx:///Pages/Main.xaml</c> rather than a disk path:
    /// <list type="number">
    ///   <item>under the compiling project's directory → relative to it, <c>/</c>-separated,
    ///   <paramref name="marker"/> <c>null</c> (<c>Pages/Main.cs</c>);</item>
    ///   <item>else under the solution / repository root → relative to it, marker
    ///   <c>"root"</c>;</item>
    ///   <item>else an absolute path → the file name only, marker <c>"0"</c>;</item>
    ///   <item>else (already relative, e.g. a deterministic <c>/_/src/App.cs</c> from
    ///   <c>PathMap</c>) → unchanged apart from <c>/</c> separators, marker <c>null</c>.</item>
    /// </list>
    /// <see cref="Element.CallSite"/> itself is not affected.
    /// </summary>
    internal static string ToPublishedPath(string filePath, out string? marker)
    {
        marker = null;
        var normalized = filePath.Replace('\\', '/');
        var table = StaticInfo();
        if (table is not null)
        {
            if (LongestPrefix(table.ProjectDirectories, normalized) is { } project)
                return normalized.Substring(project.Length);
            if (LongestPrefix(table.RootDirectories, normalized) is { } root)
            {
                marker = "root";
                return normalized.Substring(root.Length);
            }
        }

        if (IsAbsolutePath(filePath))
        {
            marker = "0";
            int slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }
        return normalized;
    }

    private static string? LongestPrefix(List<string> directories, string path)
        => global::System.Linq.Enumerable.MaxBy(
            global::System.Linq.Enumerable.Where(directories,
                d => path.Length > d.Length && path.StartsWith(d, StringComparison.OrdinalIgnoreCase)),
            static d => d.Length);

    /// <summary>
    /// A drive-rooted, UNC or <c>/</c>-rooted path. A deterministic-build path
    /// (<c>/_/…</c>, <c>/_1/…</c>: the SDK's PathMap roots) is NOT treated as absolute: it is
    /// already relative to an anonymous root and carries no developer path. Any other
    /// <c>/_…</c> directory (<c>/_work/…</c>) is an ordinary absolute path.
    /// </summary>
    internal static bool IsAbsolutePath(string path)
    {
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return true;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            return true;
        // Rooted on the current drive (\Users\me\App.cs): absolute for disclosure purposes.
        if (path.StartsWith('\\'))
            return true;
        return path.StartsWith('/') && !IsDeterministicRoot(path);
    }

    /// <summary><c>/_/</c> or <c>/_&lt;digits&gt;/</c> at the start of the path.</summary>
    private static bool IsDeterministicRoot(string path)
    {
        if (!path.StartsWith("/_", StringComparison.Ordinal)) return false;
        int i = 2;
        while (i < path.Length && char.IsAsciiDigit(path[i])) i++;
        return i < path.Length && path[i] == '/';
    }

    /// <summary><c>/</c>-separated with a trailing <c>/</c>, or null for an empty/undefined directory.</summary>
    internal static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory == "*Undefined*") return null;
        var normalized = directory.Replace('\\', '/');
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private static ReactorStaticInfoBuilder? StaticInfo()
    {
        if (Volatile.Read(ref s_pendingCount) == 0) return Volatile.Read(ref s_static);

        lock (s_staticGate)
        {
            if (s_pendingCount != 0 && s_pendingStatic is { } pending)
            {
                // A later assembly load can queue more entries after readers already hold
                // the published table, which they read without the lock. Build a fresh one
                // and swap it in, so a published table is never mutated.
                var table = new ReactorStaticInfoBuilder();
                if (s_static is { } previous)
                {
                    table.CopyFrom(previous);
                    // Facts may already be published on controls; a new assembly can make a
                    // file unattributable, so they are re-checked (see StaticFactsRevision).
                    Interlocked.Increment(ref s_factsRevision);
                }
                // An entry whose plug-in has already unloaded is simply gone from the table.
                foreach (var (assembly, fills) in pending)
                {
                    table.CurrentAssembly = assembly;
                    foreach (var fill in fills) fill(table);
                }
                table.CurrentAssembly = null;
                // Publish the table BEFORE clearing the marker: a reader that sees the marker
                // cleared (lock-free path above) must also see the table it stands for.
                Volatile.Write(ref s_static, table);
                s_pendingStatic = null;
                Volatile.Write(ref s_pendingCount, 0);
            }
            return s_static;
        }
    }
}

/// <summary>
/// Receives the generator's static source-map facts. Infrastructure for generated code;
/// not intended to be used directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ReactorStaticInfoBuilder
{
    internal ReactorStaticInfoBuilder() { }

    internal List<string> ProjectDirectories { get; } = new();
    internal List<string> RootDirectories { get; } = new();

    // Per source-mapped assembly, its class components' hooks (open generic type full name →
    // hooks): full names are only unique within one assembly. Owned weakly by the assembly, so
    // a collectible plug-in can unload; an assembly with an entry (even empty) is source-mapped.
    private readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Reflection.Assembly, Dictionary<string, string>> _componentHooks = new();
    private Dictionary<string, string>? _currentHooks;

    /// <summary>Whether any component hooks were recorded (a fast path for the common empty table).</summary>
    internal bool HasComponentHooks { get; private set; }

    /// <summary>
    /// The assembly whose fill callback is running: marks it source-mapped and keys the
    /// component hooks recorded next. Only the hook table is selected; no reference is kept.
    /// </summary>
    internal global::System.Reflection.Assembly? CurrentAssembly
    {
        set => _currentHooks = value is null ? null : _componentHooks.GetOrCreateValue(value);
    }

    /// <summary>The component hooks of a source-mapped assembly; false for any other assembly.</summary>
    internal bool TryGetAssemblyHooks(global::System.Reflection.Assembly assembly, out Dictionary<string, string> hooks)
        => _componentHooks.TryGetValue(assembly, out hooks!);

    internal void CopyFrom(ReactorStaticInfoBuilder other)
    {
        foreach (var (k, v) in other.NameTable) NameTable[k] = v;
        foreach (var (assembly, hooks) in other._componentHooks)
            _componentHooks.AddOrUpdate(assembly, new Dictionary<string, string>(hooks, StringComparer.Ordinal));
        HasComponentHooks = other.HasComponentHooks;
        foreach (var (k, v) in other.RenderFunctionHookTable) RenderFunctionHookTable[k] = v;
        foreach (var (k, v) in other.FileFingerprints) FileFingerprints[k] = v;
        ProjectDirectories.AddRange(other.ProjectDirectories);
        RootDirectories.AddRange(other.RootDirectories);
    }

    /// <summary>
    /// Records the compiling project's directory and, when known, its solution/repository
    /// root, with the same <c>PathMap</c> applied as the stamped paths. Used to publish
    /// call sites RELATIVE to them (see <c>ReactorSourceMap.ToPublishedPath</c>).
    /// </summary>
    public void Roots(string projectDirectory, string? rootDirectory)
    {
        if (ReactorSourceMap.NormalizeDirectory(projectDirectory) is { } project && !ProjectDirectories.Contains(project))
            ProjectDirectories.Add(project);
        if (ReactorSourceMap.NormalizeDirectory(rootDirectory) is { } root && !RootDirectories.Contains(root))
            RootDirectories.Add(root);
    }
    // A null value marks a location two source-mapped assemblies both claim with different
    // facts (two libraries mapping their roots to /_/ can share a path, line and column):
    // unknown, never one library's name or hooks on the other's controls.
    internal Dictionary<SourceLocation, string?> NameTable { get; } = new();
    internal Dictionary<SourceLocation, string?> RenderFunctionHookTable { get; } = new();

    /// <summary>Records that the element created at this call site was assigned to <paramref name="name"/>.</summary>
    public void Name(string filePath, int lineNumber, int columnNumber, string name)
        => AddUnlessConflicting(NameTable, new SourceLocation(filePath, lineNumber, columnNumber), name);

    /// <summary>
    /// Records the fingerprint of everything the registering assembly knows about one source
    /// file: every call site with its declared name or none, and every render function with
    /// its hooks or none (every root mount site included, as a render function may be passed
    /// there). When two assemblies stamp the same path with different fingerprints
    /// (both mapping their roots to <c>/_/</c>), none of that file's names or render-function
    /// hooks can be attributed, absent ones included, so all are reported as unknown.
    /// </summary>
    public void Source(string filePath, string fingerprint)
    {
        if (!FileFingerprints.TryGetValue(filePath, out var existing))
            FileFingerprints[filePath] = fingerprint;
        else if (!string.Equals(existing, fingerprint, StringComparison.Ordinal))
            FileFingerprints[filePath] = null;
    }

    /// <summary>Per stamped path, its facts' fingerprint; <c>null</c> when assemblies disagree.</summary>
    internal Dictionary<string, string?> FileFingerprints { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether facts keyed by a location in <paramref name="filePath"/> can be trusted.</summary>
    internal bool IsAttributable(string? filePath)
        => filePath is null || !FileFingerprints.TryGetValue(filePath, out var fingerprint) || fingerprint is not null;

    /// <summary>
    /// Records the <c>hooks=</c> value for a class component (open generic type full name) in
    /// the registering assembly. An empty value records a <c>Render()</c> override that calls
    /// no hooks.
    /// </summary>
    public void ComponentHooks(string componentTypeFullName, string hooks)
    {
        if (_currentHooks is { } assemblyHooks)
        {
            assemblyHooks[componentTypeFullName] = hooks;
            HasComponentHooks = true;
        }
    }

    /// <summary>Records the <c>hooks=</c> value for a render function passed to the call at this site.</summary>
    public void RenderFunctionHooks(string filePath, int lineNumber, int columnNumber, string hooks)
        => AddUnlessConflicting(RenderFunctionHookTable, new SourceLocation(filePath, lineNumber, columnNumber), hooks);

    private static void AddUnlessConflicting(Dictionary<SourceLocation, string?> table, SourceLocation site, string value)
    {
        if (!table.TryGetValue(site, out var existing))
            table[site] = value;
        else if (!string.Equals(existing, value, StringComparison.Ordinal))
            table[site] = null;
    }
}
