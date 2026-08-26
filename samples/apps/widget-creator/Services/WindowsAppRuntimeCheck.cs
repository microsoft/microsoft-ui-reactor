using System;
using System.Runtime.InteropServices;
using Windows.Management.Deployment;

// The Windows App SDK's own generated version info (compiled in via
// WindowsAppSdkIncludeVersionInfo). Aliased because its namespace segments —
// Runtime.Version, Runtime.Packages.Framework — collide with System.Version and
// read badly fully-qualified.
using SdkRelease = Microsoft.WindowsAppSDK.Release;
using SdkRuntimeFramework = Microsoft.WindowsAppSDK.Runtime.Packages.Framework;
using SdkRuntimeVersion = Microsoft.WindowsAppSDK.Runtime.Version;

namespace WidgetCreator.Services;

/// <summary>Outcome of probing the machine for the Windows App Runtime.</summary>
public enum WindowsAppRuntimeStatus
{
    /// <summary>A framework package at or above the required version is installed.</summary>
    Ok,

    /// <summary>The runtime is installed but older than the version widgets are built against.</summary>
    Outdated,

    /// <summary>No framework package for this architecture is registered for the user.</summary>
    Missing,

    /// <summary>The probe itself could not run; treat as "unknown", never as a failure.</summary>
    Unknown,
}

/// <summary>What the probe found, plus everything the UI needs to explain and fix it.</summary>
public sealed record WindowsAppRuntimeInfo(
    WindowsAppRuntimeStatus Status,
    string PackageFamilyName,
    Version RequiredVersion,
    Version? InstalledVersion,
    string Architecture,
    string InstallerUrl,
    string DownloadsUrl,
    string WingetCommand,
    string Message)
{
    /// <summary>True when generated widgets can actually launch on this machine.</summary>
    public bool IsSatisfied => Status is WindowsAppRuntimeStatus.Ok or WindowsAppRuntimeStatus.Unknown;
}

/// <summary>
/// Checks that the machine-wide <b>Windows App Runtime</b> a generated widget needs
/// is installed.
///
/// <para>Generated widgets are framework-dependent
/// (<c>WindowsAppSDKSelfContained=false</c>) so they do not carry the ~220 MB native
/// Windows App SDK runtime in their publish dir — they bind the runtime registered on
/// the machine at launch. Without it a widget builds fine and then dies at startup
/// inside the MXC sandbox, where the failure is close to undiagnosable. Probing up
/// front turns that into an actionable prompt.</para>
///
/// <para>The required identity is not hardcoded: it comes from the Windows App SDK's
/// own generated <c>WindowsAppSDK-VersionInfo.cs</c> (compiled in via
/// <c>WindowsAppSdkIncludeVersionInfo</c>), so it tracks
/// <c>WindowsAppSDKVersion</c> in <c>Directory.Build.props</c> automatically.</para>
/// </summary>
public static class WindowsAppRuntimeCheck
{
    /// <summary>
    /// Framework package family the SDK we build against binds, e.g.
    /// <c>Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe</c>.
    /// <c>WIDGET_CREATOR_WINAPPRUNTIME_FAMILY</c> overrides it — pointing it at a
    /// family that cannot exist is how the not-installed path (and its banner) gets
    /// exercised on a machine that already has the runtime.
    /// </summary>
    public static string PackageFamilyName
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("WIDGET_CREATOR_WINAPPRUNTIME_FAMILY");
            return string.IsNullOrWhiteSpace(over) ? SdkRuntimeFramework.PackageFamilyName : over.Trim();
        }
    }

    /// <summary>Learn page listing every Windows App SDK download.</summary>
    public const string DownloadsUrl = "https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads";

    /// <summary>
    /// Minimum runtime version, from the SDK's own version info (e.g. <c>2.1.3.0</c>).
    /// <c>WIDGET_CREATOR_MIN_WINAPPRUNTIME</c> overrides it — used to exercise the
    /// missing/outdated paths without uninstalling anything.
    /// </summary>
    public static Version RequiredVersion
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("WIDGET_CREATOR_MIN_WINAPPRUNTIME");
            if (!string.IsNullOrWhiteSpace(over) && Version.TryParse(over.Trim(), out var parsed))
                return parsed;

            return new Version(
                SdkRuntimeVersion.Major,
                SdkRuntimeVersion.Minor,
                SdkRuntimeVersion.Build,
                SdkRuntimeVersion.Revision);
        }
    }

    /// <summary>
    /// Direct installer for the SDK's release band and this machine's architecture —
    /// e.g. <c>https://aka.ms/windowsappsdk/2.1/latest/windowsappruntimeinstall-x64.exe</c>.
    /// Opening it in a browser downloads the runtime installer.
    /// </summary>
    public static string InstallerUrl =>
        $"https://aka.ms/windowsappsdk/{SdkRelease.Major}.{SdkRelease.Minor}" +
        $"/latest/windowsappruntimeinstall-{ArchitectureMoniker}.exe";

    /// <summary>
    /// winget id for the runtime the SDK binds, e.g. <c>Microsoft.WindowsAppRuntime.2</c>.
    /// <para>The shape differs by generation, and getting it wrong points the user at a
    /// package that either does not exist or cannot load these widgets. 1.x shipped a
    /// side-by-side framework package per minor (<c>...WindowsAppRuntime.1.7</c>), but 2.x
    /// ships one package for the whole major, serviced in place — which is why
    /// <see cref="PackageFamilyName"/> resolves to <c>Microsoft.WindowsAppRuntime.2_...</c>
    /// and not <c>.2.1_...</c>. There is no <c>Microsoft.WindowsAppRuntime.2.1</c> winget
    /// package, and <c>...WindowsAppRuntime.2.0</c> is a separate one pinned to 2.0.x.</para>
    /// <para>Same rule as <c>tools/WindowsAppRuntimeId.ps1</c>, which bootstrap.ps1 uses.
    /// Note <see cref="InstallerUrl"/> is deliberately NOT this shape: the aka.ms
    /// release-band URL really is major.minor.</para>
    /// </summary>
    public static string WingetCommand =>
        $"winget install {WingetPackageId}";

    /// <summary>Package id used by <see cref="WingetCommand"/>.</summary>
    public static string WingetPackageId =>
        SdkRelease.Major >= 2
            ? $"Microsoft.WindowsAppRuntime.{SdkRelease.Major}"
            : $"Microsoft.WindowsAppRuntime.{SdkRelease.Major}.{SdkRelease.Minor}";

    /// <summary>Architecture moniker used by the installer URLs (<c>x64</c> / <c>arm64</c> / <c>x86</c>).</summary>
    public static string ArchitectureMoniker => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64",
    };

    static Windows.System.ProcessorArchitecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => Windows.System.ProcessorArchitecture.Arm64,
        Architecture.X86 => Windows.System.ProcessorArchitecture.X86,
        _ => Windows.System.ProcessorArchitecture.X64,
    };

    /// <summary>
    /// Probe the packages registered for the current user. Never throws: a probe
    /// that cannot run reports <see cref="WindowsAppRuntimeStatus.Unknown"/> so a
    /// diagnostic can never block generation.
    /// </summary>
    public static WindowsAppRuntimeInfo Detect()
    {
        var required = RequiredVersion;
        var family = PackageFamilyName;
        var arch = ArchitectureMoniker;

        WindowsAppRuntimeInfo Result(WindowsAppRuntimeStatus status, Version? installed, string message) =>
            new(status, family, required, installed, arch, InstallerUrl, DownloadsUrl, WingetCommand, message);

        Version? best = null;
        try
        {
            var manager = new PackageManager();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                var id = package.Id;
                if (!string.Equals(id.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (id.Architecture != ProcessArchitecture)
                    continue;

                var v = new Version(id.Version.Major, id.Version.Minor, id.Version.Build, id.Version.Revision);
                if (best is null || v > best)
                    best = v;
            }
        }
        catch (Exception ex)
        {
            // FindPackagesForUser can fail on locked-down or unusual configurations.
            // Widget Creator is itself a framework-dependent WinUI app, so if we got
            // this far a runtime clearly loaded — refusing to guess is the safe answer.
            SessionLog.Write($"[WinAppRuntime] probe failed ({ex.GetType().Name}: {ex.Message}); reporting Unknown");
            return Result(WindowsAppRuntimeStatus.Unknown, null,
                $"Could not check for the Windows App Runtime ({ex.Message}). Widgets may fail to launch.");
        }

        if (best is null)
        {
            SessionLog.Write($"[WinAppRuntime] MISSING {family} ({arch}); widgets need >= {required}");
            return Result(WindowsAppRuntimeStatus.Missing, null,
                $"Windows App Runtime {required} ({arch}) is not installed. Generated widgets are "
                + "framework-dependent and will not launch without it.");
        }

        if (best < required)
        {
            SessionLog.Write($"[WinAppRuntime] OUTDATED {family} ({arch}) installed={best} required>={required}");
            return Result(WindowsAppRuntimeStatus.Outdated, best,
                $"Windows App Runtime {best} ({arch}) is older than the {required} that generated "
                + "widgets are built against. They may fail to launch.");
        }

        SessionLog.Write($"[WinAppRuntime] OK {family} ({arch}) installed={best} required>={required}");
        return Result(WindowsAppRuntimeStatus.Ok, best,
            $"Windows App Runtime {best} ({arch}) installed.");
    }
}
