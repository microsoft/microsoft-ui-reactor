<#
.SYNOPSIS
    Dependency-free unit tests for the testable helpers in Measure-BuildMetrics.ps1
    (the build-metrics pack/measure orchestrator): Select-LatestNupkg (package
    selection) and Get-NupkgAssemblies (ZIP shipped-DLL enumeration).

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
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add($Message) }
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
Invoke-Expression (Get-Func 'Get-NupkgAssemblies')

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
    # ── Get-NupkgAssemblies ───────────────────────────────────────────────────
    # Enumerate EVERY shipped DLL: lib/<tfm>/*.dll AND analyzers/**/*.dll, one row
    # per basename (largest length wins across TFMs), excluding satellite
    # *.resources.dll and any non-DLL file.
    $pkg = Join-Path $tmp 'all-dlls.nupkg'
    New-Nupkg -Path $pkg -Entries ([ordered]@{
        'lib/net8.0/A.dll'                            = 3000    # same basename, smaller TFM
        'lib/net10.0-windows10.0.22621.0/A.dll'       = 5000    # largest -> wins
        'analyzers/dotnet/cs/B.dll'                   = 296000  # analyzers/ DLL included
        'lib/net10.0-windows10.0.22621.0/C.resources.dll' = 999 # satellite -> excluded
        'lib/net10.0-windows10.0.22621.0/D.dll'       = 100     # a second, distinct lib DLL
        '[Content_Types].xml'                         = 200     # non-DLL -> excluded
        'docs/readme.md'                              = 10      # non-DLL -> excluded
    })
    $asms = @(Get-NupkgAssemblies -NupkgPath $pkg)
    $byName = @{}; foreach ($a in $asms) { $byName[$a.Name] = $a.Bytes }
    Assert-Equal 3 $asms.Count 'enumerates A.dll, B.dll, D.dll (satellite + non-DLL excluded)'
    Assert-Equal 5000 $byName['A.dll'] 'largest lib/<tfm> length wins for a shared basename'
    Assert-Equal 296000 $byName['B.dll'] 'analyzers/ DLL is enumerated'
    Assert-Equal 100 $byName['D.dll'] 'a second distinct lib DLL is enumerated'
    Assert-True (-not $byName.ContainsKey('C.resources.dll')) 'satellite *.resources.dll excluded'

    # A single-TFM, single-DLL package returns exactly that entry's length.
    $single = Join-Path $tmp 'single.nupkg'
    New-Nupkg -Path $single -Entries ([ordered]@{ 'lib/net10.0/Reactor.Advanced.dll' = 33792 })
    $singleAsms = @(Get-NupkgAssemblies -NupkgPath $single)
    Assert-Equal 1 $singleAsms.Count 'single-DLL package -> one row'
    Assert-Equal 'Reactor.Advanced.dll' $singleAsms[0].Name 'single-DLL package -> correct name'
    Assert-Equal 33792 $singleAsms[0].Bytes 'single-TFM assembly size'

    # No shipped DLLs -> empty array.
    $noAsm = Join-Path $tmp 'noasm.nupkg'
    New-Nupkg -Path $noAsm -Entries ([ordered]@{ 'ref/net10.0/Only.dll' = 1234; 'readme.md' = 10 })
    Assert-Equal 0 (@(Get-NupkgAssemblies -NupkgPath $noAsm)).Count 'no lib/ or analyzers/ DLL -> empty'

    # Corrupt / non-ZIP file -> empty (the try/catch swallows the open failure).
    $corrupt = Join-Path $tmp 'corrupt.nupkg'
    Set-Content -LiteralPath $corrupt -Value 'this is not a zip' -Encoding ascii
    Assert-Equal 0 (@(Get-NupkgAssemblies -NupkgPath $corrupt -WarningAction SilentlyContinue)).Count 'corrupt archive -> empty'

    # Missing file -> empty.
    Assert-Equal 0 (@(Get-NupkgAssemblies -NupkgPath (Join-Path $tmp 'nope.nupkg') -WarningAction SilentlyContinue)).Count 'missing file -> empty'

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
