using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Pack;
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
    // Braceless single-statement `if (fe is Control c) c.ClearValue(...)` — the shape a
    // region-parsing scan gets wrong. IsEnabled is now the only property clearing this way.
    [InlineData("Control", "IsEnabled")]
    // Ungated clears for the two properties #162 added. IsTabStop moved OUT of the `is Control`
    // arm as part of this change (a pooled TextBlock was keeping a stale tab stop), so
    // FrameworkElement — not Control — is the receiver that must be found.
    [InlineData("FrameworkElement", "IsHitTestVisible")]
    [InlineData("FrameworkElement", "IsTabStop")]
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
    /// The scan must attribute a reset written inside a nested <c>if</c> to the innermost type
    /// bound for it, not to the enclosing arm.
    /// </summary>
    /// <remarks>
    /// <c>Grid</c> and <c>StackPanel</c> are bound inside the <c>else if (fe is Panel resetPanel)</c>
    /// arm, so their clears sit two pattern-bindings deep. Attributing them to <c>Panel</c> would
    /// widen <c>Padding</c>'s and <c>CornerRadius</c>'s derived <c>poolResetGate</c> to every panel
    /// — including <c>Canvas</c>, which is poolable and declares neither property — and turn a
    /// correct MOD_002 into a false POOL_001 Warning. The <c>Panel</c> row is the control: it must
    /// stay attributed to <c>Panel</c>, since <c>Background</c> really is cleared for every panel.
    /// </remarks>
    [Theory]
    [InlineData("Grid", "Padding", "Panel")]
    [InlineData("StackPanel", "CornerRadius", "Panel")]
    [InlineData("Panel", "Background", "Grid")]
    public void Nested_Bindings_Attribute_To_The_Innermost_Type(
        string receiver, string property, string mustNotBeAttributedTo)
    {
        var receivers = CleanElementScan.Resets
            .Where(reset => reset.Property == property)
            .Select(reset => reset.Receiver)
            .ToList();

        Assert.True(
            receivers.Contains(receiver, StringComparer.Ordinal),
            $"'{property}' is cleared on '{receiver}' in CleanElement, but the scan attributed it " +
            $"to [{string.Join(", ", receivers)}] instead.");

        Assert.False(
            receivers.Contains(mustNotBeAttributedTo, StringComparer.Ordinal),
            $"The scan attributed a '{property}' reset to '{mustNotBeAttributedTo}', which does not " +
            "clear it. A widened attribution widens the derived poolResetGate, which turns a " +
            "correct REACTOR_MOD_002 into a false REACTOR_POOL_001 Warning — a build break under " +
            "TreatWarningsAsErrors.");
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

    // ── Spurious matches ────────────────────────────────────────────────────

    /// <summary>
    /// Text that merely <em>looks</em> like a reset must not become one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regex predecessors of this scan needed a hand-written comment/string blanker and a
    /// line-anchored region boundary precisely because this method's own comments discuss the
    /// properties being scanned — <c>"Padding / CornerRadius / … / IsEnabled"</c> — and a comment
    /// read as a reset invents a receiver/property pair no consistency invariant can satisfy.
    /// Roslyn makes that structurally impossible: trivia is trivia and literals are literals. This
    /// test is the standing proof, because "it cannot happen" is exactly the claim that stops
    /// being checked.
    /// </para>
    /// <para>
    /// The identifiers below appear in <c>CleanElement</c>'s comments (or the surrounding
    /// doc comments) but are never assigned or cleared as code. If any shows up in the scan, the
    /// parser has regressed to text matching. Each is asserted to be genuinely present in the
    /// file first, so a row cannot quietly become a test of nothing after a comment is reworded.
    /// </para>
    /// </remarks>
    [Theory]
    // Named in the FE-common comment block's prose about ApplyModifiers' receiver chains.
    [InlineData("ApplyModifiers")]
    [InlineData("PoolableTypes")]
    // Named in the prose explaining which gated receivers the pool does NOT recycle.
    [InlineData("RelativePanel")]
    public void Prose_Is_Not_Mistaken_For_A_Reset(string identifier)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs"));

        // Positive control. A row naming text the file does not contain asserts nothing — the
        // scan would "correctly" omit an identifier that was never there to match.
        Assert.Contains(identifier, source, StringComparison.Ordinal);

        var matches = CleanElementScan.Resets
            .Where(reset => string.Equals(reset.Property, identifier, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            matches.Count == 0,
            $"The scan reported a reset of '{identifier}': [{string.Join("; ", matches)}]. That name " +
            "appears only in CleanElement's comments, so the scan is matching text rather than " +
            "syntax. A phantom reset satisfies no ModifierTable row, so it fails the consistency " +
            "invariants for a reason that does not exist — and it names a plausible-looking " +
            "property while doing it.");
    }

    /// <summary>
    /// Every scanned receiver must be a type <c>CleanElement</c> genuinely narrows to.
    /// </summary>
    /// <remarks>
    /// The complement of <see cref="Every_Reset_Receiver_Resolves_To_A_Bound_Type"/>: that one
    /// catches a reset whose receiver resolves to nothing, this catches a <em>binding</em> that
    /// should not exist. <c>if (fe.Style is not null)</c> is the live hazard — under the regex
    /// predecessor's `\bis\s+(\w+)\s+(\w+)` it read as type <c>not</c> bound to name <c>null</c>,
    /// and the keyword exclusion that suppressed it was a list someone had to remember to extend.
    /// Roslyn classifies it as a <c>UnaryPattern</c>, which is not a declaration at all.
    /// </remarks>
    [Fact]
    public void No_Binding_Is_Invented_From_A_Non_Declaration_Pattern()
    {
        var suspicious = CleanElementScan.BoundReceivers
            .Where(pair => pair.Value is "not" or "null" or "and" or "or" or "var" or "true" or "false")
            .Select(pair => $"{pair.Key} -> {pair.Value}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            suspicious.Count == 0,
            $"These bindings name a pattern keyword rather than a type: [{string.Join(", ", suspicious)}]. " +
            "The scan is treating a combinator or `var` pattern as a type declaration, so any reset " +
            "naming that variable would be attributed to a type that does not exist.");
    }
}
