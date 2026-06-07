using Microsoft.UI.Reactor;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

public enum DevtoolsSubverb
{
    Run,
    List,
    Screenshot,
    Tree,
    App,
}

/// <summary>Which MCP transport the devtools surface uses.</summary>
public enum McpTransport
{
    Http,
    Stdio,
}

/// <summary>
/// Parsed result of a <c>--devtools</c> / <c>--preview</c> invocation.
/// <see cref="Subverb"/> is <c>null</c> when neither flag is present.
/// </summary>
public sealed record DevtoolsCliOptions(
    DevtoolsSubverb? Subverb,
    string? ComponentName,
    bool VsCodeMode,
    int Fps,
    string? ListOutputPath,
    string? ScreenshotOutputPath,
    int? McpPort,
    string? LogLevel,
    McpTransport Transport,
    bool UsedDeprecatedPreview,
    bool PreviewAndDevtoolsConflict,
    string? ProjectIdentifier = null,
    bool LogsDisabled = false,
    int? LogsCapacityMb = null,
    bool EmbedRequested = false,
    WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child,
    int? EmbedHostPid = null,
    string? EmbedValidationError = null,
    bool EmbedAutoEnabledVsCode = false);

/// <summary>
/// Pure command-line parser for the devtools entry point. Has no side effects so it is
/// unit-testable without spinning up a process. The runtime surfaces deprecation warnings
/// based on <see cref="DevtoolsCliOptions.UsedDeprecatedPreview"/>.
/// </summary>
internal static class DevtoolsCliParser
{
    public static DevtoolsCliOptions Parse(string[] args)
    {
        int devtoolsIdx = IndexOf(args, "--devtools");
        int previewIdx = IndexOf(args, "--preview");
        int previewListIdx = IndexOf(args, "--preview-list");
        int devtoolsListIdx = IndexOf(args, "--devtools-list");

        bool anyDevtools = devtoolsIdx >= 0 || devtoolsListIdx >= 0;
        bool anyPreview = previewIdx >= 0 || previewListIdx >= 0;

        if (anyDevtools && anyPreview)
        {
            return new DevtoolsCliOptions(
                Subverb: null,
                ComponentName: null,
                VsCodeMode: false,
                Fps: 10,
                ListOutputPath: null,
                ScreenshotOutputPath: null,
                McpPort: null,
                LogLevel: null,
                Transport: McpTransport.Http,
                UsedDeprecatedPreview: true,
                PreviewAndDevtoolsConflict: true,
                LogsDisabled: false,
                LogsCapacityMb: null,
                EmbedRequested: false,
                EmbedStyle: ParseEmbedStyle(args),
                EmbedHostPid: ParseEmbedHostPid(args),
                EmbedValidationError: null,
                EmbedAutoEnabledVsCode: false);
        }

        if (!anyDevtools && !anyPreview)
        {
            bool embedOnlyRequested = args.Contains("--embed");
            return new DevtoolsCliOptions(
                Subverb: null,
                ComponentName: null,
                VsCodeMode: embedOnlyRequested,
                Fps: 10,
                ListOutputPath: null,
                ScreenshotOutputPath: null,
                McpPort: null,
                LogLevel: null,
                Transport: McpTransport.Http,
                UsedDeprecatedPreview: false,
                PreviewAndDevtoolsConflict: false,
                LogsDisabled: false,
                LogsCapacityMb: null,
                EmbedRequested: embedOnlyRequested,
                EmbedStyle: ParseEmbedStyle(args),
                EmbedHostPid: ParseEmbedHostPid(args),
                EmbedValidationError: embedOnlyRequested ? "--embed requires '--devtools run'" : null,
                EmbedAutoEnabledVsCode: embedOnlyRequested && !args.Contains("--vscode"));
        }

        DevtoolsSubverb subverb;
        int anchorIdx;
        bool deprecated;
        int trailingArgStart;

        if (devtoolsListIdx >= 0)
        {
            subverb = DevtoolsSubverb.List;
            anchorIdx = devtoolsListIdx;
            deprecated = false;
            trailingArgStart = devtoolsListIdx + 1;
        }
        else if (devtoolsIdx >= 0)
        {
            anchorIdx = devtoolsIdx;
            deprecated = false;
            (subverb, trailingArgStart) = ParseSubverbAfter(args, devtoolsIdx);
        }
        else if (previewListIdx >= 0)
        {
            subverb = DevtoolsSubverb.List;
            anchorIdx = previewListIdx;
            deprecated = true;
            trailingArgStart = previewListIdx + 1;
        }
        else
        {
            subverb = DevtoolsSubverb.Run;
            anchorIdx = previewIdx;
            deprecated = true;
            trailingArgStart = previewIdx + 1;
        }

        string? componentName = null;
        string? listOut = null;
        string? screenshotOut = null;

        if (subverb is DevtoolsSubverb.Run or DevtoolsSubverb.Screenshot or DevtoolsSubverb.Tree)
        {
            if (trailingArgStart < args.Length && !args[trailingArgStart].StartsWith("-"))
                componentName = args[trailingArgStart];
        }
        else if (subverb == DevtoolsSubverb.List)
        {
            if (trailingArgStart < args.Length && !args[trailingArgStart].StartsWith("-"))
                listOut = args[trailingArgStart];
        }

        bool vscode = args.Contains("--vscode");
        bool embedRequested = args.Contains("--embed");
        var embedStyle = ParseEmbedStyle(args);
        int? embedHostPid = ParseEmbedHostPid(args);
        bool embedAutoEnabledVsCode = embedRequested && !vscode;
        if (embedAutoEnabledVsCode)
            vscode = true;

        int fps = 10;
        int fpsIdx = IndexOf(args, "--fps");
        if (fpsIdx >= 0 && fpsIdx + 1 < args.Length && int.TryParse(args[fpsIdx + 1], out var parsedFps))
            fps = Math.Clamp(parsedFps, 1, 30);

        int? mcpPort = null;
        int mcpPortIdx = IndexOf(args, "--mcp-port");
        if (mcpPortIdx >= 0 && mcpPortIdx + 1 < args.Length && int.TryParse(args[mcpPortIdx + 1], out var parsedPort))
            mcpPort = parsedPort;

        string? logLevel = null;
        int logLevelIdx = IndexOf(args, "--devtools-log-level");
        if (logLevelIdx >= 0 && logLevelIdx + 1 < args.Length)
            logLevel = args[logLevelIdx + 1];

        var transport = McpTransport.Http;
        int transportIdx = IndexOf(args, "--mcp-transport");
        if (transportIdx >= 0 && transportIdx + 1 < args.Length)
        {
            if (string.Equals(args[transportIdx + 1], "stdio", StringComparison.OrdinalIgnoreCase))
                transport = McpTransport.Stdio;
        }

        int outIdx = IndexOf(args, "--out");
        if (outIdx >= 0 && outIdx + 1 < args.Length && subverb == DevtoolsSubverb.Screenshot)
            screenshotOut = args[outIdx + 1];

        string? projectIdentifier = null;
        int projIdx = IndexOf(args, "--devtools-project");
        if (projIdx >= 0 && projIdx + 1 < args.Length)
            projectIdentifier = args[projIdx + 1];

        bool logsDisabled = false;
        int logsIdx = IndexOf(args, "--devtools-logs");
        if (logsIdx >= 0 && logsIdx + 1 < args.Length
            && string.Equals(args[logsIdx + 1], "off", StringComparison.OrdinalIgnoreCase))
            logsDisabled = true;

        int? logsCapMb = null;
        int logsCapIdx = IndexOf(args, "--devtools-logs-capacity");
        if (logsCapIdx >= 0 && logsCapIdx + 1 < args.Length
            && int.TryParse(args[logsCapIdx + 1], out var parsedCap) && parsedCap > 0)
            logsCapMb = parsedCap;

        _ = anchorIdx;

        string? embedValidationError = null;
        if (embedRequested)
        {
            if (deprecated || subverb != DevtoolsSubverb.Run || !IsExplicitDevtoolsRun(args, devtoolsIdx))
                embedValidationError = "--embed requires '--devtools run'";
            else if (IndexOf(args, "--embed-host-pid") is var pidIdx && (pidIdx < 0 || pidIdx + 1 >= args.Length))
                embedValidationError = "--embed requires --embed-host-pid <pid>";
            else if (embedHostPid is null)
                embedValidationError = $"--embed-host-pid value '{args[IndexOf(args, "--embed-host-pid") + 1]}' is not a valid process id";
        }

        return new DevtoolsCliOptions(
            Subverb: embedValidationError is null ? subverb : null,
            ComponentName: componentName,
            VsCodeMode: vscode,
            Fps: fps,
            ListOutputPath: listOut,
            ScreenshotOutputPath: screenshotOut,
            McpPort: mcpPort,
            LogLevel: logLevel,
            Transport: transport,
            UsedDeprecatedPreview: deprecated,
            PreviewAndDevtoolsConflict: false,
            ProjectIdentifier: projectIdentifier,
            LogsDisabled: logsDisabled,
            LogsCapacityMb: logsCapMb,
            EmbedRequested: embedRequested,
            EmbedStyle: embedStyle,
            EmbedHostPid: embedHostPid,
            EmbedValidationError: embedValidationError,
            EmbedAutoEnabledVsCode: embedAutoEnabledVsCode);
    }

    private static (DevtoolsSubverb Subverb, int TrailingArgStart) ParseSubverbAfter(string[] args, int devtoolsIdx)
    {
        // `--devtools <verb> [...positional]`. If no verb is given, default to Run.
        if (devtoolsIdx + 1 >= args.Length) return (DevtoolsSubverb.Run, devtoolsIdx + 1);
        var next = args[devtoolsIdx + 1];
        return next.ToLowerInvariant() switch
        {
            "run" => (DevtoolsSubverb.Run, devtoolsIdx + 2),
            "list" => (DevtoolsSubverb.List, devtoolsIdx + 2),
            "screenshot" => (DevtoolsSubverb.Screenshot, devtoolsIdx + 2),
            "tree" => (DevtoolsSubverb.Tree, devtoolsIdx + 2),
            "app" => (DevtoolsSubverb.App, devtoolsIdx + 2),
            _ => (DevtoolsSubverb.Run, devtoolsIdx + 1),
        };
    }

    private static bool IsExplicitDevtoolsRun(string[] args, int devtoolsIdx)
    {
        return devtoolsIdx >= 0
            && devtoolsIdx + 1 < args.Length
            && string.Equals(args[devtoolsIdx + 1], "run", StringComparison.OrdinalIgnoreCase);
    }

    private static WindowEmbedStyle ParseEmbedStyle(string[] args)
    {
        int modeIdx = IndexOf(args, "--embed-mode");
        if (modeIdx >= 0 && modeIdx + 1 < args.Length)
        {
            if (string.Equals(args[modeIdx + 1], "owner", StringComparison.OrdinalIgnoreCase))
                return WindowEmbedStyle.Owner;
            if (string.Equals(args[modeIdx + 1], "child", StringComparison.OrdinalIgnoreCase))
                return WindowEmbedStyle.Child;
        }

        // Lenient default: unknown future/typo values fall back to the safer child mode.
        return WindowEmbedStyle.Child;
    }

    private static int? ParseEmbedHostPid(string[] args)
    {
        int pidIdx = IndexOf(args, "--embed-host-pid");
        if (pidIdx >= 0 && pidIdx + 1 < args.Length && int.TryParse(args[pidIdx + 1], out var pid))
            return pid;
        return null;
    }

    private static int IndexOf(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.Ordinal))
                return i;
        return -1;
    }
}
