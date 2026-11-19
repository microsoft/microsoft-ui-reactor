using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WctControls;
using Wct = CommunityToolkit.WinUI.Controls;

namespace ToolkitCompatibility;

internal static class TokenizingSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root)
    {
        string initialPage = App.InitialPage;
        try
        {
            App.InitialPage = "tokens";
            await root.ShowAsync(Microsoft.UI.Reactor.Factories.Component<App>());
        }
        finally
        {
            App.InitialPage = initialPage;
        }
        Wct.TokenizingTextBox? box = null;
        await CompatibilityRoot.WaitAsync(() =>
        {
            box = CompatibilityRoot.Descendants(ReactorAppContent)
                .OfType<Wct.TokenizingTextBox>().SingleOrDefault();
            return box is { IsLoaded: true } &&
                CompatibilityRoot.Descendants(box).OfType<AutoSuggestBox>().Any();
        }, "Actual TokenizingTextBox sample loaded");

        var control = box!;
        var editor = CompatibilityRoot.Descendants(control).OfType<AutoSuggestBox>().Single();
        var input = CompatibilityRoot.Descendants(editor).OfType<TextBox>().Single();
        var originalItems = control.Items;
        var originalSource = control.ItemsSource;
        string[] all =
        [
            "Account", "Add friend", "Attach", "Audio", "Calendar", "Camera",
            "Contact", "Favorite", "Link", "Mail", "Map", "Phone", "Pin",
            "Send", "Tags", "Zoom"
        ];
        int textEvents = 0, added = 0, removed = 0;
        control.TextChanged += (sender, args) =>
        {
            textEvents++;
            Program.Results.Note($"native TextChanged sender={sender.GetType().FullName}; " +
                $"sender-is-owner={ReferenceEquals(sender, control)}; reason={args.Reason}; text={control.Text}");
        };
        control.TokenItemAdded += (_, _) => added++;
        control.TokenItemRemoved += (_, _) => removed++;

        CheckSuggestions(all);
        Program.Check(editor.Focus(FocusState.Programmatic), "The native editor must accept focus.");
        editor.IsSuggestionListOpen = true;
        await CompatibilityRoot.WaitAsync(
            () => SuggestionList()?.Items.Cast<string>().SequenceEqual(all) == true,
            "Initial open popup contains the full dataset");
        await QueryAsync("cal", ["Calendar"]);
        await QueryAsync("CAL", ["Calendar"]);
        await QueryAsync("zz-no-match", []);
        await QueryAsync("", all);

        // Change the real inner TextBox so WinUI raises its deferred TextChanged,
        // rather than invoking a callback or substituting a test-only mapping.
        input.Text = "cal,";
        await CompatibilityRoot.WaitAsync(
            () => Tokens().SequenceEqual(["Calendar"]) && control.Text == "",
            "Delimiter converts cal into Calendar");
        await QueryAsync("", all);
        Program.Check(added == 1, $"Expected one TokenItemAdded, got {added}.");
        control.SelectedItem = "Calendar";
        Program.Check(control.SelectedTokenText == "Calendar", "Native selected token text is Calendar.");

        await QueryAsync("zz-no-match", []);
        input.Text = "zz-no-match,";
        await CompatibilityRoot.WaitAsync(
            () => Tokens().SequenceEqual(["Calendar", "zz-no-match"]) && control.Text == "",
            "Unmatched delimiter input remains a new token");
        await QueryAsync("", all);
        Program.Check(added == 2, $"Expected two TokenItemAdded events, got {added}.");

        var calendarToken = (Wct.TokenizingTextBoxItem)control.ContainerFromItem("Calendar");
        var removeButton = CompatibilityRoot.Descendants(calendarToken).OfType<Button>()
            .Single(button => button.Name == "PART_RemoveButton");
        var removePeer = new ButtonAutomationPeer(removeButton);
        ((IInvokeProvider)removePeer.GetPattern(PatternInterface.Invoke)).Invoke();
        await CompatibilityRoot.WaitAsync(
            () => Tokens().SequenceEqual(["zz-no-match"]) && removed == 1,
            "Native remove button removes only Calendar and fires its removal callback");

        await control.ClearAsync();
        await CompatibilityRoot.WaitAsync(() => Tokens().Length == 0 && removed == 2,
            "Native ClearAsync removes both tokens and fires removal callbacks");
        await QueryAsync("phone", ["Phone"]);
        await QueryAsync("", all);
        Program.Check(ReferenceEquals(originalItems, control.Items) &&
            ReferenceEquals(originalSource, control.ItemsSource),
            "Filtering must preserve the Toolkit-owned token collection.");
        Program.Check(ReferenceEquals(control, CompatibilityRoot.Descendants(ReactorAppContent)
            .OfType<Wct.TokenizingTextBox>().Single()), "Filtering must update, not remount, the native control.");
        Program.Results.Note($"native text events={textEvents}; token added={added}; token removed={removed}");

        string[] Tokens() => control.Items.OfType<string>().ToArray();

        ListViewBase? SuggestionList() => VisualTreeHelper.GetOpenPopupsForXamlRoot(editor.XamlRoot)
            .Where(popup => popup.Child is not null)
            .SelectMany(popup => CompatibilityRoot.Descendants(popup.Child))
            .OfType<ListViewBase>().SingleOrDefault(view => view.Name == "SuggestionsList");

        void CheckSuggestions(string[] expected)
        {
            Program.Check(Strings(control.SuggestedItemsSource).SequenceEqual(expected),
                $"SuggestedItemsSource expected [{string.Join(", ", expected)}], " +
                $"actual [{string.Join(", ", Strings(control.SuggestedItemsSource))}].");
            Program.Check(Strings(editor.ItemsSource).SequenceEqual(expected),
                $"Inner AutoSuggestBox.ItemsSource expected [{string.Join(", ", expected)}], " +
                $"actual [{string.Join(", ", Strings(editor.ItemsSource))}].");
        }

        async Task QueryAsync(string query, string[] expected)
        {
            int before = textEvents;
            bool changesText = input.Text != query;
            if (query.Length != 0 && expected.Length != 0)
                editor.IsSuggestionListOpen = true;
            input.Text = query;
            string caption = query.Length == 0
                ? "Pick from suggestions or type a new tag."
                : $"Editing: {query}";
            try
            {
                await CompatibilityRoot.WaitAsync(() =>
                    control.Text == query && (!changesText || textEvents > before) &&
                    CompatibilityRoot.Descendants(ReactorAppContent).OfType<TextBlock>()
                        .Any(t => t.Text == caption) &&
                    Strings(control.SuggestedItemsSource).SequenceEqual(expected) &&
                    Strings(editor.ItemsSource).SequenceEqual(expected),
                    $"Query '{query}' updates the actual sample caption and suggestions");
            }
            finally
            {
                Program.Results.Note($"query={query}; native text={control.Text}; events={textEvents}; " +
                    $"suggestions=[{string.Join(", ", Strings(control.SuggestedItemsSource))}]; " +
                    $"caption=[{string.Join(" | ", CompatibilityRoot.Descendants(ReactorAppContent)
                        .OfType<TextBlock>().Select(t => t.Text).Where(t =>
                            t.StartsWith("Editing:", StringComparison.Ordinal) ||
                            t == "Pick from suggestions or type a new tag."))}]");
            }
            CheckSuggestions(expected);
            if (query.Length != 0 && expected.Length != 0)
            {
                ListViewBase? list = null;
                await CompatibilityRoot.WaitAsync(() =>
                {
                    list = SuggestionList();
                    return list is not null && list.Items.Cast<string>().SequenceEqual(expected);
                }, $"Open suggestion popup contains [{string.Join(", ", expected)}]");
                Program.Results.Note($"popup items=[{string.Join(", ", list!.Items.Cast<string>())}]");
            }
        }
    }

    private static DependencyObject ReactorAppContent =>
        Microsoft.UI.Reactor.ReactorApp.PrimaryWindow!.NativeWindow.Content;

    private static string[] Strings(object? value) =>
        value is IEnumerable items ? items.Cast<string>().ToArray()
            : throw new InvalidOperationException("The sample suggestion source must be enumerable.");
}
