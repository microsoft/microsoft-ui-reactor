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
}
