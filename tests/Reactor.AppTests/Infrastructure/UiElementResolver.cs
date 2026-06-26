namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

internal static class UiElementResolver
{
    public static UiElement FindByAutomationId(
        WinAppUi app,
        IUiaPropertyReader uia,
        long hwnd,
        string automationId)
    {
        var match = app.Search(automationId, hwnd)
            .FirstOrDefault(m =>
                string.Equals(m.AutomationId, automationId, StringComparison.Ordinal) ||
                string.Equals(m.Selector, automationId, StringComparison.Ordinal));

        if (match is null)
            throw new WinAppException($"No element found with AutomationId '{automationId}'.");

        return new UiElement(app, uia, automationId, automationId, hwnd);
    }

    public static UiElement FindByName(
        WinAppUi app,
        IUiaPropertyReader uia,
        long hwnd,
        string name)
    {
        var matches = app.Search(name, hwnd);
        var exact = matches.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
        if (exact is null)
        {
            var suffix = matches.Count == 0
                ? ""
                : $" Search returned {matches.Count} non-exact match(es); use App.Search for substring queries.";
            throw new WinAppException($"No element found with exact Name '{name}'.{suffix}");
        }

        var hasAutomationId = !string.IsNullOrEmpty(exact.AutomationId);
        var selector = hasAutomationId ? exact.AutomationId! : exact.Selector;
        UiRect? bounds = hasAutomationId ? null : new UiRect(exact.X, exact.Y, exact.Width, exact.Height);
        return new UiElement(app, uia, selector, exact.AutomationId, hwnd, bounds);
    }
}
