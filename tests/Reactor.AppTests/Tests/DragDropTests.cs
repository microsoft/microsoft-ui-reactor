using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for spec 027 Tier 6 drag-and-drop. Real mouse drags are synthesized via the
/// native <c>winapp ui drag</c> verb across the host fixtures declared in
/// <c>DragDropE2EFixtures.cs</c>.
/// </summary>
[TestClass]
public class DragDropTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    private static (int X, int Y) Center(Rectangle r) => (r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>
    /// Typed drag-and-drop — a card moves from the Todo column to the Done column
    /// using a <c>.OnDragStart&lt;_, CardPayload&gt;</c> source and a matching
    /// <c>.OnDrop&lt;_, CardPayload&gt;</c> target. Relies on the move-on-confirmation
    /// contract: the source only removes the card after <c>DropCompleted</c> reports Move.
    /// </summary>
    // [E2eRetry] mops up the rare unattended-desktop input-injection flake: the native winapp
    // send-keys/drag verbs are SendInput under the hood and are occasionally dropped before the Host
    // foregrounds on CI. A real regression still fails every attempt; retained pending a few stable CI runs (#652).
    [E2eRetry(3)]
    [TestMethod]
    public void DragDrop_TypedReorder_MovesCard()
    {
        NavigateToFixtureFresh("DragDrop_TypedReorder");

        WaitForText("Col_Todo_Count", "Count:1");
        WaitForText("Col_Done_Count", "Count:0");

        // The native drag verb interpolates the motion internally (crossing WinUI's drag-detection
        // threshold) and re-resolves both element endpoints; --dwell-ms settles on the Done column so
        // its hover-armed drop target latches before the button releases.
        App.Drag("Card_c1", "Col_Done", dwellMs: 350);

        // After a successful Move the source column should shrink and the target grow.
        WaitForText("Col_Done_Count", "Count:1", timeoutMs: 6000);
        WaitForText("Col_Todo_Count", "Count:0", timeoutMs: 6000);
    }

    /// <summary>
    /// Cancelled drag — drop outside any valid target. The source column should still
    /// have the card (move-on-confirmation guarantees the source doesn't optimistically
    /// remove it, and WasCancelled → CompletedOperation = None).
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void DragDrop_CancelledDrag_LeavesSourceIntact()
    {
        NavigateToFixtureFresh("DragDrop_TypedReorder");

        WaitForText("Col_Todo_Count", "Count:1");

        var c = Center(FindById("Card_c1").Rect);

        // Drag into empty space well outside any column and release — no target accepts, so the
        // drop is cancelled. The destination is a screen coordinate (no element lives there).
        App.Drag("Card_c1", $"{c.X + 400},{c.Y - 400}", dwellMs: 200);

        // Source still has the card.
        WaitForText("Col_Todo_Count", "Count:1");
        WaitForText("Col_Done_Count", "Count:0");
    }

    /// <summary>
    /// Text format round-trip — drag a control that writes text to the DataPackage
    /// onto a target that reads it via <c>TryGetText</c>.
    /// </summary>
    [E2eRetry(3)]
    [TestMethod]
    public void DragDrop_TextFormat_RoundTrip()
    {
        NavigateToFixtureFresh("DragDrop_TextFormat");

        WaitForText("TextDropResult", "Dropped: (none)");

        App.Drag("TextDragSource", "TextDropZone", dwellMs: 350);

        WaitForText("TextDropResult", "Dropped: dragged-text", timeoutMs: 6000);
    }
}

