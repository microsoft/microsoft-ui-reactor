#nullable enable

using Microsoft.UI.Reactor.VsExtension.Session;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    public sealed class ChildFailureDiagnosticsTests
    {
        /// <summary>
        /// Verbatim stderr from a packaged (MSIX) Reactor app launched by `dotnet watch run`
        /// without Microsoft.Windows.SDK.BuildTools.WinApp, captured while reproducing the
        /// failure. Kept real rather than synthesised so the matcher is exercised against the
        /// shape it actually has to recognise.
        /// </summary>
        private const string PackagedWithoutIdentityStderr =
            "Unhandled exception. System.Runtime.InteropServices.COMException (0x80040154): Class not registered (0x80040154 (REGDB_E_CLASSNOTREG))\n" +
            "   at System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(Int32 errorCode)\n" +
            "   at WinRT.ActivationFactory.Get(String typeName)\n" +
            "   at Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentInitializeOptions..ctor()\n" +
            "   at Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManagerCS.AutoInitialize.get_Options()\n" +
            "   at Microsoft.Windows.ApplicationModel.WindowsAppRuntime.Common.AutoInitialize.InitializeWindowsAppSDK()\n" +
            "   at .cctor()";

        [Fact]
        public void Describe_RecognisesPackagedAppLaunchedWithoutIdentity()
        {
            var result = ChildFailureDiagnostics.Describe(PackagedWithoutIdentityStderr);

            Assert.NotNull(result);
            Assert.Equal(ChildFailureDiagnostics.PackagedWithoutIdentityGuidance, result);
        }

        [Fact]
        public void Guidance_NamesBothRequiredProjectChanges()
        {
            var result = ChildFailureDiagnostics.Describe(PackagedWithoutIdentityStderr)!;

            // Either one alone leaves the preview broken: the package supplies identity, the
            // property supplies the inherited stdout the handshake is read from.
            Assert.Contains("Microsoft.Windows.SDK.BuildTools.WinApp", result);
            Assert.Contains("WinAppRunUseExecutionAlias", result);
            Assert.Contains("WindowsPackageType", result);
        }

        [Theory]
        // A bare CLASSNOTREG with no Windows App SDK frames is some other missing
        // registration; claiming it is a packaging problem would send the user the wrong way.
        [InlineData("System.Runtime.InteropServices.COMException (0x80040154): Class not registered (REGDB_E_CLASSNOTREG)\n   at Contoso.Widget.Activate()")]
        // Windows App SDK frames without CLASSNOTREG are a different failure entirely.
        [InlineData("Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManager threw InvalidOperationException")]
        [InlineData("MSBUILD : error MSB1009: Project file does not exist.")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Describe_ReturnsNullForUnrelatedFailures(string? stderr)
        {
            Assert.Null(ChildFailureDiagnostics.Describe(stderr));
        }

        [Fact]
        public void Describe_MatchesOnSymbolicNameWhenHresultIsAbsent()
        {
            var stderr =
                "COMException: Class not registered (REGDB_E_CLASSNOTREG)\n" +
                "   at Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentInitializeOptions..ctor()";

            Assert.NotNull(ChildFailureDiagnostics.Describe(stderr));
        }

        [Fact]
        public void Describe_MatchesOnHresultWhenSymbolicNameIsAbsent()
        {
            var stderr =
                "COMException (0x80040154): Class not registered\n" +
                "   at Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManagerCS.AutoInitialize.get_Options()";

            Assert.NotNull(ChildFailureDiagnostics.Describe(stderr));
        }
    }
}
