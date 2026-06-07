#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio.Threading;
using ViewStatus = Microsoft.UI.Reactor.VsExtension.UI.EmbedStatus;

namespace Microsoft.UI.Reactor.VsExtension.Session
{
    internal sealed class EmbedSession : IDisposable
    {
        private const int MaxAckAttempts = 5;
        // dotnet watch's first build of a Reactor app (loads 6+ projects + analyzers)
        // commonly takes 30-90s on a cold disk cache. The handshake timer starts the
        // moment we spawn the child, so we need substantial headroom. Override via
        // env var Reactor_VsExtension_HandshakeTimeoutSeconds when iterating on
        // performance or testing slow machines.
        private static readonly TimeSpan DefaultFirstSessionTimeout = ResolveHandshakeTimeoutFromEnv();
        private static readonly TimeSpan DefaultHwndTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan AckRetryDelay = TimeSpan.FromMilliseconds(250);

        private static TimeSpan ResolveHandshakeTimeoutFromEnv()
        {
            var raw = Environment.GetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
            return TimeSpan.FromSeconds(180);
        }

        private readonly ReactorEmbedControlViewModel _vm;
        private readonly IntPtr _placeholderHwnd;
        private readonly Func<Rect> _getPlaceholderRect;
        private readonly JoinableTaskFactory _jtf;
        private readonly Func<string, DotnetResolver.Result?> _dotnetResolver;
        private readonly Func<ReactorChildLauncher.StartOptions, IReactorChildLauncher> _launcherFactory;
        private readonly Func<int, string, IEmbedClient> _clientFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly Func<bool> _isCurrentProcessElevated;
        private readonly Func<string, EmbedSession> _projectSwitchSessionFactory;
        private readonly TimeSpan _firstSessionTimeout;
        private readonly TimeSpan _hwndTimeout;
        private readonly Func<string, Task> _logAsync;

        private IReactorChildLauncher? _launcher;
        private IEmbedClient? _client;
        private int _currentSessionId;
        private bool _disposed;
        private bool _stopping;
        private bool _ownerMode;
        private bool _ownerModeFallbackUsed;
        private int _unexpectedRestartAttempts;
        private string? _lastComponentName;
        private string? _lastFailureStderr;
        private TaskCompletionSource<NewSessionEventArgs>? _initialSessionTcs;

        public EmbedSession(
            string csprojPath,
            IntPtr placeholderHwnd,
            Func<Rect> getPlaceholderRect,
            ReactorEmbedControlViewModel vm,
            JoinableTaskFactory jtf,
            bool ownerMode = false)
            : this(
                csprojPath,
                placeholderHwnd,
                getPlaceholderRect,
                vm,
                jtf,
                ownerMode,
                workspaceRoot => DotnetResolver.Resolve(workspaceRoot),
                options => new ReactorChildLauncher(options),
                (port, token) => new EmbedClient(port, token),
                (delay, ct) => Task.Delay(delay, ct),
                () => false,
                null,
                DefaultFirstSessionTimeout,
                DefaultHwndTimeout,
                message => OutputChannel.WriteLineAsync(message))
        {
        }

        internal EmbedSession(
            string csprojPath,
            IntPtr placeholderHwnd,
            Func<Rect> getPlaceholderRect,
            ReactorEmbedControlViewModel vm,
            JoinableTaskFactory jtf,
            bool ownerMode,
            Func<string, DotnetResolver.Result?> dotnetResolver,
            Func<ReactorChildLauncher.StartOptions, IReactorChildLauncher> launcherFactory,
            Func<int, string, IEmbedClient> clientFactory,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
            Func<bool>? isCurrentProcessElevated = null,
            Func<string, EmbedSession>? projectSwitchSessionFactory = null,
            TimeSpan? firstSessionTimeout = null,
            TimeSpan? hwndTimeout = null,
            Func<string, Task>? logAsync = null)
        {
            CsprojPath = string.IsNullOrWhiteSpace(csprojPath) ? throw new ArgumentException("A .csproj path is required.", nameof(csprojPath)) : csprojPath;
            _placeholderHwnd = placeholderHwnd;
            _getPlaceholderRect = getPlaceholderRect ?? throw new ArgumentNullException(nameof(getPlaceholderRect));
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _jtf = jtf ?? throw new ArgumentNullException(nameof(jtf));
            _ownerMode = ownerMode;
            _dotnetResolver = dotnetResolver ?? throw new ArgumentNullException(nameof(dotnetResolver));
            _launcherFactory = launcherFactory ?? throw new ArgumentNullException(nameof(launcherFactory));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _delayAsync = delayAsync ?? ((delay, ct) => Task.Delay(delay, ct));
            _isCurrentProcessElevated = isCurrentProcessElevated ?? (() => false);
            _projectSwitchSessionFactory = projectSwitchSessionFactory ?? (path => new EmbedSession(path, _placeholderHwnd, _getPlaceholderRect, _vm, _jtf, OwnerMode));
            _firstSessionTimeout = firstSessionTimeout ?? DefaultFirstSessionTimeout;
            _hwndTimeout = hwndTimeout ?? DefaultHwndTimeout;
            _logAsync = logAsync ?? (_ => Task.CompletedTask);
        }

        public string CsprojPath { get; }

        public bool OwnerMode => _ownerMode;

        public event EventHandler<ProjectSwitchEventArgs>? ProjectSwitchRequested;

        public Task StartAsync(string? componentName, CancellationToken ct)
        {
            return StartCoreAsync(componentName, ct, resetRestartBudget: true);
        }

        public async Task StopAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            _stopping = true;
            var client = _client;
            _client = null;
            _currentSessionId = 0;

            if (client != null)
            {
                using (var releaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    releaseCts.CancelAfter(ReleaseTimeout);
                    try
                    {
                        await client.ReleaseAsync(releaseCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                    {
                        Log("Release failed: " + ex.Message);
                    }
                }

                client.Dispose();
            }

            var launcher = _launcher;
            _launcher = null;
            if (launcher != null)
            {
                UnsubscribeLauncher(launcher);
                launcher.Dispose();
            }

            _initialSessionTcs = null;

            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.TransitionTo(ViewStatus.Idle);
            _stopping = false;
        }

        public async Task ForceReloadAsync(string? componentName, CancellationToken ct)
        {
            try
            {
                await StopAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Log("Stop during force reload failed: " + ex.Message);
            }

            await _delayAsync(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            await StartCoreAsync(componentName, ct, resetRestartBudget: true).ConfigureAwait(false);
        }

        public async Task RefreshComponentsAsync(CancellationToken ct)
        {
            var client = _client;
            if (client == null)
            {
                return;
            }

            var response = await client.GetComponentsAsync(ct).ConfigureAwait(false);
            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.SetComponents(response.Components, response.Current ?? _vm.SelectedComponent);
        }

        public async Task OnActiveDocumentChangedAsync(string? path, CancellationToken ct = default)
        {
            try
            {
                if (path == null || path.Length == 0 || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var csproj = ProjectContextResolver.FindContainingCsproj(path);
                if (csproj == null)
                {
                    return;
                }

                if (!string.Equals(csproj, CsprojPath, StringComparison.OrdinalIgnoreCase))
                {
                    var componentToSelect = FindFirstComponentInFile(path);
                    await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
                    _vm.ClearPin();
                    _vm.TransitionTo(ViewStatus.ProjectSwitching);
                    await StopAsync(ct).ConfigureAwait(false);
                    var newSession = _projectSwitchSessionFactory(csproj);
                    ProjectSwitchRequested?.Invoke(this, new ProjectSwitchEventArgs(newSession, componentToSelect));
                    return;
                }

                var components = ReadComponentsInFile(path);
                if (components.Count == 0)
                {
                    return;
                }

                await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
                var previous = _vm.SelectedComponent;
                _vm.OnActiveDocumentChanged(path, components);
                var selected = _vm.SelectedComponent;

                if (_client != null && !_vm.IsManuallyPinned && !string.IsNullOrEmpty(selected) && !string.Equals(previous, selected, StringComparison.Ordinal))
                {
                    await _client.PreviewAsync(selected!, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Log("Active document handling failed: " + ex.Message);
            }
        }

        public async Task PostResizeAsync(int width, int height, CancellationToken ct)
        {
            var client = _client;
            if (client == null || _currentSessionId == 0)
            {
                return;
            }

            try
            {
                await client.ResizeAsync(width, height, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Log("Resize failed: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            // StopAsync performs the graceful /embed/release path. Dispose is intentionally
            // synchronous so tool-window teardown cannot deadlock the UI thread; the job kill
            // below is the guaranteed cleanup path for direct Dispose callers.
            _client?.Dispose();
            _client = null;
            var launcher = _launcher;
            _launcher = null;
            if (launcher != null)
            {
                UnsubscribeLauncher(launcher);
                launcher.Dispose();
            }

            _initialSessionTcs = null;
        }

        private async Task StartCoreAsync(string? componentName, CancellationToken ct, bool resetRestartBudget)
        {
            ThrowIfDisposed();
            if (_isCurrentProcessElevated())
            {
                await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
                _vm.ShowError("Visual Studio is elevated", "Embedded preview will silently drop input due to UIPI. Restart VS non-elevated to use embedded preview, or use the standalone preview (mur devtools).");
                _vm.TransitionTo(ViewStatus.BuildFailed);
                return;
            }

            _stopping = false;
            _lastComponentName = componentName;
            if (resetRestartBudget)
            {
                _unexpectedRestartAttempts = 0;
            }

            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.TransitionTo(ViewStatus.Launching);

            var workspaceRoot = Path.GetDirectoryName(CsprojPath) ?? Environment.CurrentDirectory;
            var dotnet = _dotnetResolver(workspaceRoot);
            if (dotnet == null)
            {
                await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
                _vm.ShowError("Cannot find dotnet", "Install the .NET SDK or add dotnet.exe to PATH, then reload the preview.");
                _vm.TransitionTo(ViewStatus.BuildFailed);
                return;
            }

            var options = new ReactorChildLauncher.StartOptions
            {
                CsprojPath = CsprojPath,
                ComponentName = componentName,
                DotnetPath = dotnet.Path,
                HostPid = Process.GetCurrentProcess().Id,
                OwnerMode = OwnerMode,
            };

            var firstSession = new TaskCompletionSource<NewSessionEventArgs>();
            _initialSessionTcs = firstSession;

            var previousLauncher = _launcher;
            _launcher = null;
            if (previousLauncher != null)
            {
                UnsubscribeLauncher(previousLauncher);
                previousLauncher.Dispose();
            }

            _client?.Dispose();
            _client = null;
            _currentSessionId = 0;

            var launcher = _launcherFactory(options);
            _launcher = launcher;
            SubscribeLauncher(launcher);

            NewSessionEventArgs args;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var timeoutTask = _delayAsync(_firstSessionTimeout, timeoutCts.Token);
                var completed = await Task.WhenAny(firstSession.Task, timeoutTask).ConfigureAwait(false);
                if (completed != firstSession.Task)
                {
                    var stderr = string.IsNullOrWhiteSpace(launcher.LastStderr) ? _lastFailureStderr : launcher.LastStderr;
                    await SwitchToMainThreadAsync(ct).ConfigureAwait(true);

                    // Clear the TCS so a late handshake from the still-spawning child
                    // routes through OnLauncherNewSession → HandleNewSessionAsync (which
                    // will TransitionTo(Embedded) and complete the embed). Without this
                    // clear, the late CAPTURE_PORT/CAPTURE_TOKEN sets the TCS that no
                    // one is awaiting and the embed never recovers from BuildFailed.
                    _initialSessionTcs = null;

                    var detail = new StringBuilder();
                    detail.Append("The preview process started but never completed the devtools handshake within ");
                    detail.Append(((int)_firstSessionTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    detail.AppendLine("s.");
                    detail.AppendLine();
                    detail.AppendLine("Common causes:");
                    detail.AppendLine("  • First `dotnet watch` build is slow on a cold disk cache. The session will");
                    detail.AppendLine("    automatically recover if the child eventually emits CAPTURE_PORT — watch");
                    detail.AppendLine("    the Output pane. Set Reactor_VsExtension_HandshakeTimeoutSeconds=300");
                    detail.AppendLine("    in the VS environment to extend this timeout.");
                    detail.AppendLine("  • Target project does not reference Microsoft.UI.Reactor.Devtools.");
                    detail.AppendLine("  • Target project is missing  <RuntimeHostConfigurationOption Include=\"Reactor.DevtoolsSupport\" Value=\"true\" Trim=\"true\" />");
                    detail.AppendLine("  • MSBuild really did fail — see the stderr below.");
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        detail.AppendLine();
                        detail.AppendLine("--- child stderr ---");
                        detail.AppendLine(stderr!.TrimEnd());
                    }

                    _vm.ShowError("Preview handshake timeout", detail.ToString());
                    _vm.TransitionTo(ViewStatus.BuildFailed);
                    return;
                }

                timeoutCts.Cancel();
                args = await firstSession.Task.ConfigureAwait(false);
            }

            _initialSessionTcs = null;
            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.TransitionTo(ViewStatus.WaitingForHandshake);
            await HandleNewSessionAsync(args, ct).ConfigureAwait(false);
        }

        private void SubscribeLauncher(IReactorChildLauncher launcher)
        {
            launcher.NewSession += OnLauncherNewSession;
            launcher.StdoutLine += OnLauncherStdoutLine;
            launcher.StderrLine += OnLauncherStderrLine;
            launcher.SupervisorExited += OnLauncherSupervisorExited;
        }

        private void UnsubscribeLauncher(IReactorChildLauncher launcher)
        {
            launcher.NewSession -= OnLauncherNewSession;
            launcher.StdoutLine -= OnLauncherStdoutLine;
            launcher.StderrLine -= OnLauncherStderrLine;
            launcher.SupervisorExited -= OnLauncherSupervisorExited;
        }

        private void OnLauncherNewSession(object? sender, NewSessionEventArgs args)
        {
            if (sender is not IReactorChildLauncher source || !ReferenceEquals(source, _launcher))
            {
                return;
            }

            var initial = _initialSessionTcs;
            if (initial != null && !initial.Task.IsCompleted)
            {
                initial.TrySetResult(args);
                return;
            }

            SafeAsync.Run(_jtf, () => HandleNewSessionAsync(args, CancellationToken.None), "EmbedSession.NewSession");
        }

        private void OnLauncherStderrLine(object? sender, string line)
        {
            if (sender is not IReactorChildLauncher source || !ReferenceEquals(source, _launcher))
            {
                return;
            }

            Log("stderr: " + line);
        }

        private void OnLauncherStdoutLine(object? sender, string line)
        {
            if (sender is not IReactorChildLauncher source || !ReferenceEquals(source, _launcher))
            {
                return;
            }

            // CAPTURE_PORT/CAPTURE_TOKEN lines are handshake plumbing, not signal
            // for the user, so suppress them here. Everything else (including
            // [devtools] dispatch messages) is useful breadcrumb data when the
            // embed handshake misbehaves.
            if (line.StartsWith("CAPTURE_PORT=", StringComparison.Ordinal) ||
                line.StartsWith("CAPTURE_TOKEN=", StringComparison.Ordinal))
            {
                return;
            }

            Log("stdout: " + line);
        }

        private void OnLauncherSupervisorExited(object? sender, EventArgs args)
        {
            if (sender is not IReactorChildLauncher source || !ReferenceEquals(source, _launcher))
            {
                return;
            }

            SafeAsync.Run(_jtf, () => HandleSupervisorExitedAsync(source, CancellationToken.None), "EmbedSession.SupervisorExited");
        }

        private async Task HandleNewSessionAsync(NewSessionEventArgs args, CancellationToken ct)
        {
            if (_currentSessionId == args.SessionId)
            {
                return;
            }

            _currentSessionId = args.SessionId;
            _lastFailureStderr = null;
            _client?.Dispose();
            var client = _clientFactory(args.Port, args.Token);
            client.GenerationMismatch += (_, mismatch) => Log("Generation mismatch during embed ack: expected " + mismatch.Expected + ", got " + mismatch.Got);
            _client = client;

            try
            {
                await client.StatusAsync(ct).ConfigureAwait(false);
            }
            catch (EmbedProtocolMismatchException ex)
            {
                await FailHandshakeAsync("Reactor version mismatch", ex.Message, ct).ConfigureAwait(false);
                return;
            }

            (IntPtr Hwnd, int Generation) hwnd;
            try
            {
                hwnd = await WaitForHwndAsync(client, ct).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                await FailHandshakeAsync("Preview window not ready", ex.Message, ct).ConfigureAwait(false);
                return;
            }

            var rect = _getPlaceholderRect();
            var width = Math.Max(0, (int)rect.Width);
            var height = Math.Max(0, (int)rect.Height);
            var acknowledged = false;
            string? lastAckError = null;
            for (var attempt = 0; attempt < MaxAckAttempts && !acknowledged; attempt++)
            {
                try
                {
                    acknowledged = await client.AckEmbedAsync(_placeholderHwnd, width, height, hwnd.Generation, ct).ConfigureAwait(false);
                }
                catch (EmbedDpiMismatchException)
                {
                    if (!OwnerMode && !_ownerModeFallbackUsed)
                    {
                        await RestartInOwnerModeAsync(ct).ConfigureAwait(false);
                        return;
                    }

                    var detail = _ownerModeFallbackUsed
                        ? "DPI mismatch in both child and owner modes — see troubleshooting."
                        : "Tool window DPI ≠ Reactor app DPI. Try docking the tool window on the same monitor, or run the app as PerMonitorV2.";
                    await FailHandshakeAsync("DPI mismatch", detail, ct).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                {
                    lastAckError = ex.GetType().Name + ": " + ex.Message;
                    Log("Ack attempt " + (attempt + 1) + " failed: " + lastAckError);
                }

                if (!acknowledged && attempt + 1 < MaxAckAttempts)
                {
                    await _delayAsync(AckRetryDelay, ct).ConfigureAwait(false);
                }
            }

            if (!acknowledged)
            {
                var detail = "The Reactor preview server was not ready to attach after multiple attempts.";
                if (!string.IsNullOrWhiteSpace(lastAckError))
                {
                    detail += " Last error: " + lastAckError;
                }

                await FailHandshakeAsync("Embed handshake failed", detail, ct).ConfigureAwait(false);
                return;
            }

            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.ClearError();
            _vm.TransitionTo(ViewStatus.Embedded);

            try
            {
                await RefreshComponentsAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Log("Component refresh failed: " + ex.Message);
            }
        }

        private async Task<(IntPtr Hwnd, int Generation)> WaitForHwndAsync(IEmbedClient client, CancellationToken ct)
        {
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var timeoutTask = _delayAsync(_hwndTimeout, timeoutCts.Token);
                while (true)
                {
                    var hwndTask = client.GetHwndAsync(ct);
                    var completed = await Task.WhenAny(hwndTask, timeoutTask).ConfigureAwait(false);
                    if (completed == timeoutTask)
                    {
                        throw new TimeoutException("Timed out waiting for the Reactor window HWND.");
                    }

                    var hwnd = await hwndTask.ConfigureAwait(false);
                    if (hwnd.Hwnd != IntPtr.Zero)
                    {
                        timeoutCts.Cancel();
                        return hwnd;
                    }

                    await _delayAsync(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
                }
            }
        }

        internal async Task RestartInOwnerModeAsync(CancellationToken ct)
        {
            _ownerModeFallbackUsed = true;
            try
            {
                await _logAsync("DPI mismatch detected — automatically retrying in owner-mode (window will float above VS instead of embedding)").ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await StopAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Log("Stop during owner-mode fallback failed: " + ex.Message);
            }

            _ownerMode = true;
            await StartCoreAsync(_lastComponentName, ct, resetRestartBudget: false).ConfigureAwait(false);
        }

        private async Task FailHandshakeAsync(string title, string detail, CancellationToken ct)
        {
            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.ShowError(title, detail);
            _vm.TransitionTo(ViewStatus.BuildFailed);
        }

        private async Task HandleSupervisorExitedAsync(IReactorChildLauncher source, CancellationToken ct)
        {
            if (_disposed || _stopping || !ReferenceEquals(source, _launcher))
            {
                return;
            }

            var stderr = source.LastStderr ?? string.Empty;
            _lastFailureStderr = stderr;
            Log("Supervisor exited." + (string.IsNullOrWhiteSpace(stderr) ? string.Empty : Environment.NewLine + stderr));
            _client?.Dispose();
            _client = null;
            _currentSessionId = 0;

            if (_unexpectedRestartAttempts == 0)
            {
                _unexpectedRestartAttempts++;
                await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
                _vm.TransitionTo(ViewStatus.Respawning);
                await StartCoreAsync(_lastComponentName, ct, resetRestartBudget: false).ConfigureAwait(false);
                return;
            }

            await SwitchToMainThreadAsync(ct).ConfigureAwait(true);
            _vm.ShowError("Preview crashed", string.IsNullOrWhiteSpace(stderr) ? "The Reactor preview process exited unexpectedly." : stderr);
            _vm.TransitionTo(ViewStatus.Crashed);
        }

        private static IReadOnlyList<string> ReadComponentsInFile(string path)
        {
            try
            {
                return ProjectContextResolver.FindComponentClasses(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static string? FindFirstComponentInFile(string path)
        {
            return ReadComponentsInFile(path).FirstOrDefault();
        }

        private void Log(string message)
        {
            try
            {
                SafeAsync.Run(_jtf, () => _logAsync(message), "EmbedSession.Log");
            }
            catch
            {
            }
        }

        private async Task SwitchToMainThreadAsync(CancellationToken ct)
        {
            await _jtf.SwitchToMainThreadAsync(ct);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EmbedSession));
            }
        }
    }

    internal sealed class ProjectSwitchEventArgs : EventArgs
    {
        public ProjectSwitchEventArgs(EmbedSession session, string? component)
        {
            NewSession = session ?? throw new ArgumentNullException(nameof(session));
            ComponentToSelect = component;
        }

        public EmbedSession NewSession { get; }

        public string? ComponentToSelect { get; }
    }
}
