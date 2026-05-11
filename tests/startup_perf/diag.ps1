<# Diagnostic: capture each variant manually with tracelog (bypasses WPR)
   and dump per-event provider GUID, name, and all property values.
   Run from admin PowerShell. #>

$ErrorActionPreference = "Continue"
$root = $PSScriptRoot
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" }
$tfm = "net10.0-windows10.0.22621.0"
$rid = if ($arch -eq "ARM64") { "win-arm64" } else { "win-x64" }
$ProviderGuid = [guid]'FD80D616-E92B-4B2B-9BED-131ADA36A8FD'

$variants = @(
    @{ Name = "WinUI3";  Exe = Join-Path $root "BlankWinUI3\bin\$arch\Release\$tfm\BlankWinUI3.exe" },
    @{ Name = "Reactor"; Exe = Join-Path $root "BlankReactor\bin\$arch\Release\$tfm\BlankReactor.exe" },
    @{ Name = "RNW";     Exe = Join-Path $root "BlankRNW\windows\$arch\Release\BlankRNW.exe" }
)

foreach ($v in $variants) {
    Write-Host "`n========== $($v.Name) ==========" -ForegroundColor Cyan
    if (-not (Test-Path $v.Exe)) { Write-Host "MISSING $($v.Exe)" -ForegroundColor Red; continue }

    $etl = Join-Path $env:TEMP "diag-$($v.Name).etl"
    if (Test-Path $etl) { Remove-Item $etl -Force }

    & logman stop DiagSession -ets 2>&1 | Out-Null
    $startOut = & logman create trace DiagSession -p "{FD80D616-E92B-4B2B-9BED-131ADA36A8FD}" 0xFFFFFFFFFFFFFFFF 0xFF -ets -o $etl -ow 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host "logman create failed: $startOut" -ForegroundColor Red; continue }
    Start-Sleep -Milliseconds 200

    $p = Start-Process $v.Exe -PassThru
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 8000 -and $p.MainWindowHandle -eq 0 -and -not $p.HasExited) {
        Start-Sleep -Milliseconds 50
        $p.Refresh()
    }
    Start-Sleep -Seconds 2
    if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(3000)) { $p.Kill() } }

    & logman stop DiagSession -ets 2>&1 | Out-Null

    $all = @(Get-WinEvent -Path $etl -Oldest -ErrorAction SilentlyContinue)
    $ours = @($all | Where-Object { $_.ProviderId -eq $ProviderGuid })
    Write-Host "ETL total events: $($all.Count). On our GUID: $($ours.Count)"

    if ($all.Count -gt 0 -and $ours.Count -eq 0) {
        Write-Host "Top providers in ETL:"
        $all | Group-Object { "$($_.ProviderId)  $($_.ProviderName)" } | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { Write-Host "  $($_.Count.ToString().PadLeft(5))  $($_.Name)" }
    }

    Write-Host "Get-WinEvent view (first 3 of our events):"
    foreach ($e in ($ours | Select-Object -First 3)) {
        $taskName = $e.TaskDisplayName
        if (-not $taskName) { try { $taskName = ([xml]$e.ToXml()).Event.System.Task } catch {} }
        Write-Host "  Task=$taskName  Id=$($e.Id)  PropCount=$($e.Properties.Count)"
        for ($i = 0; $i -lt $e.Properties.Count; $i++) {
            $val = $e.Properties[$i].Value
            $tn = if ($null -ne $val) { $val.GetType().Name } else { "<null>" }
            Write-Host "    [$i] ($tn) = $val"
        }
    }

    # tracerpt fully decodes TraceLogging metadata that Get-WinEvent doesn't.
    # The slim wprp keeps the ETL small (<1MB) so XML output is tractable.
    $xml = "$etl.xml"
    if (Test-Path $xml) { Remove-Item $xml -Force }
    $tracerptOut = & tracerpt $etl -o $xml -of XML -lr -y 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "tracerpt failed: $tracerptOut" -ForegroundColor Yellow
    } elseif (Test-Path $xml) {
        Write-Host "tracerpt view (first 4 events on our provider):"
        try {
            [xml]$doc = Get-Content $xml -Raw
            $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
            $ns.AddNamespace('e', 'http://schemas.microsoft.com/win/2004/08/events/event')
            $evts = $doc.SelectNodes("//e:Event[e:System/e:Provider/@Guid='{fd80d616-e92b-4b2b-9bed-131ada36a8fd}']", $ns)
            if (-not $evts -or $evts.Count -eq 0) {
                $evts = $doc.SelectNodes("//e:Event[contains(translate(e:System/e:Provider/@Guid,'ABCDEF','abcdef'),'fd80d616')]", $ns)
            }
            $count = 0
            foreach ($e in $evts) {
                if ($count -ge 4) { break }
                $count++
                $task = $e.RenderingInfo.Task
                if (-not $task) { $task = $e.System.Task }
                Write-Host "  $task"
                if ($e.EventData -and $e.EventData.Data) {
                    foreach ($d in $e.EventData.Data) { Write-Host "    $($d.Name) = $($d.'#text')" }
                }
            }
            Write-Host "  (total events on our provider in XML: $($evts.Count))"
        } catch {
            Write-Host "  Failed to parse XML: $_" -ForegroundColor Yellow
        }
    }
}
