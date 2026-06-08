#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.VsExtension.Embed
{
    internal interface IReactorChildLauncher : IDisposable
    {
        event EventHandler<NewSessionEventArgs>? NewSession;

        event EventHandler<string>? StdoutLine;

        event EventHandler<string>? StderrLine;

        event EventHandler? SupervisorExited;

        string LastStderr { get; }
    }

    internal sealed class ReactorChildLauncher : IReactorChildLauncher
    {
        private const int StderrTailLimit = 16 * 1024;
        private static readonly Regex PortRegex = new Regex("^CAPTURE_PORT=(\\d+)$", RegexOptions.Compiled);
        private static readonly Regex TokenRegex = new Regex("^CAPTURE_TOKEN=([A-Za-z0-9_\\-]+)$", RegexOptions.Compiled);

        private readonly JobObject? _job;
        private readonly IChildProcessIo _io;
        private readonly StringBuilder _stderrTail = new StringBuilder();
        private readonly object _lock = new object();
        private int _sessionCounter;
        private int? _pendingPort;
        private string? _pendingToken;
        private EventHandler<NewSessionEventArgs>? _newSession;
        private NewSessionEventArgs? _lastSession;
        private bool _disposed;

        public ReactorChildLauncher(StartOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            JobObject? job = null;
            IChildProcessIo? io = null;
            try
            {
                job = new JobObject();
                _job = job;
                io = new ChildProcessIo(CreateStartInfo(options));
                _io = io;
                AttachIoHandlers();
                _io.Start(_job);
            }
            catch
            {
                io?.Dispose();
                job?.Dispose();
                throw;
            }
        }

        internal ReactorChildLauncher(IChildProcessIo io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
            AttachIoHandlers();
            _io.Start(null);
        }

        public event EventHandler<NewSessionEventArgs>? NewSession
        {
            add
            {
                NewSessionEventArgs? replay;
                lock (_lock)
                {
                    _newSession += value;
                    replay = _lastSession;
                }

                if (value != null && replay != null)
                {
                    value(this, replay);
                }
            }
            remove
            {
                lock (_lock)
                {
                    _newSession -= value;
                }
            }
        }

        public event EventHandler<string>? StdoutLine;

        public event EventHandler<string>? StderrLine;

        public event EventHandler? SupervisorExited;

        public string LastStderr { get; private set; } = string.Empty;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _job?.Dispose();
            _io.Dispose();
        }

        private static ProcessStartInfo CreateStartInfo(StartOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.CsprojPath))
            {
                throw new ArgumentException("A .csproj path is required.", nameof(options));
            }

            var workingDirectory = Path.GetDirectoryName(options.CsprojPath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = Environment.CurrentDirectory;
            }

            var info = new ProcessStartInfo
            {
                FileName = options.DotnetPath,
                Arguments = BuildArguments(options),
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            info.EnvironmentVariables["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "1";
            info.EnvironmentVariables["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1";
            info.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"] = "1";
            return info;
        }

        private static string BuildArguments(StartOptions options)
        {
            var args = new List<string>
            {
                "watch",
                "run",
                "--no-hot-reload-warnings",
            };

            if (!string.IsNullOrWhiteSpace(options.TargetFramework))
            {
                args.Add("--framework");
                args.Add(options.TargetFramework!);
            }

            args.Add("--project");
            args.Add(options.CsprojPath);
            args.Add("--");
            args.Add("--devtools");
            args.Add("run");

            if (!string.IsNullOrWhiteSpace(options.ComponentName))
            {
                args.Add(options.ComponentName!);
            }

            args.Add("--embed");
            args.Add("--embed-mode");
            args.Add(options.OwnerMode ? "owner" : "child");
            args.Add("--embed-host-pid");
            args.Add(options.HostPid.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var builder = new StringBuilder();
            for (var i = 0; i < args.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(args[i]));
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length == 0)
            {
                return "\"\"";
            }

            if (argument.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0)
            {
                return argument;
            }

            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        private void AttachIoHandlers()
        {
            _io.StdoutLine += OnStdoutLine;
            _io.StderrLine += OnStderrLine;
            _io.Exited += OnExited;
        }

        private void OnStdoutLine(string line)
        {
            if (line == null)
            {
                return;
            }

            // Forward to subscribers (EmbedSession logs to the Output pane) BEFORE
            // pattern matching so authors can see [devtools] / [embed] / etc. lines
            // from the child even when CAPTURE_PORT/CAPTURE_TOKEN never arrive.
            StdoutLine?.Invoke(this, line);

            var portMatch = PortRegex.Match(line);
            if (portMatch.Success)
            {
                lock (_lock)
                {
                    _pendingPort = int.Parse(portMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                TryEmitSession();
            }

            var tokenMatch = TokenRegex.Match(line);
            if (tokenMatch.Success)
            {
                lock (_lock)
                {
                    _pendingToken = tokenMatch.Groups[1].Value;
                }

                TryEmitSession();
            }

            if (line.IndexOf("Do you want to restart your app", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _io.WriteToStdin("y\n");
            }
        }

        private void TryEmitSession()
        {
            NewSessionEventArgs? args = null;
            EventHandler<NewSessionEventArgs>? handler = null;
            lock (_lock)
            {
                if (_pendingPort.HasValue && _pendingToken != null)
                {
                    args = new NewSessionEventArgs
                    {
                        SessionId = ++_sessionCounter,
                        Port = _pendingPort.Value,
                        Token = _pendingToken,
                    };
                    _pendingPort = null;
                    _pendingToken = null;
                    _lastSession = args;
                    handler = _newSession;
                }
            }

            if (args != null)
            {
                handler?.Invoke(this, args);
            }
        }

        private void OnStderrLine(string line)
        {
            if (line == null)
            {
                return;
            }

            lock (_lock)
            {
                _stderrTail.AppendLine(line);
                if (_stderrTail.Length > StderrTailLimit)
                {
                    _stderrTail.Remove(0, _stderrTail.Length - StderrTailLimit);
                }

                LastStderr = _stderrTail.ToString();
            }

            StderrLine?.Invoke(this, line);
        }

        private void OnExited()
        {
            lock (_lock)
            {
                LastStderr = _stderrTail.ToString();
            }

            SupervisorExited?.Invoke(this, EventArgs.Empty);
        }

        internal sealed class StartOptions
        {
            public string CsprojPath { get; init; } = string.Empty;

            public string? ComponentName { get; init; }

            public string? TargetFramework { get; init; }

            public string DotnetPath { get; init; } = "dotnet";

            public int HostPid { get; init; } = Process.GetCurrentProcess().Id;

            public bool OwnerMode { get; init; }
        }
    }

    internal sealed class NewSessionEventArgs : EventArgs
    {
        public int SessionId { get; init; }

        public int Port { get; init; }

        public string Token { get; init; } = string.Empty;
    }

    internal interface IChildProcessIo : IDisposable
    {
        event Action<string>? StdoutLine;

        event Action<string>? StderrLine;

        event Action? Exited;

        void Start(JobObject? job);

        void WriteToStdin(string value);
    }

    internal sealed class ChildProcessIo : IChildProcessIo
    {
        private readonly Process _process;
        private bool _started;

        public ChildProcessIo(ProcessStartInfo startInfo)
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    StdoutLine?.Invoke(e.Data);
                }
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    StderrLine?.Invoke(e.Data);
                }
            };
            _process.Exited += (_, __) => Exited?.Invoke();
        }

        public event Action<string>? StdoutLine;

        public event Action<string>? StderrLine;

        public event Action? Exited;

        public void Start(JobObject? job)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to start the Reactor preview supervisor process.");
            }

            try
            {
                // Process.Start cannot request CREATE_SUSPENDED, so a very fast child could
                // spawn a grandchild before job assignment. Phase 1 accepts this tiny race.
                job?.AssignProcess(_process);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch
            {
                TryKillStartedProcess();
                throw;
            }
        }

        public void WriteToStdin(string value)
        {
            try
            {
                _process.StandardInput.Write(value);
                _process.StandardInput.Flush();
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }

        private void TryKillStartedProcess()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }
}
