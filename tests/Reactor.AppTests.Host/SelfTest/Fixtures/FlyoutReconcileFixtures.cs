using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinUI   = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for the Flyout-on-button reconcile fix
/// (companion to <see cref="Issue343Fixtures"/>, which covers the
/// equivalent CommandBar / TeachingTip Content gap).
///
/// <c>DropDownButtonElement.Flyout</c>, <c>SplitButtonElement.Flyout</c>,
/// and <c>ToggleSplitButtonElement.Flyout</c> were attached by Mount but
/// never reconciled by Update, so dynamic flyout content (the children
/// inside a <c>ContentFlyoutElement</c>, or a <c>MenuFlyoutContentElement</c>'s
/// item list) stayed frozen at first-mount values across re-renders.
///
/// Each fixture mounts a button whose flyout depends on a
/// <c>UseState</c> counter, bumps the counter via a sibling button, and
/// asserts the WinUI flyout content reflects the updated value. Before
/// the fix the post-update flyout equals the mount-time flyout; after
/// the fix it tracks the new state. The realized WinUI Flyout instance
/// stays the same — <c>ApplyFlyoutAttachment</c> reuses it via
/// <c>UpdateFlyoutInPlace</c>, which is what preserves an already-open
/// flyout across re-renders.
/// </summary>
internal static class FlyoutReconcileFixtures
{
    // ── DropDownButtonElement.Flyout (MenuFlyout, text-mutating) ──────────

    internal class DropDownButtonFlyoutUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                // MenuFlyout with a single item whose Text encodes the tick.
                var ddb = new DropDownButtonElement("Menu")
                {
                    Flyout = new MenuFlyoutContentElement(
                    [
                        new MenuFlyoutItemData($"item-tick = {tick}"),
                    ]),
                };
                return VStack(
                    Button("FlyoutReconcile_BumpDDB", () => setTick(tick + 1)),
                    ddb
                );
            });

            await Harness.Render();

            var realized = H.FindControl<WinUI.DropDownButton>(_ => true);
            H.Check("FlyoutReconcile_DDB_Mounted", realized is not null);

            var initialFlyout = realized?.Flyout as WinUI.MenuFlyout;
            H.Check("FlyoutReconcile_DDB_InitialFlyoutAttached",
                initialFlyout is not null && initialFlyout.Items.Count == 1);

            var initialItem = initialFlyout?.Items[0] as WinUI.MenuFlyoutItem;
            H.Check("FlyoutReconcile_DDB_InitialItemText",
                initialItem is not null && initialItem.Text == "item-tick = 0");

            // Bump three times — covers both reconcile-after-first-update
            // and stable behaviour across repeated updates.
            for (int i = 0; i < 3; i++)
            {
                H.ClickButton("FlyoutReconcile_BumpDDB");
                await Harness.Render();
            }

            var realizedAfter = H.FindControl<WinUI.DropDownButton>(_ => true);
            H.Check("FlyoutReconcile_DDB_SameInstance",
                realized is not null && ReferenceEquals(realized, realizedAfter));

            // ApplyFlyoutAttachment routes through UpdateFlyoutInPlace, so
            // the WinUI MenuFlyout instance is preserved — an open flyout
            // would stay open while its items mutate underneath it.
            var flyoutAfter = realizedAfter?.Flyout as WinUI.MenuFlyout;
            H.Check("FlyoutReconcile_DDB_FlyoutSameInstance",
                initialFlyout is not null && ReferenceEquals(initialFlyout, flyoutAfter));

            var itemAfter = flyoutAfter?.Items[0] as WinUI.MenuFlyoutItem;
            H.Check("FlyoutReconcile_DDB_ItemTextReconciled",
                itemAfter is not null && itemAfter.Text == "item-tick = 3");
        }
    }

    // ── SplitButtonElement.Flyout (MenuFlyout, text-mutating) ─────────────

    internal class SplitButtonFlyoutUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var sb = new SplitButtonElement("Save")
                {
                    Flyout = new MenuFlyoutContentElement(
                    [
                        new MenuFlyoutItemData($"split-tick = {tick}"),
                    ]),
                };
                return VStack(
                    Button("FlyoutReconcile_BumpSB", () => setTick(tick + 1)),
                    sb
                );
            });

            await Harness.Render();

            var realized = H.FindControl<WinUI.SplitButton>(_ => true);
            H.Check("FlyoutReconcile_SB_Mounted", realized is not null);

            var initialFlyout = realized?.Flyout as WinUI.MenuFlyout;
            var initialItem   = initialFlyout?.Items[0] as WinUI.MenuFlyoutItem;
            H.Check("FlyoutReconcile_SB_InitialItemText",
                initialItem is not null && initialItem.Text == "split-tick = 0");

            H.ClickButton("FlyoutReconcile_BumpSB");
            await Harness.Render();
            H.ClickButton("FlyoutReconcile_BumpSB");
            await Harness.Render();

            var flyoutAfter = (H.FindControl<WinUI.SplitButton>(_ => true)?.Flyout) as WinUI.MenuFlyout;
            H.Check("FlyoutReconcile_SB_FlyoutSameInstance",
                initialFlyout is not null && ReferenceEquals(initialFlyout, flyoutAfter));
            var itemAfter = flyoutAfter?.Items[0] as WinUI.MenuFlyoutItem;
            H.Check("FlyoutReconcile_SB_ItemTextReconciled",
                itemAfter is not null && itemAfter.Text == "split-tick = 2");
        }
    }

    // ── ToggleSplitButtonElement.Flyout (MenuFlyout, text-mutating) ───────

    internal class ToggleSplitButtonFlyoutUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var tsb = new ToggleSplitButtonElement("Bold")
                {
                    Flyout = new MenuFlyoutContentElement(
                    [
                        new MenuFlyoutItemData($"toggle-tick = {tick}"),
                    ]),
                };
                return VStack(
                    Button("FlyoutReconcile_BumpTSB", () => setTick(tick + 1)),
                    tsb
                );
            });

            await Harness.Render();

            var realized = H.FindControl<WinUI.ToggleSplitButton>(_ => true);
            H.Check("FlyoutReconcile_TSB_Mounted", realized is not null);

            var initialFlyout = realized?.Flyout as WinUI.MenuFlyout;
            var initialItem   = initialFlyout?.Items[0] as WinUI.MenuFlyoutItem;
            H.Check("FlyoutReconcile_TSB_InitialItemText",
                initialItem is not null && initialItem.Text == "toggle-tick = 0");

            H.ClickButton("FlyoutReconcile_BumpTSB");
            await Harness.Render();
            H.ClickButton("FlyoutReconcile_BumpTSB");
            await Harness.Render();
            H.ClickButton("FlyoutReconcile_BumpTSB");
            await Harness.Render();

            var flyoutAfter = (H.FindControl<WinUI.ToggleSplitButton>(_ => true)?.Flyout) as WinUI.MenuFlyout;
            H.Check("FlyoutReconcile_TSB_FlyoutSameInstance",
                initialFlyout is not null && ReferenceEquals(initialFlyout, flyoutAfter));
            var itemAfter = flyoutAfter?.Items[0] as WinUI.MenuFlyoutItem;
            H.Check("FlyoutReconcile_TSB_ItemTextReconciled",
                itemAfter is not null && itemAfter.Text == "toggle-tick = 3");
        }
    }

    // ── Items length changes are reconciled too ───────────────────────────

    /// <summary>
    /// Belt-and-braces: dynamic *count* changes go through
    /// <c>UpdateFlyoutInPlace</c>'s clear+repopulate path on
    /// <c>MenuFlyout.Items</c>, not just text mutations on a stable item.
    /// This is the original Radiant repro — a button whose flyout
    /// enumerates a state-derived list.
    /// </summary>
    internal class DropDownButtonFlyoutItemsGrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(1);
                var items = new global::Microsoft.UI.Reactor.Core.MenuFlyoutItemBase[count];
                for (int i = 0; i < count; i++)
                    items[i] = new MenuFlyoutItemData($"row-{i}");

                var ddb = new DropDownButtonElement("Grow")
                {
                    Flyout = new MenuFlyoutContentElement(items),
                };
                return VStack(
                    Button("FlyoutReconcile_AddRow", () => setCount(count + 1)),
                    ddb
                );
            });

            await Harness.Render();

            var realized = H.FindControl<WinUI.DropDownButton>(_ => true);
            var flyout0  = realized?.Flyout as WinUI.MenuFlyout;
            H.Check("FlyoutReconcile_Grow_InitialOne",
                flyout0 is not null && flyout0.Items.Count == 1);

            H.ClickButton("FlyoutReconcile_AddRow");
            await Harness.Render();
            H.ClickButton("FlyoutReconcile_AddRow");
            await Harness.Render();
            H.ClickButton("FlyoutReconcile_AddRow");
            await Harness.Render();

            var flyoutN = (H.FindControl<WinUI.DropDownButton>(_ => true)?.Flyout) as WinUI.MenuFlyout;
            H.Check("FlyoutReconcile_Grow_Reconciled",
                flyoutN is not null && flyoutN.Items.Count == 4);
        }
    }

    // ── ContentFlyoutElement subtree reconciles too ───────────────────────

    /// <summary>
    /// Non-menu flyout: a <c>ContentFlyoutElement</c> wraps an arbitrary
    /// element subtree (here a <c>TextBlock</c>) so it should reconcile
    /// through <c>UpdateFlyoutInPlace</c>'s child-update path.
    /// </summary>
    internal class DropDownButtonContentFlyoutUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var ddb = new DropDownButtonElement("Hint")
                {
                    Flyout = new ContentFlyoutElement(
                        TextBlock($"content-tick = {tick}")
                            .Set(t => t.Name = "FlyoutReconcile_ContentText")),
                };
                return VStack(
                    Button("FlyoutReconcile_BumpContent", () => setTick(tick + 1)),
                    ddb
                );
            });

            await Harness.Render();

            var realized = H.FindControl<WinUI.DropDownButton>(_ => true);
            var initial  = realized?.Flyout as WinUI.Flyout;
            var initialTb = initial?.Content as TextBlock;
            H.Check("FlyoutReconcile_Content_InitialText",
                initialTb is not null && initialTb.Text == "content-tick = 0");

            H.ClickButton("FlyoutReconcile_BumpContent");
            await Harness.Render();
            H.ClickButton("FlyoutReconcile_BumpContent");
            await Harness.Render();

            var after   = (H.FindControl<WinUI.DropDownButton>(_ => true)?.Flyout) as WinUI.Flyout;
            var afterTb = after?.Content as TextBlock;
            H.Check("FlyoutReconcile_Content_FlyoutSameInstance",
                initial is not null && ReferenceEquals(initial, after));
            H.Check("FlyoutReconcile_Content_TextReconciled",
                afterTb is not null && afterTb.Text == "content-tick = 2");
            H.Check("FlyoutReconcile_Content_TextSameInstance",
                initialTb is not null && ReferenceEquals(initialTb, afterTb));
        }
    }

    // ── X → null transitions clear the realized flyout ────────────────────

    /// <summary>
    /// Symmetric to the X→X update path: when a button's flyout transitions
    /// from non-null to null across renders, the realized WinUI control
    /// should drop its <c>.Flyout</c> attachment too. Without this branch,
    /// the user-observable button still pops the stale flyout on click.
    /// </summary>
    internal class DropDownButtonFlyoutClears(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (hasFlyout, setHasFlyout) = ctx.UseState(true);
                var ddb = new DropDownButtonElement("Maybe")
                {
                    Flyout = hasFlyout
                        ? new MenuFlyoutContentElement(
                          [
                              new MenuFlyoutItemData("item"),
                          ])
                        : null,
                };
                return VStack(
                    Button("FlyoutReconcile_Clear", () => setHasFlyout(false)),
                    ddb
                );
            });

            await Harness.Render();

            var realized = H.FindControl<WinUI.DropDownButton>(_ => true);
            H.Check("FlyoutReconcile_Clear_InitiallyAttached",
                realized?.Flyout is WinUI.MenuFlyout);

            H.ClickButton("FlyoutReconcile_Clear");
            await Harness.Render();

            var after = H.FindControl<WinUI.DropDownButton>(_ => true);
            H.Check("FlyoutReconcile_Clear_ButtonSameInstance",
                realized is not null && ReferenceEquals(realized, after));
            H.Check("FlyoutReconcile_Clear_FlyoutDetached",
                after is not null && after.Flyout is null);
        }
    }
}
