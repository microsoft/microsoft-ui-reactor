using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// <summary>Marker the host emits when its out-of-band containment check (C-2)
    /// kills a widget it found running uncontained.</summary>
    public const string ContainmentFailedMarker = "WIDGET_CONTAINMENT_FAILED";

    public string ExitCodeHex => $"0x{(uint)ExitCode:X8}";

    /// <summary>
    /// True once the widget process actually started its Reactor window — i.e. the
    /// sandbox launched it. Used to tell a real widget crash apart from a failure
    /// to even launch (a sandbox/policy/host problem).
    /// </summary>
    public bool WidgetStarted =>
        Output.Contains("MountAndActivate ok", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("OpenWindowCore", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the host's own out-of-band containment check (C-2) positively
    /// determined the widget was running <b>uncontained</b> and terminated it. This
    /// is a sandbox/host fail-open, not a widget bug — even if the widget had
    /// already printed its window markers — so it is a launch failure and must
    /// never reach the crash-repair loop.
    /// </summary>
    public bool ContainmentFailed =>
        Output.Contains(ContainmentFailedMarker, StringComparison.Ordinal);

    /// <summary>
    /// True when <c>wxc-exec</c> itself failed to set up the sandbox and never ran
    /// the widget — e.g. the host's BaseContainer tier is gated and the DACL
    /// fallback can't grant the requested path (no <c>WRITE_DAC</c>), or a
    /// capability is unimplemented — or when the host's containment check found
    /// the widget uncontained. These are permission/host problems, NOT widget
    /// bugs, so they must not trigger the Copilot crash-repair loop.
    /// </summary>
    public bool LaunchFailed =>
        ContainmentFailed || (ExitCode != 0 && !WidgetStarted && HasSandboxError);

    /// <summary>First <c>wxc-exec</c> <c>error:</c> line, if any, for user-facing messaging.</summary>
    public string? LaunchErrorMessage =>
        Output.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            ?.Substring("error:".Length).Trim();

    bool HasSandboxError =>
        Output.Contains("BaseContainer is unavailable", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("DACL fallback", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("WRITE_DAC", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("E_NOTIMPL", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("Experimental_CreateProcessInSandbox", StringComparison.OrdinalIgnoreCase)
        || Output.Split('\n').Any(l => l.TrimStart().StartsWith("error:", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the widget started and then exited abnormally. A cleanly-closed
    /// WinUI window exits 0; a Reactor cross-thread fast-fail is <c>0xC0000409</c>,
    /// an access violation <c>0xC0000005</c>, a CLR unhandled exception
    /// <c>0xE0434352</c> — all non-zero. A non-zero exit that is actually a
    /// <see cref="LaunchFailed"/> sandbox error is excluded (not a widget bug).
    /// </summary>
    public bool Crashed => ExitCode != 0 && !LaunchFailed;
}

/// <summary>
/// Runs a built widget inside an MXC sandbox via the native <c>wxc-exec</c>
/// binary. The default policy is <b>least privilege</b>:
/// <list type="bullet">
///   <item>UI allowed — the widget shows a real WinUI window.</item>
///   <item>No outbound network by default — the <c>internetClient</c> capability is
///   opt-in per widget via the Permissions dialog (H-2).</item>
///   <item>No local filesystem — the AppContainer is default-deny; MXC grants
///   read+execute to <b>only the app's own publish directory</b> (from
///   <c>filesystem.readonlyPaths</c>, via its DACL manager), so the user
///   profile, Documents, etc. stay unreachable. We never touch ACLs ourselves.</item>
/// </list>
/// </summary>
public sealed class MxcSandbox
{
    public const string SchemaVersion = "0.6.0-alpha";

    /// <summary>Human-readable summary of the default policy this sandbox applies.</summary>
    public static (string Label, string Value, string Note)[] PolicyRows =>
    [
        ("UI / display",     "Allowed",  "the widget renders a real window"),
        ("Outbound network", "Blocked",  "off by default — grant per-widget in Permissions"),
        ("Local filesystem", "Blocked",  "only the app's own dir is granted; user files unreachable"),
        ("Clipboard",        "None",     "no clipboard read/write"),
        ("Input injection",  "None",     "cannot synthesize keyboard/mouse"),
    ];

    /// <summary>Resolve the wxc-exec binary path: an explicit env override, the
    /// pinned copy vendored in the app output (the default), and — only when
    /// explicitly opted in via <see cref="UseLocalMxcVar"/> — a local mxc checkout
    /// (for developers iterating on the MXC CLI itself). The vendored binary wins
    /// over an uncontrolled local build by default so a stale or tampered checkout
    /// cannot silently replace the sandbox we ship (C-2/C-3).</summary>
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

    /// <summary>
    /// Opt-in env var (set to <c>1</c>/<c>true</c>): prefer a local mxc checkout
    /// over the vendored binary, for developers iterating on the MXC CLI itself.
    /// Off by default — an uncontrolled or stale local build must NOT silently
    /// override the pinned sandbox we ship, both to avoid sandbox substitution
    /// (C-3) and because older builds may not auto-fall back off BaseContainer (C-2).
    /// </summary>
    const string UseLocalMxcVar = "WIDGET_CREATOR_USE_LOCAL_MXC";

    static bool UseLocalCheckout =>
        Environment.GetEnvironmentVariable(UseLocalMxcVar) is { Length: > 0 } v &&
        (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    static string ResolveWxcExec()
    {
        // 1. A fully-specified explicit override always wins (dev/test/CI).
        var direct = Environment.GetEnvironmentVariable("WIDGET_CREATOR_WXC_EXEC");
        if (!string.IsNullOrWhiteSpace(direct) && File.Exists(direct))
            return direct;

        var candidates = new List<string>();

        // 2. An explicit bin dir override (caller-controlled, so it is trusted).
        var binBase = Environment.GetEnvironmentVariable("WIDGET_CREATOR_MXC_BIN");
        if (!string.IsNullOrWhiteSpace(binBase))
            candidates.Add(Path.Combine(binBase, Arch, "wxc-exec.exe"));

        // 3. Only when explicitly opted in, prefer a local mxc checkout so a
        // freshly built binary is used over the vendored copy. Off by default:
        // the pinned vendored binary is the trusted sandbox, and a stale local
        // build here previously broke launches by preferring BaseContainer with
        // no fallback (C-2). wxc-exec finds its helper binaries (sandbox
        // daemon/guest, proxy shim) as siblings, so each dir must hold the full set.
        if (UseLocalCheckout)
        {
            candidates.Add(Path.Combine(McxRoot, "src", "target", Triple, "release", "wxc-exec.exe"));
            candidates.Add(Path.Combine(McxRoot, "sdk", "bin", Arch, "wxc-exec.exe"));
        }

        // 4. Pinned, vendored copy shipped in the app's own output dir (the
        // default) — makes `git clone` + `dotnet run` work sandboxed with no
        // external mxc checkout, and is known to auto-fall back off BaseContainer.
        candidates.Add(BundledWxcExec);

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return BundledWxcExec;
    }

    public bool IsAvailable => File.Exists(WxcExecPath);

    /// <summary>Whether the resolved wxc-exec is the pinned, vendored copy (vs a dev override).</summary>
    static bool UsingBundled =>
        string.Equals(WxcExecPath, BundledWxcExec, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// C-3 integrity result: <c>null</c> when the pinned vendored binary set verifies
    /// (or when a deliberate dev override is in effect), otherwise the failure reason.
    /// Only the vendored copy is pinned; explicit <c>WIDGET_CREATOR_WXC_EXEC</c> /
    /// <c>WIDGET_CREATOR_MXC_BIN</c> / <c>WIDGET_CREATOR_USE_LOCAL_MXC</c> overrides are a
    /// conscious developer choice and bypass the pin (logged). Computed once.
    /// </summary>
    public static string? IntegrityError => _integrity.Value;

    static readonly Lazy<string?> _integrity = new(ComputeIntegrityError);

    static string? ComputeIntegrityError()
    {
        if (!UsingBundled)
        {
            SessionLog.Write($"[Sandbox] integrity pin bypassed — non-vendored wxc-exec '{WxcExecPath}'");
            return null;
        }
        var dir = Path.GetDirectoryName(BundledWxcExec)!;
        var error = MxcBinaryManifest.Verify(BundleRid, dir);
        SessionLog.Write(error is null
            ? "[Sandbox] vendored MXC binaries verified against integrity pins."
            : $"[Sandbox] INTEGRITY FAILURE: {error}");
        return error;
    }

    /// <summary>
    /// Build the wxc-exec ContainerConfig JSON for a widget exe. Starts from a
    /// per-widget permission policy template (<paramref name="policyTemplateJson"/>;
    /// the default UI+internet policy when null/blank/invalid), then merges the
    /// run-specific fields: the process command line/cwd/timeout, and a guaranteed
    /// read grant on the app's own directory so the widget can always launch
    /// regardless of the chosen policy.
    /// </summary>
    public static string BuildConfigJson(
        string exePath, string appDir, string? extraArgs = null, int timeoutSeconds = 0,
        string? policyTemplateJson = null)
    {
        var commandLine = Quote(exePath);
        if (!string.IsNullOrWhiteSpace(extraArgs))
            commandLine += " " + extraArgs;

        var config = MxcPolicy.TryParse(policyTemplateJson, out var parsed, out _)
            ? parsed
            : MxcPolicy.DefaultTemplate();

        // Required base fields (fill only if the policy omitted them).
        config["version"] ??= SchemaVersion;
        config["containment"] ??= "processcontainer";

        // Process is always run-controlled — overwrite whatever the policy had.
        config["process"] = new JsonObject
        {
            ["commandLine"] = commandLine,
            ["cwd"] = appDir,
            ["timeout"] = timeoutSeconds <= 0 ? 0 : timeoutSeconds * 1000, // ms; 0 = run until the window closes
        };

        // Always grant read+execute on the app's own run dir, even under a custom
        // policy — otherwise the widget cannot be launched.
        if (config["filesystem"] is not JsonObject fs)
        {
            fs = new JsonObject();
            config["filesystem"] = fs;
        }
        if (fs["readonlyPaths"] is not JsonArray readonlyPaths)
        {
            readonlyPaths = new JsonArray();
            fs["readonlyPaths"] = readonlyPaths;
        }
        if (!readonlyPaths.Any(n => string.Equals((string?)n, appDir, StringComparison.OrdinalIgnoreCase)))
            readonlyPaths.Add(JsonValue.Create(appDir));

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static string Quote(string p) => p.Contains(' ') ? $"\"{p}\"" : p;

    const string RequireBaseContainerNote =
        "Strict containment is enabled (WIDGET_CREATOR_REQUIRE_BASE_CONTAINER):";

    static string Tail(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[^max..];

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
        string? extraArgs = null, int timeoutSeconds = 0, string? policyTemplateJson = null)
    {
        if (!IsAvailable)
        {
            var msg = $"wxc-exec not found at '{WxcExecPath}'. Set WIDGET_CREATOR_WXC_EXEC or WIDGET_CREATOR_MXC_BIN.";
            SessionLog.Write($"[Sandbox] {msg}");
            return new SandboxResult(-1, msg);
        }

        // C-3: refuse to run untrusted code if the pinned sandbox binaries don't
        // verify — an unverified wxc-exec could be a no-op that runs the widget
        // uncontained. The leading `error:` marks this as a launch/host problem
        // (LaunchFailed) so it never triggers the crash-repair loop.
        if (IntegrityError is { } integ)
        {
            var msg = $"error: MXC sandbox integrity check failed — {integ}. Refusing to run "
                + $"'{Path.GetFileName(exePath)}' because it could not be guaranteed to be sandboxed.";
            SessionLog.Write($"[Sandbox] {msg}");
            return new SandboxResult(-1, msg);
        }

        // C-2 (fail closed, opt-in): when the deployment requires the strong
        // BaseContainer tier, probe out-of-band and refuse to launch untrusted
        // code if that tier can't be confirmed, instead of silently running under
        // the weaker AppContainer+DACL fallback.
        if (MxcContainmentVerifier.StrictBaseContainer)
        {
            var probe = await MxcContainmentVerifier.ProbeTierAsync(WxcExecPath, ct).ConfigureAwait(false);
            if (!probe.StrongTierConfirmed)
            {
                var msg = $"error: {RequireBaseContainerNote} BaseContainer could not be confirmed on this host "
                    + $"(probe: {Tail(probe.Raw, 200)}). Refusing to run '{Path.GetFileName(exePath)}' uncontained-by-policy.";
                SessionLog.Write($"[Sandbox] {msg}");
                return new SandboxResult(-1, msg);
            }
        }

        var configJson = BuildConfigJson(exePath, appDir, extraArgs, timeoutSeconds, policyTemplateJson);
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

        // Let wxc-exec select the strongest available containment tier: it tries
        // BaseContainer first and falls back to AppContainer + DACL on hosts where
        // BaseContainer is gated. We no longer force the weaker tier — C-2. To pin
        // the DACL tier for debugging, set MXC_DISABLE_BASE_CONTAINER=1 in the
        // environment yourself; ProcessStartInfo inherits it into the child.

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

        // C-2 (out-of-band verification): independently confirm the widget really
        // is contained instead of trusting wxc-exec stdout. Locate the sandboxed
        // widget process from the host and inspect its token; if it is positively
        // running uncontained, kill it and mark the run as a containment failure
        // (a launch/host problem, never a widget crash → no repair loop).
        using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var widgetExeName = Path.GetFileName(exePath);
        var rootPid = proc.Id;
        var verifyTask = Task.Run(() =>
        {
            try
            {
                var tok = MxcContainmentVerifier.VerifyWidgetProcess(
                    rootPid, widgetExeName, TimeSpan.FromSeconds(8), verifyCts.Token);
                if (tok.ConfirmedUncontained)
                {
                    SessionLog.Write($"[Sandbox] CONTAINMENT FAILURE — widget uncontained ({tok.Detail}); killing process tree.");
                    Sink(SandboxResult.ContainmentFailedMarker);
                    Sink($"error: MXC containment verification failed — the widget was running uncontained "
                        + $"({tok.Detail}) and was terminated. This is a sandbox/host problem, not a widget bug.");
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                }
                else if (tok.ConfirmedContained)
                    SessionLog.Write($"[Sandbox] containment verified out-of-band — {tok.Detail}");
                else
                    SessionLog.Write($"[Sandbox] containment not verified (best-effort) — {tok.Detail}");
            }
            catch (Exception ex)
            {
                SessionLog.Write($"[Sandbox] containment verification error: {ex.Message}");
            }
        }, verifyCts.Token);

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            SessionLog.Write("[Sandbox] wxc-exec cancelled/timed out — killed process tree");
            Sink("WIDGET_SANDBOX_TIMEOUT");
            verifyCts.Cancel();
            try { await verifyTask.ConfigureAwait(false); } catch { /* best-effort */ }
            try { File.Delete(configPath); } catch { /* best-effort cleanup */ }
            return new SandboxResult(unchecked((int)0xC000013A), sb.ToString());
        }

        // Ensure any containment-failure marker/error line is flushed before we
        // classify the result.
        verifyCts.Cancel();
        try { await verifyTask.ConfigureAwait(false); } catch { /* best-effort */ }

        SessionLog.Write($"[Sandbox] wxc-exec exited code={proc.ExitCode}");
        try { File.Delete(configPath); } catch { /* best-effort cleanup */ }
        return new SandboxResult(proc.ExitCode, sb.ToString());
    }
}
