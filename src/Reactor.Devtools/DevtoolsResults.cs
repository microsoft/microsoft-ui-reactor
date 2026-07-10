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
    string? Host = null,
    string? Pane = null,
    string? Name = null,
    string? Expected = null,
    string? Actual = null,
    string[]? Available = null,
    string[]? ReachableMethods = null,
    string? Hint = null);
