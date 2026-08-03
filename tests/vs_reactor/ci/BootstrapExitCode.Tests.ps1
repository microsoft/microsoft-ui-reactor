<#
.SYNOPSIS
    Contract tests for the bootstrap.ps1 / Reinstall-Vsix.ps1 exit-code
    handshake (issue #1074).

.DESCRIPTION
    Headless: no build, no dotnet, no Visual Studio. Wired into
    .github/workflows/vs-reactor-lib-tests.yml. Exits non-zero on any failed
    assertion.

    Two kinds of check live here.

    (a) Behavioural. `Write-Host` does not clear $LASTEXITCODE, and bootstrap.yml
        runs `./bootstrap.ps1` *in-process* before checking $LASTEXITCODE on the
        next line. $LASTEXITCODE is a global, so a non-zero code from the VSIX
        child survived to the workflow's `if ($LASTEXITCODE -ne 0) { throw }` —
        the actual CI failure in #1074. Cases 2/3 reproduce that mechanism with
        throwaway scripts and prove which parts of the fix stop it. The `leaky`
        variant is the positive control: if it ever reads 0, the other two
        assertions are measuring nothing.

    (b) Structural. Whether bootstrap.ps1 *itself* applies that pattern cannot be
        observed without running a full bootstrap (dotnet builds, winget, VS), so
        cases 4-6 assert it over the parsed AST instead. These are deliberately
        shape assertions — here the shape *is* the fix — and each one reddens if
        the corresponding line is deleted.

    Run locally:  pwsh tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]") }
}
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add($Message) }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$bootstrap = Join-Path $repoRoot 'bootstrap.ps1'
$reinstall = Join-Path $repoRoot 'src\vs-reactor\Reinstall-Vsix.ps1'

function Get-Ast {
    param([string]$Path)
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(
        (Get-Content -LiteralPath $Path -Raw), [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        Write-Host "$(Split-Path $Path -Leaf) has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
        $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
        exit 1
    }
    return $ast
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("bootstrap-exit-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$noBom = [System.Text.UTF8Encoding]::new($false)

try {
    # -- 1. Both scripts parse. --
    $bootstrapAst = Get-Ast $bootstrap
    Get-Ast $reinstall | Out-Null
    $script:Pass += 2

    # -- 2/3. Positive control + fix, modelled on how bootstrap.yml calls us. --
    # The workflow runs `./bootstrap.ps1` *in-process* and then checks
    # $LASTEXITCODE on the next line. $LASTEXITCODE is a global, so a non-zero
    # code set by a child inside bootstrap.ps1 is still there when the step
    # inspects it — that is the #1074 mechanism, and it is why `pwsh -File`
    # (which returns 0 on fall-through) does not reproduce it.
    $pwshPath = (Get-Process -Id $PID).Path
    $child = Join-Path $tmp 'child.ps1'
    [System.IO.File]::WriteAllText($child, "exit 7`n", $noBom)

    $callChild = "& '$pwshPath' -NoLogo -NoProfile -File '$child'`n" +
                 "Write-Host ('    [warn] child failed ({0}); continuing anyway.' -f `$LASTEXITCODE)`n"

    $variants = @{
        # Pre-fix shape: warn via Write-Host, fall off the end.
        'leaky' = $callChild + "Write-Host 'Bootstrap complete.'`n"
        # Half the fix: reset the global, fall off the end.
        'reset' = $callChild + "`$global:LASTEXITCODE = 0`nWrite-Host 'Bootstrap complete.'`n"
        # Full fix as shipped: reset the global *and* exit 0 explicitly.
        'fixed' = $callChild + "`$global:LASTEXITCODE = 0`nWrite-Host 'Bootstrap complete.'`nexit 0`n"
    }
    $driverLines = New-Object System.Collections.Generic.List[string]
    foreach ($name in @('leaky', 'reset', 'fixed')) {
        $path = Join-Path $tmp "$name.ps1"
        [System.IO.File]::WriteAllText($path, $variants[$name], $noBom)
        $driverLines.Add("& '$path' | Out-Null") | Out-Null
        $driverLines.Add("Write-Output ('$name=' + `$LASTEXITCODE)") | Out-Null
    }
    $driver = Join-Path $tmp 'driver.ps1'
    [System.IO.File]::WriteAllText($driver, ($driverLines -join "`n") + "`n", $noBom)

    $observed = @{}
    foreach ($line in @(& $pwshPath -NoLogo -NoProfile -File $driver)) {
        if ($line -match '^(\w+)=(\d+)$') { $observed[$Matches[1]] = [int]$Matches[2] }
    }

    # Positive control: without the fix the code really does leak. If this ever
    # reads 0, the two assertions below are measuring nothing.
    Assert-Equal 7 $observed['leaky'] 'positive control: Write-Host alone does NOT clear a leaked child exit code'
    Assert-Equal 0 $observed['reset'] 'fix: $global:LASTEXITCODE = 0 clears the leaked code'
    Assert-Equal 0 $observed['fixed'] 'fix: reset + trailing exit 0 yields a clean exit'

    # -- 4. bootstrap.ps1 ends with an explicit `exit 0`. --
    $endBlock = $bootstrapAst.EndBlock
    $lastStatement = $endBlock.Statements[$endBlock.Statements.Count - 1]
    Assert-True ($lastStatement -is [System.Management.Automation.Language.ExitStatementAst]) `
        "bootstrap.ps1: final statement is an exit statement (found: $($lastStatement.GetType().Name))"
    if ($lastStatement -is [System.Management.Automation.Language.ExitStatementAst]) {
        Assert-Equal '0' $lastStatement.Pipeline.Extent.Text 'bootstrap.ps1: final statement is `exit 0`'
    } else {
        $script:Fail++; $script:Failures.Add('bootstrap.ps1: final statement is `exit 0`')
    }

    # -- 5. The VS-extension branch resets $LASTEXITCODE. --
    # Scope the search to the VSIX call site so an unrelated reset elsewhere in
    # the script cannot satisfy this.
    $reinstallCall = $bootstrapAst.FindAll({
        param($n)
        $n -is [System.Management.Automation.Language.CommandAst] -and
        $n.Extent.Text -match '-File \$reinstall'
    }, $true)
    Assert-Equal 1 @($reinstallCall).Count 'bootstrap.ps1: found the Reinstall-Vsix.ps1 call site'

    if (@($reinstallCall).Count -eq 1) {
        $callLine = $reinstallCall[0].Extent.StartLineNumber
        # The handling block sits immediately after the call; 40 lines is ample
        # and keeps a distant reset from counting.
        $resets = $bootstrapAst.FindAll({
            param($n)
            $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left.Extent.Text -eq '$global:LASTEXITCODE' -and
            $n.Right.Extent.Text -eq '0'
        }, $true) | Where-Object {
            $_.Extent.StartLineNumber -gt $callLine -and
            $_.Extent.StartLineNumber -le ($callLine + 40)
        }
        Assert-True (@($resets).Count -ge 1) `
            "bootstrap.ps1: the VS-extension branch resets `$global:LASTEXITCODE (searched lines $($callLine + 1)-$($callLine + 40))"
    }

    # -- 6. Reinstall-Vsix.ps1 exposes the documented exit codes. --
    $reinstallAst = Get-Ast $reinstall
    $exitCodes = @($reinstallAst.FindAll({
        param($n) $n -is [System.Management.Automation.Language.ExitStatementAst]
    }, $true) | ForEach-Object {
        if ($null -ne $_.Pipeline) { $_.Pipeline.Extent.Text } else { '' }
    })
    Assert-True ($exitCodes -contains '3') 'Reinstall-Vsix.ps1: signals the /updateconfiguration timeout with `exit 3`'
    Assert-True ($exitCodes -contains '0') 'Reinstall-Vsix.ps1: signals full success with an explicit `exit 0`'

    # bootstrap.ps1 must actually branch on 3 rather than lumping it into the
    # generic failure warning — otherwise it prints `[ok] VS extension installed`
    # for a run whose pkgdef merge never happened.
    $bootstrapText = Get-Content -LiteralPath $bootstrap -Raw
    Assert-True ($bootstrapText -match '\$vsixExit -eq 3') 'bootstrap.ps1: special-cases Reinstall-Vsix.ps1 exit code 3'
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# -- Report --
Write-Host ""
Write-Host "Bootstrap exit-code tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
