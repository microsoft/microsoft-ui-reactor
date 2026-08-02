using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Tests.Tooling;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Pins <em>which</em> WinRT event the reduced-motion listeners subscribe to.
///
/// <para>
/// <c>UISettings.ColorValuesChanged</c> does not fire when the "Animation effects" toggle
/// changes, but <c>UISettings.AnimationsEnabled</c> still <em>reads back</em> the new value
/// — so code subscribed to the wrong event looks correct in every static reading and simply
/// never updates. Both listeners shipped that way: the value was right on first render and
/// then stayed frozen for the life of the process.
/// </para>
///
/// <para>
/// Measured on Windows 11 with a live subscription to both events, toggling
/// Settings → Accessibility → Visual effects → Animation effects:
/// <c>AnimationsEnabledChanged</c> fired on both the on→off and the off→on transition, and
/// <c>ColorValuesChanged</c> fired on neither. Toggling the system theme in the same session
/// fired <c>ColorValuesChanged</c>, which is the positive control proving the subscription
/// machinery was live and only the event choice was wrong.
/// </para>
///
/// <para>
/// <b>Why this is structural rather than behavioural.</b> The notification is raised by the
/// Settings app's own write path, not by the underlying value changing: a synthetic
/// <c>SystemParametersInfoW(SPI_SETCLIENTAREAANIMATION, …, SPIF_SENDCHANGE)</c> moves the
/// value that <c>AnimationsEnabled</c> reports and raises <b>no</b> event at all. So a
/// fixture that flipped the setting itself and waited for a callback would time out against
/// correct code — a test that can only fail — while one that asserted the polled value
/// instead would pass against the frozen-value bug it is meant to catch. No test tier can
/// synthesize the trigger, so the mechanism is asserted here and the transition itself was
/// verified by hand through the real Settings UI.
/// </para>
/// </summary>
public sealed class ReducedMotionSubscriptionTests
{
    const string AnimationsEvent = "AnimationsEnabledChanged";
    const string ColorEvent = "ColorValuesChanged";

    const string HookFile = "Core/RenderContext.cs";
    const string HostFile = "Hosting/ReactorHost.cs";

    static SyntaxNode Source(string file)
    {
        var path = Path.Join(GallerySources.RepoRoot(), "src", "Reactor", file);
        Assert.True(File.Exists(path), $"{file} not found at {path}");

        return CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
    }

    static MethodDeclarationSyntax Method(string file, string name)
    {
        var method = Source(file).DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(m => m.Identifier.ValueText == name);

        // Without this, a rename turns every assertion below into "no bindings found",
        // which several of them would accept as a pass.
        Assert.True(method is not null, $"{name} not found in {file} — was it renamed?");
        return method!;
    }

    /// <summary>
    /// Event/handler pairs bound with <c>+=</c> or <c>-=</c> under <paramref name="scope"/>,
    /// taken from the member name on the left of the operator and the handler on the right.
    /// </summary>
    static (string Event, string Handler)[] EventHandlerBindings(SyntaxNode scope, SyntaxKind op) =>
        scope.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(op))
            .Where(a => a.Left is MemberAccessExpressionSyntax)
            .Select(a => (
                Event: ((MemberAccessExpressionSyntax)a.Left).Name.Identifier.ValueText,
                Handler: a.Right is MemberAccessExpressionSyntax m
                    ? m.Name.Identifier.ValueText
                    : a.Right.ToString()))
            .ToArray();

    static string[] EventBindings(SyntaxNode scope, SyntaxKind op) =>
        EventHandlerBindings(scope, op).Select(b => b.Event).ToArray();

    [Theory]
    [InlineData(HookFile, "UseReducedMotionState", "OnChanged")]
    [InlineData(HostFile, "InitChartingState", "OnAnimationsEnabledChanged")]
    public void ReducedMotionListeners_SubscribeToTheEventThatActuallyFires(
        string file, string name, string handler)
    {
        var subscriptions = EventHandlerBindings(
            Method(file, name), SyntaxKind.AddAssignmentExpression);

        // The pair, not just the event: binding the right event to the wrong handler is
        // the same defect wearing a passing name.
        Assert.Contains((AnimationsEvent, handler), subscriptions);
    }

    [Theory]
    [InlineData(HookFile, "UseReducedMotionState", "UseReducedMotionState")]
    [InlineData(HostFile, "InitChartingState", "Dispose")]
    public void EverySubscription_HasAMatchingRelease(string file, string subscribeIn, string releaseIn)
    {
        var subscribed = EventHandlerBindings(Method(file, subscribeIn), SyntaxKind.AddAssignmentExpression);
        var released = EventHandlerBindings(Method(file, releaseIn), SyntaxKind.SubtractAssignmentExpression);

        // Non-vacuity floor: the empty set is a subset of everything.
        Assert.Contains(AnimationsEvent, subscribed.Select(b => b.Event));

        // Pairs, so releasing a *different* delegate than the one subscribed — which leaks
        // silently, because -= on a non-matching instance is a no-op — fails here.
        // Subset, not equality: ReactorHost.Dispose also releases events subscribed at other
        // sites (ActualThemeChanged among them), so an extra release is correct there.
        Assert.Empty(subscribed.Except(released));
    }

    /// <summary>
    /// The same extractor must report the animation event as <em>absent</em> from the
    /// high-contrast hook, which is correctly served by <c>ColorValuesChanged</c>. Without a
    /// case it answers "no" to, an extractor that matched every member access would satisfy
    /// every other assertion in this class.
    /// </summary>
    [Fact]
    public void TheExtractor_DistinguishesTheTwoHooksRatherThanMatchingEverything()
    {
        var reducedMotion = EventBindings(
            Method(HookFile, "UseReducedMotionState"), SyntaxKind.AddAssignmentExpression);
        var highContrast = EventBindings(
            Method(HookFile, "UseHighContrastState"), SyntaxKind.AddAssignmentExpression);

        Assert.Contains(AnimationsEvent, reducedMotion);
        Assert.DoesNotContain(AnimationsEvent, highContrast);
        Assert.Contains(ColorEvent, highContrast);
    }

    /// <summary>
    /// True when <paramref name="binding"/> is reached only if the capability probe said
    /// <em>yes</em>. Checks polarity and branch, not just mention: <c>if (!Probe)</c> and
    /// <c>if (Probe) { } else { bind; }</c> both name the probe while running exactly when
    /// it is absent.
    /// </summary>
    static bool GuardedByProbe(SyntaxNode binding) =>
        binding.Ancestors().OfType<IfStatementSyntax>()
            // The else-branch runs when the probe said no, so it does not guard.
            .Where(branch => branch.Else is not { } negative || !negative.Span.Contains(binding.Span))
            .Any(branch => branch.Condition.DescendantNodesAndSelf()
                .OfType<SimpleNameSyntax>()
                .Where(n => n.Identifier.ValueText
                    == nameof(UiSettingsCapabilities.HasAnimationsEnabledChanged))
                .Any(n => !n.Ancestors()
                    .TakeWhile(a => a != branch)
                    .Any(a => a.IsKind(SyntaxKind.LogicalNotExpression))));

    /// <summary>
    /// <c>AnimationsEnabledChanged</c> arrived in Windows 10 2004 (19041) and Reactor declares
    /// <c>TargetPlatformMinVersion 10.0.17763.0</c>, where subscribing throws. Every binding
    /// must sit under the capability probe. CA1416 covers the straight-line sites, but its
    /// flow analysis does not follow a captured guard into a lambda, so an unsubscribe inside
    /// a cleanup closure compiles unguarded. Scoped to the whole file rather than to a method
    /// so a binding added at a third site is caught too.
    /// </summary>
    [Theory]
    [InlineData(HookFile)]
    [InlineData(HostFile)]
    public void EveryAnimationsEventBinding_SitsUnderTheCapabilityProbe(string file)
    {
        var bindings = Source(file).DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression)
                     || a.IsKind(SyntaxKind.SubtractAssignmentExpression))
            .Where(a => a.Left is MemberAccessExpressionSyntax m
                     && m.Name.Identifier.ValueText == AnimationsEvent)
            .ToArray();

        Assert.NotEmpty(bindings);

        foreach (var binding in bindings)
        {
            Assert.True(
                GuardedByProbe(binding),
                $"{file}: `{binding.Left} {binding.OperatorToken}` is not under a "
                + $"{nameof(UiSettingsCapabilities.HasAnimationsEnabledChanged)} check — it throws on "
                + "Windows builds before 19041, which TargetPlatformMinVersion still admits.");
        }
    }

    static AssignmentExpressionSyntax ParseBinding(string statement) =>
        CSharpSyntaxTree.ParseText($"class C {{ void M() {{ {statement} }} }}")
            .GetRoot()
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Single(a => a.IsKind(SyntaxKind.AddAssignmentExpression));

    /// <summary>
    /// The polarity check above is the whole value of that test, so it needs a case it
    /// answers "no" to. A mention-only check (<c>Condition.ToString().Contains(...)</c>)
    /// passes all four of these.
    /// </summary>
    [Theory]
    [InlineData("if (UiSettingsCapabilities.HasAnimationsEnabledChanged) s.AnimationsEnabledChanged += H;", true)]
    [InlineData("if (!UiSettingsCapabilities.HasAnimationsEnabledChanged) s.AnimationsEnabledChanged += H;", false)]
    [InlineData("if (UiSettingsCapabilities.HasAnimationsEnabledChanged) { } else { s.AnimationsEnabledChanged += H; }", false)]
    [InlineData("if (someUnrelatedFlag) s.AnimationsEnabledChanged += H;", false)]
    public void TheProbeGuardCheck_RejectsConditionsThatMerelyMentionTheProbe(
        string statement, bool expected)
    {
        Assert.Equal(expected, GuardedByProbe(ParseBinding(statement)));
    }

    /// <summary>
    /// <c>D3Charts</c>'s accessibility flags are <c>[ThreadStatic]</c> and these handlers run
    /// on the WinRT notification thread, so a push from here writes a copy the render thread
    /// never reads — silently, since the write itself succeeds. <c>Render()</c> already pushes
    /// on the UI thread every frame, so <c>RequestRender</c> is the whole propagation path.
    /// </summary>
    [Theory]
    [InlineData("OnAnimationsEnabledChanged")]
    [InlineData("OnColorValuesChanged")]
    public void UiSettingsHandlers_LeaveTheChartingPushToTheRenderThread(string handler)
    {
        var body = Method(HostFile, handler);

        var calls = body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(i => i.Expression.ToString())
            .ToArray();

        // Non-vacuity floor: an empty call list would satisfy the DoesNotContain below, and
        // RequestRender is the propagation these handlers exist to trigger.
        Assert.Contains("RequestRender", calls);

        Assert.DoesNotContain("PushChartingState", calls);
    }

    /// <summary>
    /// Every WinRT settings object these hooks construct must be constructed inside a
    /// <c>try</c>, so an unavailable projection degrades to the hook's default instead of
    /// failing the frame.
    ///
    /// <para>
    /// The seed runs during render, which widened the reach of these constructors: before
    /// seeding existed they ran only from <c>UseEffect</c>, and effects flush only under a
    /// live reconciler. <c>FlushEffectsTraced</c> wraps the flush in <c>try</c>/<c>finally</c>,
    /// not <c>try</c>/<c>catch</c>, so neither path has a backstop above it.
    /// </para>
    ///
    /// <para>
    /// Asserted structurally because the failure cannot be produced: measured in this test
    /// host, <c>new UISettings()</c>, <c>new AccessibilitySettings()</c> and the properties
    /// read off both succeed. A runtime test would therefore pass with the guards deleted.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("UseReducedMotionState", 2)]
    [InlineData("UseHighContrastState", 3)]
    public void SettingsConstruction_IsGuarded(string hook, int expected)
    {
        var method = Method(HookFile, hook);

        var constructions = method.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(c => c.Type.ToString().Contains("Windows.UI.ViewManagement"))
            .ToArray();

        // Non-vacuity floor: zero constructions satisfies an "all are guarded" assertion.
        // Reduced-motion builds UISettings in the seed and in the effect; high contrast also
        // needs AccessibilitySettings in both, plus UISettings for the event source.
        Assert.Equal(expected, constructions.Length);

        foreach (var construction in constructions)
        {
            // Ancestors().OfType<TryStatementSyntax>() would also accept a construction sitting
            // in the catch or finally clause, which is not guarded by that try.
            var guarded = construction.Ancestors()
                .OfType<TryStatementSyntax>()
                .Any(t => t.Block.Span.Contains(construction.Span));

            Assert.True(
                guarded,
                $"{hook}: `new {construction.Type}` at line "
                    + $"{construction.GetLocation().GetLineSpan().StartLinePosition.Line + 1} is not inside a try "
                    + "block — an unavailable projection would fail the render instead of "
                    + "degrading to the hook's default.");
        }
    }
}
