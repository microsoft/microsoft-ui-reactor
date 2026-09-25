#nullable enable

using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    /// <summary>
    /// Covers the environment the preview child is launched with. These are the knobs that
    /// decide whether a packaged (MSIX) project can complete the devtools handshake at all.
    /// </summary>
    public sealed class ReactorChildLauncherEnvironmentTests
    {
        private static ReactorChildLauncher.StartOptions Options() => new ReactorChildLauncher.StartOptions
        {
            CsprojPath = @"C:\src\App\App.csproj",
            DotnetPath = "dotnet",
            HostPid = 1234,
        };

        /// <summary>
        /// Without this the WinApp launcher activates a packaged app by AUMID, which is
        /// brokered and has no stdout, so CAPTURE_PORT is never observable and the preview
        /// times out with nothing in stderr to explain why.
        /// </summary>
        [Fact]
        public void StartInfo_RequestsExecutionAliasLaunchForPackagedProjects()
        {
            var info = ReactorChildLauncher.CreateStartInfoForTest(Options());

            Assert.Equal(
                "true",
                info.EnvironmentVariables[ReactorChildLauncher.WinAppRunUseExecutionAliasVariable]);
        }

        /// <summary>
        /// The property has to travel in the environment. `dotnet watch` rejects `-p` when
        /// `--project` is also present ("Cannot specify both '--project' and '-p' options"),
        /// and an environment property is the lowest-precedence MSBuild property, so a
        /// project that sets it explicitly still wins.
        /// </summary>
        [Fact]
        public void StartInfo_DoesNotPassTheAliasPropertyOnTheCommandLine()
        {
            var info = ReactorChildLauncher.CreateStartInfoForTest(Options());

            Assert.DoesNotContain(ReactorChildLauncher.WinAppRunUseExecutionAliasVariable, info.Arguments);
            Assert.DoesNotContain(" -p:", info.Arguments);
            Assert.DoesNotContain(" --property", info.Arguments);
        }

        [Fact]
        public void StartInfo_StillCarriesTheExistingWatchEnvironment()
        {
            var info = ReactorChildLauncher.CreateStartInfoForTest(Options());

            Assert.Equal("1", info.EnvironmentVariables["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"]);
            Assert.Equal("1", info.EnvironmentVariables["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"]);
            Assert.Equal("1", info.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"]);
        }

        [Fact]
        public void StartInfo_RedirectsStdoutSoTheHandshakeCanBeRead()
        {
            var info = ReactorChildLauncher.CreateStartInfoForTest(Options());

            Assert.True(info.RedirectStandardOutput);
            Assert.False(info.UseShellExecute);
        }
    }
}
