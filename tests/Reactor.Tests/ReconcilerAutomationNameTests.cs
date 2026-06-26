using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pins the decision policy of <see cref="Reconciler.UpdateDefaultAutomationName"/> via its
/// pure, DP-free helper <see cref="Reconciler.ResolveDefaultAutomationNameUpdate"/> (P3 — trim
/// the per-cell automation round-trip).
///
/// The optimization: when the caption is unchanged (or empty/whitespace) the live method skips
/// the UIA <c>GetName</c> read + <c>SetName</c> write entirely. These tests assert that policy
/// AND that the two correctness-critical guarantees survive: an author-set automation Name is
/// never clobbered, and a genuine caption change still flows through to the Name.
/// </summary>
public class ReconcilerAutomationNameTests
{
    // ────────────────────────────────────────────────────────────────
    //  The P3 skip (teeth: revert the unchanged-caption fast-path → these flip)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Unchanged_Caption_Returns_Null_When_Name_Already_Matches()
    {
        // Name already reflects the caption → nothing to write.
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current: "X", oldCaption: "X", newCaption: "X"));
    }

    [Fact]
    public void Unchanged_Caption_Returns_Null_Even_When_Live_Name_Is_Empty()
    {
        // TEETH for P3: with the `oldCaption == newCaption` fast-path the unchanged caption is a
        // skip regardless of the live Name. Remove that line and the helper falls through to the
        // author-override logic: current "" is not an override, so it returns "X" (a write) and
        // this assertion fails — i.e. reverting the optimization breaks this test.
        Assert.Null(Reconciler.ResolveDefaultAutomationNameUpdate(current: "", oldCaption: "X", newCaption: "X"));
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
