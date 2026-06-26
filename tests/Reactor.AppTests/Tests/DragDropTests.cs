using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E tests for spec 027 Tier 6 drag-and-drop. Real mouse drags are synthesized via the
/// Win32 <see cref="InputInjector"/> fallback (winapp ui has no drag verb) across the host
/// fixtures declared in <c>DragDropE2EFixtures.cs</c>.
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
    // [Retry] mops up the rare unattended-desktop input-injection flake: Win32 SendInput is
    // occasionally dropped before the Host window foregrounds on CI. A real regression still
    // fails every attempt. Removable once winappCli #562 (send-keys)/#498 (drag) ship native verbs.
    [Retry(3)]
    [TestMethod]
    public void DragDrop_TypedReorder_MovesCard()
    {
        NavigateToFixtureFresh("DragDrop_TypedReorder");

        WaitForText("Col_Todo_Count", "Count:1");
        WaitForText("Col_Done_Count", "Count:0");

        var c = Center(FindById("Card_c1").Rect);
        var d = Center(FindById("Col_Done").Rect);

        // Intermediate moves force WinUI to observe continuous pointer motion beyond its
        // drag-detection threshold; a single jump is too abrupt and the drag never starts.
        InputInjector.Foreground(HostHwnd);
        InputInjector.Drag(new[]
        {
            c, (c.X + 8, c.Y), (c.X + 16, c.Y), (c.X + 36, c.Y),
            ((c.X + d.X) / 2, (c.Y + d.Y) / 2), d,
        });

        // After a successful Move the source column should shrink and the target grow.
        WaitForText("Col_Done_Count", "Count:1", timeoutMs: 6000);
        WaitForText("Col_Todo_Count", "Count:0", timeoutMs: 6000);
    }

    /// <summary>
    /// Cancelled drag — drop outside any valid target. The source column should still
    /// have the card (move-on-confirmation guarantees the source doesn't optimistically
    /// remove it, and WasCancelled → CompletedOperation = None).
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void DragDrop_CancelledDrag_LeavesSourceIntact()
    {
        NavigateToFixtureFresh("DragDrop_TypedReorder");

        WaitForText("Col_Todo_Count", "Count:1");

        var c = Center(FindById("Card_c1").Rect);

        // Drag into empty space and release — no target accepts, drop is cancelled.
        InputInjector.Foreground(HostHwnd);
        InputInjector.Drag(new[]
        {
            c, (c.X + 8, c.Y), (c.X + 16, c.Y), (c.X + 200, c.Y),
            (c.X + 400, c.Y), (c.X + 400, c.Y - 200), (c.X + 400, c.Y - 400),
        });

        // Source still has the card.
        WaitForText("Col_Todo_Count", "Count:1");
        WaitForText("Col_Done_Count", "Count:0");
    }

    /// <summary>
    /// Text format round-trip — drag a control that writes text to the DataPackage
    /// onto a target that reads it via <c>TryGetText</c>.
    /// </summary>
    [Retry(3)]
    [TestMethod]
    public void DragDrop_TextFormat_RoundTrip()
    {
        NavigateToFixtureFresh("DragDrop_TextFormat");

        WaitForText("TextDropResult", "Dropped: (none)");

        var s = Center(FindById("TextDragSource").Rect);
        var t = Center(FindById("TextDropZone").Rect);

        InputInjector.Foreground(HostHwnd);
        InputInjector.Drag(new[]
        {
            s, (s.X + 8, s.Y), (s.X + 16, s.Y), (s.X + 36, s.Y),
            ((s.X + t.X) / 2, (s.Y + t.Y) / 2), t,
        });

        WaitForText("TextDropResult", "Dropped: dragged-text", timeoutMs: 6000);
    }
}

