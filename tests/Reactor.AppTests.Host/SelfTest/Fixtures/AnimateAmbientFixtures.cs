using System.Collections.Specialized;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 042 Phase 3 §6 — end-to-end verification that wrapping a state
/// mutation in <see cref="Animations.Animate"/> carries animation intent
/// from the setter dispatch site through the dispatcher hop and into the
/// reconcile-time diff. The fixtures here pin three observable contracts:
/// <list type="bullet">
/// <item><description>Insert: the newly-inserted <see cref="ReactorRow"/>
/// carries <see cref="ReactorRow.PendingEnterAnimation"/> when WinUI
/// receives the <c>CollectionChanged.Add</c>. Without the ambient, the
/// row is null-tagged so the realize handler does nothing
/// extra.</description></item>
/// <item><description>Move: the moved container's
/// <c>Composition.Visual.ImplicitAnimations</c> carries an "Offset" entry
/// after the diff fires the move op.</description></item>
/// <item><description>Hand-built <see cref="ChildReconciler"/> path: a
/// keyed <c>FlexColumn</c> child inserted under
/// <see cref="Animations.Animate"/> picks up the ambient via a freshly-
/// attached implicit animation; without the ambient, no such attachment.</description></item>
/// </list>
/// In-process WinUI rather than Appium because the assertions inspect
/// the OC event stream synchronously and the live <c>Composition.Visual</c>
/// state, which Appium cannot observe.
/// </summary>
internal static class AnimateAmbientFixtures
{
    private record Item(string Id, string Label);

    // ────────────────────────────────────────────────────────────────────
    //  Insert under Animate: inserted ReactorRow carries the kind tag at
    //  the moment WinUI sees the CollectionChanged.Add event.
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_InsertUnderAnimate_TagsRowWithKind(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "Alpha"), new Item("b", "Beta") }
                    : new[] { new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") };

                // Wrap the setter in Animations.Animate so the ambient
                // flows from the click callback through the dispatcher to
                // the reconcile-time diff.
                return VStack(
                    Button("AddSpring", () => Animations.Animate(AnimationKind.Spring,
                        () => setPhase(1))),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("AAF_InsertSpring_ListViewExists", lv is not null);

            // Intercept the CollectionChanged Add event so we observe the
            // ReactorRow's PendingEnterAnimation *before* the realize handler
            // clears it. This is the cleanest observable contract — the OC
            // event fires synchronously inside KeyedListDiff.Apply, the
            // realize fires asynchronously when WinUI materializes the
            // container.
            AnimationKind? observedKind = null;
            if (lv?.ItemsSource is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += (_, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Add
                        && e.NewItems is { Count: > 0 }
                        && e.NewItems[0] is ReactorRow row)
                    {
                        observedKind = row.PendingEnterAnimation;
                    }
                };
            }

            H.ClickButton("AddSpring");
            await Harness.Render();

            H.Check("AAF_InsertSpring_RowTaggedSpring",
                observedKind == AnimationKind.Spring);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Insert without Animate: row is NOT tagged. This pins the gating
    //  contract — outside Animations.Animate, the steady-state hot path
    //  does no animation tagging and no MovedRows allocation.
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_InsertWithoutAnimate_RowNotTagged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "Alpha"), new Item("b", "Beta") }
                    : new[] { new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") };

                return VStack(
                    Button("AddBare", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);

            AnimationKind? observedKind = AnimationKind.Spring; // sentinel: must become null
            bool addSeen = false;
            if (lv?.ItemsSource is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += (_, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Add
                        && e.NewItems is { Count: > 0 }
                        && e.NewItems[0] is ReactorRow row)
                    {
                        addSeen = true;
                        observedKind = row.PendingEnterAnimation;
                    }
                };
            }

            H.ClickButton("AddBare");
            await Harness.Render();

            H.Check("AAF_InsertBare_AddObserved", addSeen);
            H.Check("AAF_InsertBare_RowNotTagged", observedKind is null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Animate(.None) explicitly suppresses: nested or top-level None
    //  must not tag the inserted row, matching the AsyncLocal-stack
    //  contract.
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_InsertUnderAnimateNone_RowNotTagged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "Alpha"), new Item("b", "Beta") }
                    : new[] { new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") };

                return VStack(
                    Button("AddNone", () => Animations.Animate(AnimationKind.None,
                        () => setPhase(1))),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);

            AnimationKind? observedKind = AnimationKind.Spring; // sentinel
            if (lv?.ItemsSource is INotifyCollectionChanged ncc)
            {
                ncc.CollectionChanged += (_, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Add
                        && e.NewItems is { Count: > 0 }
                        && e.NewItems[0] is ReactorRow row)
                        observedKind = row.PendingEnterAnimation;
                };
            }

            H.ClickButton("AddNone");
            await Harness.Render();

            H.Check("AAF_AnimateNone_RowNotTagged", observedKind is null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Move under Animate: the moved container's Visual carries the
    //  Offset implicit animation that drives the slide. This is the
    //  user-observable shape (vs. the Insert path, which fires a
    //  show-then-clear animation we can only observe via the row tag).
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_MoveUnderAnimate_AttachesImplicitOffset(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") }
                    : new[] { new Item("a", "Alpha"), new Item("c", "Gamma"), new Item("b", "Beta") };

                return VStack(
                    Button("MoveSpring", () => Animations.Animate(AnimationKind.Spring,
                        () => setPhase(1))),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(220)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("AAF_MoveSpring_ListViewMounted", lv is not null);

            H.ClickButton("MoveSpring");
            await Harness.Render();
            // The move-animation attach is deferred to the next dispatcher
            // turn (so WinUI has reconciled containers to their new
            // indices); a second Render() pumps the deferred work.
            await Harness.Render();

            // The moved row "c" lives at index 1 after the swap. Its
            // realized container's Visual.ImplicitAnimations is set by
            // ApplyMoveAnimations / StartMoveOffsetAnimation so subsequent
            // layout-driven Offset changes spring instead of snapping.
            // We assert the collection is present and contains an "Offset"
            // entry (the only key we attach).
            var state = lv is not null ? Reconciler.GetListState(lv) : null;
            bool offsetAttached = false;
            if (state is not null && lv is not null)
            {
                ReactorRow? movedC = null;
                foreach (var r in state.Source)
                    if (r.Key == "c") { movedC = r; break; }
                if (movedC is not null)
                {
                    // Same fallback as production ApplyMoveAnimations.
                    var container =
                        lv.ContainerFromIndex(movedC.Index) as global::Microsoft.UI.Xaml.UIElement
                        ?? lv.ContainerFromItem(movedC) as global::Microsoft.UI.Xaml.UIElement;
                    if (container is not null)
                    {
                        var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(container);
                        offsetAttached = visual.ImplicitAnimations is not null
                            && visual.ImplicitAnimations.ContainsKey("Offset");
                    }
                }
            }
            H.Check("AAF_MoveSpring_OffsetImplicitAttached", offsetAttached);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  FlexColumn (hand-built ChildReconciler path) under Animate:
    //  the moved child's Visual picks up an implicit Offset animation
    //  exactly like the templated-list path. Spec 042 §6 calls for the
    //  same ambient to drive both pipelines.
    // ────────────────────────────────────────────────────────────────────

    internal class FlexColumn_MoveUnderAnimate_AttachesImplicitOffset(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var keys = phase == 0
                    ? new[] { "a", "b", "c" }
                    : new[] { "a", "c", "b" }; // swap "b" and "c"
                return VStack(
                    Button("Swap", () => Animations.Animate(AnimationKind.Spring,
                        () => setPhase(1))),
                    FlexColumn(keys.Select(k =>
                        Border(TextBlock(k).AutomationId($"fc_row_{k}"))
                            .WithKey(k)).Cast<Element>().ToArray())
                );
            });

            await Harness.Render();

            H.ClickButton("Swap");
            await Harness.Render();

            // Find the moved "c" Border. After the swap "c" is at index 1.
            // ChildReconciler.ApplyAmbientMove attaches the implicit
            // animation so a subsequent layout pass animates Offset.
            var tb = H.FindControl<TextBlock>(t =>
                global::Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "fc_row_c");
            bool offsetAttached = false;
            if (tb?.Parent is Border bord)
            {
                var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(bord);
                offsetAttached = visual.ImplicitAnimations is not null
                    && visual.ImplicitAnimations.ContainsKey("Offset");
            }
            H.Check("AAF_FlexColumnMove_OffsetImplicitAttached", offsetAttached);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scope discipline (§6): a property change on a surviving leaf
    //  inside Animate(.Spring) must NOT animate via the transactional
    //  ambient — that is the job of per-element .WithImplicitTransition()
    //  / .LayoutAnimation() modifiers. We assert by observing that
    //  AnimationScope (the property-setter channel) is untouched
    //  inside the click callback.
    // ────────────────────────────────────────────────────────────────────

    internal class Animate_DoesNot_AnimateLeafProperties(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            bool scopeWasSetInside = false;
            host.Mount(_ =>
                VStack(
                    Button("Tap", () => Animations.Animate(AnimationKind.Spring, () =>
                    {
                        // Reactor's property-setter hot path consults
                        // AnimationScope.Current. The Animate ambient flows
                        // on AsyncLocal; the property scope is ThreadStatic.
                        // The two channels must remain independent.
                        scopeWasSetInside =
                            global::Microsoft.UI.Reactor.Animation.AnimationScope.HasScope;
                    })),
                    TextBlock("leaf")));

            await Harness.Render();
            H.ClickButton("Tap");
            await Harness.Render();

            H.Check("AAF_Animate_DoesNotPushAnimationScope", !scopeWasSetInside);
        }
    }
}
