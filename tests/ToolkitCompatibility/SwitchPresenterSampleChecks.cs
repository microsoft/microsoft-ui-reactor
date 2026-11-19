using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class SwitchPresenterSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("swp"));
        await CompatibilityRoot.WaitAsync(() => Presenters().Any(p => p.IsLoaded), "sample presenters loaded");
        var initial = Presenters();
        foreach (var presenter in initial)
        {
            Program.Results.Note($"native={presenter.GetType().FullName}; SwitchCases.Count={presenter.SwitchCases?.Count.ToString() ?? "(null collection)"}; " +
                $"CurrentCase={presenter.CurrentCase?.Value ?? "(null)"}; Content={presenter.Content?.GetType().Name ?? "(null)"}");
            Program.Check(presenter.SwitchCases is { Count: > 0 } && presenter.CurrentCase is not null,
                "The actual sample bypasses native SwitchCases/CurrentCase.");
        }
        Program.Check(initial.Length == 2, "Expected the sample's default and no-default presenters.");
        var withDefault = Find(initial, "SwitchPresenter.WithDefault");
        var withoutDefault = Find(initial, "SwitchPresenter.WithoutDefault");
        var defaultCases = withDefault.SwitchCases;
        var matchOnlyCases = withoutDefault.SwitchCases;
        var defaults = defaultCases.Cast<Wct.Case>().ToArray();
        var matches = matchOnlyCases.Cast<Wct.Case>().ToArray();
        CheckCases(defaults, matches);
        CheckSelection(withDefault, defaults[0], "Loading", "Loading");
        CheckSelection(withoutDefault, matches[0], "Loading", "Loading");

        var choice = Tree().OfType<Wct.Segmented>().Single();
        Program.Check(choice.Items.Count == 4, "Expected three matching values and Unknown.");
        foreach (var (index, value) in new[] { (1, "Ready"), (2, "Error"), (3, "Unknown"), (1, "Ready") })
        {
            // Raise the native selection event; only the real sample's callback changes its state.
            choice.SelectedIndex = index;
            await CompatibilityRoot.WaitAsync(
                () => Equals(withDefault.Value, value) && Equals(withoutDefault.Value, value),
                $"sample declarative Value={value}");
            ((FrameworkElement)ReactorApp.PrimaryWindow!.NativeWindow.Content).UpdateLayout();
            var updated = Presenters();
            Program.Check(ReferenceEquals(Find(updated, "SwitchPresenter.WithDefault"), withDefault) &&
                ReferenceEquals(Find(updated, "SwitchPresenter.WithoutDefault"), withoutDefault),
                "Changing the sample value replaced a native presenter.");
            Program.Check(ReferenceEquals(withDefault.SwitchCases, defaultCases) &&
                ReferenceEquals(withoutDefault.SwitchCases, matchOnlyCases) &&
                defaults.Zip(defaultCases.Cast<Wct.Case>()).All(pair => ReferenceEquals(pair.First, pair.Second)) &&
                matches.Zip(matchOnlyCases.Cast<Wct.Case>()).All(pair => ReferenceEquals(pair.First, pair.Second)),
                "Changing only Value replaced native cases.");
            CheckCases(defaultCases.Cast<Wct.Case>().ToArray(), matchOnlyCases.Cast<Wct.Case>().ToArray());
            CheckSelection(withDefault, defaults[index], value, index == 3 ? "Unknown state (default)" : value);
            CheckSelection(withoutDefault, index == 3 ? null : matches[index], value, index == 3 ? null : value);
        }

        await root.ShowAsync(TextBlock("SwitchPresenter sample removed"));
        await CompatibilityRoot.WaitAsync(() => !withDefault.IsLoaded && !withoutDefault.IsLoaded,
            "sample presenters detached");
        Program.Check(Presenters().Length == 0, "Removed sample still contains a native presenter.");

        await root.ShowAsync(WctControls.SamplePages.CreatePage("swp"));
        await CompatibilityRoot.WaitAsync(() => Presenters().Length == 2 && Presenters().All(p => p.IsLoaded),
            "sample presenters remounted");
        var remounted = Presenters();
        var freshDefault = Find(remounted, "SwitchPresenter.WithDefault");
        var freshMatchOnly = Find(remounted, "SwitchPresenter.WithoutDefault");
        Program.Check(!ReferenceEquals(withDefault, freshDefault) && !ReferenceEquals(withoutDefault, freshMatchOnly) &&
            !ReferenceEquals(defaultCases, freshDefault.SwitchCases) && !ReferenceEquals(matchOnlyCases, freshMatchOnly.SwitchCases),
            "Remount reused the removed page's presenters or cases.");
        var freshDefaults = freshDefault.SwitchCases.Cast<Wct.Case>().ToArray();
        var freshMatches = freshMatchOnly.SwitchCases.Cast<Wct.Case>().ToArray();
        CheckCases(freshDefaults, freshMatches);
        CheckSelection(freshDefault, freshDefaults[0], "Loading", "Loading");
        CheckSelection(freshMatchOnly, freshMatches[0], "Loading", "Loading");
        await root.ShowAsync(TextBlock("SwitchPresenter sample checks complete"));
    }

    private static IEnumerable<DependencyObject> Tree() =>
        CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content);

    private static Wct.SwitchPresenter[] Presenters() => Tree().OfType<Wct.SwitchPresenter>().ToArray();

    private static Wct.SwitchPresenter Find(IEnumerable<Wct.SwitchPresenter> presenters, string id) =>
        presenters.Single(p => AutomationProperties.GetAutomationId(p) == id);

    private static void CheckCases(Wct.Case[] withDefault, Wct.Case[] withoutDefault)
    {
        Program.Check(withDefault.Length == 4 && withoutDefault.Length == 3, "Native case counts must be 4 and 3.");
        string[] values = ["Loading", "Ready", "Error"];
        Program.Check(withDefault.Take(3).Select(c => c.Value).SequenceEqual(values) &&
            withoutDefault.Select(c => c.Value).SequenceEqual(values), "Native matching case values differ from the sample.");
        Program.Check(withDefault.Count(c => c.IsDefault) == 1 && withDefault[3].IsDefault &&
            withDefault[3].Value is null && withoutDefault.All(c => !c.IsDefault), "Default case configuration is incorrect.");
    }

    private static void CheckSelection(Wct.SwitchPresenter presenter, Wct.Case? expected, string value, string? text)
    {
        Program.Results.Note($"{AutomationProperties.GetAutomationId(presenter)}: Value={presenter.Value}; " +
            $"CurrentCase={presenter.CurrentCase?.Value ?? (presenter.CurrentCase?.IsDefault == true ? "(default)" : "(null)")}; " +
            $"Content={(presenter.Content as TextBlock)?.Text ?? "(null)"}");
        Program.Check(Equals(presenter.Value, value), $"Expected declarative Value={value}.");
        Program.Check(ReferenceEquals(presenter.CurrentCase, expected), $"Toolkit did not select the expected case for {value}.");
        if (expected is null)
        {
            Program.Check(presenter.Content is null, "No-match without a default must clear selected Content.");
            return;
        }
        Program.Check(presenter.SwitchCases.Any(c => ReferenceEquals(c, expected)),
            "CurrentCase is not an entry in the native collection.");
        Program.Check(ReferenceEquals(presenter.Content, expected.Content),
            "Reactor overwrote Toolkit-owned Content instead of preserving CurrentCase.Content.");
        Program.Check(presenter.Content is TextBlock block && block.Text == text &&
            CompatibilityRoot.Descendants(presenter).Any(child => ReferenceEquals(child, block)),
            $"Expected native case content '{text}' in the presenter's visual tree.");
    }
}
