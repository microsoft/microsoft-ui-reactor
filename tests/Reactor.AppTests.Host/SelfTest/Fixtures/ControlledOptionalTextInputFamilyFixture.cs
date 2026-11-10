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
            await ControlledOptionalSelfTestHelpers.RunUserEditAfterStateWriteAsync(
                H, fixture, scenario, "EditAfterWrite", scenario.SecondValue);
            // RichEditBox skips writing an empty value; a skipped write must not
            // reserve an echo either.
            await ControlledOptionalSelfTestHelpers.RunUserEditAfterStateWriteAsync(
                H, fixture, scenario, "EditAfterEmptyWrite", "");
            if (scenario.Name is "RichEditBox")
            {
                // The document reads "\n" back as "\r": the write must still
                // recognise its own echo.
                await ControlledOptionalSelfTestHelpers.RunUserEditAfterStateWriteAsync(
                    H, fixture, scenario, "EditAfterMultilineWrite", "rich-line-one\nrich-line-two");
                await RunRichEditTrailingLineBreakAsync(fixture);
            }
        }

        // A trailing line break is part of the value. The document always ends
        // with an implicit paragraph mark (GetText(None) returns "" as "\r" and
        // "a\n" as "a\r\r"); only that mark is dropped on readback, so "\n" is
        // not equal to "" and "text\n" is not equal to "text", and both writes
        // must reach the document.
        private async Task RunRichEditTrailingLineBreakAsync(string fixture)
        {
            var prefix = $"{fixture}_RichEditBox_TrailingLineBreak";

            // Mount "\n" on an empty document.
            using (var host = H.CreateHost())
            {
                host.Mount(_ => RichEditBox("\n", _ => { }));
                await Harness.Render();
                var box = H.FindControl<WinUI.RichEditBox>(_ => true);
                H.Check($"{prefix}_MountControlFound", box is not null);
                if (box is null) return;
                var raw = GetRawRichEditText(box);
                H.Check($"{prefix}_MountNewlineOnlyWritten", raw == "\r\r", $"document=[{Escape(raw)}]");
            }

            // A state change from "text" to "text\n" (an "insert line break" button).
            var callbacks = new List<string>();
            var writeLabel = $"{prefix}_Write";
            using (var host = H.CreateHost())
            {
                host.Mount(ctx =>
                {
                    var (value, setValue) = ctx.UseState("rich-text");
                    return VStack(
                        Button(writeLabel, () => setValue("rich-text\n")),
                        RichEditBox(value, v =>
                        {
                            callbacks.Add(v);
                            setValue(v);
                        }));
                });
                await Harness.Render();
                var box = H.FindControl<WinUI.RichEditBox>(_ => true);
                H.Check($"{prefix}_UpdateControlFound", box is not null);
                if (box is null) return;
                await Harness.WaitFor(() => !ArmPending(box), maxPasses: 25, perPassMs: 20);
                callbacks.Clear();

                H.ClickButton(writeLabel);
                await Harness.Render();
                await Harness.WaitFor(() => !ArmPending(box), maxPasses: 25, perPassMs: 20);
                await Harness.Render();

                var raw = GetRawRichEditText(box);
                H.Check($"{prefix}_UpdateLineBreakWritten", raw == "rich-text\r\r", $"document=[{Escape(raw)}]");
                H.Check($"{prefix}_UpdateEchoSuppressed", callbacks.Count == 0,
                    $"callbacks=[{string.Join(", ", callbacks.Select(Escape))}]");
            }
        }

        private static bool ArmPending(WinUI.RichEditBox box) =>
            Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(box, out var state)
            && state.PendingEchoMatch is not null;

        private static string GetRawRichEditText(WinUI.RichEditBox box)
        {
            box.Document.GetText(TextGetOptions.None, out var text);
            return text ?? "";
        }

        private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
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

    // Same readback as the RichEditBox descriptor: drop only the document's
    // implicit final paragraph mark.
    private static string GetRichEditText(WinUI.RichEditBox box)
    {
        box.Document.GetText(TextGetOptions.None, out var text);
        if (string.IsNullOrEmpty(text)) return "";
        return text[^1] == '\r' ? text[..^1] : text;
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
