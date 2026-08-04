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
/// <c>ClearValue</c> the dependency property, never assign the property's default</b> — and,
/// since #986, <b>must exist at all</b>.
/// <para>
/// The two writes are indistinguishable when you read the effective value on a control with no
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
    /// The reconciler methods scanned here, with the identifier each one uses for the new
    /// and the previous modifier bag. <c>ApplyAccessibilityModifiers</c> joined the scan with
    /// issue #986: it used to open with <c>if (a is null) return;</c>, which swallowed exactly
    /// the transition it needed to handle, so dropping the whole accessibility sub-record
    /// released nothing.
    /// </summary>
    private static readonly (string Method, string Bag, string OldBag, int MinArms, int MinUnsetArms)[] ScannedMethods =
    [
        // Both are per-method non-vacuity floors, deliberately close to the real count. A
        // single total floor is not enough: if the matcher stopped recognizing
        // ApplyAccessibilityModifiers entirely, ApplyModifiers alone would still clear a
        // global threshold and the a11y half would go unchecked in silence — which is exactly
        // the half issue #986 found broken.
        //
        // MinArms governs the *set*-arm scan (Every_Diff_Guarded_Modifier_Has_An_Unset_Arm);
        // MinUnsetArms governs the *unset*-arm scan and is enforced inside ReadUnsetArms, so
        // every consumer of that helper inherits it rather than having to remember a floor.
        // They are separate populations and must not be collapsed into one constant.
        //
        // Real counts when these were set: set arms 37 / 12, unset arms 36 / 12. Read them out
        // of the assertion message (raise the floor to 99999 and run) rather than re-deriving
        // with a text scan — the scan walks syntax nodes, so a line-scoped regex undercounts.
        ("ApplyModifiers", "m", "oldM", 32, 31),
        ("ApplyAccessibilityModifiers", "a", "oldA", 10, 10),
    ];

    /// <summary>
    /// Assignments inside an unset arm that are legitimately not <c>ClearValue</c> calls, keyed
    /// by the assignment target's trailing identifier. An entry is a claim that the target is
    /// not a dependency property at all — reconciler bookkeeping that happens to live inside the
    /// arm — so the #952 precedence argument does not apply to it. Add one only with a reason;
    /// anything that <em>is</em> a DP must go through <c>ClearValue</c>.
    /// <para>
    /// Scoped to every method in <see cref="ScannedMethods"/>, not to <c>ApplyModifiers</c> alone —
    /// the sole entry below comes from <c>ApplyAccessibilityModifiers</c>.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> UnsetArmAssignmentExceptions =
        new(StringComparer.Ordinal)
        {
            ["PendingLabeledBy"] =
                "ReactorState.PendingLabeledBy is a plain field, not a dependency property. The " +
                "LabeledBy unset arm nulls it to retire a still-parked deferred resolution so a " +
                "Loaded handler from an earlier render cannot re-apply the label the user just " +
                "dropped (issue #986). There is no DP here to clear, and the DP that does exist " +
                "(LabeledByProperty) is cleared in the same arm.",
        };

    /// <summary>
    /// Diff-guarded modifiers that deliberately have <em>no</em> unset arm, keyed
    /// <c>Method.Modifier</c>. Every entry must still describe a genuinely missing arm —
    /// <see cref="Every_Diff_Guarded_Modifier_Has_An_Unset_Arm"/> fails on a stale entry too,
    /// so landing the arm forces the exception to be deleted rather than left to rot.
    /// </summary>
    private static readonly Dictionary<string, string> MissingUnsetArmExceptions =
        new(StringComparer.Ordinal)
        {
            // Issue #1001. These four are XAML *facade* properties. With no ambient curve the
            // set path (AnimationHelper.SetOrAnimate / SetOrAnimateVector3) falls through to
            // SetVector3Direct and assigns the facade property, which a mechanical ClearValue
            // arm would release normally. But when a curve *is* ambient the same path calls
            // visual.StartAnimation instead, and a running composition animation outranks the
            // DP while leaving no local value for ClearValue to release — so the mechanical
            // arm every other modifier gets would silently do nothing in exactly the case
            // that matters. The fix needs a StopAnimation companion plus a
            // selftest that actually exercises the animated path; deleting these four entries
            // is the first commit of #1001.
            ["ApplyModifiers.Scale"] = "Compositor-backed facade property — issue #1001.",
            ["ApplyModifiers.Rotation"] = "Compositor-backed facade property — issue #1001.",
            ["ApplyModifiers.Translation"] = "Compositor-backed facade property — issue #1001.",
            ["ApplyModifiers.CenterPoint"] = "Compositor-backed facade property — issue #1001.",
        };

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

    /// <summary>
    /// Every unset arm in <see cref="ScannedMethods"/> — both <c>ApplyModifiers</c> and
    /// <c>ApplyAccessibilityModifiers</c> — must reset through <c>ClearValue</c> rather than
    /// assigning a value.
    /// </summary>
    [Fact]
    public void Every_Unset_Arm_Clears_The_Dependency_Property()
    {
        // The non-vacuity floor lives inside ReadUnsetArms and is enforced *per method*, so a
        // shape change that silently stops matching one of the two scanned methods fails here
        // rather than hiding behind the other method's arm count.
        var arms = ReadUnsetArms();

        var offenders = new List<string>();

        foreach (var (method, modifier, arm) in arms)
        {
            foreach (var assignment in arm.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var target = assignment.Left.ToString();
                var property = target.Contains('.') ? target[(target.LastIndexOf('.') + 1)..] : target;
                if (UnsetArmAssignmentExceptions.ContainsKey(property)) continue;

                offenders.Add($"{method}.{modifier}: `{assignment.ToString().Trim()}`");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These unset arms in the reconciler write a value instead of calling " +
            "ClearValue(<DP>Property). A local value outranks Style setters, so the write does " +
            "not restore the styled value — it permanently overrides it (issue #952):\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The positive half of <see cref="Every_Unset_Arm_Clears_The_Dependency_Property"/>:
    /// an arm that neither assigns nor clears would satisfy that test while resetting nothing.
    /// Same scope — every method in <see cref="ScannedMethods"/>.
    /// </summary>
    [Fact]
    public void Every_Unset_Arm_Actually_Resets_Something()
    {
        var inert = ReadUnsetArms()
            .Where(entry => !entry.Arm.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "ClearValue" }))
            .Where(entry => !entry.Arm.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any())
            .Select(entry => entry.Method + "." + entry.Modifier)
            .ToList();

        Assert.True(
            inert.Count == 0,
            "These unset arms in the reconciler neither clear nor write anything, so " +
            $"the modifier is never reset on a set → unset update: [{string.Join(", ", inert)}]");
    }

    /// <summary>
    /// The half neither test above can see: a modifier with <em>no</em> unset arm at all.
    /// Both of them only inspect arms that already exist, so a set arm written without its
    /// reset is invisible to them — which is how <c>IsTabStop</c>, <c>TabIndex</c>,
    /// <c>XYFocusKeyboardNavigation</c>, <c>HeadingLevel</c>, <c>ElementSoundMode</c> and the
    /// whole of <c>ApplyAccessibilityModifiers</c> shipped with the last render's value pinned
    /// on the control forever (issue #986).
    /// </summary>
    /// <remarks>
    /// Scoped to <em>diff-guarded</em> set arms — conditions that name both <c>bag.X</c> and
    /// <c>oldBag.X</c>. That is the shape of "write this property when present, compared
    /// against the previous render", and it is precisely the shape that owes a reset. It
    /// deliberately does not try to read the three arms that compute a local first
    /// (<c>Margin</c>, <c>Padding</c>, <c>BorderThickness</c> overlay their BiDi-aware inline
    /// variants into <c>resolvedX</c>): their conditions never name <c>m.X</c>, and
    /// <c>Padding</c>'s reset is guarded by <c>wantsPadding</c>/<c>hadPadding</c> rather than
    /// by the bag at all, so matching them loosely enough to see the set arm would report a
    /// missing reset that is right there. Those three are covered behaviourally by
    /// <c>ModifierEvent_InlineDropRestoresPhysical</c> instead.
    /// </remarks>
    [Fact]
    public void Every_Diff_Guarded_Modifier_Has_An_Unset_Arm()
    {
        var missing = new List<string>();

        foreach (var (method, bag, oldBag, minArms, _) in ScannedMethods)
        {
            var unset = ReadUnsetModifierNames(method, bag, oldBag);
            var found = 0;
            foreach (var modifier in ReadDiffGuardedModifiers(method, bag, oldBag))
            {
                found++;
                if (!unset.Contains(modifier)) missing.Add($"{method}.{modifier}");
            }

            // Per-method non-vacuity floor: a matcher that stops recognizing the set-arm
            // shape in one method would report zero missing arms for it and pass, which is
            // the failure mode this whole test exists to prevent.
            Assert.True(
                found >= minArms,
                $"Only {found} diff-guarded modifier(s) were read out of {method} (expected at " +
                $"least {minArms}). The set-arm shape ('{bag}.X.HasValue && {bag}.X != " +
                $"{oldBag}?.X') has probably changed, which would make this test pass without " +
                "checking anything.");
        }

        var offenders = missing.Where(entry => !MissingUnsetArmExceptions.ContainsKey(entry)).ToList();

        Assert.True(
            offenders.Count == 0,
            "These modifiers are written when present but never released when dropped, so the " +
            "previous render's value stays pinned on the control forever (issue #986): " +
            $"[{string.Join(", ", offenders)}]. Add an `else if` arm calling " +
            "ClearValue(<DP>Property), or record the exemption in MissingUnsetArmExceptions " +
            "with a reason.");

        // A stale exemption is worse than none: it silently re-opens the hole for a modifier
        // whose arm was later deleted. Landing an arm must delete its entry here.
        var stale = MissingUnsetArmExceptions.Keys.Where(key => !missing.Contains(key)).ToList();

        Assert.True(
            stale.Count == 0,
            "These MissingUnsetArmExceptions entries no longer describe a missing unset arm — " +
            "either the arm was added (delete the entry) or the modifier was renamed/removed " +
            $"(update it): [{string.Join(", ", stale)}]");
    }

    /// <summary>
    /// Every control type a modifier is <em>written</em> to must also be a control type it is
    /// <em>cleared</em> on — and a modifier written anywhere must be released somewhere.
    /// </summary>
    /// <remarks>
    /// <see cref="Every_Diff_Guarded_Modifier_Has_An_Unset_Arm"/> asks a per-<em>property</em>
    /// question, so it is satisfied the moment a property has any reset at all. Several arms
    /// are not a single write but an <c>if (fe is WinUI.X) … else if (fe is WinUI.Y) …</c>
    /// type-dispatch chain, and those chains get widened one control type at a time — #970
    /// added <c>TextBlock</c> to <c>Padding</c>, #1003 adds <c>Grid</c>/<c>RelativePanel</c> to
    /// <c>Padding</c> and <c>Grid</c>/<c>StackPanel</c>/<c>RelativePanel</c> to
    /// <c>CornerRadius</c>. Widening only the write half leaves a control type that can have
    /// the property set and never released: the #986 bug again, one type down, and invisible
    /// to a property-level scan because the property's reset still exists for its original
    /// types.
    /// <para>
    /// That asymmetry is also the standing merge hazard on this file. The two halves live in
    /// different hunks, so a rebase can auto-merge the widened write while the widened clear
    /// conflicts — resolving that conflict in favour of either side alone reinstates the bug
    /// while the arm a reviewer reads first still looks correct. This test is the tripwire:
    /// it fails on the merge result, naming the property and the orphaned type.
    /// </para>
    /// <para>
    /// The second obligation exists because the per-property sibling test is not total. Its
    /// exclusion list covers the three modifiers that compute a local before testing it —
    /// <c>Margin</c>, <c>Padding</c>, <c>BorderThickness</c> — whose conditions never name
    /// <c>m.X</c>, so it cannot see them at all. Deleting <c>BorderThickness</c>'s whole unset
    /// arm while keeping its two type-gated writes therefore left every test in this file green:
    /// the sibling excludes it, both floors have slack, and the property simply vanished from
    /// <c>clears</c> and was skipped. Those three are exactly the properties this file's other
    /// tripwires talk about most, which is what made the hole comfortable to look at.
    /// </para>
    /// <para>
    /// Verified by mutation — deleting a single <c>else if (fe is WinUI.T …) …ClearValue(…)</c>
    /// branch while leaving its write branch in place reddens this test and nothing else, naming
    /// the property and the orphaned type; deleting an entire unset arm reddens it naming the
    /// property alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_Type_Gated_Write_Has_A_Matching_Type_Gated_Clear()
    {
        var writes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var clears = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var (method, _, _, _, _) in ScannedMethods)
        {
            var gated = ReadIfStatements(method)
                .Select(ifStatement => (ifStatement, Gate: ReadTypeGate(ifStatement)))
                .Where(entry => entry.Gate is not null);

            foreach (var (ifStatement, gate) in gated)
            {
                var (typeName, variable) = gate!.Value;

                // The gate's own branch only. An `else if` chain nests the next gate inside
                // this one's Else clause, so including the else would attribute every later
                // control type's writes to the first type in the chain and mask the exact
                // asymmetry this test looks for.
                var branch = ifStatement.Statement;

                foreach (var property in ReadWrittenProperties(branch, variable))
                    Add(writes, property, typeName);

                foreach (var property in ReadClearedProperties(branch, variable))
                    Add(clears, property, typeName);
            }
        }

        // Non-vacuity floor. A matcher that stopped recognizing the `fe is WinUI.T v` gate
        // would collect nothing, find no asymmetry and pass — the failure mode this test
        // exists to prevent. There are 28 type-gated write pairs today over 11 properties;
        // the two longest chains are Padding (6 types) and CornerRadius (5).
        //
        // The floor is deliberately close to the real count, for the same reason MinArms is.
        // A floor low enough to survive losing a whole chain does not protect the chains this
        // test exists for: at 10 the scanner could drop Padding *and* CornerRadius entirely —
        // the two properties #970, #986 and #1003 all widened — and still report 17, passing
        // while blind to precisely the arms under active merge pressure. At 24 the loss of
        // either chain trips it, with four arms of slack for a legitimate narrowing.
        //
        // Read this count out of the assertion message rather than re-deriving it by hand: a
        // line-scoped text scan misses the writes nested inside a gate's block (it reports 22,
        // silently dropping HorizontalContentAlignment and VerticalContentAlignment), and
        // calibrating the floor against that number would sit it below the real population.
        var pairs = writes.Sum(entry => entry.Value.Count);

        Assert.True(
            pairs >= 24,
            $"Only {pairs} type-gated modifier write(s) were read out of the reconciler " +
            "(expected at least 24). The `if (fe is WinUI.T v) v.Prop = …` shape has probably " +
            "changed, which would make this test pass without checking anything. Properties " +
            $"seen: [{string.Join(", ", writes.Keys)}]");

        // The floor above is computed from `writes` alone, so it is structurally incapable of
        // noticing a failure confined to the *clear* side — and the `continue` below turns that
        // blindness into a pass rather than an error. If ReadClearedProperties stops matching
        // (the reconciler routes clears through a helper, the invocation shape changes, the
        // matcher regresses), `clears` is empty, every property takes the `continue`, `offenders`
        // is empty, and `Assert.True(offenders.Count == 0, …)` is vacuously true. Both arms of
        // this test then agree that nothing is wrong, and no other test calls that reader.
        //
        // Measured through this test's own parser rather than a text scan, for the reason given
        // above: 28 type-gated clears over the same 11 properties, mirroring the write side
        // exactly (Padding 6, CornerRadius 5, Background 3, six properties at 2, the two
        // ContentAlignment properties at 1). Same floor and same calibration as its sibling —
        // losing either of the two longest chains trips it, with four arms of slack.
        var clearPairs = clears.Sum(entry => entry.Value.Count);

        Assert.True(
            clearPairs >= 24,
            $"Only {clearPairs} type-gated modifier clear(s) were read out of the reconciler " +
            "(expected at least 24), while the write side still reads " + pairs + ". Either the " +
            "unset arms were removed wholesale, or the `v.ClearValue(WinUI.T.PropProperty)` " +
            "shape has changed and this test can no longer see the clears it compares against — " +
            "in which case the comparison below passes without checking anything. Properties " +
            $"seen: [{string.Join(", ", clears.Keys)}]");

        var offenders = new List<string>();
        var unreleased = new List<string>();

        // Receiver-blind: is the property released *at all*, through any
        // `x.ClearValue(WinUI.T.PropProperty)`? The gate-bound reader above cannot answer that,
        // because an arm is free to clear through the method's own `fe` rather than a gate's
        // pattern variable. Both readers share one extraction, so this is a superset of `clears`
        // by construction — which is precisely what makes the assert below a check on the one
        // axis that can independently break: the *scope* it walks.
        var releasedSomehow = ReadAnyClearedProperties();

        Assert.True(
            clears.Keys.All(releasedSomehow.Contains),
            "The receiver-blind ClearValue reader missed a property the gate-bound reader found. " +
            "It matches a superset of the same invocations, so this only happens if it walked the " +
            "wrong scope — and a reader that walks nothing turns the branch below into the " +
            "unconditional `continue` it replaced. Gate-bound: " +
            $"[{string.Join(", ", clears.Keys)}]; receiver-blind: " +
            $"[{string.Join(", ", releasedSomehow.OrderBy(name => name, StringComparer.Ordinal))}]");

        foreach (var (property, writtenTypes) in writes)
        {
            if (!clears.TryGetValue(property, out var clearedTypes))
            {
                // No type-dispatch chain to compare against. Legitimate for an arm that expresses
                // its clear as an ungated `fe.ClearValue(…)` instead of through the gate's own
                // pattern variable: semantically identical, and correctly not a chain. Only an arm
                // that already dispatches on type is asked to dispatch over the same set of types.
                //
                // Not legitimate when the property is released nowhere — that is the #986 defect
                // itself, and skipping it unconditionally is how this test used to certify it.
                // Every_Diff_Guarded_Modifier_Has_An_Unset_Arm does not cover the gap: it
                // deliberately excludes the three modifiers that compute a local first (Margin,
                // Padding, BorderThickness — see its remarks), so for exactly those three a
                // deleted unset arm is invisible to both tests. Verified by mutation — deleting
                // BorderThickness's whole unset arm while keeping its two type-gated writes left
                // all seven tests in this file green before this branch existed. The write-side
                // floor cannot catch it either: `writes` is untouched by a clear-side deletion,
                // and the clear-side floor only falls from 28 to 26, still over 24.
                if (!releasedSomehow.Contains(property)) unreleased.Add(property);
                continue;
            }

            foreach (var type in writtenTypes.Except(clearedTypes))
                offenders.Add($"{property} on {type}");
        }

        Assert.True(
            unreleased.Count == 0,
            "These modifiers are written to at least one control type and released on none of " +
            "them — no type-gated clear, and no ungated `fe.ClearValue(…)` either — so the value " +
            "stays pinned on the control forever once the modifier is dropped from the chain " +
            $"(issue #986): [{string.Join(", ", unreleased)}]. Restore the unset arm. If you " +
            "reached this after resolving a Reconciler.cs merge conflict, the resolution dropped " +
            "the whole arm — reapply it rather than deleting the write.");

        Assert.True(
            offenders.Count == 0,
            "These control types can have a modifier written but never released — the write " +
            "half of a type-dispatch chain was widened without the clear half, so the value " +
            "stays pinned on that control type forever (issue #986): " +
            $"[{string.Join(", ", offenders)}]. Add the matching " +
            "`else if (fe is WinUI.<Type> v) v.ClearValue(WinUI.<Type>.<Prop>Property);` " +
            "branch to the unset arm. If you reached this after resolving a Reconciler.cs " +
            "merge conflict, the conflict resolution dropped the clear branch — reapply it " +
            "rather than deleting the write.");
    }

    /// <summary>
    /// The <c>fe is WinUI.T v</c> shape used to gate a modifier write on the control type,
    /// or null when the condition is not a type gate.
    /// </summary>
    private static (string TypeName, string Variable)? ReadTypeGate(IfStatementSyntax ifStatement)
    {
        if (ifStatement.Condition is not IsPatternExpressionSyntax pattern) return null;
        if (pattern.Expression is not IdentifierNameSyntax { Identifier.Text: "fe" }) return null;
        if (pattern.Pattern is not DeclarationPatternSyntax declaration) return null;
        if (declaration.Designation is not SingleVariableDesignationSyntax designation) return null;

        var qualified = declaration.Type.ToString();
        var typeName = qualified[(qualified.LastIndexOf('.') + 1)..];
        return typeName.Length == 0 ? null : (typeName, designation.Identifier.Text);
    }

    /// <summary>Properties assigned through <c>variable.Prop = …</c> inside the branch.</summary>
    private static IEnumerable<string> ReadWrittenProperties(SyntaxNode branch, string variable) =>
        branch.DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .Select(assignment => assignment.Left)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(access => access.Expression is IdentifierNameSyntax id
                             && id.Identifier.Text == variable)
            .Select(access => access.Name.Identifier.Text);

    /// <summary>
    /// Properties released through <c>variable.ClearValue(WinUI.T.PropProperty)</c> inside the
    /// branch, named by the modifier rather than the DP field so they key the same as the
    /// writes.
    /// </summary>
    private static IEnumerable<string> ReadClearedProperties(SyntaxNode branch, string variable) =>
        ReadClearedPropertyNames(branch, variable);

    /// <summary>
    /// The <c>ClearValue</c> shape, in one place. <paramref name="variable"/> restricts the
    /// receiver; <see langword="null"/> accepts any. Both callers share this so the gate-bound
    /// and receiver-blind readers cannot drift into disagreeing about what a clear looks like.
    /// </summary>
    private static IEnumerable<string> ReadClearedPropertyNames(SyntaxNode scope, string? variable) =>
        scope.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax access
                                 && access.Name.Identifier.Text == "ClearValue"
                                 && access.Expression is IdentifierNameSyntax id
                                 && (variable is null || id.Identifier.Text == variable))
            .SelectMany(invocation => invocation.ArgumentList.Arguments)
            .Select(argument => argument.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(access => access.Name.Identifier.Text)
            .Where(name => name.EndsWith("Property", StringComparison.Ordinal))
            .Select(name => name[..^"Property".Length]);

    /// <summary>
    /// Every property released through <c>ClearValue(WinUI.T.PropProperty)</c> anywhere in the
    /// scanned methods, whatever the receiver.
    /// </summary>
    /// <remarks>
    /// Answers "is this released at all", which the gate-bound reader cannot: a clear expressed
    /// as an ungated <c>fe.ClearValue(…)</c> is invisible to it, and treating that absence as
    /// "nothing to compare" is indistinguishable from the arm having been deleted outright.
    /// Whole-method scope on purpose — an unset arm need not sit inside a type gate, and reading
    /// gates only would reproduce the blind spot in a new place.
    /// </remarks>
    private static HashSet<string> ReadAnyClearedProperties()
    {
        var scanned = ScannedMethods
            .Select(entry => entry.Method)
            .ToHashSet(StringComparer.Ordinal);

        var methods = ReconcilerRoot.Value
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => scanned.Contains(method.Identifier.Text))
            .ToList();

        Assert.True(
            methods.Count > 0,
            $"None of the scanned methods ({string.Join(", ", scanned)}) were found in " +
            "Reconciler.cs, so the receiver-blind clear scan would report every property as " +
            "never released.");

        return methods
            .SelectMany(method => ReadClearedPropertyNames(method, variable: null))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, SortedSet<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var set))
        {
            set = new SortedSet<string>(StringComparer.Ordinal);
            map[key] = set;
        }

        set.Add(value);
    }

    /// <summary>
    /// The deferred <c>LabeledBy</c> resolution must re-check that its request is still the
    /// current one before writing.
    /// </summary>
    /// <remarks>
    /// <c>ApplyAccessibilityModifiers</c> cannot resolve a <c>LabeledBy</c> AutomationId until
    /// the element is in the visual tree, so an unresolved request parks a <c>Loaded</c>
    /// handler that outlives the render which created it. If that handler writes
    /// unconditionally, this sequence re-pins a label the user dropped: render A requests a
    /// not-yet-present id and parks the handler; render B drops the modifier and the unset arm
    /// clears the property; the element then loads and the stale handler resolves the *old* id
    /// and sets it again — permanently, since no later render will clear a property whose
    /// modifier is already absent. That defeats the reset arm for exactly the property the arm
    /// exists to release (issue #986).
    /// <para>
    /// Pinned structurally rather than behaviourally because driving it needs <c>Loaded</c>
    /// held open across a re-render, which the selftest harness cannot do — a fixture that
    /// merely dropped the modifier and re-rendered would pass whether or not the guard is
    /// present, which is the vacuous shape this repo rejects. The reachable half (drop
    /// releases the property) is covered by
    /// <c>ModifierEvent_AccessibilityClearResets</c>' <c>A11yClear_Phase1_LabeledByReleased</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Deferred_LabeledBy_Rechecks_Pending_Request()
    {
        // The LabeledBy set/unset pair: the only if-statement in the method whose condition
        // names LabeledBy and which has an else. Located through the syntax tree rather than
        // by text search so a match cannot be satisfied by an enclosing statement's text.
        var labeledBy = ReadIfStatements("ApplyAccessibilityModifiers")
            .Where(statement => statement.Condition.ToString().Contains("LabeledBy", StringComparison.Ordinal))
            .FirstOrDefault(statement => statement.Else is not null);

        Assert.True(
            labeledBy is not null,
            "No `if (... LabeledBy ...) { } else { }` pair found in ApplyAccessibilityModifiers. " +
            "If LabeledBy stopped deferring resolution this test is obsolete — delete it; if it " +
            "moved, retarget it. Leaving it matching nothing would silently stop guarding the " +
            "stale-write bug.");

        // The deferred handler itself — the local function registered on Loaded. Scoped to the
        // function body, not the enclosing branch: the branch also *publishes* the request
        // (`PendingLabeledBy = labelId`), so a branch-wide search for the field name passes
        // even with the handler's re-check deleted. Verified by mutation.
        var handler = labeledBy!.Statement
            .DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .FirstOrDefault(fn => fn.ParameterList.Parameters.Count == 2);

        Assert.True(
            handler is not null,
            "The LabeledBy set arm no longer declares a local Loaded handler. If deferral was " +
            "removed entirely the stale-write bug is gone and this test should be deleted; if " +
            "the handler merely moved or changed shape, retarget the test.");

        var handlerBody = handler!.Body?.ToString() ?? handler.ExpressionBody?.ToString() ?? "";

        Assert.True(
            handlerBody.Contains("Loaded -=", StringComparison.Ordinal),
            "The deferred LabeledBy handler no longer unsubscribes itself, so it can fire on " +
            $"every later load. Handler body reads: {handlerBody}");

        Assert.True(
            handlerBody.Contains("PendingLabeledBy", StringComparison.Ordinal),
            "The deferred LabeledBy handler no longer consults ReactorState.PendingLabeledBy. " +
            "Without that re-check it writes the id captured at request time, so a LabeledBy " +
            "dropped before the element loads gets re-applied after the unset arm cleared it " +
            $"and stays pinned forever (issue #986). Handler body reads: {handlerBody}");

        // The unset arm must retire the pending request, or cancelling is impossible however
        // carefully the handler re-checks. Read from the else clause alone.
        var unsetArm = labeledBy.Else!.Statement.ToString();

        Assert.True(
            unsetArm.Contains("ClearValue", StringComparison.Ordinal)
            && unsetArm.Contains("LabeledByProperty", StringComparison.Ordinal),
            "The LabeledBy unset arm no longer clears LabeledByProperty, so a dropped label " +
            $"stays pinned on the control (issue #986). Arm reads: {unsetArm}");

        Assert.True(
            unsetArm.Contains("PendingLabeledBy", StringComparison.Ordinal),
            "The LabeledBy unset arm clears the dependency property without also clearing " +
            "ReactorState.PendingLabeledBy, so a parked Loaded handler will still consider its " +
            $"request current and re-apply the dropped label (issue #986). Arm reads: {unsetArm}");

        // Ordering, not just presence. Two handlers can be parked on one element at once: the
        // set arm's `a.LabeledBy != oldA?.LabeledBy` diff guard means the second park always
        // carries a *different* labelId, so on Loaded the stale handler runs first and the live
        // one second. The stale handler is harmless only because its staleness guard precedes
        // the clear — it bails before touching a token that now belongs to the live request.
        // Hoist the clear above that guard and the stale handler cancels the live one, which
        // then no-ops and leaves the label unresolved forever. Nothing else pins this ordering.
        //
        // Located by syntax span, not by string index. Keying on the first `return` in the body
        // is vacuous: the handler opens with an unrelated `TryGetReactorState` bail-out, so a
        // clear hoisted above the *staleness* guard still sorts after that first return and the
        // assertion passes while the bug is present. Verified by mutation.
        var stalenessGuard = handler.Body?
            .DescendantNodes()
            .OfType<IfStatementSyntax>()
            .FirstOrDefault(statement =>
                statement.Condition.ToString().Contains("labelId", StringComparison.Ordinal));

        var retire = handler.Body?
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(assignment =>
                assignment.Left.ToString().Contains("PendingLabeledBy", StringComparison.Ordinal)
                && assignment.Right.ToString() == "null");

        Assert.True(
            stalenessGuard is not null && retire is not null,
            "Could not locate both the labelId staleness guard and the `PendingLabeledBy = null` " +
            "retirement in the deferred LabeledBy handler, so their ordering cannot be checked " +
            $"and this assertion would pass vacuously. Handler body reads: {handlerBody}");

        Assert.True(
            stalenessGuard!.Span.End < retire!.SpanStart,
            "The deferred LabeledBy handler retires ReactorState.PendingLabeledBy before the " +
            "guard that returns when the captured labelId is no longer the current request. A " +
            "handler parked by an earlier render would therefore cancel a *later* render's " +
            "still-pending request before returning, and the live handler would then see a null " +
            $"token, no-op, and leave the label unresolved (issue #986). Handler body reads: {handlerBody}");
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
    /// The positive half of <see cref="CleanElement_Resets_Through_ClearValue_Not_Default_Assignment"/>,
    /// and its dual rather than its subordinate. Three distinct faults, three different halves:
    /// <list type="bullet">
    ///   <item>an outright deleted <c>ClearValue</c> produces no offender for the scan to
    ///     catch, so the required releases are pinned by name — the original reason;</item>
    ///   <item>anything that SHRINKS the scanned region (see the anchor note in
    ///     <c>ReadCleanElementCommonBlock</c>) makes the scan vacuous rather than failing,
    ///     and only this pin set notices — MEASURED: region 12922 -&gt; 7335 chars leaves the
    ///     scan green and reddens this test;</item>
    ///   <item>conversely, a default-assignment reset of a property NOT among these rows is
    ///     invisible here and caught only by the scan — MEASURED: injecting
    ///     <c>fe.AllowDrop = false;</c> reddens the scan, names the property, and leaves this
    ///     test green.</item>
    /// </list>
    /// So the pin set guards the scan's SCOPE and the scan guards the pin set's CLOSED WORLD:
    /// a hardcoded list defends only the names someone thought to write down, and the scan
    /// catches the 35th. Each is precisely blind where the other is loud, which is why neither
    /// is redundant. Note that the first bullet — the stated original reason — is the one that
    /// looks dispensable next to the pool analyzer, so anyone collapsing this pair will most
    /// likely do it by reading that bullet alone and dropping two protections nobody wrote down.
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
            // The filter runs on the projection, not the key: `Where(closure.ContainsKey)`
            // followed by `closure[gateName]` would hash the same key twice, so resolve each
            // gate once and drop the misses.
            foreach (var receiver in info.ControlGate
                         .Select(closure.GetValueOrDefault)
                         .OfType<Type>())
            {
                required.Add(receiver.Name + "." + property);
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

    /// <summary>Reconciler.cs, parsed once per test run rather than once per helper call.</summary>
    private static readonly Lazy<SyntaxNode> ReconcilerRoot = new(() =>
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var file = Path.Join(root!, "src", "Reactor", "Core", "Reconciler.cs");
        Assert.True(File.Exists(file), $"Reconciler.cs not found at {file}");
        return CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    });

    /// <summary>Every <c>if</c> statement in the named reconciler method(s).</summary>
    private static List<IfStatementSyntax> ReadIfStatements(string methodName)
    {
        var methods = ReconcilerRoot.Value
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text == methodName)
            .ToList();

        Assert.True(methods.Count > 0, $"No {methodName} method found in Reconciler.cs");

        return methods
            .SelectMany(method => method.DescendantNodes().OfType<IfStatementSyntax>())
            .ToList();
    }

    /// <summary>
    /// Every <c>else</c>-arm in the scanned reconciler methods guarded by an unset
    /// transition — the modifier is absent now and was present on the previous render.
    /// </summary>
    private static List<(string Method, string Modifier, StatementSyntax Arm)> ReadUnsetArms()
    {
        var arms = new List<(string, string, StatementSyntax)>();

        foreach (var (method, bag, oldBag, _, minUnsetArms) in ScannedMethods)
        {
            var found = 0;
            foreach (var ifStatement in ReadIfStatements(method))
            {
                var modifier = UnsetTransitionModifier(ifStatement.Condition, bag, oldBag)
                               ?? ElseOfSetArmModifier(ifStatement, bag, oldBag);
                if (modifier is null) continue;

                // Only the arm's own body — an `else if` chain nests the next arm inside this
                // one's Else clause, and attributing that arm's statements here would report
                // every following modifier under the first one's name.
                arms.Add((method, modifier, ifStatement.Statement));
                found++;
            }

            // Per-method non-vacuity floor, asserted here rather than at the call sites so no
            // consumer can forget it — Every_Unset_Arm_Actually_Resets_Something had no floor
            // at all, and Every_Unset_Arm_Clears_The_Dependency_Property had only a combined
            // one. A combined floor cannot see a single method disappear: the ~37 arms in
            // ApplyModifiers clear any plausible total on their own, so the a11y half could
            // stop matching entirely and both tests would still report zero offenders.
            Assert.True(
                found >= minUnsetArms,
                $"Only {found} modifier-unset arm(s) were read out of {method} (expected at " +
                $"least {minUnsetArms}). The unset-transition shape ('!{bag}.X.HasValue && " +
                $"{oldBag}?.X.HasValue == true' or '{bag}.X is null && {oldBag}?.X is not " +
                "null') has probably changed, which would make every caller of ReadUnsetArms " +
                "pass without checking anything.");
        }

        return arms;
    }

    /// <summary>
    /// Every modifier name covered by an unset arm in the named method. Unlike
    /// <see cref="ReadUnsetArms"/> this returns <em>all</em> names an arm releases, not just
    /// the first: the ToolTip/RichToolTip pair share one arm, and reporting only one of them
    /// would make the other look unreleased.
    /// </summary>
    private static HashSet<string> ReadUnsetModifierNames(string methodName, string bag, string oldBag)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ifStatement in ReadIfStatements(methodName))
        {
            var text = ifStatement.Condition.ToString();
            var unset = ModifierNames(ifStatement.Condition, bag, oldBag)
                .Where(name => AbsentNow(text, bag, name) && PresentBefore(text, oldBag, name));

            foreach (var name in unset) names.Add(name);

            var implicitElse = ElseOfSetArmModifier(ifStatement, bag, oldBag);
            if (implicitElse is not null) names.Add(implicitElse);
        }

        return names;
    }

    /// <summary>
    /// Modifiers written under a diff guard in the named method — the condition names both
    /// <c>bag.X</c> and <c>oldBag.X</c> and does not test for absence, i.e. "write X when
    /// present, compared against the previous render". Every one of these owes a reset.
    /// </summary>
    private static HashSet<string> ReadDiffGuardedModifiers(string methodName, string bag, string oldBag)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ifStatement in ReadIfStatements(methodName))
        {
            var text = ifStatement.Condition.ToString();
            var diffGuarded = ModifierNames(ifStatement.Condition, bag, oldBag)
                // The unset arm itself names both bags too; excluding absence tests keeps this
                // to the set arms, which are the ones that owe a reset.
                .Where(name => !AbsentNow(text, bag, name))
                .Where(name => MentionsCurrent(text, bag, name))
                .Where(name => PresentBefore(text, oldBag, name) || MentionsDiff(text, oldBag, name));

            foreach (var name in diffGuarded) names.Add(name);
        }

        return names;
    }

    /// <summary>"this modifier is absent on the current render", in every shape used.</summary>
    private static bool AbsentNow(string text, string bag, string name) =>
        text.Contains($"!{bag}.{name}.HasValue", StringComparison.Ordinal)
        || text.Contains($"{bag}.{name} is null", StringComparison.Ordinal)
        || text.Contains($"{bag}?.{name} is null", StringComparison.Ordinal)
        || text.Contains($"{bag}?.{name}.HasValue != true", StringComparison.Ordinal);

    /// <summary>"this modifier was present on the previous render", in every shape used.</summary>
    private static bool PresentBefore(string text, string oldBag, string name) =>
        text.Contains($"{oldBag}?.{name}.HasValue == true", StringComparison.Ordinal)
        || text.Contains($"{oldBag}?.{name} is not null", StringComparison.Ordinal);

    /// <summary>The previous render's value is read at all — a diff guard, not a reset test.</summary>
    private static bool MentionsDiff(string text, string oldBag, string name) =>
        Regex.IsMatch(text, $@"(?<![\w.]){Regex.Escape(oldBag)}\??\.{Regex.Escape(name)}\b");

    /// <summary>The current render's value is read at all.</summary>
    private static bool MentionsCurrent(string text, string bag, string name) =>
        Regex.IsMatch(text, $@"(?<![\w.]){Regex.Escape(bag)}\??\.{Regex.Escape(name)}\b");

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
    private static string? UnsetTransitionModifier(ExpressionSyntax condition, string bag, string oldBag)
    {
        var text = condition.ToString();

        foreach (var name in ModifierNames(condition, bag, oldBag).Distinct(StringComparer.Ordinal))
        {
            if (AbsentNow(text, bag, name) && PresentBefore(text, oldBag, name)) return name;
        }

        // Padding and BorderThickness are computed into a local first (to overlay the
        // BiDi-aware inline variants), so the absent-now half reads `!resolvedPadding.HasValue`
        // rather than `!m.Padding.HasValue`. Match on the oldM half and the local's shape.
        var resolved = Regex.Match(text, @"!\s*(resolved\w+)\.HasValue");
        if (resolved.Success)
        {
            var oldHalf = Regex.Match(text, $@"{Regex.Escape(oldBag)}\?\.(\w+)\.HasValue == true");
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
    private static string? ElseOfSetArmModifier(IfStatementSyntax ifStatement, string bag, string oldBag)
    {
        if (ifStatement.Parent is not ElseClauseSyntax { Parent: IfStatementSyntax setArm }) return null;

        var text = ifStatement.Condition.ToString();
        var oldHalf = Regex.Match(text, $@"{Regex.Escape(oldBag)}\?\.(\w+)(?:\.HasValue == true| is not null)");
        if (!oldHalf.Success) return null;

        var name = oldHalf.Groups[1].Value;

        // If the condition tests `m.X` itself, UnsetTransitionModifier already owns it (or
        // deliberately rejected it); only the implicit-else shape belongs here.
        if (MentionsCurrent(text, bag, name)) return null;

        // The guarding `if` must be the set arm for the same modifier, otherwise this is some
        // unrelated `else if` that happens to mention oldM.
        if (!MentionsCurrent(setArm.Condition.ToString(), bag, name)) return null;

        return name;
    }

    /// <summary>Modifier names read off the bag identifiers in a condition.</summary>
    private static IEnumerable<string> ModifierNames(SyntaxNode node, string bag, string oldBag)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            switch (descendant)
            {
                case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax bagId } access
                    when bagId.Identifier.Text == bag || bagId.Identifier.Text == oldBag:
                    yield return access.Name.Identifier.Text;
                    break;

                case ConditionalAccessExpressionSyntax { Expression: IdentifierNameSyntax bagId } conditional
                    when bagId.Identifier.Text == bag || bagId.Identifier.Text == oldBag:
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
    /// <remarks>
    /// The boundary is located through Roslyn syntax nodes, not a text scan, and that is
    /// load-bearing rather than stylistic. Any text form — <c>IndexOf("switch (fe)")</c> or an
    /// anchored <c>^\s*switch</c> regex alike — can be made to match inside a comment, which
    /// silently truncates the region and turns every absence-shaped assertion over it vacuous
    /// while still reporting green. A line comment defeats the unanchored form and a block
    /// comment whose inner line begins with the dispatch text defeats the anchored one; a
    /// <see cref="SwitchStatementSyntax"/> cannot be forged by a comment of any shape.
    /// Do not "simplify" this back to a string search.
    /// <para>
    /// Closing the forgery route does not make the presence-shaped detectors redundant, and
    /// they must not be deleted on that reasoning. The two halves guard different things: the
    /// presence pins guard this region's <em>scope</em> (a truncated region makes every
    /// absence-shaped assertion pass more readily, so it goes vacuous without failing), and the
    /// absence scan guards the pin list's <em>closed world</em> (a pin list only knows the names
    /// someone thought to write down). Neither subsumes the other. The presence half lives in
    /// <see cref="CleanElement_Releases_Every_Modifier_Backed_Dependency_Property"/> here, and in
    /// <c>Every_TrappedProperty_Is_Reset_In_CleanElement</c>,
    /// <c>Every_TrappedAttachedProperty_Is_Reset_In_CleanElement</c> and
    /// <c>Attached_Reset_Scan_Sees_Every_Owner_The_Table_Names</c> in
    /// <see cref="PoolResetSetConsistencyTests"/> — four detectors across two files.
    /// </para>
    /// </remarks>
    private static string ReadCleanElementCommonBlock(out string paramName)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var file = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(file), $"ElementPool.cs not found at {file}");

        var source = File.ReadAllText(file);
        var method = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(candidate =>
                candidate.Identifier.ValueText == "CleanElement"
                && candidate.Modifiers.Any(SyntaxKind.StaticKeyword)
                && candidate.ParameterList.Parameters.Count == 1
                && candidate.ParameterList.Parameters[0].Type?.ToString()
                    .EndsWith("FrameworkElement", StringComparison.Ordinal) == true);
        Assert.True(method is not null, "Could not locate static CleanElement(FrameworkElement) in ElementPool.cs");

        var body = method!.Body;
        Assert.True(body is not null, "CleanElement no longer has a block body — the FE-common region is undefined.");

        // Located on the syntax tree rather than by an anchored regex over the source text.
        // The regex form this replaces could be truncated by a block comment whose inner line
        // began with the dispatch keyword, and truncation was silent: every absence-shaped
        // assertion over this region ("no offender is present") passes more readily on a
        // smaller region, so it went vacuous without failing. A SwitchStatementSyntax lookup
        // cannot match a comment at all, which removes that residual rather than documenting
        // it. The presence-shaped detectors named in the remarks above are still the half that
        // catches a region defect, and none of them is made unnecessary by this change.
        paramName = method.ParameterList.Parameters[0].Identifier.ValueText;
        var governingName = paramName;

        var dispatch = body!.DescendantNodes()
            .OfType<SwitchStatementSyntax>()
            .FirstOrDefault(candidate => candidate.Expression is IdentifierNameSyntax governing
                && governing.Identifier.ValueText == governingName);
        Assert.True(dispatch is not null, $"CleanElement layout changed — no 'switch ({governingName})' boundary found.");

        return source[body.OpenBraceToken.SpanStart..dispatch!.SpanStart];
    }
}
