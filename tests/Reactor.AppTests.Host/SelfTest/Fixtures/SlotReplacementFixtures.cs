using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerDsl;
using static Microsoft.UI.Reactor.Factories;
using AdvancedControls = Microsoft.UI.Reactor.Controls;
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
/// must not unmount that control a second time. The nested-slot fixtures cover the same while
/// the update also runs a slot reconcile of its own, before or after unmounting its control, or
/// while a handler inside that nested reconcile unmounts the outer control. The last fixture
/// checks that a slot updated after such a self-unmount still unmounts its own replaced
/// control.</para>
///
/// <para>The custom-control fixtures cover code built on Reactor that hosts a child: the data
/// grid's resize grip, and a <c>RegisterType</c> host written the way the extending-controls
/// guide teaches. Both used to update the child with <c>Reconciler.UpdateChild</c>, which
/// neither checks the element type nor unmounts a control it replaces.</para>
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
    /// that records the control and tears the probe down. The reconciler finds that callback
    /// through the element tag it records on registered controls (#1301), so the registration
    /// does not tag its controls itself.
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
                mount: (r, _, requestRerender) => Build(r, requestRerender),
                update: (r, _, _, control, requestRerender) =>
                {
                    Updates++;
                    return Replaces ? Build(r, requestRerender) : control;
                },
                unmount: (r, control) =>
                {
                    Unmounted.Add(control);
                    if (control.Child is { } child) r.UnmountChild(child);
                });

        private WinXC.Border Build(Reconciler reconciler, Action requestRerender)
        {
            var control = new WinXC.Border
            {
                Child = reconciler.Mount(Component<EffectProbe, ProbeProps>(new ProbeProps(Counts)), requestRerender),
            };
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

    private sealed record NestedHostElement(int Generation) : Element;

    /// <summary>
    /// A <c>RegisterType</c> control in a <c>Border</c> whose <c>update</c> unmounts the control it
    /// was handed and returns a new one, and also runs a nested slot reconcile: it updates a
    /// <c>Border</c> it mounted through the reconciler, which reconciles that <c>Border</c>'s child
    /// through <c>ReconcileV1Child</c> while the outer slot's update is still running. The outer
    /// slot must still see the self-unmount and not unmount the control again, so the
    /// registration's <c>unmount</c> callback runs exactly once. Run in both orders: the nested
    /// reconcile has to hand back the outer slot's control, and the record that it was unmounted.
    /// </summary>
    internal abstract class NestedSlotFixture(Harness h) : SelfTestFixtureBase(h)
    {
        protected abstract string Name { get; }

        /// <summary>True when <c>update</c> unmounts its control before the nested reconcile.</summary>
        protected abstract bool UnmountFirst { get; }

        private readonly List<WinXC.Border> _controls = new();
        private readonly List<WinXC.Border> _unmounted = new();
        private readonly List<FrameworkElement> _parents = new();
        private string? _nestedText;
        private int _updates;

        private static string InnerText(int generation) => $"nested-inner@{generation}";

        private static Element Inner(int generation) => Border(TextBlock(InnerText(generation)));

        private WinXC.Border Build(Reconciler reconciler, int generation, Action requestRerender)
        {
            var control = new WinXC.Border { Child = reconciler.Mount(Inner(generation), requestRerender) };
            _controls.Add(control);
            return control;
        }

        private void NestedSlotUpdate(Reconciler reconciler, WinXC.Border control, int from, int to, Action requestRerender)
        {
            var existing = control.Child;
            var next = reconciler.Reconcile(Inner(from), Inner(to), existing, requestRerender);
            if (!ReferenceEquals(next, existing))
                control.Child = next;
            _nestedText = ((control.Child as WinXC.Border)?.Child as WinXC.TextBlock)?.Text;
        }

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Reconciler.RegisterType<NestedHostElement, WinXC.Border>(
                mount: (r, element, requestRerender) => Build(r, element.Generation, requestRerender),
                update: (r, oldEl, newEl, control, requestRerender) =>
                {
                    _updates++;
                    if (!UnmountFirst)
                        NestedSlotUpdate(r, control, oldEl.Generation, newEl.Generation, requestRerender);
                    r.UnmountChild(control);
                    var replacement = Build(r, UnmountFirst ? oldEl.Generation : newEl.Generation, requestRerender);
                    if (UnmountFirst)
                        NestedSlotUpdate(r, replacement, oldEl.Generation, newEl.Generation, requestRerender);
                    return replacement;
                },
                unmount: (r, control) =>
                {
                    _unmounted.Add(control);
                    if (control.Child is { } child) r.UnmountChild(child);
                });

            var label = $"{Name} rerender";
            host.Mount(ctx =>
            {
                var (generation, setGeneration) = ctx.UseState(0);
                return VStack(
                    Button(label, () => setGeneration(generation + 1)),
                    Border(new NestedHostElement(generation)).OnMount(_parents.Add));
            });

            H.Check($"{Name}_Mounted",
                await Harness.WaitFor(() => _parents.Count == 1 && H.FindText(InnerText(0)) is not null));
            if (_parents.Count != 1 || _controls.Count != 1) return;
            var parent = (WinXC.Border)_parents[0];

            H.ClickButton(label);
            H.Check($"{Name}_Updated", await Harness.WaitFor(() => _updates == 1), $"update ran {_updates} times");
            // Exact counts below: give a second unmount a pass to show up.
            await Harness.Render();

            H.Check($"{Name}_NestedSlotReconciled", _nestedText == InnerText(1), $"nested child reads '{_nestedText}'");
            H.Check($"{Name}_ReplacementInSlot",
                _parents.Count == 1 && _controls.Count == 2 && ReferenceEquals(parent.Child, _controls[1]),
                $"parent mounted {_parents.Count} times, {_controls.Count} controls built");
            H.Check($"{Name}_OldControlUnmountedOnce",
                _unmounted.Count == 1 && ReferenceEquals(_unmounted[0], _controls[0]),
                $"unmount callback ran for "
                + (_unmounted.Count == 0 ? "none" : string.Join(", ", _unmounted.Select(c => $"#{_controls.IndexOf(c)}"))));
        }
    }

    internal sealed class NestedSlotThenSelfUnmount(Harness h) : NestedSlotFixture(h)
    {
        protected override string Name => "SlotReplace_NestedSlotThenSelfUnmount";
        protected override bool UnmountFirst => false;
    }

    internal sealed class SelfUnmountThenNestedSlot(Harness h) : NestedSlotFixture(h)
    {
        protected override string Name => "SlotReplace_SelfUnmountThenNestedSlot";
        protected override bool UnmountFirst => true;
    }

    private sealed record OuterHostElement(int Generation) : Element;

    private sealed record InnerProbeElement(int Generation) : Element;

    /// <summary>
    /// The outer control is unmounted from inside a nested slot update rather than by its own
    /// <c>update</c>. A <c>RegisterType</c> control in a <c>Border</c> updates a <c>Border</c> it
    /// mounted, whose child is a second registered element; that child's <c>update</c>, running in
    /// the nested <c>ReconcileV1Child</c>, unmounts the outer control. The outer update then returns
    /// a replacement. The outer slot must see that its control was already unmounted, so the outer
    /// registration's <c>unmount</c> callback runs exactly once.
    /// </summary>
    internal sealed class NestedHandlerUnmountsOuter(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "SlotReplace_NestedHandlerUnmountsOuter";

        private readonly List<WinXC.Border> _outerControls = new();
        private readonly List<WinXC.Border> _outerUnmounted = new();
        private readonly List<FrameworkElement> _parents = new();
        private WinXC.Border? _updatingOuter;
        private int _innerUnmountsOfOuter;
        private int _outerUpdates;

        private static Element Inner(int generation) => Border(new InnerProbeElement(generation));

        private WinXC.Border BuildOuter(Reconciler reconciler, int generation, Action requestRerender)
        {
            var control = new WinXC.Border { Child = reconciler.Mount(Inner(generation), requestRerender) };
            _outerControls.Add(control);
            return control;
        }

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Reconciler.RegisterType<OuterHostElement, WinXC.Border>(
                mount: (r, element, requestRerender) => BuildOuter(r, element.Generation, requestRerender),
                update: (r, oldEl, newEl, control, requestRerender) =>
                {
                    _outerUpdates++;
                    _updatingOuter = control;
                    try
                    {
                        var existing = control.Child;
                        var next = r.Reconcile(Inner(oldEl.Generation), Inner(newEl.Generation), existing, requestRerender);
                        if (!ReferenceEquals(next, existing))
                            control.Child = next;
                    }
                    finally { _updatingOuter = null; }
                    return BuildOuter(r, newEl.Generation, requestRerender);
                },
                unmount: (r, control) =>
                {
                    _outerUnmounted.Add(control);
                    if (control.Child is { } child) r.UnmountChild(child);
                });
            host.Reconciler.RegisterType<InnerProbeElement, WinXC.TextBlock>(
                mount: (_, element, _) => new WinXC.TextBlock { Text = $"nested-probe@{element.Generation}" },
                update: (r, _, element, text, _) =>
                {
                    text.Text = $"nested-probe@{element.Generation}";
                    if (_updatingOuter is { } outer)
                    {
                        _innerUnmountsOfOuter++;
                        r.UnmountChild(outer);
                    }
                    return null;
                });

            var label = $"{Name} rerender";
            host.Mount(ctx =>
            {
                var (generation, setGeneration) = ctx.UseState(0);
                return VStack(
                    Button(label, () => setGeneration(generation + 1)),
                    Border(new OuterHostElement(generation)).OnMount(_parents.Add));
            });

            H.Check($"{Name}_Mounted",
                await Harness.WaitFor(() => _parents.Count == 1 && H.FindText("nested-probe@0") is not null));
            if (_parents.Count != 1 || _outerControls.Count != 1) return;
            var parent = (WinXC.Border)_parents[0];

            H.ClickButton(label);
            H.Check($"{Name}_Updated", await Harness.WaitFor(() => _outerUpdates == 1), $"outer update ran {_outerUpdates} times");
            // Exact counts below: give a second unmount a pass to show up.
            await Harness.Render();

            H.Check($"{Name}_NestedHandlerUnmountedOuter", _innerUnmountsOfOuter == 1,
                $"the nested update unmounted the outer control {_innerUnmountsOfOuter} times");
            H.Check($"{Name}_ReplacementInSlot",
                _parents.Count == 1 && _outerControls.Count == 2 && ReferenceEquals(parent.Child, _outerControls[1]),
                $"parent mounted {_parents.Count} times, {_outerControls.Count} outer controls built");
            H.Check($"{Name}_OuterUnmountedOnce",
                _outerUnmounted.Count == 1 && ReferenceEquals(_outerUnmounted[0], _outerControls[0]),
                "outer unmount callback ran for "
                + (_outerUnmounted.Count == 0 ? "none" : string.Join(", ", _outerUnmounted.Select(c => $"#{_outerControls.IndexOf(c)}"))));
        }
    }

    /// <summary>
    /// Two sibling slots reconciled one after the other use the same frame of the slot-update
    /// watch. In the first, a <c>Flyout</c>'s Target changes element type and the flyout's update
    /// unmounts the old Target itself. In the second, a registered control's <c>update</c> returns
    /// a new control, which only the slot unmounts. The second slot must not inherit the first's
    /// record of an unmount, so each old registered control gets its <c>unmount</c> exactly once.
    /// </summary>
    internal sealed class FrameReusedAfterSelfUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "SlotReplace_FrameReusedAfterSelfUnmount";

        private readonly HostedRegistration _registration = new() { Replaces = true };
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
                    Border(Flyout(target, TextBlock($"{Name}-content"))),
                    Border(new HostedElement(generation)).OnMount(_parents.Add));
            });

            H.Check($"{Name}_Mounted",
                await Harness.WaitFor(() => _parents.Count == 1 && registration.Controls.Count == 2),
                registration.Describe());
            if (_parents.Count != 1 || registration.Controls.Count != 2) return;
            var parent = (WinXC.Border)_parents[0];
            var siblingOld = (WinXC.Border)parent.Child;
            var targetOld = registration.Controls.Single(c => !ReferenceEquals(c, siblingOld));

            H.ClickButton(label);
            H.Check($"{Name}_Updated",
                await Harness.WaitFor(() => H.FindText(newTarget) is not null && registration.Updates == 1),
                registration.Describe());
            // Exact counts below: give a second unmount a pass to show up.
            await Harness.Render();

            H.Check($"{Name}_SiblingReplaced",
                registration.Controls.Count == 3 && ReferenceEquals(parent.Child, registration.Controls[2]),
                registration.Describe());
            H.Check($"{Name}_EachOldControlUnmountedOnce",
                registration.Unmounted.Count == 2
                && registration.Unmounted.Count(c => ReferenceEquals(c, targetOld)) == 1
                && registration.Unmounted.Count(c => ReferenceEquals(c, siblingOld)) == 1,
                registration.Describe());
        }
    }

    /// <summary>
    /// A custom control that hosts one child element, taken through every way the child can
    /// leave: an update that builds a new control (a <c>ValidationVisualizer</c> remounts on
    /// update), a change of element type (to a <c>TextBlock</c>), and removal; then a child is
    /// added back. Each old child must be unmounted exactly once, the type change must not throw,
    /// and the host control must stay.
    /// </summary>
    internal abstract class CustomHostChildFixture(Harness h) : SelfTestFixtureBase(h)
    {
        protected abstract string Name { get; }

        /// <summary>Registers the host control on the test host's reconciler, if it needs it.</summary>
        protected abstract void Prepare(Reconciler reconciler);

        /// <summary>The host element, holding <paramref name="child"/>, or no child when null.</summary>
        protected abstract Element Host(Element? child);

        /// <summary>The control the host currently holds as its child.</summary>
        protected abstract UIElement? Child(FrameworkElement host);

        private readonly Counts _counts = new();
        private readonly List<FrameworkElement> _hosts = new();
        private readonly List<FrameworkElement> _mounted = new();
        private readonly List<FrameworkElement> _unmounted = new();
        private readonly List<FrameworkElement> _textUnmounted = new();

        private string Text => $"{Name}-text";

        private string ReaddedText => $"{Name}-readded";

        private Element? ChildAt(int step) => step switch
        {
            0 or 1 => ValidationVisualizer(
                    VisualizerStyle.Inline,
                    Component<EffectProbe, ProbeProps>(new ProbeProps(_counts)),
                    title: $"{Name} generation {step}")
                .OnMount(_mounted.Add)
                .OnUnmount(_unmounted.Add),
            2 => TextBlock(Text).OnUnmount(_textUnmounted.Add),
            4 => TextBlock(ReaddedText),
            _ => null,
        };

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Prepare(host.Reconciler);

            var label = $"{Name} next";
            host.Mount(ctx =>
            {
                var (step, setStep) = ctx.UseState(0);
                return VStack(
                    Button(label, () => setStep(step + 1)),
                    Host(ChildAt(step)).OnMount(_hosts.Add));
            });

            H.Check($"{Name}_Mounted", await Harness.WaitFor(() => _hosts.Count == 1 && _mounted.Count == 1), Describe());
            if (_hosts.Count != 1 || _mounted.Count != 1) return;
            var hostControl = _hosts[0];
            H.Check($"{Name}_Mount_ChildInHost", ReferenceEquals(Child(hostControl), _mounted[0]), Describe());

            H.ClickButton(label);
            H.Check($"{Name}_Replaced", await Harness.WaitFor(() => _mounted.Count == 2), Describe());
            // Exact counts below: give a second unmount a pass to show up.
            await Harness.Render();
            H.Check($"{Name}_Replaced_OldChildUnmountedOnce",
                _unmounted.Count == 1 && ReferenceEquals(_unmounted[0], _mounted[0])
                && _counts.Mounts == 2 && _counts.Cleanups == 1
                && ReferenceEquals(Child(hostControl), _mounted[^1]),
                Describe());

            H.ClickButton(label);
            H.Check($"{Name}_TypeChanged",
                await Harness.WaitFor(() => Child(hostControl) is WinXC.TextBlock { Text: var text } && text == Text),
                Describe());
            await Harness.Render();
            H.Check($"{Name}_TypeChanged_OldChildUnmountedOnce",
                _unmounted.Count == 2 && ReferenceEquals(_unmounted[1], _mounted[1]) && _counts.Cleanups == 2,
                Describe());

            H.ClickButton(label);
            H.Check($"{Name}_Removed", await Harness.WaitFor(() => Child(hostControl) is null), Describe());
            await Harness.Render();
            H.Check($"{Name}_Removed_ChildUnmountedOnce", _textUnmounted.Count == 1, Describe());

            H.ClickButton(label);
            H.Check($"{Name}_Readded",
                await Harness.WaitFor(() => Child(hostControl) is WinXC.TextBlock { Text: var readded } && readded == ReaddedText),
                Describe());
            H.Check($"{Name}_HostKept", _hosts.Count == 1, Describe());
        }

        private string Describe()
        {
            var child = _hosts.Count == 0 ? null : Child(_hosts[0]);
            var held = child switch
            {
                null => "nothing",
                WinXC.TextBlock text => $"TextBlock '{text.Text}'",
                FrameworkElement fe when _mounted.IndexOf(fe) is var i and >= 0 => $"visualizer #{i}",
                _ => child.GetType().Name,
            };
            return $"host mounted {_hosts.Count} times and holds {held}; visualizers mounted {_mounted.Count}, "
                + $"unmounted [{string.Join(", ", _unmounted.Select(c => $"#{_mounted.IndexOf(c)}"))}]; "
                + $"text unmounted {_textUnmounted.Count} times; effect mounts {_counts.Mounts}, cleanups {_counts.Cleanups}";
        }
    }

    /// <summary>The data grid's resize grip, an <c>IElementHandler</c> with an optional child.</summary>
    internal sealed class ResizeGripChild(Harness h) : CustomHostChildFixture(h)
    {
        protected override string Name => "SlotReplace_ResizeGripChild";

        protected override void Prepare(Reconciler reconciler) => _ = AdvancedControls.ResizeGripRegistration.Done;

        protected override Element Host(Element? child) => new AdvancedControls.ResizeGripElement(child);

        protected override UIElement? Child(FrameworkElement host)
        {
            var grip = (WinXC.Grid)host;
            return grip.Children.Count > 0 ? grip.Children[0] : null;
        }
    }

    private sealed record FramedElement(Element Content) : Element;

    /// <summary>
    /// A <c>RegisterType</c> host that updates its child the way the extending-controls guide
    /// teaches: <c>Reconcile</c>, and the returned control installed when it differs.
    /// </summary>
    internal sealed class RegisteredHostReconcilesChild(Harness h) : CustomHostChildFixture(h)
    {
        protected override string Name => "SlotReplace_RegisteredHostReconcilesChild";

        protected override void Prepare(Reconciler reconciler) =>
            reconciler.RegisterType<FramedElement, WinXC.Border>(
                mount: static (r, el, requestRerender) =>
                    new WinXC.Border { Child = r.Mount(el.Content, requestRerender) },
                update: static (r, oldEl, newEl, frame, requestRerender) =>
                {
                    var next = r.Reconcile(oldEl.Content, newEl.Content, frame.Child, requestRerender);
                    if (!ReferenceEquals(next, frame.Child)) frame.Child = next;
                    return null;
                });

        protected override Element Host(Element? child) => new FramedElement(child ?? Empty());

        protected override UIElement? Child(FrameworkElement host) => ((WinXC.Border)host).Child;
    }
}
