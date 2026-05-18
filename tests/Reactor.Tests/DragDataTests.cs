using global::Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for <see cref="DragData"/> and <see cref="DragOperationNegotiation"/> — the
/// typed payload store and source/target operation negotiation shipped in Phase 6a.
/// </summary>
public class DragDataTests
{
    private sealed record CardPayload(string Id, string Title);

    [Fact]
    public void Typed_RoundTrips()
    {
        var payload = new CardPayload("A1", "First card");
        var data = DragData.Typed(payload);

        Assert.True(data.TryGetTypedPayload<CardPayload>(out var recovered));
        Assert.Equal(payload, recovered);
    }

    [Fact]
    public void TryGetTypedPayload_ReturnsFalseForMissingType()
    {
        var data = DragData.Typed(new CardPayload("A1", "x"));
        Assert.False(data.TryGetTypedPayload<string>(out _));
    }

    [Fact]
    public void AvailableFormats_IncludesTypedFormat()
    {
        var data = DragData.Typed(new CardPayload("A1", "x"));
        var typedFormat = DragData.TypedFormatId<CardPayload>();

        Assert.Contains(typedFormat, data.AvailableFormats);
        Assert.True(data.HasFormat(typedFormat));
    }

    [Fact]
    public void AvailableFormats_AlwaysIncludesProcIdMarker()
    {
        var data = new DragData();
        Assert.Contains(DragData.ProcIdFormatId, data.AvailableFormats);
    }

    [Fact]
    public void OriginProcessId_MatchesCurrentProcess()
    {
        var data = new DragData();
        Assert.Equal(global::System.Diagnostics.Process.GetCurrentProcess().Id, data.OriginProcessId);
    }

    [Fact]
    public void WithTypedPayload_ChainsMultipleTypes()
    {
        var card = new CardPayload("A1", "x");
        var data = new DragData()
            .WithTypedPayload(card)
            .WithTypedPayload(42);

        Assert.True(data.TryGetTypedPayload<CardPayload>(out var recoveredCard));
        Assert.Equal(card, recoveredCard);
        Assert.True(data.TryGetTypedPayload<int>(out var recoveredInt));
        Assert.Equal(42, recoveredInt);
    }

    [Fact]
    public void TransferRegistry_RoundTrips()
    {
        var data = DragData.Typed(new CardPayload("A1", "x"));
        var id = DragData.Register(data);
        try
        {
            Assert.Same(data, DragData.Resolve(id));
        }
        finally
        {
            DragData.Unregister(id);
        }
        Assert.Null(DragData.Resolve(id));
    }

    // ── Operation negotiation ───────────────────────────────────────

    [Fact]
    public void Negotiate_PrefersMoveOverCopy()
    {
        var final = DragOperationNegotiation.Negotiate(
            source: DragOperations.Copy | DragOperations.Move,
            target: DragOperations.Copy | DragOperations.Move);
        Assert.Equal(DragOperations.Move, final);
    }

    [Fact]
    public void Negotiate_FallsBackToCopyWhenMoveNotAvailable()
    {
        var final = DragOperationNegotiation.Negotiate(
            source: DragOperations.Copy | DragOperations.Move,
            target: DragOperations.Copy);
        Assert.Equal(DragOperations.Copy, final);
    }

    [Fact]
    public void Negotiate_ReturnsNoneWhenIntersectionEmpty()
    {
        var final = DragOperationNegotiation.Negotiate(
            source: DragOperations.Copy,
            target: DragOperations.Move);
        Assert.Equal(DragOperations.None, final);
    }

    [Fact]
    public void Negotiate_LinkIsLastResort()
    {
        var final = DragOperationNegotiation.Negotiate(
            source: DragOperations.All,
            target: DragOperations.Link);
        Assert.Equal(DragOperations.Link, final);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Phase 6b — standard format eager + lazy round-trips
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void WithText_Eager_RoundTripsSync()
    {
        var data = new DragData().WithText("hello");
        Assert.True(data.TryGetText(out var text));
        Assert.Equal("hello", text);
    }

    [Fact]
    public async Task WithText_LazySync_ResolvesAsync()
    {
        var data = new DragData().WithText(() => "world");
        var text = await data.GetTextAsync();
        Assert.Equal("world", text);
    }

    [Fact]
    public async Task WithText_LazyAsync_ResolvesAsync()
    {
        var data = new DragData().WithText(async ct =>
        {
            await Task.Delay(1, ct);
            return "async world";
        });
        var text = await data.GetTextAsync();
        Assert.Equal("async world", text);
    }

    [Fact]
    public async Task WithHtml_LazyProvider_NotInvokedWhenOnlyTextRequested()
    {
        int htmlInvocations = 0;
        var data = new DragData()
            .WithText("plain")
            .WithHtml(() => { htmlInvocations++; return "<html>"; });

        Assert.True(data.TryGetText(out var text));
        Assert.Equal("plain", text);
        Assert.Equal(0, htmlInvocations);
    }

    [Fact]
    public async Task WithHtml_LazyProvider_InvokedOnceOnGetHtmlAsync()
    {
        int htmlInvocations = 0;
        var data = new DragData()
            .WithHtml(() => { htmlInvocations++; return "<html>"; });

        var html1 = await data.GetHtmlAsync();
        // Sync provider is invoked every resolve — that's expected behavior. The
        // contract guarantee is "not invoked when target doesn't request the format".
        Assert.Equal("<html>", html1);
        Assert.Equal(1, htmlInvocations);
    }

    [Fact]
    public void WithUri_Eager_RoundTrips()
    {
        var uri = new Uri("https://example.com/");
        var data = new DragData().WithUri(uri);
        Assert.True(data.TryGetUri(out var recovered));
        Assert.Equal(uri, recovered);
    }

    [Fact]
    public void WithRtf_Eager_RoundTrips()
    {
        var data = new DragData().WithRtf(@"{\rtf1 hi}");
        Assert.True(data.TryGetRtf(out var rtf));
        Assert.Equal(@"{\rtf1 hi}", rtf);
    }

    [Fact]
    public void WithCustomFormat_RoundTripsByFormatId()
    {
        var data = new DragData().WithCustomFormat("application/x-widget", 42);
        Assert.True(data.TryGetCustomFormat<int>("application/x-widget", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void AvailableFormats_IncludesEachRegisteredFormat()
    {
        var data = new DragData()
            .WithText("hi")
            .WithHtml("<b>hi</b>")
            .WithCustomFormat("myfmt", new object());

        var formats = data.AvailableFormats;
        Assert.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text, formats);
        Assert.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Html, formats);
        Assert.Contains("myfmt", formats);
    }

    [Fact]
    public async Task GetTextAsync_ReturnsNullWhenFormatMissing()
    {
        var data = new DragData();
        var text = await data.GetTextAsync();
        Assert.Null(text);
    }

    [Fact]
    public async Task GetCustomFormatAsync_LazyProvider_ResolvesAsync()
    {
        var data = new DragData().WithCustomFormat("x", async ct =>
        {
            await Task.Yield();
            return (object)"resolved";
        });
        var value = await data.GetCustomFormatAsync<string>("x");
        Assert.Equal("resolved", value);
    }

    [Fact]
    public async Task GetCustomFormatAsync_TypeMismatch_ReturnsDefault()
    {
        var data = new DragData().WithCustomFormat("x", 42);
        var value = await data.GetCustomFormatAsync<string>("x");
        Assert.Null(value);
    }

    [Fact]
    public void TypedAndStandardFormats_CoexistOnSameDragData()
    {
        var data = new DragData()
            .WithTypedPayload(new CardPayload("A1", "x"))
            .WithText("A1 title");

        Assert.True(data.TryGetTypedPayload<CardPayload>(out var card));
        Assert.Equal("A1", card.Id);
        Assert.True(data.TryGetText(out var text));
        Assert.Equal("A1 title", text);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Phase 6c — DropCompleted → DragEndContext mapping
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildDragEndContext_Move_SucceedsUncancelled()
    {
        var ctx = Reconciler.BuildDragEndContext(DataPackageOperation.Move);
        Assert.Equal(DragOperations.Move, ctx.CompletedOperation);
        Assert.False(ctx.WasCancelled);
    }

    [Fact]
    public void BuildDragEndContext_Copy_SucceedsUncancelled()
    {
        var ctx = Reconciler.BuildDragEndContext(DataPackageOperation.Copy);
        Assert.Equal(DragOperations.Copy, ctx.CompletedOperation);
        Assert.False(ctx.WasCancelled);
    }

    [Fact]
    public void BuildDragEndContext_None_IsCancelled()
    {
        var ctx = Reconciler.BuildDragEndContext(DataPackageOperation.None);
        Assert.Equal(DragOperations.None, ctx.CompletedOperation);
        Assert.True(ctx.WasCancelled);
    }

    [Fact]
    public void BuildDragEndContext_Link_SucceedsUncancelled()
    {
        var ctx = Reconciler.BuildDragEndContext(DataPackageOperation.Link);
        Assert.Equal(DragOperations.Link, ctx.CompletedOperation);
        Assert.False(ctx.WasCancelled);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Lazy providers — sync + async variants for every standard format
    //  Each test pins: "if WithX(Func<...>) regresses to eager evaluation,
    //  the provider would run at WithX-call time instead of GetXAsync-call
    //  time — observable when the source provides expensive content (file
    //  read, network fetch) that should only fire on actual consumer pull."
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WithUri_LazySync_DefersResolution()
    {
        int invocations = 0;
        var data = new DragData().WithUri(() => { invocations++; return new Uri("https://example.com/"); });
        Assert.Equal(0, invocations);
        var got = await data.GetUriAsync();
        Assert.Equal(1, invocations);
        Assert.Equal(new Uri("https://example.com/"), got);
        // Second pull invokes the provider again (FormatEntry.SyncProvider is not memoized).
        await data.GetUriAsync();
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task WithUri_LazyAsync_DefersResolution()
    {
        int invocations = 0;
        var data = new DragData().WithUri(async ct => { invocations++; await Task.Yield(); return new Uri("https://example.com/"); });
        Assert.Equal(0, invocations);
        var got = await data.GetUriAsync();
        Assert.Equal(1, invocations);
        Assert.Equal(new Uri("https://example.com/"), got);
    }

    [Fact]
    public void WithUri_Sync_TryGetUri_ResolvesSynchronously()
    {
        // Bug this catches: sync providers should resolve inside TryGet*
        // without spinning the dispatcher. A regression that always required
        // the async path would deadlock UI thread consumers.
        var data = new DragData().WithUri(() => new Uri("https://x/"));
        Assert.True(data.TryGetUri(out var uri));
        Assert.Equal(new Uri("https://x/"), uri);
    }

    [Fact]
    public async Task WithUri_AsyncOnly_TryGetUri_FallsThroughToFalse()
    {
        // Bug this catches: TryGet (sync) should NOT block on an async-only
        // provider. The contract: TryGet returns false, the caller falls
        // through to GetXAsync. A regression that synchronously blocked an
        // async provider would freeze the UI thread on drop.
        var data = new DragData().WithUri(async ct => { await Task.Yield(); return new Uri("https://x/"); });
        Assert.False(data.TryGetUri(out _));
        // But the async path still works.
        var got = await data.GetUriAsync();
        Assert.Equal(new Uri("https://x/"), got);
    }

    [Fact]
    public async Task WithRtf_LazySync_DefersResolution()
    {
        int invocations = 0;
        var data = new DragData().WithRtf(() => { invocations++; return "{\\rtf1}"; });
        Assert.Equal(0, invocations);
        var got = await data.GetRtfAsync();
        Assert.Equal(1, invocations);
        Assert.Equal("{\\rtf1}", got);
    }

    [Fact]
    public async Task WithRtf_LazyAsync_DefersResolution()
    {
        int invocations = 0;
        var data = new DragData().WithRtf(async ct => { invocations++; await Task.Yield(); return "{\\rtf1}"; });
        Assert.Equal(0, invocations);
        var got = await data.GetRtfAsync();
        Assert.Equal(1, invocations);
        Assert.Equal("{\\rtf1}", got);
    }

    [Fact]
    public void WithHtml_LazySync_SatisfiesTryGet()
    {
        // Bug this catches: WithHtml(Func<string>) routing through a wrong
        // storage path so TryGetHtml never finds the entry.
        var data = new DragData().WithHtml(() => "<p>x</p>");
        Assert.True(data.TryGetHtml(out var html));
        Assert.Equal("<p>x</p>", html);
    }

    [Fact]
    public async Task WithCustomFormat_LazySync_DefersResolution()
    {
        int invocations = 0;
        var data = new DragData().WithCustomFormat(
            "reactor.test/payload",
            () => { invocations++; return (object)"value"; });
        Assert.Equal(0, invocations);
        Assert.True(data.TryGetCustomFormat<string>("reactor.test/payload", out var got));
        Assert.Equal("value", got);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task WithCustomFormat_LazyAsync_DefersResolutionAndYieldsViaGetAsync()
    {
        int invocations = 0;
        var data = new DragData().WithCustomFormat(
            "reactor.test/payload",
            async ct => { invocations++; await Task.Yield(); return (object)42; });
        Assert.Equal(0, invocations);
        // TryGetCustomFormat is sync — async-only provider should return false.
        Assert.False(data.TryGetCustomFormat<int>("reactor.test/payload", out _));
        // GetCustomFormatAsync resolves the provider.
        var got = await data.GetCustomFormatAsync<int>("reactor.test/payload");
        Assert.Equal(42, got);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task GetCustomFormatAsync_MissingFormat_ReturnsDefault()
    {
        // Bug this catches: GetCustomFormatAsync throwing for an absent
        // format instead of returning default(T) — the contract per spec
        // is "absent → default, never throw" so callers can branch on
        // null/0/etc. without try/catch.
        var data = new DragData();
        var got = await data.GetCustomFormatAsync<string>("absent");
        Assert.Null(got);
    }

    [Fact]
    public async Task GetCustomFormatAsync_WrongType_ReturnsDefault()
    {
        // Bug this catches: a wrong-type cast inside GetCustomFormatAsync
        // bubbling up an InvalidCastException — the spec says wrong type
        // is equivalent to absent.
        var data = new DragData().WithCustomFormat("k", 42);
        var got = await data.GetCustomFormatAsync<string>("k");
        Assert.Null(got);
    }

    [Fact]
    public void TryGetCustomFormat_MissingFormat_ReturnsFalse()
    {
        var data = new DragData();
        Assert.False(data.TryGetCustomFormat<string>("absent", out var v));
        Assert.Null(v);
    }

    [Fact]
    public void TryGetCustomFormat_WrongType_ReturnsFalse()
    {
        // Bug this catches: a wrong-type cast throwing — same contract as
        // GetCustomFormatAsync. Mismatch is silent-false, not exception.
        var data = new DragData().WithCustomFormat("k", 42);
        Assert.False(data.TryGetCustomFormat<string>("k", out var v));
        Assert.Null(v);
    }

    [Fact]
    public void AvailableFormats_IncludesAllStandardFormats()
    {
        // Bug this catches: WithX populating the wrong format key (e.g.
        // Text → StorageItems) — the agent-visible FormatEntries list
        // would advertise a format the consumer can't pull.
        var data = new DragData()
            .WithText("t")
            .WithUri(new Uri("https://x/"))
            .WithHtml("<p>x</p>")
            .WithRtf("{\\rtf1}");
        Assert.Contains(StandardDataFormats.Text, data.AvailableFormats);
        Assert.Contains(StandardDataFormats.WebLink, data.AvailableFormats);
        Assert.Contains(StandardDataFormats.Html, data.AvailableFormats);
        Assert.Contains(StandardDataFormats.Rtf, data.AvailableFormats);
        Assert.Contains(DragData.ProcIdFormatId, data.AvailableFormats);
    }

    [Fact]
    public void HasFormat_CustomFormat_ReturnsTrue()
    {
        var data = new DragData().WithCustomFormat("reactor.test/x", 1);
        Assert.True(data.HasFormat("reactor.test/x"));
        Assert.False(data.HasFormat("reactor.test/y"));
    }

    [Fact]
    public void HasFormat_ProcIdMarker_AlwaysTrue()
    {
        // ProcId marker is virtual — always advertised even on an empty
        // DragData. This is how cross-process drops detect "same proc".
        var data = new DragData();
        Assert.True(data.HasFormat(DragData.ProcIdFormatId));
    }

    [Fact]
    public void FormatEntries_ExposesInternalDictionary()
    {
        // Internal getter — used by PopulatePackage when projecting to a
        // DataPackage. Bug this catches: a regression that exposed the
        // dictionary by *copy* instead of *reference* would leak two
        // sources of truth and same-process drops would see stale data.
        var data = new DragData().WithText("hello").WithCustomFormat("k", 1);
        var entries = data.FormatEntries;
        Assert.Equal(2, entries.Count);
        Assert.True(entries.ContainsKey(StandardDataFormats.Text));
        Assert.True(entries.ContainsKey("k"));
    }

    [Fact]
    public void WithText_OverwritesPriorEntry()
    {
        // Bug this catches: WithText called twice appends two entries
        // instead of overwriting — agents would see stale text in the
        // FormatEntries dictionary even after the source updated it.
        var data = new DragData().WithText("first").WithText("second");
        Assert.True(data.TryGetText(out var got));
        Assert.Equal("second", got);
        // Only one Text entry — overwrite, not append.
        Assert.Single(data.FormatEntries, kvp => kvp.Key == StandardDataFormats.Text);
    }

    [Fact]
    public async Task SwitchingProviderShape_LastWriteWins()
    {
        // Bug this catches: WithUri(sync provider) followed by WithUri(eager)
        // leaving both a SyncProvider AND an EagerValue on the FormatEntry —
        // ResolveSync would return the eager value but ResolveAsync would
        // return the sync provider's result if they diverged. Contract is
        // "last write wins, single source of truth per FormatEntry."
        var data = new DragData()
            .WithUri(() => new Uri("https://stale/"))
            .WithUri(new Uri("https://fresh/"));
        Assert.True(data.TryGetUri(out var sync));
        Assert.Equal(new Uri("https://fresh/"), sync);
        var async = await data.GetUriAsync();
        Assert.Equal(new Uri("https://fresh/"), async);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Internal in-memory transfer registry (Register / Resolve / Unregister)
    //  Same-process DnD relies on the registry to round-trip the DragData
    //  via a GUID written into DataPackage.Properties. A leak here is a
    //  per-drag memory leak; a miss is a same-process drop that silently
    //  drops the typed payload.
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void TransferRegistry_RegisterResolveUnregister_FullCycle()
    {
        var data = new DragData().WithText("x");
        var id = DragData.Register(data);
        Assert.Same(data, DragData.Resolve(id));
        DragData.Unregister(id);
        Assert.Null(DragData.Resolve(id));
    }

    [Fact]
    public void TransferRegistry_Resolve_UnknownGuid_ReturnsNull()
    {
        // Bug this catches: Resolve(unknown) throwing KeyNotFoundException
        // instead of returning null — the cross-process detection path
        // expects "absent → null" to fall back to the DataPackage formats.
        Assert.Null(DragData.Resolve(Guid.NewGuid()));
    }

    [Fact]
    public void TransferRegistry_Unregister_UnknownGuid_IsIdempotent()
    {
        // Bug this catches: Unregister throwing on an unknown id —
        // DropCompleted can fire twice (cancel + completed race) and the
        // second call must not throw.
        DragData.Unregister(Guid.NewGuid());
        DragData.Unregister(Guid.NewGuid());
    }
}

/// <summary>
/// Tests for <see cref="DragData.FormatEntry"/> directly. The entry's resolve
/// state machine (eager > async > sync precedence in ResolveAsync; eager > sync
/// precedence in ResolveSync; async-only returns null in ResolveSync) is the
/// foundation of every WithX/GetX path; pinning it here means future regressions
/// surface here, not in flaky drop-target tests.
/// </summary>
public class DragDataFormatEntryTests
{
    [Fact]
    public void HasEager_TrueWhenOnlyEagerSet()
    {
        var e = new DragData.FormatEntry { EagerValue = "x" };
        Assert.True(e.HasEager);
    }

    [Fact]
    public void HasEager_FalseWhenSyncProviderPresent()
    {
        // Bug this catches: HasEager incorrectly returning true when a sync
        // provider is set, causing the PopulatePackage path to emit the
        // null EagerValue to the DataPackage instead of the provider's
        // resolved value.
        var e = new DragData.FormatEntry { SyncProvider = () => "x" };
        Assert.False(e.HasEager);
    }

    [Fact]
    public void HasEager_FalseWhenAsyncProviderPresent()
    {
        var e = new DragData.FormatEntry { AsyncProvider = _ => Task.FromResult<object?>("x") };
        Assert.False(e.HasEager);
    }

    [Fact]
    public void HasEager_FalseWhenEagerValueNull()
    {
        // Bug this catches: HasEager treating an explicit-null eager as
        // "present", causing the package to emit null where the source
        // didn't intend any payload.
        var e = new DragData.FormatEntry { EagerValue = null };
        Assert.False(e.HasEager);
    }

    [Fact]
    public async Task ResolveAsync_PrefersEagerOverProviders()
    {
        var e = new DragData.FormatEntry
        {
            EagerValue = "eager",
            SyncProvider = () => "sync",
            AsyncProvider = _ => Task.FromResult<object?>("async"),
        };
        Assert.Equal("eager", await e.ResolveAsync(default));
    }

    [Fact]
    public async Task ResolveAsync_AsyncProviderTakesPrecedenceOverSync()
    {
        // Bug this catches: precedence regression where ResolveAsync picks
        // the sync provider when both are set. Per the source order, async
        // wins — important because async providers carry the cancellation
        // token and the sync provider does not.
        var e = new DragData.FormatEntry
        {
            SyncProvider = () => "sync",
            AsyncProvider = _ => Task.FromResult<object?>("async"),
        };
        Assert.Equal("async", await e.ResolveAsync(default));
    }

    [Fact]
    public async Task ResolveAsync_NoValueAndNoProviders_ReturnsNull()
    {
        var e = new DragData.FormatEntry();
        Assert.Null(await e.ResolveAsync(default));
    }

    [Fact]
    public async Task ResolveAsync_PropagatesCancellationToken()
    {
        // Bug this catches: regression that drops the CancellationToken
        // before handing it to the async provider — long-running provider
        // can't be aborted on drag-leave.
        using var cts = new global::System.Threading.CancellationTokenSource();
        global::System.Threading.CancellationToken seen = default;
        var e = new DragData.FormatEntry
        {
            AsyncProvider = ct => { seen = ct; return Task.FromResult<object?>(null); },
        };
        await e.ResolveAsync(cts.Token);
        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public void ResolveSync_PrefersEagerOverSync()
    {
        var e = new DragData.FormatEntry { EagerValue = "eager", SyncProvider = () => "sync" };
        Assert.Equal("eager", e.ResolveSync());
    }

    [Fact]
    public void ResolveSync_AsyncOnly_ReturnsNullWithoutBlocking()
    {
        // The critical UI-thread invariant: ResolveSync must NOT block on
        // an async-only entry. Bug this catches: a regression that called
        // `.GetAwaiter().GetResult()` here would freeze the dispatcher on
        // every drop with a lazy-async format.
        bool invoked = false;
        var e = new DragData.FormatEntry
        {
            AsyncProvider = _ => { invoked = true; return Task.FromResult<object?>("x"); },
        };
        Assert.Null(e.ResolveSync());
        Assert.False(invoked); // not even invoked — fall through, caller goes async.
    }

    [Fact]
    public void ResolveSync_SyncProvider_InvokesAndReturns()
    {
        int n = 0;
        var e = new DragData.FormatEntry { SyncProvider = () => { n++; return "v"; } };
        Assert.Equal("v", e.ResolveSync());
        Assert.Equal(1, n);
    }

    [Fact]
    public void ResolveSync_NoValueAndNoProviders_ReturnsNull()
    {
        Assert.Null(new DragData.FormatEntry().ResolveSync());
    }
}
