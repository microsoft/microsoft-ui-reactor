using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Tests.Tooling;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Pins where the accessibility environment hooks read their initial value.
///
/// <para>
/// <c>UseReducedMotion</c>, <c>UseHighContrast</c> and <c>UseHighContrastScheme</c> all shipped
/// seeding their state <em>inside</em> the <c>UseEffect</c> body. Effects do not run until after
/// the first render commits, so the first frame reported <c>false</c> / <c>null</c> to every
/// caller — and a component that never re-renders keeps that value for its whole lifetime. Both
/// hooks therefore failed in the one direction an accessibility hook must not: the first paint
/// denies the accommodation to exactly the users who requested it. Observed live on the
/// AnimatedIcon gallery page, where a reduced-motion notice stayed hidden on arrival and only
/// appeared after an unrelated click forced a re-render.
/// </para>
///
/// <para>
/// The value itself is environment-derived — it depends on the machine's "Animation effects"
/// setting — so a test that asserts <c>UseReducedMotion() == true</c> is a tautology on any
/// machine where animations are enabled, and passes just as happily against the buggy code. That
/// is exactly how the bug survived: the existing selftest
/// (<c>CoreReconcilerRenderCoverageFixtures</c>) reads the hook and renders its value into a
/// TextBlock without asserting anything about it, so it is green for a correct hook, a broken
/// hook, or a constant.
/// </para>
///
/// <para>
/// So this asserts the <em>mechanism</em> instead, which is environment-independent: the seeding
/// write must exist outside the effect lambda. Moving it back inside — the precise regression —
/// reddens this on every machine, with or without reduced motion enabled.
/// </para>
/// </summary>
public sealed class EnvironmentHookSeedingTests
{
    static SyntaxNode Parse(string source) => CSharpSyntaxTree.ParseText(source).GetRoot();

    static MethodDeclarationSyntax Method(string name)
    {
        var path = Path.Join(GallerySources.RepoRoot(), "src", "Reactor", "Core", "RenderContext.cs");
        Assert.True(File.Exists(path), $"RenderContext.cs not found at {path}");

        var root = Parse(File.ReadAllText(path));
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(m => m.Identifier.ValueText == name);

        // A rename would otherwise silently reduce every assertion below to "zero writes
        // found, none of them inside an effect" — vacuously true.
        Assert.True(method is not null, $"{name} not found in RenderContext.cs — was it renamed?");
        return method!;
    }

    /// <summary>
    /// True when <paramref name="node"/> sits inside a lambda that is an argument to a
    /// <c>UseEffect(...)</c> call, i.e. it runs after the render rather than during it.
    /// </summary>
    static bool InsideUseEffect(SyntaxNode node, MethodDeclarationSyntax scope) =>
        node.Ancestors()
            .TakeWhile(a => a != scope)
            .OfType<InvocationExpressionSyntax>()
            .Any(i => GallerySources.InvokedName(i) == "UseEffect");

    /// <summary>
    /// True when <paramref name="assignment"/> writes to <c>state.&lt;field&gt;</c> — receiver
    /// included. Matching the member name alone would count
    /// <c>other.IsReducedMotion = !state.Settings.AnimationsEnabled;</c>, i.e. a hook that seeds
    /// something other than its own state, as a correct seed.
    /// </summary>
    static bool TargetsState(AssignmentExpressionSyntax assignment, string field) =>
        assignment.Left is MemberAccessExpressionSyntax m
        && m.Name.Identifier.ValueText == field
        && m.Expression is IdentifierNameSyntax { Identifier.ValueText: "state" };

    /// <summary>
    /// True when <paramref name="assignment"/>'s right-hand side reads
    /// <c>state.&lt;settings&gt;.&lt;property&gt;</c> with the expected polarity — i.e. the value
    /// comes from the live WinRT settings object, from the <em>right</em> property on it, the
    /// <em>right</em> way round.
    ///
    /// <para>
    /// The receiver is pinned to <c>state</c> and not just to any object named
    /// <paramref name="settings"/>, and <paramref name="negated"/> is checked, because neither
    /// "reads something live" nor "reads the right property" is sufficient on its own. Both holes
    /// were measured: <c>state.IsReducedMotion = state.Settings.AnimationsEnabled;</c> — the
    /// correct source read the wrong way round, which reports "animations are fine" to exactly
    /// the users who turned them off — passed the previous version of this test 7/7.
    /// </para>
    /// </summary>
    static bool ReadsLiveSetting(
        AssignmentExpressionSyntax assignment, string settings, string property, bool negated) =>
        assignment.Right.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(read => read.Name.Identifier.ValueText == property
                && read.Expression is MemberAccessExpressionSyntax recv
                && recv.Name.Identifier.ValueText == settings
                && recv.Expression is IdentifierNameSyntax { Identifier.ValueText: "state" }
                && read.Parent.IsKind(SyntaxKind.LogicalNotExpression) == negated);

    /// <summary>
    /// Assignments to <c>state.&lt;field&gt;</c> within the hook, split by whether they run
    /// during render or inside the effect.
    ///
    /// <para>
    /// When <paramref name="settings"/> is given, only assignments that actually read
    /// <c>state.&lt;settings&gt;.&lt;property&gt;</c> with the expected polarity are counted.
    /// Without that the oracle is satisfied by <c>state.IsReducedMotion = false;</c> — a
    /// render-time write of the very default the bug produced — which was measured to pass an
    /// earlier version of this test 5/5. Checking <em>where</em> the write happens without
    /// checking <em>what it writes</em> is a partial oracle that reads as a complete one.
    /// </para>
    /// </summary>
    static (int DuringRender, int InsideEffect) Writes(
        MethodDeclarationSyntax method,
        string field,
        string? settings = null,
        string? property = null,
        bool negated = false)
    {
        var targets = method.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => TargetsState(a, field))
            .Where(a => settings is null || ReadsLiveSetting(a, settings, property!, negated))
            .ToList();

        return (targets.Count(a => !InsideUseEffect(a, method)),
                targets.Count(a => InsideUseEffect(a, method)));
    }

    [Theory]
    [InlineData("UseReducedMotionState", "IsReducedMotion", "Settings", "AnimationsEnabled", true)]
    [InlineData("UseHighContrastState", "IsHighContrast", "A11ySettings", "HighContrast", false)]
    [InlineData("UseHighContrastState", "HighContrastScheme", "A11ySettings", "HighContrastScheme", false)]
    public void EnvironmentHooks_SeedTheirValueDuringRender_NotOnlyInTheEffect(
        string hook, string field, string settings, string property, bool negated)
    {
        var method = Method(hook);
        var (duringRender, insideEffect) = Writes(method, field, settings, property, negated);

        var expected = negated ? $"!state.{settings}.{property}" : $"state.{settings}.{property}";

        // Positive control on the extractor: if this reads 0 the walker is broken, something was
        // renamed, or the read changed shape — and the real assertion below would then pass or
        // fail for a reason that has nothing to do with when the seed happens.
        Assert.True(duringRender + insideEffect > 0,
            $"no assignment of the form `state.{field} = {expected}` found anywhere in {hook} — "
            + "extractor broken, a member was renamed, or the read changed polarity/shape");

        Assert.True(duringRender > 0,
            $"{hook} only assigns state.{field} from {expected} inside UseEffect, so the "
            + "first render — the frame the user actually sees — reports the default instead of "
            + "the real system preference. Seed it during render; keep the effect for change "
            + "notifications.");
    }

    /// <summary>
    /// The effect must keep writing too: it carries the change subscription plus the re-read that
    /// closes the gap between the render-time seed and the subscription. A "fix" that deleted the
    /// effect writes would satisfy the assertion above while breaking live updates.
    /// </summary>
    [Theory]
    [InlineData("UseReducedMotionState", "IsReducedMotion")]
    [InlineData("UseHighContrastState", "IsHighContrast")]
    [InlineData("UseHighContrastState", "HighContrastScheme")]
    public void EnvironmentHooks_StillUpdateTheirValueFromTheEffect(string hook, string field)
    {
        var (_, insideEffect) = Writes(Method(hook), field);

        Assert.True(insideEffect > 0,
            $"{hook} no longer assigns state.{field} inside UseEffect, so the hook can no longer "
            + "react to the preference changing while the component is mounted.");
    }

    /// <summary>
    /// Control on <see cref="ReadsLiveSetting"/> itself, in every direction it can be wrong.
    ///
    /// <para>
    /// The assertions above are only as good as this predicate: a detector that accepted
    /// everything would make them pass on any code at all, and a detector that accepted nothing
    /// would trip the positive control rather than the real assertion — so neither failure is
    /// self-announcing from the theories alone. Each case below is a shape that was, at some
    /// point, actually accepted by a previous version of this predicate.
    /// </para>
    /// </summary>
    [Fact]
    public void ReadsLiveSetting_AcceptsOnlyTheRightPropertyOffTheRightReceiverTheRightWayRound()
    {
        const string source = """
            class Probe
            {
                void Hook()
                {
                    state.Live = !state.Settings.AnimationsEnabled;
                    other.Live = !state.Settings.AnimationsEnabled;
                    state.Constant = false;
                    state.WrongReceiver = !other.Settings.AnimationsEnabled;
                    state.WrongPolarity = state.Settings.AnimationsEnabled;
                    state.WrongProperty = !state.Settings.AutoHideScrollBars;
                }
            }
            """;

        var method = Parse(source)
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        int Filtered(string field) =>
            Writes(method, field, "Settings", "AnimationsEnabled", negated: true).DuringRender;

        Assert.Equal(1, Filtered("Live"));

        // The write TARGET must be state too, not merely a member with the right name. The
        // synthetic source assigns `other.Live` from a perfectly good live read; counting it
        // would mean a hook that seeds some other object passes as one that seeds its own state.
        // Symmetric with the receiver check on the read side, and it was missing here after that
        // one was added — fixing one side of a symmetry is a good way to believe both are done.
        Assert.Equal(1, Writes(method, "Live", "Settings", "AnimationsEnabled", negated: true)
            .DuringRender);

        // A constant seed reintroduces the original bug outright.
        Assert.Equal(0, Filtered("Constant"));

        // Right property, right polarity, wrong object — the receiver must be `state`, or the
        // predicate is satisfied by anything that merely has a member named `Settings`.
        Assert.Equal(0, Filtered("WrongReceiver"));

        // Right source, read the wrong way round. This is the mutant that passed 7/7 before this
        // case existed: it reports "no reduced motion" to precisely the users who asked for it,
        // which is the same user-visible failure as the bug this file guards.
        Assert.Equal(0, Filtered("WrongPolarity"));

        // Right object, wrong property.
        Assert.Equal(0, Filtered("WrongProperty"));

        // Unfiltered, every one of those IS counted — that is the exact hole the filter closes,
        // and asserting it here means weakening or removing the filter cannot pass silently.
        Assert.Equal(1, Writes(method, "Constant").DuringRender);
        Assert.Equal(1, Writes(method, "WrongPolarity").DuringRender);
    }

    /// <summary>
    /// <c>UseHighContrastScheme()</c> is documented to return <c>null</c> when high contrast is
    /// off, so the scheme write must be gated on <c>HighContrast</c> rather than read
    /// unconditionally — otherwise callers get a scheme name for a mode that is not active.
    /// The polarity check above cannot see this: the ungated read is the same property off the
    /// same receiver.
    /// </summary>
    [Fact]
    public void HighContrastScheme_IsGatedOnHighContrastBeingOn()
    {
        var method = Method("UseHighContrastState");

        var seeds = method.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => TargetsState(a, "HighContrastScheme"))
            .Where(a => !InsideUseEffect(a, method))
            .ToList();

        Assert.True(seeds.Count > 0,
            "no render-time assignment to state.HighContrastScheme found — extractor broken or "
            + "the field was renamed");

        Assert.All(seeds, a => Assert.True(
            a.Right is ConditionalExpressionSyntax c
                && c.Condition.DescendantNodesAndSelf()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Any(r => r.Name.Identifier.ValueText == "HighContrast"),
            "state.HighContrastScheme is assigned without gating on HighContrast, so "
            + "UseHighContrastScheme() reports a scheme name while high contrast is off, "
            + "contradicting its documented null-when-off contract"));
    }
}
