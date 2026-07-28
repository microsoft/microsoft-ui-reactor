using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed class InfrastructureHelperTests
{
    [TestMethod]
    public void WinAppUi_BuildArgs_AppendsWindowAndJsonAfterVerbArgs()
    {
        var args = WinAppUi.BuildArgs("set-value", 1234, "Name With Spaces", "-p", "Value");

        CollectionAssert.AreEqual(
            new[] { "set-value", "Name With Spaces", "-p", "Value", "-w", "1234", "--json" },
            args);
    }

    [TestMethod]
    public void WinAppUi_ParseJson_RejectsEmptyAndMalformedOutput()
    {
        AssertThrowsWinAppException(() => WinAppUi.ParseJson(1, "", "missing output").Dispose());
        AssertThrowsWinAppException(() => WinAppUi.ParseJson(1, "{not-json", "").Dispose());
    }

    [TestMethod]
    public void WinAppUi_FindFirstEditableSelector_WalksNestedInspectTree()
    {
        using var doc = JsonDocument.Parse(
            """
            [
              {
                "type": "Group",
                "children": [
                  { "type": "Text", "selector": "label-1" },
                  {
                    "type": "DataItem",
                    "children": [
                      { "type": "Edit", "selector": "editor-42" }
                    ]
                  }
                ]
              }
            ]
            """);

        Assert.AreEqual("editor-42", WinAppUi.FindFirstEditableSelector(doc.RootElement));
    }

    [TestMethod]
    public void SendKeysTokens_LiteralText_BecomesEscapedTextToken()
    {
        Assert.AreEqual("text=hello", UiElement.ToSendKeysTokens("hello"));
        // Spaces inside a literal run must be escaped so the whitespace tokenizer keeps the run intact.
        Assert.AreEqual(@"text=hi\sthere", UiElement.ToSendKeysTokens("hi there"));
        // A literal backslash is doubled so it survives the text= escape decoder.
        Assert.AreEqual(@"text=a\\b", UiElement.ToSendKeysTokens(@"a\b"));
    }

    [TestMethod]
    public void SendKeysTokens_Sentinels_BecomeNamedKeys()
    {
        Assert.AreEqual("tab", UiElement.ToSendKeysTokens(Keys.Tab));
        Assert.AreEqual("enter", UiElement.ToSendKeysTokens(Keys.Enter));
        Assert.AreEqual("space", UiElement.ToSendKeysTokens(Keys.Space));
        // A literal run flushes before the following sentinel -> two whitespace-separated tokens.
        Assert.AreEqual("text=42 enter", UiElement.ToSendKeysTokens("42" + Keys.Enter));
    }

    [TestMethod]
    public void SendKeysTokens_ModifierSentinel_BindsToNextKeyAsChord()
    {
        // Control sentinel + 'a' -> a single "ctrl+a" chord (mirrors the old per-keystroke modifier hold).
        Assert.AreEqual("ctrl+a", UiElement.ToSendKeysTokens(Keys.Control + "a"));
    }

    [TestMethod]
    public void SendKeysTokens_DanglingModifier_BecomesBareVkTap()
    {
        // A modifier sentinel with no following key can't chord; it must NOT vanish. It becomes a bare
        // vk= tap (VK_CONTROL 0x11 / VK_SHIFT 0x10), matching the old InputInjector's press+release.
        Assert.AreEqual("vk=0x11", UiElement.ToSendKeysTokens(Keys.Control));
        Assert.AreEqual("vk=0x10", UiElement.ToSendKeysTokens(Keys.Shift));
        // A preceding literal run still flushes to its own text= token before the dangling-modifier tap.
        Assert.AreEqual("text=x vk=0x11", UiElement.ToSendKeysTokens("x" + Keys.Control));
    }

    private static void AssertThrowsWinAppException(Action action)
    {
        try
        {
            action();
        }
        catch (WinAppException)
        {
            return;
        }

        Assert.Fail("Expected WinAppException.");
    }
}
