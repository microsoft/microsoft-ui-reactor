using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Persistence;

/// <summary>
/// Reactor-native docking layout persistence — v2 schema reader/writer.
/// Spec 045 §5.4 / §8.9 (security limits) / §8.10 (safe-fallback);
/// tracking §2.7 / §2.11.
/// </summary>
/// <remarks>
/// AOT-clean: all parsing goes through <see cref="DockLayoutJsonContext"/>;
/// no reflection at runtime, no type-name instantiation from JSON, no
/// external schema URLs. Inputs above 1 MB or nested deeper than 32 are
/// rejected at the boundary (spec §8.9).
///
/// <para>
/// The reader's failure mode never throws on the load path: corrupt JSON
/// causes a log event (via <c>ReactorEventSource</c> in callers) and a
/// fallback to the default layout — the load returns
/// <see cref="DockLayoutLoadResult.Fallback"/>. The caller decides what
/// the default is (spec §8.10).
/// </para>
/// </remarks>
public static class DockLayoutSerializer
{
    /// <summary>
    /// Maximum bytes a persisted layout JSON is allowed to occupy. Spec §8.9
    /// security limit; exceeded inputs are rejected as corrupt.
    /// </summary>
    public const int MaxBytes = 1 * 1024 * 1024;

    /// <summary>
    /// Maximum JSON nesting depth allowed in a layout document. Spec §8.9
    /// security limit; exceeded inputs are rejected as corrupt.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>Current schema version emitted by <see cref="Save"/>.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Serializes the supplied tree + side/floating state into a v2 JSON string.</summary>
    /// <param name="root">The docked layout root (Split / TabGroup / leaf).</param>
    /// <param name="leftSide">Left-side pinned tool windows.</param>
    /// <param name="topSide">Top-side pinned tool windows.</param>
    /// <param name="rightSide">Right-side pinned tool windows.</param>
    /// <param name="bottomSide">Bottom-side pinned tool windows.</param>
    /// <param name="floating">Floating-window state.</param>
    /// <param name="activeKey">Stringified key of the currently-active pane, or null.</param>
    public static string Save(
        DockNode? root,
        IReadOnlyList<ToolWindow>? leftSide = null,
        IReadOnlyList<ToolWindow>? topSide = null,
        IReadOnlyList<ToolWindow>? rightSide = null,
        IReadOnlyList<ToolWindow>? bottomSide = null,
        IReadOnlyList<FloatingDockWindow>? floating = null,
        object? activeKey = null)
    {
        var doc = new DockLayoutDoc
        {
            Schema = CurrentSchemaVersion,
            Root = root is not null ? Convert(root) : null,
            LeftSide   = leftSide?.Select(ConvertPane).ToList(),
            TopSide    = topSide?.Select(ConvertPane).ToList(),
            RightSide  = rightSide?.Select(ConvertPane).ToList(),
            BottomSide = bottomSide?.Select(ConvertPane).ToList(),
            Floating   = floating?.Select(ConvertFloating).ToList(),
            ActiveKey  = activeKey?.ToString(),
        };

        return JsonSerializer.Serialize(doc, DockLayoutJsonContext.Default.DockLayoutDoc);
    }

    /// <summary>
    /// Parses a persisted layout JSON string. Always returns a result —
    /// corruption / oversize / depth-overflow yield <see cref="DockLayoutLoadResult.Fallback"/>
    /// with the reason logged (caller is responsible for the actual log
    /// emit via <see cref="DockLayoutLoadResult.FailureReason"/>).
    /// </summary>
    /// <remarks>Spec 045 §5.4, §8.9, §8.10.</remarks>
    public static DockLayoutLoadResult Load(string? json) =>
        Load(json, migrations: null);

    /// <summary>
    /// Parses a persisted layout JSON string, routing pre-v<see cref="CurrentSchemaVersion"/>
    /// payloads through the supplied <see cref="DockLayoutMigrationRegistry"/>
    /// (§5.4.4 / §8.11). When <paramref name="migrations"/> is null, a fresh
    /// registry with the built-in v1→v2 step is used so the common case —
    /// loading a P1 file with no <c>$schema</c> marker — succeeds without
    /// requiring callers to pre-build a registry.
    /// </summary>
    public static DockLayoutLoadResult Load(string? json, DockLayoutMigrationRegistry? migrations)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("empty", "empty input");

        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxBytes)
            return Fail("oversize", $"input exceeds {MaxBytes}-byte limit ({byteCount} bytes)");

        // Parse to a mutable JsonNode tree so the migration ladder can
        // upgrade pre-current-schema documents in place. Enforce
        // MaxDepth at the parse boundary (spec §8.9 security limit).
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException ex)
        {
            return Fail("json-parse", $"json parse failure: {ex.Message}");
        }

        if (rootNode is null)
            return Fail("null-document", "null document");

        // §5.4.4 — detect the source schema. Absent $schema means v1 (the
        // P1 wrapper's vendored format). The registry's DetectSchema
        // encodes that convention.
        int sourceSchema = DockLayoutMigrationRegistry.DetectSchema(rootNode);
        var registry = migrations ?? new DockLayoutMigrationRegistry();

        JsonNode? migratedRoot;
        string? migrationWarning;
        bool upgraded = registry.TryUpgrade(rootNode, sourceSchema, CurrentSchemaVersion, out migratedRoot, out migrationWarning);
        if (!upgraded || migratedRoot is null)
        {
            return Fail("schema-missing",
                migrationWarning ?? $"unable to migrate schema v{sourceSchema} → v{CurrentSchemaVersion}");
        }

        // Forward tolerance (§8.11): the registry returns success-with-warning
        // when the source schema is newer than CurrentSchemaVersion. Emit a
        // PII-safe "schema-newer" event so on-disk traces still capture the
        // case (the load itself proceeds best-effort).
        if (sourceSchema > CurrentSchemaVersion)
        {
            ReactorEventSource.Log.DockingLayoutLoadFallback("schema-newer");
        }

        DockLayoutDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize(migratedRoot, DockLayoutJsonContext.Default.DockLayoutDoc);
        }
        catch (JsonException ex)
        {
            return Fail("json-parse", $"json parse failure post-migration: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return Fail("unsupported-schema", $"unsupported schema: {ex.Message}");
        }

        if (doc is null)
            return Fail("null-document", "null document");

        // After migration the document carries the post-upgrade $schema.
        // Treat a missing/invalid marker post-migration as a genuine
        // schema-missing failure (the migration ladder is supposed to
        // stamp it).
        if (doc.Schema < 1)
            return Fail("schema-missing", $"missing/invalid $schema after migration (got {doc.Schema}, source v{sourceSchema})");

        try
        {
            var root = doc.Root is not null ? ConvertNodeBack(doc.Root) : null;
            var left   = doc.LeftSide?.Select(ConvertPaneBackAsToolWindow).ToList() ?? new();
            var top    = doc.TopSide?.Select(ConvertPaneBackAsToolWindow).ToList() ?? new();
            var right  = doc.RightSide?.Select(ConvertPaneBackAsToolWindow).ToList() ?? new();
            var bottom = doc.BottomSide?.Select(ConvertPaneBackAsToolWindow).ToList() ?? new();
            var floating = doc.Floating?.Select(ConvertFloatingBack).ToList() ?? new();

            return new DockLayoutLoadResult
            {
                Success    = true,
                Schema     = doc.Schema,
                Root       = root,
                LeftSide   = left,
                TopSide    = top,
                RightSide  = right,
                BottomSide = bottom,
                Floating   = floating,
                ActiveKey  = doc.ActiveKey,
                IsFallback = false,
                FailureReason = migrationWarning,
            };
        }
        catch (InvalidOperationException ex)
        {
            return Fail("validation", $"validation failure: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a fallback result + emit a coarse-grained
    /// <c>Microsoft-UI-Reactor</c> ETW event so on-disk traces capture
    /// load failures (spec 044 / 045 §2.7). The event payload is the
    /// <paramref name="category"/> bucket only — PII-safe — while the
    /// in-process <see cref="DockLayoutLoadResult.FailureReason"/>
    /// carries the full message for app-level diagnostics.
    /// </summary>
    private static DockLayoutLoadResult Fail(string category, string reason)
    {
        ReactorEventSource.Log.DockingLayoutLoadFallback(category);
        return DockLayoutLoadResult.Fallback(reason);
    }

    // ── Tree → JSON ─────────────────────────────────────────────────────

    private static DockLayoutNode Convert(DockNode node) => node switch
    {
        DockSplit split => new DockLayoutNode
        {
            Kind = "split",
            Orientation = split.Orientation == Orientation.Horizontal ? "horizontal" : "vertical",
            Children = split.Children.Select(Convert).ToList(),
            Width = split.Width,    Height = split.Height,
            MinWidth = split.MinWidth, MinHeight = split.MinHeight,
            MaxWidth = split.MaxWidth, MaxHeight = split.MaxHeight,
        },
        DockTabGroup grp => new DockLayoutNode
        {
            Kind = "tabGroup",
            Documents = grp.Documents.Select(ConvertPane).ToList(),
            TabPosition = grp.TabPosition == Docking.TabPosition.Top ? "top" : "bottom",
            CompactTabs = grp.CompactTabs ? true : null,
            // §4.6: only emit when non-default. Keeps legacy files unchanged.
            TabChrome = grp.TabChrome switch
            {
                Docking.TabChrome.Win11    => null,
                Docking.TabChrome.Flat     => "flat",
                Docking.TabChrome.TitleBar => "titleBar",
                _ => null,
            },
            ShowWhenEmpty = grp.ShowWhenEmpty ? true : null,
            SelectedIndex = grp.SelectedIndex >= 0 ? grp.SelectedIndex : null,
            Width = grp.Width, Height = grp.Height,
        },
        DockableContent leaf => new DockLayoutNode
        {
            Kind = "pane",
            Pane = ConvertPane(leaf),
            Width = leaf.Width, Height = leaf.Height,
        },
        _ => throw new InvalidOperationException($"Unknown DockNode subtype: {node.GetType().Name}"),
    };

    private static DockLayoutPane ConvertPane(DockableContent leaf) => new()
    {
        Title = leaf.Title,
        Key   = leaf.Key?.ToString(),
        Role  = leaf switch
        {
            ToolWindow => "toolWindow",
            Document   => "document",
            _          => "dockableContent",
        },
        State = leaf.PersistenceState,
        // Only emit overrides when they diverge from the role default. Keep
        // the file small + the round-trip clean.
        CanClose            = ShouldEmitCanClose(leaf),
        CanPin              = ShouldEmitCanPin(leaf),
        CanFloat            = leaf.CanFloat ? null : false,   // default true
        CanMove             = leaf.CanMove  ? null : false,   // default true
        CanHide             = (leaf as ToolWindow) is { } twH && !twH.CanHide ? false : null,
        CanAutoHide         = (leaf as ToolWindow) is { } twA && !twA.CanAutoHide ? false : null,
        CanDockAsDocument   = (leaf as ToolWindow) is { } twD && !twD.CanDockAsDocument ? false : null,
        CanDockAsToolWindow = (leaf as Document) is { CanDockAsToolWindow: true } ? true : null,
        Width  = leaf.Width,
        Height = leaf.Height,
    };

    private static DockLayoutFloatingWindow ConvertFloating(FloatingDockWindow fw) => new()
    {
        Id = fw.Id?.ToString() ?? string.Empty,
        X = fw.X, Y = fw.Y, Width = fw.Width, Height = fw.Height,
        Contents = fw.Contents.Select(ConvertPane).ToList(),
    };

    // Role-default-aware emission for CanClose / CanPin: defaults differ by
    // role, so we only emit when the runtime value is the inverse of the
    // role default. Avoids redundant fields in the JSON.
    private static bool? ShouldEmitCanClose(DockableContent leaf)
    {
        bool roleDefault = leaf switch
        {
            Document   => true,    // documents close by default
            ToolWindow => false,   // tool windows hide by default
            _          => false,   // P1 base default
        };
        return leaf.CanClose == roleDefault ? null : leaf.CanClose;
    }

    private static bool? ShouldEmitCanPin(DockableContent leaf)
    {
        bool roleDefault = leaf switch
        {
            Document   => false,
            ToolWindow => true,
            _          => false,
        };
        return leaf.CanPin == roleDefault ? null : leaf.CanPin;
    }

    // ── JSON → tree ─────────────────────────────────────────────────────

    private static DockNode ConvertNodeBack(DockLayoutNode node) => node.Kind switch
    {
        "split" => ConvertSplitBack(node),
        "tabGroup" => ConvertTabGroupBack(node),
        "pane" when node.Pane is not null => ConvertPaneBackAsLeaf(node.Pane),
        _ => throw new InvalidOperationException($"Unknown layout node kind '{node.Kind}' (or missing pane payload)"),
    };

    private static DockSplit ConvertSplitBack(DockLayoutNode node)
    {
        var orientation = node.Orientation switch
        {
            "horizontal" => Orientation.Horizontal,
            "vertical"   => Orientation.Vertical,
            _ => throw new InvalidOperationException($"split node missing/invalid orientation '{node.Orientation}'"),
        };
        var children = node.Children?.Select(ConvertNodeBack).ToList() ?? new();
        return new DockSplit(orientation, children,
            Width: node.Width, Height: node.Height,
            MinWidth: node.MinWidth, MinHeight: node.MinHeight,
            MaxWidth: node.MaxWidth, MaxHeight: node.MaxHeight);
    }

    private static DockTabGroup ConvertTabGroupBack(DockLayoutNode node)
    {
        var docs = node.Documents?.Select(ConvertPaneBackAsLeaf).ToList() ?? new();
        var tabPos = node.TabPosition switch
        {
            "top" or null => Docking.TabPosition.Top,
            "bottom"      => Docking.TabPosition.Bottom,
            _ => throw new InvalidOperationException($"tabGroup node has invalid tabPosition '{node.TabPosition}'"),
        };
        var chrome = node.TabChrome switch
        {
            null or "win11" => Docking.TabChrome.Win11,
            "flat"          => Docking.TabChrome.Flat,
            "titleBar"      => Docking.TabChrome.TitleBar,
            _ => throw new InvalidOperationException($"tabGroup node has invalid tabChrome '{node.TabChrome}'"),
        };
        return new DockTabGroup(docs,
            TabPosition: tabPos,
            CompactTabs: node.CompactTabs ?? false,
            ShowWhenEmpty: node.ShowWhenEmpty ?? false,
            SelectedIndex: node.SelectedIndex ?? -1,
            Width: node.Width, Height: node.Height,
            TabChrome: chrome);
    }

    private static DockableContent ConvertPaneBackAsLeaf(DockLayoutPane pane) =>
        pane.Role switch
        {
            "document" => new Document
            {
                Title = pane.Title,
                Key = pane.Key,
                CanClose = pane.CanClose ?? true,
                CanPin   = pane.CanPin   ?? false,
                CanFloat = pane.CanFloat ?? true,
                CanMove  = pane.CanMove  ?? true,
                CanDockAsToolWindow = pane.CanDockAsToolWindow ?? false,
                PersistenceState = pane.State,
                Width = pane.Width, Height = pane.Height,
            },
            "toolWindow" => ConvertPaneBackAsToolWindow(pane),
            _ => new DockableContent(
                Title: pane.Title,
                Key: pane.Key,
                CanClose: pane.CanClose ?? false,
                CanPin: pane.CanPin ?? false,
                Width: pane.Width,
                Height: pane.Height,
                PersistenceState: pane.State)
            {
                CanFloat = pane.CanFloat ?? true,
                CanMove  = pane.CanMove  ?? true,
            },
        };

    private static ToolWindow ConvertPaneBackAsToolWindow(DockLayoutPane pane) => new()
    {
        Title = pane.Title,
        Key = pane.Key,
        CanClose = pane.CanClose ?? false,
        CanPin   = pane.CanPin   ?? true,
        CanFloat = pane.CanFloat ?? true,
        CanMove  = pane.CanMove  ?? true,
        CanHide             = pane.CanHide             ?? true,
        CanAutoHide         = pane.CanAutoHide         ?? true,
        CanDockAsDocument   = pane.CanDockAsDocument   ?? true,
        PersistenceState = pane.State,
        Width = pane.Width, Height = pane.Height,
    };

    private static FloatingDockWindow ConvertFloatingBack(DockLayoutFloatingWindow fw) => new()
    {
        Id = fw.Id,
        X = fw.X, Y = fw.Y, Width = fw.Width, Height = fw.Height,
        Contents = fw.Contents.Select(ConvertPaneBackAsLeaf).ToList(),
    };
}

/// <summary>
/// Result envelope returned by <see cref="DockLayoutSerializer.Load(string?)"/>.
/// Always non-null; <see cref="IsFallback"/> indicates whether the input
/// was usable.
/// </summary>
public sealed class DockLayoutLoadResult
{
    /// <summary>Whether the parse succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Schema version detected in the input (0 if fallback).</summary>
    public int Schema { get; init; }

    /// <summary>Parsed root, or null if no root or fallback.</summary>
    public DockNode? Root { get; init; }

    /// <summary>Parsed left-side ToolWindows.</summary>
    public IReadOnlyList<ToolWindow> LeftSide { get; init; } = Array.Empty<ToolWindow>();

    /// <summary>Parsed top-side ToolWindows.</summary>
    public IReadOnlyList<ToolWindow> TopSide { get; init; } = Array.Empty<ToolWindow>();

    /// <summary>Parsed right-side ToolWindows.</summary>
    public IReadOnlyList<ToolWindow> RightSide { get; init; } = Array.Empty<ToolWindow>();

    /// <summary>Parsed bottom-side ToolWindows.</summary>
    public IReadOnlyList<ToolWindow> BottomSide { get; init; } = Array.Empty<ToolWindow>();

    /// <summary>Parsed floating windows.</summary>
    public IReadOnlyList<FloatingDockWindow> Floating { get; init; } = Array.Empty<FloatingDockWindow>();

    /// <summary>Active pane key (stringified) at save time.</summary>
    public string? ActiveKey { get; init; }

    /// <summary>True when the load failed and the caller should fall back to default.</summary>
    public bool IsFallback { get; init; }

    /// <summary>If <see cref="IsFallback"/>, the reason — suitable for logging.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Constructs a fallback (failure) result with the given reason.</summary>
    public static DockLayoutLoadResult Fallback(string reason) => new()
    {
        Success = false,
        IsFallback = true,
        FailureReason = reason,
    };
}
