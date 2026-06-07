#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    [Collection("JobObjectCounterTests")]
    public sealed class ReactorChildLauncherTests
    {
        [Fact]
        public void Launcher_BadCsproj_DoesNotLeakJobHandle()
        {
            JobObject.ResetAliveCountForTests();

            Assert.Throws<ArgumentException>(() => new ReactorChildLauncher(new ReactorChildLauncher.StartOptions { CsprojPath = string.Empty }));

            Assert.Equal(0, JobObject.AliveCountForTests);
        }

        [Fact]
        public void Launcher_PortAndTokenSequence_ParsesPair_BumpsSession()
        {
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                NewSessionEventArgs? observed = null;
                launcher.NewSession += (_, args) => observed = args;

                io.PushStdout("CAPTURE_PORT=1234");
                io.PushStdout("CAPTURE_TOKEN=abc");

                Assert.NotNull(observed);
                Assert.Equal(1, observed!.SessionId);
                Assert.Equal(1234, observed.Port);
                Assert.Equal("abc", observed.Token);
            }
        }

        [Fact]
        public void Launcher_PortBeforeToken_Pairs()
        {
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                NewSessionEventArgs? observed = null;
                launcher.NewSession += (_, args) => observed = args;

                io.PushStdout("CAPTURE_PORT=1234");
                io.PushStdout("CAPTURE_TOKEN=abc");

                Assert.NotNull(observed);
                Assert.Equal(1234, observed!.Port);
                Assert.Equal("abc", observed.Token);
            }
        }

        [Fact]
        public void Launcher_TokenBeforePort_Pairs()
        {
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                NewSessionEventArgs? observed = null;
                launcher.NewSession += (_, args) => observed = args;

                io.PushStdout("CAPTURE_TOKEN=abc");
                io.PushStdout("CAPTURE_PORT=1234");

                Assert.NotNull(observed);
                Assert.Equal(1, observed!.SessionId);
                Assert.Equal(1234, observed.Port);
                Assert.Equal("abc", observed.Token);
            }
        }

        [Fact]
        public void Launcher_NewPairAfterPrior_RaisesSecondSession()
        {
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                var observed = new List<NewSessionEventArgs>();
                launcher.NewSession += (_, args) => observed.Add(args);

                io.PushStdout("CAPTURE_PORT=1234");
                io.PushStdout("CAPTURE_TOKEN=abc");
                io.PushStdout("CAPTURE_PORT=5678");
                io.PushStdout("CAPTURE_TOKEN=def");

                Assert.Equal(2, observed.Count);
                Assert.Equal(1, observed[0].SessionId);
                Assert.Equal(1234, observed[0].Port);
                Assert.Equal("abc", observed[0].Token);
                Assert.Equal(2, observed[1].SessionId);
                Assert.Equal(5678, observed[1].Port);
                Assert.Equal("def", observed[1].Token);
            }
        }

        [Fact]
        public void Launcher_RudeEditPrompt_AutoAnswersYes()
        {
            var io = new TestChildProcessIo();
            using (new ReactorChildLauncher(io))
            {
                io.PushStdout("Do you want to restart your app? [y/n]");

                Assert.Equal("y\n", io.WrittenToStdin);
            }
        }

        [Fact]
        public void Launcher_StderrLines_PropagateThroughEvent()
        {
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                string? observed = null;
                launcher.StderrLine += (_, line) => observed = line;

                io.PushStderr("CS1002");

                Assert.Equal("CS1002", observed);
                Assert.Contains("CS1002", launcher.LastStderr);
            }
        }

        [Fact]
        public void Launcher_StdoutLines_PropagateThroughEvent()
        {
            // Regression: pre-fix, OnStdoutLine only inspected the line for
            // CAPTURE_PORT/CAPTURE_TOKEN and silently dropped everything else
            // — so [devtools] dispatch lines and MSBuild progress never made
            // it to the VS Output pane. That hid the symptom of the embed
            // P/Invoke crash for hours. Verify all stdout lines reach the
            // StdoutLine event so subscribers (EmbedSession.Log) can see them.
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                var observed = new List<string>();
                launcher.StdoutLine += (_, line) => observed.Add(line);

                io.PushStdout("[devtools] Previewing DemoApp");
                io.PushStdout("[devtools] VS Code mode enabled");
                io.PushStdout("CAPTURE_PORT=1234");
                io.PushStdout("CAPTURE_TOKEN=abc");

                // ALL lines forwarded — including the CAPTURE_ ones. Suppression
                // of handshake lines is the consumer's choice (EmbedSession), not
                // the launcher's, so they appear here too.
                Assert.Equal(
                    new[]
                    {
                        "[devtools] Previewing DemoApp",
                        "[devtools] VS Code mode enabled",
                        "CAPTURE_PORT=1234",
                        "CAPTURE_TOKEN=abc",
                    },
                    observed.ToArray());
            }
        }

        [Fact]
        public void Launcher_StdoutForwarding_DoesNotBreakHandshakePatternMatching()
        {
            // Forward-before-match: the forwarding hook must not consume or
            // mutate the line passed to the pattern matcher. NewSession must
            // still fire on the canonical CAPTURE_PORT/CAPTURE_TOKEN pair.
            var io = new TestChildProcessIo();
            using (var launcher = new ReactorChildLauncher(io))
            {
                NewSessionEventArgs? observed = null;
                var stdoutLines = new List<string>();
                launcher.NewSession += (_, args) => observed = args;
                launcher.StdoutLine += (_, line) => stdoutLines.Add(line);

                io.PushStdout("CAPTURE_PORT=5678");
                io.PushStdout("CAPTURE_TOKEN=token-xyz");

                Assert.NotNull(observed);
                Assert.Equal(5678, observed!.Port);
                Assert.Equal("token-xyz", observed.Token);
                Assert.Equal(2, stdoutLines.Count);
            }
        }

        private sealed class TestChildProcessIo : IChildProcessIo
        {
            private readonly StringBuilder _stdin = new StringBuilder();

            public event Action<string>? StdoutLine;

            public event Action<string>? StderrLine;

            public event Action? Exited;

            public string WrittenToStdin => _stdin.ToString();

            public void Start(JobObject? job)
            {
            }

            public void WriteToStdin(string value)
            {
                _stdin.Append(value);
            }

            public void PushStdout(string line)
            {
                StdoutLine?.Invoke(line);
            }

            public void PushStderr(string line)
            {
                StderrLine?.Invoke(line);
            }

            public void PushExited()
            {
                Exited?.Invoke();
            }

            public void Dispose()
            {
            }
        }
    }
}
