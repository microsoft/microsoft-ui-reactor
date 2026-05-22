using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// Spec 045 — E2E fixtures that put real editable TextFields inside docked
/// panes so WinAppDriver/Appium can verify keyboard focus, tab traversal,
/// and the controlled-input contract across docking layout mutations.
/// </summary>
/// <remarks>
/// The matching tests live in <c>tests/Reactor.AppTests/Tests/DockingInputTests.cs</c>.
/// Each pane's TextField mirrors its state into a sibling TextBlock with
/// AutomationId <c>*_State</c> so tests assert against the model rather
/// than reading the TextBox's runtime <c>Text</c> property (which lags
/// behind state in the controlled-input cycle).
/// </remarks>
internal static class DockingInputE2EFixtures
{
    internal class TwoPaneTextFieldComponent : Component
    {
        public override Element Render()
        {
            // Controlled state for both panes. Updates here are the source
            // of truth — the *_State TextBlocks reflect what the test
            // should see after sending keystrokes.
            var (left, setLeft) = UseState(string.Empty);
            var (right, setRight) = UseState(string.Empty);

            // Two side-by-side tool windows. CanPin: true matches the
            // showcase IDE layout shape — that's the configuration that
            // exposed the pin-header rebuild bug in
            // Reconciler.Update.UpdateTabView, so the fixture mirrors it
            // verbatim.
            var leftPane = new ToolWindow
            {
                Title = "Left",
                Key = "dock-input:left",
                CanClose = true,
                CanPin = true,
                Content = VStack(6,
                    TextField(left, setLeft, placeholder: "left input")
                        .AutomationId("DockEditor_Left"),
                    TextBlock($"Left state: {left}").AutomationId("DockEditor_Left_State")
                ).Padding(12),
            };
            var rightPane = new ToolWindow
            {
                Title = "Right",
                Key = "dock-input:right",
                CanClose = true,
                CanPin = true,
                Content = VStack(6,
                    TextField(right, setRight, placeholder: "right input")
                        .AutomationId("DockEditor_Right"),
                    TextBlock($"Right state: {right}").AutomationId("DockEditor_Right_State")
                ).Padding(12),
            };

            // Initial layout: horizontal split, each side a single-pane
            // DockTabGroup. The matrix-drag test confirms the user can
            // drag Right's tab into Left's group to make them tabbed
            // siblings; the test then types into the tabbed pane to
            // verify the §2.30 shape-only override preserves typed
            // state across the layout mutation.
            var initialLayout = new DockSplit(
                Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                new DockNode[]
                {
                    new DockTabGroup(new DockableContent[] { leftPane }),
                    new DockTabGroup(new DockableContent[] { rightPane }),
                });

            return new DockManager
            {
                PersistenceId = "apptest:docking-input-twopane",
                Layout = initialLayout,
            };
        }
    }

    internal static Element TwoPaneTextFieldTest(RenderContext ctx) =>
        Component<TwoPaneTextFieldComponent>();
}
