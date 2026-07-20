namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Key tokens used by <see cref="UiElement.SendKeys"/> and the input helpers.
/// Replaces <c>OpenQA.Selenium.Keys</c> now that Appium is gone. The values are
/// private-use Unicode code points (the same convention WebDriver used) so they
/// can be embedded inside an otherwise-literal SendKeys string and split back
/// out into native <c>winapp ui send-keys</c> tokens by <see cref="UiElement.ToSendKeysTokens"/>.
/// </summary>
public static class Keys
{
    public const string Tab = "\ue004";
    public const string Enter = "\ue007";
    public const string Return = "\ue006";
    public const string Space = "\ue00d";
    public const string Escape = "\ue00c";
    public const string Shift = "\ue008";
    public const string Control = "\ue009";
    public const string Backspace = "\ue003";
    public const string Delete = "\ue017";
}
