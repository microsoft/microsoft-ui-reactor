using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Interactive tests that use winapp ui (UIA) + Win32 SendInput to simulate real user input.
/// These are the ~4 scenarios where cross-process input injection is the point
/// of the test: button clicks, checkbox toggles, navigation.
///
/// All other tests run in-process via SelfTestBatch at CPU speed.
/// </summary>
[TestClass]
public class InteractiveTests : AppTestBase
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
    /// Counter: click increment/decrement/reset buttons via WinAppDriver,
    /// verify the count display updates through the full UI pipeline.
    /// </summary>
    [TestMethod]
    public void Interactive_Counter()
    {
        NavigateToFixture("Demo_Counter");

        WaitForText("CountDisplay", "Current count: 0");

        ClickButton("IncrementBtn");
        WaitForText("CountDisplay", "Current count: 1");

        ClickButton("IncrementBtn");
        WaitForText("CountDisplay", "Current count: 2");

        ClickButton("DecrementBtn");
        WaitForText("CountDisplay", "Current count: 1");

        ClickButton("ResetBtn");
        WaitForText("CountDisplay", "Current count: 0");
    }

    /// <summary>
    /// Observable: click mutation button via WinAppDriver, verify INPC propagates to UI.
    /// </summary>
    [TestMethod]
    public void Interactive_ObservableMutation()
    {
        NavigateToFixture("Observable_UseObservable_Rerender");

        WaitForText("NameDisplay", "Name: Alice");

        ClickButton("ChangeNameBtn");
        WaitForText("NameDisplay", "Name: Bob");
    }
}
