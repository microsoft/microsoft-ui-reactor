using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Interactions;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for spec 027 Tier 6 drag-and-drop. WinAppDriver drives real mouse
/// drags across the host fixtures declared in <c>DragDropE2EFixtures.cs</c>.
/// </summary>
[TestClass]
public class DragDropTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Typed drag-and-drop — a card moves from the Todo column to the Done column
    /// using a <c>.OnDragStart&lt;_, CardPayload&gt;</c> source and a matching
    /// <c>.OnDrop&lt;_, CardPayload&gt;</c> target. Relies on the move-on-confirmation
    /// contract: the source only removes the card after <c>DropCompleted</c> reports Move.
    /// </summary>
    [TestMethod]
    public void DragDrop_TypedReorder_MovesCard()
    {
        NavigateToFixtureFresh("DragDrop_TypedReorder");

        WaitForText("Col_Todo_Count", "Count:1");
        WaitForText("Col_Done_Count", "Count:0");

        var card = FindById("Card_c1");
        var doneColumn = FindById("Col_Done");

        // Intermediate MoveByOffset forces WinUI to observe continuous pointer motion
        // beyond its drag-detection threshold; a single MoveToElement is too abrupt
        // and the drag gesture never kicks in under synthesized Appium events.
        new Actions(Session)
            .ClickAndHold(card)
            .MoveByOffset(20, 0).MoveByOffset(20, 0)
            .MoveToElement(doneColumn)
            .Release()
            .Perform();

        // After a successful Move the source column should shrink and the target grow.
        WaitForText("Col_Done_Count", "Count:1", timeoutMs: 6000);
        WaitForText("Col_Todo_Count", "Count:0", timeoutMs: 6000);
    }

    /// <summary>
    /// Cancelled drag — drop outside any valid target. The source column should still
    /// have the card (move-on-confirmation guarantees the source doesn't optimistically
    /// remove it, and WasCancelled → CompletedOperation = None).
    /// </summary>
    [TestMethod]
    public void DragDrop_CancelledDrag_LeavesSourceIntact()
    {
        NavigateToFixtureFresh("DragDrop_TypedReorder");

        WaitForText("Col_Todo_Count", "Count:1");

        var card = FindById("Card_c1");

        // Drag into empty space and release — no target accepts, drop is cancelled.
        new Actions(Session)
            .ClickAndHold(card)
            .MoveByOffset(400, 0)
            .MoveByOffset(0, -400)
            .Release()
            .Perform();

        // Source still has the card.
        WaitForText("Col_Todo_Count", "Count:1");
        WaitForText("Col_Done_Count", "Count:0");
    }

    /// <summary>
    /// Text format round-trip — drag a control that writes text to the DataPackage
    /// onto a target that reads it via <c>TryGetText</c>.
    /// </summary>
    [TestMethod]
    public void DragDrop_TextFormat_RoundTrip()
    {
        NavigateToFixtureFresh("DragDrop_TextFormat");

        WaitForText("TextDropResult", "Dropped: (none)");

        var source = FindById("TextDragSource");
        var target = FindById("TextDropZone");

        // See TypedReorder_MovesCard for why intermediate MoveByOffset is required.
        new Actions(Session)
            .ClickAndHold(source)
            .MoveByOffset(20, 0).MoveByOffset(20, 0)
            .MoveToElement(target)
            .Release()
            .Perform();

        WaitForText("TextDropResult", "Dropped: dragged-text", timeoutMs: 6000);
    }
}
