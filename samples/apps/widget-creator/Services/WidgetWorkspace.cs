using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>The on-disk layout of one scaffolded widget project.</summary>
public sealed record WidgetProject(string Id, string Dir, string CsFile, string CsprojFile, string Rid, string Platform);

/// <summary>
/// Scaffolds a single-file Reactor app around generated source: writes
/// <c>widget.cs</c>, a self-contained <c>widget.csproj</c>, and a
/// <c>nuget.config</c> that resolves <c>Microsoft.UI.Reactor 0.0.0-local</c>
/// from this repo's <c>local-nupkgs</c> feed.
///
/// <para>Projects live under a <b>persistent</b> library root
/// (<c>%LOCALAPPDATA%\WidgetCreator\apps\&lt;id&gt;</c>) so generated apps — and
/// their published binaries — survive across sessions and can be re-run from the
/// gallery. A scaffold can target a fresh id or reuse an existing dir (the
/// build-and-fix loop rewrites <c>widget.cs</c> in place between attempts).</para>
/// </summary>
public sealed class WidgetWorkspace
{
    /// <summary>Persistent library root holding one folder per generated app.</summary>
    public string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetCreator", "apps");

    public static string Rid => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64",
    };

    public static string Platform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "ARM64",
        _ => "x64",
    };

    public static string LocalNupkgsFeed { get; } = ResolveLocalNupkgs();

    static string ResolveLocalNupkgs()
    {
        var env = Environment.GetEnvironmentVariable("WIDGET_CREATOR_NUPKGS");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "local-nupkgs");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return Path.Join(AppContext.BaseDirectory, "local-nupkgs");
    }

    public string NewId() => $"widget-{DateTime.Now:yyyyMMdd-HHmmss-fff}";

    /// <summary>
    /// Write the widget source. With <paramref name="existingId"/> the same
    /// folder is reused (only <c>widget.cs</c> is rewritten) — used by the fix
    /// loop. Otherwise a fresh id folder is created with all scaffolding.
    /// </summary>
    public async Task<WidgetProject> ScaffoldAsync(string source, string? existingId = null)
    {
        var id = existingId ?? NewId();
        var dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);

        var csFile = Path.Combine(dir, "widget.cs");
        var csprojFile = Path.Combine(dir, "widget.csproj");
        var nugetFile = Path.Combine(dir, "nuget.config");

        await File.WriteAllTextAsync(csFile, source).ConfigureAwait(false);
        if (NeedsCsprojRewrite(csprojFile))
        {
            await File.WriteAllTextAsync(csprojFile, CsprojTemplate()).ConfigureAwait(false);
            // The csproj declares RestorePackagesWithLockFile, so a widget scaffolded
            // by an older Widget Creator has a packages.lock.json pinned to the
            // previous package set. Drop it when the project changes so restore
            // re-evaluates the graph instead of resolving against a stale lock.
            var lockFile = Path.Combine(dir, "packages.lock.json");
            if (File.Exists(lockFile))
            {
                try { File.Delete(lockFile); }
                catch (IOException ex) { SessionLog.Write($"[Workspace] could not delete stale lock file: {ex.Message}"); }
            }
        }
        if (NeedsNugetRewrite(nugetFile))
            await File.WriteAllTextAsync(nugetFile, NugetConfigTemplate(LocalNupkgsFeed)).ConfigureAwait(false);

        SessionLog.Write($"[Workspace] scaffolded {dir} (rid={Rid}, platform={Platform}, reuse={existingId is not null})");
        return new WidgetProject(id, dir, csFile, csprojFile, Rid, Platform);
    }

    static bool NeedsCsprojRewrite(string csprojFile)
    {
        // Content comparison rather than a growing list of "does it contain X?"
        // probes: the csproj is entirely generated (the model only ever writes
        // widget.cs), so any drift from the current template — a stale Windows App
        // SDK pin, a missing hardening property — should simply be overwritten.
        // A list of feature probes has to be extended for every template change and
        // silently leaves old widgets on a broken csproj when someone forgets.
        if (!File.Exists(csprojFile))
            return true;

        return !string.Equals(File.ReadAllText(csprojFile), CsprojTemplate(), StringComparison.Ordinal);
    }

    /// <summary>
    /// H-1: (re)write the widget's <c>nuget.config</c> when it is missing or predates
    /// the <c>packageSourceMapping</c> hardening, so a widget's restore always pins
    /// each package ID to a single trusted feed (no dependency confusion).
    /// </summary>
    static bool NeedsNugetRewrite(string nugetFile)
    {
        if (!File.Exists(nugetFile))
            return true;
        return !File.ReadAllText(nugetFile).Contains("packageSourceMapping", StringComparison.Ordinal);
    }

    static string CsprojTemplate() =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
            <Platforms>x64;ARM64;X86</Platforms>
            <UseWinUI>true</UseWinUI>
            <WindowsPackageType>None</WindowsPackageType>
            <!-- Framework-dependent Windows App SDK: load the machine-installed
                 Windows App Runtime instead of copying the full ~220 MB native
                 runtime into every widget's publish dir. The unpackaged
                 auto-bootstrapper resolves the installed Microsoft.WindowsAppRuntime
                 at launch; the sandbox's AppContainer already has read+execute on
                 the framework package under C:\Program Files\WindowsApps (same way
                 it reaches C:\Program Files\dotnet). -->
            <WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>
            <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' And ('$(Platform)' == '' Or '$(Platform)' == 'AnyCPU' Or '$(Platform)' == 'Any CPU')">$(NETCoreSdkPortableRuntimeIdentifier)</RuntimeIdentifier>
            <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
            <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
            <RootNamespace>GeneratedWidget</RootNamespace>
            <AssemblyName>widget</AssemblyName>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <LangVersion>preview</LangVersion>
            <!-- H-1: harden the build of untrusted AI-generated source. A lock file
                 makes the restored dependency graph reproducible + tamper-evident,
                 and NuGetAudit surfaces known-vulnerable (transitive) packages. Feed
                 pinning is enforced by the generated nuget.config packageSourceMapping. -->
            <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
            <NuGetAudit>true</NuGetAudit>
            <NuGetAuditMode>all</NuGetAuditMode>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.UI.Reactor" Version="0.0.0-local" />
            <PackageReference Include="Microsoft.UI.Reactor.Advanced" Version="0.0.0-local" />
            <!-- Windows App SDK 2.0 split the metapackage into independently-versioned
                 sub-packages. A framework-dependent WinUI *executable* takes the lean
                 WinUI slice plus Runtime (Microsoft.WindowsAppSDK.Base.targets rejects a
                 framework-dependent app that references neither Runtime nor
                 WindowsAppSDKSelfContained) — mirroring the injection rule in this repo's
                 Directory.Build.targets. The versions are generated from
                 Directory.Build.props so they always match the Microsoft.UI.Reactor
                 0.0.0-local package the widget links against; a mismatch fails the build
                 in Microsoft.WindowsAppSDK.ComponentReference.targets. -->
            <PackageReference Include="Microsoft.WindowsAppSDK.WinUI" Version="{WidgetSdkVersions.WindowsAppSdkWinUI}" />
            <PackageReference Include="Microsoft.WindowsAppSDK.Runtime" Version="{WidgetSdkVersions.WindowsAppSdk}" />
          </ItemGroup>

        </Project>
        """;

    static string NugetConfigTemplate(string feed) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="reactor-local" value="{feed}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </packageSources>
          <disabledPackageSources>
            <clear />
          </disabledPackageSources>
          <!-- H-1: pin each package ID to exactly one feed so a malicious or
               typosquatted package on nuget.org cannot shadow the local Reactor
               packages (dependency-confusion defense). All local-feed packages are
               Microsoft.UI.Reactor*; everything else (WindowsAppSDK + transitive
               deps) comes only from nuget.org. -->
          <packageSourceMapping>
            <packageSource key="reactor-local">
              <package pattern="Microsoft.UI.Reactor*" />
            </packageSource>
            <packageSource key="nuget.org">
              <package pattern="*" />
            </packageSource>
          </packageSourceMapping>
        </configuration>
        """;
}
