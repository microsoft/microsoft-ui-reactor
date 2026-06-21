#!/usr/bin/env pwsh
# Packs the native TableView control as a NuGet package and verifies a clean
# external consumer can reference it via a single <PackageReference> — proving the
# native Microsoft.UI.Xaml.Controls.Advanced.dll ships as a package asset rather
# than a committed binary.
#
#   ./pack-and-verify.ps1
#
# Produces (into a local feed) and verifies:
#   - Microsoft.UI.Reactor            (core, 0.0.0-local)   — the framework dependency
#   - Microsoft.UI.Reactor.TableView  (0.0.0-poc)           — control + projection (lib/)
#                                                             + native Advanced.dll
#                                                             (runtimes/win-x64/native/)
#                                                             + WinRT activation manifest (build/)
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$Feed = (Join-Path ([System.IO.Path]::GetTempPath()) 'tv-feed'),
    [string]$ConsumerDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'tv-consumer')
)
$ErrorActionPreference = 'Stop'
$demo = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $demo '..\..\..')).Path

Write-Host "[1/4] packing Microsoft.UI.Reactor (core) + Microsoft.UI.Reactor.TableView -> $Feed"
Remove-Item $Feed -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Feed | Out-Null
dotnet pack (Join-Path $repo 'src\Reactor\Reactor.csproj') -c $Configuration -p:Platform=$Platform -o $Feed | Out-Null
dotnet pack (Join-Path $demo 'Reactor.Controls.TableView\Reactor.Controls.TableView.csproj') -c $Configuration -p:Platform=$Platform -o $Feed | Out-Null
Get-ChildItem $Feed -Filter *.nupkg | ForEach-Object { Write-Host "        $($_.Name)" }

Write-Host "[2/4] scaffolding an external consumer (PackageReference only) -> $ConsumerDir"
Remove-Item $ConsumerDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ConsumerDir | Out-Null
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-tv-feed" value="$Feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $ConsumerDir 'nuget.config')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <Platforms>x64</Platforms>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <SelfContained>true</SelfContained>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  </PropertyGroup>
  <ItemGroup>
    <!-- ONE reference: the native TableView control as a package. -->
    <PackageReference Include="Microsoft.UI.Reactor.TableView" Version="0.0.0-poc" />
  </ItemGroup>
</Project>
'@ | Set-Content (Join-Path $ConsumerDir 'tv-consumer.csproj')
@'
using Microsoft.UI.Reactor;
ReactorApp.Run<ConsumerApp.App>("TableView from a NuGet package", width: 800, height: 560);
'@ | Set-Content (Join-Path $ConsumerDir 'Program.cs')
@'
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;

namespace ConsumerApp;

public sealed record Row(string Name, int Age, string City);

public sealed class App : Component
{
    static readonly Row[] Data =
    {
        new("Alice", 30, "Seattle"), new("Bob", 25, "Redmond"), new("Cara", 41, "Bellevue"),
    };

    public override Element Render() =>
        VStack(12,
            TextBlock("Native C++/WinRT TableView consumed purely from a NuGet package"),
            TableView(Data));
}
'@ | Set-Content (Join-Path $ConsumerDir 'App.cs')

Write-Host "[3/4] restoring + building the consumer (restores the package from the local feed)"
Push-Location $ConsumerDir
try {
    dotnet build tv-consumer.csproj -c $Configuration | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error 'consumer build FAILED'; exit 1 }
} finally { Pop-Location }

$out = Get-ChildItem (Join-Path $ConsumerDir 'bin') -Recurse -Filter 'tv-consumer.exe' | Select-Object -First 1
$nativeOk = Test-Path (Join-Path $out.DirectoryName 'Microsoft.UI.Xaml.Controls.Advanced.dll')
Write-Host "        consumer built; native Advanced.dll deployed from package: $nativeOk"

Write-Host "[4/4] headless activation self-test (native control through the package)"
$log = Join-Path $out.DirectoryName 'tvdemo-selftest.log'
Remove-Item $log -ErrorAction SilentlyContinue
$env:TVDEMO_SELFTEST = '1'
$p = Start-Process -FilePath $out.FullName -PassThru -WorkingDirectory $out.DirectoryName
$null = $p.WaitForExit(30000)
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
$env:TVDEMO_SELFTEST = $null
if ((Test-Path $log) -and ((Get-Content $log -Raw) -match 'PASS:.*native.*TableView.*activated')) {
    Write-Host "        PASS — native TableView activated via the packaged consumer." -ForegroundColor Green
    Get-Content $log
    exit 0
}
Write-Error 'PASS line not found — activation through the package not confirmed.'
exit 1
