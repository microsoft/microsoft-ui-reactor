using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest coverage for the reconcile-highlight overlay feature:
///   - Mounted elements are captured in LastMountedElements
///   - Modified elements are captured in LastModifiedElements
///   - Lists are empty when the flag is off
///   - Containers don't appear as modified when only children change
///   - Update regression: elements go through Update path (not remount)
/// </summary>
internal static class ReconcileHighlightTests
{
    // ── Initial mount populates LastMountedElements ──
    internal class MountCapturesElements(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("highlight-mount-a").AutomationId("hmA"),
                    TextBlock("highlight-mount-b").AutomationId("hmB")
                ));

                await Harness.Render();

                var mounted = host.Reconciler.LastMountedElements;
                H.Check("ReconcileHighlight_MountCaptures_NonEmpty",
                    mounted.Count > 0);

                // The VStack (StackPanel) + 2 TextBlocks = at least 3 mounted elements.
                H.Check("ReconcileHighlight_MountCaptures_AtLeast3",
                    mounted.Count >= 3);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Property update populates LastModifiedElements ──
    internal class UpdateCapturesModified(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (text, setText) = ctx.UseState("before");
                    return VStack(
                        TextBlock(text).AutomationId("hlText"),
                        Button("toggle", () => setText("after"))
                    );
                });

                await Harness.Render();

                // After initial mount, all elements are in mounted list.
                var mountedInitial = host.Reconciler.LastMountedElements;
                H.Check("ReconcileHighlight_UpdateCaptures_InitialMountNonEmpty",
                    mountedInitial.Count > 0);

                // Trigger a state change — this should cause an update (not remount).
                H.ClickButton("toggle");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;
                H.Check("ReconcileHighlight_UpdateCaptures_ModifiedNonEmpty",
                    modified.Count > 0);

                // The TextBlock whose text changed should be in the modified list.
                var modifiedTb = H.FindControl<TextBlock>(tb =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(tb) == "hlText");
                H.Check("ReconcileHighlight_UpdateCaptures_TextBlockModified",
                    modifiedTb is not null && modified.Contains(modifiedTb));
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Lists are empty when the flag is off ──
    internal class NoCaptureWhenFlagOff(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = false;

                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("no-capture-test")
                ));

                await Harness.Render();

                H.Check("ReconcileHighlight_FlagOff_MountedEmpty",
                    host.Reconciler.LastMountedElements.Count == 0);

                H.Check("ReconcileHighlight_FlagOff_ModifiedEmpty",
                    host.Reconciler.LastModifiedElements.Count == 0);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Containers are NOT marked modified when only children change ──
    internal class ContainerNotModifiedWhenOnlyChildrenChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (text, setText) = ctx.UseState("alpha");
                    return VStack(
                        TextBlock(text).AutomationId("containerTestText"),
                        Button("change", () => setText("beta"))
                    );
                });

                await Harness.Render();

                // Trigger update — only TextBlock text changes, VStack's own props are stable.
                H.ClickButton("change");
                await Harness.Render();

                var modified = host.Reconciler.LastModifiedElements;

                // The TextBlock should be captured (its text changed).
                var tb = H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "containerTestText");
                H.Check("ReconcileHighlight_ContainerFilter_TextBlockCaptured",
                    tb is not null && modified.Contains(tb));

                // The StackPanel wrapper should NOT be captured — only children changed.
                var stackPanels = modified.OfType<StackPanel>().ToList();
                H.Check("ReconcileHighlight_ContainerFilter_StackPanelNotCaptured",
                    stackPanels.Count == 0);
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
        }
    }

    // ── Update regression: re-rendering same element type uses Update, not Mount ──
    // If an element type is missing from the Update switch, it falls through to
    // Mount() which is slower and produces a red (mount) flash instead of yellow (update).
    internal class UpdatePathNotRemount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("v1");
                return VStack(
                    // Test a variety of element types that should all go through Update
                    TextBlock(label).AutomationId("updateRegText"),
                    Viewbox(TextBlock("inside-viewbox")),
                    Button("trigger", () => setLabel("v2"))
                );
            });

            await Harness.Render();
            var createdAfterMount = host.Reconciler.DebugUIElementsCreated;
            H.Check("UpdatePath_InitialMount_CreatedPositive", createdAfterMount > 0);

            // Trigger re-render
            H.ClickButton("trigger");
            await Harness.Render();

            // On update, DebugUIElementsCreated tracks NEW elements.
            // If Update works correctly, no new elements should be created
            // (only DebugUIElementsModified should increase).
            var createdAfterUpdate = host.Reconciler.DebugUIElementsCreated;
            H.Check("UpdatePath_NoNewCreations",
                createdAfterUpdate == 0);

            H.Check("UpdatePath_ModifiedPositive",
                host.Reconciler.DebugUIElementsModified > 0);

            // Verify the TextBlock was updated in-place (same control instance, new text)
            var tb = H.FindControl<TextBlock>(t =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "updateRegText");
            H.Check("UpdatePath_TextBlockUpdated",
                tb is not null && tb.Text == "v2");
        }
    }

    // ── MenuFlyout goes through Update path ──
    internal class MenuFlyoutUpdatesInPlace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("Item1");
                return VStack(
                    MenuFlyout(
                        Button("mfTarget", () => setLabel("Item2")),
                        MenuItem(label)
                    )
                );
            });

            await Harness.Render();

            // Trigger update — the menu flyout item label changes.
            H.ClickButton("mfTarget");
            await Harness.Render();

            // No new elements should be created (Update path, not Mount).
            H.Check("MenuFlyoutUpdate_NoNewCreations",
                host.Reconciler.DebugUIElementsCreated == 0);
        }
    }
}
