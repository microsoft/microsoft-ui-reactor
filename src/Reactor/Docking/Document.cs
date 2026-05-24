using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// A closable document pane — lives inside a <c>DocumentPane</c>; cannot be
/// pinned to a side. Default tab styling: top-position, full.
/// </summary>
/// <remarks>
/// Spec 045 §5.3.1 (Phase 2 addition). Documents and <see cref="ToolWindow"/>s
/// distinguish themselves only via their default permission flags and
/// default placement; both reconcile through the same pane pipeline.
///
/// <para>
/// Use object-initializer syntax (<c>new Document { Title = "X", Key = id }</c>)
/// rather than the positional <see cref="DockableContent"/> constructor —
/// this matches the rest of Reactor's record style and lets apps opt into
/// the new permission flags additively.
/// </para>
/// </remarks>
public record Document : DockableContent
{
    /// <summary>
    /// Whether this document can also be docked as a tool window (side-pinnable).
    /// Default false (spec 045 §5.3.8).
    /// </summary>
    public bool CanDockAsToolWindow { get; init; } = false;

    /// <summary>Parameterless ctor — overrides the base permission defaults to Document semantics.</summary>
    public Document()
    {
        // We can't shadow the base init-only props (the `new` keyword would
        // hide them without changing the value the serializer reads via a
        // DockableContent reference). Instead, set the base props through
        // the init setter inside this ctor — the runtime treats the
        // constructor body as init-scope, so the setter is callable.
        CanClose = true;   // documents close by default
        CanPin   = false;  // documents do not pin to sides
    }
}

/// <summary>
/// A hideable tool window — lives inside a <c>ToolPane</c>; pinnable to a
/// side strip; can be auto-hidden. Default tab styling: bottom-position,
/// compact.
/// </summary>
/// <remarks>
/// Spec 045 §5.3.1 (Phase 2 addition). The X button on a ToolWindow
/// <em>hides</em> rather than closes (AvalonDock semantic). See spec 045
/// §5.3.8 for permission defaults.
/// </remarks>
public sealed record ToolWindow : DockableContent
{
    /// <summary>X button hides rather than closes. Default true.</summary>
    public bool CanHide { get; init; } = true;

    /// <summary>Auto-hide affordance enabled. Default true.</summary>
    public bool CanAutoHide { get; init; } = true;

    /// <summary>Tool window can be docked as a document (promoted into a DocumentPane). Default true.</summary>
    public bool CanDockAsDocument { get; init; } = true;

    /// <summary>
    /// Edges this tool window may dock to. Affects drag-drop drop-target
    /// eligibility (filtered targets dim during drag, hit-test ignores
    /// them) and programmatic <see cref="DockHostModel.PinToSide"/>
    /// (which throws <see cref="InvalidOperationException"/> on an edge
    /// the mask forbids). Default <see cref="DockSides.All"/> preserves
    /// pre-spec-046 unconstrained placement.
    /// </summary>
    /// <remarks>
    /// Spec 046 §6.2 / §6.6 / §9 Q4. Strategies that need to bypass the
    /// mask (e.g. a custom layout strategy placing a tool window on its
    /// own terms) should clear the mask via <c>tw with { AllowedSides =
    /// DockSides.All }</c> before calling <see cref="DockHostModel.PinToSide"/>.
    /// Setting the mask to <see cref="DockSides.None"/> is allowed and
    /// means the tool window is float-only — every <see cref="DockHostModel.PinToSide"/>
    /// call throws.
    /// </remarks>
    public DockSides AllowedSides { get; init; } = DockSides.All;

    /// <summary>Parameterless ctor — overrides the base permission defaults to ToolWindow semantics.</summary>
    public ToolWindow()
    {
        CanClose = false;  // X button hides, doesn't close
        CanPin   = true;   // tool windows pin to sides by default
    }
}

/// <summary>
/// A document with typed per-pane state. The <typeparamref name="TState"/>
/// is serialized through <c>WindowPersistedScope</c> (spec 033/036) and
/// included in the layout JSON round-trip so app state survives
/// save→quit→restart.
/// </summary>
/// <remarks>
/// Spec 045 §5.3.2 (Phase 2 addition). Per-pane <typeparamref name="TState"/>
/// schema versioning is the app's responsibility — Reactor stores
/// whatever round-trips through the configured JSON serializer; if the
/// app changes <typeparamref name="TState"/>'s shape it owns the
/// migration (spec 045 §8.11).
///
/// <para>
/// Example: a code-editor pane stores its scroll offset and selection range:
/// <code>
/// new Document&lt;EditorState&gt; {
///     Title = "MainView.xaml",
///     Key = fileId,
///     State = new EditorState(scrollOffset: 1024, caret: (line: 42, col: 7)),
///     Content = new Editor(...)
/// }
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TState">App-defined per-pane state envelope. Must be
/// JSON-serializable through the app's configured
/// <c>System.Text.Json.JsonSerializerOptions</c>.</typeparam>
public sealed record Document<TState> : Document
{
    /// <summary>The typed pane state envelope. Round-trips through layout JSON.</summary>
    public TState? State { get; init; }
}
