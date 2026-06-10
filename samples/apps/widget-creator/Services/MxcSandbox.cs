using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>Outcome of a sandboxed run (full lifetime — returns when the widget exits).</summary>
public sealed record SandboxResult(int ExitCode, string Output)
{
    public string ExitCodeHex => $"0x{(uint)ExitCode:X8}";

    /// <summary>
    /// True when the widget exited abnormally. A cleanly-closed WinUI window
    /// exits 0; a Reactor cross-thread fast-fail is <c>0xC0000409</c>, an access
    /// violation <c>0xC0000005</c>, a CLR unhandled exception <c>0xE0434352</c> —
    /// all non-zero. We treat any non-zero exit as a crash to repair.
    /// </summary>
    public bool Crashed => ExitCode != 0;
}

/// <summary>
/// Runs a built widget inside an MXC sandbox via the native <c>wxc-exec</c>
/// binary. The policy demonstrates a "web-like" experience:
/// <list type="bullet">
///   <item>UI allowed — the widget shows a real WinUI window.</item>
///   <item>Outbound network allowed (the <c>internetClient</c> capability).</item>
///   <item>No local filesystem — the AppContainer is default-deny; MXC grants
///   read+execute to <b>only the app's own publish directory</b> (from
///   <c>filesystem.readonlyPaths</c>, via its DACL manager), so the user
///   profile, Documents, etc. stay unreachable. We never touch ACLs ourselves.</item>
/// </list>
/// </summary>
public sealed class MxcSandbox
{
    public const string SchemaVersion = "0.6.0-alpha";

    /// <summary>
    /// On this dev host the BaseContainer backend is gated by the OS build
    /// (<c>Experimental_CreateProcessInSandbox → E_NOTIMPL</c>). Setting
    /// <c>MXC_DISABLE_BASE_CONTAINER=1</c> makes wxc-exec's tier detector skip
    /// BaseContainer and use AppContainer + DACL, which grants the app dir and
    /// runs here. Harmless on hosts where BaseContainer works (it just uses the
    /// DACL tier instead). Opt out by pre-setting the variable yourself.
    /// </summary>
    const string DisableBaseContainerVar = "MXC_DISABLE_BASE_CONTAINER";

    /// <summary>Human-readable summary of the policy this sandbox applies.</summary>
    public static (string Label, string Value, string Note)[] PolicyRows =>
    [
        ("UI / display",     "Allowed",  "the widget renders a real window"),
        ("Outbound network", "Allowed",  "remote HTTP(S) reachable (internetClient)"),
        ("Local filesystem", "Blocked",  "only the app's own dir is granted; user files unreachable"),
        ("Clipboard",        "None",     "no clipboard read/write"),
        ("Input injection",  "None",     "cannot synthesize keyboard/mouse"),
    ];

    /// <summary>Resolve the wxc-exec binary path: env override, a local mxc
    /// checkout (for MXC developers), then the copy vendored in the app output.</summary>
    public static string WxcExecPath { get; } = ResolveWxcExec();

    static string McxRoot =>
        Environment.GetEnvironmentVariable("WIDGET_CREATOR_MXC_ROOT") is { Length: > 0 } r
            ? r
            : @"C:\Users\andersonch\Code\mxc";

    static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    static string Triple => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "aarch64-pc-windows-msvc"
        : "x86_64-pc-windows-msvc";

    /// <summary>RID-style folder for the vendored binaries (matches the csproj layout).</summary>
    static string BundleRid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    /// <summary>The wxc-exec copy shipped next to the app (output dir: mxc/&lt;rid&gt;/).</summary>
    static string BundledWxcExec => Path.Combine(AppContext.BaseDirectory, "mxc", BundleRid, "wxc-exec.exe");

    static string ResolveWxcExec()
    {
        var direct = Environment.GetEnvironmentVariable("WIDGET_CREATOR_WXC_EXEC");
        if (!string.IsNullOrWhiteSpace(direct) && File.Exists(direct))
            return direct;

        var candidates = new List<string>();
        var binBase = Environment.GetEnvironmentVariable("WIDGET_CREATOR_MXC_BIN");
        if (!string.IsNullOrWhiteSpace(binBase))
            candidates.Add(Path.Combine(binBase, Arch, "wxc-exec.exe"));

        // A present local mxc checkout (developers iterating on MXC itself) wins, so a
        // freshly built binary is used over the vendored copy. wxc-exec finds its helper
        // binaries (sandbox daemon/guest, proxy shim) as siblings, so each candidate dir
        // must contain the full set.
        candidates.Add(Path.Combine(McxRoot, "src", "target", Triple, "release", "wxc-exec.exe"));
        candidates.Add(Path.Combine(McxRoot, "sdk", "bin", Arch, "wxc-exec.exe"));

        // Vendored copy shipped in the app's own output dir — makes `git clone` +
        // `dotnet run` work sandboxed with no external mxc checkout.
        candidates.Add(BundledWxcExec);

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return BundledWxcExec;
    }

    public bool IsAvailable => File.Exists(WxcExecPath);

    /// <summary>Build the wxc-exec ContainerConfig JSON for a widget exe.</summary>
    public static string BuildConfigJson(string exePath, string appDir, string? extraArgs = null, int timeoutSeconds = 0)
    {
        var commandLine = Quote(exePath);
        if (!string.IsNullOrWhiteSpace(extraArgs))
            commandLine += " " + extraArgs;

        var config = new JsonObject
        {
            ["version"] = SchemaVersion,
            ["containment"] = "processcontainer",
            ["process"] = new JsonObject
            {
                ["commandLine"] = commandLine,
                ["cwd"] = appDir,
                ["timeout"] = timeoutSeconds <= 0 ? 0 : timeoutSeconds * 1000, // ms; 0 = run until the window closes
            },
            // MXC grants read+execute to exactly this directory (the app's own
            // run dir) — nothing else under the user profile is reachable.
            ["filesystem"] = new JsonObject
            {
                ["readonlyPaths"] = new JsonArray(appDir),
            },
            ["appContainer"] = new JsonObject
            {
                ["leastPrivilege"] = false,
                ["capabilities"] = new JsonArray("internetClient"),
            },
            ["network"] = new JsonObject
            {
                ["defaultPolicy"] = "allow",
                ["enforcementMode"] = "capabilities",
            },
            ["ui"] = new JsonObject
            {
                ["disable"] = false,
                ["clipboard"] = "none",
                ["injection"] = false,
            },
        };
        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static string Quote(string p) => p.Contains(' ') ? $"\"{p}\"" : p;

    /// <summary>
    /// Launch <paramref name="exePath"/> in the sandbox. Blocks until the
    /// sandboxed process exits (the widget window closes), streaming wxc-exec
    /// output through <paramref name="onLine"/>. Run on a background task.
    /// Pass <paramref name="extraArgs"/> (e.g. <c>--selftest</c>) and a positive
    /// <paramref name="timeoutSeconds"/> for headless, self-terminating runs; if
    /// <paramref name="ct"/> fires first the sandboxed process tree is killed.
    /// </summary>
    public async Task<SandboxResult> RunAsync(
        string exePath, string appDir, Action<string>? onLine, CancellationToken ct,
        string? extraArgs = null, int timeoutSeconds = 0)
    {
        if (!IsAvailable)
        {
            var msg = $"wxc-exec not found at '{WxcExecPath}'. Set WIDGET_CREATOR_WXC_EXEC or WIDGET_CREATOR_MXC_BIN.";
            SessionLog.Write($"[Sandbox] {msg}");
            return new SandboxResult(-1, msg);
        }

        var configJson = BuildConfigJson(exePath, appDir, extraArgs, timeoutSeconds);
        var configPath = Path.Combine(Path.GetTempPath(), "widget-creator", $"mxc-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, configJson, ct).ConfigureAwait(false);

        SessionLog.Write($"[Sandbox] wxc-exec '{WxcExecPath}' config='{configPath}'");
        SessionLog.Write($"[Sandbox] config: {configJson}");
        onLine?.Invoke($"$ wxc-exec {Path.GetFileName(configPath)}");
        onLine?.Invoke(configJson);

        var psi = new ProcessStartInfo
        {
            FileName = WxcExecPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(configPath);

        // Force the AppContainer + DACL tier unless the caller pinned the var.
        if (Environment.GetEnvironmentVariable(DisableBaseContainerVar) is null)
            psi.Environment[DisableBaseContainerVar] = "1";

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();
        void Sink(string? s)
        {
            if (s is null) return;
            lock (sb) sb.AppendLine(s);
            onLine?.Invoke(s);
        }
        proc.OutputDataReceived += (_, e) => Sink(e.Data);
        proc.ErrorDataReceived += (_, e) => Sink(e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            SessionLog.Write("[Sandbox] wxc-exec cancelled/timed out — killed process tree");
            Sink("WIDGET_SANDBOX_TIMEOUT");
            try { File.Delete(configPath); } catch { /* best-effort cleanup */ }
            return new SandboxResult(unchecked((int)0xC000013A), sb.ToString());
        }

        SessionLog.Write($"[Sandbox] wxc-exec exited code={proc.ExitCode}");
        try { File.Delete(configPath); } catch { /* best-effort cleanup */ }
        return new SandboxResult(proc.ExitCode, sb.ToString());
    }
}
