using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;

// `using static Factories` shadows the WinUI type names with factory methods, so the
// attached-property helpers and the source interface are reached through aliases.
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;
using IAnimatedVisualSource2 = Microsoft.UI.Xaml.Controls.IAnimatedVisualSource2;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Pins the mechanism the ReactorGallery AnimatedIcon page rests on (issue #983):
/// AnimatedIcon has no Play() — the animation *is* an <c>AnimatedIcon.State</c> write, and
/// the state name must resolve to a "{from}To{to}" marker segment on the visual source.
/// These fixtures fail if either half of that contract regresses.
/// </summary>
internal static class AnimatedIconStateFixtures
{
    /// <summary>The state names the gallery page writes.</summary>
    /// <remarks>
    /// This list and the sources checked below are pinned to the gallery page by
    /// <c>GallerySampleLintTests.AnimatedIcons_UseSourcesAndStatesTheSelfTestProves</c>: if the page
    /// starts using a source or a state that this fixture does not cover, that lint fails and names
    /// this file. Keep the two in step — the lint cannot read markers (it is headless) and this
    /// fixture cannot read the page (it is a separate app), so neither half is sufficient alone.
    /// </remarks>
    static readonly string[] GalleryStates = ["Normal", "PointerOver", "Pressed"];

    // ════════════════════════════════════════════════════════════════════════
    //  1. Marker oracle — the state strings the page writes must name real transitions
    //     on the built-in sources it uses, *and* those transitions must span a non-zero
    //     slice of the timeline. A present-but-zero-length segment plays nothing, which
    //     leaves the page a static decoration — exactly the issue #983 complaint.
    // ════════════════════════════════════════════════════════════════════════

    internal class BuiltInSourceMarkers(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // The three sources card 1 of the gallery page renders.
            CheckSource("Settings", new AnimatedSettingsVisualSource());
            CheckSource("Find", new AnimatedFindVisualSource());
            CheckSource("GlobalNav", new AnimatedGlobalNavigationButtonVisualSource());

            // Negative control. The checks above only mean something if a zero-length
            // segment is actually distinguishable from an animated one — otherwise
            // "TransitionsHaveDuration" could be measuring nothing and passing anyway.
            // ChevronDownSmall is a real built-in asset whose NormalToPointerOver and
            // PointerOverToNormal segments both sit at [0..0] (a chevron looks the same
            // hovered), so it is a free fixed point: if this reads as "animated", the
            // duration oracle is broken; if the gallery's three read as "static", the
            // gallery regressed. The pair fails in opposite directions. Both directions
            // are asserted below, so the comment and the check claim the same thing.
            CheckStaticPair("ChevronDownSmall", new AnimatedChevronDownSmallVisualSource());
            return Task.CompletedTask;
        }

        void CheckStaticPair(string label, IAnimatedVisualSource2 source)
        {
            var markers = source.Markers;
            H.Check($"AnimIconMarkers_{label}_HoverPairIsZeroLength", () =>
            {
                // Both directions, not just NormalToPointerOver: the check is named for the
                // hover *pair* and the comment above claims both sit at [0..0], so asserting
                // one direction would let the other animate while the control still passed.
                var offenders = new global::System.Collections.Generic.List<string>();
                foreach (var transition in new[] { "NormalToPointerOver", "PointerOverToNormal" })
                {
                    if (!markers.TryGetValue($"{transition}_Start", out var start)
                        || !markers.TryGetValue($"{transition}_End", out var end))
                    {
                        throw new global::System.InvalidOperationException(
                            $"expected {transition} markers to exist; present [{string.Join(", ", markers.Keys)}]");
                    }

                    // Require exact equality, not `end <= start`: an inverted pair
                    // (end < start) is a broken marker map, not a valid zero-length
                    // segment, and must not be able to satisfy the negative control.
                    if (end != start)
                    {
                        offenders.Add($"{transition}=[{start:0.####}..{end:0.####}]"
                            + (end < start ? " (inverted — broken marker map)" : string.Empty));
                    }
                }

                if (offenders.Count == 0) return true;
                throw new global::System.InvalidOperationException(
                    $"expected zero-length control segments, got [{string.Join(", ", offenders)}] — "
                    + "the duration oracle's negative control no longer holds");
            });
        }

        void CheckSource(string label, IAnimatedVisualSource2 source)
        {
            var markers = source.Markers;

            // Every ordered pair of gallery states needs a start+end marker pair.
            H.Check($"AnimIconMarkers_{label}_CoversGalleryStates", () =>
            {
                var missing = new global::System.Collections.Generic.List<string>();
                foreach (var from in GalleryStates)
                {
                    foreach (var to in GalleryStates.Where(to => to != from))
                    {
                        if (!markers.ContainsKey($"{from}To{to}_Start")) missing.Add($"{from}To{to}_Start");
                        if (!markers.ContainsKey($"{from}To{to}_End")) missing.Add($"{from}To{to}_End");
                    }
                }

                if (missing.Count == 0) return true;
                throw new global::System.InvalidOperationException(
                    $"missing [{string.Join(", ", missing)}]; present [{string.Join(", ", markers.Keys)}]");
            });

            // Presence alone does not distinguish an animation from a hard cut: a segment
            // whose Start and End sit on the same frame plays nothing. This is the actual
            // claim in issue #983 — that the page never demonstrates a *transition* — so
            // assert the segment spans a non-zero slice of the timeline.
            H.Check($"AnimIconMarkers_{label}_TransitionsHaveDuration", () =>
            {
                var degenerate = new global::System.Collections.Generic.List<string>();
                foreach (var from in GalleryStates)
                {
                    foreach (var to in GalleryStates.Where(to => to != from))
                    {
                        if (!markers.TryGetValue($"{from}To{to}_Start", out var start)
                            || !markers.TryGetValue($"{from}To{to}_End", out var end))
                        {
                            degenerate.Add($"{from}To{to}=<missing>");
                            continue;
                        }

                        // NaN-explicit rather than `end <= start`: a NaN marker makes every
                        // ordered comparison false, so `end <= start` would wave it through
                        // and leave the degenerate-segment check silently unable to fail.
                        if (double.IsNaN(start) || double.IsNaN(end) || end <= start)
                            degenerate.Add($"{from}To{to}=[{start:0.####}..{end:0.####}]");
                    }
                }

                if (degenerate.Count == 0) return true;
                throw new global::System.InvalidOperationException(
                    $"zero-length or unusable segments [{string.Join(", ", degenerate)}]");
            });

            // Differential: the checks above would also pass against a Markers map that
            // answers true to everything, so prove it discriminates.
            H.Check($"AnimIconMarkers_{label}_RejectsUnknownTransition",
                !markers.ContainsKey("NormalToSelfTestNonsense_Start"));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. .Set re-application on update — the page drives State through
    //     `.Set(icon => AnimatedIcon.SetState(icon, state))`, which only works because
    //     DescriptorHandler.Update calls ApplySetters on every update.
    // ════════════════════════════════════════════════════════════════════════

    internal class StateFollowsHookState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => new AnimatedSettingsVisualSource());
                var (idx, setIdx) = ctx.UseState(0);
                var state = GalleryStates[idx];
                return VStack(
                    AnimatedIcon(source).Size(32, 32)
                        .Set(icon => XamlAnimatedIcon.SetState(icon, state)),
                    TextBlock($"State:{state}"),
                    Button("NextState", () => setIdx((idx + 1) % GalleryStates.Length)));
            });

            await Harness.Render();
            var mounted = H.FindControl<XamlAnimatedIcon>(_ => true);
            var mountState = mounted is null ? null : XamlAnimatedIcon.GetState(mounted);
            H.Check("AnimIconState_MountWritesInitialState", mountState == "Normal");

            H.ClickButton("NextState");
            await Harness.Render();
            var updated = H.FindControl<XamlAnimatedIcon>(_ => true);
            var afterState = updated is null ? null : XamlAnimatedIcon.GetState(updated);
            H.Check("AnimIconState_UpdateReappliesSetter", afterState == "PointerOver");

            // Differential guard: a null/empty read on both sides would satisfy neither
            // equality above, but state the pair explicitly so a future "both are null"
            // regression cannot be mistaken for a naming change.
            H.Check("AnimIconState_StateActuallyMoved",
                mountState is not null && afterState is not null && afterState != mountState);

            // The control is updated in place, not remounted — a remount would restart the
            // composition visual and swallow the transition.
            H.Check("AnimIconState_ControlUpdatedInPlace",
                mounted is not null && ReferenceEquals(mounted, updated));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. Source must be hoisted into UseMemo. AnimatedIconElement.Source is a
    //     reference-compared OneWayConditional binding, so a `new …VisualSource()`
    //     built inside Render() is rewritten on every re-render and rebuilds the
    //     composition visual mid-transition.
    // ════════════════════════════════════════════════════════════════════════

    internal class MemoizedSourceSurvivesUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Arm A — source hoisted into UseMemo (what the gallery page does).
            var memoHost = H.CreateHost();
            memoHost.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => new AnimatedSettingsVisualSource());
                var (n, setN) = ctx.UseState(0);
                return VStack(
                    AnimatedIcon(source).Size(32, 32),
                    TextBlock($"Memo:{n}"),
                    Button("BumpMemo", () => setN(n + 1)));
            });

            await Harness.Render();
            var memoBefore = H.FindControl<XamlAnimatedIcon>(_ => true)?.Source;
            H.ClickButton("BumpMemo");
            await Harness.Render();
            var memoAfter = H.FindControl<XamlAnimatedIcon>(_ => true)?.Source;

            // Arm B — identical tree, but the source is constructed inline in Render().
            var inlineHost = H.CreateHost();
            inlineHost.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return VStack(
                    AnimatedIcon(new AnimatedSettingsVisualSource()).Size(32, 32),
                    TextBlock($"Inline:{n}"),
                    Button("BumpInline", () => setN(n + 1)));
            });

            await Harness.Render();
            var inlineBefore = H.FindControl<XamlAnimatedIcon>(_ => true)?.Source;
            H.ClickButton("BumpInline");
            await Harness.Render();
            var inlineAfter = H.FindControl<XamlAnimatedIcon>(_ => true)?.Source;

            H.Check("AnimIconSource_MemoizedStableAcrossUpdate",
                memoBefore is not null && ReferenceEquals(memoBefore, memoAfter));

            // The differential that makes the check above mean something: an inline source
            // really is rewritten, so "stable" is a property of UseMemo and not of the
            // projection handing back the same object no matter what.
            H.Check("AnimIconSource_InlineRewrittenOnUpdate",
                inlineBefore is not null && inlineAfter is not null
                    && !ReferenceEquals(inlineBefore, inlineAfter));
        }
    }
}
