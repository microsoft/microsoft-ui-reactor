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

    // Unique per instance, so a test can pick its own fakes out of the process-wide
    // registry without depending on any other host-identifying member.
    private sealed class FakeHost(string label) : IReactorDiagnosticHost
    {
        public string Label { get; } = label + "#" + Guid.NewGuid().ToString("N");
        public bool Disposed { get; set; }
        public Component? Root { get; set; }
        public Func<RenderContext, Element>? RenderRoot { get; set; }
        public int TagRefreshes { get; private set; }

        public void TagComponentBoundaries() => TagRefreshes++;

        public ReactorHostInfo? CaptureDiagnosticInfo() => Disposed
            ? null
            : ReactorHostInfo.ForRoot(
                ReactorHostKind.HostControl, host: null, hostControl: null, reactorWindow: null,
                window: null, hostElement: null, rootControl: null,
                rootComponent: Root, rootRenderFunction: RenderRoot,
                mountSite: new SourceLocation(Label, 1, 0));
    }

    private sealed class ProbeComponent : Component
    {
        public override Element Render() => TextBlock("probe");
    }

    private static IEnumerable<ReactorHostInfo> InfosFor(params FakeHost[] hosts)
        => ReactorDiagnostics.GetHosts().Where(i => hosts.Any(h => i.MountSite?.FilePath == h.Label));

    private static string LabelOf(ReactorHostInfo info) => info.MountSite!.Value.FilePath.Split('#')[0];

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
            Assert.Equal(new[] { "a", "b" }, infos.Select(LabelOf));
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

            var info = Assert.Single(InfosFor(kept, removed, disposed));
            Assert.Equal("kept", LabelOf(info));
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

        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "The host registry rooted a host nothing else references.");
    }

    [Fact]
    public void Snapshot_PrunesCollectedHosts()
    {
        var weak = RegisterUnreferencedHost();
        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        Assert.False(weak.IsAlive);

        // No Register() call follows, so only Snapshot() can drop the dead entry.
        Assert.True(ReactorHostRegistry.DeadEntryCountForTest > 0);
        var live = ReactorHostRegistry.Snapshot();
        Assert.Equal(0, ReactorHostRegistry.DeadEntryCountForTest);
        Assert.Equal(live.Length, ReactorHostRegistry.EntryCountForTest);
    }

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RegisterUnreferencedHost()
    {
        var host = new FakeHost("transient");
        ReactorHostRegistry.Register(host);
        return new WeakReference(host);
    }

    [Fact]
    public void TurningSourceMappingOn_AsksEveryHostToTagItsMountedBoundaries()
    {
        ReactorSourceMap.Enabled = false;
        var a = new FakeHost("a");
        var b = new FakeHost("b");
        ReactorHostRegistry.Register(a);
        ReactorHostRegistry.Register(b);
        try
        {
            ReactorSourceMap.Enabled = true;
            Assert.Equal(1, a.TagRefreshes);
            Assert.Equal(1, b.TagRefreshes);

            // Only the off → on transition refreshes; a repeated set or a switch-off does not.
            ReactorSourceMap.Enabled = true;
            ReactorSourceMap.Enabled = false;
            Assert.Equal(1, a.TagRefreshes);

            ReactorSourceMap.Enabled = true;
            Assert.Equal(2, a.TagRefreshes);
        }
        finally
        {
            ReactorHostRegistry.Unregister(a);
            ReactorHostRegistry.Unregister(b);
        }
    }
    // ── Root names ───────────────────────────────────────────────────────

    [Fact]
    public void HostInfo_NamesAComponentRootByItsType()
    {
        var host = new FakeHost("typed") { Root = new ProbeComponent() };
        ReactorHostRegistry.Register(host);
        try
        {
            var info = Assert.Single(InfosFor(host));
            Assert.Equal("Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.ProbeComponent", info.RootComponentName);
            Assert.Null(info.RootRenderFunctionName);
        }
        finally
        {
            ReactorHostRegistry.Unregister(host);
        }
    }

    [Fact]
    public void HostInfo_NamesARenderFunctionRootByItsDeclaringMethod()
    {
        var captured = 0;
        var host = new FakeHost("render") { RenderRoot = _ => TextBlock($"r{captured}") };
        ReactorHostRegistry.Register(host);
        try
        {
            var info = Assert.Single(InfosFor(host));
            Assert.Null(info.RootComponentName);
            Assert.Equal(
                "Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.HostInfo_NamesARenderFunctionRootByItsDeclaringMethod (lambda)",
                info.RootRenderFunctionName);
        }
        finally
        {
            ReactorHostRegistry.Unregister(host);
        }
    }

    [Fact]
    public void HostInfo_ComponentRootWinsOverAnIgnoredRenderFunction()
    {
        // The hosts keep rendering a component root when a render function is mounted on
        // top, so the snapshot must describe the component and drop the render function.
        var host = new FakeHost("both") { Root = new ProbeComponent(), RenderRoot = _ => TextBlock("ignored") };
        ReactorHostRegistry.Register(host);
        try
        {
            var info = Assert.Single(InfosFor(host));
            Assert.EndsWith(".ProbeComponent", info.RootComponentName);
            Assert.Null(info.RootRenderFunctionName);
        }
        finally
        {
            ReactorHostRegistry.Unregister(host);
        }
    }

    private sealed class Outer<T>
    {
        public sealed class Inner<U> : Component
        {
            public override Element Render() => TextBlock("generic");
        }
    }

    [Fact]
    public void RootNames_TypeNamesAreReadable()
    {
        Assert.Equal("System.String", RootNames.ForType(typeof(string)));
        Assert.Equal(
            "Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.ProbeComponent",
            RootNames.ForType(typeof(ProbeComponent)));
        Assert.Equal(
            "System.Collections.Generic.Dictionary<System.String, System.Int32>",
            RootNames.ForType(typeof(Dictionary<string, int>)));
        Assert.Equal(
            "Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.Outer.Inner<System.Int32, System.String>",
            RootNames.ForType(typeof(Outer<int>.Inner<string>)));
        // Arrays keep their element's type arguments and their rank.
        Assert.Equal(
            "System.Collections.Generic.Dictionary<System.Collections.Generic.List<System.Int32>[], System.String[,]>",
            RootNames.ForType(typeof(Dictionary<List<int>[], string[,]>)));
        Assert.NotEqual(
            RootNames.ForType(typeof(List<List<int>[]>)),
            RootNames.ForType(typeof(List<List<string>[]>)));
    }

    public static TheoryData<string?, string, string> CompilerNames => new()
    {
        // Ordinary method group.
        { "MyApp.Shell", "Render", "MyApp.Shell.Render" },
        // Static lambda: cached in the <>c singleton.
        { "MyApp.Program+<>c", "<Main>b__0_0", "MyApp.Program.Main (lambda)" },
        // Capturing lambda: display class.
        { "MyApp.Program+<>c__DisplayClass0_0", "<Main>b__1", "MyApp.Program.Main (lambda)" },
        // Local function.
        { "MyApp.Program", "<Main>g__Render|0_1", "MyApp.Program.Main.Render" },
        // Lambda inside a local function.
        { "MyApp.Program+<>c", "<<Main>g__Render|0_1>b__0", "MyApp.Program.Main.Render (lambda)" },
        // Top-level statements: the entry point is "<Main>$".
        { "Program+<>c", "<<Main>$>b__0_0", "Program.Main (lambda)" },
        // Lambda inside an async method: closure class nested in the state machine's owner.
        { "MyApp.Page+<>c__DisplayClass3_0", "<LoadAsync>b__0", "MyApp.Page.LoadAsync (lambda)" },
        // Generic declaring type.
        { "MyApp.ListPage`1+<>c", "<Render>b__2_0", "MyApp.ListPage.Render (lambda)" },
        // No declaring type available.
        { null, "<Main>b__0_0", "Main (lambda)" },
    };

    [Theory]
    [MemberData(nameof(CompilerNames))]
    public void RootNames_CompilerGeneratedMethodNamesAreDemangled(string? declaringType, string method, string expected)
        => Assert.Equal(expected, RootNames.ForMethod(declaringType, method));

    private static Element NamedRender(RenderContext _) => TextBlock("named");

    [Fact]
    public void RootNames_ForDelegate_UsesTheRealDelegateMetadata()
    {
        // Real delegates, not strings: proves DiagnosticMethodInfo supplies what ForMethod
        // expects for the three shapes an app actually passes to Mount.
        Func<RenderContext, Element> methodGroup = NamedRender;
        Func<RenderContext, Element> lambda = _ => TextBlock("lambda");
        Element Local(RenderContext _) => TextBlock("local");
        Func<RenderContext, Element> local = Local;

        Assert.Equal(
            "Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.NamedRender",
            RootNames.ForDelegate(methodGroup));
        Assert.Equal(
            "Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.RootNames_ForDelegate_UsesTheRealDelegateMetadata (lambda)",
            RootNames.ForDelegate(lambda));
        Assert.Equal(
            "Microsoft.UI.Reactor.Tests.ReactorHostDiagnosticsTests.RootNames_ForDelegate_UsesTheRealDelegateMetadata.Local",
            RootNames.ForDelegate(local));
    }
    // ── Root mount call-site scopes ──────────────────────────────────────

    [Fact]
    public void RootMountSite_IsTakenWithTheFlagOff_ButOnlyKeptWhileItIsOn()
    {
            // Capture is unconditional (Run's site must survive a flag switched on later, as
            // `--devtools app` does); the host's store is what the flag gates.
            ReactorSourceMap.Enabled = false;
            var token = ReactorSourceMap.EnterRootMountSite("App.cs", 12, 9);
            try
            {
                var site = ReactorSourceMap.TakeRootMountSite();
                // The generator stamps the method name's column; it reaches the site.
                Assert.Equal(new SourceLocation("App.cs", 12, 9), site);
                Assert.Equal(9, site!.Value.ColumnNumber);

                Assert.Null(ReactorSourceMap.KeepIfEnabled(site));
                ReactorSourceMap.Enabled = true;
                Assert.Equal(site, ReactorSourceMap.KeepIfEnabled(site));
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
        var token = ReactorSourceMap.EnterRootMountSite("App.cs", 12, 0);
        try
        {
            Assert.Equal(new SourceLocation("App.cs", 12, 0), ReactorSourceMap.TakeRootMountSite());
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
        var outer = ReactorSourceMap.EnterRootMountSite("Program.cs", 5, 0);
        try
        {
            var inner = ReactorSourceMap.EnterRootMountSite("Startup.cs", 30, 0);
            try
            {
                Assert.Equal(new SourceLocation("Startup.cs", 30, 0), ReactorSourceMap.TakeRootMountSite());
            }
            finally
            {
                ReactorSourceMap.ExitRootMountSite(inner);
            }

            Assert.Equal(new SourceLocation("Program.cs", 5, 0), ReactorSourceMap.TakeRootMountSite());
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
        var token = ReactorSourceMap.EnterRootMountSite("Throws.cs", 7, 0);
        ReactorSourceMap.ExitRootMountSite(token);

        Assert.Null(ReactorSourceMap.TakeRootMountSite());
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    [Fact]
    public void RootMountSite_OutOfOrderExitUnlinksOnlyThatScope()
    {
        ReactorSourceMap.Enabled = true;
        var outer = ReactorSourceMap.EnterRootMountSite("Outer.cs", 1, 0);
        var inner = ReactorSourceMap.EnterRootMountSite("Inner.cs", 2, 0);

        ReactorSourceMap.ExitRootMountSite(outer);
        Assert.Equal(1, ReactorSourceMap.OpenRootMountScopeCountForTest);
        Assert.Equal(new SourceLocation("Inner.cs", 2, 0), ReactorSourceMap.TakeRootMountSite());

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
        var token = ReactorSourceMap.EnterRootMountSite("Program.cs", 3, 0);
        try
        {
            SourceLocation? claimedElsewhere = new SourceLocation("sentinel", -1, 0);
            var thread = new Thread(() => claimedElsewhere = ReactorSourceMap.TakeRootMountSite());
            thread.Start();
            thread.Join();

            Assert.Null(claimedElsewhere);
            Assert.Equal(new SourceLocation("Program.cs", 3, 0), ReactorSourceMap.TakeRootMountSite());
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
        var token = ReactorSourceMap.EnterRootMountSite("App.cs", 40, 0);
        try
        {
            Assert.Equal(new SourceLocation("App.cs", 40, 0), ReactorSourceMap.TakeRootMountSite());
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

    [Fact]
    public void RootMountSiteSlot_NeverTearsUnderConcurrentWrites()
    {
        // Two sites that differ in every word: a torn read would surface as a mixed pair
        // (A's file with B's line), a has-value with a null path, or similar.
        var a = new SourceLocation("A.cs", 111, 0);
        var b = new SourceLocation("Bbbbbbbb.cs", 222222, 0);
        var slot = new RootMountSiteSlot { Value = a };
        var stop = 0;
        var bad = 0;
        var reads = 0;
        using var started = new ManualResetEventSlim();

        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                var v = slot.Value;
                Interlocked.Increment(ref reads);
                started.Set();
                if (v is { } site && site != a && site != b) Interlocked.Increment(ref bad);
            }
        }) { IsBackground = true };
        reader.Start();

        int overlapped;
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "The reader thread never started.");
            var readsBefore = Volatile.Read(ref reads);
            var budget = global::System.Diagnostics.Stopwatch.StartNew();
            // Keep writing until the reader has overlapped plenty of writes (10 s cap).
            for (int i = 0; i < 200_000 || (Volatile.Read(ref reads) - readsBefore < 10_000 && budget.Elapsed < TimeSpan.FromSeconds(10)); i++)
                slot.Value = (i % 3) switch { 0 => a, 1 => b, _ => null };
            overlapped = Volatile.Read(ref reads) - readsBefore;
        }
        finally
        {
            Volatile.Write(ref stop, 1);
            reader.Join();
        }

        Assert.True(overlapped > 0, "No read overlapped the writes.");
        Assert.Equal(0, bad);
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
