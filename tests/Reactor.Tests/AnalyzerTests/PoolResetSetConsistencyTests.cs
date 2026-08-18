using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Cross-source consistency tests for <see cref="PoolResetSetAnalyzer"/>
/// (<c>REACTOR_POOL_001</c>). Catches drift between three files:
///
///   1. <c>src/Reactor/Core/ElementPool.cs</c> — the reset list in
///      <c>CleanElement(FrameworkElement)</c>, covering both the FE instance
///      properties and the attached properties it clears.
///   2. <c>src/Reactor/Elements/ElementExtensions.cs</c> — the modifier methods
///      that survive pool reset.
///   3. <c>src/Reactor.Analyzers/ModifierTable.cs</c> — the pool-reset half of
///      <c>Properties</c> (surfaced as <c>TrappedProperties</c>) and
///      <c>AttachedProperties</c> (surfaced as <c>TrappedAttachedProperties</c>).
///
/// The bug we're guarding against: someone adds a new property reset to
/// <c>CleanElement</c> (because pooled controls were leaking that prop into
/// the next mount), there is already a modifier with the same name, but
/// nobody updates the analyzer — so <c>.Set(fe => fe.NewProp = ...)</c>
/// still silently loses values and there's no warning at edit time. The
/// invariant test below fails in that scenario and tells the developer
/// exactly what to add.
/// </summary>
public class PoolResetSetConsistencyTests
{
    /// <summary>
    /// FE properties that <c>CleanElement</c> resets but that we intentionally
    /// do NOT include in <see cref="PoolResetSetAnalyzer.TrappedProperties"/>.
    /// Add a new entry here (with a comment explaining why) only when the
    /// property genuinely has no clean modifier-based replacement.
    /// </summary>
    private static readonly Dictionary<string, string> IntentionallyExcluded =
        new(StringComparer.Ordinal)
        {
            // Modifier is .IsVisible(bool); .Set(...) writes Visibility (enum).
            // The codefix needs an enum→bool translation, so it stays out of the
            // POOL_001 auto-fix set. It is instead handled by REACTOR_VIS_001 — a
            // separate descriptor on PoolResetSetAnalyzer with its own
            // SetVisibilityCodeFix — so it deliberately remains excluded here.
            { "Visibility", "different signature (enum vs bool); handled by REACTOR_VIS_001 + SetVisibilityCodeFix" },

            // No exact-name modifier exists, and Reactor uses Tag internally
            // to attach its element record — user .Set writes here are wrong
            // for a different reason (TASK-060 / Reconciler.ClearElementTag).
            { "Tag", "framework-internal — Reactor stores its element record here" },

            // No matching modifier; transform pipeline goes through Animate /
            // Scale / Rotation / Translation modifiers instead.
            { "RenderTransform", "no modifier; use Scale/Rotation/Translation modifiers" },

            // No matching modifier; FlowDirection is set on the root via app
            // configuration, not via a per-element modifier.
            { "FlowDirection", "no modifier; root-level concern" },

            // The same-named modifiers take ElementRef cells, not raw FrameworkElement
            // values. Direct .Set writes are non-reactive reference snapshots; those
            // should be replaced with ref-edge modifiers by hand (and REACTOR_REF_001
            // catches the common ElementRef.Current form), not auto-fixed by the pool
            // analyzer.
            { "XYFocusUp", "modifier takes ElementRef, not FrameworkElement" },
            { "XYFocusDown", "modifier takes ElementRef, not FrameworkElement" },
            { "XYFocusLeft", "modifier takes ElementRef, not FrameworkElement" },
            { "XYFocusRight", "modifier takes ElementRef, not FrameworkElement" },

            // No matching modifier — the framework sets IsHitTestVisible imperatively
            // (chart label/tick subtree hiding, issue #162) and the pool resets it
            // alongside IsTabStop. There is deliberately no user-facing .IsHitTestVisible
            // modifier, so there is nothing to trap; documented here so the reset is
            // recognized as intentional rather than an oversight.
            { "IsHitTestVisible", "no modifier; framework-internal, reset for chart-label hiding (#162)" },
        };

    [Fact]
    public void Every_TrappedProperty_Is_Reset_In_CleanElement()
    {
        var resetProps = ReadResetProperties();

        foreach (var prop in PoolResetSetAnalyzer.TrappedProperties.Keys)
        {
            Assert.True(
                resetProps.Contains(prop),
                $"'{prop}' is in PoolResetSetAnalyzer.TrappedProperties but is " +
                $"NOT reset in ElementPool.CleanElement. Either remove it from " +
                $"TrappedProperties or add a reset for it in CleanElement.");
        }
    }

    [Fact]
    public void Every_TrappedProperty_Has_A_Matching_Modifier()
    {
        var modifierNames = ReadModifierNames();

        foreach (var (prop, modifier) in PoolResetSetAnalyzer.TrappedProperties)
        {
            Assert.True(
                modifierNames.Contains(modifier),
                $"'{prop}' maps to modifier '.{modifier}(...)' in " +
                $"PoolResetSetAnalyzer.TrappedProperties, but no such " +
                $"extension method exists in ElementExtensions.cs. The " +
                $"codefix would produce code that doesn't compile.");
        }
    }

    [Fact]
    public void Every_Reset_Property_With_Matching_Modifier_Is_Tracked()
    {
        // This is the load-bearing invariant: if someone adds a new
        // property to CleanElement's reset list, and ElementExtensions already
        // has a same-named modifier, then PoolResetSetAnalyzer MUST flag
        // .Set writes to that property — otherwise the trap is silent.
        var resetProps = ReadResetProperties();
        var modifierNames = ReadModifierNames();
        var tracked = PoolResetSetAnalyzer.TrappedProperties.Keys;

        var missing = resetProps
            .Where(prop =>
                !IntentionallyExcluded.ContainsKey(prop) &&
                modifierNames.Contains(prop) &&
                !tracked.Contains(prop))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These properties are reset in ElementPool.CleanElement AND have " +
            "a matching '.PROP(...)' modifier in ElementExtensions.cs, but " +
            "are NOT in PoolResetSetAnalyzer.TrappedProperties: " +
            $"[{string.Join(", ", missing)}]. " +
            "Either add them to TrappedProperties (so REACTOR_POOL_001 fires " +
            "on .Set writes to them), or — if intentional — add them to " +
            "IntentionallyExcluded in this test with a documented reason.");
    }

    /// <summary>
    /// Table-driven exercise of every entry in <see cref="PoolResetSetAnalyzer.TrappedProperties"/>:
    /// for each, prove the analyzer fires on the corresponding <c>.Set</c>
    /// lambda. This keeps the regular-test count growing automatically as
    /// new entries land, instead of relying on hand-written per-prop tests.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTrappedProperties))]
    public async Task Analyzer_Fires_For_Every_TrappedProperty(string propName, string modifierName)
    {
        _ = modifierName; // not consumed here; pinned by Every_TrappedProperty_Has_A_Matching_Modifier
        var stubs = BuildStubs();
        // A concrete value, not `default!`: the analyzer deliberately skips null/default
        // right-hand sides (ApplyModifiers treats a null modifier value as "no modifier
        // supplied", so the rewrite would not perform the write), and it now sees through
        // casts and the null-forgiving operator to recognise them. BuildStubs declares every
        // property as `object?`, so one literal serves every row.
        var source = stubs + $@"
class C
{{
    void M()
    {{
        var el = new FakeElement();
        {{|REACTOR_POOL_001:el.Set(fe => fe.{propName} = ""v"")|}};
    }}
}}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    public static IEnumerable<object[]> AllTrappedProperties() =>
        PoolResetSetAnalyzer.TrappedProperties
            .Select(kvp => new object[] { kvp.Key, kvp.Value });

    // ── Attached properties ──────────────────────────────────────────────────

    [Fact]
    public void Every_TrappedAttachedProperty_Is_Reset_In_CleanElement()
    {
        var resetAttached = ReadResetAttachedProperties();

        foreach (var key in PoolResetSetAnalyzer.TrappedAttachedProperties.Keys)
        {
            Assert.True(
                resetAttached.Contains(key),
                $"'{key}' is in ModifierTable.AttachedProperties but is NOT cleared in " +
                "ElementPool.CleanElement's FE-common block. REACTOR_POOL_001 claims the " +
                "write is lost on pool return; if it isn't, the diagnostic is wrong. Either " +
                "drop the entry or add the ClearValue for it in CleanElement.");
        }
    }

    [Fact]
    public void Every_Reset_Attached_Property_Is_Classified()
    {
        // The attached half of the load-bearing invariant, and the reason this file no longer
        // filters AutomationProperties.* / FlexPanel.* out of the reset scan.
        //
        // Stronger than the instance version, which only demands a decision when a same-named
        // modifier exists: attached modifiers are routinely renamed (LandmarkType ->
        // .Landmark, FlexPanel.Grow -> .Flex(grow:)), so a name-matching filter would let a
        // new reset slip through unnoticed. Every attached ClearValue must be classified.
        var resetAttached = ReadResetAttachedProperties();
        var tracked = PoolResetSetAnalyzer.TrappedAttachedProperties.Keys;

        var missing = resetAttached
            .Where(key => !tracked.Contains(key))
            .Where(key => !ModifierTable.DeliberatelyExcludedAttached.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // Name the mixed-owner case explicitly. A `Grid.Row` reset reaching this list is the
        // #1067 shape, and the surrounding evidence points the wrong way: Grid is in
        // InstancePropertyOwnerProbes, and two other Grid.* clears one line above are instance
        // properties nothing complains about. Without this sentence the obvious "fix" is to
        // declare the owner instance-only again, which is what silenced the failure in the
        // first place.
        var mixedOwner = missing
            .Where(key => InstancePropertyOwnerProbes.ContainsKey(key.Substring(0, key.IndexOf('.'))))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These attached properties are cleared in ElementPool.CleanElement but are in " +
            "neither ModifierTable.AttachedProperties nor DeliberatelyExcludedAttached: " +
            $"[{string.Join(", ", missing)}]. " +
            "Either map them (so REACTOR_POOL_001 fires on '.Set(fe => Owner.SetPROP(fe, ...))'), " +
            "or exclude them with a documented reason." +
            (mixedOwner.Count == 0
                ? string.Empty
                : $" Note: [{string.Join(", ", mixedOwner)}] — each sits on a MIXED owner, one whose " +
                  "other clears really are instance properties (Grid.Padding), which is why the " +
                  "owner is in InstancePropertyOwnerProbes. That is not a reason to reclassify " +
                  "them: they declare the static Owner.SetPROP setter, so they are attached and " +
                  "belong in one of the two tables above."));
    }

    [Theory]
    // Grid is THE mixed owner: these four are attached, and are already cleared for pooled
    // reuse by PanelAttachedHooks.ApplyGridAttached — one of them moving into CleanElement is
    // the change #1067 says must not pass silently.
    [InlineData("Grid", "Row", true)]
    [InlineData("Grid", "Column", true)]
    [InlineData("Grid", "RowSpan", true)]
    [InlineData("Grid", "ColumnSpan", true)]
    // …and the same owner's instance DPs, which CleanElement clears today and which must stay
    // out of the attached bucket.
    [InlineData("Grid", "Padding", false)]
    [InlineData("Grid", "CornerRadius", false)]
    // Control is mixed too — proof the probe generalizes past the owner that motivated it.
    [InlineData("Control", "IsTemplateFocusTarget", true)]
    [InlineData("Control", "IsEnabled", false)]
    [InlineData("Control", "Padding", false)]
    [InlineData("FrameworkElement", "Margin", false)]
    [InlineData("Panel", "Background", false)]
    [InlineData("TextBlock", "Padding", false)]
    public void Attached_Setter_Probe_Separates_Attached_From_Instance_On_The_Same_Owner(
        string owner, string property, bool expectedAttached)
    {
        // The instrument check for IsAttachedReset. Every other assertion in this file trusts
        // the probe, and a dead probe — wrong BindingFlags, a projection that stops surfacing
        // the static setters — fails silently in the one direction that matters: it answers
        // "not attached" for everything, which is exactly today's owner-keyed behaviour with
        // every test still green. So assert both directions on the same owner, where the only
        // thing that differs between the rows is the property.
        Assert.True(InstancePropertyOwnerProbes.ContainsKey(owner),
            $"'{owner}' is not in InstancePropertyOwnerProbes, so this row proves nothing about " +
            "the probe — IsAttachedReset short-circuits to 'attached' for unknown owners.");

        Assert.Equal(expectedAttached, IsAttachedReset(owner, property));
    }

    [Fact]
    public void Every_Instance_Owner_Key_Names_Its_Own_Type()
    {
        // The keys are how ElementPool.cs spells an owner; the typeof is what the probe reads.
        // Nothing else ties the two together, and a mismatched pair
        // (["Grid"] = typeof(StackPanel)) would probe a type with no Grid.SetRow and answer
        // "instance" for every attached Grid property — the original bug, restored.
        var mismatched = InstancePropertyOwnerProbes
            .Where(entry => !string.Equals(entry.Key, entry.Value.OwnerType.Name, StringComparison.Ordinal))
            .Select(entry => $"'{entry.Key}' -> {entry.Value.OwnerType.FullName}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            mismatched.Count == 0,
            "These InstancePropertyOwnerProbes entries are keyed by a name their typeof does not " +
            $"have: [{string.Join(", ", mismatched)}]. The key must be the type's simple name — " +
            "that is the spelling the CleanElement scan captures, and the probe is only meaningful " +
            "when it interrogates the type that owns the property being classified.");
    }

    /// <summary>
    /// No <c>DeliberatelyExcludedAttached</c> row may name an owner that
    /// <see cref="InstancePropertyOwnerProbes"/> classifies per property — and every key must stay
    /// in the <c>Owner.Property</c> form both that check and the owner split in
    /// <see cref="Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names"/> read it as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two tables answer different questions and a key in both is wrong in <em>either</em>
    /// direction, which is why owner membership — not <see cref="IsAttachedReset"/> — is the
    /// predicate. If the property has a static <c>Owner.SetPROP</c>, the probe calls it attached,
    /// <c>REACTOR_POOL_001</c> can match a <c>.Set(...)</c> write to it, and it belongs in
    /// <c>AttachedProperties</c> where the analyzer will use it. If it has none, the probe calls it
    /// an instance DP, it never reaches the attached scan at all, and the row suppresses nothing.
    /// </para>
    /// <para>
    /// The inert case is the dangerous one, because inert is not harmless: the row is a standing
    /// allow-list entry for that owner, so a genuinely attached <c>Grid.*</c> reset added later
    /// lands beside it and reads as already-triaged. <c>Grid.Padding</c>, <c>Grid.CornerRadius</c>
    /// and <c>StackPanel.CornerRadius</c> sat in that list on <c>main</c> until #1015 removed them
    /// in favour of classifying the owners (#1048), so this branch's list is a strict
    /// <em>subset</em> of the older one — and a merge that reconciles the two by taking the other
    /// side's rows restores all three. Git merges the file cleanly and nothing objects, which is
    /// #1066.
    /// </para>
    /// <para>
    /// MEASURED at <c>2b4385f7</c>, before this test existed: restoring <c>["Grid.Padding"]</c>
    /// reddened exactly one test, <c>Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names</c>
    /// (this class plus <c>ModifierTableIntegrityTests</c> went 87/0 -> 86/1), and its message
    /// reads "found no ClearValue at all for these owners: [Grid] … or
    /// <c>ReadResetAttachedProperties</c>' regex no longer matches" — a true statement pointing at
    /// the wrong repair. That neighbour also only fires while the probed owners happen to be
    /// disjoint from the owners with attached clears; give <c>Grid</c> one attached clear in the
    /// scanned block (the #1067 shape) and it goes quiet while the bogus row survives. This test
    /// depends on neither coincidence, and was measured failing under <c>--filter</c> on its own
    /// name so its verdict is not borrowed from that neighbour.
    /// </para>
    /// </remarks>
    [Fact]
    public void Excluded_Attached_Rows_Never_Name_An_Instance_Owner()
    {
        // Guard the join key before trusting a zero below. Both this check and the owner split in
        // Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names recover the owner as the text before
        // the first '.', so a table re-keyed with qualified owners resolves every row to
        // "Microsoft" and the membership assertion below reports zero offenders for all of them at
        // once — including any a merge restored.
        //
        // MEASURED, re-keying one row to
        // "Microsoft.UI.Xaml.Automation.AutomationProperties.DescribedBy": that is not silent
        // overall — Every_Reset_Attached_Property_Is_Classified reports DescribedBy as being in
        // neither table, and the scan test reports a missing owner "Microsoft" — but neither names
        // the key shape, and the one check that would have caught a restored Grid.* row is exactly
        // the one that goes quiet. So pin the spelling rather than let a green offender count stand
        // in for a check that is no longer running.
        var misshapen = ModifierTable.DeliberatelyExcludedAttached.Keys
            .Where(key => key.Split('.').Length != 2
                || key.StartsWith(".", StringComparison.Ordinal)
                || key.EndsWith(".", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            misshapen.Count == 0,
            "These ModifierTable.DeliberatelyExcludedAttached keys are not in 'Owner.Property' " +
            $"form: [{string.Join(", ", misshapen)}]. The owner is recovered as the text before " +
            "the first '.', so 'Microsoft.UI.Xaml.Automation.AutomationProperties.DescribedBy' " +
            "resolves to owner 'Microsoft', matches no probe, and empties the offender list below " +
            "for every row at once — including any a merge restored. Re-key the table in short " +
            "form, or update this test and Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names' " +
            "owner splits together.");

        var offenders = ExclusionRowsOnInstanceOwners(ModifierTable.DeliberatelyExcludedAttached.Keys);

        Assert.True(
            offenders.Count == 0,
            "These ModifierTable.DeliberatelyExcludedAttached rows name an owner that " +
            $"InstancePropertyOwnerProbes already classifies per property: [{string.Join(", ", offenders)}]. " +
            "Delete them. That list suppresses genuinely attached properties the " +
            "'Owner.SetPROP(x, v)' rule cannot match, and neither thing a probed owner's property " +
            "can be needs suppressing: if it declares the static setter it is matchable, so map it " +
            "in ModifierTable.AttachedProperties instead; if it does not, it is an ordinary " +
            "instance dependency property that never reaches the attached scan, so the row " +
            "suppresses nothing and merely pre-approves the next genuinely attached reset added " +
            "under the same owner.");
    }

    [Theory]
    // The three rows #1015 deleted from main — the exact mutation #1066 reproduces.
    [InlineData("Grid.Padding", true)]
    [InlineData("Grid.CornerRadius", true)]
    [InlineData("StackPanel.CornerRadius", true)]
    // Attached, on a mixed owner, and still offenders: Grid.SetRow and
    // Control.SetIsTemplateFocusTarget both exist, so the rule can match them and they belong in
    // AttachedProperties. Pins that the predicate is owner membership rather than IsAttachedReset,
    // which would wave both rows through.
    [InlineData("Grid.Row", true)]
    [InlineData("Control.IsTemplateFocusTarget", true)]
    // Non-offenders, because none of these owners is classified per property at all.
    // AutomationProperties.DescribedBy is a live DeliberatelyExcludedAttached row;
    // ToolTipService.ToolTip and FlexPanel.Grow are mapped in AttachedProperties instead. Both
    // sides of the attached split have to answer "false", since the predicate is owner membership
    // and being attached is not what makes a row an offender.
    [InlineData("AutomationProperties.DescribedBy", false)]
    [InlineData("ToolTipService.ToolTip", false)]
    [InlineData("FlexPanel.Grow", false)]
    // The spelling the key-shape assertion in the [Fact] above exists to reject, pinned here as a
    // measurement rather than left as a claim in a comment: a qualified owner resolves to
    // "Microsoft", matches no probe, and so the detector answers "not an offender" for a row that
    // plainly is one. This row and that assertion are one guard in two halves — teaching the
    // detector to split qualified names correctly should fail here, and should retire both.
    [InlineData("Microsoft.UI.Xaml.Controls.Grid.Row", false)]
    public void Instance_Owner_Exclusion_Detector_Distinguishes_Offenders_From_Legitimate_Rows(
        string key, bool expectedOffender)
    {
        // The instrument check for the [Fact] above, which is absence-shaped over a three-row table
        // whose owners are currently disjoint from the probed ones — so it reports zero today and
        // would report zero just as calmly if the detector stopped discriminating. Driving that same
        // detector with keys that must and must not match proves it can still answer both ways.
        //
        // This does NOT by itself make the [Fact] non-vacuous, and the division of labour matters:
        // these keys are literals, so a re-keyed real table leaves every row here green. Detecting
        // that is the [Fact]'s own 'Owner.Property' key-shape assertion; the qualified InlineData
        // row above is what establishes that the spelling it rejects really does mis-answer.
        Assert.Equal(
            expectedOffender,
            ExclusionRowsOnInstanceOwners(new[] { key }).Count == 1);
    }

    /// <summary>
    /// The <paramref name="keys"/> whose owner segment names an entry of
    /// <see cref="InstancePropertyOwnerProbes"/>, in ordinal order.
    /// </summary>
    private static List<string> ExclusionRowsOnInstanceOwners(IEnumerable<string> keys) =>
        keys.Where(key =>
                key.IndexOf('.') > 0
                && InstancePropertyOwnerProbes.ContainsKey(key.Substring(0, key.IndexOf('.'))))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names()
    {
        // Guards the scan itself, not the table. ReadResetAttachedProperties is a regex over
        // ElementPool.cs, and a change to how the owners are qualified there (an added alias,
        // a using directive that drops the prefix) could quietly stop matching a whole owner —
        // which would make Every_Reset_Attached_Property_Is_Classified pass vacuously.
        var scanned = ReadResetAttachedProperties()
            .Select(key => key.Substring(0, key.IndexOf('.')))
            .ToHashSet(StringComparer.Ordinal);

        var mapped = ModifierTable.AttachedProperties.Values
            .Select(info => info.Owner)
            .ToHashSet(StringComparer.Ordinal);

        var expected = mapped
            .Concat(ModifierTable.DeliberatelyExcludedAttached.Keys
                .Select(key => key.Substring(0, key.IndexOf('.'))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal)
            .ToList();

        var unseen = expected.Where(owner => !scanned.Contains(owner)).ToList();

        // An owner reaching this list *only* through an exclusion row is a different failure with a
        // different repair, and the sentence below would send its reader at the regex. That is the
        // one shape this test is measured to catch before Excluded_Attached_Rows_Never_Name_An_
        // Instance_Owner existed (see its remarks), so name the alternative rather than let the
        // scan-drift wording stand as the only reading.
        var exclusionOnly = unseen.Where(owner => !mapped.Contains(owner)).ToList();

        Assert.True(
            unseen.Count == 0,
            "The CleanElement attached-reset scan found no ClearValue at all for these owners: " +
            $"[{string.Join(", ", unseen)}]. Either the resets were removed (drop the table " +
            "entries) or ReadResetAttachedProperties' regex no longer matches how they are " +
            "written in ElementPool.cs." +
            (exclusionOnly.Count == 0
                ? string.Empty
                : $" Note: [{string.Join(", ", exclusionOnly)}] are named only by " +
                  "ModifierTable.DeliberatelyExcludedAttached, never by AttachedProperties. If the " +
                  "row was added to silence a classification failure rather than to suppress a " +
                  "genuinely unmatchable attached property, the repair is to delete the row, not " +
                  "to touch the scan — see Excluded_Attached_Rows_Never_Name_An_Instance_Owner."));
    }

    [Fact]
    public void Every_ClearValue_In_CleanElement_Is_Recognized_By_The_Reset_Scan()
    {
        // Both reset scans are regexes over the literal `RECEIVER.ClearValue(OWNER.PROPProperty)`
        // shape. A reset written any other way — `var dp = X.YProperty; fe.ClearValue(dp);`, or
        // a helper — is invisible to them, and an invisible reset makes
        // Every_Reset_Attached_Property_Is_Classified pass vacuously for the property it
        // clears. Counting is the cheap way to notice: the scan must account for every
        // ClearValue in the block, not just the ones it happens to parse.
        var commonBlock = ReadCleanElementCommonBlock(out _);

        var total = Regex.Matches(commonBlock, @"\.ClearValue\s*\(").Count;
        var recognized = Regex.Matches(commonBlock,
            @"\b\w+\.ClearValue\(\s*(?:[\w.]+\.)?(\w+)\.(\w+)Property\s*\)").Count;

        Assert.True(
            total > 0,
            "No ClearValue calls found in CleanElement's FE-common block — the block boundary " +
            "detection in ReadCleanElementCommonBlock has probably drifted.");

        Assert.True(
            total == recognized,
            $"CleanElement's FE-common block has {total} ClearValue call(s) but the reset scan " +
            $"only recognizes {recognized}. A reset written in a shape the regex does not match " +
            "(a local dependency-property alias, a helper method) is silently excluded from the " +
            "REACTOR_POOL_001 consistency invariants. Either write it in the " +
            "'receiver.ClearValue(Owner.PropProperty)' form, or teach ReadResetProperties / " +
            "ReadResetAttachedProperties about the new shape.");
    }

    /// <summary>
    /// Table-driven exercise of every entry in
    /// <see cref="PoolResetSetAnalyzer.TrappedAttachedProperties"/>: for each, prove the
    /// analyzer fires on <c>.Set(fe =&gt; Owner.SetPROP(fe, value))</c>. Each owner is stubbed
    /// in its <em>real</em> namespace, so this also pins the namespace check positively — the
    /// negative half lives in <c>PoolResetSetAnalyzerTests</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTrappedAttachedProperties))]
    public async Task Analyzer_Fires_For_Every_TrappedAttachedProperty(string key, string modifierName)
    {
        _ = modifierName; // pinned by ModifierTableIntegrityTests against the real DSL.
        var info = ModifierTable.AttachedProperties[key];
        var source = BuildAttachedStubs() + $@"
class C
{{
    void M()
    {{
        var el = new FakeElement();
        {{|REACTOR_POOL_001:el.Set(fe => {info.OwnerNamespace}.{info.Owner}.{info.Setter}(fe, ""v""))|}};
    }}
}}";

        await new CSharpAnalyzerTest<PoolResetSetAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    public static IEnumerable<object[]> AllTrappedAttachedProperties() =>
        PoolResetSetAnalyzer.TrappedAttachedProperties
            .Select(kvp => new object[] { kvp.Key, kvp.Value });

    /// <summary>
    /// Stub preamble declaring every attached owner named by
    /// <c>ModifierTable.AttachedProperties</c> in its real namespace, with an
    /// <c>object</c>-typed two-argument setter per entry. The analyzer matches on syntax plus
    /// the owner's containing namespace, so this is sufficient — and generating the setters
    /// from the table means a wrong <c>Setter</c> name would produce a stub the test source
    /// cannot call.
    /// </summary>
    private static string BuildAttachedStubs()
    {
        var owners = ModifierTable.AttachedProperties.Values
            .GroupBy(info => info.OwnerNamespace + "." + info.Owner, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var declarations = string.Join("\n", owners.Select(group =>
        {
            var first = group.First();
            var setters = string.Join(
                "\n        ",
                group.Select(info => info.Setter)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(setter => setter, StringComparer.Ordinal)
                    .Select(setter => $"public static void {setter}(object target, object value) {{ }}"));

            return $@"
namespace {first.OwnerNamespace}
{{
    public static class {first.Owner}
    {{
        {setters}
    }}
}}";
        }));

        return $@"
using System;
using Microsoft.UI.Reactor;

#nullable enable

namespace Microsoft.UI.Xaml.Controls
{{
    // The .Set receiver has to be a control ElementPool actually recycles: REACTOR_POOL_001
    // claims the attached write is cleared on pool return, which is only true of one.
    public class Button {{ }}
}}

namespace Microsoft.UI.Reactor
{{
    public class FakeElement
    {{
        public FakeElement Set(Action<Microsoft.UI.Xaml.Controls.Button> configure) {{ configure(new Microsoft.UI.Xaml.Controls.Button()); return this; }}
    }}
}}
{declarations}
";
    }

    // ── Source-scanning helpers ─────────────────────────────────────────

    /// <summary>
    /// Extract the set of <em>instance</em> property names reset in the FE-common block of
    /// <c>ElementPool.CleanElement</c> — from the method's opening brace up
    /// to (but not including) the <c>switch (fe)</c> that begins type-specific
    /// cleanup. Captures both <c>fe.PROP = ...</c> direct sets and every
    /// <c>RECEIVER.ClearValue(OWNER.PROPProperty)</c> that <see cref="IsAttachedReset"/>
    /// classifies as an instance property.
    /// </summary>
    private static HashSet<string> ReadResetProperties()
    {
        var commonBlock = ReadCleanElementCommonBlock(out var paramName);

        // ClearValue() is a method call caught separately by the second regex;
        // filter it out of the direct-assignment match set.
        var escapedParam = Regex.Escape(paramName);
        var directAssignments = Regex.Matches(commonBlock, $@"\b{escapedParam}\.(\w+)\s*=")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(name => name != "ClearValue");

        // ClearValue(OWNER.PROPProperty) resets. Receiver is `\w+` (not pinned to
        // the captured param) because some resets run on a narrowed cast — e.g.
        // `if (fe is Control c) c.ClearValue(Control.IsTabStopProperty)` (issue #162).
        // Only the clears IsAttachedReset says are instance properties: attached owners
        // are captured separately by ReadResetAttachedProperties, owner-qualified, because
        // their bare names collide with instance properties (AutomationProperties.Name vs
        // FrameworkElement.Name).
        var clearValueProps = Regex.Matches(commonBlock, ClearValuePattern)
            .Cast<Match>()
            .Where(m => !IsAttachedReset(m.Groups[1].Value, m.Groups[2].Value))
            .Select(m => m.Groups[2].Value);

        return new HashSet<string>(directAssignments.Concat(clearValueProps), StringComparer.Ordinal);
    }

    /// <summary>
    /// Extract the <c>Owner.Property</c> names of the <em>attached</em> properties reset in
    /// the FE-common block of <c>ElementPool.CleanElement</c> — every
    /// <c>RECEIVER.ClearValue(...OWNER.PROPProperty)</c> that <see cref="IsAttachedReset"/>
    /// classifies as attached; the exact complement of <see cref="ReadResetProperties"/>'
    /// <c>ClearValue</c> half, over the same matches.
    /// </summary>
    /// <remarks>
    /// The owner may be written with any amount of qualification in the source
    /// (<c>Microsoft.UI.Xaml.Automation.AutomationProperties</c>, <c>WinUI.ToolTipService</c>,
    /// <c>Layout.FlexPanel</c>), so only the rightmost segment before the property is kept —
    /// which is exactly how <c>ModifierTable.AttachedProperties</c> is keyed, and how the
    /// analyzer sees the owner at a call site.
    /// </remarks>
    private static HashSet<string> ReadResetAttachedProperties()
    {
        var commonBlock = ReadCleanElementCommonBlock(out _);

        var attached = Regex.Matches(commonBlock, ClearValuePattern)
            .Cast<Match>()
            .Where(m => IsAttachedReset(m.Groups[1].Value, m.Groups[2].Value))
            .Select(m => m.Groups[1].Value + "." + m.Groups[2].Value);

        return new HashSet<string>(attached, StringComparer.Ordinal);
    }

    /// <summary>
    /// Matches <c>RECEIVER.ClearValue(OWNER.PROPProperty)</c>, capturing the owner's rightmost
    /// segment and the bare property name. The single shape both reset scans read, so that
    /// instance ∪ attached is every recognized clear by construction and
    /// <see cref="IsAttachedReset"/> is the only thing that decides which half a clear lands in.
    /// </summary>
    /// <remarks>
    /// The optional <c>[\w.]+.</c> prefix absorbs whatever qualification the source uses
    /// (<c>Microsoft.UI.Xaml.Automation.AutomationProperties</c>, <c>WinUI.Border</c>,
    /// <c>Layout.FlexPanel</c>) — the rightmost segment is how
    /// <c>ModifierTable.AttachedProperties</c> is keyed and how the analyzer sees the owner at a
    /// call site. <c>Every_ClearValue_In_CleanElement_Is_Recognized_By_The_Reset_Scan</c> pins
    /// that every clear in the block really is written in this shape.
    /// </remarks>
    private const string ClearValuePattern =
        @"\b\w+\.ClearValue\(\s*(?:[\w.]+\.)?(\w+)\.(\w+)Property\s*\)";

    /// <summary>
    /// Whether <c>OWNER.PROPProperty</c> names an <em>attached</em> dependency property —
    /// the one discriminator that splits the reset scan in two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owner membership alone cannot answer this, which is issue #1067: <c>Grid</c> is a
    /// <em>mixed</em> owner. <c>Grid.Padding</c> / <c>Grid.CornerRadius</c> are ordinary instance
    /// DPs (which is why <c>Grid</c> is in <see cref="InstancePropertyOwnerProbes"/>) while
    /// <c>Grid.Row</c> / <c>Column</c> / <c>RowSpan</c> / <c>ColumnSpan</c> are genuinely attached.
    /// Keyed by owner, a <c>Grid.Row</c> clear was absorbed by the instance bucket and
    /// <c>Every_Reset_Attached_Property_Is_Classified</c> never saw it — no failure, so no triage
    /// moment to get wrong. That is not hypothetical: <c>PanelAttachedHooks.ApplyGridAttached</c>
    /// already clears all four for pooled reuse, just outside the scanned region.
    /// </para>
    /// <para>
    /// So ask the property, not the owner, and ask it the same question the analyzer asks: an
    /// attached property is one whose owner declares the static
    /// <c>Owner.SetPROP(DependencyObject, value)</c> that <c>PoolResetSetAnalyzer</c> matches
    /// inside a <c>.Set(...)</c> lambda. <c>Grid.SetRow</c> exists, so <c>Grid.Row</c> is attached;
    /// there is no <c>Grid.SetPadding</c>, so <c>Grid.Padding</c> stays instance. A future mixed
    /// owner needs no edit here.
    /// </para>
    /// <para>
    /// Both error directions are not equal, and the bias is deliberate. Calling an instance
    /// property attached fails <c>Every_Reset_Attached_Property_Is_Classified</c> loudly and
    /// someone triages it; calling an attached property instance is silent — the whole bug. So
    /// anything unresolvable resolves to attached: an owner absent from the probe table is
    /// attached by default.
    /// </para>
    /// </remarks>
    private static bool IsAttachedReset(string owner, string property) =>
        !InstancePropertyOwnerProbes.TryGetValue(owner, out var probe)
        || probe.DeclaresAttachedSetter(property);

    /// <summary>
    /// The <c>DependencyObject</c> base types that back <em>FrameworkElement instance</em>
    /// properties in <c>CleanElement</c>'s FE-common block, each paired with a metadata probe
    /// over the real type so <see cref="IsAttachedReset"/> can carve the attached DPs back out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #985: CleanElement's FE-common block clears the Padding / CornerRadius /
    /// BorderThickness / BorderBrush / Background family through a Control | Border |
    /// Panel/Grid/StackPanel | TextBlock chain that mirrors ApplyModifiers' receivers.
    /// <c>Border.PaddingProperty</c> and friends are ordinary instance properties; without them
    /// here the attached scan would claim them and
    /// <c>Every_Reset_Attached_Property_Is_Classified</c> would fail on owners that have no
    /// business being in the attached table. <c>Grid</c> arrived with #1003, which widened those
    /// gates to the concrete panels; <c>TextBlock</c> because #985 moved TextBlock.Padding (added
    /// by #950) into the scanned block.
    /// </para>
    /// <para>
    /// Two hazards live here and this list only ever closed the first. Route the family through
    /// <c>DeliberatelyExcludedAttached</c> instead and a future attached <c>Grid.*</c> reset does
    /// fail the classification test — but the two existing <c>Grid.*</c> suppression rows sitting
    /// right there invite the wrong triage ("add another row"). Route it through owner membership,
    /// as this list does, and that same future reset produces <em>no failure at all</em>, because
    /// bare-owner membership cannot express that <c>Grid</c> owns instance <em>and</em> attached
    /// DPs (#1067). Naming the owners is therefore necessary but not sufficient:
    /// <see cref="IsAttachedReset"/> asks per property, and this list supplies the type it asks.
    /// </para>
    /// <para>
    /// Hand-listed as <c>typeof(...)</c> literals rather than resolved from a name via
    /// <c>Type.GetType</c>, so the probes stay statically analyzable (IL2057/IL2072) and adding an
    /// owner is a deliberate edit — the same rationale as <c>ModifierTableIntegrityTests</c>'
    /// <c>KnownAttachedOwners</c>. The key must equal the type's simple name, which is exactly how
    /// the scan spells an owner; <c>Every_Instance_Owner_Key_Names_Its_Own_Type</c> pins that.
    /// Reflection reads metadata only — no WinUI object is constructed, which this headless suite
    /// cannot do.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, (Type OwnerType, Func<string, bool> DeclaresAttachedSetter)>
        InstancePropertyOwnerProbes =
            new Dictionary<string, (Type, Func<string, bool>)>(StringComparer.Ordinal)
            {
                ["FrameworkElement"] = ProbeFor(typeof(Microsoft.UI.Xaml.FrameworkElement)),
                ["UIElement"] = ProbeFor(typeof(Microsoft.UI.Xaml.UIElement)),
                ["Control"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Control)),
                ["Border"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Border)),
                ["Panel"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Panel)),
                ["StackPanel"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.StackPanel)),
                ["Grid"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.Grid)),
                ["TextBlock"] = ProbeFor(typeof(Microsoft.UI.Xaml.Controls.TextBlock)),
            };

    /// <summary>
    /// One entry of <see cref="InstancePropertyOwnerProbes"/>, with the probe derived from the very
    /// <see cref="Type"/> stored beside it.
    /// </summary>
    /// <remarks>
    /// Naming the owner twice per entry — once for <c>OwnerType</c>, once inside the probe — would
    /// let the two drift apart, and <c>Every_Instance_Owner_Key_Names_Its_Own_Type</c> would not
    /// notice: it pins the key to <c>OwnerType</c> and never looks at what the probe reads. A pair
    /// like <c>(typeof(Grid), AttachedSetterProbe(typeof(StackPanel)))</c> answers "instance" for
    /// every attached <c>Grid</c> property, which is #1067 restored. For <c>Grid</c> and
    /// <c>Control</c> the probe theory catches it, but the other six owners carry no
    /// attached-expecting row, so there the whole class of mistake is silent. Taking the type once
    /// and deriving both from it makes the mismatch inexpressible rather than merely tested for.
    /// </remarks>
    private static (Type OwnerType, Func<string, bool> DeclaresAttachedSetter) ProbeFor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
        => (type, AttachedSetterProbe(type));

    /// <summary>
    /// A probe over <paramref name="type"/>'s <c>public static void SetPROP(target, value)</c>
    /// declarations — the shape <c>PoolResetSetAnalyzer</c> matches, so this asks the rule's own
    /// question about a property rather than an approximation of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reflection pass runs once per owner, when this map initializes; the returned probe is
    /// an ordinal set lookup, so classifying a <c>ClearValue</c> match costs no reflection and the
    /// per-owner method list is never re-materialized.
    /// </para>
    /// <para>
    /// <c>FlattenHierarchy</c> is deliberate: an attached setter inherited from a base is still an
    /// attached setter, and the direction it can err in (instance read as attached) is the loud
    /// one. The one shape this misses is an attached property with <em>no</em> static setter — the
    /// <c>AutomationProperties.DescribedBy</c> collection form, which WinUI exposes as
    /// <c>GetXxx(...)</c> returning a mutable list. None of the owners above has one, and it is
    /// precisely the shape the <c>Owner.SetPROP(x, v)</c> rule cannot match either, which is why
    /// those three live in <c>ModifierTable.DeliberatelyExcludedAttached</c>.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> shared with <c>ModifierTableIntegrityTests.HasStaticTwoArgMethod</c>,
    /// which looks similar but asks a weaker question: it omits <c>FlattenHierarchy</c> and checks
    /// neither the <c>void</c> return nor that the first parameter is a <c>DependencyObject</c> —
    /// enough for the pure attached-property holders it runs against (<c>AutomationProperties</c>,
    /// <c>ToolTipService</c>, <c>TitleBar</c>, <c>FlexPanel</c>), where no instance member can
    /// collide. This probe runs against <em>mixed</em> owners, where a loose match silently
    /// reclassifies a property, so the extra constraints are the point. Reusing the looser helper
    /// here would reintroduce #1067 by a different route; unifying them would have to tighten it
    /// for callers that do not need it.
    /// </para>
    /// </remarks>
    private static Func<string, bool> AttachedSetterProbe(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        var setterNames = type.GetMethods(Flags)
            .Where(method =>
                method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 2 } parameters
                && typeof(Microsoft.UI.Xaml.DependencyObject).IsAssignableFrom(parameters[0].ParameterType))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        return propertyName => setterNames.Contains("Set" + propertyName);
    }

    /// <summary>
    /// The FE-common block of <c>ElementPool.CleanElement</c> — from the method's opening
    /// brace up to (but not including) the <c>switch (fe)</c> that begins type-specific
    /// cleanup — plus the name of the method's parameter.
    /// </summary>
    private static string ReadCleanElementCommonBlock(out string paramName)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        // Path.Join (vs Path.Combine) avoids the "rooted segment silently
        // discards the base path" behavior flagged by CodeQL cs/path-combine.
        // All segments here are hardcoded literals, so the warning is a
        // false positive — but the equivalent Path.Join keeps the analyzer
        // quiet and is otherwise identical for non-rooted segments.
        var path = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(path), $"ElementPool.cs not found at {path}");
        var source = File.ReadAllText(path);

        // Locate `(internal|private|...) static void CleanElement(FrameworkElement <param>)`,
        // capturing the parameter name. Matching by signature shape — not by the
        // exact `(FrameworkElement fe)` string — keeps the test robust to harmless
        // renames or spacing changes.
        var sigMatch = Regex.Match(source,
            @"static\s+void\s+CleanElement\s*\(\s*FrameworkElement\s+(\w+)\s*\)");
        Assert.True(sigMatch.Success,
            "Could not locate CleanElement(FrameworkElement) signature in ElementPool.cs — has it been removed or had its type changed?");
        paramName = sigMatch.Groups[1].Value;

        var braceStart = source.IndexOf('{', sigMatch.Index + sigMatch.Length);
        Assert.True(braceStart > sigMatch.Index, "CleanElement opening brace not found");

        // The FE-common block runs from the opening brace up to the first
        // `switch (<param>)` that starts the type-specific cleanup. Anchored to the start
        // of a line (Multiline) so a `//` comment mentioning the dispatch cannot masquerade
        // as the boundary — an unanchored match truncated the scanned region at a doc comment
        // once, which silently shrank every invariant built on this block.
        //
        // The anchor closes the `//` case, not every case: a block comment whose inner line
        // BEGINS with the dispatch text still matches `^\s*switch` and truncates the region
        // (MEASURED: 12922 -> 7335 chars). Truncation cannot fail an absence-shaped assertion
        // — a smaller region holds fewer offenders — so the detectors are the presence-shaped
        // ones: Every_TrappedProperty_Is_Reset_In_CleanElement,
        // Every_TrappedAttachedProperty_Is_Reset_In_CleanElement and
        // Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names in this file all redden on that
        // mutation (54/0 -> 54/3), as does
        // ModifierUnsetClearValueTests.CleanElement_Releases_Every_Modifier_Backed_Dependency_Property.
        // They are load-bearing for this helper's correctness, not just for their own subject.
        var switchRegex = new Regex(
            $@"^\s*switch\s*\(\s*{Regex.Escape(paramName)}\s*\)", RegexOptions.Multiline);
        var switchMatch = switchRegex.Match(source, braceStart);
        Assert.True(switchMatch.Success,
            $"CleanElement layout changed — could not find 'switch ({paramName})' boundary after the opening brace.");

        return source.Substring(braceStart, switchMatch.Index - braceStart);
    }

    /// <summary>
    /// Extract the set of modifier method names defined in
    /// <c>ElementExtensions.cs</c> — any <c>public static T Name&lt;T&gt;(this T el, ...)</c>.
    /// </summary>
    private static HashSet<string> ReadModifierNames()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        // Path.Join — see ReadResetProperties for the cs/path-combine rationale.
        var path = Path.Join(root!, "src", "Reactor", "Elements", "ElementExtensions.cs");
        Assert.True(File.Exists(path), $"ElementExtensions.cs not found at {path}");
        var source = File.ReadAllText(path);

        var names = Regex.Matches(source, @"public\s+static\s+T\s+(\w+)\s*<T>\s*\(\s*this\s+T\s+\w+")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value);

        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    /// <summary>
    /// Build a stub C# preamble that declares <c>FakeElement</c> with a
    /// public field for every property in <c>TrappedProperties</c>, so the
    /// table-driven analyzer test can compile uniformly. Uses <c>object?</c>
    /// fields with <c>default!</c> assignment — analyzer matches on syntax,
    /// not types, so this is sufficient.
    /// </summary>
    /// <remarks>
    /// One exception to "syntax, not types": a trapped property that also declares a
    /// <c>ControlGate</c> (Padding / CornerRadius / BorderThickness / BorderBrush /
    /// Background, since issue #985) is only reported when the <c>.Set</c> lambda
    /// parameter's type inherits from one of the gate's control types in
    /// <c>Microsoft.UI.Xaml.Controls</c>. <c>FakeElement</c> therefore derives from a stub
    /// <c>Control</c>, which satisfies every gate currently declared on a pool-reset row.
    /// Without it the analyzer would stop firing for those rows and
    /// <c>Analyzer_Fires_For_Every_TrappedProperty</c> would fail loudly, because it asserts
    /// the diagnostic is <em>present</em> — that positive shape is what keeps a lost gate
    /// from reading as a pass. Keep the marker, keep the base type.
    /// </remarks>
    private static string BuildStubs()
    {
        var fields = string.Join(
            "\n    ",
            PoolResetSetAnalyzer.TrappedProperties.Keys
                .Select(p => $"public object? {p};"));

        return $@"
using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml.Controls;

#nullable enable

namespace Microsoft.UI.Xaml.Controls
{{
    public class Control {{ }}

    // The .Set receiver has to be a control ElementPool actually recycles, because that is
    // exactly what REACTOR_POOL_001 asserts. Button is in PoolableTypes and derives Control,
    // so it also satisfies every control gate these trapped properties declare.
    public class Button : Control
    {{
        {fields}
    }}
}}

namespace Microsoft.UI.Reactor
{{
    public class FakeElement
    {{
        public FakeElement Set(Action<Button> configure) {{ configure(new Button()); return this; }}
    }}
}}
";
    }
}
