using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pins the decision policy of <see cref="Reconciler.UpdateDefaultAutomationName"/> via its
/// pure, DP-free helper <see cref="Reconciler.ResolveDefaultAutomationNameUpdate"/> (P3 — trim
/// the redundant per-cell automation write).
///
/// The optimization is an idempotent-write guard: the live method still reads the UIA Name, but
/// skips the <c>SetName</c> write when the Name already equals the caption-derived default (the
/// steady-state hot path). These tests pin that saving AND the three correctness guarantees that
/// must survive it: an author-set Name is never clobbered, a genuine caption change still flows
/// through, and a Name cleared this render (e.g. a removed <c>.AutomationName()</c> override) is
/// restored to the caption default even when the caption itself is unchanged.
/// </summary>
public class ReconcilerAutomationNameTests
{
    // ────────────────────────────────────────────────────────────────
    //  The P3 idempotent-write guard (teeth: revert it → these flip)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Unchanged_Caption_Skips_Write_When_Name_Already_Matches()
    {
        // Steady-state hot path: the live Name already equals the caption-derived default, so the
        // SetName main would issue is a value no-op → return null (skip).
        // TEETH: remove the `current == trimmed` guard and the helper falls through to
        // `return trimmed` ("X", a redundant write) → this assertion fails.
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current: "X", oldCaption: "X", newCaption: "X"));
    }

    [Fact]
    public void Cleared_Name_Restores_Caption_Default_When_Caption_Unchanged()
    {
        // Regression guard (the bug a blanket unchanged-caption skip would introduce): a removed
        // `.AutomationName()` override makes ApplyModifiers clear the live Name to empty *before*
        // this runs, even though the caption is unchanged. The default must be re-applied — so an
        // empty current with an unchanged caption "X" resolves to a WRITE of "X", matching main.
        // TEETH the other way: re-add `if (oldCaption == newCaption) return null;` → this fails.
        Assert.Equal("X", Reconciler.ResolveDefaultAutomationNameUpdate(current: "", oldCaption: "X", newCaption: "X"));
    }

    [Theory]
    [InlineData("X", null)]
    [InlineData("X", "")]
    [InlineData("X", "   ")]
    public void Empty_Or_Whitespace_New_Caption_Returns_Null(string? current, string? newCaption)
    {
        // No caption to project onto the Name → never touch it (matches the original guard).
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current, oldCaption: "anything", newCaption));
    }

    // ────────────────────────────────────────────────────────────────
    //  Author-override preservation (MED-risk invariant — must not regress)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Author_Override_Survives_Caption_Change()
    {
        // The live Name ("custom") differs from the previous caption ("A") → the author set it.
        // A caption change A→B must NOT clobber the author's value.
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current: "custom", oldCaption: "A", newCaption: "B"));
    }

    [Fact]
    public void Author_Override_Survives_When_Old_Caption_Unknown()
    {
        // oldCaption null but a non-empty live Name is present → treat as author-owned, leave it.
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current: "custom", oldCaption: null, newCaption: "B"));
    }

    [Fact]
    public void Author_Override_Survives_Unchanged_Caption()
    {
        // Unchanged caption "X" but the live Name is an author override ("custom" ≠ oldCaption) →
        // the idempotent guard must NOT fire (custom ≠ trimmed "X"); author-override wins → null.
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current: "custom", oldCaption: "X", newCaption: "X"));
    }

    // ────────────────────────────────────────────────────────────────
    //  The default still follows a genuine caption change
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Default_Follows_Caption_Change()
    {
        // Live Name equals the previous caption ("A") → our default owns it → update to "B".
        Assert.Equal("B", Reconciler.ResolveDefaultAutomationNameUpdate(current: "A", oldCaption: "A", newCaption: "B"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FirstTime_Set_When_Live_Name_Empty_And_Caption_Changed(string? current)
    {
        // No author Name yet + a real (changed) caption → set it.
        Assert.Equal("B", Reconciler.ResolveDefaultAutomationNameUpdate(current, oldCaption: "A", newCaption: "B"));
    }

    [Fact]
    public void Long_Changed_Caption_Is_Trimmed_To_100_Chars()
    {
        var longCaption = new string('a', 250);
        var resolved = Reconciler.ResolveDefaultAutomationNameUpdate(current: "old", oldCaption: "old", newCaption: longCaption);
        Assert.NotNull(resolved);
        Assert.Equal(100, resolved!.Length);
        Assert.Equal(new string('a', 100), resolved);
    }
}
