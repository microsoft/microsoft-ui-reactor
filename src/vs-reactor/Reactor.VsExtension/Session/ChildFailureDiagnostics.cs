#nullable enable

using System;

namespace Microsoft.UI.Reactor.VsExtension.Session
{
    /// <summary>
    /// Translates known child-process failure signatures into actionable guidance.
    /// Pure string analysis so it is unit-testable without a VS shell or a live child.
    /// </summary>
    internal static class ChildFailureDiagnostics
    {
        /// <summary>
        /// Returns guidance for a recognised failure in <paramref name="stderr"/>, or
        /// <see langword="null"/> when nothing specific is recognised (the caller then falls
        /// back to its generic cause list).
        /// </summary>
        public static string? Describe(string? stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
            {
                return null;
            }

            return IsPackagedWithoutIdentity(stderr!) ? PackagedWithoutIdentityGuidance : null;
        }

        /// <summary>
        /// A packaged (MSIX) project launched by <c>dotnet watch run</c> starts the built
        /// .exe directly, with no package identity. The Windows App SDK's deployment
        /// auto-initializer then fails to activate its WinRT types, because those live in the
        /// Windows App Runtime framework package and are resolved through the process's
        /// package graph — which an identity-less process does not have.
        /// </summary>
        /// <remarks>
        /// Both halves of the signature are required. <c>REGDB_E_CLASSNOTREG</c> alone is a
        /// generic COM error that any missing registration produces, so matching on it by
        /// itself would mislabel unrelated failures.
        /// </remarks>
        private static bool IsPackagedWithoutIdentity(string stderr)
        {
            var classNotRegistered =
                Contains(stderr, "REGDB_E_CLASSNOTREG") ||
                Contains(stderr, "0x80040154");

            if (!classNotRegistered)
            {
                return false;
            }

            return Contains(stderr, "WindowsAppRuntime") ||
                   Contains(stderr, "DeploymentManager") ||
                   Contains(stderr, "Microsoft.Windows.ApplicationModel");
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal const string PackagedWithoutIdentityGuidance =
            "This looks like a packaged (MSIX) project that was launched without package identity.\r\n" +
            "\r\n" +
            "The preview runs your app with `dotnet watch run`, which starts the built .exe directly.\r\n" +
            "A packaged app needs two things that a bare .exe launch does not provide: MSIX identity,\r\n" +
            "so the Windows App SDK deployment initializer can resolve its WinRT types, and an\r\n" +
            "inherited stdout, which is how the devtools handshake reports CAPTURE_PORT.\r\n" +
            "\r\n" +
            "Add both of these to the project and preview again:\r\n" +
            "\r\n" +
            "  <PropertyGroup>\r\n" +
            "    <WinAppRunUseExecutionAlias>true</WinAppRunUseExecutionAlias>\r\n" +
            "  </PropertyGroup>\r\n" +
            "  <ItemGroup>\r\n" +
            "    <PackageReference Include=\"Microsoft.Windows.SDK.BuildTools.WinApp\" Version=\"*\" />\r\n" +
            "  </ItemGroup>\r\n" +
            "\r\n" +
            "The package gives `dotnet watch run` a launcher that registers a debug identity.\r\n" +
            "WinAppRunUseExecutionAlias makes that launcher use an execution alias, which inherits\r\n" +
            "stdout; the default AUMID activation is brokered and has no stdout at all, so the\r\n" +
            "handshake could never be read even though the app itself would run.\r\n" +
            "\r\n" +
            "To preview the app unpackaged instead, set <WindowsPackageType>None</WindowsPackageType>.";
    }
}
