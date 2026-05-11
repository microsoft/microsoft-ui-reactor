using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for the validation pit-of-success APIs:
///  • <c>NumberBox.Immediate()</c> — fires <c>OnValueChanged</c> on every
///    parseable keystroke instead of waiting for blur/Enter/spin.
///  • <c>Button.DisabledFocusable()</c> — keeps Submit in the keyboard tab
///    order while dimmed, and drops the user <c>OnClick</c> when active so
///    invalid submits are suppressed.
///
/// These behaviors cannot be reliably exercised from in-process self-tests:
/// programmatically setting <c>NumberBox.Text</c> also triggers the WinUI
/// Value coerce/commit path, which masks the difference between Immediate
/// and non-Immediate wiring. WinAppDriver SendKeys reproduces the real
/// keystroke flow (Text changes per character, no Value commit until blur),
/// so the Immediate behavior is observable here.
/// </summary>
[TestClass]
public class ImmediateAndDisabledFocusableTests : AppTestBase
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
    /// Types a valid age character-by-character into the NumberBox and
    /// verifies the <c>OnValueChanged</c> callback fires *before* the field
    /// is blurred. Without <c>.Immediate()</c>, the callback would only fire
    /// when focus left the control — proven indirectly by observing the
    /// fire-count rise while focus is still inside the NumberBox.
    /// </summary>
    [TestMethod]
    public void Immediate_FiresOnEveryKeystroke_BeforeBlur()
    {
        NavigateToFixtureFresh("Validation_ImmediateAndDisabledFocusable");

        WaitForText("ValImmediate_FireCount", "Fires: 0");
        WaitForText("ValImmediate_AgeDisplay", "Age: 0");

        var ageInput = FindById("ValImmediate_Age");
        ageInput.Click(); // focus

        // Type one digit at a time. The displayed age must update while
        // focus is still inside the NumberBox — that is the keystroke-level
        // Immediate behavior we couldn't observe in-process.
        ageInput.SendKeys("2");
        WaitForTextContaining("ValImmediate_AgeDisplay", "Age: 2", timeoutMs: 3000);

        ageInput.SendKeys("5");
        WaitForText("ValImmediate_AgeDisplay", "Age: 25");

        // FireCount should now be > 0 — confirming OnValueChanged ran before
        // any blur. (Exact count is timing-sensitive across WinUI versions
        // and IME/digit-coalescing, so we only assert "rose above zero".)
        var fires = FindById("ValImmediate_FireCount").Text ?? "";
        Assert.IsFalse(fires.EndsWith(": 0"),
            $"Expected at least one OnValueChanged before blur, got '{fires}'.");
    }

    /// <summary>
    /// Verifies the keyboard-trap fix from issue #231: with a
    /// DisabledFocusable Submit and an Immediate NumberBox, the user can fix
    /// the last invalid field and tab directly to Submit without focus
    /// skipping past it. Compares against the documented original trap:
    /// commit-on-blur input + true-disabled Submit removes the button from
    /// the tab order at the moment Tab navigation runs.
    /// </summary>
    [TestMethod]
    public void DisabledFocusable_TabReachesSubmit_AfterTypingValidValue()
    {
        NavigateToFixtureFresh("Validation_ImmediateAndDisabledFocusable");

        // Pre-fill email so only the age field can flip form validity.
        var email = FindById("ValImmediate_Email");
        email.Click();
        email.SendKeys("user@example.com");

        var age = FindById("ValImmediate_Age");
        age.Click();
        age.SendKeys("25");
        WaitForText("ValImmediate_AgeDisplay", "Age: 25");
        WaitForText("ValImmediate_FormValid", "valid");

        // Tab from the age field should land on Submit (DisabledFocusable
        // doesn't matter once the form is valid; this also verifies the
        // tab graph is intact across the re-render that flipped validity).
        age.SendKeys(Keys.Tab);
        var submit = FindById("ValImmediate_Submit");
        Assert.IsTrue(submit.GetAttribute("HasKeyboardFocus") == "True",
            "Submit should receive keyboard focus after Tab from age field.");

        // Activate via Space (standard button keyboard invoke) — should fire
        // because form is now valid.
        WaitForText("ValImmediate_SubmitCount", "Submits: 0");
        submit.SendKeys(Keys.Space);
        WaitForText("ValImmediate_SubmitCount", "Submits: 1");
    }

    /// <summary>
    /// While the form is invalid, the Submit button must:
    ///  1. Stay reachable via Tab (it's still in the focus tree).
    ///  2. Drop the user OnClick invocation when activated — the click
    ///     trampoline checks <c>IsDisabledFocusable</c> and returns early.
    /// </summary>
    [TestMethod]
    public void DisabledFocusable_InvalidFormDropsInvokes()
    {
        NavigateToFixtureFresh("Validation_ImmediateAndDisabledFocusable");

        WaitForText("ValImmediate_FormValid", "invalid");
        WaitForText("ValImmediate_SubmitCount", "Submits: 0");

        // Clicking the dimmed Submit should NOT increment the counter —
        // the trampoline drops the invoke. (Without DisabledFocusable,
        // .Disabled(true) would make the click target unreachable; with
        // it, the click reaches the trampoline but is suppressed there.)
        var submit = FindById("ValImmediate_Submit");
        submit.Click();

        // Brief observation window: if a stray invoke fires, the count
        // would tick up. Use WaitForText for the *unchanged* value as the
        // assertion — a positive change would surface as a timeout-free
        // re-read on a subsequent assertion path. To make the assertion
        // robust against asynchronous state writes, we re-poll the count
        // after the click and expect it to still be zero.
        Thread.Sleep(300); // give any state write a chance to settle
        var count = FindById("ValImmediate_SubmitCount").Text ?? "";
        Assert.AreEqual("Submits: 0", count,
            "Submit OnClick must be suppressed while DisabledFocusable is on.");
    }
}
