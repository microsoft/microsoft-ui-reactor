using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Inspector-facing host diagnostics: the live-host registry behind
/// <see cref="ReactorDiagnostics.GetHosts"/>, the root mount call-site scopes the
/// source-map interceptors open, and the component-boundary arm of
/// <c>Reconciler.NeedsTag</c>. Real hosts need a WinUI window, so the registry is
/// driven through <see cref="IReactorDiagnosticHost"/> fakes here; the hosts' own
/// implementations are thin field reads.
/// </summary>
[Collection("SourceMapGlobals")]
public sealed class ReactorHostDiagnosticsTests : IDisposable
{
    private readonly bool _previousEnabled = ReactorSourceMap.Enabled;

    public void Dispose() => ReactorSourceMap.Enabled = _previousEnabled;

    // ── Host registry ────────────────────────────────────────────────────

    private sealed class FakeHost(string label) : IReactorDiagnosticHost
    {
        public bool Disposed { get; set; }
        public Reconciler Reconciler { get; } = new();
        public Component? Root { get; set; }

        public ReactorHostInfo? CaptureDiagnosticInfo() => Disposed
            ? null
            : new ReactorHostInfo(
                ReactorHostKind.HostControl, host: null, hostControl: null, reactorWindow: null,
                window: null, hostElement: null, Reconciler, rootControl: null,
                rootComponent: Root, rootRenderFunction: null,
                mountSite: new SourceLocation(label, 1));
    }

    private sealed class ProbeComponent : Component
    {
        public override Element Render() => TextBlock("probe");
    }

    private static IEnumerable<ReactorHostInfo> InfosFor(params FakeHost[] hosts)
        => ReactorDiagnostics.GetHosts().Where(i => hosts.Any(h => ReferenceEquals(h.Reconciler, i.Reconciler)));

    [Fact]
    public void GetHosts_ListsRegisteredHostsInRegistrationOrder()
    {
        var a = new FakeHost("a");
        var b = new FakeHost("b");
        ReactorHostRegistry.Register(a);
        ReactorHostRegistry.Register(b);
        try
        {
            var infos = InfosFor(a, b).ToList();
            Assert.Equal(new[] { "a", "b" }, infos.Select(i => i.MountSite!.Value.FilePath));
        }
        finally
        {
            ReactorHostRegistry.Unregister(a);
            ReactorHostRegistry.Unregister(b);
        }
    }

    [Fact]
    public void GetHosts_OmitsUnregisteredAndDisposedHosts()
    {
        var kept = new FakeHost("kept");
        var removed = new FakeHost("removed");
        var disposed = new FakeHost("disposed");
        ReactorHostRegistry.Register(kept);
        ReactorHostRegistry.Register(removed);
        ReactorHostRegistry.Register(disposed);
        try
        {
            Assert.Equal(3, InfosFor(kept, removed, disposed).Count());

            ReactorHostRegistry.Unregister(removed);
            disposed.Disposed = true;

            var infos = InfosFor(kept, removed, disposed).ToList();
            Assert.Single(infos);
            Assert.Same(kept.Reconciler, infos[0].Reconciler);
        }
        finally
        {
            ReactorHostRegistry.Unregister(kept);
            ReactorHostRegistry.Unregister(disposed);
        }
    }

    [Fact]
    public void Registry_DoesNotKeepHostsAlive()
    {
        var weak = RegisterUnreferencedHost();

        for (int i = 0; i < 5 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "The host registry rooted a host nothing else references.");
        Assert.DoesNotContain(ReactorHostRegistry.Snapshot(), h => ReferenceEquals(h, weak.Target));
    }

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RegisterUnreferencedHost()
    {
        var host = new FakeHost("transient");
        ReactorHostRegistry.Register(host);
        return new WeakReference(host);
    }

    [Fact]
    public void HostInfo_ExposesRootComponentAndItsType()
    {
        var root = new ProbeComponent();
        var host = new FakeHost("typed") { Root = root };
        ReactorHostRegistry.Register(host);
        try
        {
            var info = Assert.Single(InfosFor(host));
            Assert.Same(root, info.RootComponent);
            Assert.Equal(typeof(ProbeComponent), info.RootComponentType);
            Assert.Null(info.RootRenderFunction);
        }
        finally
        {
            ReactorHostRegistry.Unregister(host);
        }
    }

    // ── Root mount call-site scopes ──────────────────────────────────────

    [Fact]
    public void RootMountSite_IsNotRecordedWhenSourceMappingIsOff()
    {
        ReactorSourceMap.Enabled = false;
        var token = ReactorSourceMap.EnterRootMountSite("App.cs", 12);
        try
        {
            Assert.Null(token);
            Assert.Null(ReactorSourceMap.TakeRootMountSite());
        }
        finally
        {
            ReactorSourceMap.ExitRootMountSite(token);
        }
    }

    [Fact]
    public void RootMountSite_IsClaimedExactlyOnce()
    {
        ReactorSourceMap.Enabled = true;
        var token = ReactorSourceMap.EnterRootMountSite("App.cs", 12);
        try
        {
            Assert.Equal(new SourceLocation("App.cs", 12), ReactorSourceMap.TakeRootMountSite());
            Assert.Null(ReactorSourceMap.TakeRootMountSite());
        }
        finally
        {
            ReactorSourceMap.ExitRootMountSite(token);
        }
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    [Fact]
    public void RootMountSite_InnermostScopeWins_AndOuterSurvivesForItsOwnMount()
    {
        ReactorSourceMap.Enabled = true;
        var outer = ReactorSourceMap.EnterRootMountSite("Program.cs", 5);
        try
        {
            var inner = ReactorSourceMap.EnterRootMountSite("Startup.cs", 30);
            try
            {
                Assert.Equal(new SourceLocation("Startup.cs", 30), ReactorSourceMap.TakeRootMountSite());
            }
            finally
            {
                ReactorSourceMap.ExitRootMountSite(inner);
            }

            Assert.Equal(new SourceLocation("Program.cs", 5), ReactorSourceMap.TakeRootMountSite());
        }
        finally
        {
            ReactorSourceMap.ExitRootMountSite(outer);
        }
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    [Fact]
    public void RootMountSite_AClosedScopeLeavesNothingForALaterMount()
    {
        ReactorSourceMap.Enabled = true;
        var token = ReactorSourceMap.EnterRootMountSite("Throws.cs", 7);
        ReactorSourceMap.ExitRootMountSite(token);

        Assert.Null(ReactorSourceMap.TakeRootMountSite());
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    [Fact]
    public void RootMountSite_OutOfOrderExitUnlinksOnlyThatScope()
    {
        ReactorSourceMap.Enabled = true;
        var outer = ReactorSourceMap.EnterRootMountSite("Outer.cs", 1);
        var inner = ReactorSourceMap.EnterRootMountSite("Inner.cs", 2);

        ReactorSourceMap.ExitRootMountSite(outer);
        Assert.Equal(1, ReactorSourceMap.OpenRootMountScopeCountForTest);
        Assert.Equal(new SourceLocation("Inner.cs", 2), ReactorSourceMap.TakeRootMountSite());

        ReactorSourceMap.ExitRootMountSite(inner);
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
        Assert.Null(ReactorSourceMap.TakeRootMountSite());
    }

    [Fact]
    public void RootMountSite_IsNotVisibleFromAnotherThread()
    {
        // Every entry point claims its own scope synchronously on the caller's thread
        // (Run included, before it starts WinUI), so scopes are per-thread: a mount on a
        // different thread must never pick up — or steal — this thread's site.
        ReactorSourceMap.Enabled = true;
        var token = ReactorSourceMap.EnterRootMountSite("Program.cs", 3);
        try
        {
            SourceLocation? claimedElsewhere = new SourceLocation("sentinel", -1);
            var thread = new Thread(() => claimedElsewhere = ReactorSourceMap.TakeRootMountSite());
            thread.Start();
            thread.Join();

            Assert.Null(claimedElsewhere);
            Assert.Equal(new SourceLocation("Program.cs", 3), ReactorSourceMap.TakeRootMountSite());
        }
        finally
        {
            ReactorSourceMap.ExitRootMountSite(token);
        }
    }

    [Fact]
    public void RootMountSite_ClaimedScopeHidesItselfFromNestedUninterceptedMounts()
    {
        // An entry point claims its scope first; anything it then runs (a configure
        // callback mounting a second host, a framework-created window) sees no site
        // rather than the outer call's line.
        ReactorSourceMap.Enabled = true;
        var token = ReactorSourceMap.EnterRootMountSite("App.cs", 40);
        try
        {
            Assert.Equal(new SourceLocation("App.cs", 40), ReactorSourceMap.TakeRootMountSite());
            Assert.Null(ReactorSourceMap.TakeRootMountSite());
        }
        finally
        {
            ReactorSourceMap.ExitRootMountSite(token);
        }
    }

    [Fact]
    public void RootMountSite_ExitNullIsANoOp()
    {
        ReactorSourceMap.ExitRootMountSite(null);
        ReactorSourceMap.ExitRootMountSite(new object());
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    // ── Component-boundary tagging ───────────────────────────────────────

    public static TheoryData<string> ComponentBoundaries => new() { "component", "func", "memo" };

    private static Element Boundary(string kind) => kind switch
    {
        "component" => new ComponentElement(typeof(ProbeComponent)),
        "func" => new FuncElement(_ => TextBlock("f")),
        "memo" => new MemoElement(_ => TextBlock("m")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(ComponentBoundaries))]
    public void NeedsTag_ComponentBoundaryIsTaggedOnlyWhileSourceMappingIsOn(string kind)
    {
        var element = Boundary(kind);
        Assert.Null(element.Extensions);

        ReactorSourceMap.Enabled = false;
        Assert.False(Reconciler.NeedsTag(element));

        ReactorSourceMap.Enabled = true;
        Assert.True(Reconciler.NeedsTag(element));
    }

    [Fact]
    public void NeedsTag_PlainLeafStaysUntaggedWithSourceMappingOn()
    {
        // PR #468's allocation win: an unstamped, callback-free leaf is never tagged,
        // whatever the flag says. Only component boundaries opt in.
        ReactorSourceMap.Enabled = true;
        var leaf = TextBlock("leaf");
        Assert.Null(leaf.Extensions);
        Assert.False(Reconciler.NeedsTag(leaf));
    }

    [Fact]
    public void NeedsTag_TransparentKeyedMemoIsNotABoundary()
    {
        // KeyedMemoElement mounts its factory output directly — there is no wrapper to tag.
        ReactorSourceMap.Enabled = true;
        var keyedMemo = new KeyedMemoElement("k", () => TextBlock("x"));
        Assert.Null(keyedMemo.Extensions);
        Assert.False(Reconciler.NeedsTag(keyedMemo with { Key = null }));
    }
}
