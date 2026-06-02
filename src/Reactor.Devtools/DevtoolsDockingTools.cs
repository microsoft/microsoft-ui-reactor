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
                InputSchema: new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false,
                }),
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
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        hostId = new { type = "string", description = "Host id from docking.list (e.g. 'dh:1')." },
                    },
                    required = new[] { "hostId" },
                    additionalProperties = false,
                }),
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
                InputSchema: new
                {
                    type = "object",
                    properties = new
                    {
                        hostId = new { type = "string", description = "Host id from docking.list." },
                        paneKey = new { type = "string", description = "Stringified pane Key (matches DockableContent.Key.ToString())." },
                        action = new { type = "string", description = "dock | float | hide | show | close | activate | pinToSide" },
                        target = new { type = "string", description = "DockTarget for action=dock (Center, SplitLeft, ...)." },
                        side = new { type = "string", description = "DockSide for action=pinToSide (Left, Top, Right, Bottom)." },
                    },
                    required = new[] { "hostId", "paneKey", "action" },
                    additionalProperties = false,
                }),
            @params => server.OnDispatcher<object>(() => BuildDockPayload(@params)));
    }

    // ── Payload builders (testable without the live MCP transport) ──────

    internal static object BuildListPayload()
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
                return (object)new
                {
                    id = r.Id,
                    paneCount,
                    activeKey = snapshot.ActiveKey,
                    sideCounts = new
                    {
                        left = snapshot.LeftSide.Count,
                        top = snapshot.TopSide.Count,
                        right = snapshot.RightSide.Count,
                        bottom = snapshot.BottomSide.Count,
                    },
                };
            })
            .Where(h => h is not null)
            .ToArray();
        return new { hosts };
    }

    internal static object BuildSnapshotPayload(JsonElement? @params)
    {
        var hostId = DevtoolsTools.ReadString(@params, "hostId")
            ?? throw new McpToolException("Missing 'hostId'.", JsonRpcErrorCodes.InvalidParams);
        var record = DockHostRegistry.Get(hostId)
            ?? throw new McpToolException(
                $"Unknown hostId '{hostId}'.",
                JsonRpcErrorCodes.ToolExecution,
                new { code = "unknown-host", hostId });
        var snapshot = DockSnapshotBuilder.FromRecord(record)
            ?? throw new McpToolException(
                $"Host '{hostId}' is no longer live (manager GC'd).",
                JsonRpcErrorCodes.ToolExecution,
                new { code = "host-gc", hostId });
        return ToJsonShape(snapshot);
    }

    internal static object BuildDockPayload(JsonElement? @params)
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
                new { code = "unknown-host", hostId });
        var manager = record.Manager
            ?? throw new McpToolException(
                $"Host '{hostId}' is no longer live (manager GC'd).",
                JsonRpcErrorCodes.ToolExecution,
                new { code = "host-gc", hostId });
        var model = DockHostModelBridge.Get(manager)
            ?? throw new McpToolException(
                $"Host '{hostId}' has no bound DockHostModel (renderer not yet mounted?).",
                JsonRpcErrorCodes.ToolExecution,
                new { code = "no-model", hostId });

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
                new { code = "unknown-pane", hostId, paneKey });
        if (matchCount > 1)
            throw new McpToolException(
                $"Pane key '{paneKey}' is ambiguous on host '{hostId}' ({matchCount} matches). " +
                "Distinct DockableContent.Key objects whose ToString() collide cannot be addressed by docking.dock today; " +
                "give the panes unique stringified keys (spec §2.9 / §2.26 follow-up: stable pane-id field).",
                JsonRpcErrorCodes.ToolExecution,
                new { code = "ambiguous-pane", hostId, paneKey, matchCount });

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

        return new { ok = true, hostId, paneKey, action };
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

    // Convert the typed DockSnapshot record to anonymous-object shape so the
    // JSON-RPC framework's existing serializer surface handles it without
    // needing source-gen entries in DevtoolsJsonContext. The shape mirrors
    // DockSnapshotShape one-to-one.
    internal static object ToJsonShape(DockSnapshot snap) => new
    {
        hostId = snap.HostId,
        root = NodeToJson(snap.Root),
        leftSide = snap.LeftSide.Select(PaneToJson).ToArray(),
        topSide = snap.TopSide.Select(PaneToJson).ToArray(),
        rightSide = snap.RightSide.Select(PaneToJson).ToArray(),
        bottomSide = snap.BottomSide.Select(PaneToJson).ToArray(),
        activeKey = snap.ActiveKey,
    };

    private static object? NodeToJson(DockSnapshotNode? node) => node switch
    {
        null => null,
        DockSnapshotSplit s => new
        {
            kind = "split",
            orientation = s.Orientation,
            children = s.Children.Select(NodeToJson).ToArray(),
        },
        DockSnapshotTabGroup g => new
        {
            kind = "tabgroup",
            selectedIndex = g.SelectedIndex,
            documents = g.Documents.Select(PaneToJson).ToArray(),
        },
        DockSnapshotLeaf l => new
        {
            kind = "leaf",
            pane = PaneToJson(l.Pane),
        },
        _ => null,
    };

    private static object PaneToJson(DockSnapshotPane p) => new
    {
        key = p.Key,
        title = p.Title,
        role = p.Role,
        canClose = p.CanClose,
        canFloat = p.CanFloat,
        canMove = p.CanMove,
    };
}
