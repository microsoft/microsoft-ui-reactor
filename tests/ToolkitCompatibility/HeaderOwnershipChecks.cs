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
    }

    private static string Text(FrameworkElement element) =>
        CompatibilityRoot.Descendants(element).OfType<Native.TextBlock>().Single().Text;
}
