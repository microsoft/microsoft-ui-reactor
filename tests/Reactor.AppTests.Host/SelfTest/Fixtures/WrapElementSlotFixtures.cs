using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Exercises the generated <c>[WrapElementSlot]</c> bridge end-to-end against a live
/// WinUI <see cref="TabView"/> — the framework control whose <c>TabStripHeader</c> slot
/// is declared with the attribute. Covers the three transitions the generated
/// <c>ImperativeBridged</c> entry must handle:
/// <list type="number">
///   <item>mount — the slot element materializes onto the control property,</item>
///   <item>update — a content change reconciles in place (same control reused),</item>
///   <item>removal — setting the slot to <c>null</c> clears the control property.</item>
/// </list>
/// </summary>
internal static class WrapElementSlotFixtures
{
    internal class TabStripHeaderMountUpdateRemove(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                Element? header = phase switch
                {
                    0 => TextBlock("slot-v1"),
                    1 => TextBlock("slot-v2"),
                    _ => null,
                };
                var tab = TabView(new TabViewItemData("Tab1", TextBlock("body")))
                    with { TabStripHeader = header };
                return VStack(Button("AdvanceSlot", () => set(phase + 1)), tab);
            });

            await Harness.Render();

            var tabView = H.FindControl<TabView>(_ => true);
            H.Check("WrapElementSlot_TabViewMounted", tabView is not null);
            // The check above already reports the miss; throwing would truncate the TAP
            // stream and hide every later check (issue #998).
            if (tabView is null)
                return;

            // Phase 0 — mount: the slot element is on the control property.
            var header0 = tabView.TabStripHeader as TextBlock;
            H.Check("WrapElementSlot_HeaderMounted", header0 is not null && header0.Text == "slot-v1");

            // Phase 1 — update: content changes; the SAME TextBlock is reused (state-preserving
            // reconcile, not remount).
            H.ClickButton("AdvanceSlot");
            await Harness.Render();
            var header1 = tabView.TabStripHeader as TextBlock;
            H.Check("WrapElementSlot_HeaderUpdatedInPlace",
                header1 is not null && header1.Text == "slot-v2" && ReferenceEquals(header0, header1));

            // Phase 2 — removal: the slot goes null; the control property is cleared.
            H.ClickButton("AdvanceSlot");
            await Harness.Render();
            H.Check("WrapElementSlot_HeaderRemoved", tabView.TabStripHeader is null);
        }
    }
}
