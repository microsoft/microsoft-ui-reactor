using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public class ItemsHostGestureTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    [TestMethod]
    [E2eRetry(3)]
    public void SkippedItemPanUsesLatestCallback()
    {
        NavigateToFixtureFresh("ItemsHost_SkippedGesture");
        WaitForText("ItemsHostVersion", "version:1");
        Pan();
        WaitForText("ItemsHostTapResult", "pan:1");
        App.Click("ItemsHostRefresh");
        WaitForText("ItemsHostVersion", "version:2");
        Pan();
        WaitForText("ItemsHostTapResult", "pan:2");

        void Pan()
        {
            var rect = FindById("ItemsHostTapTarget").Rect;
            App.Drag("ItemsHostTapTarget", $"{rect.X + rect.Width / 2 + 60},{rect.Y + rect.Height / 2 + 30}");
        }
    }
}
