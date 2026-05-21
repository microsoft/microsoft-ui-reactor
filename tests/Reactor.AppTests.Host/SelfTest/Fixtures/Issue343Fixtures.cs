using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Repro for https://github.com/microsoft/microsoft-ui-reactor/issues/343
///
/// CommandBar.Content (and the matching TeachingTip.Content) were assigned by
/// Mount but never reconciled by Update, so data-driven content (e.g. a
/// subtitle that reflects a state value) stayed frozen at its mount-time
/// value across re-renders.
///
/// Each fixture mounts a control whose Content is a TextBlock bound to a
/// useState counter, bumps the counter, and asserts the rendered text reflects
/// the updated value. Before the fix the post-update text equals the
/// mount-time text; after the fix it tracks the new state.
/// </summary>
internal static class Issue343Fixtures
{
    // ── CommandBar.Content is reconciled across re-renders ────────────────

    internal class CommandBarContentUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var bar = CommandBar(primaryCommands: [AppBarButton("Save")])
                    with { Content = TextBlock($"tick = {tick}").Set(t => t.Name = "Issue343_CmdContent") };
                return VStack(
                    Button("Issue343_BumpCmd", () => setTick(tick + 1)),
                    bar
                );
            });

            await Harness.Render();

            var cb = H.FindControl<CommandBar>(_ => true);
            H.Check("Issue343_CmdBar_Mounted", cb is not null);

            var initial = H.FindControl<TextBlock>(t => t.Name == "Issue343_CmdContent");
            H.Check("Issue343_CmdBar_InitialContent",
                initial is not null && initial.Text == "tick = 0");

            H.ClickButton("Issue343_BumpCmd");
            await Harness.Render();
            H.ClickButton("Issue343_BumpCmd");
            await Harness.Render();
            H.ClickButton("Issue343_BumpCmd");
            await Harness.Render();

            var afterBump = H.FindControl<TextBlock>(t => t.Name == "Issue343_CmdContent");
            H.Check("Issue343_CmdBar_ContentReconciled",
                afterBump is not null && afterBump.Text == "tick = 3");

            // Identity is preserved: same TextBlock instance, just with updated Text.
            H.Check("Issue343_CmdBar_ContentSameInstance",
                initial is not null && ReferenceEquals(initial, afterBump));

            // CommandBar itself should not be torn down either.
            var cb2 = H.FindControl<CommandBar>(_ => true);
            H.Check("Issue343_CmdBar_SameInstance",
                cb is not null && ReferenceEquals(cb, cb2));
        }
    }

    // ── TeachingTip.Content is reconciled across re-renders ───────────────

    internal class TeachingTipContentUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                // IsOpen=false keeps the content TextBlock in the tip's
                // Content slot rather than pushed into a Popup, so we can
                // read it directly off the TeachingTip control.
                var tip = new TeachingTipElement("Hint")
                {
                    Content = TextBlock($"tip-tick = {tick}"),
                };
                return VStack(
                    Button("Issue343_BumpTip", () => setTick(tick + 1)),
                    tip
                );
            });

            await Harness.Render();

            var ttControl = H.FindControl<Microsoft.UI.Xaml.Controls.TeachingTip>(_ => true);
            H.Check("Issue343_Tip_Mounted", ttControl is not null);
            var initialContent = ttControl?.Content as TextBlock;
            H.Check("Issue343_Tip_InitialContent",
                initialContent is not null && initialContent.Text == "tip-tick = 0");

            H.ClickButton("Issue343_BumpTip");
            await Harness.Render();
            H.ClickButton("Issue343_BumpTip");
            await Harness.Render();

            var ttControlAfter = H.FindControl<Microsoft.UI.Xaml.Controls.TeachingTip>(_ => true);
            var afterContent = ttControlAfter?.Content as TextBlock;
            H.Check("Issue343_Tip_ContentReconciled",
                afterContent is not null && afterContent.Text == "tip-tick = 2");
            H.Check("Issue343_Tip_ContentSameInstance",
                initialContent is not null && ReferenceEquals(initialContent, afterContent));
        }
    }
}
