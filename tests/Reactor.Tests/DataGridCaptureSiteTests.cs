using System.Collections.Generic;
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
/// <para><b>Position is necessary but not sufficient.</b> A capture in exactly the right place
/// whose <i>result is discarded</i> restores #987 in its original, shipped form — deterministically
/// modifier-blind, not as a race — while every position assertion here stays green, because
/// <c>KeyChord.Capture</c> is still called in the right place and is merely unused. That is why
/// <see cref="DeferredDispatch_ReceivesTheCapturedChord"/> exists. Unlike the position invariant
/// this one <i>is</i> reachable behaviourally, but only by the E2E tier: the headless tiers enter
/// at the <c>HandleKeyDownForTests</c> seam and are handed a chord, so they never observe which
/// chord the routed handler actually passes. Measured, not assumed — rewriting the dispatch as
/// <c>HandleKeyDown(state, currentEl, KeyChord.Unmodified(e.Key))</c> compiles clean and passes
/// all 385 DataGrid unit tests when that assertion is absent.</para>
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

    /// <summary>
    /// The routed handler's capture site, the deferral it must precede, the deferred body, and every
    /// <c>HandleKeyDown</c> dispatch inside that body (which must receive the captured chord).
    /// </summary>
    private readonly record struct CaptureSite(
        InvocationExpressionSyntax Capture,
        InvocationExpressionSyntax Deferral,
        AnonymousFunctionExpressionSyntax DeferredBody,
        AnonymousFunctionExpressionSyntax RoutedHandler,
        IReadOnlyList<InvocationExpressionSyntax> Dispatches);

    /// <summary>
    /// Resolves the invoked member name for the call shapes that appear at these sites:
    /// <c>KeyChord.Capture(..)</c> (member access), <c>dq?.TryEnqueue(..)</c> (member binding, via
    /// the null-conditional) and <c>HandleKeyDown(..)</c> (a bare identifier — it is a static
    /// method called unqualified from inside its own type).
    /// <para>The <see cref="GenericNameSyntax"/> arm is breadth, not a fourth anchor shape. The
    /// scanned file does contain generic-form invocations (<c>UseRef&lt;..&gt;(..)</c>), but neither
    /// name this helper is asked for is ever spelled with explicit type arguments, so the arm is
    /// unreachable for the questions actually posed. Deleting it leaves both tests below passing —
    /// measured, not assumed. It is kept so the switch stays total over the shapes that genuinely
    /// occur in this file, and it is recorded as non-load-bearing so a later reader does not
    /// mistake it for part of the guard.</para>
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

        // Every dispatch inside the deferred body has to receive the captured chord, so collect them
        // all rather than assuming there is one. Non-emptiness is guaranteed by the predicate that
        // selected this body, and is pinned anyway because DeferredDispatch_ReceivesTheCapturedChord
        // quantifies over this list and would pass vacuously against an empty one.
        var dispatches = deferrals[0].Body!.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => InvokedName(call) == "HandleKeyDown")
            .ToList();
        Assert.True(
            dispatches.Count >= 1,
            $"Found no HandleKeyDown dispatch inside the deferred body in {HandlerFile}, which "
            + "contradicts the predicate that selected that body. Resolve the inconsistency before "
            + "trusting this file.");

        return new CaptureSite(captures[0], deferral, deferrals[0].Body!, routedHandler!, dispatches);
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

    /// <summary>
    /// The capture must not merely sit in the right place — its result must be what reaches the
    /// deferred dispatch.
    ///
    /// <para>This is a different defect from the one the two tests above catch, and it is the more
    /// dangerous of the pair. Sinking the capture into the lambda reintroduces #987 as a
    /// <i>race</i>, which at least fails sometimes. Leaving the capture correctly placed but
    /// rebuilding the chord at the dispatch — <c>KeyChord.Unmodified(e.Key)</c> — reintroduces #987
    /// exactly as originally shipped: modifier-blind on every keystroke, deterministically, so
    /// Shift+Tab moves forward every time.</para>
    ///
    /// <para>It is a realistic merge resolution rather than a contrived one. After this fix
    /// <c>HandleKeyDown</c> takes a <c>KeyChord</c>, so any conflict resolution that loses the
    /// <c>chord</c> local produces a compiler error <i>at the dispatch</i>, and the nearest
    /// compiling expression is <c>KeyChord.Unmodified(e.Key)</c> — which is already present twenty
    /// lines above, in the deliberately modifier-blind <c>ShouldHandleKey</c> gate. The capture
    /// itself keeps compiling and stays used (the cell-edit <c>SuppressNextLostFocusCommit</c> guard
    /// reads <c>chord.Key</c>), so there is not even an unused-variable warning to prompt a second
    /// look.</para>
    ///
    /// <para>Measured, not assumed: applying that mutation leaves all 385 DataGrid unit tests green,
    /// including both position tests above.</para>
    ///
    /// <para><b>This check is deliberately conservative, and that has a cost worth naming.</b> It
    /// accepts the capture expression itself or an identifier matching the capture's declarator —
    /// nothing else. An intermediate alias (<c>var forDispatch = chord;</c>) therefore fails it even
    /// though the dataflow is correct, and the failure message would be wrong about the cause. If
    /// that ever happens the legitimate adjustment is to teach this check the alias; the
    /// illegitimate one is to relax it until a dispatch that <i>rebuilds</i> the chord passes. The
    /// two look similar at the diff and are opposites: the first preserves the invariant that the
    /// value reaching the dispatch originated at the capture, the second discards it. Syntactic
    /// matching is chosen over a semantic-model walk because this file parses one source text with
    /// no compilation, and the alias case has never occurred here.</para>
    /// </summary>
    [Fact]
    public void DeferredDispatch_ReceivesTheCapturedChord()
    {
        var site = ReadCaptureSite();

        // Derived from the declaration rather than hard-coded, so renaming the local is invisible
        // to this check and only genuinely discarding the chord fails it.
        var capturedName = site.Capture.Ancestors().OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault()?.Identifier.ValueText;

        bool CarriesChord(InvocationExpressionSyntax dispatch) =>
            dispatch.ArgumentList.Arguments.Any(argument =>
                argument.Expression == site.Capture
                || (capturedName is not null
                    && argument.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == capturedName));

        var discarding = site.Dispatches.Where(dispatch => !CarriesChord(dispatch)).ToList();

        Assert.True(
            discarding.Count == 0,
            $"{discarding.Count} of {site.Dispatches.Count} HandleKeyDown dispatch(es) inside the "
            + "deferred body do not receive the captured chord, so the capture's result is "
            + "discarded. The position assertions in this file still pass — KeyChord.Capture is "
            + "still called, in the right place, and is simply unused for dispatch — but the grid "
            + "is modifier-blind again on every keystroke, which is #987 exactly as originally "
            + "shipped (deterministic, not a race: Shift+Tab moves forward every time). Pass the "
            + "captured chord into the deferred work instead of rebuilding one at the dispatch.");
    }
}
