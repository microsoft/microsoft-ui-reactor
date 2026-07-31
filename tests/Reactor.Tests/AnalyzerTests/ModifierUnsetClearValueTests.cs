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
    private static readonly (string Method, string Bag, string OldBag)[] ScannedMethods =
    [
        ("ApplyModifiers", "m", "oldM"),
        ("ApplyAccessibilityModifiers", "a", "oldA"),
    ];

    /// <summary>
    /// Statements in an unset arm that are legitimately not <c>ClearValue</c> calls, keyed
    /// by the assignment target as written. Empty on purpose — every unset arm in
    /// <c>ApplyModifiers</c> can and does clear its dependency property. An entry here is a
    /// claim that a property has no DP to clear; add one only with a reason.
    /// </summary>
    private static readonly Dictionary<string, string> ApplyModifiersAssignmentExceptions = new(StringComparer.Ordinal);

    /// <summary>
    /// Diff-guarded modifiers that deliberately have <em>no</em> unset arm, keyed
    /// <c>Method.Modifier</c>. Every entry must still describe a genuinely missing arm —
    /// <see cref="Every_Diff_Guarded_Modifier_Has_An_Unset_Arm"/> fails on a stale entry too,
    /// so landing the arm forces the exception to be deleted rather than left to rot.
    /// </summary>
    private static readonly Dictionary<string, string> MissingUnsetArmExceptions =
        new(StringComparer.Ordinal)
        {
            // Issue #1001. These four are XAML *facade* properties: the DP identifier is real,
            // but the live value lives on the element's composition visual, and the set path
            // (AnimationHelper.SetOrAnimate / SetOrAnimateVector3) calls visual.StartAnimation
            // whenever a curve is ambient. A running composition animation outranks the DP and
            // the XAML property was never assigned, so ClearValue has no local value to release
            // — the mechanical arm every other modifier gets would be a reset that silently
            // does nothing in the animated case. The fix needs a StopAnimation companion plus a
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
        var scanned = new List<string>();
        var missing = new List<string>();

        foreach (var (method, bag, oldBag) in ScannedMethods)
        {
            var unset = ReadUnsetModifierNames(method, bag, oldBag);
            foreach (var modifier in ReadDiffGuardedModifiers(method, bag, oldBag))
            {
                scanned.Add($"{method}.{modifier}");
                if (!unset.Contains(modifier)) missing.Add($"{method}.{modifier}");
            }
        }

        // Same non-vacuity floor as the arm scan above: a matcher that stops recognizing the
        // set-arm shape would report zero missing arms and pass, which is the failure mode
        // this whole test exists to prevent.
        Assert.True(
            scanned.Count >= 30,
            $"Only {scanned.Count} diff-guarded modifier(s) were read out of " +
            $"[{string.Join(", ", ScannedMethods.Select(entry => entry.Method))}]. The set-arm shape " +
            "('m.X.HasValue && m.X != oldM?.X') has probably changed, which would make this test " +
            "pass without checking anything.");

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

        foreach (var (method, bag, oldBag) in ScannedMethods)
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
            foreach (var name in ModifierNames(ifStatement.Condition, bag, oldBag))
            {
                if (AbsentNow(text, bag, name) && PresentBefore(text, oldBag, name))
                    names.Add(name);
            }

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
            foreach (var name in ModifierNames(ifStatement.Condition, bag, oldBag))
            {
                // The unset arm itself names both bags too; excluding absence tests keeps this
                // to the set arms, which are the ones that owe a reset.
                if (AbsentNow(text, bag, name)) continue;
                if (!MentionsCurrent(text, bag, name)) continue;
                if (!PresentBefore(text, oldBag, name) && !MentionsDiff(text, oldBag, name)) continue;
                names.Add(name);
            }
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
