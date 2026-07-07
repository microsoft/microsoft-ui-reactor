<#
.SYNOPSIS
    Build + pack the shipped Reactor NuGet packages in one source tree and emit a
    JSON list of artifact sizes for the build-metrics PR comment.

.DESCRIPTION
    Runs `dotnet pack -c Release` for each tracked package, then measures:
      * the compressed .nupkg (what a consumer downloads), and
      * the primary uncompressed assembly inside it (lib/<tfm>/<Assembly>.dll —
        the real "did our code grow" signal).

    The output JSON is an array of measurement objects consumed by
    BuildMetricsLib.ps1's Format-BuildMetricsComment:
      { "Key": "...", "Label": "...", "Group": "...", "Bytes": <int|null> }

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
# Keys are stable identifiers (NOT filenames — the .nupkg carries a version), so
# base and head rows line up even when their MinVer versions differ. Advanced +
# Devtools pack from their AnyCPU output (mirrors release.yml), which the standalone
# `dotnet pack` produces by default; passing it explicitly keeps parity if the
# csproj default ever changes.
$targets = @(
    [pscustomobject]@{
        PkgKey = 'nupkg.Reactor'; PkgLabel = 'Microsoft.UI.Reactor.nupkg'
        AsmKey = 'asm.Reactor';   AsmLabel = 'Reactor.dll'
        Project = 'src/Reactor/Reactor.csproj'
        PackageId = 'Microsoft.UI.Reactor'; AssemblyFile = 'Reactor.dll'
        ExtraArgs = @()
    }
    [pscustomobject]@{
        PkgKey = 'nupkg.Advanced'; PkgLabel = 'Microsoft.UI.Reactor.Advanced.nupkg'
        AsmKey = 'asm.Advanced';   AsmLabel = 'Reactor.Advanced.dll'
        Project = 'src/Reactor.Advanced/Reactor.Advanced.csproj'
        PackageId = 'Microsoft.UI.Reactor.Advanced'; AssemblyFile = 'Reactor.Advanced.dll'
        ExtraArgs = @('-p:Platform=AnyCPU')
    }
    [pscustomobject]@{
        PkgKey = 'nupkg.Devtools'; PkgLabel = 'Microsoft.UI.Reactor.Devtools.nupkg'
        AsmKey = 'asm.Devtools';   AsmLabel = 'Microsoft.UI.Reactor.Devtools.dll'
        Project = 'src/Reactor.Devtools/Reactor.Devtools.csproj'
        PackageId = 'Microsoft.UI.Reactor.Devtools'; AssemblyFile = 'Microsoft.UI.Reactor.Devtools.dll'
        ExtraArgs = @('-p:Platform=AnyCPU')
    }
)

$packageGroup  = 'Packages (compressed .nupkg)'
$assemblyGroup = 'Assemblies (uncompressed)'

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

function Get-NupkgAssemblyBytes {
    <#
    .SYNOPSIS
        Uncompressed size of the largest lib/<tfm>/<AssemblyFile> entry in a
        .nupkg, without extracting it. $null if the archive has no such entry.
    #>
    param([string]$NupkgPath, [string]$AssemblyFile)
    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($NupkgPath)
        $best = $null
        foreach ($entry in $zip.Entries) {
            # NuGet lib assets: lib/<tfm>/<file>. FullName uses forward slashes.
            if ($entry.FullName -match ('^lib/[^/]+/' + [regex]::Escape($AssemblyFile) + '$')) {
                if ($null -eq $best -or $entry.Length -gt $best) { $best = $entry.Length }
            }
        }
        return $best
    } catch {
        Write-Warning "Failed to read '$AssemblyFile' from ${NupkgPath}: $($_.Exception.Message)"
        return $null
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
    $asmBytes = $null

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
                $asmBytes = Get-NupkgAssemblyBytes -NupkgPath $nupkg.FullName -AssemblyFile $t.AssemblyFile
                Write-Host "    $($nupkg.Name): nupkg=$nupkgBytes B, $($t.AssemblyFile)=$asmBytes B"
            } else {
                Write-Warning "No .nupkg matching '$($t.PackageId).<version>.nupkg' in $PackOutput."
            }
        }
    }

    $measurements.Add([pscustomobject]@{ Key = $t.PkgKey; Label = $t.PkgLabel; Group = $packageGroup;  Bytes = $nupkgBytes })
    $measurements.Add([pscustomobject]@{ Key = $t.AsmKey; Label = $t.AsmLabel; Group = $assemblyGroup; Bytes = $asmBytes })
}

# Depth 5 is plenty for the flat measurement objects; force an array shape even
# for a single element so the consumer always ConvertFrom-Json's an array.
$json = ConvertTo-Json -InputObject @($measurements) -Depth 5
Set-Content -LiteralPath $OutFile -Value $json -Encoding UTF8
Write-Host ""
Write-Host "Wrote $($measurements.Count) measurement(s) to $OutFile"
