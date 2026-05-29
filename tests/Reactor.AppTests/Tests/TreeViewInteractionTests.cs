using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Support.UI;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E expand/collapse tests for the legacy text-node <c>TreeView</c>, driving
/// the real WinUI control through WinAppDriver. These exercise the same path as
/// the ReactorGallery "Basic TreeView" card via the <c>TreeView_BasicTextTree</c>
/// host fixture.
///
/// Why E2E and not the headless self-test harness: the bug only reproduces in a
/// real, on-screen window with real pointer input — the headless host lays out
/// at full desired size and does not reproduce the constrained-viewport /
/// container-recycling timing (see
/// <c>c:\temp\treeview-expand-collapse-investigation.md</c> §6). So these tests
/// are the automated repro surface AND the future regression guard.
///
/// How expand/collapse is observed: in node-mode TreeView, a node's children are
/// only realized (and therefore only present in the UIA tree) while that node is
/// expanded. So "is descendant X findable by Name?" is a faithful proxy for "is
/// its ancestor expanded?".
///
/// Gesture under test: <see cref="ClickNodeBody"/> clicks the node's text (the
/// center of the content area) — NOT the expand/collapse chevron on the far left.
/// Per the investigation, the chevron works correctly; clicking the row body is
/// what misbehaves in the live GUI.
///
/// CURRENT STATUS (main, 2026-05): both tests PASS — i.e. a WinAppDriver-injected
/// item-body click does NOT collapse the node, and clicking a child does NOT
/// collapse its parent. That is itself useful signal: the reported collapse does
/// not reproduce under automated pointer injection on a data-pre-expanded tree,
/// which narrows the suspect surface toward human-gesture timing and/or the
/// user-expands-then-it-collapses sequence (investigation §4/§5) rather than the
/// steady-state mount path these tests cover. The tests stand as (a) a live-GUI
/// debugging harness for the gallery path — set REACTOR_TV_TRACE=1 and navigate
/// to TreeView_BasicTextTree — and (b) a regression guard that the steady-state
/// expand/collapse path stays healthy. If a future change makes an item-body or
/// child click collapse the tree, these go red.
/// </summary>
[TestClass]
public class TreeViewInteractionTests : AppTestBase
{
    private const string Fixture = "TreeView_BasicTextTree";

    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Symptom 1: clicking a node's item body (not the chevron) must NOT collapse
    /// it. "Work" starts expanded, so "Report.docx" is visible; after clicking the
    /// "Work" row body the child must remain visible.
    /// </summary>
    [TestMethod]
    public void ClickItemBody_DoesNotCollapseExpandedNode()
    {
        NavigateToFixtureFresh(Fixture);

        // Precondition: Work is expanded → its children are realized.
        AssertNodeVisible("Report.docx", "Work should start expanded.");

        ClickNodeBody("Work");
        Settle();

        // Prove the click actually landed on the row (otherwise a "stayed visible"
        // pass would be vacuous): an item-body click in SelectionMode.Single must
        // select the node.
        Assert.IsTrue(IsNodeSelected("Work"),
            "Item-body click did not register — 'Work' is not selected, so the " +
            "'no collapse' assertion below would be meaningless.");

        AssertNodeVisible(
            "Report.docx",
            "Clicking the 'Work' row body collapsed it (its child 'Report.docx' disappeared). " +
            "The item body should not toggle expansion — only the chevron should.");
    }

    /// <summary>
    /// Symptom 2: clicking a child item must NOT collapse its parent. Click
    /// "Report.docx" (a leaf child of "Work") and verify "Work" stays expanded
    /// (its sibling "Slides.pptx" remains visible).
    /// </summary>
    [TestMethod]
    public void ClickChild_DoesNotCollapseParent()
    {
        NavigateToFixtureFresh(Fixture);

        AssertNodeVisible("Slides.pptx", "Work should start expanded.");

        ClickNodeBody("Report.docx");
        Settle();

        AssertNodeVisible(
            "Slides.pptx",
            "Clicking child 'Report.docx' collapsed its parent 'Work' " +
            "(sibling 'Slides.pptx' disappeared).");
    }

    // ── helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Clicks the body (text/content area) of a tree node, addressed by its text.
    /// The element found by Name is the node's TextBlock, whose center is over the
    /// content — well clear of the chevron on the far left.
    /// </summary>
    private void ClickNodeBody(string nodeText)
    {
        Session.FindElement(MobileBy.Name(nodeText)).Click();
    }

    /// <summary>
    /// Whether the tree node addressed by its text reports UIA selection
    /// (SelectionItem pattern). Used to prove an item-body click registered.
    /// </summary>
    private bool IsNodeSelected(string nodeText)
    {
        try
        {
            return Session.FindElement(MobileBy.Name(nodeText)).Selected;
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private void AssertNodeVisible(string nodeText, string because)
    {
        Assert.IsTrue(WaitForNodePresent(nodeText),
            $"Expected tree node '{nodeText}' to be visible. {because}");
    }

    /// <summary>
    /// Polls until a node with the given Name is present, or the timeout elapses.
    /// Returns whether the node became present.
    /// </summary>
    private bool WaitForNodePresent(string nodeText, int timeoutMs = 4000)
    {
        var wait = new DefaultWait<WindowsDriver<WindowsElement>>(Session)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            PollingInterval = TimeSpan.FromMilliseconds(150),
        };
        wait.IgnoreExceptionTypes(typeof(WebDriverException));
        try
        {
            // DefaultWait keeps polling while the lambda returns the default value
            // (false for bool), so returning false on not-found retries until the
            // timeout; returning true ends the wait successfully.
            return wait.Until(driver =>
            {
                try
                {
                    driver.FindElement(MobileBy.Name(nodeText));
                    return true;
                }
                catch (WebDriverException)
                {
                    return false; // keep polling
                }
            });
        }
        catch (WebDriverTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lets any expand/collapse animation or reconcile-driven "flash" settle so
    /// assertions observe the final state, not a transient mid-toggle frame.
    /// </summary>
    private static void Settle() => Thread.Sleep(600);
}
