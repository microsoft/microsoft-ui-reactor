using Microsoft.UI.Reactor.Wrappers;

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
//  No hand-written wrapper/handler code.
// ─────────────────────────────────────────────────────────────────────────

// A settings "row": Header + Description + an optional HeaderIcon, with a
// content child (a control on the right) and an optional whole-card Click.
// CommandParameter is an inherited ButtonBase prop with no declarative meaning.
//
// HeaderIcon is typed IconElement (UIElement-derived), so the generator treats
// it as a content slot — and a control has only ONE content slot, already taken
// by Content. [WrapElementSlot("HeaderIcon")] promotes it to a SECONDARY element
// slot: the generator surfaces the Element? HeaderIcon init prop + factory param
// and emits the state-preserving mount/reconcile wiring (ctx.MountChild on mount,
// the public ctx.ReconcileChild on update). Authors pass any Reactor icon element
// through the generated factory param, e.g. `SettingsCard(header: …, headerIcon: Icon(FontIcon("\uE701")))`.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsCard),
    Exclude = new[] { "CommandParameter" })]
[WrapElementSlot("HeaderIcon")]
public partial record SettingsCardElement;

// A settings group that expands to reveal child SettingsCards (its Items).
// Items is the primary slot, so HeaderIcon, the object-typed Content (shown on the
// header row) and the ItemsHeader / ItemsFooter UIElements become secondary element
// slots, as SettingsCard.HeaderIcon does.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.SettingsExpander))]
[WrapElementSlot("HeaderIcon")]
[WrapElementSlot("Content")]
[WrapElementSlot("ItemsHeader")]
[WrapElementSlot("ItemsFooter")]
public partial record SettingsExpanderElement;

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

// A dual-thumb range slider. RangeStart/RangeEnd both change through the SINGLE
// ValueChanged event (carrying which thumb moved via RangeChangedEventArgs), not
// two {Prop}Changed events — and a control has only one controlled-event slot, so
// they CANNOT both be [WrapControlled]. The idiomatic shape: one-way RangeStart/
// RangeEnd (force-asserted from state) + the auto-surfaced OnValueChanged typed
// event, which the sample uses to update the right end. (The generator warns,
// REACTORGEN012, if two [WrapControlled] props share a control.)
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.RangeSelector))]
public partial record RangeSelectorElement;

// ── Layout (controls) ──────────────────────────────────────────────────────

// A ContentControl with a Header (and HeaderTemplate). Content is the single
// child slot; Header surfaces as an object? value prop.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.HeaderedContentControl))]
public partial record HeaderedContentControlElement;

// An ItemsControl with a Header. Items populate through the generated items slot.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.HeaderedItemsControl))]
public partial record HeaderedItemsControlElement;

// Applies an arbitrary render Transform to its single Child without affecting
// layout. Child is the content slot; Transform is a value prop.
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

// A Panel that docks children to its edges (the per-child Dock is an attached
// property set via the layout modifier, not generated). LastChildFill fills the rest.
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
// generator surfaces only props/content here. Tabs are built declaratively via
// the MenuItems value prop in the sample.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.TabbedCommandBar))]
public partial record TabbedCommandBarElement;

// RichSuggestBox is an ItemsControl wrapping a RichEditBox. Its Items are the
// active suggestion tokens (managed internally), so exclude the auto items slot;
// RichText/PlainText + SuggestionRequested drive it declaratively.
[GenerateReactorWrapper(typeof(CommunityToolkit.WinUI.Controls.RichSuggestBox),
    Exclude = new[] { "Items" })]
public partial record RichSuggestBoxElement;
