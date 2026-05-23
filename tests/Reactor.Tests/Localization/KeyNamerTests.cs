using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class KeyNamerTests
{
    [Theory]
    [InlineData("SettingsPage", "Settings")]
    [InlineData("ProductCard", "ProductCard")]
    [InlineData("MainComponent", "Main")]
    [InlineData("LoginView", "Login")]
    [InlineData("HeaderPanel", "Header")]
    [InlineData("ConfirmDialog", "Confirm")]
    [InlineData("AppWindow", "App")]
    [InlineData("App", "App")]
    public void StripClassSuffix_CorrectResults(string className, string expected)
    {
        Assert.Equal(expected, KeyNamer.StripClassSuffix(className));
    }

    [Theory]
    [InlineData("Save", "Save")]
    [InlineData("Add to Cart", "AddToCart")]
    [InlineData("Hello, World!", "HelloWorld")]
    [InlineData("Settings", "Settings")]
    public void GenerateHintFromValue_PascalCase(string value, string expected)
    {
        Assert.Equal(expected, KeyNamer.GenerateHintFromValue(value));
    }

    [Fact]
    public void GenerateHintFromValue_InterpolationPlaceholders_Removed()
    {
        Assert.Equal("Hello", KeyNamer.GenerateHintFromValue("Hello, {name}"));
    }

    [Fact]
    public void AssignKeys_SimpleText_CorrectKeyAndNamespace()
    {
        var extractions = new List<LocalizableString>
        {
            new()
            {
                FilePath = "SettingsPage.cs",
                ClassName = "SettingsPage",
                Context = "Text",
                Value = "Save",
                SpanStart = 100,
                SpanLength = 6,
            }
        };

        var keyed = KeyNamer.AssignKeys(extractions);

        Assert.Single(keyed);
        Assert.Equal("Settings", keyed[0].ReswFileName);
        Assert.Equal("Save", keyed[0].Key);
        Assert.Equal("Save", keyed[0].Value);
    }

    [Fact]
    public void AssignKeys_ToolTipContext_AppendsContextSuffix()
    {
        var extractions = new List<LocalizableString>
        {
            new()
            {
                FilePath = "App.cs",
                ClassName = "App",
                Context = "ToolTip",
                Value = "Copy to clipboard",
                SpanStart = 100,
                SpanLength = 20,
            }
        };

        var keyed = KeyNamer.AssignKeys(extractions);

        Assert.Single(keyed);
        Assert.Equal("CopyToClipboardToolTip", keyed[0].Key);
    }

    [Fact]
    public void AssignKeys_Interpolation_AddsComment()
    {
        var extractions = new List<LocalizableString>
        {
            new()
            {
                FilePath = "Cart.cs",
                ClassName = "CartPage",
                Context = "Text",
                Value = "You have {count} items",
                SpanStart = 100,
                SpanLength = 30,
                IsInterpolation = true,
                ArgumentMap = new Dictionary<string, string> { ["count"] = "items.Count" },
            }
        };

        var keyed = KeyNamer.AssignKeys(extractions);

        Assert.Single(keyed);
        Assert.Contains("auto-extracted from interpolation", keyed[0].Comment);
        Assert.Contains("plural support", keyed[0].Comment!);
    }

    [Fact]
    public void AssignKeys_DuplicateKeys_MadeUnique()
    {
        var extractions = new List<LocalizableString>
        {
            new()
            {
                FilePath = "App.cs", ClassName = "App", Context = "Text",
                Value = "Save", SpanStart = 100, SpanLength = 6,
            },
            new()
            {
                FilePath = "App.cs", ClassName = "App", Context = "Button",
                Value = "Save", SpanStart = 200, SpanLength = 6,
            },
        };

        var keyed = KeyNamer.AssignKeys(extractions);

        Assert.Equal(2, keyed.Count);
        Assert.Equal("Save", keyed[0].Key);
        Assert.Equal("Save2", keyed[1].Key);
    }

    [Fact]
    public void AssignKeys_NamedParamContext_AppendsSuffix()
    {
        var extractions = new List<LocalizableString>
        {
            new()
            {
                FilePath = "CatalogPage.cs",
                ClassName = "CatalogPage",
                Context = "TextBox.placeholder",
                Value = "Search products...",
                SpanStart = 100,
                SpanLength = 20,
            }
        };

        var keyed = KeyNamer.AssignKeys(extractions);

        Assert.Single(keyed);
        Assert.Equal("Catalog", keyed[0].ReswFileName);
        Assert.Equal("SearchProductsPlaceholder", keyed[0].Key);
    }
}
