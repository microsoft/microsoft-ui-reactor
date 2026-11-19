using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using System.Runtime.CompilerServices;

namespace WctControls;

// ─────────────────────────────────────────────────────────────────────────
//  Windows Community Toolkit controls, turned into first-class Reactor
//  elements by Reactor.Wrappers.Generator.
//
//  Each partial record below is annotated with [GenerateReactorWrapper(...)]
//  naming the WCT control to wrap. The source generator fills in the rest of
//  that same partial: one init-property per surfaced control property, child /
//  items slots, On{Event} callbacks, the ControlDescriptor, Pattern-A
//  registration, and a parameterized factory method named after the control.
//  The string-header helpers below preserve accessible naming for rich slots.
// ─────────────────────────────────────────────────────────────────────────

// A settings "row": Header + Description + an optional HeaderIcon, with a
// content child (a control on the right) and an optional whole-card Click.
// CommandParameter is an inherited ButtonBase prop with no declarative meaning.
//
// Object-valued properties are NOT reconciled Element slots without annotations.
// The string overload keeps a native string Header so the Toolkit retains its
// own group/button/content naming rules. Rich-header callers supply names.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard),
    Exclude = new[] { "CommandParameter" })]
[WrapElementSlot("HeaderIcon")]
[WrapElementSlot("Description")]
[WrapManual("Header")]
[WrapManual("HeaderText")]
public partial record SettingsCardElement
{
    public Element? Header { get; init; }
    public string? HeaderText { get; init; }

    public static SettingsCardElement SettingsCard(
        Element header, Element? description = null, Element? content = null,
        Element? headerIcon = null, Optional<bool> isClickEnabled = default, Action? onClick = null) =>
        SettingsCardElement.SettingsCard(
            description: description, content: content, headerIcon: headerIcon,
            isClickEnabled: isClickEnabled, onClick: onClick) with { Header = header };

    public static SettingsCardElement SettingsCard(
        string header, string? description = null, Element? content = null,
        Element? headerIcon = null, Optional<bool> isClickEnabled = default, Action? onClick = null) =>
        SettingsCardElement.SettingsCard(
            description: description is null ? null : TextBlock(description),
            content: content,
            headerIcon: headerIcon, isClickEnabled: isClickEnabled, onClick: onClick)
            with { HeaderText = header };

    private static partial ControlDescriptor<SettingsCardElement, CommunityToolkit.WinUI.Controls.SettingsCard>
        Customize(ControlDescriptor<SettingsCardElement, CommunityToolkit.WinUI.Controls.SettingsCard> d) =>
        d.WithSettingsHeader(static e => e.Header, static e => e.HeaderText,
            static () => CommunityToolkit.WinUI.Controls.SettingsCard.HeaderProperty);
}

// A settings group that expands to reveal child SettingsCards (its Items).
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsExpander))]
[WrapManual("Header")]
[WrapElementSlot("Description")]
[WrapElementSlot("Content")]
[WrapElementSlot("HeaderIcon")]
[WrapControlled("IsExpanded", Events = new[] { "Expanded", "Collapsed" })]
[WrapManual("HeaderText")]
public partial record SettingsExpanderElement
{
    public Element? Header { get; init; }
    public string? HeaderText { get; init; }

    public static SettingsExpanderElement SettingsExpander(
        Element header, Element? description = null, Element? content = null,
        Element? headerIcon = null, Optional<bool> isExpanded = default,
        Action<bool>? onIsExpandedChanged = null, IReadOnlyList<object>? items = null) =>
        SettingsExpanderElement.SettingsExpander(
            description: description, content: content, headerIcon: headerIcon,
            isExpanded: isExpanded, onIsExpandedChanged: onIsExpandedChanged,
            items: items?.ToArray() ?? []) with { Header = header };

    public static SettingsExpanderElement SettingsExpander(
        string header, string? description = null, Element? content = null,
        Element? headerIcon = null, Optional<bool> isExpanded = default,
        Action<bool>? onIsExpandedChanged = null, IReadOnlyList<object>? items = null) =>
        SettingsExpanderElement.SettingsExpander(
            description: description is null ? null : TextBlock(description),
            content: content,
            headerIcon: headerIcon, isExpanded: isExpanded,
            onIsExpandedChanged: onIsExpandedChanged, items: items?.ToArray() ?? [])
            with { HeaderText = header };

    private static partial ControlDescriptor<SettingsExpanderElement, CommunityToolkit.WinUI.Controls.SettingsExpander>
        Customize(ControlDescriptor<SettingsExpanderElement, CommunityToolkit.WinUI.Controls.SettingsExpander> d) =>
        d.WithSettingsHeader(static e => e.Header, static e => e.HeaderText,
            static () => CommunityToolkit.WinUI.Controls.SettingsExpander.HeaderProperty);
}

internal static class SettingsHeaderBridge
{
    private sealed class HeaderState
    {
        internal UIElement? Child;
        internal object? Value;
    }

    // Keep ownership on the native control even when Header is replaced or its RCW is collected.
    // Register lazily so constructing a descriptor does not touch WinUI.
    private static DependencyProperty? s_ownedHeaderProperty;
    private static DependencyProperty OwnedHeaderProperty => s_ownedHeaderProperty ??=
        DependencyProperty.RegisterAttached(
            "ReactorOwnedSettingsHeader", typeof(object), typeof(SettingsHeaderBridge), new PropertyMetadata(null));

    private static HeaderState? GetOwnedHeader(FrameworkElement control) =>
        s_ownedHeaderProperty is { } property ? control.GetValue(property) as HeaderState : null;

    internal static ControlDescriptor<TElement, TControl> WithSettingsHeader<TElement, TControl>(
        this ControlDescriptor<TElement, TControl> descriptor,
        Func<TElement, Element?> getHeader,
        Func<TElement, string?> getText,
        Func<DependencyProperty> getHeaderProperty)
        where TElement : Element
        where TControl : FrameworkElement, new() =>
        descriptor.ImperativeBridged(
            mount: (ctx, control, element) =>
            {
                var header = getHeader(element);
                var text = getText(element);
                if (header is null && text is null) return;
                var state = new HeaderState();
                control.SetValue(OwnedHeaderProperty, state);
                state.Child = header is null ? null : ctx.MountChild(header);
                state.Value = header is null ? text : state.Child;
                control.SetValue(getHeaderProperty(), state.Value);
            },
            update: (ctx, control, oldElement, newElement) =>
            {
                var oldHeader = getHeader(oldElement);
                var header = getHeader(newElement);
                if (header is not null)
                {
                    var state = GetOwnedHeader(control);
                    var hadHeader = state is not null;
                    if (state is null)
                    {
                        state = new HeaderState();
                        control.SetValue(OwnedHeaderProperty, state);
                    }
                    state.Child = ctx.ReconcileChild(hadHeader ? oldHeader : null, header, state.Child);
                    state.Value = state.Child;
                    if (!ReferenceEquals(control.GetValue(getHeaderProperty()), state.Value))
                        control.SetValue(getHeaderProperty(), state.Value);
                    return;
                }
                var text = getText(newElement);
                if (oldHeader is null && getText(oldElement) == text) return;
                ReleaseHeader(ctx.Reconciler, control, getHeaderProperty(), clearHeader: text is null);
                if (text is not null)
                {
                    control.SetValue(OwnedHeaderProperty, new HeaderState { Value = text });
                    control.SetValue(getHeaderProperty(), text);
                }
            },
            unmount: (ctx, control) =>
                ReleaseHeader(ctx.Reconciler, control, getHeaderProperty(), clearHeader: true));

    private static void ReleaseHeader(
        Reconciler reconciler, FrameworkElement control, DependencyProperty headerProperty, bool clearHeader)
    {
        if (GetOwnedHeader(control) is not { } state) return;
        control.ClearValue(OwnedHeaderProperty);
        if (state.Child is { } child) reconciler.UnmountChild(child);
        var current = control.GetValue(headerProperty);
        if (clearHeader && (ReferenceEquals(current, state.Value) ||
            current is string text && state.Value is string ownedText && text == ownedText))
            control.ClearValue(headerProperty);
    }
}

// A circular gauge — pure value props (Value/Minimum/Maximum/Unit/…) plus a
// ValueChanged event, so it surfaces as a two-way controlled Value.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.RadialGauge))]
public partial record RadialGaugeElement;

// A horizontal segmented selector (like a small tab strip / single-choice group).
// SelectedIndex's change event (SelectionChanged) doesn't follow the
// {Prop}Changed convention, so [WrapControlled] binds it explicitly — making
// SelectedIndex a two-way controlled prop with an OnSelectedIndexChanged callback.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.Segmented))]
[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]
public partial record SegmentedElement;

// A live camera preview. CameraPreview is an IMPERATIVE control — it must be
// started with StartAsync(...) after Loaded and cleaned up on unmount. [WrapLifecycle]
// owns that ordering here; the page's effect requests the same cleanup on host
// disposal. PreviewFailed (no camera, access denied, …) projects to
// OnPreviewFailed via [WrapEvent].
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.CameraPreview))]
[WrapEvent("PreviewFailed", Arg = "Error")]
[WrapLifecycle(nameof(StartPreview), OnUnmounted = nameof(StopPreview))]
public partial record CameraPreviewElement
{
    private static readonly ConditionalWeakTable<CommunityToolkit.WinUI.Controls.CameraPreview, PreviewSession> Previews = new();
    private static readonly CameraPreviewCleanupQueue Cleanup = new();

    internal static CameraPreviewLifetime GetLifetime(CommunityToolkit.WinUI.Controls.CameraPreview control) =>
        Previews.TryGetValue(control, out var session)
            ? session.Lifetime
            : throw new InvalidOperationException("The camera preview is not mounted.");

    private static void StartPreview(CommunityToolkit.WinUI.Controls.CameraPreview control)
    {
        var session = new PreviewSession(control, Cleanup.WaitAsync());
        Cleanup.Track(session.Lifetime);
        Previews.Add(control, session);
        session.Attach();
    }

    internal static void StopPreview(CommunityToolkit.WinUI.Controls.CameraPreview control)
    {
        if (Previews.TryGetValue(control, out var session))
        {
            Previews.Remove(control);
            try
            {
                session.Detach();
            }
            finally
            {
                Cleanup.Retire(session.Lifetime);
            }
        }
    }

    private sealed class PreviewSession
    {
        private readonly CommunityToolkit.WinUI.Controls.CameraPreview _control;
        private readonly CameraPreviewLifetime _lifetime;
        private CommunityToolkit.WinUI.Helpers.CameraHelper? _helper;
        private bool _unmounted;

        public CameraPreviewLifetime Lifetime => _lifetime;

        public PreviewSession(CommunityToolkit.WinUI.Controls.CameraPreview control,
            Task<IReadOnlyList<string>> previousCleanup)
        {
            _control = control;
            _lifetime = new(ReportEarlierAsync(previousCleanup), StartAsync, control.Stop, CleanupAsync, Report,
                () => _control.PreviewFailed -= OnPreviewFailed, SettleAsync);
        }

        private async Task ReportEarlierAsync(Task<IReadOnlyList<string>> previousCleanup)
        {
            foreach (string message in await previousCleanup)
                Report("previous preview", new InvalidOperationException(message));
        }

        // Low priority runs after continuations already queued at normal priority,
        // such as Toolkit's camera-switch UI work that the helper semaphore released.
        private Task SettleAsync()
        {
            var settled = new TaskCompletionSource();
            if (!_control.DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, settled.SetResult))
                settled.SetException(new InvalidOperationException("The camera preview dispatcher is shutting down."));
            return settled.Task;
        }

        public void Attach()
        {
            _control.PreviewFailed += OnPreviewFailed;
            _control.Loaded += OnLoaded;
            if (_control.IsLoaded)
                StartLoadedPreview();
        }

        private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) => StartLoadedPreview();

        private void StartLoadedPreview()
        {
            _control.Loaded -= OnLoaded;
            _lifetime.Start();
        }

        private async Task StartAsync()
        {
            // Before Loaded, StartAsync can return before async-void OnApplyTemplate
            // initializes capture. Apply the template before assigning our owned helper.
            _control.ApplyTemplate();
            _helper = new CommunityToolkit.WinUI.Helpers.CameraHelper();
            await _control.StartAsync(_helper);
        }

        private Task CleanupAsync() => _helper?.CleanUpAsync() ?? Task.CompletedTask;

        public void Detach()
        {
            _unmounted = true;
            _control.Loaded -= OnLoaded;
            // Host disposal can leave the native subtree visible.
            _control.IsEnabled = false;
        }

        private void OnPreviewFailed(object? sender, CommunityToolkit.WinUI.Controls.PreviewFailedEventArgs args)
        {
            Console.Error.WriteLine($"CameraPreview failed: {args.Error}");
            _lifetime.RecordFailure();
            // While mounted, the generated OnPreviewFailed projection delivers this.
            if (_unmounted)
                Cleanup.RecordLate($"preview: {args.Error}");
        }

        private void Report(string operation, Exception error)
        {
            Console.Error.WriteLine($"CameraPreview {operation} failed: {error}");
            if (_unmounted || Reconciler.GetElementTag(_control) is not CameraPreviewElement element)
            {
                Cleanup.RecordLate($"{operation}: {error.Message}");
                return;
            }
            try
            {
                element.OnPreviewFailed?.Invoke($"{operation}: {error.Message}");
            }
            catch (Exception callbackError) when (callbackError is not OutOfMemoryException and not StackOverflowException)
            {
                Console.Error.WriteLine($"CameraPreview error callback failed: {callbackError}");
            }
        }
    }
}

// A full color picker. Color ↔ ColorChanged follows the {Prop}Changed convention,
// so Color surfaces as a two-way controlled prop automatically.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.ColorPicker))]
public partial record ColorPickerElement;

// Crops/zooms/rotates an image. Mostly one-way display props (AspectRatio, CropShape, …).
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.ImageCropper))]
public partial record ImageCropperElement;

// A draggable splitter for resizing adjacent grid columns/rows (from the Sizers package).
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.GridSplitter))]
public partial record GridSplitterElement;

// A text box that turns input into removable "tokens" (chips). Its Items collection
// is managed internally (direct Items.Clear()/Add() throws), so exclude the auto
// ItemsHost. TokenItemAdding is a typed event — its whole args (TokenItemAddingEventArgs,
// with TokenText to read and Item to set) are surfaced AUTOMATICALLY as
// Action<TokenItemAddingEventArgs>, no [WrapEvent] needed, letting the sample convert
// typed text into a matched suggestion or a new token (the WCT gallery pattern).
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.TokenizingTextBox),
    Exclude = new[] { "Items" })]
public partial record TokenizingTextBoxElement;

// ═════════════════════════════════════════════════════════════════════════
//  Round 2 — the rest of the WCT (non-Labs) Controls + Layouts gallery.
// ═════════════════════════════════════════════════════════════════════════

// ── Input ────────────────────────────────────────────────────────────────

// Both endpoints use the same native ValueChanged event. The generated bindings
// notify only the changed endpoint and reassert supplied parent values on update.
// One-way props would leave native drift untouched when parent values stay equal.
// The pinned Toolkit does not raise ValueChanged for keyboard edits, so this
// sample's change notifications cover pointer interaction, not keyboard control.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.RangeSelector))]
[WrapControlled("RangeStart", ChangedEvent = "ValueChanged")]
[WrapControlled("RangeEnd", ChangedEvent = "ValueChanged")]
public partial record RangeSelectorElement;

// ── Layout (controls) ──────────────────────────────────────────────────────

// A ContentControl with a Header (and HeaderTemplate). Content is the single
// child slot; Header surfaces as an object? value prop.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.HeaderedContentControl))]
public partial record HeaderedContentControlElement;

// An ItemsControl with a Header. Items populate through the generated items slot.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.HeaderedItemsControl))]
public partial record HeaderedItemsControlElement;

// Applies a Transform to its single Child and includes it in layout measurement.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.LayoutTransformControl))]
public partial record LayoutTransformControlElement;

// ── Sizers ──────────────────────────────────────────────────────────────────

// A draggable sizer that resizes a target control's content (vs GridSplitter
// which resizes grid definitions). Orientation + drag-increment value props.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.ContentSizer))]
public partial record ContentSizerElement;

// A sizer that drives a bound double dependency property (e.g. a column width)
// directly through Binding. Minimum/Maximum clamp the dragged value.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.PropertySizer))]
public partial record PropertySizerElement;

// ── Status & info ────────────────────────────────────────────────────────────

// A compact "metadata" line (author · date · tags) separated by a glyph. Its
// Items are MetadataItem records (not UIElements), so it surfaces as plain props.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.MetadataControl))]
public partial record MetadataControlElement;

// ── Layouts (panels) ──────────────────────────────────────────────────────────

// A ContentPresenter that enforces width/height/aspect-ratio constraints on its
// single child (Content). The Scale* / *AspectRatio props express the constraint.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.ConstrainedBox))]
public partial record ConstrainedBoxElement;

// A Grid-derived panel that lays its children out in equal-sized cells.
// Rows/Columns control the grid shape; children fill cells in order.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.UniformGrid))]
public partial record UniformGridElement;

// A Panel that docks children to its edges. The sample uses the Toolkit's default
// Dock.Left; non-default sides need the Toolkit's per-child Dock attached property.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.DockPanel))]
public partial record DockPanelElement;

// A Pinterest-style column panel — children flow top-to-bottom into the shortest
// column. DesiredColumnWidth drives the column count.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.StaggeredPanel))]
public partial record StaggeredPanelElement;

// A panel that wraps children onto new lines/columns. Orientation + spacing props.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.WrapPanel))]
public partial record WrapPanelElement;

// ── Risky shapes (TreeView / NavigationView / RichEditBox / case-presenter) ──

// HeaderedTreeView derives from WinUI TreeView (not ItemsControl): its hierarchy
// comes from ItemsSource/RootNodes, not a flat Items/Children collection, so it
// surfaces as plain value props (Header + ItemsSource) — bind data declaratively.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.HeaderedTreeView))]
public partial record HeaderedTreeViewElement;

// SwitchCases is a settable CaseCollection, generated as a one-way value prop.
// Toolkit selects CurrentCase and owns Content. Leave the generated Content slot
// unset so Reactor does not replace Toolkit's selected content.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SwitchPresenter))]
public partial record SwitchPresenterElement;

// TabbedCommandBar derives from NavigationView: its tabs are TabbedCommandBarItem
// objects in MenuItems (a NavigationView collection, not a flat Items), so the
// sample builds them once through a guarded native setter, not an Element slot.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.TabbedCommandBar))]
public partial record TabbedCommandBarElement;

// RichSuggestBox is an ItemsControl wrapping a RichEditBox. Its Items are the
// active suggestion tokens (managed internally), so exclude the auto items slot;
// RichText/PlainText + SuggestionRequested drive it declaratively.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.RichSuggestBox),
    Exclude = new[] { "Items" })]
public partial record RichSuggestBoxElement;
