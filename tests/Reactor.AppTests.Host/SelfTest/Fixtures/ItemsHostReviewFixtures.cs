using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Deliberately NOT WrapControlled: terminal teardown must also silence ordinary events.
[GenerateReactorWrapper(typeof(ListBox))]
[WrapEvent("SelectionChanged")]
internal partial record PlainSelectionElement;

internal static class ItemsHostReviewFixtures
{
    private static readonly Action Noop = static () => { };

    internal sealed class ExpanderHeaderCleanup(Harness h, bool useDescriptor = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int cleanups = 0;
            bool parented = false;
            var element = Expander("fallback", TextBlock("body"), isExpanded: true) with
            {
                HeaderTemplate = Button("owned header").OnUnmount(c =>
                {
                    cleanups++;
                    parented = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(c) is not null;
                }),
            };
            var reconciler = new Reconciler();
            // Override only this reconciler; the public factory normally uses
            // ExpanderHandler, so exercise the descriptor's own teardown too.
            if (useDescriptor)
                reconciler.RegisterHandler<ExpanderElement, Expander>(
                    new DescriptorHandler<ExpanderElement, Expander>(ExpanderElement.Descriptor));
            var expander = (Expander)reconciler.Mount(element, Noop)!;
            H.SetContent(expander);
            H.Check("ItemsHost_ExpanderHeader_Realized", await Harness.WaitFor(() =>
                H.FindControl<Button>(b => b.Content as string == "owned header") is not null));
            reconciler.UnmountChild(expander);
            H.Check("ItemsHost_ExpanderHeader_UnmountedParented", cleanups == 1 && parented);
            H.Check("ItemsHost_ExpanderHeader_Detached", expander.Header is null);
        }
    }

    internal sealed class SkippedClick(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hits = new List<int>();
            Element Item(int version) => Button("skip-go", () => hits.Add(version)).WithKey("go");
            var before = Item(1);
            var after = Item(2);
            H.Check("ItemsHost_Callback_EqualAppearance", Element.CanSkipUpdate(before, after));
            Element Pan(int version) => Button("pan").OnPan(onChanged: _ => hits.Add(version), minimumDistance: 4);
            H.Check("ItemsHost_Gesture_EqualAppearance", Element.CanSkipUpdate(Pan(1), Pan(2)));
            var reconciler = new Reconciler();
            Element old = ListBoxWrapperElement.ListBox(items: [before]);
            var list = (ListBox)reconciler.Mount(old, Noop)!;
            H.SetContent(list);
            await Harness.WaitFor(() => H.FindControl<Button>(b => b.Content as string == "skip-go") is not null);
            var original = H.FindControl<Button>(b => b.Content as string == "skip-go");
            H.ClickButton("skip-go");
            H.Check("ItemsHost_Callback_InitiallyDispatches", hits.SequenceEqual(new[] { 1 }));
            var next = ListBoxWrapperElement.ListBox(items: [after]);
            reconciler.UpdateChild(old, next, list, Noop);
            await Harness.Render();
            H.Check("ItemsHost_Callback_LiveChildReused", ReferenceEquals(original,
                H.FindControl<Button>(b => b.Content as string == "skip-go")));
            H.ClickButton("skip-go");
            H.Check("ItemsHost_Callback_NewClosureInvoked", hits.SequenceEqual(new[] { 1, 2 }));
            reconciler.UnmountChild(list);
        }
    }

    internal sealed class PlainEventUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int calls = 0;
            var element = PlainSelectionElement.ListBox(items: ["first", "second"]) with
            {
                OnSelectionChanged = _ => calls++,
            };
            var reconciler = new Reconciler();
            var list = (ListBox)reconciler.Mount(element, Noop)!;
            H.SetContent(list);
            await Harness.Render();
            list.SelectedIndex = 0;
            H.Check("ItemsHost_PlainEvent_SelectionWorks", calls == 1);
            calls = 0;
            reconciler.UnmountChild(list);
            await Harness.Render();
            H.Check("ItemsHost_PlainEvent_NoTerminalCallback", calls == 0);
            H.Check("ItemsHost_PlainEvent_TerminalItemsCleared", list.Items.Count == 0);

            // Positive control: native Clear really raises SelectionChanged
            // while the generated callback's element tag is still live.
            var live = (ListBox)reconciler.Mount(element, Noop)!;
            H.SetContent(live);
            await Harness.Render();
            live.SelectedIndex = 0;
            calls = 0;
            live.Items.Clear();
            H.Check("ItemsHost_PlainEvent_LiveClearRaisesCallback", calls == 1 && live.Items.Count == 0);
            reconciler.UnmountChild(live);
        }
    }

    internal sealed class NativeReorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            Element Item(string id) => Button(id).WithKey(id);
            Element old = ListBoxWrapperElement.ListBox(items: [Item("A"), Item("B"), Item("C")]);
            var list = (ListBox)reconciler.Mount(old, Noop)!;
            H.SetContent(list);
            await Harness.WaitFor(() => H.FindControl<Button>(b => b.Content as string == "C") is not null);
            var controls = new[] { "A", "B", "C" }.ToDictionary(id => id,
                id => H.FindControl<Button>(b => b.Content as string == id));
            var next = ListBoxWrapperElement.ListBox(items: [Item("C"), Item("A"), Item("B")]);
            reconciler.UpdateChild(old, next, list, Noop);
            await Harness.Render();
            H.Check("ItemsHost_Native_KeyedReorder", list.Items.Count == 3
                && list.Items.Cast<Button>().Select(b => b.Content).SequenceEqual(new object[] { "C", "A", "B" }));
            H.Check("ItemsHost_Native_ReorderLiveIdentity", controls.All(pair => ReferenceEquals(pair.Value,
                H.FindControl<Button>(b => b.Content as string == pair.Key))));
            reconciler.UnmountChild(list);

            // New equal objects are retained values, not new logical items. Removing
            // a different row must not clear selection on the retained first row.
            static string FreshA() => new(new[] { 'a' });
            old = ListBoxWrapperElement.ListBox(items: [FreshA(), "b", "c"]);
            list = (ListBox)reconciler.Mount(old, Noop)!;
            H.SetContent(list);
            await Harness.Render();
            list.SelectedIndex = 0;
            next = ListBoxWrapperElement.ListBox(items: [FreshA(), "c"]);
            reconciler.UpdateChild(old, next, list, Noop);
            await Harness.Render();
            H.Check("ItemsHost_Native_RawStructuralContents", list.Items.Cast<object>().SequenceEqual(new object[] { "a", "c" }));
            H.Check("ItemsHost_Native_EqualValueRetainsSelection", list.SelectedIndex == 0 && list.SelectedItem as string == "a");
            reconciler.UnmountChild(list);
        }
    }

    internal sealed class ComboReopen(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int opened = 0;
            Element Build(int revision) => VStack(
                ComboBox(new[] { "first", "second" }, 0) with { OnDropDownOpened = () => opened++ },
                ComboBox(new[] { "third", "fourth" }, 0) with { OnDropDownOpened = () => opened++ })
                .Margin(revision % 2);
            var reconciler = new Reconciler();
            Element old = Build(0);
            var root = reconciler.Mount(old, Noop)!;
            H.SetContent(root);
            await Harness.Render();
            for (int i = 0; i < 6; i++)
            {
                var current = H.FindControl<ComboBox>(c => c.Items[0] as string == (i % 2 == 0 ? "first" : "third"))!;
                current.IsDropDownOpen = true;
                await Harness.Render();
                current.IsDropDownOpen = false;
                var next = Build(i + 1);
                reconciler.UpdateChild(old, next, root, Noop);
                old = next;
                await Harness.Render();
                var first = H.FindControl<ComboBox>(c => c.Items[0] as string == "first")!;
                var second = H.FindControl<ComboBox>(c => c.Items[0] as string == "third")!;
                H.Check($"ItemsHost_ComboReopen_NoDuplicates_{i}", first.Items.Cast<object>().SequenceEqual(new object[] { "first", "second" })
                    && second.Items.Cast<object>().SequenceEqual(new object[] { "third", "fourth" }));
            }
            H.Check("ItemsHost_ComboReopen_OpenEventsObserved", opened == 6);
            reconciler.UnmountChild(root);
        }
    }
}
