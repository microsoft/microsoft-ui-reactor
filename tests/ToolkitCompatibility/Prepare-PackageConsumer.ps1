[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
if ($destinationPath.StartsWith($repo.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package consumer must be outside the repository so repository MSBuild imports cannot mask shipping defects.'
}
if (Test-Path $destinationPath) { throw 'Choose a new, empty destination.' }
$revision = (& git -C $repo rev-parse HEAD).Trim()
$version = "0.0.0-compat.$($revision.Substring(0, 12)).$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$feed = Join-Path $destinationPath 'feed'
$consumer = Join-Path $destinationPath 'consumer'
$sample = Join-Path $destinationPath 'sample'
foreach ($directory in @($feed, $consumer, $sample)) {
    New-Item $directory -ItemType Directory | Out-Null
}
$reactor = Join-Path $repo 'src\Reactor\Reactor.csproj'
& dotnet restore $reactor -p:Platform=x64 -p:SkipSignaturesGen=true --verbosity quiet *> (Join-Path $destinationPath 'restore.log')
if ($LASTEXITCODE) { throw 'Reactor restore failed; see restore.log.' }
& dotnet pack $reactor --no-restore -c Release -p:Platform=x64 -p:SkipSignaturesGen=true `
    "-p:Version=$version" -o $feed -m:1 -nr:false --verbosity quiet *> (Join-Path $destinationPath 'pack.log')
if ($LASTEXITCODE) { throw 'Reactor pack failed; see pack.log.' }

$packagePath = Join-Path $feed "Microsoft.UI.Reactor.$version.nupkg"
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in @(
        'analyzers/dotnet/cs/Reactor.Wrappers.Generator.dll',
        'lib/net10.0-windows10.0.22621/Reactor.Wrappers.Abstractions.dll',
        'buildTransitive/Microsoft.UI.Reactor.targets')) {
        if ($required -notin $entries) { throw "Shipping package is missing $required" }
    }
    if (-not ($entries | Where-Object { $_ -match '\.xbf$' })) { throw 'No shipped XBF resource.' }
    $assemblyEntries = @($archive.Entries | Where-Object FullName -Match '^lib/[^/]+/Reactor\.dll$')
    if ($assemblyEntries.Count -ne 1) { throw 'Expected one shipped Reactor runtime assembly.' }
    $assemblyStream = $assemblyEntries[0].Open()
    try { $reactorAssemblyHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assemblyStream)) }
    finally { $assemblyStream.Dispose() }
    $entries | Set-Content (Join-Path $destinationPath 'package-entries.txt')
} finally { $archive.Dispose() }

Copy-Item (Join-Path $PSScriptRoot '*.cs') $consumer
Copy-Item (Join-Path $repo 'samples\apps\wct-controls\*.cs') $sample
Copy-Item (Join-Path $repo 'global.json') $destinationPath
'<Project />' | Set-Content (Join-Path $destinationPath 'Directory.Build.props')
'<Project />' | Set-Content (Join-Path $destinationPath 'Directory.Build.targets')
'<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>' |
    Set-Content (Join-Path $destinationPath 'Directory.Packages.props')
Copy-Item (Join-Path $repo 'tests\Reactor.PackagedTests.Host\Images') (Join-Path $consumer 'Images') -Recurse

[xml]$project = Get-Content (Join-Path $PSScriptRoot 'ToolkitCompatibility.csproj') -Raw
foreach ($reference in @($project.SelectNodes('//ProjectReference'))) {
    $null = $reference.ParentNode.RemoveChild($reference)
}
foreach ($reference in @($project.SelectNodes('//PackageReference'))) {
    if ($reference.GetAttribute('Include').StartsWith('CommunityToolkit.')) {
        $reference.SetAttribute('Version', '8.3.260402-preview2')
    }
}
$properties = $project.CreateElement('PropertyGroup')
foreach ($entry in @{
    ReactorPackageVersion = $version
    SampleMappings = '..\sample\WctControls.cs'
    SamplePages = '..\sample\SamplePages.cs'
    SampleCameraLifetime = '..\sample\CameraPreviewLifetime.cs'
    LangVersion = 'preview'
    WindowsSdkPackageVersion = '10.0.22621.57'
    IsAotCompatible = 'true'
    TrimmerSingleWarn = 'false'
    TreatWarningsAsErrors = 'true'
    EnforceCodeStyleInBuild = 'true'
    ManagePackageVersionsCentrally = 'false'
}.GetEnumerator()) {
    $element = $project.CreateElement($entry.Key)
    $element.InnerText = $entry.Value
    if ($entry.Key -eq 'TreatWarningsAsErrors') {
        $element.SetAttribute('Condition', "'`$(Configuration)' == 'Release'")
    }
    $null = $properties.AppendChild($element)
}
$null = $project.Project.AppendChild($properties)
$runtimeReferences = $project.CreateElement('ItemGroup')
$sdkReference = $project.CreateElement('PackageReference')
$sdkReference.SetAttribute('Include', 'Microsoft.WindowsAppSDK')
[xml]$repositoryProperties = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
$sdkReference.SetAttribute('Version',
    $repositoryProperties.SelectSingleNode('//WindowsAppSDKVersion').InnerText)
$null = $runtimeReferences.AppendChild($sdkReference)
$null = $project.Project.AppendChild($runtimeReferences)
foreach ($content in $project.SelectNodes('//Content')) {
    if ($content.GetAttribute('Include').Contains('Reactor.PackagedTests.Host')) {
        $content.SetAttribute('Include', 'Images\*.png')
    }
}
$projectPath = Join-Path $consumer 'ToolkitCompatibility.csproj'
$project.Save($projectPath)

$nuget = [xml]'<configuration><packageSources><clear /></packageSources></configuration>'
foreach ($source in @{ 'reactor-local' = $feed; 'nuget.org' = 'https://api.nuget.org/v3/index.json' }.GetEnumerator()) {
    $node = $nuget.CreateElement('add')
    $node.SetAttribute('key', $source.Key)
    $node.SetAttribute('value', $source.Value)
    $null = $nuget.configuration.packageSources.AppendChild($node)
}
$nuget.Save((Join-Path $destinationPath 'NuGet.Config'))
@{
    revision = $revision
    dirty = @(& git -C $repo status --short)
    packageVersion = $version
    packageSha256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash
    reactorAssemblySha256 = $reactorAssemblyHash
    sampleMappingsSha256 = (Get-FileHash (Join-Path $sample 'WctControls.cs') -Algorithm SHA256).Hash
    project = $projectPath
    files = @(Get-ChildItem $consumer, $sample -File -Recurse | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($destinationPath, $_.FullName)
           sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    })
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $destinationPath 'consumer.json')
Write-Output $projectPath
