using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.BasicInput;

class TextBoxPage: Component
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
            PageHeader("TextBox", "A single-line or multi-line plain text input field."),

            SampleCard("Basic TextBox",
                VStack(8,
                    TextBox(text, v => setText(v), "Type here..."),
                    TextBlock($"Characters: {text.Length}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
TextBox(text, v => setText(v), ""Type here..."")
"),

            SampleCard("Multiline TextBox",
                TextBox(multiline, v => setMultiline(v), "Enter multiple lines...")
                    .Set(tb => { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; })
                    .Height(120),
                sourceCode: @"
TextBox(multiline, v => setMultiline(v), ""Enter multiple lines..."")
    .Set(tb => { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; })
    .Height(120)
"),

            SampleCard("TextBox with Header",
                TextBox(headerText, v => setHeaderText(v), "user@example.com").Header("Email"),
                sourceCode: @"
TextBox(headerText, v => setHeaderText(v), ""user@example.com"").Header(""Email"")
"),

            // Phase 8.1 — InputScope fluents (spec 039 §17.3) + .Description() (§5).
            SampleCard("Numeric input — .NumericInput()",
                TextBox(numericText, v => setNumericText(v), "0")
                    .Header("Quantity")
                    .NumericInput()
                    .Description("Soft keyboards show a number pad."),
                sourceCode: @"
TextBox(numericText, v => setNumericText(v), ""0"")
    .Header(""Quantity"")
    .NumericInput()
    .Description(""Soft keyboards show a number pad."")
"),

            SampleCard("Email input — .EmailInput()",
                TextBox(emailText, v => setEmailText(v), "name@contoso.com")
                    .Header("Email address")
                    .EmailInput()
                    .Description("Hints the IME to surface '@' and '.com'."),
                sourceCode: @"
TextBox(emailText, v => setEmailText(v), ""name@contoso.com"")
    .Header(""Email address"")
    .EmailInput()
    .Description(""Hints the IME to surface '@' and '.com'."")
"),

            SampleCard("URL input — .UrlInput()",
                TextBox(urlText, v => setUrlText(v), "https://example.com")
                    .Header("Homepage")
                    .UrlInput()
                    .Description("Hints the IME to surface '/' and '.com'."),
                sourceCode: @"
TextBox(urlText, v => setUrlText(v), ""https://example.com"")
    .Header(""Homepage"")
    .UrlInput()
    .Description(""Hints the IME to surface '/' and '.com'."")
")
        ).Margin(36, 24, 36, 36));
    }
}
