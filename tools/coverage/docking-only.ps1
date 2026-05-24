[xml]$x = Get-Content "$PSScriptRoot/../../coverage/merged.cobertura.xml"
$byFile = @{}
foreach ($cls in $x.SelectNodes('//class')) {
  $f = "$($cls.filename)"
  if (-not $f) { continue }
  $norm = $f -replace '\\','/'
  if ($norm -notlike '*/Reactor/Docking/*') { continue }
  $key = $norm -replace '.+/Reactor/Docking/','Docking/'
  if (-not $byFile.ContainsKey($key)) {
    $byFile[$key] = [pscustomobject]@{ File=$key; LinesCov=0; LinesTot=0; BrCov=0; BrTot=0 }
  }
  $row = $byFile[$key]
  foreach ($l in $cls.SelectNodes('.//line')) {
    $row.LinesTot++
    if ([int]$l.hits -gt 0) { $row.LinesCov++ }
    if ($l.'condition-coverage' -match '\((\d+)/(\d+)\)') {
      $row.BrCov += [int]$Matches[1]; $row.BrTot += [int]$Matches[2]
    }
  }
}

$rows = $byFile.Values | ForEach-Object {
  [pscustomobject]@{
    File    = $_.File
    LinePct = if ($_.LinesTot) { [math]::Round(100.0*$_.LinesCov/$_.LinesTot,1) } else { 100.0 }
    LinesCov= $_.LinesCov
    LinesTot= $_.LinesTot
    Missed  = $_.LinesTot - $_.LinesCov
  }
} | Sort-Object LinePct

$rows | Format-Table File, LinePct, LinesCov, LinesTot, Missed -AutoSize

$lc = ($rows | Measure-Object LinesCov -Sum).Sum
$lt = ($rows | Measure-Object LinesTot -Sum).Sum

""
"===== Docking namespace aggregate ($($rows.Count) files) ====="
"  Line : {0:F2}%  ({1}/{2})  missed={3}" -f (100.0*$lc/$lt), $lc, $lt, ($lt-$lc)
