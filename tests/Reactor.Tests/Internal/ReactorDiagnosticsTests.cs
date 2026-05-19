using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Internal;

/// <summary>
/// Tests for <see cref="ReactorDiagnostics"/> (spec 042 Phase 6.2).
///
/// The collector is process-wide. Each test seeds a fresh control instance
/// and disjoint sample-key sets so dedup ledgers across tests don't collide.
/// The shared <see cref="ReactorDiagnostics.RecentKeyedListWarnings"/> buffer
/// is observed *by content*, not by count, so concurrent producers in other
/// tests don't make assertions flaky.
/// </summary>
public class ReactorDiagnosticsTests
{
    // Wrapper because `global::System.Guid.NewGuid()` inside an interpolated
    // string blows up the parser at the `::` token.
    private static string NewId() => global::System.Guid.NewGuid().ToString("N");

    // Per-test marker keys keep this test's entries identifiable in the
    // shared buffer.
    private static string[] Sample(string suffix, params string[] keys)
    {
        var arr = new string[keys.Length];
        for (int i = 0; i < keys.Length; i++) arr[i] = $"{suffix}:{keys[i]}";
        return arr;
    }

    private static global::System.Collections.Generic.IReadOnlyList<KeyedListDiagnostic> WarningsForContext(string context)
    {
        var all = ReactorDiagnostics.RecentKeyedListWarnings;
        var hits = new global::System.Collections.Generic.List<KeyedListDiagnostic>(all.Count);
        foreach (var w in all)
            if (string.Equals(w.ControlContext, context, global::System.StringComparison.Ordinal))
                hits.Add(w);
        return hits;
    }

    [Fact]
    public void First_Record_Lands_With_Count_1()
    {
        var control = new object();
        var ctx = $"FirstRecord_{NewId()}";

        var entry = ReactorDiagnostics.Record(
            control, ctx,
            KeyedListDiagnosticKind.DuplicateKey,
            Sample("first", "x", "y"));

        Assert.Equal(1, entry.Count);
        Assert.Equal(ctx, entry.ControlContext);
        Assert.Equal(KeyedListDiagnosticKind.DuplicateKey, entry.Kind);
        Assert.Equal(2, entry.SampleKeys.Count);
    }

    [Fact]
    public void Repeat_With_Same_Triple_Bumps_Count_In_Place()
    {
        var control = new object();
        var ctx = $"Bump_{NewId()}";
        var keys = Sample("bump", "a", "b");

        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, keys);
        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, keys);
        var third = ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, keys);

        Assert.Equal(3, third.Count);

        var hits = WarningsForContext(ctx);
        Assert.Single(hits);  // dedup'd into one buffer entry
        Assert.Equal(3, hits[0].Count);
    }

    [Fact]
    public void Different_Kinds_Get_Separate_Entries()
    {
        var control = new object();
        var ctx = $"KindSplit_{NewId()}";

        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, Sample("ks", "a"));
        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.NullKey, Sample("ks", "<null>"));

        var hits = WarningsForContext(ctx);
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Kind == KeyedListDiagnosticKind.DuplicateKey);
        Assert.Contains(hits, h => h.Kind == KeyedListDiagnosticKind.NullKey);
    }

    [Fact]
    public void Different_Control_Instances_Dedup_Independently()
    {
        var ctlA = new object();
        var ctlB = new object();
        var ctx = $"PerControl_{NewId()}";
        var keys = Sample("pc", "x");

        ReactorDiagnostics.Record(ctlA, ctx, KeyedListDiagnosticKind.DuplicateKey, keys);
        var b = ReactorDiagnostics.Record(ctlB, ctx, KeyedListDiagnosticKind.DuplicateKey, keys);

        // Different control instance ⇒ B's ledger reports count = 1, not 2.
        Assert.Equal(1, b.Count);
    }

    [Fact]
    public void IsFirstOccurrence_Returns_False_After_First_Record()
    {
        var control = new object();
        var ctx = $"FirstOcc_{NewId()}";
        var keys = Sample("fo", "z");

        Assert.True(ReactorDiagnostics.IsFirstOccurrence(control, KeyedListDiagnosticKind.DuplicateKey, keys, ctx));
        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, keys);
        Assert.False(ReactorDiagnostics.IsFirstOccurrence(control, KeyedListDiagnosticKind.DuplicateKey, keys, ctx));
    }

    [Fact]
    public void Sample_Keys_Truncated_With_Ellipsis_Past_Cap()
    {
        var control = new object();
        var ctx = $"Truncate_{NewId()}";

        // Push 12 keys — cap is MaxSampleKeys (8), so the entry should
        // hold 8 + 1 ellipsis line.
        var manyKeys = new string[12];
        for (int i = 0; i < 12; i++) manyKeys[i] = $"trunc-{i}";

        var entry = ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, manyKeys);

        Assert.Equal(ReactorDiagnostics.MaxSampleKeys + 1, entry.SampleKeys.Count);
        Assert.StartsWith("…and", entry.SampleKeys[^1]);
    }

    [Fact]
    public void Global_Ledger_Dedupes_By_Context_When_ControlInstance_Is_Null()
    {
        // Regression: when controlInstance is null, the dedup key must
        // include controlContext. Without it, two different contexts
        // sharing the same (kind, sample-set) collided in the global
        // _seenContextual ledger and the second Record incremented the
        // first context's Count instead of starting its own.
        var ctxA = $"NullCtl_A_{NewId()}";
        var ctxB = $"NullCtl_B_{NewId()}";
        var keys = Sample("nc", "shared");

        Assert.True(ReactorDiagnostics.IsFirstOccurrence(null, KeyedListDiagnosticKind.DuplicateKey, keys, ctxA));
        Assert.True(ReactorDiagnostics.IsFirstOccurrence(null, KeyedListDiagnosticKind.DuplicateKey, keys, ctxB));

        var a = ReactorDiagnostics.Record(null, ctxA, KeyedListDiagnosticKind.DuplicateKey, keys);
        var b = ReactorDiagnostics.Record(null, ctxB, KeyedListDiagnosticKind.DuplicateKey, keys);

        Assert.Equal(1, a.Count);
        Assert.Equal(1, b.Count);
        // After A is recorded, B is still a first-occurrence for its own context.
        Assert.False(ReactorDiagnostics.IsFirstOccurrence(null, KeyedListDiagnosticKind.DuplicateKey, keys, ctxA));
        Assert.False(ReactorDiagnostics.IsFirstOccurrence(null, KeyedListDiagnosticKind.DuplicateKey, keys, ctxB));
    }

    [Fact]
    public void Snapshot_Returns_Newest_First()
    {
        var control = new object();
        var ctxA = $"OrderA_{NewId()}";
        var ctxB = $"OrderB_{NewId()}";

        ReactorDiagnostics.Record(control, ctxA, KeyedListDiagnosticKind.DuplicateKey, Sample("o", "a"));
        ReactorDiagnostics.Record(control, ctxB, KeyedListDiagnosticKind.DuplicateKey, Sample("o", "b"));

        var all = ReactorDiagnostics.RecentKeyedListWarnings;
        int idxA = -1, idxB = -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].ControlContext == ctxA) idxA = i;
            if (all[i].ControlContext == ctxB) idxB = i;
        }
        Assert.True(idxA >= 0 && idxB >= 0, "both contexts present");
        // Newest-first: B was inserted after A ⇒ B's index < A's.
        Assert.True(idxB < idxA, $"expected B (newer) before A (older); got A={idxA}, B={idxB}");
    }
}
