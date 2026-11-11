using System.Diagnostics.Tracing;
using System.Text;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Reactor.Tests.Shared;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// The <c>Microsoft-UI-Reactor-Perf</c> provider (<see cref="ReactorPerfTrace"/>): its identity,
/// its TraceLogging wire schema, the enable gate every emit site relies on, and — the reason it
/// exists — that a <b>non-elevated private ETW session</b>, the kind <c>winapp perf</c> attaches
/// with, records it and TDH decodes it.
///
/// <para>The expected schema below is written out by hand from
/// <c>docs/reference/perf-trace-events.md</c> rather than read from the provider, so a change to
/// the wire format that the docs (and winapp's decoder) don't know about fails here.</para>
/// </summary>
[Collection(ReactorPerfTraceCollection.Name)]
public sealed class ReactorPerfTraceTests
{
    private sealed record Field(string Name, int InType);

    private sealed record EventSchema(ushort Id, string Name, byte Opcode, byte Level, ulong Keyword, Field[] Fields);

    private const int UnicodeString = 1, UInt32 = 8, UInt64 = 10, Bool32 = 13;

    // Fields appended in schema revision 2 sit after the revision-1 fields of the same event.
    private static readonly EventSchema[] DocumentedSchema =
    [
        new(1, "RenderPass", 1, 4, 0x1, [new("hostId", UInt64), new("requesterId", UInt64), new("requesterName", UnicodeString), new("requestCount", UInt32)]),
        new(2, "RenderPass", 2, 4, 0x1, [new("hostId", UInt64)]),
        new(3, "ComponentRender", 1, 4, 0x2, [new("componentId", UInt64), new("name", UnicodeString), new("reason", UnicodeString),
            new("callSiteFile", UnicodeString), new("callSiteLine", UInt32), new("callSiteColumn", UInt32)]),
        new(4, "ComponentRender", 2, 4, 0x2, [new("componentId", UInt64)]),
        new(5, "Reconcile", 1, 4, 0x1, [new("entry", UnicodeString), new("element", UnicodeString)]),
        new(6, "Reconcile", 2, 4, 0x1, [new("inserted", UInt32), new("removed", UInt32), new("moved", UInt32), new("replaced", UInt32)]),
        new(7, "Effects", 1, 4, 0x1, [new("componentId", UInt64)]),
        new(8, "Effects", 2, 4, 0x1, [new("componentId", UInt64)]),
        new(9, "ChildReconcile", 1, 5, 0x4, [new("oldCount", UInt32), new("newCount", UInt32)]),
        new(10, "ChildReconcile", 2, 5, 0x4, [new("inserted", UInt32), new("removed", UInt32), new("moved", UInt32), new("replaced", UInt32)]),
        new(11, "StateChange", 0, 5, 0x8, [new("componentId", UInt64), new("hook", UnicodeString), new("changed", Bool32)]),
        new(12, "KeyedListDiagnostic", 0, 5, 0x4, [new("kind", UnicodeString), new("bailedOut", Bool32)]),
    ];

    [EventSource(Name = ReactorPerfTrace.ProviderName)]
    private sealed class NameHashProbe : EventSource;

    [Fact]
    public void ProviderId_IsTheEventSourceStyleHashOfItsName()
    {
        // The hash xperf / tracelog / WPR compute for "*Microsoft-UI-Reactor-Perf", so a
        // consumer can enable the provider by name without knowing the GUID.
        Assert.Equal(EventSource.GetGuid(typeof(NameHashProbe)), ReactorPerfTrace.ProviderId);
        Assert.Equal(new Guid("0902a96c-4e92-5b91-4d2b-5a8058055c78"), ReactorPerfTrace.ProviderId);
    }

    [Fact]
    public void ProviderTraits_CarryTheName()
    {
        var blob = ReactorPerfTrace.Metadata.Provider.Span;
        Assert.Equal(blob.Length, BitConverter.ToUInt16(blob[..2]));
        Assert.Equal(ReactorPerfTrace.ProviderName + "\0", Encoding.UTF8.GetString(blob[2..]));
    }

    [Fact]
    public void EventMetadata_MatchesTheDocumentedSchema()
    {
        var blobs = new Dictionary<ushort, ReactorPerfTrace.Blob>
        {
            [1] = ReactorPerfTrace.Metadata.RenderPassStart,
            [2] = ReactorPerfTrace.Metadata.RenderPassStop,
            [3] = ReactorPerfTrace.Metadata.ComponentRenderStart,
            [4] = ReactorPerfTrace.Metadata.ComponentRenderStop,
            [5] = ReactorPerfTrace.Metadata.ReconcileStart,
            [6] = ReactorPerfTrace.Metadata.ReconcileStop,
            [7] = ReactorPerfTrace.Metadata.EffectsStart,
            [8] = ReactorPerfTrace.Metadata.EffectsStop,
            [9] = ReactorPerfTrace.Metadata.ChildReconcileStart,
            [10] = ReactorPerfTrace.Metadata.ChildReconcileStop,
            [11] = ReactorPerfTrace.Metadata.StateChange,
            [12] = ReactorPerfTrace.Metadata.KeyedListDiagnostic,
        };
        Assert.Equal(DocumentedSchema.Select(s => s.Id), blobs.Keys.Order());

        foreach (var expected in DocumentedSchema)
        {
            var (name, fields) = ParseEventMetadata(blobs[expected.Id].Span);
            Assert.Equal(expected.Name, name);
            Assert.Equal(expected.Fields, fields);
        }
    }

    // UINT16 size (counting itself), tag byte, nul-terminated name, then per field a
    // nul-terminated name and an in-type byte (TraceLoggingProvider.h).
    private static (string Name, Field[] Fields) ParseEventMetadata(ReadOnlySpan<byte> blob)
    {
        Assert.Equal(blob.Length, BitConverter.ToUInt16(blob[..2]));
        Assert.Equal(0, blob[2]);
        int position = 3;
        string name = ReadString(blob, ref position);
        var fields = new List<Field>();
        while (position < blob.Length)
        {
            string fieldName = ReadString(blob, ref position);
            int inType = blob[position++];
            Assert.True((inType & 0x80) == 0, "no out-type chain is expected");
            fields.Add(new Field(fieldName, inType));
        }
        return (name, fields.ToArray());
    }

    private static string ReadString(ReadOnlySpan<byte> blob, ref int position)
    {
        int end = blob[position..].IndexOf((byte)0);
        Assert.True(end >= 0, "unterminated string");
        string value = Encoding.UTF8.GetString(blob.Slice(position, end));
        position += end + 1;
        return value;
    }

    [Fact]
    public void EnableGate_FollowsLevelAndKeywordsLikeTraceLogging()
    {
        try
        {
            // Informational + Pass: the pass spans only.
            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Pass, 0);
            Assert.True(ReactorPerfTrace.PassEnabled);
            Assert.False(ReactorPerfTrace.ComponentEnabled);
            Assert.False(ReactorPerfTrace.ChildrenEnabled);
            Assert.False(ReactorPerfTrace.StateEnabled);

            // Informational with every keyword still leaves the Verbose events off.
            ReactorPerfTrace.ApplyControlCode(1, 4, 0xF, 0);
            Assert.True(ReactorPerfTrace.ComponentEnabled);
            Assert.False(ReactorPerfTrace.ChildrenEnabled);
            Assert.False(ReactorPerfTrace.StateEnabled);

            ReactorPerfTrace.ApplyControlCode(1, 5, 0xF, 0);
            Assert.True(ReactorPerfTrace.ChildrenEnabled);
            Assert.True(ReactorPerfTrace.StateEnabled);

            // MatchAllKeyword: an event needs every "all" bit.
            ReactorPerfTrace.ApplyControlCode(1, 5, 0xF, 0x3);
            Assert.False(ReactorPerfTrace.IsEnabled(5, ReactorPerfTrace.Keywords.Children));
            Assert.True(ReactorPerfTrace.IsEnabled(5, 0x3));

            // MatchAllKeyword still applies when MatchAnyKeyword is 0.
            ReactorPerfTrace.ApplyControlCode(1, 5, 0, ReactorPerfTrace.Keywords.Pass);
            Assert.True(ReactorPerfTrace.PassEnabled);
            Assert.False(ReactorPerfTrace.ComponentEnabled);
            Assert.False(ReactorPerfTrace.ChildrenEnabled);
            Assert.False(ReactorPerfTrace.StateEnabled);

            // Level 0 and MatchAnyKeyword 0 mean "everything".
            ReactorPerfTrace.ApplyControlCode(1, 0, 0, 0);
            Assert.True(ReactorPerfTrace.StateEnabled);
            Assert.True(ReactorPerfTrace.PassEnabled);

            // Capture-state requests leave the state alone; disable turns everything off.
            ReactorPerfTrace.ApplyControlCode(2, 1, 0x1, 0);
            Assert.True(ReactorPerfTrace.StateEnabled);
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
            Assert.False(ReactorPerfTrace.PassEnabled);
            Assert.False(ReactorPerfTrace.IsEnabled(255, 0));
        }
        finally
        {
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
        }
    }

    [Fact]
    public void RenderContextId_IsAssignedOnce_AndSharedWithTheRoot()
    {
        var ctx = new RenderContext();
        Assert.Equal(0, ctx.DiagnosticId);
        long id = ctx.EnsureDiagnosticId();
        Assert.NotEqual(0, id);
        Assert.Equal(id, ctx.EnsureDiagnosticId());
        Assert.Equal(id, ctx.DiagnosticId);

        // The host root adopts its context's id, so its spans and its state changes agree.
        var root = new RootRenderDiagnostics();
        Assert.Equal(id, root.EnsureId(ctx));
        Assert.Equal(id, root.IdForTests);
        // Ids are per context.
        Assert.NotEqual(id, new RenderContext().EnsureDiagnosticId());
    }

    [Fact]
    public void RootPeekReason_DoesNotConsumeTheFirstRender()
    {
        var root = new RootRenderDiagnostics();
        Assert.Equal("mount", root.PeekReason(hotReloadRender: false, forcePending: false));
        Assert.Equal("mount", root.PeekReason(hotReloadRender: false, forcePending: false));
        root.TraceRendered("Root", hotReloadRender: false, forcePending: false, elapsedMilliseconds: 0);
        Assert.Equal("state", root.PeekReason(hotReloadRender: false, forcePending: false));
        Assert.Equal("forced", root.PeekReason(hotReloadRender: false, forcePending: true));
        root.MarkHotReloadRetry();
        Assert.Equal("hotReload", root.PeekReason(hotReloadRender: false, forcePending: false));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\src\App\MainPage.cs", "MainPage.cs")]
    [InlineData("/_/src/App/Pages/Row.cs", "Row.cs")]
    [InlineData(@"C:\mixed/sep\Grid.cs", "Grid.cs")]
    [InlineData("Bare.cs", "Bare.cs")]
    [InlineData("", "")]
    public void CallSiteFile_IsTheFileNameWithoutItsDirectories(string path, string expected)
        => Assert.Equal(expected, path[ReactorPerfTrace.FileNameStart(path)..]);

    private static Element NamedRender(RenderContext ctx) => TextBlock("named");

    private sealed class NamedComponent : Component
    {
        public override Element Render() => TextBlock("class");
    }

    [Fact]
    public void FunctionAndMemoComponents_AreNamedByTheirRenderFunction_AsDevToolsNamesThem()
    {
        // A method group names the method; a lambda names the factory and the method it is
        // written in. Never the element type, which every function component shares.
        Assert.Equal("ReactorPerfTraceTests.NamedRender", ComponentNames.ForRenderFunction(RenderEachTime(NamedRender)));
        Assert.Equal("RenderEachTime in ReactorPerfTraceTests." + nameof(FunctionAndMemoComponents_AreNamedByTheirRenderFunction_AsDevToolsNamesThem),
            ComponentNames.ForRenderFunction(RenderEachTime(_ => TextBlock("lambda"))));
        Assert.Equal("Memo in ReactorPerfTraceTests." + nameof(FunctionAndMemoComponents_AreNamedByTheirRenderFunction_AsDevToolsNamesThem),
            ComponentNames.ForRenderFunction(Memo(_ => TextBlock("memo"))));
        Assert.Equal("render", ComponentNames.ForRenderFunction(null, ComponentNames.RootRenderFunctionFallback));
        // Anything else keeps its ComponentNames.For name.
        Assert.Equal(nameof(NamedComponent), ComponentNames.ForRenderFunction(Component<NamedComponent>()));
    }

    [Fact]
    public void PerfComponentName_ResolvesAFunctionComponentOnce_AndClassComponentsEveryTime()
    {
        var func = new Reconciler.ComponentNode { Context = new RenderContext() };
        Assert.Equal("ReactorPerfTraceTests.NamedRender", Reconciler.PerfComponentName(func, RenderEachTime(NamedRender)));
        // Kept for the component's lifetime, even if a later render passes another function.
        Assert.Equal("ReactorPerfTraceTests.NamedRender", Reconciler.PerfComponentName(func, RenderEachTime(_ => TextBlock("other"))));
        Assert.Equal("ReactorPerfTraceTests.NamedRender", func.PerfName);

        var memo = new Reconciler.ComponentNode { Context = new RenderContext() };
        Assert.StartsWith("Memo in ", Reconciler.PerfComponentName(memo, Memo(_ => TextBlock("m"))));

        var cls = new Reconciler.ComponentNode { Component = new NamedComponent() };
        Assert.Equal(nameof(NamedComponent), Reconciler.PerfComponentName(cls, Component<NamedComponent>()));
        Assert.Null(cls.PerfName);
    }

    [Fact]
    public void RenderPassRequests_TheInnermostClaimAndTheFirstRequestWin()
    {
        object inner = new(), outer = new(), later = new();
        var requests = new RenderPassRequests();
        try
        {
            // Nobody listening: a request records nothing, claimed or not.
            Assert.True(RenderPassRequests.Claim(inner));
            requests.Note();
            RenderPassRequests.Release();
            Assert.False(requests.HasPending);

            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Pass, 0);

            // A request climbing through its ancestors: the first claim on the thread holds.
            Assert.True(RenderPassRequests.Claim(inner));
            Assert.False(RenderPassRequests.Claim(outer));
            requests.Note();
            RenderPassRequests.Release();
            Assert.Null(RenderPassRequests.CurrentForTests);

            // An unclaimed request counts but names nobody; a later claimed one does not displace the first.
            requests.Note();
            Assert.True(RenderPassRequests.Claim(later));
            requests.Note();
            RenderPassRequests.Release();
            Assert.True(requests.HasPending);

            Assert.Same(inner, requests.Take(out uint count));
            Assert.Equal(3u, count);
            Assert.False(requests.HasPending);
            Assert.Null(requests.Take(out count));
            Assert.Equal(0u, count);

            // Requests that name nobody still leave something to take.
            requests.Note();
            Assert.True(requests.HasPending);
            Assert.Null(requests.Take(out count));
            Assert.Equal(1u, count);

            // An untraced pass discards what a session noted before it stopped.
            Assert.True(RenderPassRequests.Claim(inner));
            requests.Note();
            RenderPassRequests.Release();
            requests.DiscardStale();
            Assert.False(requests.HasPending);
            Assert.Null(requests.Take(out count));
            Assert.Equal(0u, count);
        }
        finally
        {
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
            RenderPassRequests.Release();
        }
    }

    [Fact]
    public void RootPerfName_IsResolvedOncePerRoot_AndResetWithIt()
    {
        var root = new RootRenderDiagnostics();
        Assert.Equal("ReactorPerfTraceTests.NamedRender", root.PerfName(null, (Func<RenderContext, Element>)NamedRender));
        Assert.Equal("ReactorPerfTraceTests.NamedRender", root.PerfName(null, (Func<RenderContext, Element>)(_ => TextBlock("x"))));
        // A new root (Reset) resolves its own name.
        root.Reset();
        Assert.Equal("render in ReactorPerfTraceTests." + nameof(RootPerfName_IsResolvedOncePerRoot_AndResetWithIt),
            root.PerfName(null, (Func<RenderContext, Element>)(_ => TextBlock("y"))));
        // A class root is named by its type every time.
        Assert.Equal(nameof(NamedComponent), root.PerfName(new NamedComponent(), null));
    }

    [Fact]
    public void InvokeAs_ClaimsOnlyWhileThePassKeywordIsOn_AndAlwaysReleases()
    {
        object requester = new();
        object? seen = "unset";
        try
        {
            RenderPassRequests.InvokeAs(requester, () => seen = RenderPassRequests.CurrentForTests);
            Assert.Null(seen);

            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Component, 0);
            RenderPassRequests.InvokeAs(requester, () => seen = RenderPassRequests.CurrentForTests);
            Assert.Null(seen);

            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Pass, 0);
            RenderPassRequests.InvokeAs(requester, () => seen = RenderPassRequests.CurrentForTests);
            Assert.Same(requester, seen);
            Assert.Null(RenderPassRequests.CurrentForTests);

            // Nested: the inner (first) requester stays; the outer leaves its claim alone.
            object other = new();
            RenderPassRequests.InvokeAs(requester, () =>
                RenderPassRequests.InvokeAs(other, () => seen = RenderPassRequests.CurrentForTests));
            Assert.Same(requester, seen);

            // A throwing request still releases the claim.
            Assert.Throws<InvalidOperationException>(() =>
                RenderPassRequests.InvokeAs(requester, () => throw new InvalidOperationException()));
            Assert.Null(RenderPassRequests.CurrentForTests);
        }
        finally
        {
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
            RenderPassRequests.Release();
        }
    }

    [Fact]
    public void PassStart_NamesTheRequester_TheRoot_OrNobody()
    {
        ReactorPerfTrace.EnsureRegistered();
        ulong hostId = 0xFACE_0000_0000_0000UL | (uint)Random.Shared.Next();
        var rootContext = new RenderContext();
        Func<RenderContext, Element> rootRender = NamedRender;
        var child = new Reconciler.ComponentNode { Context = new RenderContext(), Element = Memo(_ => TextBlock("c")) };

        var events = Capture(level: 4, keywords: ReactorPerfTrace.Keywords.Pass, () =>
        {
            var root = new RootRenderDiagnostics();
            var requests = new RenderPassRequests();

            RenderPassRequests.InvokeAs(child, requests.Note);
            root.RequestFromRoot(requests.Note);
            root.WritePassStart(hostId, requests, root: null, rootContext, rootRender);

            root.RequestFromRoot(requests.Note);
            root.WritePassStart(hostId, requests, root: null, rootContext, rootRender);

            requests.Note();
            root.WritePassStart(hostId, requests, root: null, rootContext, rootRender);

            // A root claim with no live root (disposed between the request and the pass).
            root.RequestFromRoot(requests.Note);
            root.WritePassStart(hostId, requests, root: null, funcContext: null, renderFunc: null);

            // A request from a root that was replaced (remounted) before the pass names nobody.
            new RootRenderDiagnostics().RequestFromRoot(requests.Note);
            root.WritePassStart(hostId, requests, root: null, rootContext, rootRender);

            // An untraced pass drops what was noted; a traced one writes.
            RenderPassRequests.InvokeAs(child, requests.Note);
            root.BeginPass(perfPass: false, hostId, requests, root: null, rootContext, rootRender);
            root.BeginPass(perfPass: true, hostId, requests, root: null, rootContext, rootRender);
        });

        var starts = events.Where(e => e.Id == 1 && Equals(e.Fields["hostId"], hostId))
            .Select(e => ((ulong)e.Fields["requesterId"], (string)e.Fields["requesterName"], (uint)e.Fields["requestCount"]))
            .ToList();
        Assert.Equal(
            new[]
            {
                ((ulong)child.DiagnosticId, child.PerfName!, 2u),
                ((ulong)rootContext.DiagnosticId, "ReactorPerfTraceTests.NamedRender", 1u),
                (0UL, "", 1u),
                (0UL, "", 1u),
                (0UL, "", 1u),
                (0UL, "", 0u),
            },
            starts);
        // The ids compared above were assigned by the code under test, not left at 0.
        Assert.NotEqual(0, child.DiagnosticId);
        Assert.NotEqual(0, rootContext.DiagnosticId);
        Assert.StartsWith("Memo in ", child.PerfName);
    }

    [Fact]
    public void Note_ConsumesTheClaim_SoAPassItRunsSynchronouslyIsNotCreditedToIt()
    {
        object outer = new(), inner = new();
        var first = new RenderPassRequests();
        var nested = new RenderPassRequests();
        try
        {
            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Pass, 0);
            // A host's first render runs inside the request that scheduled it; a request made
            // during that pass (here: after Note, still inside the outer request) is not the
            // outer one's.
            RenderPassRequests.InvokeAs(outer, () =>
            {
                first.Note();
                Assert.Null(RenderPassRequests.CurrentForTests);
                nested.Note();
            });
            Assert.Same(outer, first.Take(out uint firstCount));
            Assert.Equal(1u, firstCount);
            Assert.Null(nested.Take(out uint nestedCount));
            Assert.Equal(1u, nestedCount);

            RenderPassRequests.InvokeAs(outer, () =>
            {
                first.Note();
                RenderPassRequests.InvokeAs(inner, nested.Note);
            });
            Assert.Same(outer, first.Take(out firstCount));
            Assert.Same(inner, nested.Take(out nestedCount));
            Assert.Equal(1u, nestedCount);
            Assert.Null(RenderPassRequests.CurrentForTests);
        }
        finally
        {
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
            RenderPassRequests.Release();
        }
    }

    [Fact]
    public void RenderPassRequests_AnUnclaimedFirstRequest_NamesNobody_EvenIfAClaimedOneCoalesces()
    {
        object later = new();
        var requests = new RenderPassRequests();
        try
        {
            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Pass, 0);
            // A host-level request scheduled the pass; a component's request only joined it.
            requests.Note();
            RenderPassRequests.InvokeAs(later, requests.Note);
            Assert.Null(requests.Take(out uint count));
            Assert.Equal(2u, count);

            // The next pass starts clean: its first request is the component's.
            RenderPassRequests.InvokeAs(later, requests.Note);
            requests.Note();
            Assert.Same(later, requests.Take(out count));
            Assert.Equal(2u, count);
        }
        finally
        {
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
            RenderPassRequests.Release();
        }
    }

    [Fact]
    public async Task RenderPassRequests_ATakeRacingANote_NeverSplitsTheRequest()
    {
        // A worker-thread request is counted, then its requester recorded; a pass starting on the
        // UI thread in between must wait, or it takes the count without the requester (and the
        // next pass reports the requester with no count).
        object requester = new();
        var requests = new RenderPassRequests();
        Task<(object? First, uint Count)>? take = null;
        bool tookInsideNote = false;
        requests.BetweenCountAndRequesterForTests = () =>
        {
            take = Task.Run(() => (requests.Take(out uint c), c));
            tookInsideNote = SpinWait.SpinUntil(() => take.IsCompleted, 200);
        };
        try
        {
            ReactorPerfTrace.ApplyControlCode(1, 4, ReactorPerfTrace.Keywords.Pass, 0);
            RenderPassRequests.InvokeAs(requester, requests.Note);
        }
        finally
        {
            requests.BetweenCountAndRequesterForTests = null;
            ReactorPerfTrace.ApplyControlCode(0, 0, 0, 0);
            RenderPassRequests.Release();
        }

        Assert.NotNull(take);
        var (first, count) = await take;
        Assert.False(tookInsideNote);
        Assert.Same(requester, first);
        Assert.Equal(1u, count);
        Assert.False(requests.HasPending);
    }

    [Fact]
    public void RootSpan_CarriesTheMountSiteAndTheRenderFunctionName()
    {
        ReactorPerfTrace.EnsureRegistered();
        var context = new RenderContext();
        Func<RenderContext, Element> render = NamedRender;
        var events = Capture(level: 4, keywords: ReactorPerfTrace.Keywords.Component, () =>
        {
            var root = new RootRenderDiagnostics();
            using (root.BeginPerfSpan(null, context, render, new SourceLocation("/_/samples/App/Program.cs", 12, 5), false, false)) { }
            using (root.BeginPerfSpan(null, context, render, mountSite: null, false, false)) { }
        });

        var starts = events.Where(e => e.Id == 3 && Equals(e.Fields["componentId"], (ulong)context.DiagnosticId)).ToList();
        Assert.Equal(2, starts.Count);
        Assert.All(starts, e => Assert.Equal("ReactorPerfTraceTests.NamedRender", e.Fields["name"]));
        Assert.Equal(("Program.cs", 12u, 5u),
            ((string)starts[0].Fields["callSiteFile"], (uint)starts[0].Fields["callSiteLine"], (uint)starts[0].Fields["callSiteColumn"]));
        Assert.Equal(("", 0u, 0u),
            ((string)starts[1].Fields["callSiteFile"], (uint)starts[1].Fields["callSiteLine"], (uint)starts[1].Fields["callSiteColumn"]));
    }

    [Fact]
    public void PrivateSession_RecordsEveryEvent_AndTdhDecodesTheDocumentedSchema()
    {
        ReactorPerfTrace.EnsureRegistered();
        Assert.True(ReactorPerfTrace.IsRegisteredForTests);

        // Unique values so concurrent emitters (none expected: the collection is serial)
        // can never satisfy an assertion.
        ulong hostId = 0xFEED_0000_0000_0000UL | (uint)Random.Shared.Next();
        ulong componentId = hostId + 1;
        string name = "PerfTraceProbe" + hostId.ToString("x");

        var events = Capture(level: 5, keywords: 0xF, () =>
        {
            ReactorPerfTrace.RenderPassStart(hostId, componentId, name, 3);
            ReactorPerfTrace.ComponentRenderStart(componentId, name, "mount",
                new SourceLocation(@"C:\Users\someone\src\App\Pages\" + name + ".cs", 42, 7));
            ReactorPerfTrace.ReconcileStart("reconcile", name);
            ReactorPerfTrace.ChildReconcileStart(3, 4);
            ReactorPerfTrace.ChildReconcileStop(1, 2, 3, 4);
            ReactorPerfTrace.KeyedListDiagnostic("duplicateKey", true);
            ReactorPerfTrace.ReconcileStop(5, 6, 7, 8);
            ReactorPerfTrace.ComponentRenderStop(componentId);
            ReactorPerfTrace.EffectsStart(componentId);
            ReactorPerfTrace.EffectsStop(componentId);
            ReactorPerfTrace.StateChange(componentId, "UseState", true);
            ReactorPerfTrace.RenderPassStop(hostId);
        }, out long written);
        Assert.Equal(12, written);

        int firstIndex = events.FindIndex(e => e.Id == 1 && Equals(e.Fields.GetValueOrDefault("hostId"), hostId));
        Assert.True(firstIndex >= 0, Describe(events));
        var ours = events.Skip(firstIndex).Take(12).ToList();
        Assert.Equal(new ushort[] { 1, 3, 5, 9, 10, 12, 6, 4, 7, 8, 11, 2 }, ours.Select(e => e.Id));

        foreach (var e in ours)
        {
            var expected = DocumentedSchema.Single(s => s.Id == e.Id);
            Assert.Null(e.DecodeError);
            Assert.True(e.IsTraceLogging, $"event {e.Id} was not decoded from TraceLogging metadata");
            Assert.Equal(expected.Name, e.Name);
            Assert.Equal(expected.Opcode, e.Opcode);
            Assert.Equal(expected.Level, e.Level);
            Assert.Equal(expected.Keyword, e.Keyword);
            Assert.Equal(expected.Fields.Select(f => (f.Name, f.InType)), e.Schema);
            Assert.Equal(Environment.ProcessId, e.ProcessId);
        }

        Assert.Equal(hostId, ours[0].Fields["hostId"]);
        Assert.Equal(componentId, ours[0].Fields["requesterId"]);
        Assert.Equal(name, ours[0].Fields["requesterName"]);
        Assert.Equal(3u, ours[0].Fields["requestCount"]);
        Assert.Equal(componentId, ours[1].Fields["componentId"]);
        Assert.Equal(name, ours[1].Fields["name"]);
        Assert.Equal("mount", ours[1].Fields["reason"]);
        // The directories (which here carry a user name) never reach the payload.
        Assert.Equal(name + ".cs", ours[1].Fields["callSiteFile"]);
        Assert.Equal(42u, ours[1].Fields["callSiteLine"]);
        Assert.Equal(7u, ours[1].Fields["callSiteColumn"]);
        Assert.Equal("reconcile", ours[2].Fields["entry"]);
        Assert.Equal(name, ours[2].Fields["element"]);
        Assert.Equal(3u, ours[3].Fields["oldCount"]);
        Assert.Equal(4u, ours[3].Fields["newCount"]);
        Assert.Equal(new object[] { 1u, 2u, 3u, 4u },
            new[] { "inserted", "removed", "moved", "replaced" }.Select(k => ours[4].Fields[k]));
        Assert.Equal("duplicateKey", ours[5].Fields["kind"]);
        Assert.Equal(true, ours[5].Fields["bailedOut"]);
        Assert.Equal(new object[] { 5u, 6u, 7u, 8u },
            new[] { "inserted", "removed", "moved", "replaced" }.Select(k => ours[6].Fields[k]));
        Assert.Equal(componentId, ours[7].Fields["componentId"]);
        Assert.Equal(componentId, ours[8].Fields["componentId"]);
        Assert.Equal(componentId, ours[10].Fields["componentId"]);
        Assert.Equal("UseState", ours[10].Fields["hook"]);
        Assert.Equal(true, ours[10].Fields["changed"]);
        Assert.Equal(hostId, ours[11].Fields["hostId"]);
    }

    [Fact]
    public void PrivateSession_KeywordMask_GatesTheEmitSites()
    {
        ReactorPerfTrace.EnsureRegistered();
        bool passDuring = false, componentDuring = false, stateDuring = false;
        Capture(level: 4, keywords: ReactorPerfTrace.Keywords.Pass, () =>
        {
            passDuring = ReactorPerfTrace.PassEnabled;
            componentDuring = ReactorPerfTrace.ComponentEnabled;
            stateDuring = ReactorPerfTrace.StateEnabled;
        });

        // The session's own enable reached the gate (positive control), and only for what it
        // asked for; once it stops, the gate closes again.
        Assert.True(passDuring);
        Assert.False(componentDuring);
        Assert.False(stateDuring);
        Assert.False(ReactorPerfTrace.PassEnabled);
    }

    [Fact]
    public void StateSetter_ReportsItsOwner_OnlyWhileTraced()
    {
        ReactorPerfTrace.EnsureRegistered();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, set) = ctx.UseState(0);

        // Nobody listening: the setter must not even assign the component an id.
        set(1);
        Assert.Equal(0, ctx.DiagnosticId);

        var events = Capture(level: 5, keywords: ReactorPerfTrace.Keywords.State, () =>
        {
            set(2);
            set(2);
        }, out long written);

        long id = ctx.DiagnosticId;
        Assert.NotEqual(0, id);
        var mine = events.Where(e => e.Id == 11 && Equals(e.Fields["componentId"], (ulong)id)).ToList();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, e => Assert.Equal("UseState", e.Fields["hook"]));
        Assert.Equal(new object[] { true, false }, mine.Select(e => e.Fields["changed"]));
        Assert.Equal(2, written);
    }

    [Fact]
    public void StateSetters_EveryHookForm_ReportsItsOwner()
    {
        ReactorPerfTrace.EnsureRegistered();
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var (_, setLocked) = ctx.UseState(0, threadSafe: true);
        var (_, update) = ctx.UseReducer(0);
        var (_, updateLocked) = ctx.UseReducer(0, threadSafe: true);
        var (_, dispatch) = ctx.UseReducer<int, int>(static (s, a) => s + a, 0);
        var (_, dispatchLocked) = ctx.UseReducer<int, int>(static (s, a) => s + a, 0, threadSafe: true);
        var (_, setPersisted) = ctx.UsePersisted("perf-trace-test-" + Guid.NewGuid().ToString("N"), 0, PersistedScope.Application);

        // Nobody listening: no hook form writes, or even assigns the component an id.
        long untracedBefore = ReactorPerfTrace.WriteCountForTests;
        setLocked(0);
        updateLocked(static v => v);
        dispatchLocked(0);
        update(static v => v);
        dispatch(0);
        setPersisted(0);
        Assert.Equal(untracedBefore, ReactorPerfTrace.WriteCountForTests);
        Assert.Equal(0, ctx.DiagnosticId);
        var events = Capture(level: 5, keywords: ReactorPerfTrace.Keywords.State, () =>
        {
            // Thread-safe hooks write from the caller's thread, here a dedicated one.
            var worker = new Thread(() =>
            {
                setLocked(1);
                updateLocked(static v => v);
                dispatchLocked(1);
            });
            worker.Start();
            worker.Join();
            update(static v => v + 1);
            dispatch(0);
            setPersisted(3);
        }, out long written);

        long id = ctx.DiagnosticId;
        Assert.NotEqual(0, id);
        var mine = events.Where(e => e.Id == 11).ToList();
        Assert.All(mine, e => Assert.Equal((ulong)id, e.Fields["componentId"]));
        Assert.Equal(
            new (string, bool)[] { ("UseState", true), ("UseReducer", false), ("UseReducer", true), ("UseReducer", true), ("UseReducer", false), ("UsePersisted", true) },
            mine.Select(e => ((string)e.Fields["hook"], (bool)e.Fields["changed"])));
        // The thread-safe writes are reported on the worker thread that made them.
        Assert.All(mine.Take(3), e => Assert.NotEqual(mine[3].ThreadId, e.ThreadId));
        Assert.Equal(6, written);
    }

    [Fact]
    public void Metadata_IsRootedForTheProcessLifetime()
    {
        // The blobs are read by pointer on every write; a pinned array is never moved but is
        // still collected once unreachable. Each blob must therefore hold its array (the static
        // fields that hold the blobs then root it), not just its address.
        Assert.True(global::System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ReactorPerfTrace.Blob>(),
            "Blob must hold a managed reference to its pinned array");

        var live = new[]
        {
            ReactorPerfTrace.Metadata.Provider, ReactorPerfTrace.Metadata.RenderPassStart, ReactorPerfTrace.Metadata.ComponentRenderStart,
            ReactorPerfTrace.Metadata.ReconcileStart, ReactorPerfTrace.Metadata.ChildReconcileStop, ReactorPerfTrace.Metadata.StateChange,
        };
        var expected = live.Select(b => b.Span.ToArray()).ToArray();
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        // Let the pinned heap reuse anything that was freed.
        var filler = Enumerable.Range(0, 4096).Select(_ =>
        {
            var a = GC.AllocateArray<byte>(64, pinned: true);
            a.AsSpan().Fill(0xCD);
            return a;
        }).ToList();

        unsafe
        {
            for (int i = 0; i < live.Length; i++)
                Assert.Equal(expected[i], new ReadOnlySpan<byte>(live[i].Pointer, (int)live[i].Size).ToArray());
        }
        GC.KeepAlive(filler);
    }

    [EventSource(Name = "Microsoft-UI-Reactor-Tests-PrivateSessionProbe")]
    private sealed class PrivateSessionProbe : EventSource
    {
        public PrivateSessionProbe() : base(EventSourceSettings.EtwSelfDescribingEventFormat) { }

        [Event(1, Level = EventLevel.Informational)]
        public void Ping(int value) => WriteEvent(1, value);
    }

    [Fact]
    public void PrivateSession_RecordsThisProvider_ButNeverEnablesAnEventSource()
    {
        // The premise of the provider, and proof the capture really is private: a system
        // session would enable the probe too. If a future .NET runtime enables EventSources
        // for private sessions this fails; then the docs' "as of .NET 10" notes need updating.
        ReactorPerfTrace.EnsureRegistered();
        using var probe = new PrivateSessionProbe();
        Guid probeId = EventSource.GetGuid(typeof(PrivateSessionProbe));
        string etl = global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(),
            "reactor-perf-" + Guid.NewGuid().ToString("N") + ".etl");
        try
        {
            EtwPrivateCapture capture;
            try
            {
                capture = EtwPrivateCapture.Start(etl);
            }
            catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                Assert.Skip("This account may not start an ETW session (not elevated and not in Performance Log Users).");
                throw;
            }
            bool probeEnabled;
            using (capture)
            {
                capture.Enable(probeId, 5, ulong.MaxValue);
                capture.Enable(ReactorPerfTrace.ProviderId, 5, ulong.MaxValue);
                Assert.True(ReactorPerfTrace.IsEnabled(5, ReactorPerfTrace.Keywords.Pass), "the private session's enable never reached the provider");
                probeEnabled = probe.IsEnabled();
                probe.Ping(1);
                ReactorPerfTrace.ReconcileStart("reconcile", "Probe");
                ReactorPerfTrace.ReconcileStop(0, 0, 0, 0);
                capture.Stop();
            }

            Assert.False(probeEnabled);
            Assert.Empty(EtwPrivateCapture.Read(etl, probeId));
            Assert.Equal(2, EtwPrivateCapture.Read(etl, ReactorPerfTrace.ProviderId).Count(e => e.Name == "Reconcile"));
        }
        finally
        {
            try { global::System.IO.File.Delete(etl); }
            catch (global::System.IO.IOException ex)
            {
                // Best effort: a leftover temp ETL must not fail the test.
                global::System.Diagnostics.Trace.WriteLine("could not delete " + etl + ": " + ex.Message);
            }
        }
    }

    [Fact]
    public void KeyedListDiagnostics_ReportBailoutsAndSiblingDuplicates_OnlyWhileTraced()
    {
        ReactorPerfTrace.EnsureRegistered();
        var children = new[] { "d", "e", "d" }.Select(k => (Element?)(TextBlock("x") with { Key = k })).ToArray();
        var state = new Microsoft.UI.Reactor.Core.Internal.ReactorListState();
        state.Reset(new (int, string)[] { (0, "a"), (1, "b") });

        // Untraced: the diagnostics still run, but nothing reaches ETW.
        long before = ReactorPerfTrace.WriteCountForTests;
        Microsoft.UI.Reactor.Core.Internal.KeyedListDiff.ReportDuplicateSiblingKeys(children, reconciler: null, new object());
        var untraced = new Microsoft.UI.Reactor.Core.Internal.ReactorListState();
        untraced.Reset(new (int, string)[] { (0, "a"), (1, "b") });
        Assert.True(Microsoft.UI.Reactor.Core.Internal.KeyedListDiff.Apply(untraced, new[] { "a", "b", "a" }, static (k, _) => k).Bailout);
        Assert.Equal(before, ReactorPerfTrace.WriteCountForTests);

        var events = Capture(level: 5, keywords: ReactorPerfTrace.Keywords.Children, () =>
        {
            var stats = Microsoft.UI.Reactor.Core.Internal.KeyedListDiff.Apply(state, new[] { "a", "b", "a" }, static (k, _) => k);
            Assert.True(stats.Bailout);
            Microsoft.UI.Reactor.Core.Internal.KeyedListDiff.ReportDuplicateSiblingKeys(children, reconciler: null, new object());
            // A null key is substituted, not a bailout.
            var nullKeyed = new Microsoft.UI.Reactor.Core.Internal.ReactorListState();
            nullKeyed.Reset(new (int, string)[] { (0, "a") });
            Assert.False(Microsoft.UI.Reactor.Core.Internal.KeyedListDiff.Apply(nullKeyed, new[] { "a", "b" }, static (k, _) => k == "b" ? null! : k).Bailout);
        }, out long written);

        var mine = events.Where(e => e.Id == 12).Select(e => ((string)e.Fields["kind"], (bool)e.Fields["bailedOut"])).ToList();
        Assert.Equal(new[] { ("duplicateKey", true), ("duplicateKey", false), ("nullKey", false) }, mine);
        Assert.Equal(3, written);
    }

    /// <summary>
    /// Runs <paramref name="emit"/> under a private, PID-scoped session (winapp's attach mode)
    /// and returns this provider's events. Skips when the account may not start one.
    /// </summary>
    private static List<CapturedEtwEvent> Capture(byte level, ulong keywords, Action emit)
        => Capture(level, keywords, emit, out _);

    /// <param name="written">Events the provider handed ETW while <paramref name="emit"/> ran.</param>
    private static List<CapturedEtwEvent> Capture(byte level, ulong keywords, Action emit, out long written)
    {
        string etl = global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(),
            "reactor-perf-" + Guid.NewGuid().ToString("N") + ".etl");
        try
        {
            EtwPrivateCapture capture;
            try
            {
                capture = EtwPrivateCapture.Start(etl);
            }
            catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                Assert.Skip("This account may not start an ETW session (not elevated and not in Performance Log Users).");
                throw;
            }
            using (capture)
            {
                capture.Enable(ReactorPerfTrace.ProviderId, level, keywords);
                // EnableTraceEx2 waits for the enable callback, so the gate is open now.
                Assert.True(ReactorPerfTrace.IsEnabled(level, keywords), "the private session's enable never reached the provider");
                long before = ReactorPerfTrace.WriteCountForTests;
                emit();
                written = ReactorPerfTrace.WriteCountForTests - before;
                capture.Stop();
            }
            return EtwPrivateCapture.Read(etl, ReactorPerfTrace.ProviderId);
        }
        finally
        {
            try { global::System.IO.File.Delete(etl); }
            catch (global::System.IO.IOException ex)
            {
                // Best effort: a leftover temp ETL must not fail the test.
                global::System.Diagnostics.Trace.WriteLine("could not delete " + etl + ": " + ex.Message);
            }
        }
    }

    private static string Describe(List<CapturedEtwEvent> events)
        => $"{events.Count} events: " + string.Join("; ", events.Take(20).Select(e => $"{e.Id}/{e.Name}"));
}

/// <summary>
/// Serializes the tests that open ETW sessions or drive the provider's enable state, which is
/// process-wide.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReactorPerfTraceCollection
{
    public const string Name = "ReactorPerfTrace";
}
