using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using WctControls;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;   // IBuffer.AsStream() for the ImageCropper sample bitmap
using static Microsoft.UI.Reactor.Factories;
using static WctControls.SettingsCardElement;      // generated SettingsCard(...) factory
using static WctControls.SettingsExpanderElement;  // generated SettingsExpander(...) factory
using static WctControls.RadialGaugeElement;       // generated RadialGauge(...) factory
using static WctControls.SegmentedElement;         // generated Segmented(...) factory
using static WctControls.CameraPreviewElement;     // generated CameraPreview(...) factory
using static WctControls.ColorPickerElement;        // generated ColorPicker(...) factory
using static WctControls.ImageCropperElement;       // generated ImageCropper(...) factory
using static WctControls.GridSplitterElement;       // generated GridSplitter(...) factory
using static WctControls.TokenizingTextBoxElement;  // generated TokenizingTextBox(...) factory
using static WctControls.RangeSelectorElement;
using static WctControls.HeaderedContentControlElement;
using static WctControls.HeaderedItemsControlElement;
using static WctControls.HeaderedTreeViewElement;
using static WctControls.LayoutTransformControlElement;
using static WctControls.TabbedCommandBarElement;
using static WctControls.ContentSizerElement;
using static WctControls.PropertySizerElement;
using static WctControls.MetadataControlElement;
using static WctControls.ConstrainedBoxElement;
using static WctControls.SwitchPresenterElement;
using static WctControls.DockPanelElement;
using static WctControls.StaggeredPanelElement;
using static WctControls.WrapPanelElement;
using static WctControls.RichSuggestBoxElement;

ReactorApp.Run<App>("WCT × Reactor — control gallery", width: 980, height: 720);

// A small "gallery" app modelled on the Windows Community Toolkit sample app: a
// NavigationView pane on the left lists the wrapped WCT controls, and each
// control gets its OWN page in the content area (no cramming everything onto one
// page). Every control is a real WCT control turned into a first-class Reactor
// element by Reactor.Wrappers.Generator (see WctControls.cs) — no hand-written
// wrapper code. Each page is its own Reactor Component, so its demo state is
// self-contained and the page mounts/unmounts as you navigate.
internal sealed class App : Component
{
    public override Element Render()
    {
        var (tag, setTag) = UseState("cards");

        var menu = new[]
        {
            NavItemHeader("Input"),
            NavItem("Segmented",            "Bullets",   "segmented"),
            NavItem("Range selector",       "Volume",    "range"),
            NavItem("Radial gauge",         "Volume",    "gauge"),
            NavItem("Color picker",         "Highlight", "color"),

            NavItemHeader("Layout"),
            NavItem("Settings card",        "Setting",   "cards"),
            NavItem("Settings expander",    "AllApps",   "expander"),
            NavItem("Headered content",     "GroupList", "hcc"),
            NavItem("Headered items",       "List",      "hic"),
            NavItem("Headered tree",        "Library",   "htv"),
            NavItem("Layout transform",     "Rotate",    "ltc"),
            NavItem("Tabbed command bar",   "Tab",       "tcb"),

            NavItemHeader("Media"),
            NavItem("Camera preview",       "Camera",    "camera"),
            NavItem("Image cropper",        "Crop",      "cropper"),

            NavItemHeader("Sizers"),
            NavItem("Grid splitter",        "Page2",     "splitter"),
            NavItem("Content sizer",        "Page2",     "csz"),
            NavItem("Property sizer",       "Page2",     "psz"),

            NavItemHeader("Status & info"),
            NavItem("Metadata",             "Tag",       "meta"),

            NavItemHeader("Text"),
            NavItem("Tokenizing box",       "Tag",       "tokens"),
            NavItem("Rich suggest box",     "Edit",      "rsb"),

            NavItemHeader("Layouts (panels)"),
            NavItem("Dock panel",           "ViewAll",   "dock"),
            NavItem("Uniform grid",         "ViewAll",   "ug"),
            NavItem("Wrap panel",           "ViewAll",   "wrap"),
            NavItem("Staggered panel",      "ViewAll",   "stag"),
            NavItem("Constrained box",      "FitPage",   "cbox"),
            NavItem("Switch presenter",     "Switch",    "swp"),
        };

        // Each route maps to its own page Component — switching the tag swaps the
        // NavigationView content, mounting a fresh page (and unmounting the old).
        Element page = tag switch
        {
            "expander"  => Component<ExpanderPage>(),
            "segmented" => Component<SegmentedPage>(),
            "range"     => Component<RangeSelectorPage>(),
            "gauge"     => Component<GaugePage>(),
            "camera"    => Component<CameraPage>(),
            "color"     => Component<ColorPickerPage>(),
            "cropper"   => Component<ImageCropperPage>(),
            "splitter"  => Component<GridSplitterPage>(),
            "csz"       => Component<ContentSizerPage>(),
            "psz"       => Component<PropertySizerPage>(),
            "tokens"    => Component<TokenizingPage>(),
            "rsb"       => Component<RichSuggestBoxPage>(),
            "hcc"       => Component<HeaderedContentPage>(),
            "hic"       => Component<HeaderedItemsPage>(),
            "htv"       => Component<HeaderedTreePage>(),
            "ltc"       => Component<LayoutTransformPage>(),
            "tcb"       => Component<TabbedCommandBarPage>(),
            "meta"      => Component<MetadataPage>(),
            "dock"      => Component<DockPanelPage>(),
            "ug"        => Component<UniformGridPage>(),
            "wrap"      => Component<WrapPanelPage>(),
            "stag"      => Component<StaggeredPanelPage>(),
            "cbox"      => Component<ConstrainedBoxPage>(),
            "swp"       => Component<SwitchPresenterPage>(),
            _           => Component<CardsPage>(),
        };

        return NavigationView(menu, content: page) with
        {
            SelectedTag = tag,
            OnSelectedTagChanged = t => { if (t is not null) setTag(t); },
            PaneTitle = "Controls",
            IsSettingsVisible = false,
        };
    }
}

// Shared page chrome: a scrollable column with a title, a one-line description,
// and the page body.
internal static class Gallery
{
    public static Element Page(string title, string subtitle, Element body) =>
        ScrollView(VStack(16, Heading(title), Caption(subtitle), body)).Margin(28);

    // A small colored tile used by the panel demos.
    public static Element Box(string color, string label, double height = 64) =>
        Border(TextBlock(label).Center())
            .Background(color)
            .CornerRadius(6)
            .Height(height);
}

// ── SettingsCard ────────────────────────────────────────────────────────
internal sealed class CardsPage : Component
{
    public override Element Render()
    {
        var (wifiOn, setWifiOn) = UseState(true);
        var (clicks, setClicks) = UseState(0);

        return Gallery.Page(
            "SettingsCard",
            "A settings row: header + description, content on the right, and an optional whole-card click.",
            VStack(12,
                SettingsCard(
                    header: "Wi-Fi",
                    description: wifiOn ? "Connected to CONTOSO-5G" : "Disconnected",
                    content: ToggleSwitch(isOn: wifiOn, onIsOnChanged: setWifiOn),
                    headerIcon: Icon(FontIcon("\uE701"))),   // secondary element slot, a generated factory param via [WrapElementSlot]
                SettingsCard(
                    header: "About",
                    description: $"Tapped {clicks} time(s) — click anywhere on this card",
                    isClickEnabled: true,
                    onClick: () => setClicks(clicks + 1))));
    }
}

// ── SettingsExpander ────────────────────────────────────────────────────
internal sealed class ExpanderPage : Component
{
    public override Element Render()
    {
        var (notifications, setNotifications) = UseState(true);
        var (sounds, setSounds) = UseState(false);

        return Gallery.Page(
            "SettingsExpander",
            "A settings group that expands to reveal child cards (populated through the generated items slot).",
            SettingsExpander(
                header: "Notifications",
                description: notifications ? "On" : "Off",
                isExpanded: true,
                items: new object[]
                {
                    SettingsCard(
                        header: "Show notifications",
                        content: ToggleSwitch(isOn: notifications, onIsOnChanged: setNotifications)),
                    SettingsCard(
                        header: "Play sounds",
                        content: ToggleSwitch(isOn: sounds, onIsOnChanged: setSounds)),
                }));
    }
}

// ── Segmented ───────────────────────────────────────────────────────────
internal sealed class SegmentedPage : Component
{
    public override Element Render()
    {
        var (choice, setChoice) = UseState(0);
        string[] views = { "List", "Grid", "Details" };

        return Gallery.Page(
            "Segmented",
            "A single-choice selector. SelectionChanged is bound two-way to SelectedIndex via [WrapControlled].",
            VStack(12,
                Segmented(
                    selectedIndex: choice,
                    onSelectedIndexChanged: setChoice,
                    items: new object[] { "List", "Grid", "Details" }),
                Caption($"Selected view: {views[choice]}")));
    }
}

// ── RadialGauge ─────────────────────────────────────────────────────────
internal sealed class GaugePage : Component
{
    public override Element Render()
    {
        var (volume, setVolume) = UseState(35.0);

        return Gallery.Page(
            "RadialGauge",
            "A circular gauge whose Value is a two-way controlled prop, driven live by a Reactor Slider.",
            SettingsCard(
                header: "Volume",
                description: $"{volume:0} %",
                content: HStack(16,
                    Slider(value: volume, min: 0, max: 100, onValueChanged: setVolume).Width(240),
                    RadialGauge(
                        value: volume, minimum: 0, maximum: 100, unit: "%",
                        onValueChanged: setVolume).Size(132, 132))));
    }
}

// ── CameraPreview (imperative — lifecycle handled by [WrapLifecycle]) ────
internal sealed class CameraPage : Component
{
    public override Element Render()
    {
        var (camError, setCamError) = UseState<string?>(null);

        return Gallery.Page(
            "CameraPreview",
            "An imperative WCT control — its StartAsync-on-mount / Stop-on-unmount lifecycle is declared once via [WrapLifecycle], so the call site is a plain declarative element. PreviewFailed surfaces via OnPreviewFailed.",
            SettingsCard(
                header: "Live camera",
                description: camError is null ? "Streaming (or starting…)" : $"Unavailable — {camError}",
                content: CameraPreview(
                    isFrameSourceGroupButtonVisible: true,
                    onPreviewFailed: setCamError).Size(440, 280)));
    }
}

// ── ColorPicker ─────────────────────────────────────────────────────────
internal sealed class ColorPickerPage : Component
{
    public override Element Render()
    {
        var (color, setColor) = UseState(Microsoft.UI.Colors.DodgerBlue);
        string hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        return Gallery.Page(
            "ColorPicker",
            "A full color picker. Color ↔ ColorChanged follows the {Prop}Changed convention, so Color is a two-way controlled prop.",
            VStack(12,
                SettingsCard(header: "Selected", description: hex),
                ColorPicker(
                    color: color,
                    onColorChanged: setColor,
                    isAlphaEnabled: true)));
    }
}

// ── ImageCropper ────────────────────────────────────────────────────────
internal sealed class ImageCropperPage : Component
{
    public override Element Render()
    {
        var (shape, setShape) = UseState(0);   // 0 Rectangular · 1 Circular
        // Build the sample bitmap once (UseMemo), then pass it DECLARATIVELY as the
        // control's Source prop — no imperative reach-into-the-control needed.
        var image = UseMemo(SampleBitmap, System.Array.Empty<object>());

        return Gallery.Page(
            "ImageCropper",
            "Crops/zooms/rotates an image. Both Source (a sample bitmap) and CropShape are bound declaratively as element props.",
            VStack(12,
                Segmented(
                    selectedIndex: shape,
                    onSelectedIndexChanged: setShape,
                    items: new object[] { "Rectangular", "Circular" }),
                ImageCropper(
                    source: image,
                    cropShape: shape == 0
                        ? CommunityToolkit.WinUI.Controls.CropShape.Rectangular
                        : CommunityToolkit.WinUI.Controls.CropShape.Circular)
                    .Size(460, 320)));
    }

    // A small gradient WriteableBitmap so the cropper has something to crop (a real
    // app would load a user image).
    private static Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap SampleBitmap()
    {
        const int w = 480, h = 360;
        var wb = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(w, h);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i + 0] = (byte)(220 - y * 180 / h);   // B
                px[i + 1] = (byte)(x * 200 / w);         // G
                px[i + 2] = (byte)(60 + y * 160 / h);    // R
                px[i + 3] = 255;                          // A
            }
        using (var s = wb.PixelBuffer.AsStream())
            s.Write(px, 0, px.Length);
        return wb;
    }
}

// ── GridSplitter ────────────────────────────────────────────────────────
internal sealed class GridSplitterPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "GridSplitter",
            "A draggable splitter (from the Sizers package). Drag the bar to resize the two panes.",
            Grid(
                columns: new[] { GridSize.Star(), GridSize.Px(11), GridSize.Star() },
                rows: new[] { GridSize.Star() },
                Pane("Left").Grid(row: 0, column: 0),
                GridSplitter().Grid(row: 0, column: 1),
                Pane("Right").Grid(row: 0, column: 2))
            .Height(260));

    private static Element Pane(string label) =>
        Border(TextBlock(label).Center())
            .Background("#F0F8FF")
            .CornerRadius(8);
}

// ── TokenizingTextBox ───────────────────────────────────────────────────
internal sealed class TokenizingPage : Component
{
    // The suggestion list the AutoSuggest dropdown filters as you type — mirrors
    // the WCT gallery sample's recognizable items (it uses an icon+text data type
    // via a DataTemplate; we use plain strings since the generator wraps props/
    // events, not DataTemplates).
    private static readonly string[] Suggestions =
    {
        "Account", "Add friend", "Attach", "Audio", "Calendar", "Camera",
        "Contact", "Favorite", "Link", "Mail", "Map", "Phone", "Pin",
        "Send", "Tags", "Zoom",
    };

    public override Element Render()
    {
        var (text, setText) = UseState("");

        return Gallery.Page(
            "TokenizingTextBox",
            "Type to filter the suggestions and pick one, or type free text + the delimiter (comma) to create a token. TokenItemAdding converts the typed text into a matching suggestion (or keeps it as a new token), and the box is capped at 5 — the WCT gallery pattern.",
            VStack(12,
                TokenizingTextBox(
                    text: text,
                    onTextChanged: setText,
                    onTokenItemAdding: ConvertToken,
                    suggestedItemsSource: Suggestions,
                    header: "Add up to 5 actions",
                    placeholderText: "Add actions",
                    tokenDelimiter: ",",
                    maximumTokens: 5),
                Caption(string.IsNullOrEmpty(text) ? "Pick from suggestions or type a new tag." : $"Editing: {text}")));
    }

    // TokenItemAdding (the WCT sample's TokenItemCreating): convert the typed text
    // into a matching suggestion (case-insensitive substring), else keep it as a
    // new token. Setting e.Item is what makes the token.
    private static void ConvertToken(CommunityToolkit.WinUI.Controls.TokenItemAddingEventArgs e)
        => e.Item = System.Array.Find(
               Suggestions,
               s => s.Contains(e.TokenText, System.StringComparison.CurrentCultureIgnoreCase))
           ?? e.TokenText;
}

// ── RangeSelector ───────────────────────────────────────────────────────
internal sealed class RangeSelectorPage : Component
{
    public override Element Render()
    {
        var (lo, setLo) = UseState(20.0);
        var (hi, setHi) = UseState(80.0);

        return Gallery.Page(
            "RangeSelector",
            "A dual-thumb range slider. RangeStart/RangeEnd are one-way props (force-asserted from state) and the single ValueChanged event — which carries WHICH thumb moved — drives the matching state setter. (Two thumbs share one event, so they can't both be [WrapControlled]; this one-way + typed-event shape is the idiomatic fit.)",
            VStack(12,
                RangeSelector(
                    minimum: 0, maximum: 100, stepFrequency: 1,
                    rangeStart: lo, rangeEnd: hi,
                    onValueChanged: e =>
                    {
                        if (e.ChangedRangeProperty == CommunityToolkit.WinUI.Controls.RangeSelectorProperty.MinimumValue)
                            setLo(e.NewValue);
                        else
                            setHi(e.NewValue);
                    }).Width(380),
                Caption($"Selected range: {lo:0} – {hi:0}")));
    }
}

// ── HeaderedContentControl ──────────────────────────────────────────────
internal sealed class HeaderedContentPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "HeaderedContentControl",
            "A ContentControl with a Header (an object? value prop) above its single content child.",
            HeaderedContentControl(
                header: "Shipping address",
                content: Border(
                    VStack(6,
                        TextBlock("1 Microsoft Way"),
                        TextBlock("Redmond, WA 98052")))
                    .Background("#F0F8FF").CornerRadius(8).Margin(0))
                .Width(360));
}

// ── HeaderedItemsControl ────────────────────────────────────────────────
internal sealed class HeaderedItemsPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "HeaderedItemsControl",
            "An ItemsControl with a Header. Items flow through the generated items slot — strings pass straight through, Element items mount via the reconciler.",
            HeaderedItemsControl(
                header: "Fruits",
                items: new object[] { "Apple", "Banana", "Cherry", "Date" })
                .Width(320));
}

// ── HeaderedTreeView ────────────────────────────────────────────────────
internal sealed class HeaderedTreePage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "HeaderedTreeView",
            "Derives from WinUI TreeView, so its hierarchy is data-bound through ItemsSource (not a flat Children/Items slot). Header + ItemsSource are surfaced as declarative props.",
            HeaderedTreeView(
                header: "Library",
                itemsSource: new[] { "Documents", "Pictures", "Music", "Videos" })
                .Width(320).Height(220));
}

// ── LayoutTransformControl ──────────────────────────────────────────────
internal sealed class LayoutTransformPage : Component
{
    public override Element Render()
    {
        var (angle, setAngle) = UseState(20.0);

        return Gallery.Page(
            "LayoutTransformControl",
            "Applies a render Transform to its single Child while still affecting layout. Transform is a one-way prop, driven live here by a Reactor Slider.",
            VStack(16,
                LayoutTransformControl(
                    transform: new Microsoft.UI.Xaml.Media.RotateTransform { Angle = angle },
                    content: Gallery.Box("#FFB74D", "Rotated", 90).Width(180)),
                Slider(value: angle, min: 0, max: 360, onValueChanged: setAngle).Width(300),
                Caption($"Angle: {angle:0}°")));
    }
}

// ── TabbedCommandBar ────────────────────────────────────────────────────
internal sealed class TabbedCommandBarPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "TabbedCommandBar",
            "A ribbon-style NavigationView whose tabs are TabbedCommandBarItem (a CommandBar) objects in MenuItems — a WinUI control subtree the prop/event wrapper can't express declaratively, so the tabs are built once through the imperative Setters escape hatch (legitimate here: it's controls, not data). The document body is the declarative content slot.",
            (TabbedCommandBar(
                content: Border(TextBlock("Document body — pick a ribbon tab above.").Center())
                    .Background("#F0F8FF").CornerRadius(8).Height(150))
                .Set(BuildTabs))
                .Height(260));

    // Build the ribbon tabs once (Setters run on every Mount/Update → guard on Count).
    private static void BuildTabs(CommunityToolkit.WinUI.Controls.TabbedCommandBar tcb)
    {
        if (tcb.MenuItems.Count > 0) return;

        var home = new CommunityToolkit.WinUI.Controls.TabbedCommandBarItem { Header = "Home" };
        home.PrimaryCommands.Add(Cmd("Add", Microsoft.UI.Xaml.Controls.Symbol.Add));
        home.PrimaryCommands.Add(Cmd("Edit", Microsoft.UI.Xaml.Controls.Symbol.Edit));
        home.PrimaryCommands.Add(new Microsoft.UI.Xaml.Controls.AppBarSeparator());
        home.PrimaryCommands.Add(Cmd("Share", Microsoft.UI.Xaml.Controls.Symbol.Share));

        var view = new CommunityToolkit.WinUI.Controls.TabbedCommandBarItem { Header = "View" };
        view.PrimaryCommands.Add(Cmd("Zoom", Microsoft.UI.Xaml.Controls.Symbol.Zoom));
        view.PrimaryCommands.Add(Cmd("Refresh", Microsoft.UI.Xaml.Controls.Symbol.Refresh));

        tcb.MenuItems.Add(home);
        tcb.MenuItems.Add(view);
    }

    private static Microsoft.UI.Xaml.Controls.AppBarButton Cmd(string label, Microsoft.UI.Xaml.Controls.Symbol symbol) =>
        new() { Label = label, Icon = new Microsoft.UI.Xaml.Controls.SymbolIcon(symbol) };
}

// ── ContentSizer ────────────────────────────────────────────────────────
internal sealed class ContentSizerPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "ContentSizer",
            "A draggable sizer (Sizers package) that resizes its target content (vs GridSplitter, which resizes grid definitions). Drag the bar to resize the panel above it.",
            VStack(0,
                Gallery.Box("#FFCC80", "Resizable content — drag the bar below", 140),
                ContentSizer(orientation: Microsoft.UI.Xaml.Controls.Orientation.Horizontal))
                .Width(360));
}

// ── PropertySizer ───────────────────────────────────────────────────────
internal sealed class PropertySizerPage : Component
{
    public override Element Render()
    {
        var (width, setWidth) = UseState(220.0);
        var token = UseRef(0L);

        // PropertySizer's whole job is to two-way drive a single bound double DP — in
        // XAML via x:Bind Mode=TwoWay. There's no change EVENT to surface, so it can't
        // be a controlled prop; instead we push state into Binding one-way and observe
        // BindingProperty changes (the drag) back into state. `.OnMount` runs once per
        // mount, registering the property-changed callback exactly once; `.OnUnmount`
        // releases the token so navigating away doesn't leak the callback.
        return Gallery.Page(
            "PropertySizer",
            "A sizer that drives a single bound double dependency property (e.g. a pane width), clamped by Minimum/Maximum. Drag the bar to resize the pane — Binding is pushed from state one-way and its changes are observed back into state.",
            HStack(0,
                Gallery.Box("#A5D6A7", $"Pane — {width:0} px", 160).Width(width),
                PropertySizer(binding: width, minimum: 120, maximum: 360)
                    .Height(160)
                    .OnMount(fe => token.Current = ((CommunityToolkit.WinUI.Controls.PropertySizer)fe).RegisterPropertyChangedCallback(
                        CommunityToolkit.WinUI.Controls.PropertySizer.BindingProperty,
                        (s, _) => setWidth(((CommunityToolkit.WinUI.Controls.PropertySizer)s).Binding)))
                    .OnUnmount(fe => ((CommunityToolkit.WinUI.Controls.PropertySizer)fe).UnregisterPropertyChangedCallback(
                        CommunityToolkit.WinUI.Controls.PropertySizer.BindingProperty, token.Current))));
    }
}

// ── MetadataControl ─────────────────────────────────────────────────────
internal sealed class MetadataPage : Component
{
    private static readonly CommunityToolkit.WinUI.Controls.MetadataItem[] Items =
    {
        new() { Label = "By Megan Bowen" },
        new() { Label = "June 11, 2026" },
        new() { Label = "5 min read" },
        new() { Label = "Reactor, WinUI" },
    };

    public override Element Render() =>
        Gallery.Page(
            "MetadataControl",
            "A compact metadata line (author • date • tags) joined by a Separator. Its Items are a typed IEnumerable<MetadataItem> — surfaced as a declarative collection prop you just pass, no escape hatch.",
            MetadataControl(separator: " • ", items: Items));
}

// ── DockPanel ───────────────────────────────────────────────────────────
internal sealed class DockPanelPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "DockPanel",
            "A Panel that docks children to its edges. Children flow through the generated Panel slot; the per-child Dock is an attached property (set via a layout modifier, not generated), so here they dock in order with LastChildFill filling the remainder.",
            DockPanel(
                lastChildFill: true,
                children: new Element[]
                {
                    Gallery.Box("#EF9A9A", "A", 80),
                    Gallery.Box("#FFE082", "B", 80),
                    Gallery.Box("#A5D6A7", "Fills", 80),
                })
                .Height(200));
}

// ── UniformGrid ─────────────────────────────────────────────────────────
internal sealed class UniformGridPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "UniformGrid",
            "A Grid-derived panel that arranges children into equal-sized cells. Columns/Rows drive the shape; children fill cells in order.",
            UniformGridElement.UniformGrid(
                columns: 3, rows: 2,
                children: new Element[]
                {
                    Gallery.Box("#90CAF9", "1"), Gallery.Box("#A5D6A7", "2"), Gallery.Box("#FFCC80", "3"),
                    Gallery.Box("#CE93D8", "4"), Gallery.Box("#80CBC4", "5"), Gallery.Box("#EF9A9A", "6"),
                })
                .Width(360));
}

// ── WrapPanel ───────────────────────────────────────────────────────────
internal sealed class WrapPanelPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "WrapPanel",
            "A panel that wraps children onto new lines once they run out of room, with Horizontal/Vertical spacing.",
            WrapPanel(
                horizontalSpacing: 8, verticalSpacing: 8,
                children: new Element[]
                {
                    Gallery.Box("#90CAF9", "One").Width(90), Gallery.Box("#A5D6A7", "Two").Width(120),
                    Gallery.Box("#FFCC80", "Three").Width(80), Gallery.Box("#CE93D8", "Four").Width(140),
                    Gallery.Box("#80CBC4", "Five").Width(100), Gallery.Box("#EF9A9A", "Six").Width(110),
                })
                .Width(360));
}

// ── StaggeredPanel ──────────────────────────────────────────────────────
internal sealed class StaggeredPanelPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "StaggeredPanel",
            "A Pinterest-style panel: children flow top-to-bottom into the shortest column. DesiredColumnWidth drives the column count.",
            StaggeredPanel(
                desiredColumnWidth: 110, columnSpacing: 8, rowSpacing: 8,
                children: new Element[]
                {
                    Gallery.Box("#90CAF9", "1", 60), Gallery.Box("#A5D6A7", "2", 120),
                    Gallery.Box("#FFCC80", "3", 90), Gallery.Box("#CE93D8", "4", 70),
                    Gallery.Box("#80CBC4", "5", 110), Gallery.Box("#EF9A9A", "6", 80),
                })
                .Width(360).Height(300));
}

// ── ConstrainedBox ──────────────────────────────────────────────────────
internal sealed class ConstrainedBoxPage : Component
{
    public override Element Render() =>
        Gallery.Page(
            "ConstrainedBox",
            "A ContentPresenter that constrains its single child to an aspect ratio (or pixel multiple). Here the child is locked to 16:9 regardless of available width.",
            ConstrainedBox(
                aspectRatio: new CommunityToolkit.WinUI.Controls.AspectRatio(16, 9),
                content: Gallery.Box("#B39DDB", "16 : 9"))
                .Width(360));
}

// ── SwitchPresenter ─────────────────────────────────────────────────────
internal sealed class SwitchPresenterPage : Component
{
    public override Element Render()
    {
        var (choice, setChoice) = UseState(0);
        string[] states = { "Loading", "Ready", "Error" };

        return Gallery.Page(
            "SwitchPresenter",
            "Shows exactly one SwitchCase based on Value (a switch over UI). SwitchCase children are typed objects, not a generated slot, so here a Reactor switch drives the displayed content while Value mirrors the selection.",
            VStack(12,
                Segmented(
                    selectedIndex: choice, onSelectedIndexChanged: setChoice,
                    items: new object[] { "Loading", "Ready", "Error" }),
                SwitchPresenter(
                    value: states[choice],
                    content: Gallery.Box(
                        choice switch { 0 => "#FFE082", 1 => "#A5D6A7", _ => "#EF9A9A" },
                        states[choice], 100))));
    }
}

// ── RichSuggestBox ──────────────────────────────────────────────────────
internal sealed class RichSuggestBoxPage : Component
{
    private static readonly string[] People =
    {
        "Adele Vance", "Alex Wilber", "Diego Siciliani", "Grady Archie",
        "Isaiah Langer", "Lee Gu", "Megan Bowen", "Patti Fernandez",
    };

    public override Element Render() =>
        Gallery.Page(
            "RichSuggestBox",
            "An ItemsControl wrapping a RichEditBox. Type the “@” prefix to mention someone — SuggestionRequested fires and the suggestion list (ItemsSource) is filtered. Tokens are managed internally, so the Items slot is excluded.",
            RichSuggestBox(
                header: "Message",
                placeholderText: "Type @ to mention someone…",
                prefixes: "@",
                itemsSource: People,
                onSuggestionRequested: OnSuggestion)
                .Width(420).Height(160));

    // SuggestionRequested fires as the user types after a prefix; in a real app you
    // filter and assign the box's ItemsSource from e.QueryText. Here the static list
    // is already bound, so this is a no-op hook demonstrating the typed event arg.
    private static void OnSuggestion(CommunityToolkit.WinUI.Controls.SuggestionRequestedEventArgs e) { }
}
