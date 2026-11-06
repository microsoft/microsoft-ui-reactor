using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Mxc.Sdk.V1;

namespace WidgetCreator.Services;

/// <summary>Outcome of a sandboxed run (full lifetime — returns when the widget exits).</summary>
/// <param name="ExitCode">The contained process's exit code (-1 when it never launched).</param>
/// <param name="Output">Everything streamed to the log: the widget's stdout/stderr plus host notes.</param>
/// <param name="LaunchError">Set when the sandbox could not be set up (or the host's
/// containment check failed) — a permissions/host problem, never a widget bug.</param>
/// <param name="ContainmentFailed">True when the host's own out-of-band containment check
/// (C-2) positively determined the widget was running <b>uncontained</b> and killed it.</param>
public sealed record SandboxResult(int ExitCode, string Output, string? LaunchError = null, bool ContainmentFailed = false)
{
    public string ExitCodeHex => $"0x{(uint)ExitCode:X8}";

    /// <summary>
    /// True when MXC itself refused or failed to set up the sandbox (an
    /// <see cref="MxcException"/> — e.g. a filesystem grant the host can't apply,
    /// an unsupported policy, or an unavailable backend) or when the host's
    /// containment check found the widget uncontained. These are permission/host
    /// problems, NOT widget bugs, so they must not trigger the Copilot
    /// crash-repair loop.
    /// </summary>
    public bool LaunchFailed => LaunchError is not null || ContainmentFailed;

    /// <summary>User-facing launch error, if any.</summary>
    public string? LaunchErrorMessage => LaunchError;

    /// <summary>
    /// True when the widget started and then exited abnormally. A cleanly-closed
    /// WinUI window exits 0; a Reactor cross-thread fast-fail is <c>0xC0000409</c>,
    /// an access violation <c>0xC0000005</c>, a CLR unhandled exception
    /// <c>0xE0434352</c> — all non-zero. A <see cref="LaunchFailed"/> sandbox error
    /// is excluded (not a widget bug).
    /// </summary>
    public bool Crashed => ExitCode != 0 && !LaunchFailed;
}

/// <summary>
/// Runs a built widget inside an MXC sandbox via the in-process MXC SDK
/// (<c>Microsoft.Mxc.Sdk</c>, Windows ProcessContainer backend). The default
/// policy is <b>least privilege</b>:
/// <list type="bullet">
///   <item>UI allowed — the widget shows a real WinUI window.</item>
///   <item>No network by default — egress and ingress are denied; internet
///   access is opt-in per widget via the Permissions dialog (H-2).</item>
///   <item>No local filesystem — the container is default-deny; MXC grants
///   read+execute to <b>only the app's own publish directory</b> (from
///   <c>filesystem.readonlyPaths</c>), so the user profile, Documents, etc. stay
///   unreachable. We never touch ACLs ourselves.</item>
/// </list>
/// MXC selects the strongest isolation tier the host supports (BaseContainer,
/// then AppContainer with BFS or DACL grants).
/// </summary>
public sealed class MxcSandbox
{
    /// <summary>Marker the host emits when a run hits its timeout or is cancelled.</summary>
    public const string TimeoutMarker = "WIDGET_SANDBOX_TIMEOUT";

    /// <summary>Human-readable summary of the default policy this sandbox applies.</summary>
    public static (string Label, string Value, string Note)[] PolicyRows =>
    [
        ("UI / display",     "Allowed",  "the widget renders a real window"),
        ("Outbound network", "Blocked",  "off by default — grant per-widget in Permissions"),
        ("Local filesystem", "Blocked",  "only the app's own dir is granted; user files unreachable"),
        ("Clipboard",        "None",     "no clipboard read/write"),
        ("Input injection",  "None",     "cannot synthesize keyboard/mouse"),
    ];

    static readonly Lazy<string?> _unavailable = new(ComputeUnavailableReason);

    /// <summary><c>null</c> when MXC can launch ProcessContainer sandboxes on this host;
    /// otherwise why it can't (computed once).</summary>
    public static string? UnavailableReason => _unavailable.Value;

    public bool IsAvailable => UnavailableReason is null;

    /// <summary>Short status for the title bar.</summary>
    public static string StatusText => UnavailableReason is null
        ? $"MXC sandbox (native {SafeNativeVersion()})"
        : "MXC sandbox unavailable";

    static string SafeNativeVersion()
    {
        try { return MxcPlatform.NativeVersion; }
        catch (Exception ex) when (IsNativeRuntimeFailure(ex)) { return "unknown"; }
    }

    /// <summary>Failures that mean the MXC native runtime (mxc_ffi) can't be loaded or queried.</summary>
    static bool IsNativeRuntimeFailure(Exception ex) =>
        ex is MxcException or DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or TypeInitializationException or FileLoadException;

    static string? ComputeUnavailableReason()
    {
        // C-3: bind the SDK to the verified, Microsoft-signed native runtime before
        // any MXC call can load mxc_ffi into this host.
        if (MxcNativeRuntime.EnsureVerified() is { } untrusted)
            return $"the MXC native runtime failed verification ({untrusted})";

        try
        {
            var support = MxcPlatform.GetPlatformSupport();
            SessionLog.Write($"[Sandbox] MXC native {MxcPlatform.NativeVersion}; supported={support.IsSupported} "
                + $"backends=[{string.Join(", ", support.AvailableMethods)}]"
                + (support.Reason is { Length: > 0 } r ? $" reason={r}" : ""));
            if (!support.IsSupported)
                return support.Reason ?? "MXC cannot launch sandboxes on this host.";
            if (!support.AvailableMethods.Contains(ContainmentBackend.ProcessContainer))
                return "the MXC ProcessContainer backend is not available on this host.";
            return null;
        }
        catch (Exception ex) when (IsNativeRuntimeFailure(ex))
        {
            SessionLog.Write($"[Sandbox] MXC native runtime failed to load: {ex}");
            return $"the MXC native runtime could not be loaded ({ex.Message}).";
        }
    }

    /// <summary>
    /// Build the MXC request for a widget exe. Starts from a per-widget permission
    /// policy (<paramref name="policyJson"/>; the least-privilege default when
    /// null/blank/invalid), then adds the run-specific fields: the command line,
    /// working directory, timeout, and a guaranteed read grant on the app's own
    /// directory so the widget can always launch regardless of the chosen policy.
    /// <c>PolicyWarning</c> is set when the stored policy could not be applied
    /// and the default policy replaced it — callers must surface it to the user.
    /// </summary>
    public static (ContainerRequest Request, string PolicyJson, string? PolicyWarning) BuildRequest(
        string exePath, string appDir, string? extraArgs = null, int timeoutSeconds = 0,
        string? policyJson = null)
    {
        var commandLine = Quote(exePath);
        if (!string.IsNullOrWhiteSpace(extraArgs))
            commandLine += " " + extraArgs;

        string? warning = null;
        if (!MxcPolicy.TryParse(policyJson, out var policy, out var error))
        {
            warning = $"this widget's saved permissions were NOT applied — {error} Running with the default policy below.";
            SessionLog.Write($"[Sandbox] stored policy is invalid ({error}); using the default policy.");
            policy = MxcPolicy.DefaultTemplate();
        }

        var timeoutMs = timeoutSeconds <= 0 ? 0u : (uint)timeoutSeconds * 1000; // 0 = run until the window closes
        return (MxcPolicy.ToContainerRequest(policy, commandLine, appDir, timeoutMs), MxcPolicy.Prettify(policy), warning);
    }

    static string Quote(string p) => p.Contains(' ') ? $"\"{p}\"" : p;

    const string RequireBaseContainerNote =
        "Strict containment is enabled (WIDGET_CREATOR_REQUIRE_BASE_CONTAINER):";

    static string Describe(MxcException ex)
    {
        var text = $"{ex.Message} [{ex.Code}]";
        if (ex.Remediation is { Length: > 0 } fix)
            text += $" — {fix}";
        return text;
    }

    /// <summary>
    /// Launch <paramref name="exePath"/> in the sandbox. Blocks until the
    /// sandboxed process exits (the widget window closes), streaming its output
    /// through <paramref name="onLine"/>. Run on a background task.
    /// Pass <paramref name="extraArgs"/> (e.g. <c>--selftest</c>) and a positive
    /// <paramref name="timeoutSeconds"/> for headless, self-terminating runs; if
    /// <paramref name="ct"/> fires first the sandboxed process tree is killed.
    /// </summary>
    public async Task<SandboxResult> RunAsync(
        string exePath, string appDir, Action<string>? onLine, CancellationToken ct,
        string? extraArgs = null, int timeoutSeconds = 0, string? policyTemplateJson = null)
    {
        var sb = new StringBuilder();
        void Sink(string? s)
        {
            if (s is null) return;
            lock (sb) sb.AppendLine(s);
            onLine?.Invoke(s);
        }
        string Captured() { lock (sb) return sb.ToString(); }

        // The leading `error:` keeps the log consistent; LaunchError marks this as a
        // launch/host problem so it never triggers the crash-repair loop.
        SandboxResult LaunchFailure(string message)
        {
            SessionLog.Write($"[Sandbox] {message}");
            Sink($"error: {message}");
            return new SandboxResult(-1, Captured(), LaunchError: message);
        }

        if (UnavailableReason is { } unavailable)
            return LaunchFailure($"MXC sandbox unavailable — {unavailable}");

        ContainerRequest request;
        string policyJson;
        string? policyWarning;
        try
        {
            (request, policyJson, policyWarning) = BuildRequest(exePath, appDir, extraArgs, timeoutSeconds, policyTemplateJson);
        }
        catch (ArgumentException ex)
        {
            // The SDK's request types validate their arguments (e.g. a blank CIDR).
            return LaunchFailure($"the sandbox policy is invalid: {ex.Message}");
        }
        SessionLog.Write($"[Sandbox] spawn '{request.Command}' cwd='{appDir}' timeoutMs={request.TimeoutMs?.ToString() ?? "none"}");
        SessionLog.Write($"[Sandbox] policy: {policyJson}");
        Sink($"$ mxc processcontainer {request.Command}");
        if (policyWarning is not null)
            Sink($"warning: {policyWarning}");
        Sink(policyJson);

        // C-2 (fail closed, opt-in): when the deployment requires the strong
        // BaseContainer tier, probe out-of-band and refuse to launch untrusted
        // code if that tier can't be confirmed for this request, instead of
        // silently running under the weaker AppContainer fallback.
        if (MxcContainmentVerifier.StrictBaseContainer)
        {
            var probe = await Task.Run(() => MxcContainmentVerifier.ProbeTier(request), ct).ConfigureAwait(false);
            if (!probe.StrongTierConfirmed)
                return LaunchFailure($"{RequireBaseContainerNote} BaseContainer could not be confirmed on this host "
                    + $"(probe: {probe.Raw}). Refusing to run '{Path.GetFileName(exePath)}' uncontained-by-policy.");
        }

        MxcProcess proc;
        try
        {
            proc = await MxcContainer.SpawnAsync(request, options: null, ct).ConfigureAwait(false);
        }
        catch (MxcException ex)
        {
            return LaunchFailure($"MXC could not start the sandbox: {Describe(ex)}");
        }
        catch (OperationCanceledException)
        {
            Sink(TimeoutMarker);
            return new SandboxResult(unchecked((int)0xC000013A), Captured());
        }

        using (proc)
        {
            // MXC appends to Warnings during the terminal wait/finalization too, so
            // log the spawn warnings now and any later ones once the wait completes.
            var warningsSeen = 0;
            void EmitNewWarnings()
            {
                var warnings = proc.Warnings;
                for (; warningsSeen < warnings.Count; warningsSeen++)
                {
                    SessionLog.Write($"[Sandbox] MXC warning: {warnings[warningsSeen]}");
                    Sink($"warning: {warnings[warningsSeen]}");
                }
            }
            EmitNewWarnings();

            var pumps = new OutputPumps(proc, Sink);

            // C-2 (out-of-band verification): independently confirm the widget
            // really is contained. Inspect the contained process's token from the
            // host; if it is positively running uncontained, kill it and mark the
            // run as a containment failure (a launch/host problem, never a widget
            // crash → no repair loop).
            using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var containmentFailed = false;
            string? containmentError = null;
            var widgetExeName = Path.GetFileName(exePath);
            var rootPid = (int)proc.Id;
            var verifyTask = Task.Run(() =>
            {
                try
                {
                    var tok = MxcContainmentVerifier.VerifyWidgetProcess(
                        rootPid, widgetExeName, TimeSpan.FromSeconds(8), verifyCts.Token);
                    if (tok.ConfirmedUncontained)
                    {
                        containmentFailed = true;
                        containmentError = $"MXC containment verification failed — the widget was running uncontained "
                            + $"({tok.Detail}) and was terminated. This is a sandbox/host problem, not a widget bug.";
                        SessionLog.Write($"[Sandbox] CONTAINMENT FAILURE — widget uncontained ({tok.Detail}); killing process tree.");
                        Sink($"error: {containmentError}");
                        pumps.MarkInterrupted();
                        TryKill(proc);
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

            WaitResult wait;
            try
            {
                wait = await proc.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                pumps.MarkInterrupted();
                TryKill(proc);
                SessionLog.Write("[Sandbox] run cancelled/timed out — killed process tree");
                verifyCts.Cancel();
                await pumps.DrainAsync().ConfigureAwait(false);
                EmitNewWarnings();
                Sink(TimeoutMarker);
                try { await verifyTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* verifyCts was cancelled before the check ran */ }
                return new SandboxResult(unchecked((int)0xC000013A), Captured());
            }

            // Make sure any containment-failure line is recorded before we classify.
            verifyCts.Cancel();
            try { await verifyTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* verifyCts was cancelled before the check ran */ }
            var streamError = await pumps.DrainAsync().ConfigureAwait(false);
            EmitNewWarnings();

            if (wait.TimedOut)
            {
                SessionLog.Write("[Sandbox] run hit its MXC timeout");
                Sink(TimeoutMarker);
            }

            SessionLog.Write($"[Sandbox] contained process exited code={wait.ExitCode} timedOut={wait.TimedOut}");
            if (containmentFailed)
                return new SandboxResult(wait.ExitCode, Captured(), LaunchError: containmentError, ContainmentFailed: true);

            // The widget's output was cut short by a host-side reader failure: the log
            // is incomplete, so report a sandbox error instead of judging the widget.
            if (streamError is not null)
            {
                var message = $"MXC failed while reading the widget's output ({streamError}); the log is incomplete.";
                SessionLog.Write($"[Sandbox] {message}");
                Sink($"error: {message}");
                return new SandboxResult(wait.ExitCode, Captured(), LaunchError: message);
            }

            return new SandboxResult(wait.ExitCode, Captured());
        }
    }

    static void TryKill(MxcProcess proc)
    {
        try { proc.Kill(); }
        catch (Exception ex) when (ex is MxcException or ObjectDisposedException) { /* already gone */ }
    }

    /// <summary>
    /// Streams the contained process's stdout/stderr to the log. Reader errors
    /// caused by teardown (pipe closed at exit, our closers, kill/timeout) are
    /// expected; any other reader failure is reported by <see cref="DrainAsync"/>.
    /// </summary>
    sealed class OutputPumps
    {
        readonly MxcProcess _proc;
        readonly Task<string?> _stdout;
        readonly Task<string?> _stderr;
        volatile bool _interrupted;

        public OutputPumps(MxcProcess proc, Action<string> sink)
        {
            _proc = proc;
            _stdout = PumpAsync(proc.StandardOutput, sink);
            _stderr = PumpAsync(proc.StandardError, sink);
        }

        /// <summary>The host is tearing the process down; reader errors from here on are expected.</summary>
        public void MarkInterrupted() => _interrupted = true;

        async Task<string?> PumpAsync(Stream? stream, Action<string> sink)
        {
            if (stream is null) return null;
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                    sink(line);
                return null;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                return null; // pipe closed (process gone) or reader interrupted
            }
            catch (MxcException ex) when (!_interrupted)
            {
                return Describe(ex);
            }
            catch (MxcException)
            {
                return null;
            }
        }

        /// <summary>
        /// Let the pumps finish after exit. A grandchild that inherited the pipes
        /// can keep them open, so stop waiting after a short grace period and
        /// interrupt the readers. Returns an unexpected reader failure, if any.
        /// </summary>
        public async Task<string?> DrainAsync()
        {
            var pumps = Task.WhenAll(_stdout, _stderr);
            if (await Task.WhenAny(pumps, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false) != pumps)
            {
                _interrupted = true;
                try { _proc.StandardOutputCloser?.Close(); }
                catch (Exception ex) when (ex is MxcException or IOException or ObjectDisposedException) { /* already closed */ }
                try { _proc.StandardErrorCloser?.Close(); }
                catch (Exception ex) when (ex is MxcException or IOException or ObjectDisposedException) { /* already closed */ }
                await Task.WhenAny(pumps, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }

            var errors = new List<string>();
            foreach (var pump in new[] { _stdout, _stderr })
            {
                if (pump.IsFaulted && !_interrupted)
                    errors.Add(pump.Exception!.GetBaseException().Message);
                else if (pump.IsCompletedSuccessfully && pump.Result is { } error)
                    errors.Add(error);
            }
            return errors.Count == 0 ? null : string.Join("; ", errors);
        }
    }
}
