using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        Assert.True(
            missing.Count == 0,
            "These attached properties are cleared in ElementPool.CleanElement but are in " +
            "neither ModifierTable.AttachedProperties nor DeliberatelyExcludedAttached: " +
            $"[{string.Join(", ", missing)}]. " +
            "Either map them (so REACTOR_POOL_001 fires on '.Set(fe => Owner.SetPROP(fe, ...))'), " +
            "or exclude them with a documented reason.");
    }

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

        var expected = ModifierTable.AttachedProperties.Values
            .Select(info => info.Owner)
            .Concat(ModifierTable.DeliberatelyExcludedAttached.Keys
                .Select(key => key.Substring(0, key.IndexOf('.'))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal)
            .ToList();

        var unseen = expected.Where(owner => !scanned.Contains(owner)).ToList();

        Assert.True(
            unseen.Count == 0,
            "The CleanElement attached-reset scan found no ClearValue at all for these owners: " +
            $"[{string.Join(", ", unseen)}]. Either the resets were removed (drop the table " +
            "entries) or ReadResetAttachedProperties' regex no longer matches how they are " +
            "written in ElementPool.cs.");
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
    /// Extract the set of property names reset in the FE-common block of
    /// <c>ElementPool.CleanElement</c> — from the method's opening brace up
    /// to (but not including) the <c>switch (fe)</c> that begins type-specific
    /// cleanup. Captures both <c>fe.PROP = ...</c> direct sets and
    /// <c>RECEIVER.ClearValue((FrameworkElement|UIElement|Control).PROPProperty)</c> calls.
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
        // The owner is restricted to the DependencyObject base types that actually
        // back FrameworkElement instance properties (see InstancePropertyOwners, which
        // this alternation is derived from so the two scans can't drift);
        // attached-property owners are captured separately by
        // ReadResetAttachedProperties, owner-qualified, because their bare names
        // collide with instance properties (AutomationProperties.Name vs
        // FrameworkElement.Name).
        var clearValueProps = Regex.Matches(commonBlock, InstanceClearValuePattern)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value);

        return new HashSet<string>(directAssignments.Concat(clearValueProps), StringComparer.Ordinal);
    }

    /// <summary>
    /// Extract the <c>Owner.Property</c> names of the <em>attached</em> properties reset in
    /// the FE-common block of <c>ElementPool.CleanElement</c> — every
    /// <c>RECEIVER.ClearValue(...OWNER.PROPProperty)</c> whose owner is not one of the
    /// instance-property base types handled by <see cref="ReadResetProperties"/>.
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

        var attached = Regex.Matches(commonBlock,
                @"\b\w+\.ClearValue\(\s*(?:[\w.]+\.)?(\w+)\.(\w+)Property\s*\)")
            .Cast<Match>()
            .Where(m => !InstancePropertyOwners.Contains(m.Groups[1].Value))
            .Select(m => m.Groups[1].Value + "." + m.Groups[2].Value);

        return new HashSet<string>(attached, StringComparer.Ordinal);
    }

    private static readonly HashSet<string> InstancePropertyOwners =
        new(StringComparer.Ordinal)
        {
            "FrameworkElement",
            "UIElement",
            "Control",
            // Issue #985: CleanElement's FE-common block now clears the Padding /
            // CornerRadius / BorderThickness / BorderBrush / Background family through a
            // Control | Border | Panel/Grid/StackPanel | TextBlock chain that mirrors
            // ApplyModifiers' receivers. Border.PaddingProperty and friends are ordinary
            // instance properties; without them here the attached scan would claim them and
            // Every_Reset_Attached_Property_Is_Classified would fail on owners that have
            // no business being in the attached table.
            "Border",
            "Panel",
            "StackPanel",
            // Grid arrived with #1003, which widened the Padding / CornerRadius gates to the
            // concrete panels and added `resetGrid.ClearValue(WinUI.Grid.PaddingProperty)` one
            // line above StackPanel's. It belongs here for exactly the reason Border and
            // StackPanel do — Grid.PaddingProperty is an ordinary instance DP, not an attached
            // one — but neither branch alone had both halves: #985 wrote this list, #1003 wrote
            // those clears, and the union inherited the clears without the owner. Left out, the
            // attached scan claims `Grid.Padding` / `Grid.CornerRadius` and they have to be
            // silenced in DeliberatelyExcludedAttached, which is a suppression list, not a
            // classification — so a genuinely attached `Grid.*` reset added later would land in
            // the same bucket as these two and read as already-triaged.
            "Grid",
            // TextBlock joins them because #985 also moved TextBlock.Padding (added by
            // #950) into the scanned block. Without it here, TextBlock.PaddingProperty
            // would be read as an *attached* property named TextBlock.Padding.
            "TextBlock",
        };

    /// <summary>
    /// Matches <c>RECEIVER.ClearValue(OWNER.PROPProperty)</c> for the instance-property
    /// owners in <see cref="InstancePropertyOwners"/>, capturing the bare property name.
    /// </summary>
    /// <remarks>
    /// The owner alternation is built from <see cref="InstancePropertyOwners"/> rather than
    /// hardcoded so the instance and attached scans stay two views of one list — a new owner
    /// added to only one of them would silently reclassify a property. The optional
    /// <c>[\w.]+.</c> prefix matches <see cref="ReadResetAttachedProperties"/> so the
    /// alias-qualified spelling <c>WinUI.Border.PaddingProperty</c> used throughout
    /// <c>ElementPool.cs</c> is recognized as an instance reset.
    /// </remarks>
    private static readonly string InstanceClearValuePattern =
        @"\b\w+\.ClearValue\(\s*(?:[\w.]+\.)?(?:"
        + string.Join("|", InstancePropertyOwners.OrderBy(o => o, StringComparer.Ordinal))
        + @")\.(\w+)Property\s*\)";

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
