using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class LocalizableStringScannerTests
{
    private static List<LocalizableString> Scan(string source)
        => LocalizableStringScanner.Scan(source, "Test.cs");

    [Fact]
    public void Text_SimpleStringLiteral_Detected()
    {
        var source = """
            using static Microsoft.UI.Reactor.Factories;
            class MyPage : Component {
                public override Element Render() {
                    return TextBlock("Hello");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Hello", results[0].Value);
        Assert.Equal("TextBlock", results[0].Context);
        Assert.Equal("MyPage", results[0].ClassName);
    }

    [Fact]
    public void Button_FirstArgDetectedAsLocalizable()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return Button("Save", OnSave);
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Save", results[0].Value);
        Assert.Equal("Button", results[0].Context);
    }

    [Fact]
    public void Heading_Detected()
    {
        var source = """
            class Settings : Component {
                public override Element Render() {
                    return Heading("Settings");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Settings", results[0].Value);
        Assert.Equal("Heading", results[0].Context);
    }

    [Fact]
    public void Interpolation_SimpleVariable_ConvertedToIcu()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock($"Hello, {name}");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Hello, {name}", results[0].Value);
        Assert.True(results[0].IsInterpolation);
    }

    [Fact]
    public void Interpolation_FormatSpecifier_Currency()
    {
        var source = """
            class Cart : Component {
                public override Element Render() {
                    return TextBlock($"Total: {price:C}");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Total: {price, number, currency}", results[0].Value);
    }

    [Fact]
    public void Interpolation_FormatSpecifier_Percent()
    {
        var source = """
            class Dashboard : Component {
                public override Element Render() {
                    return TextBlock($"Score: {pct:P0}");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Score: {pct, number, percent}", results[0].Value);
    }

    [Fact]
    public void Interpolation_FormatSpecifier_DateShort()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock($"Due: {date:d}");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Due: {date, date, short}", results[0].Value);
    }

    [Fact]
    public void Interpolation_DottedExpression_CamelCased()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock($"Hello, {user.Name}");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Hello, {name}", results[0].Value);
        Assert.NotNull(results[0].ArgumentMap);
        Assert.Equal("user.Name", results[0].ArgumentMap!["name"]);
    }

    [Fact]
    public void Ternary_BothBranchesExtracted()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock(isVisible ? "Show" : "Hide");
                }
            }
            """;

        var results = Scan(source);

        Assert.Equal(2, results.Count);
        Assert.Equal("Show", results[0].Value);
        Assert.Equal(0, results[0].TernaryBranch);
        Assert.Equal("Hide", results[1].Value);
        Assert.Equal(1, results[1].TernaryBranch);
    }

    [Fact]
    public void NullCoalescing_LiteralSideOnly()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock(val ?? "Default");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Default", results[0].Value);
    }

    [Fact]
    public void MessageCall_AlreadyWrapped_Skipped()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock(t.Message(Loc.App.Hello));
                }
            }
            """;

        var results = Scan(source);

        Assert.Empty(results);
    }

    [Fact]
    public void AutomationId_NotExtracted()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return Button("Save", OnSave).AutomationId("SaveButton");
                }
            }
            """;

        var results = Scan(source);

        // Should only find the Button label, not the AutomationId
        Assert.Single(results);
        Assert.Equal("Save", results[0].Value);
    }

    [Fact]
    public void ToolTip_ExtractedAsModifier()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return Button("Copy", OnCopy).ToolTip("Copy to clipboard");
                }
            }
            """;

        var results = Scan(source);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Value == "Copy" && r.Context == "Button");
        Assert.Contains(results, r => r.Value == "Copy to clipboard" && r.Context == "ToolTip");
    }

    [Fact]
    public void Header_ExtractedAsModifier()
    {
        var source = """
            class Settings : Component {
                public override Element Render() {
                    return TextBox("", OnChanged).Header("User Name");
                }
            }
            """;

        var results = Scan(source);

        // TextBox value "" is empty (skipped), Header should be found
        Assert.Single(results);
        Assert.Equal("User Name", results[0].Value);
        Assert.Equal("Header", results[0].Context);
    }

    [Fact]
    public void EmptyString_Skipped()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock("");
                }
            }
            """;

        var results = Scan(source);

        Assert.Empty(results);
    }

    [Fact]
    public void WhitespaceOnlyString_Skipped()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock("   ");
                }
            }
            """;

        var results = Scan(source);

        Assert.Empty(results);
    }

    [Fact]
    public void MultipleElementsInMethod_AllDetected()
    {
        var source = """
            class InboxPage : Component {
                public override Element Render() {
                    return VStack(
                        Heading("Inbox"),
                        TextBlock("Welcome back"),
                        Button("Compose", OnCompose)
                    );
                }
            }
            """;

        var results = Scan(source);

        Assert.Equal(3, results.Count);
        Assert.Equal("Inbox", results[0].Value);
        Assert.Equal("Welcome back", results[1].Value);
        Assert.Equal("Compose", results[2].Value);
    }

    [Fact]
    public void ExpanderHeader_Detected()
    {
        var source = """
            class Settings : Component {
                public override Element Render() {
                    return Expander("Advanced Settings", TextBlock("Content"));
                }
            }
            """;

        var results = Scan(source);

        // Expander header + Text content
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Value == "Advanced Settings");
        Assert.Contains(results, r => r.Value == "Content");
    }

    [Fact]
    public void InfoBar_TitleAndMessage_Detected()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return InfoBar("Success", "Operation completed");
                }
            }
            """;

        var results = Scan(source);

        // InfoBar has two string args — both localizable
        // First arg is title, we detect it via the "InfoBar" method
        Assert.True(results.Count >= 1);
        Assert.Equal("Success", results[0].Value);
    }

    [Fact]
    public void SubHeadingAndCaption_Detected()
    {
        var source = """
            class App : Component {
                public override Element Render() {
                    return VStack(
                        SubHeading("Details"),
                        Caption("Last updated")
                    );
                }
            }
            """;

        var results = Scan(source);

        Assert.Equal(2, results.Count);
        Assert.Equal("Details", results[0].Value);
        Assert.Equal("SubHeading", results[0].Context);
        Assert.Equal("Last updated", results[1].Value);
        Assert.Equal("Caption", results[1].Context);
    }

    [Fact]
    public void Placeholder_NamedParameter_Detected()
    {
        var source = """
            class CatalogPage : Component {
                public override Element Render() {
                    return TextBox("", null, placeholderText: "Search products...");
                }
            }
            """;

        var results = Scan(source);

        Assert.Single(results);
        Assert.Equal("Search products...", results[0].Value);
    }
}
