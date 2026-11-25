using System.Windows.Input;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using WctControls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ToolkitMetadataCommandFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "Toolkit_MetadataControl_CommandParameter";
    private const string Label = "Open metadata";

    private sealed class RecordingCommand(Action<object?> execute) : ICommand
    {
        internal bool Allowed { get; set; } = true;
        internal List<object?> Queries { get; } = [];

        public bool CanExecute(object? parameter)
        {
            Queries.Add(parameter);
            return Allowed;
        }

        public void Execute(object? parameter) => execute(parameter);

#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }

    public override async Task RunAsync()
    {
        object firstParameter = new();
        object secondParameter = new();
        List<object?> firstCalls = [];
        List<object?> secondCalls = [];
        var firstCommand = new RecordingCommand(firstCalls.Add);
        var secondCommand = new RecordingCommand(secondCalls.Add);
        using var host = H.CreateHost();

        MetadataControlElement Tree(ICommand command, object parameter) =>
            MetadataControlElement.MetadataControl(items: new MetadataItem[]
            {
                new() { Label = Label, Command = command, CommandParameter = parameter },
            });

        host.Mount(_ => Tree(firstCommand, firstParameter));
        await host.WaitForIdleAsync();
        await Harness.Render();
        var original = H.FindControl<MetadataControl>(_ => true)
            ?? throw new InvalidOperationException("Generated MetadataControl was not mounted.");
        H.Check($"{Name}_MountDoesNotExecute", firstCalls.Count == 0 && secondCalls.Count == 0);

        CurrentInvokeProvider("Mount").Invoke();
        bool delivered = await Harness.WaitFor(() => firstCalls.Count > 0);
        await Harness.Render();
        CheckCalls("Mount", delivered, 1, 0);
        H.Check($"{Name}_MountCanExecuteParameter",
            firstCommand.Queries.Count > 0 &&
            firstCommand.Queries.All(parameter => ReferenceEquals(parameter, firstParameter)));

        // Keep the label unchanged: only the new command and parameter can distinguish this update.
        host.Mount(_ => Tree(secondCommand, secondParameter));
        await host.WaitForIdleAsync();
        await Harness.Render();
        H.Check($"{Name}_RetainedNativeControl",
            ReferenceEquals(original, H.FindControl<MetadataControl>(_ => true)));
        CheckCalls("UpdateDoesNotExecute", true, 1, 0);

        CurrentInvokeProvider("Update").Invoke();
        delivered = await Harness.WaitFor(() => secondCalls.Count > 0);
        await Harness.Render();
        CheckCalls("Update", delivered, 1, 1);
        H.Check($"{Name}_UpdateCanExecuteParameter",
            secondCommand.Queries.Count > 0 &&
            secondCommand.Queries.All(parameter => ReferenceEquals(parameter, secondParameter)));

        // Toolkit gates the Hyperlink click with CanExecute; it does not disable the link.
        secondCommand.Allowed = false;
        int queriesBefore = secondCommand.Queries.Count;
        CurrentInvokeProvider("Disabled").Invoke();
        bool queried = await Harness.WaitFor(() => secondCommand.Queries.Count > queriesBefore);
        await Harness.Render();
        H.Check($"{Name}_DisabledReachedCanExecute",
            queried && ReferenceEquals(secondCommand.Queries.LastOrDefault(), secondParameter));
        CheckCalls("Disabled", queried, 1, 1);

        secondCommand.Allowed = true;
        CurrentInvokeProvider("Reenabled").Invoke();
        delivered = await Harness.WaitFor(() => secondCalls.Count > 1);
        await Harness.Render();
        CheckCalls("Reenabled", delivered, 1, 2);

        void CheckCalls(string phase, bool observed, int expectedFirst, int expectedSecond)
        {
            bool firstMatches = firstCalls.All(parameter => ReferenceEquals(parameter, firstParameter));
            bool secondMatches = secondCalls.All(parameter => ReferenceEquals(parameter, secondParameter));
            string detail = $"first calls={firstCalls.Count}/{expectedFirst}, " +
                $"second calls={secondCalls.Count}/{expectedSecond}, " +
                $"first parameter identity={firstMatches}, second parameter identity={secondMatches}";
            Console.WriteLine($"# {Name} {phase}: {detail}");
            H.Check($"{Name}_{phase}_ExactCallbacksAndParameters",
                observed && firstCalls.Count == expectedFirst && secondCalls.Count == expectedSecond &&
                firstMatches && secondMatches, detail);
        }
    }

    private IInvokeProvider CurrentInvokeProvider(string phase)
    {
        var text = H.FindControl<TextBlock>(control => control.Name == "TextContainer")
            ?? throw new InvalidOperationException("MetadataControl did not realize its TextContainer.");
        var links = text.Inlines.OfType<Hyperlink>().ToArray();
        H.Check($"{Name}_{phase}_RealizedLink",
            text.IsLoaded && links.Length == 1 &&
            links[0].Inlines.OfType<Run>().Select(run => run.Text).SequenceEqual([Label]),
            $"loaded={text.IsLoaded}, native hyperlinks={links.Length}");

        var textPeer = FrameworkElementAutomationPeer.CreatePeerForElement(text)
            ?? throw new InvalidOperationException("Metadata TextContainer exposes no native peer.");
        var linkPeers = textPeer.GetChildren()?.Where(peer =>
            peer.GetAutomationControlType() == AutomationControlType.Hyperlink &&
            peer.GetName() == Label).ToArray() ?? [];
        if (linkPeers.Length != 1)
            throw new InvalidOperationException(
                $"Metadata TextContainer exposes {linkPeers.Length} matching native hyperlink peers; expected one.");
        return linkPeers[0].GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Metadata hyperlink exposes no native Invoke provider.");
    }
}
