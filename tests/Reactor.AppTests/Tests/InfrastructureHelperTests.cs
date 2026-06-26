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
    public void InputInjector_NormalizeAbsoluteCoordinates_HandlesVirtualScreenOriginAndBounds()
    {
        Assert.AreEqual((0, 0), InputInjector.NormalizeAbsoluteCoordinates(-1920, -100, -1920, -100, 3840, 2160));
        Assert.AreEqual((65535, 65535), InputInjector.NormalizeAbsoluteCoordinates(1919, 2059, -1920, -100, 3840, 2160));
        Assert.AreEqual((0, 65535), InputInjector.NormalizeAbsoluteCoordinates(-5000, 5000, -1920, -100, 3840, 2160));
    }

    [TestMethod]
    public void InputInjector_DragPath_ClearsThresholdBeforeTarget()
    {
        CollectionAssert.AreEqual(
            new[] { (10, 20), (18, 20), (26, 20), (100, 200) },
            InputInjector.DragPath(10, 20, 100, 200).ToArray());
    }

    [TestMethod]
    public void InputInjector_KeyTokens_DistinguishModifiersFromPressKeys()
    {
        Assert.IsTrue(InputInjector.TryMapKeyToken(Keys.Shift[0], out _, out var isShiftModifier));
        Assert.IsTrue(isShiftModifier);

        Assert.IsTrue(InputInjector.TryMapKeyToken(Keys.Tab[0], out _, out var isTabModifier));
        Assert.IsFalse(isTabModifier);
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
