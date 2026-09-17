using System;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — a call split across lines must be attributed exactly where
/// <c>[CallerLineNumber]</c> would attribute it.
///
/// <para>Roslyn derives caller-info from the argument list's OPENING PAREN, not from the
/// start of the invocation, so the two disagree whenever a call is wrapped. Reading the
/// whole invocation's span reported the receiver's line — for <c>Factories</c> newline
/// <c>.TextBlock("x")</c> that is the <c>Factories</c> line, which is not where a
/// developer would say the call is, and not where devtools should navigate.</para>
///
/// <para>Each case anchors on a live <c>[CallerLineNumber]</c> probe and then asserts a
/// small explicit offset from it, so the expectation is derived from the compiler and
/// stays correct if this file is edited above the test. The offsets are what give the
/// tests their teeth: in every wrapped case the receiver line and the paren line are
/// DIFFERENT, so attribution that used either the invocation start or the invoked name
/// lands on a different number and the assertion fails.</para>
/// <para>In the "SourceMap" collection and toggling <see cref="ReactorSourceMap.Enabled"/>
/// in the constructor, like the other interception suites: the generated interceptor
/// returns the element unstamped when the flag is off, and that flag is process-global
/// mutable state. Without this the class races whichever other suite happens to be
/// running, and a case fails on <c>CallSite</c> being null for reasons unrelated to
/// attribution.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class MultilineCallSiteTests : IDisposable
{
    public MultilineCallSiteTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    /// <summary>Reports the line the compiler attributes to a call at this position.</summary>
    private static int ProbeLine([CallerLineNumber] int line = 0) => line;

    [Fact]
    public void WrappedQualifiedCall_IsAttributedToTheOpenParenLine()
    {
        // Receiver on anchor+1, invoked name + paren on anchor+2. Attribution from the
        // invocation's start would report anchor+1.
        var anchor = ProbeLine();
        var element = Factories
            .TextBlock("wrapped");

        Assert.NotNull(element.CallSite);
        Assert.Equal(anchor + 2, element.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void ParenOnItsOwnLine_FollowsTheParenNotTheName()
    {
        // The distinguishing case: invoked NAME on anchor+1, opening PAREN on anchor+2.
        // Caller-info follows the paren, so name-based attribution would report anchor+1
        // here even though it is right for the common shape above.
        var anchor = ProbeLine();
        var element = Factories.TextBlock
            ("paren-wrapped");

        Assert.NotNull(element.CallSite);
        Assert.Equal(anchor + 2, element.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void SingleLineCall_StillMatches()
    {
        // Positive control: pins that the wrapped cases are not passing because
        // attribution collapsed to something trivially equal for every shape.
        var anchor = ProbeLine();
        var element = Factories.TextBlock("single");

        Assert.NotNull(element.CallSite);
        Assert.Equal(anchor + 1, element.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void WrappedParamsFactory_IsAttributedToTheOpenParenLine()
    {
        // The params family is the whole point of this route, and it is also the family
        // most likely to be wrapped in real code, since containers hold their children.
        var anchor = ProbeLine();
        var element = Factories
            .VStack(
                TextBlock("a"),
                TextBlock("b"));

        Assert.NotNull(element.CallSite);
        Assert.Equal(anchor + 2, element.CallSite!.Value.LineNumber);
    }

    [Fact]
    public void UnqualifiedWrappedCall_IsAttributedToTheOpenParenLine()
    {
        // The DSL is normally used unqualified via `using static Factories`, so cover
        // that shape too: name on anchor+1, paren on anchor+2.
        var anchor = ProbeLine();
        var element = VStack
            (
                TextBlock("a"));

        Assert.NotNull(element.CallSite);
        Assert.Equal(anchor + 2, element.CallSite!.Value.LineNumber);
    }
}
