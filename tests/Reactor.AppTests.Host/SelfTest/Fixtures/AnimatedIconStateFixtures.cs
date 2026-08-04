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

    /// <summary>
    /// The one definition of "this marker pair plays something", shared by the duration oracle
    /// and its negative control. Marker positions are exact constants baked into the visual
    /// source rather than computed, so an exact comparison would in fact be safe here — but two
    /// separately-written inline comparisons can drift into disagreeing about a value, which is
    /// the failure the negative control exists to rule out. Deriving both from one band removes
    /// that possibility: a pair is <see cref="Animated"/> above the band, <see cref="ZeroLength"/>
    /// inside it, and <em>neither</em> below it or when unreadable — so an inverted pair (a broken
    /// marker map) and a NaN pair fail both checks rather than quietly satisfying one of them.
    /// <para>
    /// The band value is sized against the measured data, not picked generically. Markers are
    /// normalised progress in [0, 1] (never frames or seconds), and the four sources below are
    /// strongly bimodal: every animated segment measures ≥ 0.075 (shortest is Settings' hover and
    /// press pairs at 0.075; Find/GlobalNav are 0.1125), while the negative control's hover pair
    /// measures exactly [0..0] in <em>both</em> directions. 1e-6 therefore sits ~75,000× below the
    /// shortest real segment and strictly above the static side, so it cannot absorb a real
    /// animation into "zero-length" — the failure that would let the negative control pass while
    /// being wrong. Any band strictly inside (0, 0.075) discriminates identically; the exact value
    /// is deliberately not load-bearing. Verified by mutation: raising it to 0.2 reddens the three
    /// duration oracles, and pointing the negative control at an animated source reddens it.
    /// </para>
    /// </summary>
    const double MinDuration = 1e-6;

    static bool Animated(double start, double end) =>
        !double.IsNaN(start) && !double.IsNaN(end) && end - start > MinDuration;

    static bool ZeroLength(double start, double end) =>
        !double.IsNaN(start) && !double.IsNaN(end)
        && end >= start
        && end - start <= MinDuration;

    // ════════════════════════════════════════════════════════════════════════
    //  1. Marker oracle — the state strings the page writes must name real transitions
    //     on the built-in sources it uses, *and* those transitions must span a non-zero
    //     slice of the timeline.
    //
    //     Scope, learned the hard way (issue #983, second round): a non-zero segment is
    //     NECESSARY for an animation and is NOT SUFFICIENT for a visible one. This oracle
    //     originally claimed that passing it meant the page was not "a static decoration".
    //     That claim was false and it is withdrawn. AnimatedSettings' NormalToPointerOver
    //     measures 0.075 here and renders ZERO changed pixels on screen — driving a Settings
    //     icon Normal -> PointerOver produced 0 changed frame-pairs out of 298, against an
    //     idle control reading 0 on the same region. This fixture was green throughout.
    //
    //     So: this checks that a segment EXISTS and has duration. Whether it renders a
    //     visible difference is a pixel question no marker map can answer. Normal<->PointerOver
    //     is measured invisible on ALL THREE built-in sources the gallery page uses, so that
    //     page is pinned to reaching a state outside {Normal, PointerOver} wherever an icon is
    //     pointer-driven (see
    //     GallerySampleLintTests.AnimatedIcons_UseSourcesAndStatesTheSelfTestProves).
    // ════════════════════════════════════════════════════════════════════════

    internal class BuiltInSourceMarkers(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // The band's own behaviour, pinned before anything is measured with it. Both the
            // MinDuration docstring and CheckStaticPair claim an inverted pair fails *both*
            // predicates; that only holds if ZeroLength rejects `end < start` outright, since
            // an inversion smaller than the band would otherwise read as "zero-length" and let
            // a broken marker map satisfy the negative control instead of failing it.
            H.Check("AnimIconBand_RejectsInvertedPairs",
                !Animated(0.5, 0.4999995) && !ZeroLength(0.5, 0.4999995)
                && !Animated(0.5, 0.1) && !ZeroLength(0.5, 0.1));

            // Control for the check above, which a band that rejected everything would also
            // pass: well-ordered pairs must still classify, and into opposite buckets.
            H.Check("AnimIconBand_ClassifiesOrderedPairs",
                Animated(0.0, 0.1125) && !ZeroLength(0.0, 0.1125)
                && ZeroLength(0.25, 0.25) && !Animated(0.25, 0.25));

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

                    // `ZeroLength`, not `end == start`: shares its band with the duration
                    // oracle so the two cannot disagree, and rejects both an inverted pair
                    // (end < start — a broken marker map, not a valid zero-length segment)
                    // and a NaN one, neither of which may satisfy the negative control.
                    if (!ZeroLength(start, end))
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

                        // `Animated`, not `end <= start`: the NaN arm matters because a NaN
                        // marker makes every ordered comparison false, so `end <= start`
                        // would wave it through and leave this check unable to fail. Shares
                        // its band with the negative control's `ZeroLength`.
                        if (!Animated(start, end))
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

    // ════════════════════════════════════════════════════════════════════════
    //  4. The icon must survive a state change *inside a Button*. The gallery's
    //     interactive cells put an AnimatedIcon directly in a Button's content slot;
    //     the menu sample nests it in an HStack. If a direct content swap remounts the
    //     control, the composition visual restarts and the transition is swallowed —
    //     the icon jumps to the new state's end frame with nothing drawn in between,
    //     which is indistinguishable from "hover does nothing" (issue #983).
    // ════════════════════════════════════════════════════════════════════════

    internal class StateSurvivesInsideButton(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Arm A — icon is the Button's direct content (what the gallery cells do).
            var directHost = H.CreateHost();
            directHost.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
                var (idx, setIdx) = ctx.UseState(0);
                var state = GalleryStates[idx];
                return VStack(
                    Button(AnimatedIcon(source).Size(32, 32)
                                .Set(icon => XamlAnimatedIcon.SetState(icon, state)),
                            () => { })
                        .Size(72, 56),
                    TextBlock($"Direct:{state}"),
                    Button("BumpDirect", () => setIdx((idx + 1) % GalleryStates.Length)));
            });

            await Harness.Render();
            var directBefore = H.FindControl<XamlAnimatedIcon>(_ => true);
            // The mount write matters as much as the update: AnimatedIcon hard-cuts on the
            // *first* State it is ever given and only animates subsequent ones. If mounting
            // inside a Button's content slot skips the setter, the user's first hover becomes
            // that first write -- a hard cut -- and only the second interaction animates.
            // That is precisely the reported symptom: click animates, hover does not.
            var directMountState = directBefore is null ? null : XamlAnimatedIcon.GetState(directBefore);
            H.ClickButton("BumpDirect");
            await Harness.Render();
            var directAfter = H.FindControl<XamlAnimatedIcon>(_ => true);

            // Arm B — icon nested in an HStack (what the menu sample does), as the control
            // for arm A: if both remount, the defect is not about the content slot at all.
            var nestedHost = H.CreateHost();
            nestedHost.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => new AnimatedGlobalNavigationButtonVisualSource());
                var (idx, setIdx) = ctx.UseState(0);
                var state = GalleryStates[idx];
                return VStack(
                    Button(HStack(8,
                                AnimatedIcon(source).Size(32, 32)
                                    .Set(icon => XamlAnimatedIcon.SetState(icon, state)),
                                TextBlock("label")),
                            () => { }),
                    TextBlock($"Nested:{state}"),
                    Button("BumpNested", () => setIdx((idx + 1) % GalleryStates.Length)));
            });

            await Harness.Render();
            var nestedBefore = H.FindControl<XamlAnimatedIcon>(_ => true);
            var nestedMountState = nestedBefore is null ? null : XamlAnimatedIcon.GetState(nestedBefore);
            H.ClickButton("BumpNested");
            await Harness.Render();
            var nestedAfter = H.FindControl<XamlAnimatedIcon>(_ => true);

            // Non-vacuity: a null on either side would make every ReferenceEquals below
            // trivially decidable for the wrong reason.
            H.Check("AnimIconInButton_BothArmsFoundControls",
                directBefore is not null && directAfter is not null
                && nestedBefore is not null && nestedAfter is not null);

            // The state write has to actually land, or "updated in place" is measuring an
            // icon nobody wrote to.
            H.Check("AnimIconInButton_DirectStateMoved",
                directAfter is not null && XamlAnimatedIcon.GetState(directAfter) == "PointerOver");
            H.Check("AnimIconInButton_NestedStateMoved",
                nestedAfter is not null && XamlAnimatedIcon.GetState(nestedAfter) == "PointerOver");

            // The claim under test: a state change must not remount the icon.
            H.Check("AnimIconInButton_DirectContentUpdatedInPlace",
                directBefore is not null && ReferenceEquals(directBefore, directAfter));
            H.Check("AnimIconInButton_NestedContentUpdatedInPlace",
                nestedBefore is not null && ReferenceEquals(nestedBefore, nestedAfter));

            // The mount write must land in *both* content shapes. A missing one here is not a
            // cosmetic gap: it silently converts the user's first interaction into the icon's
            // first-ever State set, which AnimatedIcon renders as a hard cut.
            H.Check("AnimIconInButton_NestedMountWroteState", nestedMountState == "Normal");
            H.Check("AnimIconInButton_DirectMountWroteState", () =>
                directMountState == "Normal"
                    ? true
                    : throw new global::System.InvalidOperationException(
                        $"direct Button content mounted with State='{directMountState ?? "<null>"}', "
                        + "expected 'Normal' — the mount setter did not run, so the first hover "
                        + "becomes the icon's first State write and hard-cuts instead of animating"));
        }
    }
}
