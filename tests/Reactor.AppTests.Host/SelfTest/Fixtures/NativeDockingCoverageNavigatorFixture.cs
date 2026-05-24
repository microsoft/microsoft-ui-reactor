using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.10 — coverage fixtures for <see cref="DockNavigatorPopup"/>.
/// Exercises the For() cache, OpenOrAdvance with empty inputs, the
/// SeedForTest fast-path, CommitForTest, CancelForTest, and CleanupFor.
/// </summary>
internal static class NativeDockingCoverageNavigatorFixtures
{
    /// <summary>
    /// For(host) returns the same instance on a second call (CWT-cached).
    /// </summary>
    internal class Navigator_ForHost_ReturnsCachedInstance(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = new Border();
            H.SetContent(host);
            await Harness.Render();

            var a = DockNavigatorPopup.For(host);
            var b = DockNavigatorPopup.For(host);
            H.Check("Navigator_For_SameHost_ReturnsSameInstance", ReferenceEquals(a, b));

            var otherHost = new Border();
            var c = DockNavigatorPopup.For(otherHost);
            H.Check("Navigator_For_DifferentHost_DifferentInstance",
                !ReferenceEquals(a, c));

            DockNavigatorPopup.CleanupFor(host);
            DockNavigatorPopup.CleanupFor(otherHost);
            H.Check("Navigator_CleanupFor_Idempotent", true);
            DockNavigatorPopup.CleanupFor(host); // second call — no-op
            H.Check("Navigator_CleanupFor_NoExisting_IsNoOp", true);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// OpenOrAdvance with an empty entry list early-returns (no popup, no
    /// crash). After SeedForTest, CommitForTest invokes the onCommit with
    /// the selected entry's Key.
    /// </summary>
    internal class Navigator_SeedAndCommit_DeliversSelectedKey(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = new Border();
            H.SetContent(host);
            await Harness.Render();

            var nav = DockNavigatorPopup.For(host);

            // Empty list early-return.
            object? committed = "untouched";
            nav.OpenOrAdvance(Array.Empty<DockNavigatorPopup.Entry>(), 0, 1, k => committed = k);
            H.Check("Navigator_EmptyList_NoCommit", committed is string s && s == "untouched");
            H.Check("Navigator_EmptyList_NotOpen", !nav.IsOpen);

            // Seed entries directly and commit.
            var entries = new[]
            {
                new DockNavigatorPopup.Entry("k1", "First"),
                new DockNavigatorPopup.Entry("k2", "Second"),
                new DockNavigatorPopup.Entry("k3", "Third"),
            };
            object? receivedKey = null;
            nav.SeedForTest(entries, selectedIndex: 1, k => receivedKey = k);
            H.Check("Navigator_Seed_IsOpen", nav.IsOpen);
            H.Check("Navigator_Seed_SelectedEntryReadable",
                nav.SelectedEntry is { } se && (string?)se.Key == "k2");

            nav.CommitForTest();
            H.Check("Navigator_Commit_NotOpen", !nav.IsOpen);
            H.Check("Navigator_Commit_DeliveredKey", (string?)receivedKey == "k2");
            H.Check("Navigator_Commit_SelectedEntryClears",
                nav.SelectedEntry is null);

            // Cancel path doesn't invoke commit.
            object? cancelReceived = "untouched";
            nav.SeedForTest(entries, selectedIndex: 0, k => cancelReceived = k);
            nav.CancelForTest();
            H.Check("Navigator_Cancel_NoCommit",
                cancelReceived is string c && c == "untouched");
            H.Check("Navigator_Cancel_NotOpen", !nav.IsOpen);

            DockNavigatorPopup.CleanupFor(host);
            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Open the navigator via the live OpenOrAdvance path so the global
    /// listener hook + popup positioning code runs against a real
    /// XamlRoot. Selection wraps with consecutive calls.
    /// </summary>
    internal class Navigator_OpenAndAdvance_WrapsSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = new Border { Width = 600, Height = 400 };
            H.SetContent(host);
            await Harness.Render();
            await Harness.Render();

            var nav = DockNavigatorPopup.For(host);
            var entries = new[]
            {
                new DockNavigatorPopup.Entry("a", "Alpha"),
                new DockNavigatorPopup.Entry("b", "Beta"),
                new DockNavigatorPopup.Entry("c", "Gamma"),
            };

            object? commitArg = null;
            bool committed = false;
            nav.OpenOrAdvance(entries, currentIndex: 0, delta: 1,
                onCommit: k => { committed = true; commitArg = k; });
            H.Check("Navigator_Open_IsOpen", nav.IsOpen);
            H.Check("Navigator_Open_InitialSelectionAdvanced",
                nav.SelectedEntry is { } se && (string?)se.Key == "b");

            // Second OpenOrAdvance — already open — advances by delta.
            nav.OpenOrAdvance(entries, currentIndex: 1, delta: 1, onCommit: _ => { });
            H.Check("Navigator_OpenSecond_AdvancesSelection",
                nav.SelectedEntry is { } se2 && (string?)se2.Key == "c");

            // Advance again — wraps.
            nav.OpenOrAdvance(entries, currentIndex: 2, delta: 1, onCommit: _ => { });
            H.Check("Navigator_Open_WrapsSelection",
                nav.SelectedEntry is { } se3 && (string?)se3.Key == "a");

            // Backwards.
            nav.OpenOrAdvance(entries, currentIndex: 0, delta: -1, onCommit: _ => { });
            H.Check("Navigator_Open_NegativeDeltaWraps",
                nav.SelectedEntry is { } se4 && (string?)se4.Key == "c");

            nav.CommitForTest();
            H.Check("Navigator_FinalCommit_FiredOriginalCallback", committed);
            H.Check("Navigator_FinalCommit_DeliveredCurrentKey",
                (string?)commitArg == "c");

            DockNavigatorPopup.CleanupFor(host);
            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// SelectedEntry returns null when the popup is closed and when seeded
    /// with an out-of-range index that wraps to a valid one. Covers the
    /// IsOpen-guarded getter path.
    /// </summary>
    internal class Navigator_SelectedEntry_NullWhenClosed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = new Border();
            H.SetContent(host);
            await Harness.Render();

            var nav = DockNavigatorPopup.For(host);
            H.Check("Navigator_Closed_SelectedEntryNull",
                nav.SelectedEntry is null);

            var entries = new[]
            {
                new DockNavigatorPopup.Entry("only", "Only"),
            };
            // Wrap behaviour: selectedIndex of 5 with 1 entry → wraps to 0.
            nav.SeedForTest(entries, selectedIndex: 5, _ => { });
            H.Check("Navigator_Seed_WrappedIndex",
                nav.SelectedEntry is { } se && (string?)se.Key == "only");

            // Negative index wraps too.
            nav.CancelForTest();
            nav.SeedForTest(entries, selectedIndex: -2, _ => { });
            H.Check("Navigator_Seed_NegativeIndexWraps",
                nav.SelectedEntry is { } se2 && (string?)se2.Key == "only");

            nav.CancelForTest();
            DockNavigatorPopup.CleanupFor(host);
            H.SetContent(null);
            await Harness.Render();
        }
    }
}
