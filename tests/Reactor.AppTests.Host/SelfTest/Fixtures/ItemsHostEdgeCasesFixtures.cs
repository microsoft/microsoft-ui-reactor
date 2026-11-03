using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// No Items, key, setters or parent callbacks: property teardown alone must root this wrapper.
public sealed partial class ItemsHostSlotControl : Control
{
    public object? Badge { get; set; } = new TextBlock { Text = "constructor default" };
}

[GenerateReactorWrapper(typeof(ItemsHostSlotControl), RegisterAssembly = false)]
[WrapElementSlot("Badge")]
internal partial record ItemsHostSlotElement;

internal static class ItemsHostEdgeCasesFixtures
{
    private static readonly Action Noop = static () => { };
    private static readonly Context<int> Revision = new(0);

    private sealed class ContextProbe
    {
        internal Action<int> SetRevision = null!;
        internal int Renders;
        internal object[] Items = null!;
    }

    private sealed class ContextRow : Component<ContextProbe>
    {
        public override Element Render()
        {
            Props.Renders++;
            return TextBlock($"stable-context:{UseContext(Revision)}");
        }
    }

    private sealed class ContextRoot : Component<ContextProbe>
    {
        public override Element Render()
        {
            var (revision, set) = UseState(0);
            Props.SetRevision = set;
            // Both the array and the Component element in it stay reference-identical.
            return ListBoxWrapperElement.ListBox(items: Props.Items)
                .Margin(revision).Provide(Revision, revision);
        }
    }

    internal sealed class SameArrayContext(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new ContextProbe();
            probe.Items = [Component<ContextRow, ContextProbe>(probe)];
            using var host = H.CreateHost();
            host.Mount(_ => Component<ContextRoot, ContextProbe>(probe));
            await Harness.WaitFor(() => H.FindText("stable-context:0") is not null);
            var original = H.FindText("stable-context:0");
            H.Check("ItemsHost_SameArray_ContextMounted", original is not null && probe.Renders == 1);
            probe.SetRevision(7);
            await Harness.Render();
            H.Check("ItemsHost_SameArray_ContextRerendered", await Harness.WaitFor(() =>
                ReferenceEquals(original, H.FindText("stable-context:7"))) && probe.Renders >= 2);
        }
    }

    internal sealed class NativeItemsAndSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            var parent = new Grid();
            H.SetContent(parent);
            Element old = ListBoxWrapperElement.ListBox(items: ["a", "b", "a"]);
            var list = (ListBox)reconciler.Mount(old, Noop)!;
            parent.Children.Add(list);
            await Harness.Render();
            Update(["b", "a"]);
            H.Check("ItemsHost_Native_DuplicatesAndMiddleRemoval", list.Items.Cast<object>().SequenceEqual(new object[] { "b", "a" }));
            Update(["c", "b", "a"]);
            Update(["a", "c", "b"]);
            H.Check("ItemsHost_Native_Reorder", list.Items.Cast<object>().SequenceEqual(new object[] { "a", "c", "b" }));
            reconciler.UnmountChild(list);
            parent.Children.Remove(list);

            int callbacks = 0;
            var comboEl = ComboBox(new[] { "first", "second" }, selectedIndex: 0,
                onSelectedIndexChanged: _ => callbacks++);
            var combo = (ComboBox)reconciler.Mount(comboEl, Noop)!;
            parent.Children.Add(combo);
            await Harness.Render();
            callbacks = 0;
            combo.SelectedIndex = 1;
            H.Check("ItemsHost_Selection_PositiveControl", callbacks == 1 && combo.SelectedIndex == 1);
            callbacks = 0;
            reconciler.UnmountChild(combo);
            parent.Children.Remove(combo);
            await Harness.Render();
            H.Check("ItemsHost_Selection_NoTerminalCallback", callbacks == 0 && combo.Items.Count == 0);

            void Update(object[] items)
            {
                Element next = ListBoxWrapperElement.ListBox(items: items);
                reconciler.UpdateChild(old, next, list, Noop);
                old = next;
            }
        }
    }

    internal sealed class UnkeyedTypeAndPool(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            var parent = new Grid();
            H.SetContent(parent);
            var cleanups = new List<string>();
            var parented = new List<bool>();
            Element Item(string id) => TextBlock(id).OnUnmount(c =>
            {
                cleanups.Add(id);
                parented.Add(VisualTreeHelper.GetParent(c) is not null);
            });
            Element old = ListBoxWrapperElement.ListBox(items: [Item("first"), Item("middle"), Item("tail")]);
            var list = (ListBox)reconciler.Mount(old, Noop)!;
            parent.Children.Add(list);
            await Harness.WaitFor(() => H.FindText("tail") is not null);
            var first = list.Items[0];
            var middle = list.Items[1];
            Update([Item("first"), Item("tail")]);
            await Harness.Render();
            H.Check("ItemsHost_Unkeyed_PositionalIdentity", list.Items.Count == 2 &&
                ReferenceEquals(first, list.Items[0]) && ReferenceEquals(middle, list.Items[1]) &&
                ReferenceEquals(middle, H.FindText("tail")));
            H.Check("ItemsHost_Removal_UnmountWhileParented", cleanups.SequenceEqual(new[] { "tail" }) && parented.SequenceEqual(new[] { true }));

            Update([Item("keyed text").WithKey("changing")]);
            await Harness.WaitFor(() => H.FindText("keyed text") is not null);
            var replaced = (TextBlock)list.Items[0];
            cleanups.Clear();
            parented.Clear();
            Update([Border(TextBlock("keyed border")).WithKey("changing")]);
            await Harness.Render();
            H.Check("ItemsHost_SameKey_TypeReplacement", list.Items.Count == 1 && list.Items[0] is Border &&
                !ReferenceEquals(replaced, list.Items[0]) && H.FindText("keyed border") is not null);
            H.Check("ItemsHost_Replacement_UnmountWhileParented", cleanups.SequenceEqual(new[] { "keyed text" }) && parented.SequenceEqual(new[] { true }));

            // UnmountChild intentionally does not pool. Exercise the actual return/rent
            // seam only after replacement has detached the retired control.
            reconciler.ReturnControl(replaced);
            Update([Item("rented again").WithKey("rented")]);
            await Harness.Render();
            H.Check("ItemsHost_Pool_RerentDetachedControl", ReferenceEquals(replaced, list.Items[0]) &&
                ReferenceEquals(replaced, H.FindText("rented again")));
            cleanups.Clear();
            parented.Clear();
            reconciler.UnmountChild(list);
            H.Check("ItemsHost_Terminal_UnmountWhileParented", cleanups.SequenceEqual(new[] { "rented again" }) &&
                parented.SequenceEqual(new[] { true }) && list.Items.Count == 0);
            parent.Children.Remove(list);

            void Update(object[] items)
            {
                Element next = ListBoxWrapperElement.ListBox(items: items);
                reconciler.UpdateChild(old, next, list, Noop);
                old = next;
            }
        }
    }

    internal sealed class SlotRootingAndDefaults(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var reconciler = new Reconciler();
            int cleanup = 0;
            bool stillOwned = false;
            ItemsHostSlotControl? owner = null;
            var element = new ItemsHostSlotElement
            {
                Badge = TextBlock("owned badge").OnUnmount(c =>
                {
                    cleanup++;
                    stillOwned = ReferenceEquals(c, owner!.Badge);
                }),
            };
            owner = (ItemsHostSlotControl)reconciler.Mount(element, Noop)!;
            H.Check("ItemsHost_Slot_RootedWithoutItemsOrCallbacks", Reconciler.GetElementTag(owner) == element);
            reconciler.UnmountChild(owner);
            H.Check("ItemsHost_Slot_TerminalOwnedTeardown", cleanup == 1 && stillOwned && owner.Badge is null);

            var unset = (ItemsHostSlotControl)reconciler.Mount(new ItemsHostSlotElement(), Noop)!;
            var defaultBadge = unset.Badge;
            reconciler.UnmountChild(unset);
            H.Check("ItemsHost_Slot_UnsetPreservesConstructorDefault", defaultBadge is TextBlock { Text: "constructor default" } &&
                ReferenceEquals(defaultBadge, unset.Badge));
            return Task.CompletedTask;
        }
    }

    private sealed record RawItem(int Id, string Label);

    private sealed class ResourceRow : Component<string>
    {
        public override Element Render() => TextBlock("items-resource-row").Foreground(Theme.Ref(Props));
    }

    internal sealed class ResourceRefresh(Harness h, bool handCoded) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string key = "ItemsHostResourceRefresh" + handCoded;
            var red = global::Windows.UI.Color.FromArgb(255, 220, 0, 0);
            var blue = global::Windows.UI.Color.FromArgb(255, 0, 0, 220);
            var resources = new ResourceDictionary { [key] = new SolidColorBrush(red) };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();
            using var host = H.CreateHost();
            using var isolation = ThemeResourceListeners.IsolateForTest(host);
            if (handCoded)
                host.Reconciler.RegisterHandler<HandItemsElement, ListBox>(new HandItemsHandler());

            // Empty logical slots precede the live row. The same list and component
            // element are retained, and only the component body contains Theme.Ref.
            object[] items = [null!, Empty(), "raw prefix", Component<ResourceRow, string>(key), TextBlock("literal")];
            Action<int>? setTick = null;
            host.Mount(ctx =>
            {
                var (tick, set) = ctx.UseState(0);
                setTick = set;
                Element list = handCoded ? new HandItemsElement(items) : ListBoxWrapperElement.ListBox(items: items);
                return list.Margin(tick);
            });
            try
            {
                await Harness.WaitFor(() => H.FindText("items-resource-row") is not null);
                var original = H.FindText("items-resource-row");
                var control = H.FindControl<ListBox>(static _ => true)
                    ?? throw new InvalidOperationException("ItemsHost resource fixture did not mount.");
                H.Check("ItemsHost_Resource_InitialRed", original?.Foreground is SolidColorBrush brush && brush.Color == red);
                H.Check("ItemsHost_Resource_LogicalGapsMapped", control.Items.Count == 3);
                control.SelectedIndex = 1;
                var originalItem = control.Items[1];

                resources[key] = new SolidColorBrush(blue);
                setTick!(1);
                await Harness.Render();
                H.Check("ItemsHost_Resource_OrdinaryRenderStaysCached",
                    ReferenceEquals(original, H.FindText("items-resource-row")) &&
                    original?.Foreground is SolidColorBrush cached && cached.Color == red);

                Theme.NotifyResourcesChanged();
                await Harness.WaitFor(() => original?.Foreground is SolidColorBrush current && current.Color == blue);
                H.Check("ItemsHost_Resource_NotifiedRetainedComponentRefreshes",
                    ReferenceEquals(original, H.FindText("items-resource-row")) &&
                    original?.Foreground is SolidColorBrush refreshed && refreshed.Color == blue);
                H.Check("ItemsHost_Resource_CollectionAndSelectionPreserved",
                    control.Items.Count == 3 && control.SelectedIndex == 1 && ReferenceEquals(originalItem, control.Items[1]));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    private sealed record HandItemsElement(IReadOnlyList<object> Items) : Element;
    private sealed class HandItemsHandler : IElementHandler<HandItemsElement, ListBox>
    {
        public ListBox Mount(MountContext ctx, HandItemsElement element) => new();
        public void Update(UpdateContext ctx, HandItemsElement oldEl, HandItemsElement newEl, ListBox control) { }
        public ChildrenStrategy<HandItemsElement, ListBox> Children { get; } = new ItemsHost<HandItemsElement, ListBox>(
            static e => e.Items, static c => c.Items)
        {
            ItemEquals = static (a, b) => a is RawItem x && b is RawItem y ? x.Id == y.Id : Equals(a, b),
        };
    }

    internal sealed class HandHandlerAndRawEquality(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var reconciler = new Reconciler();
            reconciler.RegisterHandler<HandItemsElement, ListBox>(new HandItemsHandler());
            int cleanup = 0;
            var oldRaw = new RawItem(1, "old");
            var newRaw = new RawItem(1, "new");
            Element child = TextBlock("hand child").OnUnmount(_ => cleanup++).WithKey("child");
            var old = new HandItemsElement([oldRaw, child]);
            var control = (ListBox)reconciler.Mount(old, Noop)!;
            H.Check("ItemsHost_HandHandler_CallbackFreeRoot", ReferenceEquals(Reconciler.GetElementTag(control), old));
            var unchanged = new HandItemsElement([newRaw, child]);
            reconciler.UpdateChild(old, unchanged, control, Noop);
            H.Check("ItemsHost_RawEquality_UnchangedRetainsInstance", ReferenceEquals(control.Items[0], oldRaw));
            var changed = new HandItemsElement([child, newRaw, "added"]);
            reconciler.UpdateChild(unchanged, changed, control, Noop);
            H.Check("ItemsHost_RawEquality_StructuralUpdateWritesNewInstance", control.Items.Count == 3 &&
                ReferenceEquals(control.Items[1], newRaw) && control.Items[2] as string == "added");
            reconciler.UnmountChild(control);
            H.Check("ItemsHost_HandHandler_DescendantCleanup", cleanup == 1 && control.Items.Count == 0);

            var rawOnly = new HandItemsElement([oldRaw, "before"]);
            var rawControl = (ListBox)reconciler.Mount(rawOnly, Noop)!;
            var rawChanged = new HandItemsElement([newRaw, "after"]);
            reconciler.UpdateChild(rawOnly, rawChanged, rawControl, Noop);
            H.Check("ItemsHost_RawInPlace_RefreshesEditedIdMatch", rawControl.Items.Count == 2 &&
                ReferenceEquals(rawControl.Items[0], newRaw) && rawControl.Items[1] as string == "after");
            var withNull = new HandItemsElement([null!, "after"]);
            reconciler.UpdateChild(rawChanged, withNull, rawControl, Noop);
            H.Check("ItemsHost_RawInPlace_NullRemovesNativeSlot", rawControl.Items.Count == 1 && rawControl.Items[0] as string == "after");
            reconciler.UpdateChild(withNull, rawChanged, rawControl, Noop);
            H.Check("ItemsHost_RawInPlace_RestoresNativeSlot", rawControl.Items.Count == 2 && ReferenceEquals(rawControl.Items[0], newRaw));
            reconciler.UnmountChild(rawControl);
            return Task.CompletedTask;
        }
    }
}
