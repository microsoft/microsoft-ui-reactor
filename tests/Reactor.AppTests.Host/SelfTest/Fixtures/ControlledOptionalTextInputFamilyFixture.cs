using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ControlledOptionalTextInputFamilyFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Run(TextBoxScenario());
            await Run(PasswordBoxScenario());
            await Run(RichEditBoxScenario());
            await Run(AutoSuggestBoxScenario());
        }

        private async Task Run<TControl>(ControlledOptionalSelfTestHelpers.Scenario<TControl, string> scenario)
            where TControl : DependencyObject
        {
            const string fixture = "ControlledOptionalTextInputFamily";
            await ControlledOptionalSelfTestHelpers.RunUnsetSurvivesSiblingRerenderAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunBoundUpdatesControlAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunSnapBackAsync(H, fixture, scenario);
        }
    }

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.TextBox, string> TextBoxScenario() =>
        new(
            "TextBox",
            (value, changed) => TextBox(value, changed),
            h => h.FindControl<WinUI.TextBox>(_ => true),
            c => c.Text,
            (c, v) => c.Text = v,
            "typed-finish",
            "state-update",
            "user-edit");

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.PasswordBox, string> PasswordBoxScenario() =>
        new(
            "PasswordBox",
            (value, changed) => PasswordBox(value, changed),
            h => h.FindControl<WinUI.PasswordBox>(_ => true),
            c => c.Password,
            (c, v) => c.Password = v,
            "secret-one",
            "secret-two",
            "secret-user");

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.RichEditBox, string> RichEditBoxScenario() =>
        new(
            "RichEditBox",
            (value, changed) => RichEditBox(value, changed),
            h => h.FindControl<WinUI.RichEditBox>(_ => true),
            GetRichEditText,
            (c, v) => c.Document.SetText(TextSetOptions.None, v),
            "rich-one",
            "rich-two",
            "rich-user");

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.AutoSuggestBox, string> AutoSuggestBoxScenario() =>
        new(
            "AutoSuggestBox",
            (value, changed) => AutoSuggestBox(value, changed),
            h => h.FindControl<WinUI.AutoSuggestBox>(_ => true),
            c => c.Text,
            (c, v) =>
            {
                var inner = FindDescendant<WinUI.TextBox>(c);
                if (inner is not null) inner.Text = v;
                else c.Text = v;
            },
            "auto-one",
            "auto-two",
            "auto-user");

    private static string GetRichEditText(WinUI.RichEditBox box)
    {
        box.Document.GetText(TextGetOptions.None, out var text);
        return text?.TrimEnd('\r') ?? "";
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }

        return null;
    }
}
