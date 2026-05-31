using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §4.5 — overlay handler-owned Unmount. Flyout content and Popup
/// child are Reactor subtrees hung off attached SIDE objects (the flyout
/// attached to the target; the WinUI Popup inside the wrapper) that the generic
/// type-switch in UnmountRecursive cannot reach. These fixtures pin that a child
/// Component's <c>UseEffect</c> cleanup fires when the overlay unmounts — i.e.
/// the handler tears down the side-mounted subtree rather than leaking it.
/// </summary>
public static class OverlayTeardownFixtures
{
    private static int s_cleanupCount;

    private sealed class CleanupChild : Component
    {
        public override Element Render()
        {
            UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
            return TextBlock("c");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Flyout — unmounting the Flyout runs its flyout-content Component cleanup.
    // ────────────────────────────────────────────────────────────────────
    internal class Flyout_Unmount_RunsFlyoutContentCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("Toggle", () => setShow(!show)),
                    show
                        ? Flyout(Button("WithFlyout", () => { }), Component<CleanupChild>())
                        : (Element)TextBlock("(hidden)")
                );
            });
            await Harness.Render();

            H.Check("OverlayTeardown_Flyout_NoCleanupBeforeUnmount", s_cleanupCount == 0);

            H.ClickButton("Toggle");
            await Harness.Render();

            H.Check("OverlayTeardown_Flyout_CleanupRan", s_cleanupCount == 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Flyout — unmounting also runs the OverlayInputPassThroughElement
    //  Component cleanup (a second side-mounted subtree, distinct from Content).
    // ────────────────────────────────────────────────────────────────────
    internal class Flyout_Unmount_RunsPassThroughCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("Toggle", () => setShow(!show)),
                    show
                        ? Flyout(Button("WithFlyout", () => { }), TextBlock("content"))
                            .OverlayInputPassThroughElement(Component<CleanupChild>())
                        : (Element)TextBlock("(hidden)")
                );
            });
            await Harness.Render();

            H.Check("OverlayTeardown_FlyoutPassThrough_NoCleanupBeforeUnmount", s_cleanupCount == 0);

            H.ClickButton("Toggle");
            await Harness.Render();

            H.Check("OverlayTeardown_FlyoutPassThrough_CleanupRan", s_cleanupCount == 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Popup — unmounting the Popup runs its child Component cleanup.
    // ────────────────────────────────────────────────────────────────────
    internal class Popup_Unmount_RunsChildCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("Toggle", () => setShow(!show)),
                    show
                        ? Popup(Component<CleanupChild>(), isOpen: false)
                        : (Element)TextBlock("(hidden)")
                );
            });
            await Harness.Render();

            H.Check("OverlayTeardown_Popup_NoCleanupBeforeUnmount", s_cleanupCount == 0);

            H.ClickButton("Toggle");
            await Harness.Render();

            H.Check("OverlayTeardown_Popup_CleanupRan", s_cleanupCount == 1);
        }
    }
}
