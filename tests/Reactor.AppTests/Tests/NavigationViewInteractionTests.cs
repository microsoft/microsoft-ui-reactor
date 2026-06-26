using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E expand/collapse tests for the hierarchical <c>NavigationView</c>, driving
/// the real WinUI control through winapp ui via the
/// <c>NavigationView_Hierarchical</c> host fixture (which mirrors the
/// ReactorGallery shell: stateful, re-renders on every selection, rebuilds its
/// MenuItems array each render).
///
/// These reproduce the rebuild-clobber bug: before the fix, every selection
/// re-render cleared and recreated all NavigationViewItems, so an expanded
/// category snapped shut on the same click that selected it, and selecting a
/// child collapsed its parent. The fix reconciles menu items in place (matching
/// by Tag) so the container's IsExpanded survives the re-render.
///
/// Unlike the TreeView gesture bug, this one is triggered by the re-render itself
/// (a real SelectionChanged event), so WinAppDriver reproduces it deterministically.
/// </summary>
[TestClass]
public class NavigationViewInteractionTests : AppTestBase
{
    private const string Fixture = "NavigationView_Hierarchical";

    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Clicking a collapsed parent category expands it and the expansion sticks
    /// across the selection-triggered re-render (its children stay visible).
    /// </summary>
    [TestMethod]
    public void ExpandParent_StaysExpandedAfterRerender()
    {
        NavigateToFixtureFresh(Fixture);

        // Parent starts collapsed → children not realized.
        Assert.IsFalse(IsItemPresent("Alpha-1"), "Alpha should start collapsed.");

        ClickItem("Alpha");
        Settle();

        Assert.IsTrue(WaitForItemPresent("Alpha-1"),
            "Expanding 'Alpha' did not stick — its child 'Alpha-1' is not visible after the " +
            "selection re-render (rebuild-clobber regression).");
    }

    /// <summary>
    /// Selecting a child must NOT collapse its parent. Expand "Alpha", click child
    /// "Alpha-1", and verify the sibling "Alpha-2" remains visible (parent stayed
    /// expanded) and the selection updated.
    /// </summary>
    [TestMethod]
    public void SelectChild_DoesNotCollapseParent()
    {
        NavigateToFixtureFresh(Fixture);

        ClickItem("Alpha");
        Settle();
        Assert.IsTrue(WaitForItemPresent("Alpha-2"), "Alpha should be expanded with its children visible.");

        ClickItem("Alpha-1");
        Settle();

        WaitForText("NavSelectedTag", "Selected: alpha-1");
        Assert.IsTrue(IsItemPresent("Alpha-2"),
            "Selecting child 'Alpha-1' collapsed its parent 'Alpha' " +
            "(sibling 'Alpha-2' disappeared) — rebuild-clobber regression.");
    }

    // ── helpers ─────────────────────────────────────────────────────

    private void ClickItem(string itemText) => FindByName(itemText).Click();

    private bool IsItemPresent(string itemText) =>
        App.Search(itemText).Any(m => m.Name == itemText);

    private bool WaitForItemPresent(string itemText, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (IsItemPresent(itemText)) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    private static void Settle() => Thread.Sleep(600);
}
