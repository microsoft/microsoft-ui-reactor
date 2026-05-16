using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class TextFieldPage: Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        var (multiline, setMultiline) = UseState("");
        var (headerText, setHeaderText) = UseState("");
        var (numericText, setNumericText) = UseState("");
        var (emailText, setEmailText) = UseState("");
        var (urlText, setUrlText) = UseState("");

        return ScrollView(VStack(16,
            PageHeader("TextField", "A single-line or multi-line plain text input field."),

            SampleCard("Basic TextField",
                VStack(8,
                    TextField(text, v => setText(v), "Type here..."),
                    TextBlock($"Characters: {text.Length}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
TextField(text, v => setText(v), ""Type here..."")
"),

            SampleCard("Multiline TextField",
                TextField(multiline, v => setMultiline(v), "Enter multiple lines...")
                    .Set(tb => { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; })
                    .Height(120),
                sourceCode: @"
TextField(multiline, v => setMultiline(v), ""Enter multiple lines..."")
    .Set(tb => { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; })
    .Height(120)
"),

            SampleCard("TextField with Header",
                TextField(headerText, v => setHeaderText(v), "user@example.com").Header("Email"),
                sourceCode: @"
TextField(headerText, v => setHeaderText(v), ""user@example.com"").Header(""Email"")
"),

            // Phase 8.1 — InputScope fluents (spec 039 §17.3) + .Description() (§5).
            SampleCard("Numeric input — .NumericInput()",
                TextField(numericText, v => setNumericText(v), "0")
                    .Header("Quantity")
                    .NumericInput()
                    .Description("Soft keyboards show a number pad."),
                sourceCode: @"
TextField(numericText, v => setNumericText(v), ""0"")
    .Header(""Quantity"")
    .NumericInput()
    .Description(""Soft keyboards show a number pad."")
"),

            SampleCard("Email input — .EmailInput()",
                TextField(emailText, v => setEmailText(v), "name@contoso.com")
                    .Header("Email address")
                    .EmailInput()
                    .Description("Hints the IME to surface '@' and '.com'."),
                sourceCode: @"
TextField(emailText, v => setEmailText(v), ""name@contoso.com"")
    .Header(""Email address"")
    .EmailInput()
    .Description(""Hints the IME to surface '@' and '.com'."")
"),

            SampleCard("URL input — .UrlInput()",
                TextField(urlText, v => setUrlText(v), "https://example.com")
                    .Header("Homepage")
                    .UrlInput()
                    .Description("Hints the IME to surface '/' and '.com'."),
                sourceCode: @"
TextField(urlText, v => setUrlText(v), ""https://example.com"")
    .Header(""Homepage"")
    .UrlInput()
    .Description(""Hints the IME to surface '/' and '.com'."")
")
        ).Margin(36, 24, 36, 36));
    }
}
