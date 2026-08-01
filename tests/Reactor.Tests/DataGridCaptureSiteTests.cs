using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #987's load-bearing invariant, and the only one no behavioural tier can reach:
/// <b><c>KeyChord.Capture</c> must run synchronously inside the routed KeyDown handler, never
/// inside the <c>DispatcherQueue.TryEnqueue</c> lambda that defers the grid's key handling.</b>
///
/// <para><b>Why a structural test rather than a behavioural one.</b> The bug is a read on the wrong
/// side of a frame boundary: the deferred lambda runs one or more frames later, so a capture moved
/// into it re-samples the keyboard after the user may have released Shift. Every existing tier is
/// blind to that. The unit and selftest tiers both enter at
/// <c>HandleKeyDownForTests(state, el, chord)</c> and are handed an already-built chord, so they
/// never execute the capture site at all. The E2E tier does execute it, but only fails when the
/// race is actually lost during that run — a test that detects a race probabilistically cannot fail
/// reliably, so a green result from it carries almost no information. That leaves the invariant
/// defended by comments alone, which is what this file fixes.</para>
///
/// <para><b>Why the whole check is phrased around the lambda and not around line order.</b> A
/// textual "capture appears above TryEnqueue" probe answers a slightly different question than the
/// one that matters, and the difference is exactly where this bug would re-enter: what breaks the
/// fix is the capture moving <i>inside the deferred body</i>, so that is what is asserted here —
/// directly, as a syntax-tree containment relationship.</para>
///
/// <para><b>The comment trap this shape is immune to.</b> <c>DataGridComponent.cs</c> contains two
/// textual occurrences of <c>KeyChord.Capture</c>: the real call site, and a prose mention inside
/// the <c>ShouldHandleKey</c> remarks explaining why that gate must stay modifier-blind. A
/// grep/regex probe matches both and can "confirm" the ordering off a comment; Roslyn parses
/// comments as trivia, so <see cref="InvocationExpressionSyntax"/> only ever sees the real one.
/// That is not hypothetical — it is the failure mode a hand-rolled textual version of this check
/// hit while #987 and #976 were being merged.</para>
///
/// <para><b>Anti-vacuity.</b> The invariant below is a negative ("the capture is NOT in the
/// deferred body"), and every negative assertion passes for free if the scan finds nothing. Both
/// anchors are therefore pinned by exact counts in <see cref="ReadCaptureSite"/> before any
/// assertion runs: if the capture site, the deferral, or the deferred dispatch cannot be located,
/// this file fails loudly instead of reporting a green that only means "I looked in the wrong
/// place."</para>
/// </summary>
public class DataGridCaptureSiteTests
{
    private const string HandlerFile = "DataGridComponent.cs";

    /// <summary>The routed handler's capture site, the deferral it must precede, and the deferred body.</summary>
    private readonly record struct CaptureSite(
        InvocationExpressionSyntax Capture,
        InvocationExpressionSyntax Deferral,
        AnonymousFunctionExpressionSyntax DeferredBody,
        AnonymousFunctionExpressionSyntax RoutedHandler);

    /// <summary>
    /// Resolves the invoked member name for the call shapes that appear at these sites:
    /// <c>KeyChord.Capture(..)</c> (member access), <c>dq?.TryEnqueue(..)</c> (member binding, via
    /// the null-conditional) and <c>HandleKeyDown(..)</c> (a bare identifier — it is a static
    /// method called unqualified from inside its own type).
    /// </summary>
    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        _ => null,
    };

    private static bool IsKeyChordCapture(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member
        && member.Name.Identifier.Text == "Capture"
        && member.Expression is IdentifierNameSyntax owner
        && owner.Identifier.Text == "KeyChord";

    /// <summary>
    /// Locates the three anchors, failing loudly if any is missing or ambiguous. Every count here is
    /// a precondition for the assertions in the tests below being able to fail at all.
    /// </summary>
    private static CaptureSite ReadCaptureSite()
    {
        var repoRoot = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(repoRoot);

        var path = Path.Join(repoRoot!, "src", "Reactor.Advanced", "Controls", "DataGrid", HandlerFile);
        Assert.True(File.Exists(path), $"{HandlerFile} not found at {path}");

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        // The grid defers through TryEnqueue in ~10 places, so the deferral cannot be identified by
        // name alone. Pick it out by what it does instead: it is the one whose deferred body
        // dispatches HandleKeyDown. Matching is on the exact identifier, so the HandleKeyDownForTests
        // seam next door is not mistaken for it.
        var deferrals = invocations
            .Where(invocation => InvokedName(invocation) == "TryEnqueue")
            .Select(invocation => (
                Deferral: invocation,
                Body: invocation.ArgumentList.Arguments.Count == 1
                    ? invocation.ArgumentList.Arguments[0].Expression as AnonymousFunctionExpressionSyntax
                    : null))
            .Where(candidate => candidate.Body is not null
                && candidate.Body.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(call => InvokedName(call) == "HandleKeyDown"))
            .ToList();

        Assert.True(
            deferrals.Count == 1,
            $"Expected exactly one TryEnqueue in {HandlerFile} whose deferred body dispatches "
            + $"HandleKeyDown; found {deferrals.Count}. Either the grid's key dispatch was "
            + "restructured or this test is looking in the wrong place — resolve that before "
            + "trusting any result from this file, because the #987 assertions below are negatives "
            + "and would pass vacuously against a body this test never found.");

        // The keyboard-reading capture must exist exactly once. Zero would make every assertion
        // below pass for free; more than one means a second, unaudited site now reads the keyboard.
        var captures = invocations.Where(IsKeyChordCapture).ToList();
        Assert.True(
            captures.Count == 1,
            $"Expected exactly one KeyChord.Capture(...) call site in {HandlerFile}; found "
            + $"{captures.Count}. (Prose mentions of KeyChord.Capture do not count here — this "
            + "matches invocation syntax, not text — so a count of 0 means the capture was removed "
            + "or renamed, not that a comment moved.)");

        var deferral = deferrals[0].Deferral;
        var routedHandler = deferral.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        Assert.True(
            routedHandler is not null,
            "The HandleKeyDown deferral is no longer inside a lambda, so the routed KeyDown handler "
            + "could not be identified. #987 depends on the capture being synchronous with that "
            + "handler; re-establish the anchor before assuming this is benign.");

        return new CaptureSite(captures[0], deferral, deferrals[0].Body!, routedHandler!);
    }

    /// <summary>
    /// The invariant itself. Moving <c>KeyChord.Capture(e.Key)</c> inside the <c>TryEnqueue</c>
    /// lambda compiles cleanly, produces no conflict marker, and reintroduces #987 as an
    /// intermittent race that ships green — it is the natural-looking resolution whenever this
    /// handler is refactored or merged, which is precisely why it needs a tripwire.
    /// </summary>
    [Fact]
    public void KeyChordCapture_IsNotInsideTheDeferredDispatchLambda()
    {
        var site = ReadCaptureSite();

        Assert.False(
            site.Capture.Ancestors().Any(ancestor => ancestor == site.DeferredBody),
            "KeyChord.Capture(...) is inside the DispatcherQueue.TryEnqueue lambda. That lambda runs "
            + "one or more frames after the key was pressed, so the capture re-samples the keyboard "
            + "at that later time and races the user releasing Shift — reintroducing #987 as an "
            + "intermittent wrong-direction Shift+Tab. Capture synchronously in the routed handler "
            + "and pass the resulting chord into the deferred work.");
    }

    /// <summary>
    /// The positive half. Its two assertions catch different drifts, and it is worth being exact
    /// about which, because one of them is weaker than it looks.
    ///
    /// <para>The containment check catches the capture being hoisted <i>out</i> of the routed
    /// handler altogether — into some other method that no longer runs synchronously with the key
    /// press. It does <b>not</b> catch the capture sinking into the deferred lambda: that lambda is
    /// nested inside the routed handler, so anything inside it is also inside the handler and this
    /// assertion stays green. That is established by mutation rather than by reading — moving the
    /// capture into the lambda leaves this check passing, and is caught instead by the span check
    /// below and by <see cref="KeyChordCapture_IsNotInsideTheDeferredDispatchLambda"/>.</para>
    ///
    /// <para>The span check is what has teeth for that nested case: the deferred lambda is an
    /// argument of the deferral, so a capture inside it necessarily starts after the deferral does.
    /// Keeping both is deliberate — neither subsumes the other, and the pair reports which of the
    /// two drifts actually happened.</para>
    /// </summary>
    [Fact]
    public void KeyChordCapture_RunsSynchronouslyInTheRoutedHandlerThatDefers()
    {
        var site = ReadCaptureSite();

        Assert.True(
            site.Capture.Ancestors().Any(ancestor => ancestor == site.RoutedHandler),
            "KeyChord.Capture(...) is no longer inside the routed KeyDown handler that performs the "
            + "deferral. The capture is only correct while it is synchronous with the key press "
            + "(#987): KeyRoutedEventArgs carries no modifier state, so the modifiers come from the "
            + "live keyboard and are only guaranteed to agree with the key at that instant.");

        Assert.True(
            site.Capture.SpanStart < site.Deferral.SpanStart,
            "KeyChord.Capture(...) no longer precedes the TryEnqueue deferral it feeds. This "
            + "corroborates the containment checks: the chord must be fully built before anything "
            + "defers, because that is the last moment the key and its modifiers are known to "
            + "agree (#987).");
    }
}
