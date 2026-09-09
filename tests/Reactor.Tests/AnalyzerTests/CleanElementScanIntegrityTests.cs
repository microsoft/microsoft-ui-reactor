using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Instrument checks for <see cref="CleanElementScan"/>. Every REACTOR_POOL_001 consistency
/// invariant is stated over what this scan returns, and all of them are absence-shaped: they
/// report the offenders found. A scan that quietly returns <em>less</em> therefore cannot fail
/// any of them — a smaller set holds fewer offenders — and that is exactly the failure mode
/// issue #1193 was, for four years, in its truncated-at-the-dispatch form.
/// </summary>
/// <remarks>
/// So every test here is <b>presence</b>-shaped: it names something the scan must find. A zero
/// from the invariants means something only because these pass.
/// </remarks>
public class CleanElementScanIntegrityTests
{
    /// <summary>
    /// The receiver/property pairs this scan exists to see, one per structural feature it has to
    /// get right. Each was measured to disappear from <see cref="CleanElementScan.Resets"/> when
    /// its line is deleted from <c>ElementPool.CleanElement</c>.
    /// </summary>
    [Theory]
    // Method scope, ClearValue — the shape the old FE-common-only scan already saw.
    [InlineData("FrameworkElement", "Margin")]
    // Method scope, direct assignment. The old scan matched assignments too, but only here.
    [InlineData("FrameworkElement", "Tag")]
    // Method scope, ungated ClearValue for a property that had no ModifierTable row at all
    // before #1193 — the issue's second named miss.
    [InlineData("FrameworkElement", "IsHitTestVisible")]
    // Braceless single-statement `if (fe is Control c) c.ClearValue(...)`. Resolving this to
    // Control rather than FrameworkElement is what demotes IsTabStop on a TextBlock to MOD_002.
    [InlineData("Control", "IsTabStop")]
    // `else if` arm of the FE-common narrowing chain.
    [InlineData("Border", "BorderBrush")]
    // Nested `if` inside the Panel arm — two levels of pattern binding.
    [InlineData("Grid", "Padding")]
    [InlineData("StackPanel", "CornerRadius")]
    // Past the `switch (fe)` boundary the old scan stopped at. These seven are issue #1193's
    // headline misses: every one is reset here and none was visible to any invariant.
    [InlineData("TextBlock", "FontFamily")]
    [InlineData("TextBlock", "FontSize")]
    [InlineData("TextBlock", "TextWrapping")]
    [InlineData("TextBlock", "IsTextSelectionEnabled")]
    [InlineData("TextBox", "TextWrapping")]
    [InlineData("TextBox", "IsReadOnly")]
    [InlineData("Viewbox", "Stretch")]
    // Collection-clear shape, which neither of the two previous scans recognized at all.
    [InlineData("Panel", "Children")]
    [InlineData("RichTextBlock", "Blocks")]
    public void Scan_Finds_The_Reset_It_Must_Find(string receiver, string property)
    {
        var found = CleanElementScan.Resets
            .Any(reset => reset.Receiver == receiver && reset.Property == property);

        Assert.True(
            found,
            $"CleanElementScan did not find a reset of '{property}' on '{receiver}', which " +
            "ElementPool.CleanElement performs. Either the reset was removed — in which case the " +
            "ModifierTable row claiming REACTOR_POOL_001 for it is now false and must go too — or " +
            "the scan has stopped matching that shape, which silently empties every REACTOR_POOL_001 " +
            $"consistency invariant for it. Scanned {CleanElementScan.Resets.Count} reset(s) across " +
            $"{CleanElementScan.Resets.Select(r => r.Receiver).Distinct().Count()} receiver(s).");
    }

    /// <summary>
    /// The direct regression guard for issue #1193: the scan must reach <em>both</em> sides of the
    /// type dispatch.
    /// </summary>
    /// <remarks>
    /// Stated as two non-empty populations rather than as a total, because a total cannot tell the
    /// truncation apart from an ordinary shrink — the FE-common block holds the large majority of
    /// the resets, so re-introducing the <c>switch (fe)</c> boundary would drop the count without
    /// emptying it. This is the assertion that fails, loudly and by name, if anyone reinstates it.
    /// </remarks>
    [Fact]
    public void Scan_Reaches_Both_Sides_Of_The_Type_Dispatch()
    {
        var common = CleanElementScan.Resets
            .Count(reset => reset.Receiver == CleanElementScan.RootReceiver);

        var dispatch = CleanElementScan.Resets
            .Count(reset => CleanElementScan.SwitchCaseReceivers.Contains(reset.Receiver, StringComparer.Ordinal));

        Assert.True(
            common > 0,
            "The scan found no reset at all at CleanElement's method scope. The body delimiter or " +
            "the parameter binding has broken, and every invariant built on this scan is now vacuous.");

        Assert.True(
            dispatch > 0,
            "The scan found no reset inside CleanElement's `switch (fe)` arms. That is issue #1193 " +
            "restored: the TextBlock font/text resets, the TextBox arm and the Viewbox arm become " +
            "invisible, so REACTOR_POOL_001 silently under-reports every property only reset there " +
            "and no consistency test objects. Do not 'fix' this by moving the clears — extend the " +
            "scan.");
    }

    /// <summary>
    /// Every <c>case T x:</c> arm must contribute at least one reset.
    /// </summary>
    /// <remarks>
    /// Stronger than the population check above, which one surviving arm would satisfy. An arm the
    /// scan parses the label of but none of the body of is the same silent under-report, narrowed
    /// to one receiver — and that is the granularity <c>poolResetGate</c> is written at.
    /// </remarks>
    [Fact]
    public void Every_Switch_Arm_Contributes_At_Least_One_Reset()
    {
        var receivers = CleanElementScan.Resets
            .Select(reset => reset.Receiver)
            .ToHashSet(StringComparer.Ordinal);

        var silent = CleanElementScan.SwitchCaseReceivers
            .Where(receiver => !receivers.Contains(receiver))
            .OrderBy(receiver => receiver, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            CleanElementScan.SwitchCaseReceivers.Count >= 10,
            $"Only {CleanElementScan.SwitchCaseReceivers.Count} `case T x:` label(s) were parsed out " +
            "of CleanElement's type dispatch. The label regex has stopped matching, which would make " +
            "the assertion below pass over an empty set.");

        Assert.True(
            silent.Count == 0,
            $"These `case` arms of CleanElement contribute no reset the scan can see: [{string.Join(", ", silent)}]. " +
            "Either the arm genuinely resets nothing — in which case remove this receiver from the " +
            "expectation — or its resets are written in a shape the scan does not match, which " +
            "silently exempts them from every REACTOR_POOL_001 consistency invariant.");
    }

    /// <summary>
    /// Every reset's receiver must resolve to a type the method actually binds.
    /// </summary>
    /// <remarks>
    /// The scan resolves a receiver through the local-to-type map and <em>drops</em> anything
    /// unresolved rather than defaulting it to <see cref="CleanElementScan.RootReceiver"/>.
    /// Defaulting would be the dangerous repair: a renamed pattern variable would re-attribute a
    /// whole arm's resets to <c>FrameworkElement</c>, which widens every derived
    /// <c>poolResetGate</c> and turns MOD_002 rows into false POOL_001 Warnings — a build break
    /// under TreatWarningsAsErrors. Dropping is safe but silent, so it is reported here.
    /// </remarks>
    [Fact]
    public void Every_Reset_Receiver_Resolves_To_A_Bound_Type()
    {
        Assert.True(
            CleanElementScan.UnresolvedReceivers.Count == 0,
            "These receivers appear in a reset in ElementPool.CleanElement but are bound by no " +
            $"parameter, `is` pattern or `case` label the scan recognizes: [{string.Join(", ", CleanElementScan.UnresolvedReceivers)}]. " +
            "Their resets were dropped, which exempts those properties from every REACTOR_POOL_001 " +
            "consistency invariant. Bind the receiver in one of those three shapes, or teach the " +
            $"scan the new one. Known bindings: [{string.Join(", ", CleanElementScan.BoundReceivers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}"))}].");
    }

    /// <summary>
    /// The scan must account for every <c>ClearValue</c> call in the method, not just the ones it
    /// happens to parse.
    /// </summary>
    /// <remarks>
    /// A reset written any other way — <c>var dp = X.YProperty; fe.ClearValue(dp);</c>, or through
    /// a helper — is invisible to the owner/property regex, and an invisible reset makes every
    /// invariant pass vacuously for the property it clears. Counting is the cheap way to notice.
    /// </remarks>
    [Fact]
    public void Every_ClearValue_In_CleanElement_Is_Recognized()
    {
        Assert.True(
            CleanElementScan.TotalClearValueCalls > 0,
            "No ClearValue call was found anywhere in CleanElement — the body delimiter in " +
            "ReadCleanElementBody has drifted.");

        Assert.True(
            CleanElementScan.TotalClearValueCalls == CleanElementScan.RecognizedClearValueCalls,
            $"CleanElement contains {CleanElementScan.TotalClearValueCalls} ClearValue call(s) but " +
            $"the scan recognizes {CleanElementScan.RecognizedClearValueCalls}. A reset written in a " +
            "shape the regex does not match (a local dependency-property alias, a helper method) is " +
            "silently excluded from the REACTOR_POOL_001 consistency invariants. Either write it as " +
            "'receiver.ClearValue(Owner.PropProperty)', or teach CleanElementScan the new shape.");
    }

    /// <summary>
    /// Non-degeneracy floor. Every other test here names specific pairs; this one notices a scan
    /// that keeps those and loses the long tail.
    /// </summary>
    [Fact]
    public void Scan_Is_Not_Degenerate()
    {
        var receivers = CleanElementScan.Resets.Select(reset => reset.Receiver).Distinct().Count();

        Assert.True(
            CleanElementScan.Resets.Count >= 60,
            $"CleanElementScan found only {CleanElementScan.Resets.Count} reset(s). CleanElement " +
            "performs substantially more, so the scan has partly stopped matching and the " +
            "REACTOR_POOL_001 invariants are running over a truncated set.");

        Assert.True(
            receivers >= 12,
            $"CleanElementScan attributed its resets to only {receivers} distinct receiver(s). " +
            "poolResetGate is derived per receiver, so a collapsed receiver set silently widens or " +
            "empties the gates the analyzer is checked against.");
    }

    /// <summary>
    /// The attached/instance split must produce a non-empty population on both sides.
    /// </summary>
    /// <remarks>
    /// The two halves are consumed by different invariants — instance resets derive
    /// <c>poolResetGate</c>, attached ones drive <c>Every_Reset_Attached_Property_Is_Classified</c>
    /// — and each is absence-shaped over its own half. An empty half is therefore a silent pass
    /// for a whole invariant, not a visible failure.
    /// </remarks>
    [Fact]
    public void The_Attached_Instance_Split_Populates_Both_Halves()
    {
        Assert.True(
            CleanElementScan.InstanceResets.Count > 0 && CleanElementScan.AttachedResets.Count > 0,
            $"The attached/instance split produced {CleanElementScan.InstanceResets.Count} instance " +
            $"and {CleanElementScan.AttachedResets.Count} attached reset(s). Both halves feed an " +
            "absence-shaped invariant, so an empty one is a silent pass rather than a failure.");

        Assert.Equal(
            CleanElementScan.Resets.Count,
            CleanElementScan.InstanceResets.Count + CleanElementScan.AttachedResets.Count);
    }

    // ── Comment and literal blanking ────────────────────────────────────────

    /// <summary>
    /// The blanking pass is the reason the scan can drop the old <c>^\s*switch</c> line anchor, and
    /// it is not exercised by the fixture: <c>ElementPool.cs</c> happens to contain no comment that
    /// reads as a reset today, so a stripper that did nothing at all would leave every test above
    /// green. Drive it directly instead.
    /// </summary>
    [Theory]
    // A comment naming a property assignment must not read as one. CleanElement's own comments
    // discuss the exact properties being scanned, so this is the live hazard.
    [InlineData("// tb.FontSize = 14;", false)]
    [InlineData("/* tb.FontSize = 14; */", false)]
    [InlineData("/// <example>fe.Margin = x;</example>", false)]
    // …while the same text as code must survive it.
    [InlineData("tb.FontSize = 14;", true)]
    // A literal containing assignment-shaped text is not an assignment.
    [InlineData("Log(\"tb.FontSize = 14\");", false)]
    [InlineData("Log(@\"tb.FontSize = 14\");", false)]
    // An escaped quote must not end the literal early and let its tail parse as code.
    [InlineData("Log(\"a\\\" tb.FontSize = 14\");", false)]
    // A doubled quote is the verbatim escape, with the same requirement.
    [InlineData("Log(@\"a\"\" tb.FontSize = 14\");", false)]
    // Real code containing an empty literal — CleanElement's `tb.Text = ""` — still parses.
    [InlineData("tb.Text = \"\";", true)]
    public void Blanking_Removes_Comments_And_Literals_But_Not_Code(string snippet, bool expectAssignment)
    {
        var blanked = CleanElementScan.StripCommentsAndStrings(snippet);

        Assert.Equal(
            snippet.Length,
            blanked.Length);

        var matched = Regex.IsMatch(
            blanked, @"\b(\w+)\.(\w+)\s*(?<![=!<>+\-*/%&|^])=(?!=)");

        Assert.True(
            matched == expectAssignment,
            $"Blanking '{snippet}' produced '{blanked}', which the assignment pattern " +
            $"{(matched ? "matched" : "did not match")} — expected the opposite. A comment or " +
            "literal read as a reset invents a receiver/property pair no consistency invariant " +
            "can satisfy; a blanked-away statement silently drops a real one.");
    }
}
