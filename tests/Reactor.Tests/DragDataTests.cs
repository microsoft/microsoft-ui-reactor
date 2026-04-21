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
}
