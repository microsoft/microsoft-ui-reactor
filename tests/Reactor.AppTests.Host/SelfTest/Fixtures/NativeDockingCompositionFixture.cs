using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.27 — composition-driven content selftests. Verify the
/// §2.30 shape-only `layoutOverride` contract: apps declare the full
/// tree (with current content) in `Render()`; the host owns shape,
/// the app owns content; state updates flow into pane bodies idiomatically
/// regardless of whether the user has reshaped the layout.
/// </summary>
internal static class NativeDockingCompositionFixtures
{
    /// <summary>
    /// Mutate state outside the docking subtree → assert the new content
    /// reaches the active pane's body via keyed reconciliation. This is
    /// the headline contract of the §2.30 work — apps shouldn't need a
    /// content walker.
    /// </summary>
    internal class Composition_ContentMutationFlowsToActivePane(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            int counter = 0;
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                counter = count;
                return new DockManager
                {
                    Layout = new DockTabGroup(new DockableContent[]
                    {
                        new DockableContent(
                            Title: "Counter",
                            // The body re-renders with the live count
                            // every time setCount fires — exercises the
                            // §2.30 "app owns content" claim.
                            Content: VStack(4,
                                TextBlock($"count={count}"),
                                Button("inc", () => setCount(count + 1))),
                            Key: "comp:counter"),
                    }),
                };
            });
            await Harness.Render();

            H.Check("Composition_InitialCountVisible",
                await Harness.WaitFor(() => H.FindText("count=0") is not null));

            // Click the inc button and re-render — the pane body should
            // re-render with the new state.
            H.ClickButton("inc");
            await Harness.Render();
            H.Check("Composition_AfterInc_CountIsOne", H.FindText("count=1") is not null);

            H.ClickButton("inc");
            await Harness.Render();
            H.Check("Composition_AfterIncTwice_CountIsTwo", H.FindText("count=2") is not null);

            H.Check("Composition_StateAdvanced", counter == 2);

            host.Mount(_ => TextBlock("composition-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Verify that updating a sibling pane's content does NOT cause the
    /// active pane to re-mount its component. Asserts the keyed-reconciliation
    /// contract: panes addressed by stable `Key` retain their state across
    /// content mutations on neighbouring panes.
    /// </summary>
    internal class Composition_SiblingMutation_PreservesActivePaneIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            host.Mount(ctx =>
            {
                // Two panes side-by-side; both bodies derive from a single
                // tick state but only "left" has the button.
                var (tick, setTick) = ctx.UseState(0);
                return new DockManager
                {
                    Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
                    {
                        new DockTabGroup(new DockableContent[]
                        {
                            new DockableContent(
                                Title: "Left",
                                Content: VStack(4,
                                    TextBlock($"left-tick={tick}"),
                                    Button("bump", () => setTick(tick + 1))),
                                Key: "comp:left"),
                        }),
                        new DockTabGroup(new DockableContent[]
                        {
                            new DockableContent(
                                Title: "Right",
                                Content: TextBlock($"right-tick={tick}"),
                                Key: "comp:right"),
                        }),
                    }),
                };
            });
            await Harness.Render();

            // Snapshot the realized Border identities so we can verify
            // the pane wrappers stay the same instance across re-renders
            // (keyed-reconciliation preserves them).
            var bordersBefore = H.FindAllControls<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b)
                    is "pane:comp:left" or "pane:comp:right").ToList();
            H.Check("Composition_BothPanesRealized", bordersBefore.Count == 2);

            // Fire a state change; both panes' Content reference must be
            // refreshed (left's tick text + right's tick text) but the
            // wrapper Border instances must persist.
            H.ClickButton("bump");
            await Harness.Render();

            var bordersAfter = H.FindAllControls<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b)
                    is "pane:comp:left" or "pane:comp:right").ToList();
            H.Check("Composition_BothPanesStillRealized", bordersAfter.Count == 2);

            bool identityPreserved = true;
            foreach (var before in bordersBefore)
            {
                var key = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(before);
                bool foundMatching = false;
                foreach (var after in bordersAfter)
                {
                    if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(after) == key
                        && ReferenceEquals(before, after))
                    {
                        foundMatching = true;
                        break;
                    }
                }
                if (!foundMatching) { identityPreserved = false; break; }
            }
            H.Check("Composition_PaneWrapperIdentityPreserved", identityPreserved);

            H.Check("Composition_LeftBodyUpdated", H.FindText("left-tick=1") is not null);
            H.Check("Composition_RightBodyUpdated", H.FindText("right-tick=1") is not null);

            host.Mount(_ => TextBlock("composition-sibling-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.27 — rehydration via composition. Save a layout (the
    /// JSON v2 serializer captures shape + keys; content is NOT
    /// serialized), then mount a fresh host with the loaded shape +
    /// app-supplied panes keyed identically. Assert each restored slot
    /// receives its component-supplied content matched by `Key`.
    /// </summary>
    internal class Composition_Rehydration_ContentMatchesByKey(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Build a save-shape: two-pane horizontal split with keys
            // "rehy:left" and "rehy:right". Content is irrelevant at
            // save time — only shape + keys persist.
            var saveShape = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new DockableContent[]
                {
                    new DockableContent("Left", null, Key: "rehy:left"),
                }),
                new DockTabGroup(new DockableContent[]
                {
                    new DockableContent("Right", null, Key: "rehy:right"),
                }),
            });

            var savedJson = Microsoft.UI.Reactor.Docking.Persistence.DockLayoutSerializer.Save(
                root: saveShape);
            H.Check("Rehydration_SavedJsonNonEmpty", !string.IsNullOrEmpty(savedJson));

            // Restore: load the JSON and reconstruct the layout, then
            // mount a fresh host with the loaded shape but with app-
            // supplied content panes keyed identically.
            var loadResult = Microsoft.UI.Reactor.Docking.Persistence.DockLayoutSerializer.Load(savedJson);
            H.Check("Rehydration_LoadSucceeded", loadResult.Success);
            if (loadResult.Root is not DockSplit loadedRoot)
            {
                H.Check("Rehydration_LoadProducesSplit", false);
                return;
            }

            // Walk loadedRoot and replace each leaf with an app-supplied
            // DockableContent keyed identically — this is what the spec
            // calls "component-supplied content lands in restored slots
            // matched by Key".
            DockNode RebuildWithApp(DockNode node) => node switch
            {
                DockSplit s => s with { Children = s.Children.Select(RebuildWithApp).ToArray() },
                DockTabGroup g => g with
                {
                    Documents = g.Documents.Select(d => new DockableContent(
                        Title: d.Title ?? string.Empty,
                        Content: TextBlock($"rehy-body-{d.Key}"),
                        Key: d.Key)).ToArray(),
                },
                _ => node,
            };
            var hydrated = RebuildWithApp(loadedRoot);

            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            host.Mount(_ => new DockManager { Layout = hydrated });
            await Harness.Render();

            H.Check("Rehydration_LeftBodyRendered",
                H.FindText("rehy-body-rehy:left") is not null);
            H.Check("Rehydration_RightBodyRendered",
                H.FindText("rehy-body-rehy:right") is not null);

            // Both pane wrappers should carry the original `pane:<key>`
            // AutomationId so AT addresses survive save / restore.
            var leftBorder = H.FindAllControls<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "pane:rehy:left")
                .FirstOrDefault();
            H.Check("Rehydration_LeftAutomationIdRestored", leftBorder is not null);

            host.Mount(_ => TextBlock("rehydration-done"));
            await Harness.Render();
        }
    }
}
