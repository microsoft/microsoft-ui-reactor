using System.Collections;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class RichSuggestSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("rsb"));
        var box = Find();
        await CompatibilityRoot.WaitAsync(() => box.TextDocument is not null, "RichSuggestBox text document");
        var document = box.TextDocument ?? throw new InvalidOperationException("RichSuggestBox has no text document.");
        object source = box.ItemsSource;
        var requests = new List<string?>();
        void Requested(Wct.RichSuggestBox sender, Wct.SuggestionRequestedEventArgs args) => requests.Add(args.QueryText);
        box.SuggestionRequested += Requested;
        try
        {
            foreach (var query in new[] { "al", "AL", "zzzz", "" })
            {
                int before = requests.Count;
                document.SetText(TextSetOptions.None, "");
                document.Selection.SetRange(0, 0);
                document.Selection.TypeText("@" + query);
                await CompatibilityRoot.WaitAsync(() => requests.Skip(before).Any(value =>
                    string.Equals(value, query, StringComparison.Ordinal)),
                    $"native SuggestionRequested for '{query}'");
                string[] expected = query switch
                {
                    "al" or "AL" => ["Alex Wilber"],
                    "zzzz" => [],
                    _ => ["Adele Vance", "Alex Wilber", "Diego Siciliani", "Grady Archie",
                        "Isaiah Langer", "Lee Gu", "Megan Bowen", "Patti Fernandez"]
                };
                await CompatibilityRoot.WaitAsync(() => ((IEnumerable)box.ItemsSource).Cast<string>().SequenceEqual(expected),
                    $"sample-filtered suggestions for '{query}'");
                Program.Check(ReferenceEquals(source, box.ItemsSource), "Sample replaced its stable suggestion source.");
                Program.Results.Note($"RichSuggestBox query='{query}'; native requests={requests.Count - before}; items={string.Join("|", expected)}");
            }
        }
        finally { box.SuggestionRequested -= Requested; }
        await root.ShowAsync(TextBlock("Rich suggestions removed"));
        Program.Check(!box.IsLoaded, "RichSuggestBox remained loaded after navigation.");
        await root.ShowAsync(WctControls.SamplePages.CreatePage("rsb"));
        var remounted = Find();
        Program.Check(!ReferenceEquals(box, remounted) && !ReferenceEquals(source, remounted.ItemsSource) &&
            ((IEnumerable)remounted.ItemsSource).Cast<string>().Count() == 8,
            "RichSuggestBox remount did not create a fresh complete source.");
    }

    private static Wct.RichSuggestBox Find() =>
        CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content).OfType<Wct.RichSuggestBox>().Single();

    internal static async Task ChooseAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(TextBlock("Reset rich suggestion page"));
        await root.ShowAsync(WctControls.SamplePages.CreatePage("rsb"));
        var box = Find();
        await CompatibilityRoot.WaitAsync(() => box.TextDocument is not null, "RichSuggestBox choice document");
        var document = box.TextDocument ?? throw new InvalidOperationException("No text document.");
        for (int count = 1; count <= 2; count++)
        {
            document.Selection.EndKey(TextRangeUnit.Story, false);
            document.Selection.TypeText(count == 1 ? "@al" : " @al");
            await CompatibilityRoot.WaitAsync(() => ((IEnumerable)box.ItemsSource).Cast<string>()
                .SequenceEqual(["Alex Wilber"]), "Alex Wilber native suggestion");
            await Task.Delay(100);
            var list = VisualTreeHelper.GetOpenPopupsForXamlRoot(box.XamlRoot)
                .Where(p => p.IsOpen && p.Child is not null)
                .SelectMany(p => CompatibilityRoot.Descendants(p.Child))
                .OfType<ListView>().SingleOrDefault(l => l.Items.Contains("Alex Wilber"));
            if (list?.ContainerFromItem("Alex Wilber") is not FrameworkElement item)
                throw new ScenarioBlockedException("Native suggestion container is unavailable for public UIA invocation.", "tool");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
            if (peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                throw new ScenarioBlockedException("Native suggestion item exposes no public Invoke pattern; private commit APIs are not used.", "tool");
            invoke.Invoke();
            int expectedCount = count;
            await CompatibilityRoot.WaitAsync(() => box.Tokens.Count == expectedCount,
                "native ItemClick commits a real suggestion token");
            document.GetText(TextGetOptions.NoHidden, out var text);
            Program.Check(box.Tokens.All(t => t.DisplayText == "@Alex Wilber" &&
                Equals(t.Item, "Alex Wilber")) && text.Contains("@Alex Wilber", StringComparison.Ordinal),
                $"Native token/document did not contain full chosen name: '{text}'.");
            string caption = $"Last chosen: Alex Wilber ({count} selections)";
            await CompatibilityRoot.WaitAsync(() =>
                CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
                    .OfType<TextBlock>().Any(t => t.Text == caption),
                $"current choice callback count {count}");
            Program.Results.Note($"RichSuggestBox public UIA Invoke: tokens={box.Tokens.Count}; caption='{caption}'");
        }
    }

    // Two choices land before the page re-renders, so each callback runs against the same rendered state.
    internal static async Task DeferredChoicesAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(VStack().AutomationId("RichSuggest.DeferredHost"));
        var host = CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<Panel>().Single(p => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(p) == "RichSuggest.DeferredHost");
        var owner = new Reconciler();
        int renderRequests = 0;
        void RequestRender() => renderRequests++;
        var page = WctControls.SamplePages.CreatePage("rsb");
        var control = owner.Mount(page, RequestRender) ?? throw new InvalidOperationException("Sample page did not mount.");
        host.Children.Add(control);
        try
        {
            var box = CompatibilityRoot.Descendants(control).OfType<Wct.RichSuggestBox>().Single();
            await CompatibilityRoot.WaitAsync(() => box.TextDocument is not null && box.IsLoaded, "Deferred RichSuggestBox document");
            var document = box.TextDocument ?? throw new InvalidOperationException("No text document.");
            Program.Check(CaptionText(control) == "Last chosen: none (0 selections)", "Unexpected initial caption.");
            for (int count = 1; count <= 2; count++)
            {
                document.Selection.EndKey(TextRangeUnit.Story, false);
                document.Selection.TypeText(count == 1 ? "@al" : " @al");
                await CompatibilityRoot.WaitAsync(() => ((IEnumerable)box.ItemsSource).Cast<string>()
                    .SequenceEqual(["Alex Wilber"]), "Deferred Alex Wilber native suggestion");
                await Task.Delay(100);
                var list = VisualTreeHelper.GetOpenPopupsForXamlRoot(box.XamlRoot)
                    .Where(p => p.IsOpen && p.Child is not null)
                    .SelectMany(p => CompatibilityRoot.Descendants(p.Child))
                    .OfType<ListView>().SingleOrDefault(l => l.Items.Contains("Alex Wilber"));
                if (list?.ContainerFromItem("Alex Wilber") is not FrameworkElement item)
                    throw new ScenarioBlockedException("Native suggestion container is unavailable for public UIA invocation.", "tool");
                if (FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                    throw new ScenarioBlockedException("Native suggestion item exposes no public Invoke pattern.", "tool");
                invoke.Invoke();
                int expectedTokens = count;
                await CompatibilityRoot.WaitAsync(() => box.Tokens.Count == expectedTokens, "Deferred native token commit");
            }
            document.GetText(TextGetOptions.NoHidden, out var text);
            Program.Check(box.Tokens.Count == 2 && box.Tokens.All(t => t.DisplayText == "@Alex Wilber" && Equals(t.Item, "Alex Wilber")) &&
                text.Split("@Alex Wilber").Length == 3, $"Deferred choices did not leave two full tokens: '{text}'.");
            Program.Check(renderRequests > 0 && CaptionText(control) == "Last chosen: none (0 selections)",
                $"Positive control: choices requested {renderRequests} renders while reconciliation stayed deferred.");
            var updated = owner.Reconcile(page, page, control, RequestRender);
            Program.Check(ReferenceEquals(updated, control), "Deferred reconciliation replaced the page.");
            string caption = CaptionText(control);
            Program.Results.Note($"RichSuggestBox deferred choices: tokens=2; render requests={renderRequests}; caption after one reconcile='{caption}'; public UIA Invoke, not physical input");
            Program.Check(caption == "Last chosen: Alex Wilber (2 selections)",
                $"Expected 'Last chosen: Alex Wilber (2 selections)' after one deferred reconcile, actual '{caption}'.");
        }
        finally
        {
            host.Children.Remove(control);
            owner.UnmountChild(control);
            await root.ShowAsync(TextBlock("Deferred rich suggestions removed"));
        }
    }

    private static string CaptionText(UIElement page) => CompatibilityRoot.Descendants(page).OfType<TextBlock>()
        .Single(t => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "RichSuggestBox.LastChoice").Text;
}
