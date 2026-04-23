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
}
