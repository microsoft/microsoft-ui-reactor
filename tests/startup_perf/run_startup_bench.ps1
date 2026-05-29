<#
.SYNOPSIS
    Cold-start benchmark for the startup_perf matched set.

.DESCRIPTION
    Builds (optional) BlankWinUI3 / BlankReactor / BlankRNW, then for each
    variant runs the executable N times under a WPR session, parses the
    BenchmarkSyntheticApps ETW events out of the trace, and prints
    median TTFP / TTI side-by-side.

    The ETW provider GUID and event schema match -lift's blank-app set
    (see README.md), so traces produced here can be opened against
    -lift's BenchmarkBlankApps.Regions.xml in WPA without modification.

.PARAMETER Runs
    Cold-start runs per variant (default 5). Median is reported.

.PARAMETER Skip
    Comma-separated variants to skip: WinUI3, Reactor, RNW.

.PARAMETER Build
    If set, builds C# variants before running. RNW must be built
    manually first (npm install + run-windows).

.PARAMETER OutputDir
    Where to drop ETL files and the CSV summary. Defaults to .\out.

.EXAMPLE
    .\run_startup_bench.ps1 -Runs 5

.EXAMPLE
    .\run_startup_bench.ps1 -Runs 10 -Skip RNW
#>
param(
    [int]$Runs = 5,
    [string]$Skip = "",
    [switch]$Build,
    [string]$OutputDir = (Join-Path $PSScriptRoot "out")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $root "..\..")
$wprp = Join-Path $root "Common\Tracing.wprp"
$skipped = @($Skip -split ',' | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ })

if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }

# Cancel any orphaned WPR session from a prior failed run (silently — exit
# code is non-zero when there's nothing to cancel, that's fine).
& wpr -cancel 2>&1 | Out-Null

# ---- Variant table ---------------------------------------------------------
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" }
$rid = if ($arch -eq "ARM64") { "win-arm64" } else { "win-x64" }
$tfm = "net10.0-windows10.0.22621.0"

# C# variants are non-AOT (PublishAot=false in csproj — NativeAOT trims the
# EventSource subclass and emits zero ETW events). Build path therefore has
# no publish/ subdir. Means C# numbers carry ~70-100 ms of CLR bootstrap;
# README documents this. RNW builds via the RN CLI; its exe lives under
# windows\<arch>\Release.
$variants = @(
    @{
        Name = "WinUI3"
        AppName = "blank_winui3"
        Exe = Join-Path $root "BlankWinUI3\bin\$arch\Release\$tfm\BlankWinUI3.exe"
        BuildArgs = @("build", (Join-Path $root "BlankWinUI3\BlankWinUI3.csproj"), "-c", "Release", "-p:Platform=$arch")
    },
    @{
        Name = "Reactor"
        AppName = "blank_reactor"
        Exe = Join-Path $root "BlankReactor\bin\$arch\Release\$tfm\BlankReactor.exe"
        BuildArgs = @("build", (Join-Path $root "BlankReactor\BlankReactor.csproj"), "-c", "Release", "-p:Platform=$arch")
    },
    @{
        Name = "RNW"
        AppName = "blank_rnw"
        # MSBuild puts the unpackaged exe under windows\<Platform>\Release\
        # (the wapproj packages it separately into windows\BlankRNW.Package\bin).
        Exe = Join-Path $root "BlankRNW\windows\$arch\Release\BlankRNW.exe"
        BuildArgs = $null  # built via npx run-windows manually
    }
)

# ---- Build ----------------------------------------------------------------
if ($Build) {
    foreach ($v in $variants) {
        if ($v.Name.ToLower() -in $skipped) { continue }
        if (-not $v.BuildArgs) { continue }
        Write-Host "[build] $($v.Name)" -ForegroundColor Cyan
        & dotnet @($v.BuildArgs)
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $($v.Name)" }
    }
}

# ---- Helpers --------------------------------------------------------------
function Wait-ForWindow([System.Diagnostics.Process]$p, [int]$timeoutMs = 30000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $p.Refresh()
        if ($p.HasExited) { return }
        if ($p.MainWindowHandle -ne 0) { return }
        Start-Sleep -Milliseconds 50
    }
}

$ProviderGuid = 'fd80d616-e92b-4b2b-9bed-131ada36a8fd'

function Parse-Etl([string]$etl, [string]$appName) {
    # Use tracerpt — Get-WinEvent doesn't decode TraceLogging metadata
    # (event names + payload fields), but tracerpt does. Slim wprp keeps
    # ETLs tiny so the XML is small enough to load whole.
    $xml = "$etl.xml"
    if (Test-Path $xml) { Remove-Item $xml -Force }
    $tOut = & tracerpt $etl -o $xml -of XML -lr -y 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $xml)) {
        Write-Host "    tracerpt failed: $tOut" -ForegroundColor Yellow
        return $null
    }

    [xml]$doc = Get-Content $xml -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('e', 'http://schemas.microsoft.com/win/2004/08/events/event')

    # GUID match is case-insensitive; tracerpt sometimes emits braces.
    $events = @{}
    foreach ($evt in $doc.SelectNodes("//e:Event", $ns)) {
        $guid = $evt.System.Provider.Guid
        if ($null -eq $guid) { continue }
        $g = $guid.Trim('{','}').ToLower()
        if ($g -ne $ProviderGuid) { continue }

        # tracerpt resolves event task name into RenderingInfo/Task or System/Task.
        $taskName = $evt.RenderingInfo.Task
        if (-not $taskName) { $taskName = $evt.System.Task }
        if (-not $taskName) { continue }

        # AppName field is always present on our events (added in BenchmarkTracing
        # and RNWAppTracing.h).
        $payloadName = $null
        if ($evt.EventData -and $evt.EventData.Data) {
            foreach ($d in $evt.EventData.Data) {
                if ($d.Name -eq 'AppName') { $payloadName = $d.'#text'; break }
            }
        }
        if ($payloadName -ne $appName) { continue }

        $tsFmt = $evt.System.TimeCreated.SystemTime
        $ts = [datetime]::Parse($tsFmt, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal).Ticks
        if (-not $events.ContainsKey($taskName)) { $events[$taskName] = $ts }
    }

    if (-not $events.ContainsKey('wWinMainEntry')) {
        Write-Host "    No 'wWinMainEntry' event for AppName='$appName' (saw: $($events.Keys -join ', '))" -ForegroundColor Yellow
        return $null
    }
    $entry = $events['wWinMainEntry']
    $result = @{ AppName = $appName }
    foreach ($evtName in 'XamlAppLoaded','WindowLoaded','JSBundleLoaded','ReactMounted','FirstRender','FirstIdle','ProcessStop') {
        if ($events.ContainsKey($evtName)) {
            $result[$evtName] = [math]::Round((($events[$evtName] - $entry) / 10000.0), 1)  # 100ns ticks -> ms
        } else {
            $result[$evtName] = $null
        }
    }
    return $result
}

function Median([double[]]$values) {
    if (-not $values -or $values.Count -eq 0) { return $null }
    $sorted = $values | Sort-Object
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return $sorted[[int]($n / 2)] }
    return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2.0
}

# ---- Capture loop ---------------------------------------------------------
$allRuns = @()

foreach ($v in $variants) {
    if ($v.Name.ToLower() -in $skipped) {
        Write-Host "[skip] $($v.Name)" -ForegroundColor DarkYellow
        continue
    }
    if (-not (Test-Path $v.Exe)) {
        Write-Host "[skip] $($v.Name) — exe not found at $($v.Exe). Build it first (--Build for C# variants, or 'npx run-windows' for RNW)." -ForegroundColor Yellow
        continue
    }

    Write-Host ""
    Write-Host "== $($v.Name) ($($v.AppName)) ==" -ForegroundColor Green
    for ($i = 1; $i -le $Runs; $i++) {
        $etl = Join-Path $OutputDir "$($v.AppName)-run$i.etl"

        $startOut = & wpr -start $wprp -filemode 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [run $i] wpr -start failed:" -ForegroundColor Red
            $startOut | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed }
            & wpr -cancel 2>&1 | Out-Null
            continue
        }

        Start-Sleep -Milliseconds 100  # let wpr settle
        $proc = Start-Process -FilePath $v.Exe -PassThru
        Wait-ForWindow $proc 30000

        # Hold the window open for ~1.5 s so FirstIdle and ProcessStop are
        # both captured. While we wait, sample peak working set every 50 ms
        # — Process.PeakWorkingSet64 is OS-tracked and we'll read it just
        # before exit, but we also poll so a transient peak isn't missed.
        $peakWsBytes = 0
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt 1500 -and -not $proc.HasExited) {
            $proc.Refresh()
            if ($proc.WorkingSet64 -gt $peakWsBytes) { $peakWsBytes = $proc.WorkingSet64 }
            Start-Sleep -Milliseconds 50
        }
        if (-not $proc.HasExited) {
            $proc.Refresh()
            if ($proc.PeakWorkingSet64 -gt $peakWsBytes) { $peakWsBytes = $proc.PeakWorkingSet64 }
            $proc.CloseMainWindow() | Out-Null
            if (-not $proc.WaitForExit(3000)) { $proc.Kill() }
        }
        $peakWsMB = [math]::Round($peakWsBytes / 1MB, 1)

        & wpr -stop $etl 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [run $i] wpr -stop failed" -ForegroundColor Red
            continue
        }

        $r = Parse-Etl $etl $v.AppName
        if ($null -eq $r) {
            Write-Host "  [run $i] no events parsed" -ForegroundColor Red
            continue
        }
        $r.Variant = $v.Name
        $r.Run = $i
        $r.PeakWS_MB = $peakWsMB
        $allRuns += [pscustomobject]$r
        $ttfp = $r.FirstRender
        $tti = $r.FirstIdle
        Write-Host ("  [run {0}] TTFP={1,6:N1} ms  TTI={2,6:N1} ms  PeakWS={3,6:N1} MB" -f $i, $ttfp, $tti, $peakWsMB)
    }
}

# ---- Summary --------------------------------------------------------------
Write-Host ""
Write-Host "== Medians (ms, $Runs runs) ==" -ForegroundColor Green
$grouped = $allRuns | Group-Object -Property Variant
$summary = foreach ($g in $grouped) {
    $renderMed = Median (@($g.Group | ForEach-Object { $_.FirstRender } | Where-Object { $_ -ne $null }))
    $idleMed   = Median (@($g.Group | ForEach-Object { $_.FirstIdle }   | Where-Object { $_ -ne $null }))
    $windowMed = Median (@($g.Group | ForEach-Object { $_.WindowLoaded } | Where-Object { $_ -ne $null }))
    $wsMed     = Median (@($g.Group | ForEach-Object { $_.PeakWS_MB }    | Where-Object { $_ -ne $null }))
    [pscustomobject]@{
        Variant        = $g.Name
        N              = $g.Count
        WindowLoadedMs = $windowMed
        TTFP_ms        = $renderMed
        TTI_ms         = $idleMed
        PeakWS_MB      = $wsMed
    }
}
$summary | Format-Table -AutoSize

$csv = Join-Path $OutputDir "summary.csv"
$summary | Export-Csv -NoTypeInformation -Path $csv
$allRuns | Export-Csv -NoTypeInformation -Path (Join-Path $OutputDir "runs.csv")
Write-Host ""
Write-Host "Per-run + summary CSVs in $OutputDir" -ForegroundColor Cyan
