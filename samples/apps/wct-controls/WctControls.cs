using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Wrappers;
using static Microsoft.UI.Reactor.Factories;

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
        d.ImperativeBridged(
            mount: static (ctx, c, e) =>
            {
                if (e.Header is not null) c.Header = ctx.MountChild(e.Header)!;
                else if (e.HeaderText is { } text) c.Header = text;
                else c.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.HeaderProperty);
            },
            update: static (ctx, c, oldElement, newElement) =>
            {
                if (oldElement.Header is null && newElement.Header is null &&
                    oldElement.HeaderText == newElement.HeaderText) return;
                var existing = oldElement.Header is not null ? c.Header as Microsoft.UI.Xaml.UIElement : null;
                var next = ctx.ReconcileChild(oldElement.Header, newElement.Header, existing);
                if (newElement.Header is not null) c.Header = next!;
                else if (newElement.HeaderText is { } text) c.Header = text;
                else c.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.HeaderProperty);
            },
            unmount: static (ctx, c) =>
            {
                if (Reconciler.GetElementTag(c) is not SettingsCardElement element ||
                    (element.Header is null && element.HeaderText is null)) return;
                if (element.Header is not null && c.Header is Microsoft.UI.Xaml.UIElement child)
                    ctx.Reconciler.UnmountChild(child);
                c.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.HeaderProperty);
            });
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
        d.ImperativeBridged(
            mount: static (ctx, c, e) =>
            {
                if (e.Header is not null) c.Header = ctx.MountChild(e.Header)!;
                else if (e.HeaderText is { } text) c.Header = text;
                else c.ClearValue(CommunityToolkit.WinUI.Controls.SettingsExpander.HeaderProperty);
            },
            update: static (ctx, c, oldElement, newElement) =>
            {
                if (oldElement.Header is null && newElement.Header is null &&
                    oldElement.HeaderText == newElement.HeaderText) return;
                var existing = oldElement.Header is not null ? c.Header as Microsoft.UI.Xaml.UIElement : null;
                var next = ctx.ReconcileChild(oldElement.Header, newElement.Header, existing);
                if (newElement.Header is not null) c.Header = next!;
                else if (newElement.HeaderText is { } text) c.Header = text;
                else c.ClearValue(CommunityToolkit.WinUI.Controls.SettingsExpander.HeaderProperty);
            },
            unmount: static (ctx, c) =>
            {
                if (Reconciler.GetElementTag(c) is not SettingsExpanderElement element ||
                    (element.Header is null && element.HeaderText is null)) return;
                if (element.Header is not null && c.Header is Microsoft.UI.Xaml.UIElement child)
                    ctx.Reconciler.UnmountChild(child);
                c.ClearValue(CommunityToolkit.WinUI.Controls.SettingsExpander.HeaderProperty);
            });
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
// started with StartAsync(...) after mount and stopped on unmount. [WrapLifecycle]
// declares that lifecycle ONCE here, so every call site is a plain, declarative
// `CameraPreview(...)` with no Setters/UseRef/StartAsync boilerplate. PreviewFailed
// (no camera, access denied, …) projects to OnPreviewFailed via [WrapEvent].
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.CameraPreview))]
[WrapEvent("PreviewFailed", Arg = "Error")]
[WrapLifecycle(nameof(StartPreview), OnUnmounted = nameof(StopPreview))]
public partial record CameraPreviewElement
{
    // Runs once when the control mounts. The camera may be absent or access-denied
    // in a given environment — that surfaces through PreviewFailed (→ OnPreviewFailed),
    // so swallow the imperative StartAsync exception here.
    private static async void StartPreview(CommunityToolkit.WinUI.Controls.CameraPreview cp)
    {
        try { await cp.StartAsync(cp.CameraHelper); }
        catch { /* reported via PreviewFailed */ }
    }

    // Runs once when the control unmounts — release the camera.
    private static void StopPreview(CommunityToolkit.WinUI.Controls.CameraPreview cp)
    {
        try { cp.Stop(); }
        catch { /* already torn down */ }
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

// SwitchPresenter shows exactly one matching SwitchCase based on Value. Cases are
// SwitchCase objects (not UIElements), surfaced through the generated items slot;
// Value is the discriminator (a one-way prop here — no {Prop}Changed event).
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
