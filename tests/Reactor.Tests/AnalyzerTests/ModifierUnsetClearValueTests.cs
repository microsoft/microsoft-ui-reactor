using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Pack;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Pins the reset half of the modifier protocol: <b>unsetting a modifier must
/// <c>ClearValue</c> the dependency property, never assign the property's default.</b>
/// <para>
/// The two are indistinguishable when you read the effective value on a control with no
/// relevant style, which is exactly why issue #952 survived for so long. They are not
/// equivalent: a local value outranks every <c>Style</c> setter in WinUI's precedence
/// order, so writing <c>HorizontalAlignment.Stretch</c> on unset does not restore the
/// styled value — it permanently overrides it, and the control can never get its
/// style-provided value back.
/// </para>
/// <para>
/// The behavioural proof lives in the selftests (<c>ModifierEvent_StyleUnsetRestore</c> and
/// <c>ModifierEvent_PoolClearValueOnRent</c>) because headless xUnit cannot construct a
/// WinUI control. These tests are the cheap structural counterpart: they read the source
/// and fail, naming the property, the moment any arm regresses to a default-value write.
/// Parsed with Roslyn rather than matched with a regex so "which arm is this statement in"
/// is an exact question about the syntax tree instead of guesswork about brace depth.
/// </para>
/// </summary>
public class ModifierUnsetClearValueTests
{
    /// <summary>
    /// Statements in an unset arm that are legitimately not <c>ClearValue</c> calls, keyed
    /// by the assignment target as written. Empty on purpose — every unset arm in
    /// <c>ApplyModifiers</c> can and does clear its dependency property. An entry here is a
    /// claim that a property has no DP to clear; add one only with a reason.
    /// </summary>
    private static readonly Dictionary<string, string> ApplyModifiersAssignmentExceptions = new(StringComparer.Ordinal);

    /// <summary>
    /// Direct <c>fe.PROP = …</c> writes allowed to remain in <c>CleanElement</c>'s FE-common
    /// block, with the reason each one is not a styled-DP restore.
    /// </summary>
    private static readonly Dictionary<string, string> CleanElementAssignmentExceptions =
        new(StringComparer.Ordinal)
        {
            // Reactor's own pool/element-identity slot, not a value any Style supplies.
            // Nulling it is the point; there is no styled value to fall back to.
            ["Tag"] = "framework-internal element-identity slot, never style-provided",
        };

    [Fact]
    public void Every_ApplyModifiers_Unset_Arm_Clears_The_Dependency_Property()
    {
        var arms = ReadUnsetArms();

        // A parser that finds nothing would make every assertion below vacuous. The method
        // has ~30 unset arms today; 20 is a floor that survives ordinary churn but not a
        // shape change that silently stops matching.
        Assert.True(
            arms.Count >= 20,
            $"Only {arms.Count} modifier-unset arm(s) were read out of Reconciler.ApplyModifiers. " +
            "The unset-transition shape ('!m.X.HasValue && oldM?.X.HasValue == true' or " +
            "'m.X is null && oldM?.X is not null') has probably changed, which would make this " +
            "whole test pass without checking anything.");

        var offenders = new List<string>();

        foreach (var (modifier, arm) in arms)
        {
            foreach (var assignment in arm.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var target = assignment.Left.ToString();
                var property = target.Contains('.') ? target[(target.LastIndexOf('.') + 1)..] : target;
                if (ApplyModifiersAssignmentExceptions.ContainsKey(property)) continue;

                offenders.Add($"{modifier}: `{assignment.ToString().Trim()}`");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These unset arms in Reconciler.ApplyModifiers write a value instead of calling " +
            "ClearValue(<DP>Property). A local value outranks Style setters, so the write does " +
            "not restore the styled value — it permanently overrides it (issue #952):\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The positive half of <see cref="Every_ApplyModifiers_Unset_Arm_Clears_The_Dependency_Property"/>:
    /// an arm that neither assigns nor clears would satisfy that test while resetting nothing.
    /// </summary>
    [Fact]
    public void Every_ApplyModifiers_Unset_Arm_Actually_Resets_Something()
    {
        var inert = ReadUnsetArms()
            .Where(entry => !entry.Arm.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "ClearValue" }))
            .Where(entry => !entry.Arm.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any())
            .Select(entry => entry.Modifier)
            .ToList();

        Assert.True(
            inert.Count == 0,
            "These unset arms in Reconciler.ApplyModifiers neither clear nor write anything, so " +
            $"the modifier is never reset on a set → unset update: [{string.Join(", ", inert)}]");
    }

    /// <summary>
    /// Dependency properties <c>CleanElement</c>'s FE-common block must release. Absence of a
    /// default-value assignment is not enough on its own: deleting a <c>ClearValue</c> line
    /// outright leaves no offender to find, so the block is also pinned positively. Every
    /// entry corresponds to a common modifier whose <c>ApplyModifiers</c> unset arm clears
    /// the same dependency property.
    /// </summary>
    private static readonly string[] CleanElementRequiredClears =
    [
        "FrameworkElement.Margin",
        "FrameworkElement.Width",
        "FrameworkElement.Height",
        "FrameworkElement.MinWidth",
        "FrameworkElement.MinHeight",
        "FrameworkElement.MaxWidth",
        "FrameworkElement.MaxHeight",
        "FrameworkElement.HorizontalAlignment",
        "FrameworkElement.VerticalAlignment",
        "UIElement.Opacity",
        "UIElement.Visibility",
        "UIElement.AccessKey",
        "UIElement.ContextFlyout",
        "UIElement.IsHitTestVisible",
        "FrameworkElement.RenderTransform",
        "FrameworkElement.FlowDirection",
        "Control.IsTabStop",
        // Issue #985 — the receiver-gated chain that mirrors ApplyModifiers' Padding /
        // CornerRadius / BorderThickness / BorderBrush / Background / IsEnabled writes.
        // Pinned per receiver, not per property: dropping only the Border or only the Panel
        // arm would leave the Control arm to satisfy a property-name-keyed list, and the leak
        // would be invisible again for exactly the receivers that are hardest to notice.
        "Control.Padding",
        "Control.CornerRadius",
        "Control.BorderThickness",
        "Control.BorderBrush",
        "Control.Background",
        "Control.IsEnabled",
        "Border.Padding",
        "Border.CornerRadius",
        "Border.BorderThickness",
        "Border.BorderBrush",
        "Border.Background",
        "Panel.Background",
        "StackPanel.Padding",
        "StackPanel.CornerRadius",
        "Grid.Padding",
        "Grid.CornerRadius",
        // TextBlock is a Padding receiver in ModifierTable's control gate. It predates #985
        // (it arrived with #950) but lived past the switch where no scanner reached it.
        "TextBlock.Padding",
    ];

    [Fact]
    public void CleanElement_Resets_Through_ClearValue_Not_Default_Assignment()
    {
        var commonBlock = ReadCleanElementCommonBlock(out var paramName);

        // Same shape PoolResetSetConsistencyTests.ReadResetProperties scans for, so the two
        // stay in agreement about what a "reset" looks like in this block.
        var offenders = Regex.Matches(commonBlock, $@"\b{Regex.Escape(paramName)}\.(\w+)\s*=[^=]")
            .Select(match => match.Groups[1].Value)
            .Where(property => property != "ClearValue")
            .Where(property => !CleanElementAssignmentExceptions.ContainsKey(property))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(property => property, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "ElementPool.CleanElement's FE-common block resets these properties by assigning a " +
            "default instead of calling ClearValue. A pooled control handed back with a local " +
            "value can never show its default style's value for that property, which defeats the " +
            $"pool's 'indistinguishable from a fresh control' contract (issue #952): [{string.Join(", ", offenders)}]");
    }

    /// <summary>
    /// The positive half: an outright deleted <c>ClearValue</c> produces no offender for
    /// <see cref="CleanElement_Resets_Through_ClearValue_Not_Default_Assignment"/> to catch,
    /// so the required releases are pinned by name.
    /// </summary>
    [Fact]
    public void CleanElement_Releases_Every_Modifier_Backed_Dependency_Property()
    {
        var cleared = ReadCleanElementClears();

        var missing = CleanElementRequiredClears
            .Where(required => !cleared.Contains(required))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "ElementPool.CleanElement's FE-common block no longer releases these dependency " +
            "properties, so a recycled control carries the previous renter's local value into " +
            $"its next mount (issue #952): [{string.Join(", ", missing)}]. If a reset was moved " +
            "or intentionally dropped, update CleanElementRequiredClears with the reason.");
    }

    /// <summary>
    /// The dynamic counterpart to <see cref="CleanElementRequiredClears"/>. That list is a
    /// hand-maintained snapshot of today's answer; this derives the same obligation from the
    /// two sources that actually decide it — <c>ModifierTable</c>'s control gates and
    /// <c>ElementPool</c>'s poolable set — so a receiver that becomes poolable later is
    /// required to be released without anyone remembering to add a row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The intersection is by <b>assignability</b>, not by name, and that is the whole
    /// difficulty. <c>Control</c> is a gate receiver and is never itself in
    /// <c>PoolableTypes</c> — <c>Button</c>, <c>TextBox</c>, <c>ToggleSwitch</c>,
    /// <c>ScrollViewer</c>, <c>ProgressBar</c> and <c>ProgressRing</c> are. A name-based
    /// intersection therefore derives no <c>Control.*</c> obligation at all and reports
    /// success, which is the direction that reads as "no violation found". The
    /// subclass-derivation assertion below exists to catch exactly that regression.
    /// </para>
    /// <para>
    /// <c>RelativePanel</c> is in the <c>Padding</c> and <c>CornerRadius</c> gates and is
    /// deliberately <i>not</i> required here, because nothing poolable is a
    /// <c>RelativePanel</c> today. That exclusion is derived, not declared: adding
    /// <c>RelativePanel</c> to <c>PoolableTypes</c> makes both clears mandatory here on the
    /// next run, with no edit to this file. See issue #1051 for the analyzer-side
    /// consequence of the same asymmetry.
    /// </para>
    /// <para>
    /// This is <i>not</i> redundant with <see cref="CleanElementRequiredClears"/>, and the
    /// obvious experiment does not show that. Deleting <c>Control.Padding</c>'s
    /// <c>ClearValue</c> from <c>CleanElement</c> reddens this test <b>and</b> the static-pin
    /// test, so it cannot say which one is load-bearing. The discriminating mutation deletes
    /// the clear <i>and</i> its row from the pin list, blinding the static test: measured on
    /// <c>d26dcbd5</c>, that leaves 6 passed / 1 failed with this test as the <b>sole</b>
    /// detector. Keep both — the pin list catches a reset that moves out of the FE-common
    /// block, this catches an obligation that never had a row.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement()
    {
        var poolable = ReadPoolableTypes();
        var closure = ReadPoolableReceiverClosure(poolable);
        var cleared = ReadCleanElementClears();

        var required = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (property, info) in ModifierTable.Properties)
        {
            if (!info.PoolReset || info.ControlGate is null) continue;

            // A gate name absent from the closure means no poolable type is a `gateName`, so
            // the pool never recycles that receiver and owes it no reset. Filtering explicitly
            // rather than with a `continue` keeps that exclusion visible at the loop header,
            // which is where a reader looks to find out what this derivation ranges over.
            foreach (var gateName in info.ControlGate.Where(closure.ContainsKey))
            {
                required.Add(closure[gateName].Name + "." + property);
            }
        }

        var poolableNames = poolable.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

        // The next two asserts are the vacuity defence for this test, not preamble to the real
        // one below: emptying PoolableTypes with ReadPoolableTypes' own empty check deleted
        // still fails here, so these are the checks that stop a collapsed derivation reading as
        // green. Neither is redundant with that check, which only improves the message.
        var derivedBySubclass = required
            .Where(pair => !poolableNames.Contains(pair[..pair.IndexOf('.')]))
            .ToList();

        Assert.True(
            derivedBySubclass.Count > 0,
            "No gate receiver was matched through a subclass, so the intersection has regressed " +
            "from assignability to name equality. `Control` is a gate receiver and never appears " +
            "in PoolableTypes itself — Button, TextBox and ToggleSwitch do — so a name-based " +
            "intersection silently drops every Control.* obligation and reports no violation.");

        Assert.True(
            required.Count >= 12,
            $"Only {required.Count} poolable gated receiver/property pair(s) were derived from " +
            "ModifierTable's control gates and ElementPool.PoolableTypes. The derivation has " +
            "stopped matching, which would make the assertion below pass over an empty set.");

        var missing = required.Where(pair => !cleared.Contains(pair)).ToList();

        Assert.True(
            missing.Count == 0,
            "ElementPool.CleanElement does not release these dependency properties, even though " +
            "ModifierTable's control gate writes each one to a receiver the pool recycles — so " +
            "the next renter inherits the previous renter's local value, which outranks every " +
            $"Style setter (issue #985): [{string.Join(", ", missing)}]. Add the ClearValue to " +
            "CleanElement's FE-common block. This obligation is derived from the gate and the " +
            "poolable set, so it cannot be silenced by editing a list.");
    }

    /// <summary>
    /// Every modifier <c>ModifierTable</c> declares <c>poolReset: true</c> must be released
    /// somewhere in <c>CleanElement</c>'s FE-common block. Derived from the table, so it has no
    /// list to edit and cannot be silenced by deleting a row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closes the gap left by <see cref="Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement"/>,
    /// which is receiver-granular but only ranges over rows carrying a <c>ControlGate</c> — 5 of
    /// the 18 <c>poolReset: true</c> rows. The other 13, <c>Margin</c> through
    /// <c>IsEnabled</c>, had no derived obligation at all: their only protection was a row in
    /// <see cref="CleanElementRequiredClears"/>, and an allow-list is consumed as "every listed
    /// entry must be cleared", so deleting a row removes the requirement and its detector in one
    /// edit. <c>IsEnabled</c> is one of issue #985's own six, so five of that fix was derived and
    /// the sixth was not.
    /// </para>
    /// <para>
    /// The two derivations are complementary rather than layered, and the split is exact: the 5
    /// gated properties are the same 5 cleared through more than one receiver, so this test can
    /// only guarantee that <i>one</i> of their clears survives — the gated test pins each
    /// receiver. The 13 ungated ones are each cleared exactly once, which makes a property-level
    /// check receiver-strong for them by construction. Together they cover all 18 with no
    /// hardcoded names; neither covers all 18 alone.
    /// </para>
    /// <para>
    /// Non-vacuous, and not redundant with the pin list — but the obvious mutation cannot show
    /// that, because deleting <c>Control.IsEnabled</c>'s <c>ClearValue</c> reddens this test and
    /// the static-pin test together. The discriminating mutation deletes the clear <i>and</i> its
    /// pin row, blinding the static test and leaving this one as the sole detector.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_Pool_Reset_Modifier_Is_Cleared_By_CleanElement()
    {
        var clearedProperties = ReadCleanElementClears()
            .Select(pair => pair[(pair.IndexOf('.') + 1)..])
            .ToHashSet(StringComparer.Ordinal);

        var required = ModifierTable.Properties
            .Where(entry => entry.Value.PoolReset)
            .Select(entry => entry.Key)
            .ToList();

        // Vacuity defence. A collapsed table makes `required` empty and the assertion below
        // true over nothing; a collapsed scan makes `clearedProperties` empty, which fails
        // loudly and needs no guard. Only the first direction is silent, so only it is floored.
        Assert.True(
            required.Count >= 12,
            $"Only {required.Count} modifier(s) declare poolReset: true. ModifierTable has stopped " +
            "being readable, which would make the assertion below pass over an empty set.");

        var missing = required
            .Where(property => !clearedProperties.Contains(property))
            .OrderBy(property => property, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "ModifierTable declares these modifiers pool-reset, but CleanElement's FE-common " +
            $"block never releases them: [{string.Join(", ", missing)}]. Either add the " +
            "ClearValue, or drop poolReset from the row — REACTOR_POOL_001 tells users the value " +
            "is reset on pool return, so leaving them disagreeing makes the analyzer state " +
            "something false about the pool.");
    }

    /// <summary>
    /// <c>ModifierTable</c>'s <c>poolResetGate</c> lists are a name-level mirror of
    /// <c>ControlGate ∩ ElementPool.PoolableTypes</c>, needed because the analyzer targets
    /// <c>netstandard2.0</c> and cannot reference <c>src/Reactor</c>. This is the parity gate
    /// that makes the mirror maintained rather than remembered.
    /// </summary>
    /// <remarks>
    /// Compared as a <b>set</b>, in both directions. A count would pass on the same size with
    /// different members, and a one-directional check would miss the half that matters: a gate
    /// that is too <i>wide</i> reports POOL_001 on a receiver the pool never touches — a false
    /// Warning, which is a build break under <c>TreatWarningsAsErrors</c> — while one that is
    /// too <i>narrow</i> silently downgrades a real pooling hazard to Info.
    /// </remarks>
    [Fact]
    public void Every_Pool_Reset_Gate_Matches_The_Poolable_Intersection()
    {
        var closure = ReadPoolableReceiverClosure(ReadPoolableTypes());
        var checkedProperties = new List<string>();

        foreach (var (property, info) in ModifierTable.Properties)
        {
            if (!info.PoolReset || info.ControlGate is null) continue;

            var derived = info.ControlGate
                .Where(closure.ContainsKey)
                .ToHashSet(StringComparer.Ordinal);

            var declared = (info.PoolResetGate ?? info.ControlGate).ToHashSet(StringComparer.Ordinal);

            var tooWide = declared.Except(derived, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var tooNarrow = derived.Except(declared, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            Assert.True(
                tooWide.Count == 0,
                $"'{property}' reports REACTOR_POOL_001 on [{string.Join(", ", tooWide)}], which " +
                "ElementPool never recycles — so the diagnostic asserts that a .Set write is " +
                "unwound on pool return when nothing of the sort happens. POOL_001 is a Warning, " +
                $"so this breaks consumers building with TreatWarningsAsErrors. Add the receiver " +
                $"to ElementPool.PoolableTypes, or drop it from '{property}'s poolResetGate so it " +
                "falls to REACTOR_MOD_002.");

            Assert.True(
                tooNarrow.Count == 0,
                $"'{property}' is pool-reset on [{string.Join(", ", tooNarrow)}] but its " +
                "poolResetGate omits them, so a .Set write that really is lost on pool reuse " +
                "reports as REACTOR_MOD_002 (Info, 'a modifier exists') instead of " +
                "REACTOR_POOL_001 (Warning, 'this write is dropped'). Add them to the gate.");

            checkedProperties.Add(property);
        }

        Assert.True(
            checkedProperties.Count >= 5,
            $"Only {checkedProperties.Count} gated pool-reset propert(ies) were compared, so this " +
            "parity gate has stopped seeing the table it exists to check.");

        // The mirror is only load-bearing where it actually narrows something. If no property
        // declares a poolResetGate, every assertion above is satisfied by `?? ControlGate`
        // comparing the gate against itself — true by construction, and it would stay true if
        // the analyzer stopped consulting the gate entirely.
        Assert.Contains(
            ModifierTable.Properties.Values,
            info => info.PoolReset && info.PoolResetGate is not null);
    }

    /// <summary>
    /// The analyzer's poolable-type mirror equals <c>ElementPool.PoolableTypes</c>.
    /// </summary>
    /// <remarks>
    /// <c>Reactor.Analyzers</c> targets <c>netstandard2.0</c> and cannot reference
    /// <c>src/Reactor</c>, so the poolable set has to be copied. Both drift directions are
    /// checked because they fail in opposite ways and only one is loud: a name in the mirror
    /// that the pool does not recycle makes REACTOR_POOL_001 assert "reset on pool return" of a
    /// receiver that is never pooled — a false Warning, and a build break under
    /// <c>TreatWarningsAsErrors</c> — while a name the pool recycles and the mirror omits
    /// downgrades a genuine pooling hazard to Info, where nobody sees it.
    /// </remarks>
    [Fact]
    public void Analyzer_Poolable_Type_Mirror_Matches_ElementPool()
    {
        var actual = ReadPoolableTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var mirrored = ModifierTable.PoolableTypeNames.ToHashSet(StringComparer.Ordinal);

        var missing = actual.Except(mirrored, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var extra = mirrored.Except(actual, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            extra.Count == 0,
            $"ModifierTable's poolable mirror names [{string.Join(", ", extra)}], which " +
            "ElementPool.PoolableTypes does not contain. REACTOR_POOL_001 would fire on those " +
            "receivers claiming the write is unwound on pool return, at Warning severity, for a " +
            "control that is never pooled. Remove them from the mirror, or add them to " +
            "ElementPool.PoolableTypes if they really should be recycled.");

        Assert.True(
            missing.Count == 0,
            $"ElementPool recycles [{string.Join(", ", missing)}] but ModifierTable's mirror omits " +
            "them, so a .Set write that really is dropped on pool reuse reports as REACTOR_MOD_002 " +
            "(Info) instead of REACTOR_POOL_001 (Warning). Add them to the mirror.");
    }

    // ── Source-scanning helpers ─────────────────────────────────────────────

    /// <summary>
    /// <c>ElementPool.PoolableTypes</c>, read reflectively because it is private.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Throws on every failure path rather than returning an empty set, so a rename or a type
    /// change fails here, naming the member, instead of surfacing downstream as a claim that
    /// the gate intersection regressed to name equality — which misdescribes the cause.
    /// </para>
    /// <para>
    /// This is fail-fast diagnosis, not the vacuity defence, and the distinction matters when
    /// deciding what may be simplified away. Deleting the empty check and emptying the set
    /// still reddens all three callers, because
    /// <c>Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement</c> asserts its own
    /// derivation is non-degenerate before asserting anything about it. Those two checks, not
    /// this one, are what stop an empty poolable set reading as green.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Type> ReadPoolableTypes()
    {
        const string fieldName = "PoolableTypes";

        var field = typeof(ElementPool).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
                    ?? typeof(ElementPool).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

        if (field is null)
        {
            throw new InvalidOperationException(
                $"ElementPool.{fieldName} was not found. It has been renamed or moved — point this " +
                "test at the new member. The callers' own non-degeneracy asserts would catch the " +
                "resulting empty set, but they would report it as a collapsed gate intersection, " +
                "which names the wrong cause and sends the reader to the wrong file.");
        }

        if (field.GetValue(null) is not IEnumerable<Type> types)
        {
            throw new InvalidOperationException(
                $"ElementPool.{fieldName} is no longer an IEnumerable<Type>, so the poolable " +
                "receivers can no longer be read and this test cannot check anything.");
        }

        var poolable = types.ToList();

        if (poolable.Count == 0)
        {
            throw new InvalidOperationException(
                $"ElementPool.{fieldName} is empty. Every receiver obligation is derived from it, " +
                "so the cause is reported here rather than downstream, where the callers' " +
                "non-degeneracy asserts would describe it as a collapsed gate intersection.");
        }

        return poolable;
    }

    /// <summary>
    /// Every type some poolable type is assignable to — each pooled type's own base chain.
    /// A control gate's receiver is poolable exactly when it appears here, which is
    /// assignability expressed as set membership rather than as a string type lookup.
    /// </summary>
    private static Dictionary<string, Type> ReadPoolableReceiverClosure(IReadOnlyList<Type> poolable)
    {
        var closure = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var pooled in poolable)
        {
            for (var type = pooled; type is not null; type = type.BaseType)
            {
                closure[type.Name] = type;
            }
        }

        return closure;
    }

    /// <summary>
    /// <c>Owner.Property</c> pairs released by <c>CleanElement</c>'s FE-common block, using the
    /// same anchored boundary as the rest of this file so both callers agree on the region.
    /// </summary>
    private static HashSet<string> ReadCleanElementClears()
    {
        var commonBlock = ReadCleanElementCommonBlock(out _);

        return Regex.Matches(commonBlock, @"\b\w+\.ClearValue\(\s*(?:[\w.]+\.)?(\w+)\.(\w+)Property\s*\)")
            .Select(match => match.Groups[1].Value + "." + match.Groups[2].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every <c>else</c>-arm in <c>Reconciler.ApplyModifiers</c> guarded by an unset
    /// transition — the modifier is absent now and was present on the previous render.
    /// </summary>
    private static List<(string Modifier, StatementSyntax Arm)> ReadUnsetArms()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var file = Path.Join(root!, "src", "Reactor", "Core", "Reconciler.cs");
        Assert.True(File.Exists(file), $"Reconciler.cs not found at {file}");

        var methods = CSharpSyntaxTree.ParseText(File.ReadAllText(file))
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text == "ApplyModifiers")
            .ToList();

        Assert.True(methods.Count > 0, "No ApplyModifiers method found in Reconciler.cs");

        var arms = new List<(string, StatementSyntax)>();

        foreach (var ifStatement in methods.SelectMany(method => method.DescendantNodes().OfType<IfStatementSyntax>()))
        {
            var modifier = UnsetTransitionModifier(ifStatement.Condition)
                           ?? ElseOfSetArmModifier(ifStatement);
            if (modifier is null) continue;

            // Only the arm's own body — an `else if` chain nests the next arm inside this
            // one's Else clause, and attributing that arm's statements here would report
            // every following modifier under the first one's name.
            arms.Add((modifier, ifStatement.Statement));
        }

        return arms;
    }

    /// <summary>
    /// The modifier name when <paramref name="condition"/> is an unset transition —
    /// <c>!m.X.HasValue &amp;&amp; oldM?.X.HasValue == true</c> or
    /// <c>m.X is null &amp;&amp; oldM?.X is not null</c> — otherwise null.
    /// </summary>
    /// <remarks>
    /// Both halves are required. <c>m.X</c> alone is the set arm; <c>oldM?.X</c> alone is a
    /// diff guard. Only the conjunction means "was set, now isn't", which is the transition
    /// that has to release the local value.
    /// </remarks>
    private static string? UnsetTransitionModifier(ExpressionSyntax condition)
    {
        var text = condition.ToString();

        foreach (var name in ModifierNames(condition).Distinct(StringComparer.Ordinal))
        {
            var absentNow = text.Contains($"!m.{name}.HasValue") || text.Contains($"m.{name} is null");
            var presentBefore = text.Contains($"oldM?.{name}.HasValue == true")
                                || text.Contains($"oldM?.{name} is not null");
            if (absentNow && presentBefore) return name;
        }

        // Padding and BorderThickness are computed into a local first (to overlay the
        // BiDi-aware inline variants), so the absent-now half reads `!resolvedPadding.HasValue`
        // rather than `!m.Padding.HasValue`. Match on the oldM half and the local's shape.
        var resolved = Regex.Match(text, @"!\s*(resolved\w+)\.HasValue");
        if (resolved.Success)
        {
            var oldHalf = Regex.Match(text, @"oldM\?\.(\w+)\.HasValue == true");
            if (oldHalf.Success) return oldHalf.Groups[1].Value;
        }

        return null;
    }

    /// <summary>
    /// The modifier name when <paramref name="ifStatement"/> is the <c>else</c> of a set arm
    /// and only re-tests the previous render — <c>if (m.X is not null) … else if
    /// (oldM?.X is not null)</c>. Reaching the <c>else</c> already proves <c>m.X</c> is
    /// absent, so the condition omits that half and
    /// <see cref="UnsetTransitionModifier"/> cannot see it. <c>ContextFlyout</c> is written
    /// this way; without this the arm would be silently skipped and its reset untested.
    /// </summary>
    private static string? ElseOfSetArmModifier(IfStatementSyntax ifStatement)
    {
        if (ifStatement.Parent is not ElseClauseSyntax { Parent: IfStatementSyntax setArm }) return null;

        var text = ifStatement.Condition.ToString();
        var oldHalf = Regex.Match(text, @"oldM\?\.(\w+)(?:\.HasValue == true| is not null)");
        if (!oldHalf.Success) return null;

        var name = oldHalf.Groups[1].Value;

        // If the condition tests `m.X` itself, UnsetTransitionModifier already owns it (or
        // deliberately rejected it); only the implicit-else shape belongs here.
        if (text.Contains($"m.{name}", StringComparison.Ordinal)
            && !text.Contains($"oldM?.{name}", StringComparison.Ordinal)) return null;
        if (Regex.IsMatch(text, $@"(?<!old)\bm\.{Regex.Escape(name)}\b")) return null;

        // The guarding `if` must be the set arm for the same modifier, otherwise this is some
        // unrelated `else if` that happens to mention oldM.
        var setText = setArm.Condition.ToString();
        if (!Regex.IsMatch(setText, $@"(?<!old)\bm\.{Regex.Escape(name)}\b")) return null;

        return name;
    }

    /// <summary>Modifier names read off <c>m.</c> / <c>oldM.</c> / <c>oldM?.</c> in a condition.</summary>
    private static IEnumerable<string> ModifierNames(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            switch (descendant)
            {
                case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax bag } access
                    when bag.Identifier.Text is "m" or "oldM":
                    yield return access.Name.Identifier.Text;
                    break;

                case ConditionalAccessExpressionSyntax { Expression: IdentifierNameSyntax bag } conditional
                    when bag.Identifier.Text is "m" or "oldM":
                {
                    var binding = conditional.WhenNotNull
                        .DescendantNodesAndSelf()
                        .OfType<MemberBindingExpressionSyntax>()
                        .FirstOrDefault();
                    if (binding is not null) yield return binding.Name.Identifier.Text;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The FE-common block of <c>ElementPool.CleanElement</c> — opening brace up to (but not
    /// including) the <c>switch (fe)</c> that begins type-specific cleanup — plus the
    /// method's parameter name. Mirrors the boundary
    /// <see cref="PoolResetSetConsistencyTests"/> uses, deliberately: the type-specific arms
    /// reset content slots (<c>Content</c>, <c>Child</c>, <c>Source</c>) whose correct empty
    /// state really is a written null, so they are not subject to this rule.
    /// </summary>
    private static string ReadCleanElementCommonBlock(out string paramName)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var file = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(file), $"ElementPool.cs not found at {file}");

        var source = File.ReadAllText(file);
        var signature = Regex.Match(source, @"static\s+void\s+CleanElement\s*\(\s*FrameworkElement\s+(\w+)\s*\)");
        Assert.True(signature.Success, "Could not locate CleanElement(FrameworkElement) in ElementPool.cs");
        paramName = signature.Groups[1].Value;

        var braceStart = source.IndexOf('{', signature.Index + signature.Length);
        Assert.True(braceStart > signature.Index, "CleanElement opening brace not found");

        // Anchored to the start of a line so a comment mentioning the type dispatch cannot
        // masquerade as the boundary — see the matching note in
        // PoolResetSetConsistencyTests.ReadCleanElementCommonBlock.
        var switchStart = Regex.Match(
            source[braceStart..],
            $@"^\s*switch\s*\(\s*{Regex.Escape(paramName)}\s*\)",
            RegexOptions.Multiline);
        Assert.True(switchStart.Success, $"CleanElement layout changed — no 'switch ({paramName})' boundary found.");

        return source.Substring(braceStart, switchStart.Index);
    }
}
