using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class LayoutAnimationFixtures
{
    internal class OffsetAnimationSetup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Animated Item"))
                        .LayoutAnimation()
                        .AutomationId("layout-anim-target")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "layout-anim-target");

            H.Check("LayoutAnim_TargetMounted", target is not null);

            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check("LayoutAnim_HasImplicitAnimations",
                    visual.ImplicitAnimations is not null);

                var hasOffset = false;
                if (visual.ImplicitAnimations is not null)
                {
                    try { var _ = visual.ImplicitAnimations["Offset"]; hasOffset = true; }
                    catch { hasOffset = false; }
                }
                H.Check("LayoutAnim_HasOffsetAnimation", hasOffset);
            }
        }
    }

    internal class SpringAnimationSetup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Spring Item"))
                        .SpringLayoutAnimation(dampingRatio: 0.8f, period: 0.1f)
                        .AutomationId("spring-anim-target")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "spring-anim-target");

            H.Check("LayoutAnim_SpringTargetMounted", target is not null);

            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check("LayoutAnim_SpringHasImplicitAnimations",
                    visual.ImplicitAnimations is not null);

                var hasOffset = false;
                if (visual.ImplicitAnimations is not null)
                {
                    try { var _ = visual.ImplicitAnimations["Offset"]; hasOffset = true; }
                    catch { hasOffset = false; }
                }
                H.Check("LayoutAnim_SpringHasOffsetAnimation", hasOffset);
            }
        }
    }

    internal class SizeAnimationSetup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("Size Animated"))
                        .LayoutAnimation(new LayoutAnimationConfig { AnimateSize = true })
                        .AutomationId("size-anim-target")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "size-anim-target");

            H.Check("LayoutAnim_SizeTargetMounted", target is not null);

            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check("LayoutAnim_SizeHasImplicitAnimations",
                    visual.ImplicitAnimations is not null);

                var hasOffset = false;
                var hasSize = false;
                if (visual.ImplicitAnimations is not null)
                {
                    try { var _ = visual.ImplicitAnimations["Offset"]; hasOffset = true; }
                    catch { hasOffset = false; }
                    try { var _ = visual.ImplicitAnimations["Size"]; hasSize = true; }
                    catch { hasSize = false; }
                }
                H.Check("LayoutAnim_SizeHasOffsetAnimation", hasOffset);
                H.Check("LayoutAnim_SizeHasSizeAnimation", hasSize);
            }
        }
    }

    internal class ConnectedAnimationMountUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Mount a FlexPanel with items that have ConnectedAnimation keys,
            // then switch to a VStack — the unmount→mount cycle should not crash.
            var host = H.CreateHost();
            var showFlex = true;

            host.Mount(ctx =>
            {
                if (showFlex)
                {
                    return new FlexElement(new Element[]
                    {
                        Border(TextBlock("A")).ConnectedAnimation("ca-test-a").AutomationId("ca-a"),
                        Border(TextBlock("B")).ConnectedAnimation("ca-test-b").AutomationId("ca-b"),
                    });
                }
                else
                {
                    return VStack(
                        Border(TextBlock("A")).ConnectedAnimation("ca-test-a").AutomationId("ca-a2"),
                        Border(TextBlock("B")).ConnectedAnimation("ca-test-b").AutomationId("ca-b2")
                    );
                }
            });

            await Harness.Render();

            H.Check("ConnectedAnim_InitialMounted",
                H.FindText("A") is not null && H.FindText("B") is not null);

            // Toggle to trigger unmount (PrepareToAnimate) → mount (TryStart)
            showFlex = false;
            // Force re-render by remounting
            host.Mount(ctx =>
            {
                return VStack(
                    Border(TextBlock("A")).ConnectedAnimation("ca-test-a").AutomationId("ca-a2"),
                    Border(TextBlock("B")).ConnectedAnimation("ca-test-b").AutomationId("ca-b2")
                );
            });

            await Harness.Render();

            H.Check("ConnectedAnim_AfterSwitch_Mounted",
                H.FindText("A") is not null && H.FindText("B") is not null);
        }
    }

    /// <summary>
    /// Issue: a connected animation declared with <c>.ConnectedAnimation(key)</c> never
    /// visibly ran when the source and destination lived in subtrees that the child
    /// reconciler REPLACES (the type-mismatch arm) — the destination just appeared at its
    /// final position. Root cause: <c>Mount</c> resolved the key against
    /// <c>ConnectedAnimationService</c> immediately, but the reconciler mounts a
    /// replacement BEFORE unmounting the control it replaces, so the source's
    /// <c>PrepareToAnimate</c> had not run yet and the lookup returned null.
    ///
    /// The settled tree is byte-identical whether or not the animation ran, so the oracle
    /// here is the framework's start counter: it increments only when
    /// <c>ConnectedAnimation.TryStart</c> actually returns true. Restore the mount-time
    /// lookup and this check goes 0 → red.
    /// </summary>
    internal class ConnectedAnimationStartsAcrossReplace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            // Deliberately mirrors the docs ConnectedAnimationDemo tree shape: BOTH root
            // children change element type across the transition, so index 1 goes
            // Mount(destination TextBlock) → Unmount(source Button subtree). Any shape
            // where the destination is UPDATED rather than mounted would not exercise
            // the queue at all.
            host.Mount(ctx =>
            {
                var (detail, setDetail) = ctx.UseState(false);

                if (detail)
                    return VStack(12,
                        Button("CaBack", () => setDetail(false)),
                        TextBlock("Hero").FontSize(28)
                            .ConnectedAnimation("ca-replace-hero")
                            .AutomationId("ca-destination"));

                return VStack(12,
                    TextBlock("List header"),
                    VStack(4,
                        Button("CaGo", () => setDetail(true))
                            .ConnectedAnimation("ca-replace-hero")
                            .AutomationId("ca-source")));
            });

            await Harness.Render();

            var source = H.FindControl<Button>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "ca-source");
            H.Check("ConnectedAnimReplace_SourceMounted", source is not null);
            // Positive control for the oracle below: a source with zero bounds cannot
            // produce a startable snapshot, which would make the start count a
            // property of the harness rather than of the reconciler.
            H.Check("ConnectedAnimReplace_SourceMeasured",
                source is not null && source.ActualWidth > 0 && source.ActualHeight > 0);

            int baseline = host.Reconciler.ConnectedAnimationStartCount;

            H.ClickButton("CaGo");
            await host.WaitForIdleAsync();

            int afterForward = host.Reconciler.ConnectedAnimationStartCount;

            H.Check("ConnectedAnimReplace_DestinationMounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "ca-destination") is not null);

            // Core assertion: the transition actually STARTED a connected animation.
            // Measured against the pre-fix build this reads baseline+0, i.e. red.
            H.Check("ConnectedAnimReplace_AnimationStarted", afterForward == baseline + 1);
            // The reverse trip (destination unmounts, source re-mounts) starts one too.
            // Pinned to `baseline` rather than `afterForward` on purpose: the pre-fix
            // build orphans the forward snapshot and then consumes it on the way back,
            // so a delta-from-afterForward oracle would pass on the broken build.
            H.ClickButton("CaBack");
            await host.WaitForIdleAsync();

            H.Check("ConnectedAnimReplace_AnimationStartedOnReturn",
                host.Reconciler.ConnectedAnimationStartCount == baseline + 2);
        }
    }

    /// <summary>
    /// The queue must not fire for a key that nothing unmounted under: the very first
    /// mount of a keyed element has no source to travel from, so it must render plainly
    /// rather than start a stale animation. Guards the deferred-resolution fix against
    /// over-firing.
    /// </summary>
    internal class ConnectedAnimationNoSourceDoesNotStart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            int baseline = host.Reconciler.ConnectedAnimationStartCount;

            host.Mount(ctx => VStack(
                TextBlock("Lonely")
                    .ConnectedAnimation("ca-never-prepared-" + global::System.Guid.NewGuid().ToString("N"))
                    .AutomationId("ca-lonely")));

            await Harness.Render();

            H.Check("ConnectedAnimNoSource_Mounted", H.FindText("Lonely") is not null);
            H.Check("ConnectedAnimNoSource_DidNotStart",
                host.Reconciler.ConnectedAnimationStartCount == baseline);
        }
    }

    /// <summary>
    /// Reactor prepares a source snapshot for EVERY unmounting element carrying a key,
    /// because the reconciler cannot know which sibling the user designated. When a list
    /// of N keyed siblings collapses to one destination, N-1 preparations go unclaimed —
    /// and WinUI keeps each unclaimed snapshot composited at its source element's old
    /// position for about a second, painting the old list on top of the new view. The
    /// flush must release them.
    /// </summary>
    internal class ConnectedAnimationReleasesUnclaimedSources(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            string[] items = ["Alpha", "Bravo", "Charlie"];

            host.Mount(ctx =>
            {
                var (selected, setSelected) = ctx.UseState<string?>(null);

                if (selected is not null)
                    return VStack(12,
                        Button("RelBack", () => setSelected(null)),
                        TextBlock(selected).FontSize(28)
                            .ConnectedAnimation($"rel-{selected}")
                            .AutomationId("rel-destination"));

                return VStack(12,
                    TextBlock("Pick one"),
                    VStack(4, items.Select(i =>
                        (Element)Button($"Rel{i}", () => setSelected(i))
                            .ConnectedAnimation($"rel-{i}")).ToArray()));
            });

            await Harness.Render();

            int startBaseline = host.Reconciler.ConnectedAnimationStartCount;
            int releaseBaseline = host.Reconciler.ConnectedAnimationReleaseCount;

            // Three sources unmount under keys rel-Alpha / rel-Bravo / rel-Charlie;
            // only rel-Alpha has a destination.
            H.ClickButton("RelAlpha");
            await host.WaitForIdleAsync();

            H.Check("ConnectedAnimRelease_ClaimedOneStarted",
                host.Reconciler.ConnectedAnimationStartCount == startBaseline + 1);

            // The two siblings nobody travelled from must be cancelled, not left to
            // linger as ghosts over the detail view.
            H.Check("ConnectedAnimRelease_TwoOrphansReleased",
                host.Reconciler.ConnectedAnimationReleaseCount == releaseBaseline + 2);

            // No stale snapshot leaked into a later pass. Navigating back unmounts ONE
            // keyed element (the rel-Alpha headline) and mounts three keyed buttons, so
            // exactly one animation may start. Without the release step the orphaned
            // rel-Bravo / rel-Charlie snapshots would still be sitting in the service and
            // their buttons would travel from them too, making this three.
            //
            // Deliberately not asserted via ConnectedAnimationService.GetAnimation: the
            // service keeps returning a cancelled animation for an indeterminate time, so
            // that reads green alone and red under suite load. Counting the starts Reactor
            // actually performs is the same claim without the race.
            int beforeReturn = host.Reconciler.ConnectedAnimationStartCount;
            H.ClickButton("RelBack");
            await host.WaitForIdleAsync();

            H.Check("ConnectedAnimRelease_NoStaleStartsOnReturn",
                host.Reconciler.ConnectedAnimationStartCount == beforeReturn + 1);
        }
    }
}
