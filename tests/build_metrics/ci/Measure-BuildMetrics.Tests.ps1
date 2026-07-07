<#
.SYNOPSIS
    Dependency-free unit tests for the testable helpers in Measure-BuildMetrics.ps1
    (the build-metrics pack/measure orchestrator): Select-LatestNupkg (package
    selection) and Get-NupkgAssemblyBytes (ZIP assembly-size extraction).

.DESCRIPTION
    Measure-BuildMetrics.ps1 isn't dot-sourceable (it has a param block + a main
    run flow that shells out to `dotnet pack`), so each function under test is
    extracted via the PowerShell AST and defined in isolation. The tests synthesize
    real .nupkg ZIP archives and on-disk file layouts — no `dotnet`, no network — so
    this runs headless and cross-platform on the cheap Linux runner in
    .github/workflows/build-metrics-lib-tests.yml. Exits non-zero on any failure.

    The full pack path (dotnet pack, platform args) is validated by the live
    build-metrics run, not here; this covers the pure selection/extraction logic.

    Run locally:  pwsh tests/build_metrics/ci/Measure-BuildMetrics.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()
function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]") }
}
function Assert-Null {
    param($Actual, [string]$Message)
    if ($null -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: <null>`n    actual:   [$Actual]") }
}

# --- Extract the functions under test from the orchestrator script via AST. ---
$scriptPath = Join-Path $PSScriptRoot 'Measure-BuildMetrics.ps1'
$src = Get-Content $scriptPath -Raw
$parseTokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "Measure-BuildMetrics.ps1 has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
    $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
    exit 1
}
function Get-Func([string]$name) {
    $f = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) |
        Select-Object -First 1
    if (-not $f) { throw "$name not found in Measure-BuildMetrics.ps1" }
    $f.Extent.Text
}
Invoke-Expression (Get-Func 'Select-LatestNupkg')
Invoke-Expression (Get-Func 'Get-NupkgAssemblyBytes')

# --- Test scratch dir + helpers to synthesize .nupkg ZIPs. ---
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("bm-measure-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function New-Nupkg {
    # entries: ordered hashtable of 'zip/path' = <byte length>
    param([string]$Path, [hashtable]$Entries)
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($fs, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in $Entries.Keys) {
                $entry = $zip.CreateEntry($name)
                $es = $entry.Open()
                try {
                    $buf = New-Object byte[] ([int]$Entries[$name])
                    $es.Write($buf, 0, $buf.Length)
                } finally { $es.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $fs.Dispose() }
}

try {
    # ── Get-NupkgAssemblyBytes ────────────────────────────────────────────────
    # Largest lib/<tfm>/Reactor.dll wins; ref/ and non-lib entries are ignored.
    $multi = Join-Path $tmp 'multi.nupkg'
    New-Nupkg -Path $multi -Entries ([ordered]@{
        'lib/net8.0/Reactor.dll'                     = 3000
        'lib/net10.0-windows10.0.22621.0/Reactor.dll' = 5000
        'ref/net10.0/Reactor.dll'                    = 9999   # not under lib/ -> ignored
        '[Content_Types].xml'                        = 200
    })
    Assert-Equal 5000 (Get-NupkgAssemblyBytes -NupkgPath $multi -AssemblyFile 'Reactor.dll') 'largest lib/<tfm> assembly wins; ref/ ignored'

    # A single-TFM package returns that entry's uncompressed length.
    $single = Join-Path $tmp 'single.nupkg'
    New-Nupkg -Path $single -Entries ([ordered]@{ 'lib/net10.0/Reactor.Advanced.dll' = 33792 })
    Assert-Equal 33792 (Get-NupkgAssemblyBytes -NupkgPath $single -AssemblyFile 'Reactor.Advanced.dll') 'single-TFM assembly size'

    # No matching assembly -> null.
    $noAsm = Join-Path $tmp 'noasm.nupkg'
    New-Nupkg -Path $noAsm -Entries ([ordered]@{ 'lib/net10.0/Other.dll' = 1234; 'readme.md' = 10 })
    Assert-Null (Get-NupkgAssemblyBytes -NupkgPath $noAsm -AssemblyFile 'Reactor.dll') 'no matching lib assembly -> null'

    # Corrupt / non-ZIP file -> null (the try/catch swallows the open failure).
    $corrupt = Join-Path $tmp 'corrupt.nupkg'
    Set-Content -LiteralPath $corrupt -Value 'this is not a zip' -Encoding ascii
    Assert-Null (Get-NupkgAssemblyBytes -NupkgPath $corrupt -AssemblyFile 'Reactor.dll' -WarningAction SilentlyContinue) 'corrupt archive -> null'

    # Missing file -> null.
    Assert-Null (Get-NupkgAssemblyBytes -NupkgPath (Join-Path $tmp 'nope.nupkg') -AssemblyFile 'Reactor.dll' -WarningAction SilentlyContinue) 'missing file -> null'

    # ── Select-LatestNupkg ────────────────────────────────────────────────────
    $pkgDir = Join-Path $tmp 'pkgs'
    New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null
    foreach ($f in @(
        'Microsoft.UI.Reactor.0.0.0-buildmetrics.nupkg',
        'Microsoft.UI.Reactor.0.0.0-buildmetrics.symbols.nupkg',
        'Microsoft.UI.Reactor.Advanced.0.0.0-buildmetrics.nupkg',
        'Microsoft.UI.Reactor.Devtools.0.0.0-buildmetrics.nupkg'
    )) { Set-Content -LiteralPath (Join-Path $pkgDir $f) -Value 'x' }

    $sel = Select-LatestNupkg -PackOutput $pkgDir -PackageId 'Microsoft.UI.Reactor'
    Assert-Equal 'Microsoft.UI.Reactor.0.0.0-buildmetrics.nupkg' $sel.Name 'exact package id — not the Advanced/Devtools siblings, not symbols'

    $selAdv = Select-LatestNupkg -PackOutput $pkgDir -PackageId 'Microsoft.UI.Reactor.Advanced'
    Assert-Equal 'Microsoft.UI.Reactor.Advanced.0.0.0-buildmetrics.nupkg' $selAdv.Name 'Advanced resolves to its own package'

    # No match -> null.
    Assert-Null (Select-LatestNupkg -PackOutput $pkgDir -PackageId 'Nonexistent.Package') 'no matching package -> null'

    # Newest write time wins among two versions of the same id.
    $verDir = Join-Path $tmp 'versions'
    New-Item -ItemType Directory -Path $verDir -Force | Out-Null
    $old = Join-Path $verDir 'Microsoft.UI.Reactor.0.0.0-buildmetrics.nupkg'
    $new = Join-Path $verDir 'Microsoft.UI.Reactor.0.0.1-buildmetrics.nupkg'
    Set-Content -LiteralPath $old -Value 'x'; Set-Content -LiteralPath $new -Value 'x'
    (Get-Item $old).LastWriteTime = (Get-Date).AddMinutes(-10)
    (Get-Item $new).LastWriteTime = (Get-Date)
    Assert-Equal 'Microsoft.UI.Reactor.0.0.1-buildmetrics.nupkg' (Select-LatestNupkg -PackOutput $verDir -PackageId 'Microsoft.UI.Reactor').Name 'newest write time wins'
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "Measure-BuildMetrics tests: $script:Pass passed, $script:Fail failed."
if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host 'Failures:' -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
exit 0
