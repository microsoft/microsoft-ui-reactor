using System;
using System.Threading.Tasks;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

// PermissionDecision lives under GitHub.Copilot.Rpc and is marked [Experimental]
// (GHCP001). We intentionally use it to build a deny-by-default permission handler.
#pragma warning disable GHCP001

namespace WidgetCreator.Services;

/// <summary>
/// Deny-by-default permission handler for the code-generation agent (threat-model
/// C-1). Widget Creator uses Copilot purely as a <b>text generator</b>: the
/// pipeline extracts a <c>```csharp</c> block from the reply and does every file
/// write and build itself, so the agent never needs a tool. The Copilot CLI runs
/// unsandboxed with the user's full rights, so an <c>ApproveAll</c> handler let a
/// prompt-injection (in a widget description, or in a crashed widget's output fed
/// back for repair) drive the CLI into running shell commands or writing files on
/// the host. This handler <b>rejects every tool request</b> — shell, file
/// read/write, URL fetch, MCP, hooks, extensions, memory — and logs it, closing
/// that path. Replaces <c>PermissionHandler.ApproveAll</c>.
/// </summary>
public static class AgentPermissionPolicy
{
    /// <summary>Feedback returned to the model so it self-corrects to a pure code reply.</summary>
    const string RejectFeedback =
        "Tool use is disabled in Widget Creator. Do not run shell commands, read or write files, " +
        "fetch URLs, or call any tool. Reply with ONLY the complete C# source in a single " +
        "```csharp fenced code block.";

    /// <summary>
    /// Rejects every permission request and logs the attempt. Wire into
    /// <c>SessionConfig.OnPermissionRequest</c> / <c>ResumeSessionConfig.OnPermissionRequest</c>.
    /// </summary>
    public static Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>> DenyAll { get; } =
        (request, _) =>
        {
            SessionLog.Write($"[AgentPolicy] DENIED agent tool request — {Describe(request)}");
            return Task.FromResult(PermissionDecision.Reject(RejectFeedback));
        };

    static string Describe(PermissionRequest r) => r switch
    {
        PermissionRequestShell s => $"shell: {Trunc(s.FullCommandText)}",
        PermissionRequestWrite w => $"write: {w.FileName}",
        PermissionRequestRead rd => $"read: {rd.Path}",
        PermissionRequestUrl u => $"url: {u.Url}",
        PermissionRequestCustomTool c => $"custom-tool: {c.ToolName}",
        PermissionRequestMcp m => $"mcp: {m.ServerName}/{m.ToolName}",
        PermissionRequestHook h => $"hook: {h.ToolName}",
        PermissionRequestMemory mem => $"memory: {mem.Subject}",
        PermissionRequestExtensionManagement e => $"extension-mgmt: {e.ExtensionName}",
        PermissionRequestExtensionPermissionAccess e => $"extension-access: {e.ExtensionName}",
        _ => $"kind={r.Kind}",
    };

    static string Trunc(string? s, int n = 200) =>
        string.IsNullOrEmpty(s) ? "(none)" : (s.Length <= n ? s : s[..n] + "…");
}
