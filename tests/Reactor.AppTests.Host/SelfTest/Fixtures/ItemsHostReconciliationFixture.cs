using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

[GenerateReactorWrapper(typeof(SettingsExpander))]
[WrapElementSlot("Content")]
[WrapElementSlot("HeaderIcon")]
internal partial record ObservableExpanderElement;

[GenerateReactorWrapper(typeof(SettingsCard))]
internal partial record ObservableCardElement;

internal static class ItemsHostReconciliationFixture
{
    internal sealed class Probe
    {
        public Action<string[]> SetItems = null!;
        public Action<bool> SetEnabled = null!;
        public Action RemoveHost = null!;
        public List<string> Cleanups { get; } = new();
    }

    internal sealed record RowProps(Probe Probe, string Id, bool Enabled);

    internal sealed class Row : Component<RowProps>
    {
        public override Element Render()
        {
            var (count, set) = UseState(0);
            var probe = Props.Probe;
            var id = Props.Id;
            UseEffect(() => () => probe.Cleanups.Add(id));
            return Button($"{id}:{count}", () => set(count + 1)).IsEnabled(Props.Enabled);
        }
    }

    internal sealed class Root : Component<Probe>
    {
        public override Element Render()
        {
            var (items, setItems) = UseState(new[] { "A", "B" });
            var (enabled, setEnabled) = UseState(true);
            var (visible, setVisible) = UseState(true);
            Props.SetItems = setItems;
            Props.SetEnabled = setEnabled;
            Props.RemoveHost = () => setVisible(false);
            if (!visible) return TextBlock("host removed");
            return ObservableExpanderElement.SettingsExpander(
                header: "dynamic wrapper",
                content: Component<Row, RowProps>(new(Props, "header", enabled)),
                headerIcon: Icon(FontIcon("\uE8A4")).OnUnmount(_ => Props.Cleanups.Add("icon")),
                items: items.Select(id => (object)ObservableCardElement.SettingsCard(
                    header: id, content: Component<Row, RowProps>(new(Props, id, enabled))).WithKey(id)).ToArray())
                .Set(c => c.IsExpanded = true);
        }
    }

    internal sealed class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // This code-only selftest Application does not inherit the host's generated
            // XAML provider. Root Toolkit metadata explicitly for native template loading.
            ReactorApp.RegisterControlAssembly(new global::CommunityToolkit.WinUI.Controls.SettingsControlsRns.
                CommunityToolkit_WinUI_Controls_SettingsControls_XamlTypeInfo.XamlMetaDataProvider());
            var probe = new Probe();
            using var host = H.CreateHost();
            host.RenderErrorHandler = error =>
            {
                Console.Error.WriteLine($"ItemsHost render error: {error.Exception}");
                return null;
            };
            host.Mount(_ => Component<Root, Probe>(probe));
            await Harness.Render();
            await Harness.WaitFor(() => H.FindControl<SettingsExpander>(_ => true) is not null, 30, 25);
            var expander = H.FindControl<SettingsExpander>(_ => true)
                ?? throw new InvalidOperationException("SettingsExpander was not mounted.");
            await Harness.WaitFor(() => H.FindControl<Button>(b => b.Content as string == "A:0") is not null);
            var collection = expander.Items;
            var a = (SettingsCard)collection[0];
            var b = (SettingsCard)collection[1];
            var button = H.FindControl<Button>(c => c.Content as string == "A:0")!;
            var notifications = new List<NotifyCollectionChangedAction>();
            ((INotifyCollectionChanged)collection).CollectionChanged += (_, e) => notifications.Add(e.Action);
            H.Check("ItemsHost_ObservableList", collection is ObservableCollection<object>);

            H.ClickButton("A:0");
            await Harness.Render();
            H.Check("ItemsHost_SelfState", await Harness.WaitFor(() =>
                ReferenceEquals(button, H.FindControl<Button>(c => c.Content as string == "A:1"))));
            H.Check("ItemsHost_NoMutationsForSelfState", notifications.Count == 0);

            probe.SetEnabled(false);
            await Harness.Render();
            H.Check("ItemsHost_DescendantProp", await Harness.WaitFor(() =>
                ReferenceEquals(button, H.FindControl<Button>(c => c.Content as string == "A:1" && !c.IsEnabled))));
            H.Check("ItemsHost_IdentityAndNoReset", ReferenceEquals(a, collection[0]) && notifications.Count == 0);

            probe.SetItems(new[] { "B", "A", "C" });
            await Harness.Render();
            H.Check("ItemsHost_AddedRealization", await Harness.WaitFor(() => H.FindControl<Button>(c => c.Content as string == "C:0") is not null));
            H.Check("ItemsHost_KeyedMoveIdentity", ReferenceEquals(b, collection[0]) && ReferenceEquals(a, collection[1]));
            H.Check("ItemsHost_MoveNotification", notifications.Contains(NotifyCollectionChangedAction.Move));
            H.Check("ItemsHost_NoResetNotification", !notifications.Contains(NotifyCollectionChangedAction.Reset));
            H.Check("ItemsHost_StateSurvivesMove",
                ReferenceEquals(button, H.FindControl<Button>(c => c.Content as string == "A:1")));

            probe.SetItems(new[] { "A", "C" });
            await Harness.Render();
            H.Check("ItemsHost_RemovedRealization", await Harness.WaitFor(() => H.FindControl<Button>(c => c.Content as string == "B:0") is null));
            H.Check("ItemsHost_RemovedCleanupOnce", probe.Cleanups.SequenceEqual(new[] { "B" }));
            H.Check("ItemsHost_CollectionStable", ReferenceEquals(collection, expander.Items));
            probe.RemoveHost();
            await Harness.Render();
            await Harness.WaitFor(() => H.FindText("host removed") is not null);
            H.Check("ItemsHost_TerminalCleanup", probe.Cleanups.Order().SequenceEqual(new[] { "A", "B", "C", "header", "icon" }.Order()), string.Join(",", probe.Cleanups));
            H.Check("ItemsHost_SlotsDetached", expander.Content is null && expander.HeaderIcon is null && collection.Count == 0);
        }
    }
}
