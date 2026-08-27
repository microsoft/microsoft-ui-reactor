using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 010 — proves the source-map READ path against a real WinUI control:
/// <c>UIElement</c> → <c>ReactorAttached.StateProperty</c> →
/// <c>ReactorState.Element</c> → <c>Element.CallSite</c>.
///
/// <para>The element is stamped by hand here rather than by the interceptor
/// generator. That is deliberate: this fixture's job is the reconciler half of
/// the chain (does the <see cref="ReactorSourceMap.Enabled"/> flag actually make
/// <c>NeedsTag</c> tag a callback-free display leaf, and does the tag survive to
/// be read back), which is the only half that needs a live control. The
/// generator half is measured headlessly in Reactor.SourceMap.Spike, where the
/// call-site line can be checked against an independent oracle.</para>
/// </summary>
internal static class SourceMapReadPathTests
{
    private static readonly SourceLocation Marker = new(@"C:\fixture\SourceMapReadPath.cs", 4242);

    /// <summary>
    /// Flag ON: a bare TextBlock — no callbacks, no key, no extras, no reference
    /// modifiers, i.e. precisely the leaf PR #468 stopped tagging — must become
    /// readable again, and must hand back the exact location that was stamped.
    /// </summary>
    internal class LeafIsReadableWhenEnabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("mapped") with { CallSite = Marker }));

                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "mapped");
                H.Check("SourceMapReadPath_LeafMounted", target is not null);
                if (target is null) return;

                var resolved = ReactorSourceMap.GetSource(target);

                // Non-null alone would be satisfied by any stamp reaching any
                // element, so assert the exact round-tripped values.
                H.Check("SourceMapReadPath_Resolved", resolved is not null);
                H.Check("SourceMapReadPath_LineRoundTrips",
                    resolved?.LineNumber == 4242);
                H.Check("SourceMapReadPath_PathRoundTrips",
                    resolved?.FilePath == @"C:\fixture\SourceMapReadPath.cs");
                H.Check("SourceMapReadPath_ShortForm",
                    resolved?.ToShortString() == "SourceMapReadPath.cs:4242");
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// Flag OFF: the same leaf, carrying the same stamp, must NOT be tagged —
    /// this is the negative control for the fixture above. If it also resolved,
    /// the "enabled" fixture would be proving nothing about the flag (the leaf
    /// would have been tagged either way) and the PR #468 allocation gate would
    /// have been silently defeated.
    /// </summary>
    internal class LeafIsNotTaggedWhenDisabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("unmapped") with { CallSite = Marker }));

                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "unmapped");
                H.Check("SourceMapReadPath_DisabledLeafMounted", target is not null);
                if (target is null) return;

                H.Check("SourceMapReadPath_DisabledLeafNotTagged",
                    ReactorSourceMap.GetSource(target) is null);
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// The flag must not disturb the elements that were ALREADY tagged. A
    /// callback-bearing Button is tagged with the flag off, so the source stamp
    /// has to be readable there too — confirming the new arm is purely additive
    /// to <c>NeedsTag</c> rather than replacing the existing ones.
    /// </summary>
    internal class AlreadyTaggedControlStillCarriesSource(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    Button("tagged", () => { }) with { CallSite = Marker }));

                await Harness.Render();

                var target = H.FindControl<Button>(b => b.Content as string == "tagged");
                H.Check("SourceMapReadPath_ButtonMounted", target is not null);
                if (target is null) return;

                H.Check("SourceMapReadPath_ButtonCarriesSourceWithFlagOff",
                    ReactorSourceMap.GetSource(target)?.LineNumber == 4242);
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }
}
