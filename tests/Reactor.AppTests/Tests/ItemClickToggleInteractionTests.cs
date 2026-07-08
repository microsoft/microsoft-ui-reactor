using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for issue #779 — the ListView/GridView <c>OnItemClick</c> "toggle-path"
/// double-subscribe defect.
///
/// <para>ListView/GridView update <b>in place</b>, so toggling <c>OnItemClick</c> off
/// (present→null) then on (null→present) used to leave the Mount-time native
/// <c>ItemClick</c> subscription live AND add a second one on the null→present Update
/// (never removed), so a single real click dispatched the callback twice — and stacked
/// another handler on every further off→on cycle. The fix wires <c>ItemClick</c>
/// unconditionally at Mount and never re-subscribes on Update, so exactly one subscription
/// survives any toggle sequence.</para>
///
/// <para>Each test navigates its fixture, toggles the handler OFF then ON via a real button
/// click, delivers a REAL pointer click to a row, and asserts the callback fired EXACTLY
/// once. Pre-fix these report <c>Fires: 2</c> (red); post-fix <c>Fires: 1</c> (green).</para>
///
/// Why E2E and not a selftest: WinUI raises <c>ItemClick</c> from real pointer input (not a
/// UIA Invoke and with no public raise API), and projected WinRT event subscriptions can't be
/// enumerated in-process, so only cross-process real-input delivery exercises the defect.
/// </summary>
[TestClass]
public class ItemClickToggleInteractionTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// ListView: start with <c>OnItemClick</c> present, toggle it OFF then ON (the
    /// null→present in-place Update that used to add a second native subscription), then a
    /// single real click on a row must fire the callback exactly once.
    /// </summary>
    // [Retry] mops up the rare unattended-desktop input-injection flake (SendInput dropped
    // before the Host foregrounds). A real double-subscribe regression fails every attempt
    // with Fires: 2.
    [Retry(3)]
    [TestMethod]
    public void ListView_ItemClick_FiresExactlyOnce_AfterToggleOffOn()
    {
        NavigateToFixtureFresh("ItemClick_ToggleListView");
        WaitForText("LvToggleState", "HasHandler: True");
        WaitForText("LvToggleFires", "Fires: 0");

        // present → null → present: the toggle sequence that leaked the second handler.
        ClickButton("LvToggleBtn");
        WaitForText("LvToggleState", "HasHandler: False");
        ClickButton("LvToggleBtn");
        WaitForText("LvToggleState", "HasHandler: True");

        RealClick("LvToggleItem_1");

        WaitForText("LvToggleLastIndex", "LastIndex: 1");
        WaitForText("LvToggleFires", "Fires: 1");
    }

    /// <summary>
    /// GridView: symmetric to the ListView case — off→on toggle then a single real click
    /// must fire the callback exactly once.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void GridView_ItemClick_FiresExactlyOnce_AfterToggleOffOn()
    {
        NavigateToFixtureFresh("ItemClick_ToggleGridView");
        WaitForText("GvToggleState", "HasHandler: True");
        WaitForText("GvToggleFires", "Fires: 0");

        ClickButton("GvToggleBtn");
        WaitForText("GvToggleState", "HasHandler: False");
        ClickButton("GvToggleBtn");
        WaitForText("GvToggleState", "HasHandler: True");

        RealClick("GvToggleItem_1");

        WaitForText("GvToggleLastIndex", "LastIndex: 1");
        WaitForText("GvToggleFires", "Fires: 1");
    }

    /// <summary>
    /// Deliver a real Win32 pointer click to the center of the element's bounding rectangle.
    /// A real click (not a UIA Invoke) is required: WinUI raises <c>ItemClick</c> from
    /// pointer input.
    /// </summary>
    private void RealClick(string automationId)
    {
        var r = FindById(automationId).Rect;
        InputInjector.Foreground(HostHwnd);
        InputInjector.Click(r.X + r.Width / 2, r.Y + r.Height / 2);
    }
}
