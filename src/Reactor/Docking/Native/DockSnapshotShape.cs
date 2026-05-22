namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.26 — DockManager snapshot shape for the docking.snapshot
//  MCP tool. Mirrors the host's live state without exposing pane
//  Content references (which may carry app-owned objects that aren't
//  safe to surface to an MCP client) — just identity + structure.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Read-only snapshot of a <see cref="DockManager"/>'s layout tree +
/// sides. Returned by the §2.26 <c>docking.snapshot</c> MCP tool.
/// </summary>
public sealed record DockSnapshot
{
    /// <summary>Registry id of the host this snapshot was taken from.</summary>
    public string HostId { get; init; } = string.Empty;

    /// <summary>Layout root (null = no docked tree mounted).</summary>
    public DockSnapshotNode? Root { get; init; }

    /// <summary>Hidden / pinned tool windows by side.</summary>
    public IReadOnlyList<DockSnapshotPane> LeftSide { get; init; } = Array.Empty<DockSnapshotPane>();

    /// <summary>Hidden / pinned tool windows on the top strip.</summary>
    public IReadOnlyList<DockSnapshotPane> TopSide { get; init; } = Array.Empty<DockSnapshotPane>();

    /// <summary>Hidden / pinned tool windows on the right strip.</summary>
    public IReadOnlyList<DockSnapshotPane> RightSide { get; init; } = Array.Empty<DockSnapshotPane>();

    /// <summary>Hidden / pinned tool windows on the bottom strip.</summary>
    public IReadOnlyList<DockSnapshotPane> BottomSide { get; init; } = Array.Empty<DockSnapshotPane>();

    /// <summary>Stringified active pane key, or null when nothing is active.</summary>
    public string? ActiveKey { get; init; }
}

/// <summary>Node algebra in the snapshot tree.</summary>
public abstract record DockSnapshotNode
{
    /// <summary>Kind discriminator surfaced as a JSON field.</summary>
    public abstract string Kind { get; }
}

public sealed record DockSnapshotSplit(
    string Orientation,
    IReadOnlyList<DockSnapshotNode> Children) : DockSnapshotNode
{
    public override string Kind => "split";
}

public sealed record DockSnapshotTabGroup(
    int SelectedIndex,
    IReadOnlyList<DockSnapshotPane> Documents) : DockSnapshotNode
{
    public override string Kind => "tabgroup";
}

public sealed record DockSnapshotLeaf(DockSnapshotPane Pane) : DockSnapshotNode
{
    public override string Kind => "leaf";
}

/// <summary>Pane identity surfaced to MCP clients.</summary>
public sealed record DockSnapshotPane(
    string? Key,
    string Title,
    string Role,
    bool CanClose,
    bool CanFloat,
    bool CanMove);

/// <summary>
/// Builds <see cref="DockSnapshot"/> values from a live host. Pure-
/// function transform — no mutation, no side effects.
/// </summary>
public static class DockSnapshotBuilder
{
    /// <summary>Take a snapshot of a registry record. Returns null when the host is no longer live.</summary>
    public static DockSnapshot? FromRecord(DockHostRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var manager = record.Manager;
        if (manager is null) return null;
        return FromManager(manager) with { HostId = record.Id };
    }

    /// <summary>Take a snapshot of a manager directly. <c>HostId</c> will be empty.</summary>
    public static DockSnapshot FromManager(DockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return new DockSnapshot
        {
            HostId = string.Empty,
            Root = manager.Layout is { } root ? FromNode(root) : null,
            LeftSide = ConvertSide(manager.LeftSide),
            TopSide = ConvertSide(manager.TopSide),
            RightSide = ConvertSide(manager.RightSide),
            BottomSide = ConvertSide(manager.BottomSide),
            ActiveKey = manager.ActiveDocument?.Key?.ToString(),
        };
    }

    private static DockSnapshotNode FromNode(DockNode node) => node switch
    {
        DockSplit s => new DockSnapshotSplit(
            Orientation: s.Orientation.ToString(),
            Children: s.Children.Select(FromNode).ToArray()),
        DockTabGroup g => new DockSnapshotTabGroup(
            SelectedIndex: g.SelectedIndex,
            Documents: g.Documents.Select(ConvertPane).ToArray()),
        DockableContent leaf => new DockSnapshotLeaf(ConvertPane(leaf)),
        _ => new DockSnapshotLeaf(new DockSnapshotPane(null, string.Empty, "unknown", false, false, false)),
    };

    private static IReadOnlyList<DockSnapshotPane> ConvertSide(
        IReadOnlyList<DockableContent>? side)
    {
        if (side is null || side.Count == 0) return Array.Empty<DockSnapshotPane>();
        return side.Select(ConvertPane).ToArray();
    }

    private static DockSnapshotPane ConvertPane(DockableContent pane) =>
        new(
            Key: pane.Key?.ToString(),
            Title: pane.Title ?? string.Empty,
            Role: pane switch
            {
                Document => "document",
                ToolWindow => "toolwindow",
                _ => "content",
            },
            CanClose: pane.CanClose,
            CanFloat: pane.CanFloat,
            CanMove: pane.CanMove);
}
