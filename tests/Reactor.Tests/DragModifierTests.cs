using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the Phase 6a drag/drop modifier extensions — each fluent call should
/// populate the expected <see cref="ElementModifiers.DragSource"/> or
/// <see cref="ElementModifiers.DropTarget"/> field, and chained calls on a drop
/// target should merge rather than overwrite.
/// </summary>
public class DragModifierTests
{
    private sealed record CardPayload(string Id);

    // ── Source side ─────────────────────────────────────────────────

    [Fact]
    public void OnDragStart_Typed_SetsSource()
    {
        var payload = new CardPayload("A1");
        var el = TextBlock("x").OnDragStart<TextBlockElement, CardPayload>(() => payload);

        var src = el.Modifiers!.DragSource;
        Assert.NotNull(src);
        var data = src!.GetData();
        Assert.True(data.TryGetTypedPayload<CardPayload>(out var recovered));
        Assert.Equal(payload, recovered);
    }

    [Fact]
    public void OnDragStart_AllowedOperations_Propagates()
    {
        var el = TextBlock("x").OnDragStart<TextBlockElement, CardPayload>(
            () => new CardPayload("x"),
            allowedOperations: DragOperations.Copy | DragOperations.Move);

        Assert.Equal(DragOperations.Copy | DragOperations.Move, el.Modifiers!.DragSource!.AllowedOperations);
    }

    [Fact]
    public void OnDragStart_OnEnd_Propagates()
    {
        Action<DragEndContext> onEnd = _ => { };
        var el = TextBlock("x").OnDragStart<TextBlockElement, CardPayload>(
            () => new CardPayload("x"),
            onEnd: onEnd);

        Assert.Same(onEnd, el.Modifiers!.DragSource!.OnEnd);
    }

    [Fact]
    public void DraggableWhen_AppliedOnTopOfOnDragStart_PreservesPayload()
    {
        var payload = new CardPayload("A1");
        var el = TextBlock("x")
            .OnDragStart<TextBlockElement, CardPayload>(() => payload)
            .DraggableWhen(() => false);

        var src = el.Modifiers!.DragSource!;
        Assert.NotNull(src.CanDrag);
        Assert.False(src.CanDrag!());
        // Original payload factory should still work.
        Assert.True(src.GetData().TryGetTypedPayload<CardPayload>(out _));
    }

    [Fact]
    public void DraggableWhen_OnlyCanDrag_ProducesEmptyData()
    {
        var el = TextBlock("x").DraggableWhen(() => true);
        var src = el.Modifiers!.DragSource!;
        // With no payload supplied, GetData returns an empty DragData (proc-id only).
        var data = src.GetData();
        Assert.NotNull(data);
        Assert.Contains(DragData.ProcIdFormatId, data.AvailableFormats);
    }

    // ── Target side ─────────────────────────────────────────────────

    [Fact]
    public void OnDrop_Typed_SetsTypedDrop()
    {
        var el = TextBlock("x").OnDrop<TextBlockElement, CardPayload>(_ => { });
        Assert.NotNull(el.Modifiers!.DropTarget!.TypedDrop);
    }

    [Fact]
    public void OnDrop_Raw_SetsOnDrop()
    {
        var el = TextBlock("x").OnDrop<TextBlockElement>(_ => { });
        Assert.NotNull(el.Modifiers!.DropTarget!.OnDrop);
    }

    [Fact]
    public void OnDragEnterOverLeave_ChainPreservesEachCallback()
    {
        Action<DragTargetArgs> enter = _ => { };
        Action<DragTargetArgs> over = _ => { };
        Action<DragTargetArgs> leave = _ => { };

        var el = TextBlock("x")
            .OnDragEnter(enter)
            .OnDragOver(over)
            .OnDragLeave(leave);

        var drop = el.Modifiers!.DropTarget!;
        Assert.Same(enter, drop.OnDragEnter);
        Assert.Same(over, drop.OnDragOver);
        Assert.Same(leave, drop.OnDragLeave);
    }

    [Fact]
    public void OnDrop_Typed_InvokesHandlerWhenPayloadPresent()
    {
        CardPayload? received = null;
        var el = TextBlock("x").OnDrop<TextBlockElement, CardPayload>(p => received = p);

        var args = new DragTargetArgs(
            data: DragData.Typed(new CardPayload("A1")),
            position: new global::Windows.Foundation.Point(0, 0),
            allowedOperations: DragOperations.All,
            modifiers: global::Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.None,
            uiOverride: new DragUIOverrideHandle());

        el.Modifiers!.DropTarget!.TypedDrop!(args);
        Assert.NotNull(received);
        Assert.Equal("A1", received!.Id);
    }

    [Fact]
    public void OnDrop_Typed_AutoAcceptsOnSuccessfulDrop()
    {
        var el = TextBlock("x").OnDrop<TextBlockElement, CardPayload>(_ => { },
            acceptedOps: DragOperations.Copy | DragOperations.Move);

        var args = new DragTargetArgs(
            data: DragData.Typed(new CardPayload("x")),
            position: new global::Windows.Foundation.Point(0, 0),
            allowedOperations: DragOperations.Move,
            modifiers: global::Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.None,
            uiOverride: new DragUIOverrideHandle());

        el.Modifiers!.DropTarget!.TypedDrop!(args);
        Assert.Equal(DragOperations.Move, args.AcceptedOperation);
    }

    [Fact]
    public void OnDrop_Typed_SkipsWhenPayloadTypeMismatch()
    {
        int invocations = 0;
        var el = TextBlock("x").OnDrop<TextBlockElement, CardPayload>(_ => invocations++);

        var args = new DragTargetArgs(
            data: DragData.Typed<string>("not a card"),
            position: new global::Windows.Foundation.Point(0, 0),
            allowedOperations: DragOperations.All,
            modifiers: global::Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.None,
            uiOverride: new DragUIOverrideHandle());

        el.Modifiers!.DropTarget!.TypedDrop!(args);
        Assert.Equal(0, invocations);
        Assert.Equal(DragOperations.None, args.AcceptedOperation);
    }

    // ── Merge semantics ─────────────────────────────────────────────

    [Fact]
    public void ElementModifiers_Merge_PrefersOtherDragSource()
    {
        var src1 = new DragSourceConfig(() => new DragData());
        var src2 = new DragSourceConfig(() => new DragData());
        var merged = new ElementModifiers { DragSource = src1 }.Merge(new ElementModifiers { DragSource = src2 });
        Assert.Same(src2, merged.DragSource);
    }

    [Fact]
    public void ElementModifiers_Merge_PrefersOtherDropTarget()
    {
        var t1 = new DropTargetConfig();
        var t2 = new DropTargetConfig { OnDrop = _ => { } };
        var merged = new ElementModifiers { DropTarget = t1 }.Merge(new ElementModifiers { DropTarget = t2 });
        Assert.Same(t2, merged.DropTarget);
    }
}
