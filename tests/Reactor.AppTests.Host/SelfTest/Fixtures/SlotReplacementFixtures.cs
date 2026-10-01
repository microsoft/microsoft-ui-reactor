using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerDsl;
using static Microsoft.UI.Reactor.Factories;
using WinDocs = Microsoft.UI.Xaml.Documents;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// When an update returns a new control for a child, the control it replaced has to be
/// unmounted, whatever kind of parent holds the child. A panel's child reconciler always did
/// this. <c>Reconciler.ReconcileV1Child</c>, which reconciles the single child of a
/// <c>Border</c>, a named slot, a tab's content, a generated element slot or an inline UI
/// container, installed the new control and dropped the old one without unmounting it. Effect
/// cleanups in the old subtree never ran, its <c>.OnUnmount</c> actions never fired, a
/// <c>ValidationRule</c> in it kept its message, and a ref to a control in it kept pointing at
/// that detached control.
///
/// <para>The replaced child is a <c>ValidationVisualizer</c>: its update remounts it
/// (<c>CompositeLifecycle.UpdateValidationVisualizer</c>), so every re-render hands the slot a
/// new control. Its subtree carries one probe for each of those effects, plus two refs that
/// both renders carry. Unmounting the old control runs after the update has already pointed
/// those refs at the new control, so they must survive it.</para>
///
/// <para>Each fixture also checks that the parent kept its control, and the TabViewItem or
/// InlineUIContainer that holds the slot. A parent that rebuilt its children would unmount the
/// old child itself, and the checks would pass without testing the slot.</para>
///
/// <para>The <c>RegisterType</c> fixtures check that a registration's <c>unmount</c> callback runs
/// exactly once for a control its <c>update</c> replaced, and never for one it patched in place.
/// The flyout fixture covers an update that unmounts the control it replaces by itself: the slot
/// must not unmount that control a second time.</para>
/// </summary>
internal static class SlotReplacementFixtures
{
    private sealed class Counts
    {
        public int Mounts;
        public int Cleanups;
    }

    private sealed record ProbeProps(Counts Counts);

    /// <summary>One effect with no dependencies, so it counts mounted and unmounted instances.</summary>
    private sealed class EffectProbe : Component<ProbeProps>
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                Props.Counts.Mounts++;
                return () => Props.Counts.Cleanups++;
            }, Array.Empty<object>());
            return TextBlock("slot-probe");
        }
    }

    internal abstract class ReplacedChildFixture(Harness h) : SelfTestFixtureBase(h)
    {
        /// <summary>Stem for this fixture's check names.</summary>
        protected abstract string Name { get; }

        /// <summary>Puts the replaced child in the slot under test.</summary>
        protected abstract Element Parent(Element child);

        /// <summary>The control the slot holds, read from the parent's control.</summary>
        protected abstract UIElement? SlotContent(FrameworkElement parent);

        /// <summary>
        /// The object that holds the slot when it is not the parent's control itself: a
        /// rebuild of the parent's children would recreate it without remounting the parent.
        /// </summary>
        protected virtual object? SlotHolder(FrameworkElement parent) => parent;

        private readonly Counts _counts = new();
        private readonly ValidationContext _validation = new();
        private readonly ElementRef _visualizerRef = new();
        private readonly ElementRef _keptRef = new();
        private readonly ElementRef _droppedRef = new();
        private readonly List<FrameworkElement> _parents = new();
        private readonly List<FrameworkElement> _mounted = new();
        private readonly List<FrameworkElement> _unmounted = new();

        private string Field => $"{Name}-field";

        private string RerenderLabel => $"{Name} rerender";

        private string KeptText(int generation) => $"{Name}-kept@{generation}";

        private Element Replaced(int generation) =>
            ValidationVisualizer(
                VisualizerStyle.Inline,
                VStack(
                    Component<EffectProbe, ProbeProps>(new ProbeProps(_counts)),
                    ValidationRule(() => false, "slot rule failed", Field),
                    TextBlock(KeptText(generation)).Ref(_keptRef),
                    generation == 0
                        ? TextBlock($"{Name}-dropped").Ref(_droppedRef)
                        : TextBlock($"{Name}-no-ref")),
                title: $"{Name} generation {generation}")
            .Ref(_visualizerRef)
            .OnMount(_mounted.Add)
            .OnUnmount(_unmounted.Add);

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (generation, setGeneration) = ctx.UseState(0);
                return VStack(
                    Button(RerenderLabel, () => setGeneration(generation + 1)),
                    Parent(Replaced(generation)).OnMount(_parents.Add))
                    .Provide(ValidationContexts.Current, _validation);
            });

            H.Check($"{Name}_Mounted", await Harness.WaitFor(() => _parents.Count == 1 && _mounted.Count == 1));
            if (_parents.Count != 1 || _mounted.Count != 1) return;
            var parent = _parents[0];
            var holder = SlotHolder(parent);
            H.Check($"{Name}_Mount_ChildInSlot", ReferenceEquals(SlotContent(parent), _mounted[0]));
            H.Check($"{Name}_Mount_ProbesArmed",
                _counts.Mounts == 1 && _counts.Cleanups == 0 && _unmounted.Count == 0
                && _validation.GetMessages(Field).Count == 1
                && ReferenceEquals(_visualizerRef.Current, _mounted[0])
                && _keptRef.Current is WinXC.TextBlock { Text: var kept } && kept == KeptText(0)
                && _droppedRef.Current is WinXC.TextBlock,
                Describe());

            for (int generation = 1; generation <= 2; generation++)
            {
                var step = $"{Name}_Rerender{generation}";
                H.ClickButton(RerenderLabel);
                var expected = generation + 1;
                H.Check($"{step}_ChildRemounted", await Harness.WaitFor(() => _mounted.Count == expected),
                    $"the visualizer mounted {_mounted.Count} times, expected {expected}");
                if (_mounted.Count != expected) return;
                // Exact counts below: give a second unmount a pass to show up.
                await Harness.Render();

                var current = _mounted[generation];
                var previous = _mounted[generation - 1];
                H.Check($"{step}_ParentKept",
                    _parents.Count == 1 && ReferenceEquals(SlotHolder(parent), holder),
                    $"parent mounted {_parents.Count} times; slot holder kept: {ReferenceEquals(SlotHolder(parent), holder)}");
                H.Check($"{step}_NewControlInSlot",
                    !ReferenceEquals(current, previous) && ReferenceEquals(SlotContent(parent), current));

                H.Check($"{step}_OldEffectCleanedUp",
                    _counts.Mounts == expected && _counts.Cleanups == generation, Describe());
                H.Check($"{step}_OldOnUnmountRan",
                    _unmounted.Count == generation && ReferenceEquals(_unmounted[^1], previous), Describe());
                H.Check($"{step}_OldRuleWithdrawn", _validation.GetMessages(Field).Count == 1, Describe());
                H.Check($"{step}_DroppedRefCleared", _droppedRef.Current is null, Describe());
                H.Check($"{step}_RefsFollowReplacement",
                    ReferenceEquals(_visualizerRef.Current, current)
                    && _keptRef.Current is WinXC.TextBlock { Text: var text } && text == KeptText(generation),
                    Describe());
            }
        }

        private string Describe() =>
            $"effect mounts {_counts.Mounts}, cleanups {_counts.Cleanups}; .OnUnmount ran {_unmounted.Count} times; "
            + $"rule messages {_validation.GetMessages(Field).Count}; "
            + $"visualizer ref {Label(_visualizerRef.Current)}, kept ref {Label(_keptRef.Current)}, "
            + $"dropped ref {Label(_droppedRef.Current)}";

        private string Label(FrameworkElement? control) => control switch
        {
            null => "null",
            WinXC.TextBlock text => $"TextBlock '{text.Text}'",
            _ when _mounted.IndexOf(control) is var index and >= 0 => $"visualizer #{index}",
            _ => control.GetType().Name,
        };
    }

    /// <summary><c>Border</c>: the <c>SingleContent</c> children strategy.</summary>
    internal sealed class InBorder(Harness h) : ReplacedChildFixture(h)
    {
        protected override string Name => "SlotReplace_Border";
        protected override Element Parent(Element child) => Border(child);
        protected override UIElement? SlotContent(FrameworkElement parent) => ((WinXC.Border)parent).Child;
    }

    /// <summary><c>SplitView.Pane</c>: the <c>NamedSlots</c> children strategy.</summary>
    internal sealed class InSplitViewPane(Harness h) : ReplacedChildFixture(h)
    {
        protected override string Name => "SlotReplace_SplitViewPane";
        protected override Element Parent(Element child) => SplitView(pane: child, content: TextBlock("slot-split-content"));
        protected override UIElement? SlotContent(FrameworkElement parent) => ((WinXC.SplitView)parent).Pane;
    }

    /// <summary>A <c>TabView</c> tab's content: the <c>TabItemsHost</c> children strategy.</summary>
    internal sealed class InTabViewItem(Harness h) : ReplacedChildFixture(h)
    {
        protected override string Name => "SlotReplace_TabViewItem";
        protected override Element Parent(Element child) => TabView(Tab("slot-tab", child));
        protected override UIElement? SlotContent(FrameworkElement parent) => Item(parent).Content as UIElement;
        protected override object? SlotHolder(FrameworkElement parent) => Item(parent);

        private static WinXC.TabViewItem Item(FrameworkElement parent) =>
            (WinXC.TabViewItem)((WinXC.TabView)parent).TabItems[0];
    }

    /// <summary><c>TabView.TabStripHeader</c>: a generated <c>[WrapElementSlot]</c> slot.</summary>
    internal sealed class InTabStripHeader(Harness h) : ReplacedChildFixture(h)
    {
        protected override string Name => "SlotReplace_TabStripHeader";
        protected override Element Parent(Element child) =>
            TabView(Tab("slot-tab", TextBlock("slot-tab-body"))).TabStripHeader(child);
        protected override UIElement? SlotContent(FrameworkElement parent) =>
            ((WinXC.TabView)parent).TabStripHeader as UIElement;
    }

    /// <summary>A <c>RichTextBlock</c>'s <c>InlineUI(...)</c> child, reconciled in place.</summary>
    internal sealed class InInlineUIContainer(Harness h) : ReplacedChildFixture(h)
    {
        protected override string Name => "SlotReplace_InlineUIContainer";
        protected override Element Parent(Element child) =>
            RichTextBlock(new[] { Paragraph(Run("slot-inline "), InlineUI(child)) });
        protected override UIElement? SlotContent(FrameworkElement parent) => Container(parent).Child;
        protected override object? SlotHolder(FrameworkElement parent) => Container(parent);

        private static WinDocs.InlineUIContainer Container(FrameworkElement parent) =>
            (WinDocs.InlineUIContainer)((WinDocs.Paragraph)((WinXC.RichTextBlock)parent).Blocks[0]).Inlines[1];
    }

    /// <summary>
    /// A panel child, which <c>ChildReconciler</c> already unmounted when it was replaced: the
    /// same probes, as a control. Only the ref checks depend on this change here.
    /// </summary>
    internal sealed class InPanel(Harness h) : ReplacedChildFixture(h)
    {
        protected override string Name => "SlotReplace_Panel";
        protected override Element Parent(Element child) => VStack(child);
        protected override UIElement? SlotContent(FrameworkElement parent) => ((WinXC.Panel)parent).Children[0];
    }

    private sealed record HostedElement(int Generation) : Element;

    /// <summary>
    /// A <c>RegisterType</c> registration for <see cref="HostedElement"/>: a <c>Border</c> hosting
    /// an <see cref="EffectProbe"/> mounted through the reconciler, with an <c>unmount</c> callback
    /// that records the control and tears the probe down. The registration tags its controls
    /// itself, as <c>XamlInterop</c> does: the reconciler finds the <c>unmount</c> callback through
    /// the tag, and it tags registered controls on its own only once #1301 lands.
    /// </summary>
    private sealed class HostedRegistration
    {
        public readonly Counts Counts = new();
        public readonly List<WinXC.Border> Controls = new();
        public readonly List<WinXC.Border> Unmounted = new();
        public int Updates;

        /// <summary>True when <c>update</c> returns a new control, false when it patches the one it got.</summary>
        public bool Replaces;

        public void Register(Reconciler reconciler) =>
            reconciler.RegisterType<HostedElement, WinXC.Border>(
                mount: (r, element, requestRerender) => Build(r, element, requestRerender),
                update: (r, _, element, control, requestRerender) =>
                {
                    Updates++;
                    if (Replaces) return Build(r, element, requestRerender);
                    Reconciler.SetElementTag(control, element);
                    return control;
                },
                unmount: (r, control) =>
                {
                    Unmounted.Add(control);
                    if (control.Child is { } child) r.UnmountChild(child);
                });

        private WinXC.Border Build(Reconciler reconciler, HostedElement element, Action requestRerender)
        {
            var control = new WinXC.Border
            {
                Child = reconciler.Mount(Component<EffectProbe, ProbeProps>(new ProbeProps(Counts)), requestRerender),
            };
            Reconciler.SetElementTag(control, element);
            Controls.Add(control);
            return control;
        }

        public string Describe() =>
            $"{Controls.Count} controls built, update ran {Updates} times, unmount callback ran for "
            + (Unmounted.Count == 0 ? "none" : string.Join(", ", Unmounted.Select(c => $"#{Controls.IndexOf(c)}")))
            + $"; effect mounts {Counts.Mounts}, cleanups {Counts.Cleanups}";
    }

    /// <summary>
    /// A <c>RegisterType</c> control in a <c>Border</c>. Its <c>update</c> either hands back the
    /// control it was given, which means it patched it in place, or returns a new control. The
    /// registration's <c>unmount</c> callback must run for the replaced control, exactly once, and
    /// never for a control that stays mounted.
    /// </summary>
    internal abstract class RegisteredTypeFixture(Harness h) : SelfTestFixtureBase(h)
    {
        protected abstract string Name { get; }

        /// <summary>True when <c>update</c> returns a new control, false when it returns the one it got.</summary>
        protected abstract bool Replaces { get; }

        private readonly HostedRegistration _registration = new();
        private readonly List<FrameworkElement> _parents = new();

        public override async Task RunAsync()
        {
            var registration = _registration;
            registration.Replaces = Replaces;
            var host = H.CreateHost();
            registration.Register(host.Reconciler);

            var label = $"{Name} rerender";
            host.Mount(ctx =>
            {
                var (generation, setGeneration) = ctx.UseState(0);
                return VStack(
                    Button(label, () => setGeneration(generation + 1)),
                    Border(new HostedElement(generation)).OnMount(_parents.Add));
            });

            H.Check($"{Name}_Mounted",
                await Harness.WaitFor(() => _parents.Count == 1 && registration.Counts.Mounts == 1),
                registration.Describe());
            if (_parents.Count != 1 || registration.Controls.Count != 1) return;
            var parent = (WinXC.Border)_parents[0];
            H.Check($"{Name}_Mount_ControlInSlot", ReferenceEquals(parent.Child, registration.Controls[0]));

            H.ClickButton(label);
            H.Check($"{Name}_Updated", await Harness.WaitFor(() => registration.Updates == 1), registration.Describe());
            // Exact counts below: give a second unmount a pass to show up.
            await Harness.Render();

            H.Check($"{Name}_ParentKept", _parents.Count == 1);
            H.Check($"{Name}_ExpectedControlInSlot",
                registration.Controls.Count == (Replaces ? 2 : 1)
                && ReferenceEquals(parent.Child, registration.Controls[^1]),
                registration.Describe());
            if (Replaces)
                H.Check($"{Name}_OldControlUnmountedOnce",
                    registration.Unmounted.Count == 1
                    && ReferenceEquals(registration.Unmounted[0], registration.Controls[0])
                    && registration.Counts.Mounts == 2 && registration.Counts.Cleanups == 1,
                    registration.Describe());
            else
                H.Check($"{Name}_PatchedControlStaysMounted",
                    registration.Unmounted.Count == 0
                    && registration.Counts.Mounts == 1 && registration.Counts.Cleanups == 0,
                    registration.Describe());
        }
    }

    internal sealed class RegisteredUpdateReturnsSameControl(Harness h) : RegisteredTypeFixture(h)
    {
        protected override string Name => "SlotReplace_RegisteredSameControl";
        protected override bool Replaces => false;
    }

    internal sealed class RegisteredUpdateReturnsNewControl(Harness h) : RegisteredTypeFixture(h)
    {
        protected override string Name => "SlotReplace_RegisteredNewControl";
        protected override bool Replaces => true;
    }

    /// <summary>
    /// A <c>Flyout</c> in a <c>Border</c> whose Target changes element type. The flyout's update
    /// unmounts the old Target itself before it returns the new one
    /// (<c>OverlayLifecycle.UpdateFlyoutElement</c>), so the slot must not unmount it again: a
    /// second pass would run the <c>unmount</c> callback of the registered control inside the old
    /// Target twice.
    /// </summary>
    internal sealed class FlyoutTargetTypeChange(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "SlotReplace_FlyoutTargetTypeChange";

        private readonly HostedRegistration _registration = new();
        private readonly List<FrameworkElement> _parents = new();

        public override async Task RunAsync()
        {
            var registration = _registration;
            var host = H.CreateHost();
            registration.Register(host.Reconciler);

            var label = $"{Name} rerender";
            var newTarget = $"{Name}-new-target";
            host.Mount(ctx =>
            {
                var (generation, setGeneration) = ctx.UseState(0);
                Element target = generation == 0
                    ? VStack(new HostedElement(generation))
                    : TextBlock(newTarget);
                return VStack(
                    Button(label, () => setGeneration(generation + 1)),
                    Border(Flyout(target, TextBlock($"{Name}-content"))).OnMount(_parents.Add));
            });

            H.Check($"{Name}_Mounted",
                await Harness.WaitFor(() => _parents.Count == 1 && registration.Counts.Mounts == 1),
                registration.Describe());
            if (_parents.Count != 1 || registration.Controls.Count != 1) return;
            var parent = (WinXC.Border)_parents[0];
            H.Check($"{Name}_Mount_TargetInSlot", parent.Child is WinXC.Panel);

            H.ClickButton(label);
            H.Check($"{Name}_TargetReplaced",
                await Harness.WaitFor(() => parent.Child is WinXC.TextBlock { Text: var text } && text == newTarget),
                $"slot holds {parent.Child?.GetType().Name ?? "null"}");
            // Exact counts below: give a second unmount a pass to show up.
            await Harness.Render();

            H.Check($"{Name}_ParentKept", _parents.Count == 1);
            H.Check($"{Name}_OldTargetUnmountedOnce",
                registration.Unmounted.Count == 1 && ReferenceEquals(registration.Unmounted[0], registration.Controls[0])
                && registration.Counts.Cleanups == 1,
                registration.Describe());
        }
    }
}
