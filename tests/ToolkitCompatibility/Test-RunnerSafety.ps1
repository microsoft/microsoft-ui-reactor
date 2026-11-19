$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$failures = [Collections.Generic.List[string]]::new()
function Assert-Runner([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Output "PASS $Message" }
    else { $failures.Add($Message); Write-Output "FAIL $Message" }
}

# Extract helpers without executing the runner's build/deployment entry point.
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Run-Matrix.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Invoke-Appx', 'Invoke-Consumer')) {
    $definition = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($definition.Count -ne 1) { throw "Expected one helper $name." }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}

$script:appxCommand = ''
function Invoke-Logged([string]$Log, [string]$Executable, [string[]]$Arguments) {
    if ($Executable -ne 'powershell.exe' -or $Arguments[-2] -ne '-EncodedCommand') {
        throw 'Unexpected Appx invocation.'
    }
    $script:appxCommand = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($Arguments[-1]))
}
foreach ($operation in @('query', 'remove')) {
    Invoke-Appx $operation '' 'Rct.Owned_1.0.0.0_x64__publisher' 'unused' -AllUsers
    Assert-Runner ($script:appxCommand.Contains('-AllUsers')) "$operation scopes installed MSIX to all users"
    Assert-Runner ($script:appxCommand.Contains("'Rct.Owned_1.0.0.0_x64__publisher'")) "$operation retains exact owned identity"
    Invoke-Appx $operation '' 'Rct.Owned_1.0.0.0_x64__publisher' 'unused'
    Assert-Runner (-not $script:appxCommand.Contains('-AllUsers')) "$operation keeps loose registration current-user scoped"
}

$evidence = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'reactor-toolkit-runner-' + [Guid]::NewGuid().ToString('N'))
New-Item $evidence -ItemType Directory | Out-Null
$TimeoutSeconds = 10
$shell = Join-Path $PSHOME 'pwsh.exe'
$marker = Join-Path $evidence 'child.pid'
$quotedMarker = $marker.Replace("'", "''")
try {
    $positive = Invoke-Consumer $shell @('-NoProfile', '-NonInteractive', '-Command',
        "[IO.File]::WriteAllText('$quotedMarker', [string]`$PID); [Console]::Out.WriteLine('captured stdout'); [Console]::Error.WriteLine('captured stderr'); exit 7") `
        $evidence (Join-Path $evidence 'positive')
    Assert-Runner ($positive.ExitCode -eq 7 -and -not $positive.TimedOut -and $positive.CaptureComplete) 'nonzero child exit retains complete capture'
    Assert-Runner ($positive.Stdout.Trim() -eq 'captured stdout' -and
        [IO.File]::ReadAllText((Join-Path $evidence 'positive.stderr.txt')).Trim() -eq 'captured stderr') 'both output streams contain actual child output'
    Assert-Runner (Test-Path $marker) 'child-launch marker positive control'
    Remove-Item -LiteralPath $marker

    $prefix = Join-Path $evidence 'file-failure'
    New-Item "$prefix.stderr.txt" -ItemType Directory | Out-Null
    $rejected = $false
    try {
        $null = Invoke-Consumer $shell @('-NoProfile', '-NonInteractive', '-Command',
            "[IO.File]::WriteAllText('$quotedMarker', [string]`$PID); Start-Sleep -Seconds 30") $evidence $prefix
    } catch { $rejected = $true }
    Assert-Runner $rejected 'capture-file creation failure is surfaced'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $marker) -and $watch.Elapsed.TotalSeconds -lt 3) {
        Start-Sleep -Milliseconds 50
    }
    Assert-Runner (-not (Test-Path $marker)) 'capture-file failure does not launch a child'
    $unlocked = $false
    try {
        $stream = [IO.File]::Open("$prefix.stdout.txt", [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $stream.Dispose()
        $unlocked = $true
    } catch { }
    Assert-Runner $unlocked 'partial capture stream is disposed after setup failure'

    function dotnet {
        if ($args[0] -eq '--version') { 'fixture-sdk'; $global:LASTEXITCODE = 0; return }
        if ($args[0] -eq 'restore') {
            $global:compatibilityTestRestoreCalls++
            throw 'Parameter fixture stops before restoring or building.'
        }
        throw 'Unexpected dotnet operation in parameter fixture.'
    }
    function powershell.exe { throw 'Appx operations are forbidden in parameter fixtures.' }
    foreach ($case in @(
        @{ Runtime = 'JIT'; Deployment = 'Unpackaged'; Filter = 'sEtTiNgScArD'; Control = 'SettingsCard' },
        @{ Runtime = 'AOT'; Deployment = 'LOOSE'; Filter = 'rangeSelector'; Control = 'RangeSelector' }
    )) {
        $global:compatibilityTestRestoreCalls = 0
        $caseDirectory = Join-Path $evidence $case.Control
        & (Join-Path $PSScriptRoot 'Run-Matrix.ps1') -EvidenceDirectory $caseDirectory `
            -Runtime $case.Runtime -Deployment $case.Deployment -Filter $case.Filter | Out-Null
        Assert-Runner ($global:compatibilityTestRestoreCalls -eq 1) "$($case.Control) mixed-case parameters reach the guarded restore"
        $run = Get-Content (Join-Path $caseDirectory 'run.json') -Raw | ConvertFrom-Json
        $laneResult = @(Get-Content (Join-Path $caseDirectory 'matrix.json') -Raw | ConvertFrom-Json)
        Assert-Runner (@($run.controls).Count -eq 1 -and $run.controls[0] -ceq $case.Control -and
            $run.parameters.Filter -ceq $case.Control) "$($case.Control) filter retains the declared canonical name"
        Assert-Runner ($run.parameters.Runtime[0] -ceq $case.Runtime.ToLowerInvariant() -and
            $run.parameters.Deployment[0] -ceq $case.Deployment.ToLowerInvariant() -and
            $laneResult[0].lane -ceq "$($case.Runtime.ToLowerInvariant())-$($case.Deployment.ToLowerInvariant())") `
            "$($case.Control) runtime and deployment are canonical in metadata and lane names"
        Assert-Runner ($laneResult[0].status -ceq 'failed' -and -not (Test-Path (Join-Path $caseDirectory "$($laneResult[0].lane)\blocker.json"))) `
            "$($case.Control) execution failure without a recorded blocker fails the lane"
    }
    $global:LASTEXITCODE = 0
} finally {
    if (Test-Path $marker) {
        $childId = [int][IO.File]::ReadAllText($marker)
        $child = Get-Process -Id $childId -ErrorAction SilentlyContinue
        if ($child) {
            $child.Kill($true)
            if (-not $child.WaitForExit(5000)) { throw 'Test child did not exit after cleanup.' }
            $child.Dispose()
        }
    }
    Remove-Item -LiteralPath $evidence -Recurse -Force
}
if ($failures.Count) { throw ($failures -join "`n") }
