using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for issue #679 (a) — the ListView <c>OnItemClick</c> "once-fire" guarantee.
///
/// The <c>ItemClick_OnceFire</c> host fixture renders a <c>ListView</c> whose
/// <c>OnItemClick</c> is a fresh lambda every render. These tests force the component to
/// re-render (handler continuously present) and to rebuild its items, then deliver a REAL
/// pointer click to a specific row and assert the callback fired EXACTLY once with the
/// correct index. This guards the <c>ListViewHandler</c> once-subscribe contract against
/// double-fire / double-subscription / stale-handler regressions — a naive
/// <c>ItemClick +=</c>-every-render regression would make one click report <c>Fires &gt; 1</c>.
///
/// Why E2E and not a selftest: WinUI raises <c>ItemClick</c> from real pointer input, not from
/// a UIA Invoke, so only cross-process real-input delivery exercises the full path.
/// </summary>
[TestClass]
public class ItemClickInteractionTests : AppTestBase
{
    private const string Fixture = "ItemClick_OnceFire";

    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Handler stays present across several re-renders (memoized items → no ItemsSource
    /// rebuild). A single real click on row 2 must fire the callback exactly once.
    /// </summary>
    // [Retry] mops up the rare unattended-desktop input-injection flake (SendInput dropped
    // before the Host foregrounds). A real regression still fails every attempt.
    [Retry(3)]
    [TestMethod]
    public void ItemClick_FiresExactlyOnce_AfterReRenders()
    {
        NavigateToFixtureFresh(Fixture);
        WaitForText("LvFires", "Fires: 0");

        // Force re-renders with OnItemClick continuously present. A re-subscribe-per-render
        // regression would stack native ItemClick handlers here.
        ClickButton("LvRerenderBtn");
        ClickButton("LvRerenderBtn");
        ClickButton("LvRerenderBtn");
        WaitForTextContaining("LvRev", "rev: 3");

        RealClick("LvItem_2");

        WaitForText("LvLastIndex", "LastIndex: 2");
        WaitForText("LvFires", "Fires: 1");
    }

    /// <summary>
    /// After the items array is rebuilt (the #495 ItemsSource-rebuild path), a single real
    /// click still fires the callback exactly once with the correct index.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void ItemClick_FiresExactlyOnce_AfterItemsChange()
    {
        NavigateToFixtureFresh(Fixture);
        WaitForText("LvFires", "Fires: 0");

        ClickButton("LvShuffleBtn");
        WaitForTextContaining("LvRev", "shuffle: 1");

        RealClick("LvItem_1");

        WaitForText("LvLastIndex", "LastIndex: 1");
        WaitForText("LvFires", "Fires: 1");
    }

    /// <summary>
    /// Deliver a real Win32 pointer click to the center of the element's bounding rectangle.
    /// A real click (not a UIA Invoke) is required: WinUI raises <c>ListView.ItemClick</c>
    /// from pointer input.
    /// </summary>
    private void RealClick(string automationId)
    {
        var r = FindById(automationId).Rect;
        InputInjector.Foreground(HostHwnd);
        InputInjector.Click(r.X + r.Width / 2, r.Y + r.Height / 2);
    }
}
