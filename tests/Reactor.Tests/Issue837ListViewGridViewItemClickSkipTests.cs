using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Characterization + forward guard spun off the investigation of issue #837.
///
/// <para><b>#837 as filed does NOT reproduce.</b> Its mechanism assumed
/// <see cref="Element.ShallowEquals"/> has a <c>ListViewElement</c>/<c>GridViewElement</c> arm
/// that ignores <c>OnItemClick</c> (cited <c>Element.cs:881-891</c>), so a co-present sibling
/// callback would let the reconciler structurally skip <c>Update</c> and leave
/// <c>IsItemClickEnabled</c> stale. In fact those arms live in <see cref="Element.OwnPropsEqual"/>
/// (the DevTools reconcile-highlight helper, whose only caller is gated on
/// <c>ReactorFeatureFlags.HighlightReconcileChanges</c>) — <b>not</b> in <c>ShallowEquals</c>,
/// which has no LV/GV arm and returns <c>false</c> for them via its <c>_ =&gt; false</c> default.</para>
///
/// <para>Both documented skip gates require <c>ShallowEquals(old,new) == true</c>: the
/// element-level shallow-skip (<c>Reconciler.Update.cs</c>) and the child-level
/// <see cref="Element.CanSkipUpdate"/> (used by <c>ChildReconciler</c>). Because
/// <c>ShallowEquals</c> is always <c>false</c> for LV/GV, their handler <c>Update</c> is never
/// structurally skipped and <c>IsItemClickEnabled = OnItemClick is not null</c> is refreshed on
/// every reconcile — so the toggle cannot be missed.</para>
///
/// <para>These tests lock that invariant. If a future perf change adds an LV/GV
/// <c>ShallowEquals</c> arm (making them skip-eligible), the "toggle" tests below start failing
/// unless that arm also compares <c>OnItemClick</c> presence — forcing whoever adds the skip to
/// close the #837 hazard at the same time.</para>
/// </summary>
public class Issue837ListViewGridViewItemClickSkipTests
{
    // A reference-stable co-present callback — holds aggregate HasCallbacks true on both sides of
    // an OnItemClick toggle, the exact condition #837 describes.
    private static readonly Action<IReadOnlyList<int>> SharedSelectionChanged = _ => { };

    // ── Current behavior: LV/GV are never structurally skip-eligible ──

    [Fact]
    public void ListView_IsNever_ShallowEqual_Or_SkipEligible_Today()
    {
        // Identical copy (every own prop reference-equal) is STILL not shallow-equal, because
        // ShallowEquals has no ListViewElement arm and falls through to `_ => false`. This is the
        // reason #837 cannot occur: no skip path can fire for a ListView.
        var lv = ListView(TextBlock("row")).SelectionChanged(SharedSelectionChanged);
        Assert.False(Element.ShallowEquals(lv, lv with { }));
        Assert.False(Element.CanSkipUpdate(lv, lv with { }));
    }

    [Fact]
    public void GridView_IsNever_ShallowEqual_Or_SkipEligible_Today()
    {
        var gv = GridView(TextBlock("row")).SelectionChanged(SharedSelectionChanged);
        Assert.False(Element.ShallowEquals(gv, gv with { }));
        Assert.False(Element.CanSkipUpdate(gv, gv with { }));
    }

    // ── Forward guard: an OnItemClick presence toggle must never be skip-eligible ──
    // Today these pass trivially (LV/GV are never skip-eligible). If a future change makes LV/GV
    // skip-eligible WITHOUT comparing OnItemClick presence, CanSkipUpdate would start returning
    // true here and these fail — catching the #837 regression at the skip-decision layer.

    [Fact]
    public void ListView_OnItemClick_PresenceToggle_IsNeverSkipEligible()
    {
        var noClick = ListView(TextBlock("row")).SelectionChanged(SharedSelectionChanged);
        var withClick = noClick with { OnItemClick = _ => { } };
        Assert.False(Element.CanSkipUpdate(noClick, withClick));
        Assert.False(Element.CanSkipUpdate(withClick, noClick));
    }

    [Fact]
    public void GridView_OnItemClick_PresenceToggle_IsNeverSkipEligible()
    {
        var noClick = GridView(TextBlock("row")).SelectionChanged(SharedSelectionChanged);
        var withClick = noClick with { OnItemClick = _ => { } };
        Assert.False(Element.CanSkipUpdate(noClick, withClick));
        Assert.False(Element.CanSkipUpdate(withClick, noClick));
    }
}
