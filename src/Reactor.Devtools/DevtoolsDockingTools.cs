using System.Text.Json;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.26 — docking.list / docking.snapshot / docking.dock
//  MCP tool registration.
//
//  Backs onto the building blocks shipped earlier in P2:
//    • DockHostRegistry — process-wide WeakReference-keyed enumeration
//      of live DockManager elements with stable "dh:{n}" ids.
//    • DockSnapshotBuilder — pure-function transform from a DockManager
//      to a content-ref-free DockSnapshot (layout tree + sides + active
//      key + identity + role + permissions per pane).
//    • DockHostModelBridge — resolves the live DockHostModel from a
//      DockManager element ref so mutators run against the same state
//      the host renderer reads/writes.
//
//  All three tools run on the UI dispatcher (server.OnDispatcher<T>)
//  because the model + registry are UI-thread-affined per §8.10.
// ════════════════════════════════════════════════════════════════════════

internal static class DevtoolsDockingTools
{
    public static void Register(DevtoolsMcpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        RegisterList(server);
        RegisterSnapshot(server);
        RegisterDock(server);
    }

    private static void RegisterList(DevtoolsMcpServer server)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "docking.list",
                Description:
                    "Enumerates every live DockManager host in the process. " +
                    "Returns { hosts: [{ id, paneCount, activeKey, sideCounts }] }. " +
                    "Host ids are stable for the lifetime of the underlying element; " +
                    "agents pass them to docking.snapshot / docking.dock.",
                InputSchema: Schema.Root()),
            _ => server.OnDispatcher<object>(() => BuildListPayload()));
    }

    private static void RegisterSnapshot(DevtoolsMcpServer server)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "docking.snapshot",
                Description:
                    "Returns the layout snapshot for a single host: layout tree, " +
                    "side strips, and active pane key. Tree carries identity + role + " +
                    "permissions per pane; never the app-owned Content references " +
                    "(privacy + AOT-safe).",
                InputSchema: Schema.Root(
                    new[] { "hostId" },
                    ("hostId", Schema.Str("Host id from docking.list (e.g. 'dh:1').")))),
            @params => server.OnDispatcher<object>(() => BuildSnapshotPayload(@params)));
    }

    private static void RegisterDock(DevtoolsMcpServer server)
    {
        server.Tools.Register(
            new McpToolDescriptor(
                Name: "docking.dock",
                Description:
                    "Drives a docking mutation on a live host's DockHostModel. " +
                    "Actions: dock | float | hide | show | close | activate | pinToSide. " +
                    "Pane resolution is by stringified Key against the host's AllContent(). " +
                    "Each call routes through the model's mutator queue and the §2.16 drain " +
                    "fires the matching lifecycle event (OnContentDocked / OnDocumentClosed " +
                    "/ OnToolWindowHiding / OnContentFloating / ...). Mid-flight drag state " +
                    "is intentionally not exposed (spec N6).",
                InputSchema: Schema.Root(
                    new[] { "hostId", "paneKey", "action" },
                    ("hostId", Schema.Str("Host id from docking.list.")),
                    ("paneKey", Schema.Str("Stringified pane Key (matches DockableContent.Key.ToString()).")),
                    ("action", Schema.Str("dock | float | hide | show | close | activate | pinToSide")),
                    ("target", Schema.Str("DockTarget for action=dock (Center, SplitLeft, ...).")),
                    ("side", Schema.Str("DockSide for action=pinToSide (Left, Top, Right, Bottom).")))),
            @params => server.OnDispatcher<object>(() => BuildDockPayload(@params)));
    }

    // ── Payload builders (testable without the live MCP transport) ──────

    internal static DockListResult BuildListPayload()
    {
        var records = DockHostRegistry.Snapshot();
        var hosts = records
            .Select(r =>
            {
                var manager = r.Manager;
                if (manager is null) return null;
                var snapshot = DockSnapshotBuilder.FromRecord(r);
                if (snapshot is null) return null;
                int paneCount = CountPanes(snapshot.Root);
                paneCount += snapshot.LeftSide.Count + snapshot.TopSide.Count
                           + snapshot.RightSide.Count + snapshot.BottomSide.Count;
                return new DockHostSummary(
                    Id: r.Id,
                    PaneCount: paneCount,
                    ActiveKey: snapshot.ActiveKey,
                    SideCounts: new DockSideCounts(
                        Left: snapshot.LeftSide.Count,
                        Top: snapshot.TopSide.Count,
                        Right: snapshot.RightSide.Count,
                        Bottom: snapshot.BottomSide.Count));
            })
            .Where(h => h is not null)
            .Select(h => h!)
            .ToArray();
        return new DockListResult(hosts);
    }

    internal static DockSnapshotResult BuildSnapshotPayload(JsonElement? @params)
    {
        var hostId = DevtoolsTools.ReadString(@params, "hostId")
            ?? throw new McpToolException("Missing 'hostId'.", JsonRpcErrorCodes.InvalidParams);
        var record = DockHostRegistry.Get(hostId)
            ?? throw new McpToolException(
                $"Unknown hostId '{hostId}'.",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("unknown-host", HostId: hostId));
        var snapshot = DockSnapshotBuilder.FromRecord(record)
            ?? throw new McpToolException(
                $"Host '{hostId}' is no longer live (manager GC'd).",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("host-gc", HostId: hostId));
        return ToJsonShape(snapshot);
    }

    internal static DockActionResult BuildDockPayload(JsonElement? @params)
    {
        var hostId = DevtoolsTools.ReadString(@params, "hostId")
            ?? throw new McpToolException("Missing 'hostId'.", JsonRpcErrorCodes.InvalidParams);
        var paneKey = DevtoolsTools.ReadString(@params, "paneKey")
            ?? throw new McpToolException("Missing 'paneKey'.", JsonRpcErrorCodes.InvalidParams);
        var action = DevtoolsTools.ReadString(@params, "action")
            ?? throw new McpToolException("Missing 'action'.", JsonRpcErrorCodes.InvalidParams);

        var record = DockHostRegistry.Get(hostId)
            ?? throw new McpToolException(
                $"Unknown hostId '{hostId}'.",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("unknown-host", HostId: hostId));
        var manager = record.Manager
            ?? throw new McpToolException(
                $"Host '{hostId}' is no longer live (manager GC'd).",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("host-gc", HostId: hostId));
        var model = DockHostModelBridge.Get(manager)
            ?? throw new McpToolException(
                $"Host '{hostId}' has no bound DockHostModel (renderer not yet mounted?).",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("no-model", HostId: hostId));

        // Resolve the pane via the model's enumeration so we match docked,
        // side-stripped, and floating panes alike. Stringified-key
        // comparison matches DockSnapshotPane.Key, so a snapshot's pane
        // key always resolves back to the same pane here. Two panes with
        // distinct Key objects whose ToString() collide are an ambiguous
        // mutation target — surface that as a clear failure rather than
        // silently mutating the first match.
        DockableContent? pane = null;
        int matchCount = 0;
        foreach (var p in model.AllContent())
        {
            if (string.Equals(p.Key?.ToString(), paneKey, StringComparison.Ordinal))
            {
                pane ??= p;
                matchCount++;
                if (matchCount > 1) break;
            }
        }
        if (pane is null)
            throw new McpToolException(
                $"No pane with key '{paneKey}' on host '{hostId}'.",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("unknown-pane", HostId: hostId, PaneKey: paneKey));
        if (matchCount > 1)
            throw new McpToolException(
                $"Pane key '{paneKey}' is ambiguous on host '{hostId}' ({matchCount} matches). " +
                "Distinct DockableContent.Key objects whose ToString() collide cannot be addressed by docking.dock today; " +
                "give the panes unique stringified keys (spec §2.9 / §2.26 follow-up: stable pane-id field).",
                JsonRpcErrorCodes.ToolExecution,
                new McpErrorData("ambiguous-pane", HostId: hostId, PaneKey: paneKey, MatchCount: matchCount));

        switch (action.ToLowerInvariant())
        {
            case "dock":
                {
                    var targetText = DevtoolsTools.ReadString(@params, "target")
                        ?? throw new McpToolException("action=dock requires 'target'.", JsonRpcErrorCodes.InvalidParams);
                    if (!Enum.TryParse<DockTarget>(targetText, ignoreCase: true, out var target))
                        throw new McpToolException(
                            $"Unknown DockTarget '{targetText}'.",
                            JsonRpcErrorCodes.InvalidParams);
                    model.Dock(pane, target);
                    break;
                }
            case "float":
                model.Float(pane);
                break;
            case "hide":
                if (pane is not ToolWindow twHide)
                    throw new McpToolException(
                        $"action=hide requires a ToolWindow; pane '{paneKey}' is a {pane.GetType().Name}.",
                        JsonRpcErrorCodes.InvalidParams);
                model.Hide(twHide);
                break;
            case "show":
                model.Show(pane);
                break;
            case "close":
                model.Close(pane);
                break;
            case "activate":
                model.Activate(pane);
                break;
            case "pintoside":
            case "pin":
                {
                    if (pane is not ToolWindow twPin)
                        throw new McpToolException(
                            $"action=pinToSide requires a ToolWindow; pane '{paneKey}' is a {pane.GetType().Name}.",
                            JsonRpcErrorCodes.InvalidParams);
                    var sideText = DevtoolsTools.ReadString(@params, "side") ?? "Left";
                    if (!Enum.TryParse<DockSide>(sideText, ignoreCase: true, out var side))
                        throw new McpToolException(
                            $"Unknown DockSide '{sideText}'.",
                            JsonRpcErrorCodes.InvalidParams);
                    model.PinToSide(twPin, side);
                    break;
                }
            default:
                throw new McpToolException(
                    $"Unknown action '{action}'. Use dock | float | hide | show | close | activate | pinToSide.",
                    JsonRpcErrorCodes.InvalidParams);
        }

        return new DockActionResult(Ok: true, HostId: hostId, PaneKey: paneKey, Action: action);
    }

    // ── Shape helpers ────────────────────────────────────────────────────

    private static int CountPanes(DockSnapshotNode? node) => node switch
    {
        null => 0,
        DockSnapshotLeaf => 1,
        DockSnapshotTabGroup g => g.Documents.Count,
        DockSnapshotSplit s => s.Children.Sum(CountPanes),
        _ => 0,
    };

    // Convert the typed DockSnapshot record into the devtools wire shape via
    // named records (registered in DevtoolsJsonContext) so the JSON-RPC
    // response serializes through the source generator under NativeAOT. The
    // shape mirrors the previous anonymous objects one-to-one.
    internal static DockSnapshotResult ToJsonShape(DockSnapshot snap) => new DockSnapshotResult(
        HostId: snap.HostId,
        Root: NodeToJson(snap.Root),
        LeftSide: snap.LeftSide.Select(PaneToJson).ToArray(),
        TopSide: snap.TopSide.Select(PaneToJson).ToArray(),
        RightSide: snap.RightSide.Select(PaneToJson).ToArray(),
        BottomSide: snap.BottomSide.Select(PaneToJson).ToArray(),
        ActiveKey: snap.ActiveKey);

    private static DockNodeDto? NodeToJson(DockSnapshotNode? node) => node switch
    {
        null => null,
        DockSnapshotSplit s => new DockNodeDto(
            Kind: "split",
            Orientation: s.Orientation,
            Children: s.Children.Select(NodeToJson).ToArray()),
        DockSnapshotTabGroup g => new DockNodeDto(
            Kind: "tabgroup",
            SelectedIndex: g.SelectedIndex,
            Documents: g.Documents.Select(PaneToJson).ToArray()),
        DockSnapshotLeaf l => new DockNodeDto(
            Kind: "leaf",
            Pane: PaneToJson(l.Pane)),
        _ => null,
    };

    private static DockPaneDto PaneToJson(DockSnapshotPane p) =>
        new(p.Key, p.Title, p.Role, p.CanClose, p.CanFloat, p.CanMove);
}

/// <summary>Result of the <c>docking list</c> tool — one summary per dock host.</summary>
internal sealed record DockListResult(DockHostSummary[] Hosts);

internal sealed record DockHostSummary(string Id, int PaneCount, string? ActiveKey, DockSideCounts SideCounts);

internal sealed record DockSideCounts(int Left, int Top, int Right, int Bottom);

/// <summary>Result of the <c>docking snapshot</c> tool — the full dock layout tree.</summary>
internal sealed record DockSnapshotResult(
    string HostId,
    DockNodeDto? Root,
    DockPaneDto[] LeftSide,
    DockPaneDto[] TopSide,
    DockPaneDto[] RightSide,
    DockPaneDto[] BottomSide,
    string? ActiveKey);

/// <summary>
/// A node in the dock layout tree. Union over the split/tabgroup/leaf shapes
/// (discriminated by <see cref="Kind"/>); unused arms are null and omitted, so the
/// wire is identical to the previous per-kind anonymous objects. Recursive via
/// <see cref="Children"/>.
/// </summary>
internal sealed record DockNodeDto(
    string Kind,
    string? Orientation = null,
    DockNodeDto?[]? Children = null,
    int? SelectedIndex = null,
    DockPaneDto[]? Documents = null,
    DockPaneDto? Pane = null);

/// <summary>Pane identity as surfaced by the <c>docking</c> tool.</summary>
internal sealed record DockPaneDto(string? Key, string Title, string Role, bool CanClose, bool CanFloat, bool CanMove);

/// <summary>Result of a <c>docking dock</c> mutation action.</summary>
internal sealed record DockActionResult(bool Ok, string HostId, string PaneKey, string Action) : IOkResult;
