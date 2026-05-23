using System.Text.Json.Serialization;

namespace Microsoft.UI.Reactor.Docking.Persistence;

// ════════════════════════════════════════════════════════════════════════
//  Docking Layout JSON — schema v2
//
//  Spec 045 §5.4 (Phase 2). The v2 schema is Reactor-native: structure +
//  identity only, no code paths, no reflection-discovered types. The
//  wire format is small, AOT-clean (System.Text.Json source-gen via
//  DockLayoutJsonContext), and forward-tolerant — unknown fields are
//  silently dropped on read; missing required fields reject the whole
//  layout (loader falls back to default per §8.9 / §8.10).
//
//  Schema conventions:
//    • Root carries "$schema": <integer>. v1 (P1's vendored shape) had no
//      $schema marker; loader infers v1 from absence and runs the
//      migration ladder.
//    • Sizes for splits are stored as ratios (0..1 of parent). Absolute
//      px is reserved for floating x/y/w/h and per-pane width/height
//      overrides — DPI-robust per §5.4 note 1.
//    • Numerics use the invariant culture (System.Text.Json default) —
//      verified by a save-de-DE / load-en-US selftest (§2.23).
//    • Per-pane <typeparamref name="TState"/> is stored as a serialized
//      JSON envelope; the loader hands it to the app's adapter on
//      content recreation (§5.3.2).
//  ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Root of a persisted docking layout JSON document. Schema v2 (spec 045 §5.4).
/// </summary>
internal sealed class DockLayoutDoc
{
    /// <summary>Schema version. v2 emits 2; loader rejects unknown majors. Default 0 so deserialization of a "{}" input falls back as missing-schema (rather than silently inferring the current emitter version).</summary>
    [JsonPropertyName("$schema")]
    public int Schema { get; set; }

    /// <summary>The root of the docked layout tree.</summary>
    [JsonPropertyName("root")]
    public DockLayoutNode? Root { get; set; }

    /// <summary>Tool windows pinned to the left side strip.</summary>
    [JsonPropertyName("leftSide")]
    public List<DockLayoutPane>? LeftSide { get; set; }

    /// <summary>Tool windows pinned to the top side strip.</summary>
    [JsonPropertyName("topSide")]
    public List<DockLayoutPane>? TopSide { get; set; }

    /// <summary>Tool windows pinned to the right side strip.</summary>
    [JsonPropertyName("rightSide")]
    public List<DockLayoutPane>? RightSide { get; set; }

    /// <summary>Tool windows pinned to the bottom side strip.</summary>
    [JsonPropertyName("bottomSide")]
    public List<DockLayoutPane>? BottomSide { get; set; }

    /// <summary>Floating-window state.</summary>
    [JsonPropertyName("floating")]
    public List<DockLayoutFloatingWindow>? Floating { get; set; }

    /// <summary>The serialized key (stringified) of the currently-active content, or null.</summary>
    [JsonPropertyName("activeKey")]
    public string? ActiveKey { get; set; }
}

/// <summary>
/// Tagged-union node in the persisted layout tree. Discriminated by the
/// <see cref="Kind"/> field — split / tabGroup / pane.
/// </summary>
internal sealed class DockLayoutNode
{
    /// <summary>One of: "split", "tabGroup", "pane".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "pane";

    // split:
    /// <summary>Split orientation ("horizontal" | "vertical"). Required for split nodes.</summary>
    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; }

    /// <summary>Children for split nodes.</summary>
    [JsonPropertyName("children")]
    public List<DockLayoutNode>? Children { get; set; }

    // split + tabGroup ratios:
    /// <summary>Fractional weight of this child within its split parent (0..1). Optional.</summary>
    [JsonPropertyName("ratio")]
    public double? Ratio { get; set; }

    // tabGroup:
    /// <summary>Documents for tabGroup nodes.</summary>
    [JsonPropertyName("documents")]
    public List<DockLayoutPane>? Documents { get; set; }

    /// <summary>Tab position ("top" | "bottom") for tabGroup nodes.</summary>
    [JsonPropertyName("tabPosition")]
    public string? TabPosition { get; set; }

    /// <summary>Whether tabs render in compact mode (tabGroup nodes).</summary>
    [JsonPropertyName("compactTabs")]
    public bool? CompactTabs { get; set; }

    /// <summary>
    /// Visual chrome preset for tabGroup nodes ("win11" | "flat" | "titleBar").
    /// Missing/null = "win11" (back-compat — legacy layout files predate the
    /// field). Spec 045 §4.6.
    /// </summary>
    [JsonPropertyName("tabChrome")]
    public string? TabChrome { get; set; }

    /// <summary>Whether the group renders when empty (tabGroup nodes).</summary>
    [JsonPropertyName("showWhenEmpty")]
    public bool? ShowWhenEmpty { get; set; }

    /// <summary>Selected tab index (tabGroup nodes). -1 = none.</summary>
    [JsonPropertyName("selectedIndex")]
    public int? SelectedIndex { get; set; }

    // pane (leaf):
    /// <summary>The pane payload for kind=pane nodes.</summary>
    [JsonPropertyName("pane")]
    public DockLayoutPane? Pane { get; set; }

    // dimensions (any node):
    /// <summary>Optional absolute width in DIPs (rarely used; ratios preferred for splits).</summary>
    [JsonPropertyName("width")]
    public double? Width { get; set; }

    /// <summary>Optional absolute height in DIPs.</summary>
    [JsonPropertyName("height")]
    public double? Height { get; set; }

    /// <summary>Optional minimum width in DIPs.</summary>
    [JsonPropertyName("minWidth")]
    public double? MinWidth { get; set; }

    /// <summary>Optional minimum height in DIPs.</summary>
    [JsonPropertyName("minHeight")]
    public double? MinHeight { get; set; }

    /// <summary>Optional maximum width in DIPs.</summary>
    [JsonPropertyName("maxWidth")]
    public double? MaxWidth { get; set; }

    /// <summary>Optional maximum height in DIPs.</summary>
    [JsonPropertyName("maxHeight")]
    public double? MaxHeight { get; set; }
}

/// <summary>
/// Serialized pane (leaf) — title + key + state envelope + role discriminator
/// + permission overrides. Adapter rehydrates content via OnContentCreated.
/// </summary>
internal sealed class DockLayoutPane
{
    /// <summary>Pane title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Stringified key. Reactor reconciles by this on next render.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Pane role: "document" | "toolWindow" | "dockableContent" (P1 default).</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "dockableContent";

    /// <summary>The opaque JSON state envelope (serialized TState for Document&lt;TState&gt;, else null).</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>The previous-container key (for the show-where-left mechanic, spec 045 §5.3.9).</summary>
    [JsonPropertyName("previousContainer")]
    public string? PreviousContainer { get; set; }

    /// <summary>Per-pane permission overrides — written only when they differ from role default.</summary>
    [JsonPropertyName("canClose")]    public bool? CanClose { get; set; }
    /// <summary>Per-pane permission override.</summary>
    [JsonPropertyName("canPin")]      public bool? CanPin { get; set; }
    /// <summary>Per-pane permission override.</summary>
    [JsonPropertyName("canFloat")]    public bool? CanFloat { get; set; }
    /// <summary>Per-pane permission override.</summary>
    [JsonPropertyName("canMove")]     public bool? CanMove { get; set; }
    /// <summary>Per-pane permission override for ToolWindow.</summary>
    [JsonPropertyName("canHide")]     public bool? CanHide { get; set; }
    /// <summary>Per-pane permission override for ToolWindow.</summary>
    [JsonPropertyName("canAutoHide")] public bool? CanAutoHide { get; set; }
    /// <summary>Per-pane permission override for ToolWindow.</summary>
    [JsonPropertyName("canDockAsDocument")] public bool? CanDockAsDocument { get; set; }
    /// <summary>Per-pane permission override for Document.</summary>
    [JsonPropertyName("canDockAsToolWindow")] public bool? CanDockAsToolWindow { get; set; }

    /// <summary>Optional width hint in DIPs.</summary>
    [JsonPropertyName("width")]
    public double? Width { get; set; }

    /// <summary>Optional height hint in DIPs.</summary>
    [JsonPropertyName("height")]
    public double? Height { get; set; }
}

/// <summary>Floating-window persisted state.</summary>
internal sealed class DockLayoutFloatingWindow
{
    /// <summary>Stable id (stringified DockHostModel-side identifier).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>X position in screen coordinates (DIPs).</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>Y position in screen coordinates (DIPs).</summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>Width in DIPs.</summary>
    [JsonPropertyName("width")]
    public double Width { get; set; }

    /// <summary>Height in DIPs.</summary>
    [JsonPropertyName("height")]
    public double Height { get; set; }

    /// <summary>Panes inside the floating window.</summary>
    [JsonPropertyName("contents")]
    public List<DockLayoutPane> Contents { get; set; } = new();
}

/// <summary>
/// Source-generated JSON context for the docking layout schema. Required
/// for AOT-clean parsing (spec 045 §8.9): no reflection paths from JSON.
/// </summary>
[JsonSerializable(typeof(DockLayoutDoc))]
[JsonSerializable(typeof(DockLayoutNode))]
[JsonSerializable(typeof(DockLayoutPane))]
[JsonSerializable(typeof(DockLayoutFloatingWindow))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DockLayoutJsonContext : JsonSerializerContext { }
