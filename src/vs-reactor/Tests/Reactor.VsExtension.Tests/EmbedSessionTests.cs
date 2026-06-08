#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Microsoft.UI.Reactor.VsExtension.Logging;
using Microsoft.UI.Reactor.VsExtension.Session;
using Microsoft.UI.Reactor.VsExtension.UI;
using Microsoft.VisualStudio.Threading;
using Xunit;
using ProtocolStatus = Microsoft.UI.Reactor.VsExtension.Embed.EmbedStatus;
using ViewStatus = Microsoft.UI.Reactor.VsExtension.UI.EmbedStatus;

namespace Reactor.VsExtension.Tests
{
    public sealed class EmbedSessionTests
    {
        [Fact]
        public async Task EmbedSession_StartSuccessFlow_TransitionsThroughLaunchingWaitingEmbedded()
        {
            var harness = new SessionHarness();
            var transitions = CaptureTransitions(harness.ViewModel);

            await harness.Session.StartAsync("Counter", CancellationToken.None);

            Assert.Contains(ViewStatus.Launching, transitions);
            Assert.Contains(ViewStatus.WaitingForHandshake, transitions);
            Assert.Contains(ViewStatus.Embedded, transitions);
            Assert.Equal(ViewStatus.Embedded, harness.ViewModel.CurrentStatus);
            Assert.Equal(new[] { "Counter", "Todo" }, harness.ViewModel.Components.ToArray());
        }

        [Fact]
        public async Task EmbedSession_DotnetResolverFailure_TransitionsToBuildFailed()
        {
            var harness = new SessionHarness(dotnetAvailable: false);

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.True(harness.ViewModel.ErrorOverlayVisible);
            Assert.Equal("Cannot find dotnet", harness.ViewModel.ErrorTitle);
        }

        [Fact]
        public async Task EmbedSession_LauncherFactoryThrows_TransitionsToBuildFailed()
        {
            var harness = new SessionHarness(launcherException: new Win32Exception(5, "access denied"));

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.Equal("Preview startup failed", harness.ViewModel.ErrorTitle);
            Assert.Contains("access denied", harness.ViewModel.ErrorDetail);
            Assert.Contains("cleanup job object", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_DpiMismatch_TransitionsToBuildFailed_WithActionableMessage()
        {
            var client = new FakeEmbedClient { AckException = new EmbedDpiMismatchException("dpi") };
            var harness = new SessionHarness(client: client, ownerMode: true);

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.Equal("DPI mismatch", harness.ViewModel.ErrorTitle);
            Assert.Contains("same monitor", harness.ViewModel.ErrorDetail);
            Assert.Contains("PerMonitorV2", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_DpiMismatchOnFirstAck_FallsBackToOwnerMode()
        {
            var firstClient = new FakeEmbedClient { AckException = new EmbedDpiMismatchException("dpi") };
            var secondClient = new FakeEmbedClient();
            var harness = new SessionHarness(clients: new[] { firstClient, secondClient });

            await harness.Session.StartAsync("Counter", CancellationToken.None);

            Assert.Equal(2, harness.Launchers.Count);
            Assert.False(harness.Launchers[0].Options?.OwnerMode);
            Assert.True(harness.Launchers[1].Options?.OwnerMode);
            Assert.Equal(ViewStatus.Embedded, harness.ViewModel.CurrentStatus);
            Assert.Contains(harness.Logs, line => line.IndexOf("automatically retrying in owner-mode", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public async Task EmbedSession_DpiMismatchInOwnerMode_DoesNotLoopFallback()
        {
            var firstClient = new FakeEmbedClient { AckException = new EmbedDpiMismatchException("dpi") };
            var secondClient = new FakeEmbedClient { AckException = new EmbedDpiMismatchException("dpi") };
            var harness = new SessionHarness(clients: new[] { firstClient, secondClient });

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(2, harness.Launchers.Count);
            Assert.True(harness.Launchers[1].Options?.OwnerMode);
            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.True(harness.ViewModel.ErrorOverlayVisible);
            Assert.Equal("DPI mismatch", harness.ViewModel.ErrorTitle);
            Assert.Contains("both child and owner modes", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_ProtocolMismatch_TransitionsToBuildFailed()
        {
            var client = new FakeEmbedClient { StatusException = new EmbedProtocolMismatchException("wrong protocol") };
            var harness = new SessionHarness(client: client);

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.Equal("Reactor version mismatch", harness.ViewModel.ErrorTitle);
            Assert.Contains("wrong protocol", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_GenerationMismatchOnAck_Retries()
        {
            var client = new FakeEmbedClient { AckFailuresBeforeSuccess = 3 };
            var harness = new SessionHarness(client: client);

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(ViewStatus.Embedded, harness.ViewModel.CurrentStatus);
            Assert.Equal(4, client.AckCalls);
        }

        [Fact]
        public async Task EmbedSession_AckTransientFailure_RetriesThenFailsHandshake()
        {
            var client = new FakeEmbedClient { AckException = new HttpRequestException("loopback reset") };
            var harness = new SessionHarness(client: client);

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(5, client.AckCalls);
            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.Equal("Embed handshake failed", harness.ViewModel.ErrorTitle);
            Assert.Contains("HttpRequestException", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_SupervisorExits_AutoRestartsOnce()
        {
            var harness = new SessionHarness();
            await harness.Session.StartAsync(null, CancellationToken.None);
            var first = harness.Launchers[0];

            first.EmitSupervisorExited();
            await EventuallyAsync(() => harness.Launchers.Count == 2 && harness.ViewModel.CurrentStatus == ViewStatus.Embedded);

            harness.Launchers[1].LastStderr = "boom";
            harness.Launchers[1].EmitSupervisorExited();
            await EventuallyAsync(() => harness.ViewModel.CurrentStatus == ViewStatus.Crashed);

            Assert.Equal(2, harness.Launchers.Count);
            Assert.Equal("Preview crashed", harness.ViewModel.ErrorTitle);
            Assert.Contains("boom", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_StopAsync_DisposesLauncherAndTransitionsToIdle()
        {
            var client = new FakeEmbedClient();
            var harness = new SessionHarness(client: client);
            await harness.Session.StartAsync(null, CancellationToken.None);

            await harness.Session.StopAsync(CancellationToken.None);

            Assert.True(client.ReleaseCalled);
            Assert.True(client.Disposed);
            Assert.True(harness.Launchers[0].Disposed);
            Assert.Equal(ViewStatus.Idle, harness.ViewModel.CurrentStatus);
        }

        [Fact]
        public async Task EmbedSession_StaleLauncherSupervisorExited_IgnoredAfterDispose()
        {
            var harness = new SessionHarness();
            await harness.Session.StartAsync(null, CancellationToken.None);
            var staleLauncher = harness.Launchers[0];

            await harness.Session.StopAsync(CancellationToken.None);
            staleLauncher.EmitSupervisorExited();
            await Task.Delay(100);

            Assert.Single(harness.Launchers);
            Assert.Equal(ViewStatus.Idle, harness.ViewModel.CurrentStatus);
        }

        [Fact]
        public async Task EmbedSession_ForceReload_StopsThenStarts()
        {
            var transitions = new List<ViewStatus>();
            var harness = new SessionHarness();
            harness.ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReactorEmbedControlViewModel.StatusText))
                {
                    transitions.Add(harness.ViewModel.CurrentStatus);
                }
            };

            await harness.Session.StartAsync("Counter", CancellationToken.None);
            await harness.Session.ForceReloadAsync("Counter", CancellationToken.None);

            Assert.True(harness.Launchers[0].Disposed);
            Assert.Equal(2, harness.Launchers.Count);
            Assert.Equal(ViewStatus.Embedded, harness.ViewModel.CurrentStatus);
            Assert.Contains(ViewStatus.Idle, transitions);
            Assert.True(transitions.LastIndexOf(ViewStatus.Launching) > transitions.IndexOf(ViewStatus.Idle));
        }

        [Fact]
        public async Task EmbedSession_Stop_DisposesLocalState_WhenReleaseFails()
        {
            var client = new FakeEmbedClient { ReleaseException = new OperationCanceledException("release canceled") };
            var harness = new SessionHarness(client: client);

            await harness.Session.StartAsync("Counter", CancellationToken.None);
            await harness.Session.StopAsync(CancellationToken.None);

            Assert.True(client.ReleaseCalled);
            Assert.True(client.Disposed);
            Assert.True(harness.Launchers[0].Disposed);
            Assert.Equal(ViewStatus.Idle, harness.ViewModel.CurrentStatus);
        }

        [Fact]
        public async Task EmbedSession_LateSessionAfterStop_DoesNotResurrectPreview()
        {
            var harness = new SessionHarness(autoEmitLaunchers: new[] { false }, firstSessionTimeout: TimeSpan.FromSeconds(30));
            var startTask = harness.Session.StartAsync("Counter", CancellationToken.None);
            await EventuallyAsync(() => harness.Launchers.Count == 1);

            await harness.Session.StopAsync(CancellationToken.None);
            harness.Launchers[0].EmitNewSession();
            await startTask;

            Assert.Equal(ViewStatus.Idle, harness.ViewModel.CurrentStatus);
            Assert.Empty(harness.ViewModel.Components);
        }

        [Fact]
        public async Task EmbedSession_OnActiveDocumentChanged_AutoSelectsAndPreviewsComponent()
        {
            var root = CreateProjectWorkspace("AutoSelect");
            try
            {
                var csproj = Path.Combine(root, "App.csproj");
                File.WriteAllText(csproj, "<Project />");
                var file = Path.Combine(root, "Todo.cs");
                File.WriteAllText(file, "public class Todo : Component { }");
                var client = new FakeEmbedClient();
                var harness = new SessionHarness(client: client, csprojPath: csproj);
                await harness.Session.StartAsync("Counter", CancellationToken.None);

                await harness.Session.OnActiveDocumentChangedAsync(file, CancellationToken.None);

                Assert.Equal("Todo", harness.ViewModel.SelectedComponent);
                Assert.Equal("Todo", client.PreviewedComponent);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public async Task Lifecycle_SupervisorExits_NoRespawnWithin30s_TransitionsToBuildFailed_WithStderr()
        {
            var harness = new SessionHarness(autoEmitLaunchers: new[] { true, false }, firstSessionTimeout: TimeSpan.FromMilliseconds(50));
            await harness.Session.StartAsync(null, CancellationToken.None);
            harness.Launchers[0].LastStderr = "CS1002 expected ;";

            harness.Launchers[0].EmitSupervisorExited();
            await EventuallyAsync(() => harness.ViewModel.CurrentStatus == ViewStatus.BuildFailed);

            Assert.Equal(2, harness.Launchers.Count);
            // Title renamed from "Build failed" → "Preview handshake timeout"
            // to accurately reflect what timed out: the child started, but
            // never completed the devtools handshake. MSBuild errors surface
            // in the detail's child-stderr section.
            Assert.Equal("Preview handshake timeout", harness.ViewModel.ErrorTitle);
            Assert.Contains("CS1002", harness.ViewModel.ErrorDetail);
            Assert.Contains("Microsoft.UI.Reactor.Devtools", harness.ViewModel.ErrorDetail);
        }

        [Fact]
        public async Task EmbedSession_LateHandshakeAfterTimeout_RecoversToEmbedded()
        {
            // Cold `dotnet watch` first-build can exceed the handshake timeout
            // on slow disks (loads 6+ projects + analyzers + WinAppSDK).
            // When the timeout fires we transition to BuildFailed but keep
            // the launcher subscription alive — so when CAPTURE_PORT eventually
            // arrives, OnLauncherNewSession routes the late session through
            // HandleNewSessionAsync, which completes the embed and transitions
            // back to Embedded. Without this recovery, a slow first build
            // leaves the UI permanently stuck.
            var harness = new SessionHarness(
                autoEmitLaunchers: new[] { false },
                firstSessionTimeout: TimeSpan.FromMilliseconds(50));

            await harness.Session.StartAsync(null, CancellationToken.None);

            // Timed out → BuildFailed with the handshake-timeout error.
            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.Equal("Preview handshake timeout", harness.ViewModel.ErrorTitle);

            // Simulate the late CAPTURE_PORT/CAPTURE_TOKEN arrival after the timeout.
            harness.Launchers[0].EmitNewSession();

            await EventuallyAsync(() => harness.ViewModel.CurrentStatus == ViewStatus.Embedded);
            Assert.Equal(ViewStatus.Embedded, harness.ViewModel.CurrentStatus);
            Assert.Single(harness.Launchers);
        }

        [Fact]
        public void EmbedSession_DefaultHandshakeTimeout_HonorsEnvironmentOverride()
        {
            // Static field init reads Reactor_VsExtension_HandshakeTimeoutSeconds
            // once per process; the test verifies the parser, not the side
            // effect on a running session. The default (180s) is generous
            // enough to cover a cold dotnet watch first build of a Reactor app.
            var method = typeof(EmbedSession).GetMethod(
                "ResolveHandshakeTimeoutFromEnv",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            var original = Environment.GetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds");
            try
            {
                Environment.SetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds", null);
                var defaultValue = (TimeSpan)method!.Invoke(null, null)!;
                Assert.True(defaultValue >= TimeSpan.FromSeconds(60),
                    $"Default timeout {defaultValue.TotalSeconds}s is too short for cold dotnet watch first build.");

                Environment.SetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds", "42");
                var overridden = (TimeSpan)method!.Invoke(null, null)!;
                Assert.Equal(TimeSpan.FromSeconds(42), overridden);

                Environment.SetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds", "garbage");
                var fallback = (TimeSpan)method!.Invoke(null, null)!;
                Assert.Equal(defaultValue, fallback);

                Environment.SetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds", "-1");
                var negativeFallback = (TimeSpan)method!.Invoke(null, null)!;
                Assert.Equal(defaultValue, negativeFallback);
            }
            finally
            {
                Environment.SetEnvironmentVariable("Reactor_VsExtension_HandshakeTimeoutSeconds", original);
            }
        }

        [Fact]
        public async Task Lifecycle_ProjectSwitch_IsNotAutomatic_ForDifferentProject()
        {
            var root = CreateProjectWorkspace("ProjectSwitch");
            try
            {
                var firstDir = Path.Combine(root, "First");
                var secondDir = Path.Combine(root, "Second");
                Directory.CreateDirectory(firstDir);
                Directory.CreateDirectory(secondDir);
                var firstCsproj = Path.Combine(firstDir, "First.csproj");
                var secondCsproj = Path.Combine(secondDir, "Second.csproj");
                File.WriteAllText(firstCsproj, "<Project />");
                File.WriteAllText(secondCsproj, "<Project />");
                var secondFile = Path.Combine(secondDir, "Switched.cs");
                File.WriteAllText(secondFile, "public class Switched : Component { }");
                var harness = new SessionHarness(csprojPath: firstCsproj);
                var transitions = CaptureTransitions(harness.ViewModel);
                ProjectSwitchEventArgs? switchArgs = null;
                harness.Session.ProjectSwitchRequested += (_, args) => switchArgs = args;
                await harness.Session.StartAsync("Counter", CancellationToken.None);

                await harness.Session.OnActiveDocumentChangedAsync(secondFile, CancellationToken.None);

                Assert.Null(switchArgs);
                Assert.DoesNotContain(ViewStatus.ProjectSwitching, transitions);
                Assert.Equal(ViewStatus.Embedded, harness.ViewModel.CurrentStatus);
                Assert.Single(harness.Launchers);
                Assert.Equal(firstCsproj, harness.Launchers[0].Options?.CsprojPath);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public async Task EmbedSession_StartWhenElevated_TransitionsToBuildFailed_WithMessage()
        {
            var harness = new SessionHarness(elevated: true);

            await harness.Session.StartAsync(null, CancellationToken.None);

            Assert.Equal(ViewStatus.BuildFailed, harness.ViewModel.CurrentStatus);
            Assert.Equal("Visual Studio is elevated", harness.ViewModel.ErrorTitle);
            Assert.Contains("UIPI", harness.ViewModel.ErrorDetail);
            Assert.Empty(harness.Launchers);
        }

        private static List<ViewStatus> CaptureTransitions(ReactorEmbedControlViewModel vm)
        {
            var transitions = new List<ViewStatus>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReactorEmbedControlViewModel.StatusText))
                {
                    transitions.Add(vm.CurrentStatus);
                }
            };

            return transitions;
        }

        private static string CreateProjectWorkspace(string name)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "EmbedSessionTests", name, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static async Task EventuallyAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.True(condition());
        }

        private sealed class SessionHarness : IDisposable
        {
            private readonly JoinableTaskContext _context = new JoinableTaskContext();
            private readonly Queue<FakeEmbedClient> _clients = new Queue<FakeEmbedClient>();
            private readonly Queue<bool> _autoEmitLaunchers;
            private readonly bool _dotnetAvailable;
            private readonly bool _elevated;
            private readonly bool _ownerMode;
            private readonly TimeSpan _firstSessionTimeout;
            private readonly Exception? _launcherException;

            public SessionHarness(
                bool dotnetAvailable = true,
                FakeEmbedClient? client = null,
                IEnumerable<FakeEmbedClient>? clients = null,
                string? csprojPath = null,
                IEnumerable<bool>? autoEmitLaunchers = null,
                bool elevated = false,
                bool ownerMode = false,
                TimeSpan? firstSessionTimeout = null,
                Exception? launcherException = null)
            {
                _dotnetAvailable = dotnetAvailable;
                _autoEmitLaunchers = new Queue<bool>(autoEmitLaunchers ?? new[] { true });
                _elevated = elevated;
                _ownerMode = ownerMode;
                _firstSessionTimeout = firstSessionTimeout ?? TimeSpan.FromSeconds(1);
                _launcherException = launcherException;
                if (clients != null)
                {
                    foreach (var queuedClient in clients)
                    {
                        _clients.Enqueue(queuedClient);
                    }
                }
                else if (client != null)
                {
                    _clients.Enqueue(client);
                }

                ViewModel = new ReactorEmbedControlViewModel(_context.Factory);
                Session = CreateSession(csprojPath ?? @"C:\workspace\App\App.csproj");
            }

            public ReactorEmbedControlViewModel ViewModel { get; }

            public EmbedSession Session { get; }

            public List<FakeReactorChildLauncher> Launchers { get; } = new List<FakeReactorChildLauncher>();

            public List<string> Logs { get; } = new List<string>();

            public void Dispose()
            {
                Session.Dispose();
            }

            private EmbedSession CreateSession(string csprojPath)
            {
                return new EmbedSession(
                    csprojPath,
                    new IntPtr(0x1234),
                    () => new Rect(0, 0, 800, 600),
                    ViewModel,
                    _context.Factory,
                    ownerMode: _ownerMode,
                    ResolveDotnet,
                    CreateLauncher,
                    CreateClient,
                    delayAsync: DelayAsync,
                    isCurrentProcessElevated: () => _elevated,
                    projectSwitchSessionFactory: CreateSession,
                    firstSessionTimeout: _firstSessionTimeout,
                    hwndTimeout: TimeSpan.FromSeconds(1),
                    logAsync: LogAsync);
            }

            private DotnetResolver.Result? ResolveDotnet(string workspaceRoot)
            {
                return _dotnetAvailable ? new DotnetResolver.Result(@"C:\Program Files\dotnet\dotnet.exe", "test") : null;
            }

            private IReactorChildLauncher CreateLauncher(ReactorChildLauncher.StartOptions options)
            {
                if (_launcherException != null)
                {
                    throw _launcherException;
                }

                var launcher = new FakeReactorChildLauncher
                {
                    AutoEmitOnSubscribe = _autoEmitLaunchers.Count == 0 || _autoEmitLaunchers.Dequeue(),
                    Options = options,
                };
                Launchers.Add(launcher);
                return launcher;
            }

            private IEmbedClient CreateClient(int port, string token)
            {
                return _clients.Count > 0 ? _clients.Dequeue() : new FakeEmbedClient();
            }

            private Task LogAsync(string message)
            {
                Logs.Add(message);
                return Task.CompletedTask;
            }

            private static Task DelayAsync(TimeSpan delay, CancellationToken ct)
            {
                return delay.TotalMilliseconds >= 1000 ? Task.Delay(delay, ct) : Task.CompletedTask;
            }
        }

        private sealed class FakeReactorChildLauncher : IReactorChildLauncher
        {
            private EventHandler<NewSessionEventArgs>? _newSession;
            private bool _emitted;

            public event EventHandler<NewSessionEventArgs>? NewSession
            {
                add
                {
                    _newSession += value;
                    if (AutoEmitOnSubscribe && !_emitted)
                    {
                        _emitted = true;
                        EmitNewSession();
                    }
                }
                remove => _newSession -= value;
            }

            public event EventHandler<string>? StdoutLine;

            public event EventHandler<string>? StderrLine;

            public event EventHandler? SupervisorExited;

            public string LastStderr { get; set; } = string.Empty;

            public bool AutoEmitOnSubscribe { get; set; }

            public ReactorChildLauncher.StartOptions? Options { get; set; }

            public bool Disposed { get; private set; }

            public void EmitNewSession()
            {
                _newSession?.Invoke(this, new NewSessionEventArgs { SessionId = 1, Port = 5000, Token = "token" });
            }

            public void EmitNewSession(int sessionId, int port, string token)
            {
                _newSession?.Invoke(this, new NewSessionEventArgs { SessionId = sessionId, Port = port, Token = token });
            }

            public void EmitSupervisorExited()
            {
                SupervisorExited?.Invoke(this, EventArgs.Empty);
            }

            public void EmitStderr(string line)
            {
                StderrLine?.Invoke(this, line);
            }

            public void EmitStdout(string line)
            {
                StdoutLine?.Invoke(this, line);
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class FakeEmbedClient : IEmbedClient
        {
            public event EventHandler<EmbedGenerationMismatchEventArgs>? GenerationMismatch;

            public Exception? StatusException { get; set; }

            public Exception? AckException { get; set; }

            public int AckFailuresBeforeSuccess { get; set; }

            public int AckCalls { get; private set; }

            public bool ReleaseCalled { get; private set; }

            public Exception? ReleaseException { get; set; }

            public string? PreviewedComponent { get; private set; }

            public bool Disposed { get; private set; }

            public Task<ProtocolStatus> StatusAsync(CancellationToken ct = default)
            {
                if (StatusException != null)
                {
                    throw StatusException;
                }

                return Task.FromResult(new ProtocolStatus(false, 0, 5000, "embed-v1", 1));
            }

            public Task<(IntPtr Hwnd, int Generation)> GetHwndAsync(CancellationToken ct = default)
            {
                return Task.FromResult((new IntPtr(0x5678), 1));
            }

            public Task<bool> AckEmbedAsync(IntPtr parent, int width, int height, int generation, CancellationToken ct = default)
            {
                AckCalls++;
                if (AckException != null)
                {
                    throw AckException;
                }

                if (AckCalls <= AckFailuresBeforeSuccess)
                {
                    GenerationMismatch?.Invoke(this, new EmbedGenerationMismatchEventArgs(2, 1));
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }

            public Task ResizeAsync(int width, int height, CancellationToken ct = default)
            {
                return Task.CompletedTask;
            }

            public Task MoveAsync(int x, int y, CancellationToken ct = default)
            {
                return Task.CompletedTask;
            }

            public Task ReleaseAsync(CancellationToken ct = default)
            {
                ReleaseCalled = true;
                if (ReleaseException != null)
                {
                    throw ReleaseException;
                }

                return Task.CompletedTask;
            }

            public Task<EmbedComponentsResponse> GetComponentsAsync(CancellationToken ct = default)
            {
                return Task.FromResult(new EmbedComponentsResponse(new[] { "Counter", "Todo" }, "Counter"));
            }

            public Task<bool> PreviewAsync(string componentName, CancellationToken ct = default)
            {
                PreviewedComponent = componentName;
                return Task.FromResult(true);
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }

    public sealed class LoggingTests
    {
        [Fact]
        public async Task SafeAsync_Run_CatchesException_Logs_DoesNotThrow()
        {
            OutputChannel.ResetForTest();
            var lines = new List<string>();
            OutputChannel.InitializeForTest(line =>
            {
                lines.Add(line);
                return Task.CompletedTask;
            });

            var context = new JoinableTaskContext();
            SafeAsync.Run(context.Factory, () => throw new InvalidOperationException("boom"), "Explode");

            await EventuallyAsync(() => lines.Count >= 2);
            Assert.Contains(lines, line => line.Contains("[Explode] InvalidOperationException: boom"));
            Assert.Contains(lines, line => line.Contains("InvalidOperationException"));
        }

        [Fact]
        public async Task SafeAsync_AsyncRun_NoGetAwaiterGetResult_OnUiThread()
        {
            OutputChannel.ResetForTest();
            var lines = new List<string>();
            OutputChannel.InitializeForTest(async line =>
            {
                await Task.Yield();
                lines.Add(line);
            });

            var context = new JoinableTaskContext();
            context.Factory.Run(async () =>
            {
                await context.Factory.SwitchToMainThreadAsync();
                SafeAsync.Run(context.Factory, async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("async boom");
                }, "AsyncExplode");
            });

            await EventuallyAsync(() => lines.Count >= 2);
            Assert.Contains(lines, line => line.Contains("[AsyncExplode] InvalidOperationException: async boom"));
        }

        [Fact]
        public async Task OutputChannel_BuffersBeforeInit_FlushesOnInit()
        {
            OutputChannel.ResetForTest();
            await OutputChannel.WriteLineAsync("before");

            var lines = new List<string>();
            OutputChannel.InitializeForTest(line =>
            {
                lines.Add(line);
                return Task.CompletedTask;
            });
            await OutputChannel.WriteLineAsync("after");

            Assert.Equal(2, lines.Count);
            Assert.Contains("before", lines[0]);
            Assert.Contains("after", lines[1]);
        }

        private static async Task EventuallyAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.True(condition());
        }
    }
}
