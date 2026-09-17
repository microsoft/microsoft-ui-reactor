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

    /// <summary>
    /// Every property <c>CleanElement</c> resets that has a Reactor modifier must be
    /// <em>classified</em> — mapped in <c>ModifierTable.Properties</c> at whatever severity, or
    /// listed in <c>DeliberatelyExcluded</c> with a reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The drift guard issue #1193 asked for, and the reason it now catches something: it reads
    /// <see cref="CleanElementScan"/>, which covers the whole method, where this test previously
    /// read only the FE-common block and so could not see a single reset in the type dispatch.
    /// <c>StretchDirection</c> and <c>IsActive</c> were in neither table when the scan was widened
    /// — reset on a pooled control, modifier sitting in <c>ElementExtensions.cs</c>, and no
    /// diagnostic offering it — because the staleness tests' WinUI probe never reaches
    /// <c>Viewbox</c> or <c>ProgressRing</c>.
    /// </para>
    /// <para>
    /// <b>Classification, not severity.</b> This deliberately does not require
    /// <c>poolReset: true</c>, which is what an earlier draft of the #1193 fix did — it would have
    /// promoted fifteen properties from MOD_002 (Info) to POOL_001 (Warning), a build break for
    /// consumers on <c>TreatWarningsAsErrors</c>, on a premise the code does not support.
    /// <c>CleanElement</c> runs on pool <em>return</em>, and the next mount re-applies setters:
    /// <c>DescriptorHandler.Mount</c> rents the control and ends in <c>ApplySetters</c>, and
    /// <c>Element.SettersEqual</c> keeps any element carrying setters on the Update path, which
    /// ends in <c>ApplySetters</c> too. The selftest
    /// <c>Issue950TextBlockPaddingFixture.ModifierResetOutranksASetterWrite</c> pins where a
    /// <c>.Set</c> write really is discarded — a modifier set → unset transition, because
    /// <c>ApplyModifiers</c> runs after <c>ApplySetters</c> — and its own comment names MOD_002
    /// as the diagnostic for it. So the useful invariant is that the property is <em>known</em>,
    /// which is what makes the modifier suggestion reach the user at all.
    /// </para>
    /// <para>
    /// <see cref="IntentionallyExcluded"/> still applies, and stays deliberately small: it is for
    /// properties with no clean modifier-based replacement, not a place to park a decision.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_Reset_Property_With_Matching_Modifier_Is_Classified()
    {
        var resetProps = ReadResetProperties();
        var modifierNames = ReadModifierNames();
        modifierNames.UnionWith(ReadTypeSpecificModifierNames());

        // Non-vacuity floor. Both sides are read from source, and an empty either side makes the
        // assertion below true over nothing — the exact silent failure #1193 was.
        Assert.True(
            resetProps.Count >= 40,
            $"Only {resetProps.Count} instance reset(s) were read out of CleanElement. The scan " +
            "has stopped matching, which would make this invariant pass over a truncated set.");

        var unclassified = resetProps
            .Where(prop =>
                !IntentionallyExcluded.ContainsKey(prop) &&
                modifierNames.Contains(prop) &&
                !ModifierTable.Properties.ContainsKey(prop) &&
                !ModifierTable.DeliberatelyExcluded.ContainsKey(prop))
            .OrderBy(prop => prop, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "These properties are reset in ElementPool.CleanElement AND have a matching " +
            $"'.PROP(...)' modifier in ElementExtensions.cs, but appear in neither " +
            $"ModifierTable.Properties nor ModifierTable.DeliberatelyExcluded: [{string.Join(", ", unclassified)}]. " +
            "So no diagnostic mentions them and nothing records why. Map the property (so " +
            "REACTOR_MOD_002 offers the modifier for '.Set(x => x.PROP = ...)'), or exclude it " +
            "with the real reason the modifier is not an equivalent replacement. Note the " +
            "receivers matter: the modifier must apply to the control CleanElement resets it on — " +
            "'.Source' exists only for ParallaxViewElement while the reset is on Image, which is " +
            "why that one is excluded rather than mapped.");
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
        // CleanElementScan.InstancePropertyOwnerProbes, and two other Grid.* clears one line above are instance
        // properties nothing complains about. Without this sentence the obvious "fix" is to
        // declare the owner instance-only again, which is what silenced the failure in the
        // first place.
        var mixedOwner = missing
            .Where(key => CleanElementScan.InstancePropertyOwnerProbes.ContainsKey(key.Substring(0, key.IndexOf('.'))))
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
                  "owner is in CleanElementScan.InstancePropertyOwnerProbes. That is not a reason to reclassify " +
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
        Assert.True(CleanElementScan.InstancePropertyOwnerProbes.ContainsKey(owner),
            $"'{owner}' is not in CleanElementScan.InstancePropertyOwnerProbes, so this row proves nothing about " +
            "the probe — IsAttachedReset short-circuits to 'attached' for unknown owners.");

        Assert.Equal(expectedAttached, CleanElementScan.IsAttachedReset(owner, property));
    }

    [Fact]
    public void Every_Instance_Owner_Key_Names_Its_Own_Type()
    {
        // The keys are how ElementPool.cs spells an owner; the typeof is what the probe reads.
        // Nothing else ties the two together, and a mismatched pair
        // (["Grid"] = typeof(StackPanel)) would probe a type with no Grid.SetRow and answer
        // "instance" for every attached Grid property — the original bug, restored.
        var mismatched = CleanElementScan.InstancePropertyOwnerProbes
            .Where(entry => !string.Equals(entry.Key, entry.Value.OwnerType.Name, StringComparison.Ordinal))
            .Select(entry => $"'{entry.Key}' -> {entry.Value.OwnerType.FullName}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            mismatched.Count == 0,
            "These CleanElementScan.InstancePropertyOwnerProbes entries are keyed by a name their typeof does not " +
            $"have: [{string.Join(", ", mismatched)}]. The key must be the type's simple name — " +
            "that is the spelling the CleanElement scan captures, and the probe is only meaningful " +
            "when it interrogates the type that owns the property being classified.");
    }

    /// <summary>
    /// No <c>DeliberatelyExcludedAttached</c> row may name an owner that
    /// <see cref="CleanElementScan.InstancePropertyOwnerProbes"/> classifies per property — and every key must stay
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
            $"CleanElementScan.InstancePropertyOwnerProbes already classifies per property: [{string.Join(", ", offenders)}]. " +
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
    /// <see cref="CleanElementScan.InstancePropertyOwnerProbes"/>, in ordinal order.
    /// </summary>
    private static List<string> ExclusionRowsOnInstanceOwners(IEnumerable<string> keys) =>
        keys.Where(key =>
                key.IndexOf('.') > 0
                && CleanElementScan.InstancePropertyOwnerProbes.ContainsKey(key.Substring(0, key.IndexOf('.'))))
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

    /// <summary>
    /// No <c>DeliberatelyExcluded</c> row may claim a modifier does not exist when one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the check whose absence let issue #1193 sit undetected. The staleness tests in
    /// <c>ModifierTableIntegrityTests</c> force every candidate modifier into <em>one of</em>
    /// <c>Properties</c> or <c>DeliberatelyExcluded</c> — and an exclusion counts as a
    /// classification, so a row is never revisited once written. Nothing read the stated reason
    /// back against reality, so <c>IsHitTestVisible</c> sat excluded as "No modifier exists" while
    /// <c>.IsHitTestVisible(bool)</c> was in <c>ElementExtensions.cs</c> the whole time. The
    /// consequence was not cosmetic: an excluded property is invisible to <c>REACTOR_MOD_002</c>,
    /// so users writing <c>.Set(b =&gt; b.IsHitTestVisible = false)</c> were never offered the
    /// modifier.
    /// </para>
    /// <para>
    /// Deliberately narrow. It does not assert that an exclusion is <em>right</em> — most rows
    /// exclude a property that does have a same-named modifier, for signature or semantic reasons
    /// (<c>Content</c> takes an Element, <c>Translation</c> takes three floats), and those are
    /// judgements a test cannot make. It asserts only the one claim that is mechanically
    /// checkable, and only where the row actually makes it.
    /// </para>
    /// <para>
    /// Non-vacuous by construction: <c>Name</c> makes the same claim truthfully and keeps the
    /// predicate exercised in the passing direction, so a row-matching regression that found
    /// nothing would not go unnoticed. MEASURED: restoring the <c>IsHitTestVisible</c> row fails
    /// this test alone, naming the property and the modifier's declaration shape.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_Exclusion_Claims_A_Modifier_Is_Missing_When_It_Exists()
    {
        var generic = ReadModifierNames();
        var typeSpecific = ReadTypeSpecificModifierNames();

        var claimsNoModifier = ModifierTable.DeliberatelyExcluded
            .Where(entry => entry.Value.IndexOf("No modifier exists", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        Assert.True(
            claimsNoModifier.Count > 0,
            "No DeliberatelyExcluded row states 'No modifier exists' any more. Either the phrase " +
            "was reworded — in which case update this test's predicate, because it is now " +
            "checking nothing — or the last such row was mapped, in which case delete this test.");

        var contradicted = claimsNoModifier
            .Where(entry => generic.Contains(entry.Key) || typeSpecific.Contains(entry.Key))
            .Select(entry => generic.Contains(entry.Key)
                ? $"'{entry.Key}' (generic .{entry.Key}<T>(...))"
                : $"'{entry.Key}' (type-specific .{entry.Key}(...))")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            contradicted.Count == 0,
            "These ModifierTable.DeliberatelyExcluded rows say no modifier exists, but one does: " +
            $"[{string.Join(", ", contradicted)}]. The exclusion suppresses REACTOR_MOD_002 for " +
            "the property, so users never get told about the modifier that is sitting right " +
            "there. Map the property in ModifierTable.Properties instead — or, if the modifier " +
            "is genuinely not an equivalent replacement, keep the row and replace the reason " +
            "with the real one (signature mismatch, different receiver, owned by another rule).");
    }

    /// <summary>
    /// Names of the type-specific modifiers — <c>public static XxxElement Name(this XxxElement …)</c>.
    /// </summary>
    /// <remarks>
    /// Needed because <see cref="ReadModifierNames"/> only sees the generic
    /// <c>T Name&lt;T&gt;(this T el, …)</c> shape. <c>Stretch</c>, <c>StretchDirection</c> and
    /// <c>IsActive</c> exist only in this form, so a generic-only probe would report them as
    /// having no modifier at all — the same blind spot, one shape over.
    /// </remarks>
    private static HashSet<string> ReadTypeSpecificModifierNames()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Join(root!, "src", "Reactor", "Elements", "ElementExtensions.cs");
        Assert.True(File.Exists(path), $"ElementExtensions.cs not found at {path}");

        var names = Regex.Matches(
                File.ReadAllText(path),
                @"public\s+static\s+\w+\s+(\w+)\s*\(\s*this\s+(\w+Element)\s+\w+")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value);

        return new HashSet<string>(names, StringComparer.Ordinal);
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
    /// The <em>instance</em> property names reset anywhere in <c>ElementPool.CleanElement</c>.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="CleanElementScan"/>, which reads the whole method. Until issue
    /// #1193 this was a local regex over the FE-common block only — everything below
    /// <c>switch (fe)</c> was invisible, so the type-specific arms' resets were exempt from every
    /// invariant here and #985/#950 had to <em>relocate</em> clears up into the scanned region to
    /// get them covered. Properties declared on one control (TextBlock's font DPs, Viewbox's
    /// Stretch) cannot be relocated, so they were never covered at all.
    /// </remarks>
    private static HashSet<string> ReadResetProperties() =>
        CleanElementScan.InstanceResets
            .Select(reset => reset.Property)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The <c>Owner.Property</c> names of the <em>attached</em> properties reset in
    /// <c>CleanElement</c> — the exact complement of <see cref="ReadResetProperties"/> over the
    /// same matches, split by <see cref="CleanElementScan.IsAttachedReset(string, string)"/>.
    /// </summary>
    /// <remarks>
    /// The owner may be written with any amount of qualification in the source
    /// (<c>Microsoft.UI.Xaml.Automation.AutomationProperties</c>, <c>WinUI.ToolTipService</c>,
    /// <c>Layout.FlexPanel</c>), so only the rightmost segment is kept — which is exactly how
    /// <c>ModifierTable.AttachedProperties</c> is keyed, and how the analyzer sees the owner at a
    /// call site.
    /// </remarks>
    private static HashSet<string> ReadResetAttachedProperties() =>
        CleanElementScan.AttachedResets
            .Select(reset => reset.Owner + "." + reset.Property)
            .ToHashSet(StringComparer.Ordinal);

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
