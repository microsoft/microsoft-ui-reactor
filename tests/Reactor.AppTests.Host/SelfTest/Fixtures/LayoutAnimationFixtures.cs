using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
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

            // The return trip prepares the same key from the hero. Reactor does not prepare a
            // key again until WinUI has rendered a frame since its last preparation (issue
            // #1152), so without that frame the reverse start would depend on whether WinUI
            // happened to render between the two clicks.
            H.Check("ConnectedAnimReplace_ForwardFrameRendered",
                await PreparationRenderedAsync("ca-replace-hero"));

            // The reverse trip (destination unmounts, source re-mounts) starts one too.
            // Pinned to `baseline` rather than `afterForward` on purpose: the pre-fix
            // build orphans the forward snapshot and then consumes it on the way back,
            // so a delta-from-afterForward oracle would pass on the broken build.
            H.ClickButton("CaBack");
            await host.WaitForIdleAsync();

            H.Check("ConnectedAnimReplace_AnimationStartedOnReturn",
                host.Reconciler.ConnectedAnimationStartCount == baseline + 2);

            // The hero left the tree by itself and the return animation has started. If the
            // next fixture's mount removed the destination before WinUI rendered that
            // animation, WinUI would cancel it and fault in the next commit, so let it render.
            H.Check("ConnectedAnimReplace_ReturnFrameRendered",
                await PreparationRenderedAsync("ca-replace-hero"));
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
    /// Regression for a native access violation in Microsoft.UI.Xaml.dll (0xC0000005),
    /// reproduced by hand in the ReactorGallery "Both ends need the same key" card:
    /// toggle the destination key off, then activate the source. That is the shape where
    /// a pass prepares a snapshot and NOTHING claims it — no destination carries the key
    /// at all. An earlier revision withdrew those unclaimed preparations with
    /// <c>ConnectedAnimation.Cancel()</c> to stop them ghosting, and cancelling faulted the
    /// process. This pins the no-crash behaviour: the unclaimed snapshot is left to time out
    /// on its own.
    ///
    /// <para>The round trips below fault the same way through a second path (issue #1152): the
    /// second Go prepares the key again, which makes WinUI cancel the first preparation. That
    /// faults only when WinUI has rendered no frame between the two Go passes, which made this
    /// fixture crash in about 5% of runs. <see cref="ConnectedAnimationRepreparedBeforeFrameDoesNotCrash"/>
    /// drives the same passes with no frame in between, deterministically.</para>
    /// </summary>
    internal class ConnectedAnimationOrphanOnlyPassDoesNotCrash(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);

                // Destination deliberately carries NO key, exactly like the gallery card
                // with its toggle off.
                if (open)
                    return VStack(12,
                        Button("OrphanOnlyBack", () => setOpen(false)),
                        TextBlock("Fluent").FontSize(34).AutomationId("orphan-only-dest"));

                return VStack(12,
                    TextBlock("Open the detail view"),
                    Button("OrphanOnlyGo", () => setOpen(true))
                        .ConnectedAnimation("orphan-only-key"));
            });

            await Harness.Render();

            int startBaseline = host.Reconciler.ConnectedAnimationStartCount;

            H.ClickButton("OrphanOnlyGo");
            await host.WaitForIdleAsync();

            H.Check("ConnectedAnimOrphanOnly_DestinationMounted",
                H.FindText("Fluent") is not null);
            H.Check("ConnectedAnimOrphanOnly_NothingStarted",
                host.Reconciler.ConnectedAnimationStartCount == startBaseline);

            // Round-trip and repeat: the hand repro needed a second activation, and a
            // dangling native object often only faults on later use rather than at the
            // call that orphaned it.
            H.ClickButton("OrphanOnlyBack");
            await host.WaitForIdleAsync();
            H.ClickButton("OrphanOnlyGo");
            await host.WaitForIdleAsync();
            H.ClickButton("OrphanOnlyBack");
            await host.WaitForIdleAsync();

            H.Check("ConnectedAnimOrphanOnly_SurvivesRepeatedRoundTrips",
                H.FindText("Open the detail view") is not null);
        }
    }

    /// <summary>
    /// Issue #1152: the selftest host died with 0xC0000005 at
    /// <c>CConnectedAnimationService::PreCommit+0x81</c> in Microsoft.UI.Xaml.dll during
    /// <c>ConnectedAnimation_OrphanOnlyPassDoesNotCrash</c>. WinUI processes a preparation in
    /// the next frame's commit. The first Go prepares the key and removes its button, so
    /// WinUI holds that button for the commit, and only the preparation gives it a
    /// composition node. When a second Go prepared the key again before that frame, WinUI
    /// cancelled the first preparation, which took the node away, and the commit
    /// dereferenced null.
    ///
    /// <para>This drives the orphan fixture's Go, Back, Go through a bare
    /// <see cref="Reconciler"/> in one synchronous block, so WinUI cannot render a frame
    /// between the passes. Without <c>ConnectedAnimationFrameGate</c> the process dies at
    /// the next frame, every run.</para>
    /// </summary>
    internal class ConnectedAnimationRepreparedBeforeFrameDoesNotCrash(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string key = "reprepared-before-frame-key";
            var reconciler = new Reconciler();
            Action noop = static () => { };
            Element List() => VStack(12,
                TextBlock("Reprepare list"),
                Button("ReprepareGo", noop).ConnectedAnimation(key));
            Element Detail() => VStack(12,
                Button("ReprepareBack", noop),
                TextBlock("Reprepare detail"));
            Element DetailWithHero() => VStack(12,
                Button("ReprepareBack", noop),
                TextBlock("Reprepare hero").ConnectedAnimation(key));

            var list = List();
            var root = reconciler.Reconcile(null, list, null, noop)!;
            H.SetContent(root);
            await Harness.Render();

            // Go, Back, Go with no frame in between. Only the first Go prepares the key.
            var riskyFrame = new RenderedFrame();
            int skippedBefore = reconciler.ConnectedAnimationPreparationsSkipped;
            var detail = Detail();
            reconciler.Reconcile(list, detail, root, noop);
            reconciler.FlushConnectedAnimations();
            int skippedByFirstGo = reconciler.ConnectedAnimationPreparationsSkipped - skippedBefore;
            var list2 = List();
            reconciler.Reconcile(detail, list2, root, noop);
            reconciler.FlushConnectedAnimations();
            var detail2 = Detail();
            reconciler.Reconcile(list2, detail2, root, noop);
            reconciler.FlushConnectedAnimations();
            int skippedBySecondGo = reconciler.ConnectedAnimationPreparationsSkipped - skippedBefore - skippedByFirstGo;

            H.Check("ConnectedAnimRepreparedBeforeFrame_FirstGoPrepared", skippedByFirstGo == 0,
                $"skipped={skippedByFirstGo}");
            H.Check("ConnectedAnimRepreparedBeforeFrame_SecondGoSkipped", skippedBySecondGo == 1,
                $"skipped={skippedBySecondGo}");

            // Positive control: the commit that used to fault ran. Surviving it is the regression.
            H.Check("ConnectedAnimRepreparedBeforeFrame_FrameRendered", await riskyFrame.WaitAsync());
            H.Check("ConnectedAnimRepreparedBeforeFrame_Survived", H.FindText("Reprepare detail") is not null);

            // The gate opens with that frame: the key prepares again and reaches a destination.
            var listFrame = new RenderedFrame();
            var list3 = List();
            reconciler.Reconcile(detail2, list3, root, noop);
            reconciler.FlushConnectedAnimations();
            H.Check("ConnectedAnimRepreparedBeforeFrame_ListFrameRendered", await listFrame.WaitAsync());

            int startsBefore = reconciler.ConnectedAnimationStartCount;
            var heroFrame = new RenderedFrame();
            var hero = DetailWithHero();
            reconciler.Reconcile(list3, hero, root, noop);
            reconciler.FlushConnectedAnimations();
            H.Check("ConnectedAnimRepreparedBeforeFrame_KeyAnimatesAfterFrame",
                reconciler.ConnectedAnimationStartCount == startsBefore + 1,
                $"starts={reconciler.ConnectedAnimationStartCount - startsBefore}");

            // Removing the hero before WinUI renders its animation would cancel it and fault
            // the same way, so let it render before the content goes.
            H.Check("ConnectedAnimRepreparedBeforeFrame_HeroFrameRendered", await heroFrame.WaitAsync());
            H.SetContent(null);
        }
    }

    /// <summary>
    /// True once WinUI has rendered a frame since <paramref name="key"/> was last prepared,
    /// which is what Reactor waits for before it prepares the key again. False if no frame
    /// rendered within 5 s.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="ConnectedAnimationFrameGate"/> rather than from a frame counted
    /// before the click: a host renders from the dispatcher queue, so a frame can land between
    /// a click and the pass it schedules, and that frame says nothing about the preparation.
    /// </remarks>
    private static async Task<bool> PreparationRenderedAsync(string key)
    {
        if (ConnectedAnimationFrameGate.IsAwaitingFrame(key))
            await new RenderedFrame().WaitAsync();
        return !ConnectedAnimationFrameGate.IsAwaitingFrame(key);
    }

    /// <summary>
    /// Completes on the first <see cref="CompositionTarget.Rendered"/> raised after it is
    /// created. WinUI raises it after a frame's commit, which is where it processes connected
    /// animations, so create it before the change the frame must follow.
    /// </summary>
    private sealed class RenderedFrame
    {
        private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly EventHandler<RenderedEventArgs> _onRendered;

        public RenderedFrame()
        {
            _onRendered = (_, _) =>
            {
                CompositionTarget.Rendered -= _onRendered;
                _rendered.TrySetResult();
            };
            CompositionTarget.Rendered += _onRendered;
        }

        /// <summary>True once the frame has rendered; false if none rendered within 5 s.</summary>
        public async Task<bool> WaitAsync()
        {
            if (await Task.WhenAny(_rendered.Task, Task.Delay(5000)) == _rendered.Task)
                return true;
            CompositionTarget.Rendered -= _onRendered;
            return false;
        }
    }
}
