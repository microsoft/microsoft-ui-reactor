<#
.SYNOPSIS
    Build + pack the shipped Reactor NuGet packages in one source tree and emit a
    JSON list of artifact sizes for the build-metrics PR comment.

.DESCRIPTION
    Runs `dotnet pack -c Release` for each tracked package, then measures:
      * the compressed .nupkg (what a consumer downloads), and
      * every uncompressed DLL shipped inside it (all lib/<tfm>/*.dll and
        analyzers/**/*.dll, excluding satellite *.resources.dll) — the real "did
        our code grow" signal. New DLLs appear automatically; nothing is
        hard-coded per package.

    The output JSON is an array of measurement objects consumed by
    BuildMetricsLib.ps1's Format-BuildMetricsComment:
      { "Key": "...", "Label": "...", "Group": "...", "Bytes": <int|null> }
    with keys 'nupkg.<PkgKey>' for a package and 'asm|<PkgKey>|<Dll>' per DLL.

    A fixed -PackageVersion is used for both sides (the PR's base branch and head)
    so the version string embedded in the .nuspec is identical and never
    contributes to the diff — the only size delta is real code/content change.

    Per-package failures are non-fatal: the package's rows are emitted with a
    null size (rendered as n/a) and the run continues, so one broken pack never
    blanks the whole report.

    Run locally (measures the current tree):
      pwsh tests/build_metrics/ci/Measure-BuildMetrics.ps1 `
        -Root . -OutFile head.sizes.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Configuration = 'Release',
    [string]$PackageVersion = '0.0.0-buildmetrics',
    [string]$PackOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path -LiteralPath $Root).Path

# Ensure the -OutFile parent exists before we Set-Content into it at the end — the
# caller often points it at a not-yet-created folder (e.g. the workflow's
# build-metrics-out/), and Set-Content does not create intermediate directories.
$outParent = Split-Path -Parent $OutFile
if ($outParent -and -not (Test-Path -LiteralPath $outParent)) {
    New-Item -ItemType Directory -Force -Path $outParent | Out-Null
}

if (-not $PackOutput) {
    # Derive a stable, tree-specific temp folder so a base + head run in the same
    # job never share (or clobber) each other's pack output.
    $hash = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA1]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($Root))).Replace('-', '').Substring(0, 12)
    $PackOutput = Join-Path ([System.IO.Path]::GetTempPath()) "build-metrics-$hash"
}

# Safety guard: we Remove-Item -Recurse -Force this path below. -PackOutput is
# caller-supplied, so refuse obviously-unsafe targets (a filesystem/drive root, or
# the current/root of the source tree) before deleting anything.
$packFull = [System.IO.Path]::GetFullPath($PackOutput)
$pathRoot = [System.IO.Path]::GetPathRoot($packFull)
$rootFull = [System.IO.Path]::GetFullPath($Root)
if ($packFull -eq $pathRoot -or
    $packFull -eq $rootFull -or
    $packFull -eq [System.IO.Path]::GetFullPath((Get-Location).Path) -or
    ($packFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Length -le $pathRoot.Length)) {
    throw "Refusing to use an unsafe -PackOutput '$packFull' (it is a root or the source tree). Pass a dedicated subfolder."
}

if (Test-Path -LiteralPath $packFull) {
    Remove-Item -LiteralPath $packFull -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packFull | Out-Null
$PackOutput = $packFull

# ── Tracked packages ─────────────────────────────────────────────────────────
# PkgKey is a stable, alphanumeric identifier (NOT a filename — the .nupkg carries
# a version), so base and head rows line up even when their MinVer versions differ
# and so it can never contain the '|' delimiter used in per-DLL row keys. Advanced +
# Devtools pack from their AnyCPU output (mirrors release.yml), which the standalone
# `dotnet pack` produces by default; passing it explicitly keeps parity if the
# csproj default ever changes. Every shipped DLL inside each package is enumerated
# automatically (see Get-NupkgAssemblies), so no per-assembly list lives here.
$targets = @(
    [pscustomobject]@{
        PkgKey = 'Reactor'; PkgLabel = 'Microsoft.UI.Reactor.nupkg'
        Project = 'src/Reactor/Reactor.csproj'
        PackageId = 'Microsoft.UI.Reactor'
        ExtraArgs = @()
    }
    [pscustomobject]@{
        PkgKey = 'Advanced'; PkgLabel = 'Microsoft.UI.Reactor.Advanced.nupkg'
        Project = 'src/Reactor.Advanced/Reactor.Advanced.csproj'
        PackageId = 'Microsoft.UI.Reactor.Advanced'
        ExtraArgs = @('-p:Platform=AnyCPU')
    }
    [pscustomobject]@{
        PkgKey = 'Devtools'; PkgLabel = 'Microsoft.UI.Reactor.Devtools.nupkg'
        Project = 'src/Reactor.Devtools/Reactor.Devtools.csproj'
        PackageId = 'Microsoft.UI.Reactor.Devtools'
        ExtraArgs = @('-p:Platform=AnyCPU')
    }
)

$packageGroup = 'Packages (compressed .nupkg)'

function Select-LatestNupkg {
    <#
    .SYNOPSIS
        Newest non-symbols .nupkg for exactly $PackageId in $PackOutput, or $null.
    .DESCRIPTION
        Matches `<PackageId>.<version>.nupkg` where the version segment starts with
        a digit, so `Microsoft.UI.Reactor` never picks up its own siblings
        `Microsoft.UI.Reactor.Advanced` / `.Devtools` that share the same dir. The
        `.symbols.nupkg` sidecar is excluded; ties broken by newest write time.
    #>
    param([string]$PackOutput, [string]$PackageId)
    $pattern = '^' + [regex]::Escape($PackageId) + '\.\d[^\\/]*\.nupkg$'
    Get-ChildItem -LiteralPath $PackOutput -Filter '*.nupkg' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $pattern -and $_.Name -notlike '*.symbols.nupkg' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Get-NupkgAssemblies {
    <#
    .SYNOPSIS
        Enumerate every shipped DLL in a .nupkg — one entry per assembly basename —
        without extracting the archive. Returns objects { Name; Bytes }.
    .DESCRIPTION
        Includes every zip entry whose forward-slash path matches
        ^(lib|analyzers)/.+\.dll$ — the framework assemblies under lib/<tfm>/ and
        the analyzer / source-generator DLLs shipped under analyzers/dotnet/cs/ —
        while EXCLUDING satellite resource assemblies (*.resources.dll). When the
        same basename appears under multiple TFMs the largest uncompressed length
        is reported. Returns an empty array if the archive has no such entries or
        cannot be read.
    #>
    param([string]$NupkgPath)
    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($NupkgPath)
        $bySize = @{}
        foreach ($entry in $zip.Entries) {
            # NuGet asset paths use forward slashes regardless of OS.
            if ($entry.FullName -notmatch '^(lib|analyzers)/.+\.dll$') { continue }
            if ($entry.FullName -match '\.resources\.dll$') { continue }
            $name = [System.IO.Path]::GetFileName($entry.FullName)
            if (-not $bySize.ContainsKey($name) -or $entry.Length -gt $bySize[$name]) {
                $bySize[$name] = $entry.Length
            }
        }
        $result = foreach ($name in $bySize.Keys) {
            [pscustomobject]@{ Name = $name; Bytes = $bySize[$name] }
        }
        return @($result)
    } catch {
        Write-Warning "Failed to enumerate assemblies from ${NupkgPath}: $($_.Exception.Message)"
        return @()
    } finally {
        if ($zip) { $zip.Dispose() }
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

$measurements = [System.Collections.Generic.List[object]]::new()

foreach ($t in $targets) {
    $projPath = Join-Path $Root $t.Project
    Write-Host "==> Packing $($t.PackageId) ($($t.Project))"

    $nupkgBytes = $null
    $assemblies = @()

    if (-not (Test-Path -LiteralPath $projPath)) {
        Write-Warning "Project not found: $projPath — emitting n/a for $($t.PackageId)."
    } else {
        $packArgs = @(
            'pack', $projPath,
            '-c', $Configuration,
            "-p:Version=$PackageVersion",
            '-o', $PackOutput,
            '--nologo'
        ) + $t.ExtraArgs

        & dotnet @packArgs 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "dotnet pack failed for $($t.PackageId) (exit $LASTEXITCODE) — emitting n/a."
        } else {
            $nupkg = Select-LatestNupkg -PackOutput $PackOutput -PackageId $t.PackageId
            if ($nupkg) {
                $nupkgBytes = $nupkg.Length
                $assemblies = Get-NupkgAssemblies -NupkgPath $nupkg.FullName
                Write-Host "    $($nupkg.Name): nupkg=$nupkgBytes B, $(@($assemblies).Count) DLL(s)"
                foreach ($asm in ($assemblies | Sort-Object Name)) {
                    Write-Host "      $($asm.Name) = $($asm.Bytes) B"
                }
            } else {
                Write-Warning "No .nupkg matching '$($t.PackageId).<version>.nupkg' in $PackOutput."
            }
        }
    }

    # Compressed package row.
    $measurements.Add([pscustomobject]@{ Key = "nupkg.$($t.PkgKey)"; Label = $t.PkgLabel; Group = $packageGroup; Bytes = $nupkgBytes })

    # One row per shipped DLL, keyed 'asm|<PkgKey>|<Dll>'. The Group here is
    # informational (the trusted poster re-derives it from the package spec via
    # ConvertTo-SafeMeasurements); it makes local, direct renders group per package.
    $assemblyGroup = "Assemblies in $($t.PackageId)"
    foreach ($asm in @($assemblies)) {
        $measurements.Add([pscustomobject]@{ Key = "asm|$($t.PkgKey)|$($asm.Name)"; Label = $asm.Name; Group = $assemblyGroup; Bytes = $asm.Bytes })
    }
}

# Depth 5 is plenty for the flat measurement objects; force an array shape even
# for a single element so the consumer always ConvertFrom-Json's an array.
$json = ConvertTo-Json -InputObject @($measurements) -Depth 5
Set-Content -LiteralPath $OutFile -Value $json -Encoding UTF8
Write-Host ""
Write-Host "Wrote $($measurements.Count) measurement(s) to $OutFile"
