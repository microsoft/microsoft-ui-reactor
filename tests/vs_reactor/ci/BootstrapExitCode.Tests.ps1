<#
.SYNOPSIS
    Contract tests for the bootstrap.ps1 / Reinstall-Vsix.ps1 exit-code
    handshake (issue #1074).

.DESCRIPTION
    Headless: no build, no dotnet, no Visual Studio. Wired into
    .github/workflows/vs-reactor-lib-tests.yml, which runs this file under BOTH
    pwsh and Windows PowerShell 5.1 — so nothing here may use PowerShell 6+
    syntax. Exits non-zero on any failed assertion.

    Three kinds of check live here.

    (a) Behavioural. `Write-Host` does not clear $LASTEXITCODE, and bootstrap.yml
        runs `./bootstrap.ps1` *in-process* before checking $LASTEXITCODE on the
        next line. $LASTEXITCODE is a global, so a non-zero code from the VSIX
        child survived to the workflow's `if ($LASTEXITCODE -ne 0) { throw }` —
        the actual CI failure in #1074. Case 2 reproduces that mechanism with
        throwaway scripts and proves which parts of the fix stop it. The `leaky`
        variant is the positive control: if it ever reads 0, the other two
        assertions are measuring nothing.

    (b) Structural. Whether bootstrap.ps1 and Reinstall-Vsix.ps1 *themselves*
        apply that pattern cannot be observed without running a full bootstrap
        (dotnet builds, winget, VS), so cases 3-5 assert it over the parsed AST
        instead. These are deliberately shape assertions — here the shape *is*
        the fix — and each reddens if the corresponding line is deleted. Note
        they bind an exit code to the variable that guards it, not merely to its
        presence: `if ($false) { exit 3 }` must fail, and does.

    (c) Doc/code agreement. Case 6 derives the exit codes from the AST and from
        the two places they are documented, and requires the three sets to be
        equal. A contract nobody can trust is worse than no contract, and the
        cheapest way for this one to rot is for a new exit path to skip the
        table.

    Run locally:  pwsh tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
                  powershell -File tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$bootstrap = Join-Path $repoRoot 'bootstrap.ps1'
$reinstall = Join-Path $repoRoot 'src\vs-reactor\Reinstall-Vsix.ps1'
$vsProcessLib = Join-Path $repoRoot 'src\vs-reactor\VsProcessLib.ps1'
$testingDoc = Join-Path $repoRoot 'src\vs-reactor\TESTING.md'

# These files are BOM-less UTF-8. Windows PowerShell 5.1 decodes such files as
# ANSI, which corrupts every non-ASCII comment character and yields spurious
# parse errors, so read them with an explicit encoding rather than -Raw.
function Get-Utf8Text {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Get-Ast {
    param([string]$Path)
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(
        (Get-Utf8Text $Path), [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        Write-Host "$(Split-Path $Path -Leaf) has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
        $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
        exit 1
    }
    return $ast
}

# True when the script contains `if (<Condition>) { ... exit <Code> ... }`.
# Binding the code to its guard is what stops `if ($false) { exit 3 }` from
# satisfying the contract.
function Test-GuardedExit {
    param($Ast, [string]$Condition, [string]$Code)
    foreach ($if in @($Ast.FindAll({
                    param($n) $n -is [System.Management.Automation.Language.IfStatementAst]
                }, $true))) {
        foreach ($clause in $if.Clauses) {
            if ($clause.Item1.Extent.Text.Trim() -ne $Condition) { continue }
            foreach ($e in @($clause.Item2.FindAll({
                            param($n) $n -is [System.Management.Automation.Language.ExitStatementAst]
                        }, $true))) {
                if ($null -ne $e.Pipeline -and $e.Pipeline.Extent.Text.Trim() -eq $Code) { return $true }
            }
        }
    }
    return $false
}

# Number of `<Name> = $true` assignments in the script.
function Get-TrueAssignmentCount {
    param($Ast, [string]$Name)
    return @($Ast.FindAll({
                param($n)
                $n -is [System.Management.Automation.Language.AssignmentStatementAst]
            }, $true) | Where-Object {
            $_.Left.Extent.Text -eq $Name -and $_.Right.Extent.Text -eq '$true'
        }).Count
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("bootstrap-exit-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$noBom = New-Object System.Text.UTF8Encoding($false)

try {
    # -- 1. Both scripts parse. --
    $bootstrapAst = Get-Ast $bootstrap
    $reinstallAst = Get-Ast $reinstall
    $script:Pass += 2

    # -- 2. Positive control + fix, modelled on how bootstrap.yml calls us. --
    # The workflow runs `./bootstrap.ps1` *in-process* and then checks
    # $LASTEXITCODE on the next line. $LASTEXITCODE is a global, so a non-zero
    # code set by a child inside bootstrap.ps1 is still there when the step
    # inspects it — that is the #1074 mechanism, and it is why `pwsh -File`
    # (which returns 0 on fall-through) does not reproduce it.
    $hostPath = (Get-Process -Id $PID).Path
    $child = Join-Path $tmp 'child.ps1'
    [System.IO.File]::WriteAllText($child, "exit 7`n", $noBom)

    $callChild = "& '$hostPath' -NoLogo -NoProfile -File '$child'`n" +
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
    foreach ($line in @(& $hostPath -NoLogo -NoProfile -File $driver)) {
        if ($line -match '^(\w+)=(\d+)$') { $observed[$Matches[1]] = [int]$Matches[2] }
    }

    # Positive control: without the fix the code really does leak. If this ever
    # reads 0, the two assertions below are measuring nothing.
    Assert-Equal 7 $observed['leaky'] 'positive control: Write-Host alone does NOT clear a leaked child exit code'
    Assert-Equal 0 $observed['reset'] 'fix: $global:LASTEXITCODE = 0 clears the leaked code'
    Assert-Equal 0 $observed['fixed'] 'fix: reset + trailing exit 0 yields a clean exit'

    # -- 3. bootstrap.ps1 ends with an explicit `exit 0`. --
    $endBlock = $bootstrapAst.EndBlock
    $lastStatement = $endBlock.Statements[$endBlock.Statements.Count - 1]
    Assert-True ($lastStatement -is [System.Management.Automation.Language.ExitStatementAst]) `
        "bootstrap.ps1: final statement is an exit statement (found: $($lastStatement.GetType().Name))"
    if ($lastStatement -is [System.Management.Automation.Language.ExitStatementAst]) {
        Assert-Equal '0' $lastStatement.Pipeline.Extent.Text 'bootstrap.ps1: final statement is `exit 0`'
    }
    else {
        $script:Fail++; $script:Failures.Add('bootstrap.ps1: final statement is `exit 0`')
    }

    # -- 4. The VS-extension branch resets $LASTEXITCODE and branches on 3. --
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

    # bootstrap.ps1 must actually branch on 3 rather than lumping it into the
    # generic failure warning — otherwise it prints `[ok] VS extension installed`
    # for a run whose pkgdef merge never happened.
    Assert-True ((Get-Utf8Text $bootstrap) -match '\$vsixExit -eq 3') 'bootstrap.ps1: special-cases Reinstall-Vsix.ps1 exit code 3'

    # -- 5. Reinstall-Vsix.ps1's exit codes are bound to their guards. --
    # Presence alone would be satisfied by dead code; these fail if the guard is
    # neutered (verified by mutating each condition to `$false`).
    Assert-True (Test-GuardedExit $reinstallAst '$updateConfigIncomplete' '3') `
        'Reinstall-Vsix.ps1: `exit 3` is guarded by $updateConfigIncomplete'
    Assert-True (Test-GuardedExit $reinstallAst '$installIncomplete' '1') `
        'Reinstall-Vsix.ps1: `exit 1` is guarded by $installIncomplete'

    # ...and the guards are actually raised. A guard nothing sets is the same as
    # no exit path at all. $updateConfigIncomplete has two producers — the timeout
    # itself and the missing-devenv branch — and both mean "installed, not merged".
    Assert-True ((Get-TrueAssignmentCount $reinstallAst '$updateConfigIncomplete') -ge 2) `
        'Reinstall-Vsix.ps1: both /updateconfiguration skip paths set $updateConfigIncomplete'
    Assert-True ((Get-TrueAssignmentCount $reinstallAst '$installIncomplete') -ge 1) `
        'Reinstall-Vsix.ps1: the duplicate-install branch sets $installIncomplete'

    $tail = $reinstallAst.EndBlock.Statements[$reinstallAst.EndBlock.Statements.Count - 1]
    Assert-True ($tail -is [System.Management.Automation.Language.ExitStatementAst]) `
        "Reinstall-Vsix.ps1: falls through to an explicit exit (found: $($tail.GetType().Name))"

    # -- 6. The code and both documented tables agree on the exit codes. --
    # Literal exits only: `exit $LASTEXITCODE` forwards a child's code and is
    # unreachable anyway (Write-Error throws first under $ErrorActionPreference
    # = 'Stop'), so it is not part of the contract.
    $actual = @($reinstallAst.FindAll({
                param($n) $n -is [System.Management.Automation.Language.ExitStatementAst]
            }, $true) | ForEach-Object {
            if ($null -ne $_.Pipeline) { $_.Pipeline.Extent.Text.Trim() } else { '0' }
        } | Where-Object { $_ -match '^\d+$' } | Sort-Object -Unique)
    Assert-True ($actual.Count -ge 2) "contract: Reinstall-Vsix.ps1 has literal exit codes to check (found: $($actual -join ', '))"

    # The .OUTPUTS help block lists them as "  <code>  <description>".
    $helpBlock = ''
    if ((Get-Utf8Text $reinstall) -match '(?s)\.OUTPUTS(.*?)#>') { $helpBlock = $Matches[1] }
    $documented = @([regex]::Matches($helpBlock, '(?m)^\s{4,}(\d+)\s{2,}\S') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    Assert-Equal ($actual -join ',') ($documented -join ',') `
        'contract: Reinstall-Vsix.ps1 .OUTPUTS documents exactly the exit codes the script can return'

    # TESTING.md carries the same table for humans who never open the script.
    $docTable = @([regex]::Matches((Get-Utf8Text $testingDoc), '(?m)^\|\s*`(\d+)`\s*\|') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    Assert-Equal ($actual -join ',') ($docTable -join ',') `
        'contract: src/vs-reactor/TESTING.md documents exactly the exit codes the script can return'

    # -- 7. No carriage return outside a CRLF pair. --
    # These files are CRLF. An edit that inserts a bare LF leaves a mid-line CR,
    # which merges the following statement onto the brace line — still valid
    # PowerShell, so it survives a parse check and every behavioural assertion
    # above, and is near-invisible in a diff. It happened once in this PR's own
    # history; four lines of guard is cheaper than the next reviewer finding it.
    foreach ($f in @($bootstrap, $reinstall, $vsProcessLib)) {
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $loneCr = 0
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i] -ne 13) { continue }
            if (($i + 1) -ge $bytes.Length -or $bytes[$i + 1] -ne 10) { $loneCr++ }
        }
        Assert-Equal 0 $loneCr "$(Split-Path $f -Leaf): no carriage return outside a CRLF pair"
    }
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
