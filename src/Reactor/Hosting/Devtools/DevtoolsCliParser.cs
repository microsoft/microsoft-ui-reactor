using System.Globalization;
using Microsoft.UI.Reactor;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// What the in-app preview host was asked to do: <see cref="Run"/> the preview (the
/// VS Code live preview and the Visual Studio embedded preview launch this), or
/// <see cref="List"/> the app's components.
/// </summary>
public enum DevtoolsSubverb
{
    Run,
    List,
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
    bool UsedDeprecatedPreview,
    bool PreviewAndDevtoolsConflict,
    bool EmbedRequested = false,
    WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child,
    int? EmbedHostPid = null,
    string? EmbedValidationError = null,
    bool EmbedAutoEnabledVsCode = false,
    double? WindowWidth = null,
    double? WindowHeight = null,
    double? WindowX = null,
    double? WindowY = null,
    string? RemovedSubverbError = null);

/// <summary>
/// Pure command-line parser for the devtools entry point. Has no side effects so it is
/// unit-testable without spinning up a process. <c>--devtools app|screenshot|tree</c> parse to
/// <see cref="DevtoolsCliOptions.RemovedSubverbError"/>, which the runtime prints before exiting.
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
                UsedDeprecatedPreview: true,
                PreviewAndDevtoolsConflict: true,
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
                UsedDeprecatedPreview: false,
                PreviewAndDevtoolsConflict: false,
                EmbedRequested: embedOnlyRequested,
                EmbedStyle: ParseEmbedStyle(args),
                EmbedHostPid: ParseEmbedHostPid(args),
                EmbedValidationError: embedOnlyRequested ? "--embed requires '--devtools run'" : null,
                EmbedAutoEnabledVsCode: embedOnlyRequested && !args.Contains("--vscode"));
        }

        if (devtoolsIdx >= 0 && RemovedSubverbMessage(args, devtoolsIdx) is { } removed)
        {
            return new DevtoolsCliOptions(
                Subverb: null,
                ComponentName: null,
                VsCodeMode: false,
                Fps: 10,
                ListOutputPath: null,
                UsedDeprecatedPreview: false,
                PreviewAndDevtoolsConflict: false,
                RemovedSubverbError: removed);
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

        if (subverb is DevtoolsSubverb.Run)
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

        _ = anchorIdx;

        // Window size override for a devtools-hosted window. The docs screenshot
        // harness passes these so a doc app's capture size is declared in its
        // doc-manifest.yaml rather than baked into its ReactorApp.Run call — the
        // app itself can then omit width/height and take the OS default when a
        // human runs it. Only positive finite values win; anything else leaves
        // the Run-supplied size in place.
        var windowWidth = ParseDimension(args, "--width");
        var windowHeight = ParseDimension(args, "--height");

        // Window origin override, in DIPs of the virtual desktop. The docs
        // harness uses this to place a capture window on a monitor whose scale
        // matches the repo's capture convention, when that is not the primary
        // display. Parsed separately from the size flags on purpose: a
        // coordinate may legitimately be zero or negative (a monitor left of the
        // primary has a negative origin), so the positive-only rule that guards
        // width/height would silently drop valid positions.
        var windowX = ParseCoordinate(args, "--x");
        var windowY = ParseCoordinate(args, "--y");

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
            UsedDeprecatedPreview: deprecated,
            PreviewAndDevtoolsConflict: false,
            EmbedRequested: embedRequested,
            EmbedStyle: embedStyle,
            EmbedHostPid: embedHostPid,
            EmbedValidationError: embedValidationError,
            EmbedAutoEnabledVsCode: embedAutoEnabledVsCode,
            WindowWidth: windowWidth,
            WindowHeight: windowHeight,
            WindowX: windowX,
            WindowY: windowY);
    }

    /// <summary>
    /// The largest DIP magnitude accepted for a window origin or extent. Chosen so the
    /// value survives the downstream conversion rather than to model any real display:
    /// <c>ReactorWindow.DipToPhysicalPoint</c> / <c>DipToPhysicalScalar</c> compute
    /// <c>dip * dpi / 96</c> and cast the result to <c>int</c>. A merely-finite input does
    /// not survive that — <c>1e308 * 144 / 96</c> overflows to infinity and the cast
    /// saturates to <c>int.MaxValue</c>, moving or resizing the window to the far edge of
    /// the coordinate space where capture fails. At 500% scaling this bound still converts
    /// to ±327,680 physical pixels, comfortably inside <c>int</c>, while being orders of
    /// magnitude larger than any real monitor arrangement or window.
    /// </summary>
    private const double MaxWindowCoordinateDip = 65_536;

    /// <summary>
    /// Reads a positive, finite DIP dimension following <paramref name="flag"/>.
    /// Returns <c>null</c> when absent or unusable, so the caller keeps whatever
    /// size the app itself declared. Parsed invariantly: this is a machine-facing
    /// switch, so a comma-decimal locale must not change how "600.5" is read.
    /// Oversized-but-finite values are rejected for the same reason coordinates are —
    /// they cannot survive the DIP→physical conversion.
    /// </summary>
    private static double? ParseDimension(string[] args, string flag)
    {
        int idx = IndexOf(args, flag);
        if (idx < 0 || idx + 1 >= args.Length) return null;
        if (!double.TryParse(args[idx + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        return double.IsFinite(value) && value > 0 && value <= MaxWindowCoordinateDip ? value : null;
    }

    /// <summary>
    /// Reads a finite DIP coordinate following <paramref name="flag"/>. Unlike
    /// <see cref="ParseDimension"/> this accepts zero and negative values, which
    /// are ordinary virtual-desktop coordinates for a monitor positioned at or
    /// left of / above the primary. Values that are non-finite, or finite but too
    /// large to convert to a physical pixel position, are rejected so the caller
    /// falls back to OS placement instead of flinging the window off-screen.
    /// </summary>
    private static double? ParseCoordinate(string[] args, string flag)
    {
        int idx = IndexOf(args, flag);
        if (idx < 0 || idx + 1 >= args.Length) return null;
        if (!double.TryParse(args[idx + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        if (!double.IsFinite(value) || Math.Abs(value) > MaxWindowCoordinateDip) return null;
        return value;
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
            _ => (DevtoolsSubverb.Run, devtoolsIdx + 1),
        };
    }

    /// <summary>
    /// The message for a <c>--devtools</c> verb that was removed with the MCP server and the
    /// in-app dev menu, or null. Without this, <c>--devtools app</c> would fall through to
    /// <c>run</c> with "app" read as a component name.
    /// </summary>
    // <snippet:removed-subverbs>
    internal static string? RemovedSubverbMessage(string[] args, int devtoolsIdx)
    {
        if (devtoolsIdx + 1 >= args.Length) return null;
        return args[devtoolsIdx + 1].ToLowerInvariant() switch
        {
            "app" => "'--devtools app' was removed with the in-app dev menu. Inspect the running app with the WinUI DevTools: winapp run . --devtools",
            "screenshot" => "'--devtools screenshot' was removed. Use: winapp ui screenshot -a <app>",
            "tree" => "'--devtools tree' was removed. Use: winapp devtools inspect",
            _ => null,
        };
    }
    // </snippet:removed-subverbs>

    /// <summary>The flags that went with the in-app MCP server and its log capture.</summary>
    internal static readonly string[] RemovedFlags =
    [
        "--mcp-port",
        "--mcp-transport",
        "--devtools-log-level",
        "--devtools-logs",
        "--devtools-logs-capacity",
        "--devtools-project",
    ];

    /// <summary>
    /// A one-line notice naming the removed MCP / log flags in <paramref name="args"/>, or null.
    /// The flags are ignored so old launch profiles keep previewing, but a caller that relied on
    /// them (an MCP client on stdio, say) needs to hear why nothing answers.
    /// </summary>
    internal static string? RemovedFlagsNotice(string[] args)
    {
        var found = RemovedFlags.Where(f => args.Contains(f, StringComparer.Ordinal)).ToArray();
        return found.Length == 0
            ? null
            : $"Ignoring {string.Join(", ", found)}: the in-app MCP server was removed. Inspect the app with the WinUI DevTools: winapp run . --devtools";
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
