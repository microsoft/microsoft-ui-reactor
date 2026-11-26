using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using WctControls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class HeaderOwnershipChecks
{
    public sealed class State
    {
        public int CardCleanups;
        public int ExpanderCleanups;
        public Action? IncrementCard;
        public Action? IncrementExpander;
    }

    public sealed record CounterProps(bool Card, int Revision, State State);

    public sealed class Counter : Component<CounterProps>
    {
        public override Element Render()
        {
            var (count, set) = UseState(0);
            var state = Props.State;
            bool card = Props.Card;
            UseEffect(() => () => { if (card) state.CardCleanups++; else state.ExpanderCleanups++; }, []);
            if (card) state.IncrementCard = () => set(count + 1);
            else state.IncrementExpander = () => set(count + 1);
            return TextBlock($"{(card ? "Card" : "Expander")}:{count}:{Props.Revision}");
        }
    }

    internal static async Task RunAsync(CompatibilityRoot root)
    {
        var state = new State();
        Wct.SettingsCard? mountedCard = null;
        Wct.SettingsExpander? mountedExpander = null;
        Element Build(int phase) => phase == 8 ? TextBlock("Headers removed") : VStack(
            (SettingsCardElement.SettingsCard().Width(300 + phase) with
            {
                Header = phase is 2 or 3 ? Component<Counter, CounterProps>(new(true, phase, state)) : null,
                HeaderText = phase == 5 ? "Card text" : null
            }).OnMountAdd(control => mountedCard = (Wct.SettingsCard)control),
            (SettingsExpanderElement.SettingsExpander().Width(300 + phase) with
            {
                Header = phase is 2 or 3 ? Component<Counter, CounterProps>(new(false, phase, state)) : null,
                HeaderText = phase == 5 ? "Expander text" : null
            }).OnMountAdd(control => mountedExpander = (Wct.SettingsExpander)control));

        await root.ShowAsync(Build(0));
        await CompatibilityRoot.WaitAsync(() => mountedCard is not null && mountedExpander is not null &&
            CompatibilityRoot.IsAttached(mountedCard) && CompatibilityRoot.IsAttached(mountedExpander),
            "Exact owned Settings controls mounted");
        var card = mountedCard!;
        var expander = mountedExpander!;
        var nativeCard = new Native.StackPanel();
        var nativeExpander = new Native.StackPanel();
        nativeCard.Children.Add(new Native.TextBlock { Text = "native card child" });
        nativeExpander.Children.Add(new Native.TextBlock { Text = "native expander child" });
        card.Header = nativeCard;
        expander.Header = nativeExpander;
        await root.ShowAsync(Build(1));
        Program.Check(ReferenceEquals(card.Header, nativeCard) && ReferenceEquals(expander.Header, nativeExpander) &&
            nativeCard.Children.Count == 1 && nativeExpander.Children.Count == 1,
            "Unrelated update replaced a native-owned header or its child.");

        var owner = new Reconciler();
        int foreignCardCleanup = 0, foreignExpanderCleanup = 0;
        var foreignCard = (Native.Panel)owner.Mount(VStack(TextBlock("External card"))
            .OnUnmount(_ => foreignCardCleanup++), () => { })!;
        var foreignExpander = (Native.Panel)owner.Mount(VStack(TextBlock("External expander"))
            .OnUnmount(_ => foreignExpanderCleanup++), () => { })!;
        try
        {
            card.Header = foreignCard;
            expander.Header = foreignExpander;
            await root.ShowAsync(Build(2));
            Program.Check(foreignCardCleanup == 0 && foreignExpanderCleanup == 0 &&
                foreignCard.Children.Count == 1 && foreignExpander.Children.Count == 1,
                "Rich takeover unmounted another owner's header.");
            var ownedCard = (FrameworkElement)card.Header;
            var ownedExpander = (FrameworkElement)expander.Header;
            state.IncrementCard!();
            state.IncrementExpander!();
            await CompatibilityRoot.WaitAsync(() => Text(ownedCard) == "Card:1:2" &&
                Text(ownedExpander) == "Expander:1:2", "Owned header state update");
            await root.ShowAsync(Build(3));
            Program.Check(ReferenceEquals(card.Header, ownedCard) && ReferenceEquals(expander.Header, ownedExpander) &&
                Text(ownedCard) == "Card:1:3" && Text(ownedExpander) == "Expander:1:3",
                "Parent update lost owned-header state or identity.");
            await root.ShowAsync(Build(4));
            Program.Check(state.CardCleanups == 1 && state.ExpanderCleanups == 1 &&
                card.ReadLocalValue(Wct.SettingsCard.HeaderProperty) == DependencyProperty.UnsetValue &&
                expander.ReadLocalValue(Wct.SettingsExpander.HeaderProperty) == DependencyProperty.UnsetValue,
                "Rich-to-unset did not release each owned header once.");
            await root.ShowAsync(Build(5));
            Program.Check(card.Header is "Card text" && expander.Header is "Expander text", "Text did not claim header slots.");
            await root.ShowAsync(Build(6));
            Program.Check(card.ReadLocalValue(Wct.SettingsCard.HeaderProperty) == DependencyProperty.UnsetValue &&
                expander.ReadLocalValue(Wct.SettingsExpander.HeaderProperty) == DependencyProperty.UnsetValue,
                "Text-to-unset did not release its native values.");
            card.Header = foreignCard;
            expander.Header = foreignExpander;
            await root.ShowAsync(Build(7));
            await root.ShowAsync(Build(8));
            Program.Check(ReferenceEquals(card.Header, foreignCard) && ReferenceEquals(expander.Header, foreignExpander) &&
                foreignCard.Children.Count == 1 && foreignExpander.Children.Count == 1 &&
                foreignCardCleanup == 0 && foreignExpanderCleanup == 0 &&
                state.CardCleanups == 1 && state.ExpanderCleanups == 1,
                "Wrapper removal destroyed borrowed headers or duplicated owned cleanup.");
        }
        finally
        {
            await root.ShowAsync(TextBlock("Ownership check cleanup"));
            owner.UnmountChild(foreignCard);
            owner.UnmountChild(foreignExpander);
        }
        Program.Check(foreignCardCleanup == 1 && foreignExpanderCleanup == 1,
            "External-owner cleanup positive control failed.");
        await DisplacedHeadersAsync(root);
    }

    private static async Task DisplacedHeadersAsync(CompatibilityRoot root)
    {
        var state = new State();
        Wct.SettingsCard? card = null;
        Wct.SettingsExpander? expander = null;
        var cardReplacement = Replacement("foreign card");
        var expanderReplacement = Replacement("foreign expander");
        int currentStage = 0, unmountActions = 0, checks = 0;
        var failures = new List<string>();

        Element Build(int stage)
        {
            var cardElement = SettingsCardElement.SettingsCard().Width(300 + stage) with
            {
                Header = stage == 7 ? null : Component<Counter, CounterProps>(new(true, stage, state))
            };
            var expanderElement = SettingsExpanderElement.SettingsExpander().Width(300 + stage) with
            {
                Header = stage == 7 ? null : Component<Counter, CounterProps>(new(false, stage, state))
            };
            return VStack(12,
                stage is 3 or 5 or 8 ? TextBlock("Card removed") : cardElement
                    .OnMountAdd(control => card = (Wct.SettingsCard)control)
                    .OnUnmountAdd(control =>
                    {
                        unmountActions++;
                        // The callback belongs to the previous element, but observes the removal phase.
                        if (currentStage == 3) ((Wct.SettingsCard)control).Header = cardReplacement;
                        if (currentStage == 5) control.ClearValue(Wct.SettingsCard.HeaderProperty);
                    }),
                stage is 3 or 5 or 8 ? TextBlock("Expander removed") : expanderElement
                    .OnMountAdd(control => expander = (Wct.SettingsExpander)control)
                    .OnUnmountAdd(control =>
                    {
                        unmountActions++;
                        if (currentStage == 3) control.ClearValue(Wct.SettingsExpander.HeaderProperty);
                        if (currentStage == 5) ((Wct.SettingsExpander)control).Header = expanderReplacement;
                    }));
        }

        async Task ChangeAsync(int stage)
        {
            currentStage = stage;
            await root.ShowAsync(Build(stage));
            await CompatibilityRoot.WaitAsync(() => stage is 3 or 5 or 8 ||
                card?.Width == 300 + stage && expander?.Width == 300 + stage,
                $"Displaced-header stage {stage}");
        }

        void Check(bool success, string label)
        {
            checks++;
            Program.Results.Note($"displaced-header {(success ? "PASS" : "FAIL")} {label}");
            if (!success) failures.Add(label);
        }

        try
        {
            await ChangeAsync(0);
            await CompatibilityRoot.WaitAsync(() => card is not null && expander is not null &&
                CompatibilityRoot.IsAttached(card) && CompatibilityRoot.IsAttached(expander),
                "Exact displaced-header controls mounted");
            var cardChild = (FrameworkElement)card!.Header;
            var expanderChild = (FrameworkElement)expander!.Header;
            state.IncrementCard!();
            state.IncrementExpander!();
            await CompatibilityRoot.WaitAsync(() => Text(cardChild) == "Card:1:0" &&
                Text(expanderChild) == "Expander:1:0", "Initial displaced-header descendant state");
            Check(state.CardCleanups == 0 && state.ExpanderCleanups == 0,
                "Positive control: live stateful owned headers");

            card.Header = cardReplacement;
            expander.Header = expanderReplacement;
            await ChangeAsync(1);
            Check(ReferenceEquals(cardChild, card.Header) && Text(cardChild) == "Card:1:1",
                "Card reconciles its displaced owned child without resetting state");
            Check(ReferenceEquals(expanderChild, expander.Header) && Text(expanderChild) == "Expander:1:1",
                "Expander reconciles its displaced owned child without resetting state");
            Check(cardReplacement.Children.Count == 1 && expanderReplacement.Children.Count == 1,
                "Foreign replacements are not unmounted during rich reconciliation");

            card.ClearValue(Wct.SettingsCard.HeaderProperty);
            expander.ClearValue(Wct.SettingsExpander.HeaderProperty);
            await ChangeAsync(2);
            Check(ReferenceEquals(cardChild, card.Header) && Text(cardChild) == "Card:1:2",
                "Card retains ownership after native Header clear");
            Check(ReferenceEquals(expanderChild, expander.Header) && Text(expanderChild) == "Expander:1:2",
                "Expander retains ownership after native Header clear");

            await ChangeAsync(3);
            Check(unmountActions == 2, "Positive control: parent OnUnmount callbacks executed");
            Check(state.CardCleanups == 1 && state.ExpanderCleanups == 1,
                $"Owned children cleaned after OnUnmount replacement/clear (card={state.CardCleanups}, expander={state.ExpanderCleanups})");
            Check(ReferenceEquals(card.Header, cardReplacement) && cardReplacement.Children.Count == 1,
                "Card preserves OnUnmount replacement");
            Check(expander.Header is null, "Expander preserves OnUnmount clear");

            await ChangeAsync(4);
            await ChangeAsync(5);
            Check(unmountActions == 4 && state.CardCleanups == 2 && state.ExpanderCleanups == 2,
                "Reverse clear/replacement also cleans each owned child exactly once");
            Check(ReferenceEquals(expander!.Header, expanderReplacement) && expanderReplacement.Children.Count == 1,
                "Expander preserves OnUnmount replacement");
            Check(card!.Header is null, "Card preserves OnUnmount clear");

            await ChangeAsync(6);
            card!.Header = cardReplacement;
            expander!.Header = expanderReplacement;
            await ChangeAsync(7);
            Check(state.CardCleanups == 3 && state.ExpanderCleanups == 3,
                "Rich-to-unset cleans displaced owned children");
            Check(ReferenceEquals(card.Header, cardReplacement) && ReferenceEquals(expander.Header, expanderReplacement),
                "Rich-to-unset preserves externally supplied replacements");
            await ChangeAsync(8);
            Check(state.CardCleanups == 3 && state.ExpanderCleanups == 3,
                "No duplicate cleanup after releasing the declared headers");
        }
        finally
        {
            currentStage = 8;
            await root.ShowAsync(TextBlock("Displaced header cleanup"));
        }
        Program.Results.Note($"displaced-header assertions={checks}; failures={failures.Count}");
        Program.Check(checks == 16 && failures.Count == 0,
            $"Displaced-header assertions={checks}, failures={failures.Count}: {string.Join("; ", failures)}");
    }

    private static Native.StackPanel Replacement(string value)
    {
        var panel = new Native.StackPanel();
        panel.Children.Add(new Native.TextBlock { Text = value });
        return panel;
    }

    private static string Text(FrameworkElement element) =>
        CompatibilityRoot.Descendants(element).OfType<Native.TextBlock>().Single().Text;
}
