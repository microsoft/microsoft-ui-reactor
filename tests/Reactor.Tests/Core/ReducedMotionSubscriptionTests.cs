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
    /// Event names bound with <c>+=</c> or <c>-=</c> under <paramref name="scope"/>, taken
    /// from the member name on the left of the operator.
    /// </summary>
    static string[] EventBindings(SyntaxNode scope, SyntaxKind op) =>
        scope.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(op))
            .Select(a => a.Left)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(m => m.Name.Identifier.ValueText)
            .ToArray();

    [Theory]
    [InlineData(HookFile, "UseReducedMotionState")]
    [InlineData(HostFile, "InitChartingState")]
    public void ReducedMotionListeners_SubscribeToTheEventThatActuallyFires(string file, string name)
    {
        var subscriptions = EventBindings(Method(file, name), SyntaxKind.AddAssignmentExpression);

        Assert.Contains(AnimationsEvent, subscriptions);
    }

    [Theory]
    [InlineData(HookFile, "UseReducedMotionState", "UseReducedMotionState")]
    [InlineData(HostFile, "InitChartingState", "Dispose")]
    public void EverySubscription_HasAMatchingRelease(string file, string subscribeIn, string releaseIn)
    {
        var subscribed = EventBindings(Method(file, subscribeIn), SyntaxKind.AddAssignmentExpression);
        var released = EventBindings(Method(file, releaseIn), SyntaxKind.SubtractAssignmentExpression);

        // Non-vacuity floor: the empty set is a subset of everything.
        Assert.Contains(AnimationsEvent, subscribed);

        // Subset, not equality: ReactorHost.Dispose also releases events subscribed at other
        // sites (ActualThemeChanged among them), so an extra release is correct there.
        Assert.Empty(subscribed.Except(released, global::System.StringComparer.Ordinal));
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
            var guarded = binding.Ancestors()
                .OfType<IfStatementSyntax>()
                .Any(i => i.Condition.ToString().Contains(
                    nameof(UiSettingsCapabilities.HasAnimationsEnabledChanged),
                    global::System.StringComparison.Ordinal));

            Assert.True(
                guarded,
                $"{file}: `{binding.Left} {binding.OperatorToken}` is not under a "
                + $"{nameof(UiSettingsCapabilities.HasAnimationsEnabledChanged)} check — it throws on "
                + "Windows builds before 19041, which TargetPlatformMinVersion still admits.");
        }
    }
}
