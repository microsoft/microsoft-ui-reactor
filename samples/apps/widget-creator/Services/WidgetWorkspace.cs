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
        return @"C:\Users\andersonch\Code\reactor3\local-nupkgs";
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
            await File.WriteAllTextAsync(csprojFile, CsprojTemplate()).ConfigureAwait(false);
        if (!File.Exists(nugetFile))
            await File.WriteAllTextAsync(nugetFile, NugetConfigTemplate(LocalNupkgsFeed)).ConfigureAwait(false);

        SessionLog.Write($"[Workspace] scaffolded {dir} (rid={Rid}, platform={Platform}, reuse={existingId is not null})");
        return new WidgetProject(id, dir, csFile, csprojFile, Rid, Platform);
    }

    static bool NeedsCsprojRewrite(string csprojFile)
    {
        if (!File.Exists(csprojFile))
            return true;

        var text = File.ReadAllText(csprojFile);
        return text.Contains("<Compile Remove=\"**\\*.cs\" />", StringComparison.Ordinal) ||
            !text.Contains("<Platforms>x64;ARM64;X86</Platforms>", StringComparison.Ordinal) ||
            text.Contains("<RuntimeIdentifier>win-", StringComparison.Ordinal) ||
            !text.Contains("<TargetPlatformMinVersion>", StringComparison.Ordinal) ||
            !text.Contains("<SupportedOSPlatformVersion>", StringComparison.Ordinal) ||
            !text.Contains("Microsoft.UI.Reactor.Advanced", StringComparison.Ordinal);
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
            <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
            <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' And ('$(Platform)' == '' Or '$(Platform)' == 'AnyCPU' Or '$(Platform)' == 'Any CPU')">$(NETCoreSdkPortableRuntimeIdentifier)</RuntimeIdentifier>
            <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
            <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
            <RootNamespace>GeneratedWidget</RootNamespace>
            <AssemblyName>widget</AssemblyName>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <LangVersion>preview</LangVersion>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.UI.Reactor" Version="0.0.0-local" />
            <PackageReference Include="Microsoft.UI.Reactor.Advanced" Version="0.0.0-local" />
            <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.1" />
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
        </configuration>
        """;
}
