using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Pack;
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
    private static readonly (string Method, string Bag, string OldBag, int MinArms)[] ScannedMethods =
    [
        // MinArms is a per-method non-vacuity floor, deliberately close to the real count
        // (37 and 12 at the time of writing). A single total floor is not enough: if the
        // matcher stopped recognizing ApplyAccessibilityModifiers entirely, ApplyModifiers
        // alone would still clear a global threshold and the a11y half would go unchecked
        // in silence — which is exactly the half issue #986 found broken.
        ("ApplyModifiers", "m", "oldM", 32),
        ("ApplyAccessibilityModifiers", "a", "oldA", 10),
    ];

    /// <summary>
    /// Assignments inside an unset arm that are legitimately not <c>ClearValue</c> calls, keyed
    /// by the assignment target's trailing identifier. An entry is a claim that the target is
    /// not a dependency property at all — reconciler bookkeeping that happens to live inside the
    /// arm — so the #952 precedence argument does not apply to it. Add one only with a reason;
    /// anything that <em>is</em> a DP must go through <c>ClearValue</c>.
    /// </summary>
    private static readonly Dictionary<string, string> ApplyModifiersAssignmentExceptions =
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

    [Fact]
    public void Every_ApplyModifiers_Unset_Arm_Clears_The_Dependency_Property()
    {
        var arms = ReadUnsetArms();

        // A parser that finds nothing would make every assertion below vacuous. The two
        // methods have ~45 unset arms today; 20 is a floor that survives ordinary churn but
        // not a shape change that silently stops matching.
        Assert.True(
            arms.Count >= 20,
            $"Only {arms.Count} modifier-unset arm(s) were read out of the reconciler. " +
            "The unset-transition shape ('!m.X.HasValue && oldM?.X.HasValue == true' or " +
            "'m.X is null && oldM?.X is not null') has probably changed, which would make this " +
            "whole test pass without checking anything.");

        var offenders = new List<string>();

        foreach (var (method, modifier, arm) in arms)
        {
            foreach (var assignment in arm.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var target = assignment.Left.ToString();
                var property = target.Contains('.') ? target[(target.LastIndexOf('.') + 1)..] : target;
                if (ApplyModifiersAssignmentExceptions.ContainsKey(property)) continue;

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

        foreach (var (method, bag, oldBag, minArms) in ScannedMethods)
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
    /// <em>cleared</em> on.
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
    /// Verified by mutation — deleting a single <c>else if (fe is WinUI.T …) …ClearValue(…)</c>
    /// branch while leaving its write branch in place reddens this test and nothing else.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_Type_Gated_Write_Has_A_Matching_Type_Gated_Clear()
    {
        var writes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var clears = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var (method, _, _, _) in ScannedMethods)
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
        // exists to prevent. There are 14 type-gated write pairs today.
        var pairs = writes.Sum(entry => entry.Value.Count);

        Assert.True(
            pairs >= 10,
            $"Only {pairs} type-gated modifier write(s) were read out of the reconciler " +
            "(expected at least 10). The `if (fe is WinUI.T v) v.Prop = …` shape has probably " +
            "changed, which would make this test pass without checking anything. Properties " +
            $"seen: [{string.Join(", ", writes.Keys)}]");

        var offenders = new List<string>();

        foreach (var (property, writtenTypes) in writes)
        {
            // A property with no type-gated clear at all is a missing *arm*, which is
            // Every_Diff_Guarded_Modifier_Has_An_Unset_Arm's question — and several properties
            // legitimately clear through an ungated `fe.ClearValue(…)`. Only an arm that
            // already dispatches on type is asked to dispatch over the same set of types.
            if (!clears.TryGetValue(property, out var clearedTypes)) continue;

            foreach (var type in writtenTypes.Except(clearedTypes))
                offenders.Add($"{property} on {type}");
        }

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
        branch.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax access
                                 && access.Name.Identifier.Text == "ClearValue"
                                 && access.Expression is IdentifierNameSyntax id
                                 && id.Identifier.Text == variable)
            .SelectMany(invocation => invocation.ArgumentList.Arguments)
            .Select(argument => argument.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(access => access.Name.Identifier.Text)
            .Where(name => name.EndsWith("Property", StringComparison.Ordinal))
            .Select(name => name[..^"Property".Length]);

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
        var commonBlock = ReadCleanElementCommonBlock(out _);

        var cleared = Regex.Matches(commonBlock, @"\b\w+\.ClearValue\(\s*(?:[\w.]+\.)?(\w+)\.(\w+)Property\s*\)")
            .Select(match => match.Groups[1].Value + "." + match.Groups[2].Value)
            .ToHashSet(StringComparer.Ordinal);

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

    // ── Source-scanning helpers ─────────────────────────────────────────────

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

        foreach (var (method, bag, oldBag, _) in ScannedMethods)
        {
            foreach (var ifStatement in ReadIfStatements(method))
            {
                var modifier = UnsetTransitionModifier(ifStatement.Condition, bag, oldBag)
                               ?? ElseOfSetArmModifier(ifStatement, bag, oldBag);
                if (modifier is null) continue;

                // Only the arm's own body — an `else if` chain nests the next arm inside this
                // one's Else clause, and attributing that arm's statements here would report
                // every following modifier under the first one's name.
                arms.Add((method, modifier, ifStatement.Statement));
            }
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

        var switchStart = source.IndexOf($"switch ({paramName})", braceStart, StringComparison.Ordinal);
        Assert.True(switchStart > braceStart, $"CleanElement layout changed — no 'switch ({paramName})' boundary found.");

        return source[braceStart..switchStart];
    }
}
