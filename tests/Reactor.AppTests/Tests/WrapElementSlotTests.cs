using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for the generated <c>[WrapElementSlot]</c> bridge. These drive a running
/// WinUI3 app via the winapp CLI (winapp ui) and verify, through the real UIA tree, that a
/// secondary single-element slot (TabView.TabStripHeader) mounts onto its dedicated control
/// property, updates in place, and clears when the slot goes null. The in-process selftest
/// (WrapElementSlotFixtures) covers the same transitions against a live control; this tier
/// adds the cross-process UIA proof that the slot content is actually surfaced to the user.
/// </summary>
[TestClass]
public class WrapElementSlotTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context)
    {
        TestSession.AssemblyInit(context);
    }

    [ClassCleanup]
    public static void StopAppSession()
    {
        TestSession.AssemblyCleanup();
    }

    /// <summary>
    /// Mount -> in-place update -> removal of the TabStripHeader slot, observed through UIA:
    /// the slot's TextBlock ("SlotHeader") appears with the mounted text, changes text on
    /// advance, then disappears once the slot is set to null.
    /// </summary>
    [TestMethod]
    public void Interactive_WrapElementSlot_Mounts_Updates_Removes()
    {
        NavigateToFixtureFresh("WrapElementSlot_TabStripHeader");

        // Phase 0 — mount: the slot element is rendered into TabView.TabStripHeader.
        WaitForText("SlotStatus", "Header: slot-v1");
        WaitForText("SlotHeader", "slot-v1");

        // Phase 1 — update: the slot content reconciles in place to the new text.
        ClickButton("AdvanceSlot");
        WaitForText("SlotStatus", "Header: slot-v2");
        WaitForText("SlotHeader", "slot-v2");

        // Phase 2 — removal: the slot goes null; the control property is cleared and the
        // slot element disappears from the UIA tree.
        ClickButton("AdvanceSlot");
        WaitForText("SlotStatus", "Header: none");
        AssertElementAbsent("SlotHeader");
    }

    /// <summary>
    /// Polls until the element with the given AutomationId is no longer in the UIA tree,
    /// failing if it is still present after the timeout.
    /// </summary>
    private static void AssertElementAbsent(string automationId, int timeoutMs = 3000)
    {
        if (!App.WaitForGone(automationId, timeoutMs))
            Assert.Fail($"Expected element '{automationId}' to be absent after slot removal, but it was still present after {timeoutMs}ms.");
    }
}
