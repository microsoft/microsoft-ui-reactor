using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1386 — the snapshot/replay primitives virtualized hosts use to give rows
/// realized outside the reconciler traversal their ancestors' context values.
/// </summary>
public class ContextCaptureUnitTests
{
    private static readonly Context<string> Theme = new("light");
    private static readonly Context<int> Count = new(0);

    private sealed record Payload(string Name);
    private static readonly Context<Payload?> Data = new(defaultValue: null);

    private static void Push(ContextScope scope, ContextBase ctx, object? value)
        => scope.Push(new Dictionary<ContextBase, object?> { [ctx] = value });

    // ── IsPrefixOf ───────────────────────────────────────────────────

    [Fact]
    public void IsPrefixOf_EmptyLiveStack_IsPrefixOfAnything()
    {
        var snapshotSource = new ContextScope();
        Push(snapshotSource, Theme, "dark");
        Assert.True(new ContextScope().IsPrefixOf(snapshotSource.Snapshot()));
    }

    [Fact]
    public void IsPrefixOf_EqualStack_IsPrefix()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        Push(scope, Count, 3);
        Assert.True(scope.IsPrefixOf(scope.Snapshot()));
    }

    [Fact]
    public void IsPrefixOf_LiveLongerThanSnapshot_IsNotPrefix()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var snapshot = scope.Snapshot();
        Push(scope, Count, 3);
        Assert.False(scope.IsPrefixOf(snapshot));
    }

    [Fact]
    public void IsPrefixOf_SameContextDifferentValue_IsNotPrefix()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var snapshot = scope.Snapshot();
        scope.Pop(1);
        Push(scope, Theme, "high-contrast");
        Assert.False(scope.IsPrefixOf(snapshot));
    }

    [Fact]
    public void IsPrefixOf_EqualButDistinctValue_IsPrefix()
    {
        // Matches the consumer check (HasConsumedContextChanged uses Equals).
        var scope = new ContextScope();
        Push(scope, Data, new Payload("a"));
        var snapshot = scope.Snapshot();
        scope.Pop(1);
        Push(scope, Data, new Payload("a"));
        Assert.True(scope.IsPrefixOf(snapshot));
    }

    [Fact]
    public void IsPrefixOf_DifferentContextInSameSlot_IsNotPrefix()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var snapshot = scope.Snapshot();
        scope.Pop(1);
        Push(scope, Count, 1);
        Assert.False(scope.IsPrefixOf(snapshot));
    }

    // ── Matches ──────────────────────────────────────────────────────

    [Fact]
    public void Matches_RequiresSameLengthAndSameInstances()
    {
        var scope = new ContextScope();
        var payload = new Payload("a");
        Push(scope, Data, payload);
        var snapshot = scope.Snapshot();
        Assert.True(scope.Matches(snapshot));

        scope.Pop(1);
        Push(scope, Data, new Payload("a"));
        Assert.False(scope.Matches(snapshot)); // equal but a different instance

        scope.Pop(1);
        Assert.False(scope.Matches(snapshot)); // shorter
    }

    // ── Replace / Restore ────────────────────────────────────────────

    [Fact]
    public void Replace_HidesLiveValues_AndRestoreBringsThemBack()
    {
        var captured = new ContextScope();
        Push(captured, Theme, "captured");

        var scope = new ContextScope();
        Push(scope, Theme, "live");
        Push(scope, Count, 7);

        var previous = scope.Replace(captured.Snapshot());
        Assert.Equal("captured", scope.Read(Theme));
        Assert.Equal(0, scope.Read(Count)); // live-only provider is not visible

        scope.Restore(previous);
        Assert.Equal("live", scope.Read(Theme));
        Assert.Equal(7, scope.Read(Count));
    }

    [Fact]
    public void Replace_PushesInsideTheReplacedScope_DoNotLeakIntoRestoredStack()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "live");

        var previous = scope.Replace([]);
        Push(scope, Count, 5);
        Assert.Equal(5, scope.Read(Count));
        scope.Pop(1);

        scope.Restore(previous);
        Assert.Equal("live", scope.Read(Theme));
        Assert.Equal(0, scope.Read(Count));
    }

    [Fact]
    public void Replace_Nested_RestoresInLifoOrder()
    {
        var a = new ContextScope(); Push(a, Theme, "a");
        var b = new ContextScope(); Push(b, Theme, "b");

        var scope = new ContextScope();
        Push(scope, Theme, "live");

        var outer = scope.Replace(a.Snapshot());
        var inner = scope.Replace(b.Snapshot());
        Assert.Equal("b", scope.Read(Theme));
        scope.Restore(inner);
        Assert.Equal("a", scope.Read(Theme));
        scope.Restore(outer);
        Assert.Equal("live", scope.Read(Theme));
    }

    [Fact]
    public void ReplaceAndRestore_AdvanceVersion()
    {
        var scope = new ContextScope();
        long v0 = scope.Version;
        var previous = scope.Replace([]);
        long v1 = scope.Version;
        scope.Restore(previous);
        Assert.True(v1 > v0);
        Assert.True(scope.Version > v1);
    }

    [Fact]
    public void HasActiveValues_StaysOnDuringEmptyReplay_OnceAProviderWasSeen()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        scope.Pop(1);
        Assert.False(scope.HasActiveValues);

        var outer = scope.Replace([]);
        Assert.True(scope.HasActiveValues);
        var inner = scope.Replace([]);
        Assert.True(scope.HasActiveValues);
        scope.Restore(inner);
        Assert.True(scope.HasActiveValues);
        scope.Restore(outer);
        Assert.False(scope.HasActiveValues);
    }

    [Fact]
    public void HasActiveValues_StaysOffDuringEmptyReplay_WhenNoProviderWasEverSeen()
    {
        var scope = new ContextScope();
        var previous = scope.Replace([]);
        Assert.False(scope.HasActiveValues);
        scope.Restore(previous);
    }

    // ── ContextCapture ───────────────────────────────────────────────

    private static ContextCapture Captured(ContextScope scope)
    {
        var capture = new ContextCapture();
        capture.Refresh(scope);
        return capture;
    }

    [Fact]
    public void Capture_IsStale_TracksValueChangesAndAdditions()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var capture = Captured(scope);
        Assert.False(capture.IsStale(scope));

        Push(scope, Count, 1); // provider added above the host
        Assert.True(capture.IsStale(scope));

        capture.Refresh(scope);
        Assert.False(capture.IsStale(scope));
    }

    [Fact]
    public void Capture_IsStale_RemovedProviderNeedsTheGenerationSignal()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        Push(scope, Count, 1);
        var capture = Captured(scope);

        scope.Pop(1); // provider removed: the shorter stack is still a prefix
        Assert.False(capture.IsStale(scope));

        scope.BumpProviderGeneration(); // what the reconciler does for a changed provider
        Assert.True(capture.IsStale(scope));

        capture.Refresh(scope);
        Assert.False(capture.IsStale(scope));
        Assert.Single(capture.Entries);
    }

    [Fact]
    public void Capture_IsStale_GenerationOutlivesTheChangingPass()
    {
        // A host that was NOT reached in the pass that removed the provider (a nested
        // host inside a parked row) must still be stale when it is reached later, under
        // a stack that by then looks like an ancestor's.
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var nested = Captured(scope);
        scope.Pop(1);
        scope.BumpProviderGeneration();

        var previous = scope.Replace([]); // later realization replays the outer snapshot
        Assert.True(nested.IsStale(scope));
        scope.Restore(previous);
    }

    [Fact]
    public void Capture_TakenDuringReplay_RecordsTheReplayedGeneration()
    {
        // Outer snapshot taken under a provider, then the provider is removed.
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var outer = Captured(scope);
        scope.Pop(1);
        scope.BumpProviderGeneration();

        // A row realized from the (now outdated) outer snapshot mounts a nested host.
        var previous = scope.Replace(outer.Entries, outer.Generation);
        var nested = Captured(scope);
        Assert.Equal(outer.Generation, nested.Generation);
        Assert.False(nested.IsStale(scope)); // consistent with the replayed stack
        scope.Restore(previous);

        // Back in the live traversal the nested snapshot must not look current.
        Assert.True(nested.IsStale(scope));
    }

    [Fact]
    public void Capture_Refresh_KeepsArrayWhenNothingChanged()
    {
        var scope = new ContextScope();
        Push(scope, Theme, "dark");
        var capture = Captured(scope);
        var first = capture.Entries;

        capture.Refresh(scope);
        Assert.Same(first, capture.Entries);

        scope.Pop(1);
        Push(scope, Theme, "light");
        capture.Refresh(scope);
        Assert.NotSame(first, capture.Entries);
        Assert.Equal("light", capture.Entries[0].Value);
    }

    [Fact]
    public void Capture_Clear_ReleasesValuesAndIsStale()
    {
        var scope = new ContextScope();
        Push(scope, Data, new Payload("big"));
        var capture = Captured(scope);

        capture.Clear();
        Assert.Empty(capture.Entries);
        Assert.True(capture.IsStale(scope));
    }
}
