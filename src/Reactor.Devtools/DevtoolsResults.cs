namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Structured error data attached to an <see cref="McpToolException"/> (surfaces as
/// <c>JsonRpcError.Data</c>). One named record — every field optional and omitted
/// when null (the devtools JSON options use <c>WhenWritingNull</c>) — replaces the
/// per-throw anonymous <c>{ code, ... }</c> payloads so the error path serializes
/// through the source generator instead of the reflection fallback (AOT-safe).
/// Each tool sets only the fields relevant to its error <c>code</c>; the union
/// keeps the wire shape byte-identical to the previous anonymous payloads.
/// </summary>
internal sealed record McpErrorData(
    string Code,
    string? Component = null,
    string? Event = null,
    string? Selector = null,
    string? Window = null,
    string? HostId = null,
    string? PaneKey = null,
    int? MatchCount = null,
    string? Name = null,
    string? Expected = null,
    string? Actual = null,
    string? Pattern = null,
    string? Axis = null,
    string? Flag = null,
    int? Phase = null,
    string[]? ActiveIds = null,
    string[]? Available = null,
    string[]? ReachableMethods = null,
    string? Id = null,
    string? IdWindow = null,
    string? Requested = null,
    string[]? Candidates = null,
    int? Count = null,
    string? Hint = null);

/// <summary>An empty JSON-RPC result object (<c>{}</c>) — <c>ping</c> / <c>notifications/*</c>.</summary>
internal sealed record EmptyResult;

/// <summary>Result of <c>resources/list</c> — an empty inventory (not implemented yet).</summary>
internal sealed record ResourcesListResult(object[] Resources);

/// <summary>Result of <c>prompts/list</c> — an empty inventory (not implemented yet).</summary>
internal sealed record PromptsListResult(object[] Prompts);

/// <summary>Result of the MCP <c>initialize</c> handshake.</summary>
internal sealed record InitializeResult(
    string ProtocolVersion,
    InitializeCapabilities Capabilities,
    InitializeServerInfo ServerInfo);

internal sealed record InitializeCapabilities(ToolsCapability Tools);

internal sealed record ToolsCapability(bool ListChanged);

internal sealed record InitializeServerInfo(string Name, string Version);

// -- reactor.* + windows.* tools (DevtoolsTools) ----------------------------------

/// <summary>Result of the <c>version</c> tool.</summary>
internal sealed record VersionResult(string Build, int Pid, int McpPort);

/// <summary>Result of the <c>components</c> tool. <see cref="Components"/> is either a
/// <c>ComponentInfo[]</c> (detailed host) or a <c>string[]</c> (name-only host).</summary>
internal sealed record ComponentsResult(object Components, string? Current);

/// <summary>Result of the <c>switchComponent</c> tool.</summary>
internal sealed record SwitchComponentResult(bool Ok, string Current);

/// <summary>Result of the <c>reload</c> / <c>shutdown</c> tools.</summary>
internal sealed record ExitResult(bool Ok, string ExitingBuild);

/// <summary>Result of the <c>windows</c> tool (hwnd-opt-in projection).</summary>
internal sealed record WindowsResult(WindowDto[] Windows);

internal sealed record WindowDto(
    string Id,
    string Title,
    long? Hwnd,
    WindowBoundsDto Bounds,
    bool IsMain,
    string? BuildTag,
    string? CurrentComponent);

internal sealed record WindowBoundsDto(int X, int Y, int Width, int Height);

/// <summary>Result of the <c>windows.list</c> tool.</summary>
internal sealed record WindowsListResult(WindowListItem[] Windows);

internal sealed record WindowListItem(
    string Id,
    string? Key,
    string Title,
    double Width,
    double Height,
    uint Dpi,
    string State,
    bool IsMain);

/// <summary>Result of <c>windows.activate</c> / <c>windows.open</c>.</summary>
internal sealed record WindowOkResult(bool Ok, string Id);

/// <summary>Result of <c>windows.close</c>.</summary>
internal sealed record WindowCloseResult(bool Ok, bool Cancelled, string Id);

// -- UIA automation tools (DevtoolsUiaTools) --------------------------------------

/// <summary>Result of a UIA action that only reports success (e.g. scroll-item).</summary>
internal sealed record OkResult(bool Ok);

/// <summary>Result of <c>toggle</c> / expand-collapse — success + the new state string.</summary>
internal sealed record OkStateResult(bool Ok, string State);

/// <summary>Result of <c>select</c> — success + whether the item ended up selected.</summary>
internal sealed record OkSelectedResult(bool Ok, bool Selected);

/// <summary>Result of <c>click</c> / <c>invoke</c> — success + which UIA pattern was used.</summary>
internal sealed record OkViaResult(bool Ok, string Via);

/// <summary>Result of <c>waitFor</c> — success carries elapsedMs; timeout adds reason + observed.</summary>
internal sealed record WaitForResult(
    bool Ok,
    long ElapsedMs,
    string? Reason = null,
    WaitObserved? Observed = null);

internal sealed record WaitObserved(int? Count, string? Text, bool? Visible);

/// <summary>Result of <c>scroll</c> — success plus the resulting scroll geometry.</summary>
internal sealed record ScrollResult(
    bool Ok,
    ScrollAxis ScrollPercent,
    ScrollAxis ScrollOffsetPx,
    ScrollableSize ScrollableSizePx);

internal sealed record ScrollAxis(double? Horizontal, double? Vertical);

internal sealed record ScrollableSize(double? Width, double? Height);
