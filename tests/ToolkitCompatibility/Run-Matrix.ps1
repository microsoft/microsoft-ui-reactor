[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [ValidateNotNullOrEmpty()][ValidateSet('jit', 'aot')][string[]]$Runtime = @('jit', 'aot'),
    [ValidateNotNullOrEmpty()][ValidateSet('unpackaged', 'loose', 'msix')][string[]]$Deployment = @('unpackaged', 'loose', 'msix'),
    [string]$Filter = '',
    [int]$TimeoutSeconds = 60,
    [switch]$AllowCamera,
    [switch]$CameraProbeOnly,
    [switch]$PrepareMsixOnly,
    [string]$ConsumerProject = '',
    [switch]$NativeBaseline
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Runtime = @($Runtime | ForEach-Object { $_.ToLowerInvariant() })
$Deployment = @($Deployment | ForEach-Object { $_.ToLowerInvariant() })
if ($PSBoundParameters.ContainsKey('Runtime')) { $PSBoundParameters['Runtime'] = $Runtime }
if ($PSBoundParameters.ContainsKey('Deployment')) { $PSBoundParameters['Deployment'] = $Deployment }
. (Join-Path $PSScriptRoot 'ResultProtocol.ps1')
$project = Join-Path $PSScriptRoot 'ToolkitCompatibility.csproj'
if ($ConsumerProject) { $project = (Resolve-Path $ConsumerProject).Path }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path $evidence) {
    throw "Evidence directory already exists. Choose a new run directory: $evidence"
}
New-Item $evidence -ItemType Directory | Out-Null
$run = [Guid]::NewGuid().ToString('N')
$lanes = [Collections.Generic.List[object]]::new()
$mapping = Join-Path $repo 'samples\apps\wct-controls\WctControls.cs'
$runnerRevision = (& git -C $repo rev-parse HEAD).Trim()
$sourceRevision = $runnerRevision
$consumerProvenance = $null
if ($ConsumerProject) {
    $sourceRoot = Split-Path (Split-Path $project)
    $consumerProvenance = Get-Content (Join-Path $sourceRoot 'consumer.json') -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($consumerProvenance.project) -ne $project) {
        throw 'Prepared consumer provenance refers to a different project.'
    }
    $sourceRevision = $consumerProvenance.revision
    $mapping = Join-Path $sourceRoot 'sample\WctControls.cs'
    if ((Get-FileHash $mapping -Algorithm SHA256).Hash -ne $consumerProvenance.sampleMappingsSha256) {
        throw 'Prepared consumer mappings changed after staging.'
    }
    $package = Join-Path $sourceRoot "feed\Microsoft.UI.Reactor.$($consumerProvenance.packageVersion).nupkg"
    if ((Get-FileHash $package -Algorithm SHA256).Hash -ne $consumerProvenance.packageSha256) {
        throw 'Prepared Reactor package changed after staging.'
    }
    foreach ($file in $consumerProvenance.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $sourceRoot $file.path))
        if (-not $path.StartsWith($sourceRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
            (Get-FileHash $path -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Prepared source changed or escaped its snapshot: $($file.path)"
        }
    }
}
$declarations = [regex]::Matches([IO.File]::ReadAllText($mapping),
    '\[GenerateReactorWrapper\(typeof\(CommunityToolkit\.WinUI\.Controls\.(\w+)\)')
$controls = @($declarations | ForEach-Object { $_.Groups[1].Value })
if ($controls.Count -ne 25 -or @($controls | Sort-Object -Unique).Count -ne 25) {
    throw "Expected exactly 25 distinct actual sample mappings, found $($controls.Count)."
}
if ($CameraProbeOnly) {
    if ($Filter -and $Filter -ne 'CameraPreview') { throw 'CameraProbeOnly cannot select another control.' }
    $Filter = 'CameraPreview'
}
if ($Filter) {
    $matched = @($controls | Where-Object { $_ -ieq $Filter })
    if ($matched.Count -ne 1) { throw "Unknown filter $Filter" }
    $Filter = $matched[0]
    $controls = @($Filter)
    if ($PSBoundParameters.ContainsKey('Filter')) { $PSBoundParameters['Filter'] = $Filter }
}
if ($NativeBaseline -and $Filter -notin @('SettingsCard', 'SettingsExpander', 'ColorPicker', 'TabbedCommandBar', 'RichSuggestBox', 'RadialGauge')) {
    throw 'Direct-native diagnostics require a supported single-control filter.'
}
@{
    run = $run
    revision = $sourceRevision
    runnerRevision = $runnerRevision
    preparedConsumer = $consumerProvenance
    dirty = @(& git -C $repo status --short)
    mappingSha256 = (Get-FileHash $mapping -Algorithm SHA256).Hash
    sdk = (& dotnet --version)
    os = [Environment]::OSVersion.VersionString
    controls = $controls
    project = $project
    parameters = $PSBoundParameters
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'run.json')

function Invoke-Logged([string]$Log, [string]$Executable, [string[]]$Arguments) {
    "$Executable $($Arguments -join ' ')" | Add-Content (Join-Path $evidence 'commands.txt')
    & $Executable @Arguments *> $Log
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "$Executable exited $code. See $Log" }
}

function Invoke-Appx([string]$Operation, [string]$Path, [string]$Name, [string]$Log, [switch]$AllUsers) {
    # Appx is a Windows PowerShell module. Base64 avoids quoting paths into executable code.
    $quotedPath = "'" + $Path.Replace("'", "''") + "'"
    $quotedName = "'" + $Name.Replace("'", "''") + "'"
    $scope = if ($AllUsers) { ' -AllUsers' } else { '' }
    $command = switch ($Operation) {
        'register' { "Add-AppxPackage -Register $quotedPath -ErrorAction Stop" }
        'install' { "Add-AppxPackage -Path $quotedPath -AllowUnsigned -ErrorAction Stop" }
        'query' { "Get-AppxPackage$scope -Name $quotedName | Select-Object Name,PackageFullName,InstallLocation,IsDevelopmentMode | ConvertTo-Json -Compress" }
        'remove' { "Remove-AppxPackage$scope -Package $quotedName -ErrorAction Stop" }
        default { throw "Unknown deployment operation." }
    }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
        "`$ErrorActionPreference='Stop'; $command"))
    Invoke-Logged $Log 'powershell.exe' @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
}

function Invoke-Consumer([string]$Executable, [string[]]$Arguments, [string]$Directory, [string]$Prefix) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $Directory
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    "$Executable $($Arguments -join ' ')" | Add-Content (Join-Path $evidence 'commands.txt')
    $process = $null
    $stdoutFile = $null
    $stderrFile = $null
    try {
        $stdoutFile = [IO.File]::Create("$Prefix.stdout.txt")
        $stderrFile = [IO.File]::Create("$Prefix.stderr.txt")
        $process = [Diagnostics.Process]::Start($start)
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) {
            $process.Kill($true)
            if (-not $process.WaitForExit(5000)) { throw 'Owned process did not exit after termination.' }
        }
        $captureComplete = $true
        try {
            [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdoutTask, $stderrTask)).
                WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
        } catch {
            $captureComplete = $false
            $_.Exception.ToString() | Set-Content "$Prefix.capture-error.txt"
            $process.StandardOutput.Close()
            $process.StandardError.Close()
        }
        $stdoutFile.Flush()
        $stderrFile.Flush()
        $exitCode = $process.ExitCode
    } finally {
        try {
            if ($process -and -not $process.HasExited) {
                $process.Kill($true)
                if (-not $process.WaitForExit(5000)) { throw 'Owned process did not exit during cleanup.' }
            }
        } finally {
            try { if ($stdoutFile) { $stdoutFile.Dispose() } }
            finally {
                try { if ($stderrFile) { $stderrFile.Dispose() } }
                finally { if ($process) { $process.Dispose() } }
            }
        }
    }
    [pscustomobject]@{
        Stdout = [IO.File]::ReadAllText("$Prefix.stdout.txt")
        ExitCode = $exitCode
        TimedOut = $timedOut
        CaptureComplete = $captureComplete
    }
}

foreach ($runtimeName in $Runtime) {
    foreach ($deploymentName in $Deployment) {
        $laneName = "$runtimeName-$deploymentName"
        $lane = Join-Path $evidence $laneName
        $layout = Join-Path $lane 'layout'
        New-Item $lane -ItemType Directory | Out-Null
        $identity = "Rct.Wct.$run.$runtimeName.$deploymentName"
        $alias = "toolkit-compat-$run-$laneName.exe"
        $ownedFullName = $null
        $deploymentSucceeded = $false
        $allUsers = $deploymentName -eq 'msix'
        $status = 'blocked'
        $detail = ''
        $exitCode = $null
        try {
            $manifestPath = Join-Path $lane 'Package.appxmanifest'
            [xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw
            $manifest.Package.Identity.Name = $identity
            if ($deploymentName -ne 'msix') {
                $manifest.Package.Identity.Publisher = 'CN=Reactor.ToolkitCompatibility'
            }
            $aliasNode = $manifest.SelectSingleNode("//*[local-name()='ExecutionAlias']")
            $aliasNode.SetAttribute('Alias', $alias)
            $manifest.Save($manifestPath)
            $properties = @('-p:Platform=x64', '-p:SkipSignaturesGen=true',
                "-p:CompatibilityAot=$($runtimeName -eq 'aot')",
                "-p:CompatibilityPackaged=$($deploymentName -ne 'unpackaged')",
                "-p:CompatibilityNativeBaseline=$([bool]$NativeBaseline)",
                "-p:CompatibilityManifest=$manifestPath")
            Invoke-Logged (Join-Path $lane 'restore.log') 'dotnet' (
                @('restore', $project, '-r', 'win-x64', '--verbosity', 'quiet') + $properties)
            $assetQuery = Join-Path $lane 'effective-build-properties.json'
            Invoke-Logged $assetQuery 'dotnet' (@('msbuild', $project, '-p:Configuration=Release',
                '-getProperty:ProjectAssetsFile,PublishTrimmed,PublishReadyToRun,PublishAot,WindowsPackageType,TargetFramework,RuntimeIdentifier') + $properties)
            $effective = (Get-Content $assetQuery -Raw | ConvertFrom-Json).Properties
            $assetsPath = $effective.ProjectAssetsFile
            if (-not [IO.Path]::IsPathFullyQualified($assetsPath) -or -not (Test-Path $assetsPath)) {
                throw 'Cannot resolve the exact restored assets file.'
            }
            Copy-Item $assetsPath (Join-Path $lane 'resolved-assets.json')
            if ($consumerProvenance) {
                $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
                $expectedPackage = "Microsoft.UI.Reactor/$($consumerProvenance.packageVersion)"
                $library = @($assets.libraries.PSObject.Properties | Where-Object Name -IEQ $expectedPackage)
                if ($library.Count -ne 1 -or $library[0].Value.type -ne 'package') {
                    throw "Consumer did not resolve the exact prepared package $expectedPackage."
                }
            }
            Invoke-Logged (Join-Path $lane 'publish.log') 'dotnet' (
                @('publish', $project, '--no-restore', '-c', 'Release', '-r', 'win-x64',
                    '--self-contained', '-o', $layout, '-m:1', '-nr:false', '--verbosity', 'quiet') + $properties)
            if (-not (Test-Path (Join-Path $layout 'ToolkitCompatibility.exe'))) {
                throw 'No published executable.'
            }
            if ($consumerProvenance -and $runtimeName -eq 'jit' -and
                $effective.PublishTrimmed -ne 'true' -and $effective.PublishReadyToRun -ne 'true' -and
                (Get-FileHash (Join-Path $layout 'Reactor.dll') -Algorithm SHA256).Hash -ne $consumerProvenance.reactorAssemblySha256) {
                throw 'Published Reactor.dll differs from the assembly inside the prepared nupkg.'
            }
            @(Get-ChildItem $layout -Recurse -File | Where-Object Extension -In @('.exe', '.dll', '.pri', '.xbf') |
                ForEach-Object {
                    @{ path = [IO.Path]::GetRelativePath($layout, $_.FullName)
                       sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
                }) | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $lane 'published-files.json')
            $executable = Join-Path $layout 'ToolkitCompatibility.exe'
            $expectedRoot = $layout
            if ($deploymentName -ne 'unpackaged') {
                Copy-Item $manifestPath (Join-Path $layout 'AppxManifest.xml')
                if ($deploymentName -eq 'msix') {
                    $archive = Join-Path $lane 'ToolkitCompatibility.msix'
                    Invoke-Logged (Join-Path $lane 'package.log') 'winapp' @(
                        'package', $layout, '--manifest', (Join-Path $layout 'AppxManifest.xml'),
                        '--no-sign', '--output', $archive)
                    $escapedArchive = $archive.Replace("'", "''")
                    @{
                        archive = $archive
                        sha256 = (Get-FileHash $archive -Algorithm SHA256).Hash
                        expectedIdentity = $identity
                        publisher = $manifest.Package.Identity.Publisher
                        alias = $alias
                        executable = 'ToolkitCompatibility.exe'
                        sourceRevision = $sourceRevision
                        runnerRevision = $runnerRevision
                        preparedConsumer = $consumerProvenance
                        lane = $laneName
                        installationScope = 'all-users'
                        cleanupScope = 'all-users'
                        requiresAdministrator = $true
                        installCommand = "Add-AppxPackage -Path '$escapedArchive' -AllowUnsigned -ErrorAction Stop"
                    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $lane 'installation-request.json')
                    if ($PrepareMsixOnly) {
                        # The only approval blocker; an archive alone does not mean installation was withheld.
                        @{ reasonKind = 'approval'; detail = 'Real MSIX archive prepared; installation is approval-blocked and was not attempted.' } |
                            ConvertTo-Json | Set-Content (Join-Path $lane 'blocker.json')
                        throw 'Real MSIX archive prepared. Installation is approval-blocked; no installation was attempted.'
                    }
                }
                $preflight = Join-Path $lane 'preexisting-registration.json'
                Invoke-Appx 'query' '' $identity $preflight -AllUsers:$allUsers
                if (-not [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($preflight))) {
                    throw 'Per-run identity already exists; refusing to replace or remove it.'
                }
                if ($deploymentName -eq 'loose') {
                    Invoke-Appx 'register' (Join-Path $layout 'AppxManifest.xml') $identity (Join-Path $lane 'register.log')
                    $deploymentSucceeded = $true
                } else {
                    Invoke-Appx 'install' $archive $identity (Join-Path $lane 'install.log')
                    $deploymentSucceeded = $true
                }
                $queryLog = Join-Path $lane 'registration.json'
                Invoke-Appx 'query' '' $identity $queryLog -AllUsers:$allUsers
                $registration = @(Get-Content $queryLog -Raw | ConvertFrom-Json)
                if ($registration.Count -ne 1 -or $registration[0].Name -ne $identity) {
                    throw 'Installed identity did not uniquely match owned run.'
                }
                $ownedFullName = $registration[0].PackageFullName
                $expectedRoot = $registration[0].InstallLocation
                if ($deploymentName -eq 'msix' -and (
                    $registration[0].IsDevelopmentMode -or
                    [IO.Path]::GetFullPath($expectedRoot) -eq [IO.Path]::GetFullPath($layout))) {
                    throw 'MSIX lane resolved a development/build layout, not an installed package.'
                }
                $executable = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\$alias"
            }
            $inventoryFlag = if ($CameraProbeOnly) { '--list-camera-probe' } else { '--list-scenarios' }
            $inventory = Invoke-Consumer $executable @($inventoryFlag) $expectedRoot (Join-Path $lane 'inventory')
            if ($inventory.TimedOut -or -not $inventory.CaptureComplete -or $inventory.ExitCode -ne 0) {
                throw 'Inventory process failed.'
            }
            $expectedKeys = @($inventory.Stdout -split '\r?\n' | Where-Object {
                $_ -eq 'environment/identity-runtime-location' -or
                ($_.Split('/')[0] -cin $controls)
            })
            $minimumCount = if ($CameraProbeOnly) { 2 } else { 1 + 9 * $controls.Count }
            if ($expectedKeys.Count -lt $minimumCount -or
                @($expectedKeys | Sort-Object -Unique).Count -ne $expectedKeys.Count) {
                throw 'Executable inventory does not cover the expected sample controls.'
            }
            foreach ($control in @($controls | Where-Object { -not $CameraProbeOnly })) {
                foreach ($phase in @('mount', 'update', 'remove', 'remount')) {
                    if ("$control/$phase" -cnotin $expectedKeys) { throw "Missing inventory operation $control/$phase." }
                }
            }
            # Only an inventory that completed and passed the coverage checks may plan the summary.
            $expectedKeys | Set-Content -LiteralPath (Join-Path $lane 'inventory.validated.txt')
            $controlOutcomes = [Collections.Generic.List[object]]::new()
            foreach ($control in $controls) {
                $controlKeys = @($expectedKeys | Where-Object {
                    $_ -eq 'environment/identity-runtime-location' -or $_.StartsWith("$control/")
                })
                $resultPath = Join-Path $lane "$control.results.jsonl"
                $controlStatus = 'failed'
                $controlExit = $null
                $execution = $null
                try {
                    $arguments = @('--output', $resultPath, '--deployment', $deploymentName,
                        '--runtime', $runtimeName, '--expected-root', $expectedRoot,
                        '--expected-identity', $identity, '--filter', $control,
                        '--expected-variant', $(if ($NativeBaseline) { 'direct-native' } else { 'sample-mappings' }))
                    if ($AllowCamera) { $arguments += @('--allow-camera', 'true') }
                    if ($CameraProbeOnly) { $arguments += @('--camera-probe-only', 'true') }
                    $execution = Invoke-Consumer $executable $arguments $expectedRoot (Join-Path $lane $control)
                    $controlExit = $execution.ExitCode
                    if ($execution.TimedOut) { throw "Timeout after $TimeoutSeconds seconds." }
                    if (-not $execution.CaptureComplete) { throw 'Output capture did not complete.' }
                    if (-not (Test-Path $resultPath)) { throw "No result file; exit $controlExit." }
                    $results = @(Get-Content $resultPath | ForEach-Object { $_ | ConvertFrom-Json })
                    $verdict = Test-CompatibilityResults $execution.Stdout $results $controlKeys $controlExit
                    $controlStatus = $verdict.Status
                    $controlDetail = $verdict.Detail
                } catch {
                    $controlDetail = $_.Exception.ToString()
                }
                $controlOutcomes.Add([pscustomobject]@{
                    control = $control; status = $controlStatus; detail = $controlDetail; exitCode = $controlExit
                    execution = if ($execution) {
                        @{ timedOut = $execution.TimedOut; captureComplete = $execution.CaptureComplete }
                    } else { $null }
                })
                $controlOutcomes | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $lane 'controls.json')
            }
            $failures = @($controlOutcomes | Where-Object status -eq 'failed').Count
            $blocks = @($controlOutcomes | Where-Object status -eq 'blocked').Count
            $status = if ($failures) { 'failed' } elseif ($blocks) { 'blocked' } else { 'passed' }
            $detail = "$($controlOutcomes.Count) controls, $failures failed, $blocks blocked."
        } catch {
            $detail = $_.Exception.ToString()
            $detail | Set-Content (Join-Path $lane 'error.txt')
            # Only an explicitly recorded blocker keeps the lane blocked; anything else is an execution failure.
            $status = if (Test-Path (Join-Path $lane 'blocker.json')) { 'blocked' } else { 'failed' }
        } finally {
            if ($deploymentSucceeded -and -not $ownedFullName) {
                try {
                    $recoveryLog = Join-Path $lane 'cleanup-lookup.json'
                    Invoke-Appx 'query' '' $identity $recoveryLog -AllUsers:$allUsers
                    $recovered = @(Get-Content $recoveryLog -Raw | ConvertFrom-Json)
                    if ($recovered.Count -ne 1 -or $recovered[0].Name -ne $identity) {
                        throw 'Cannot uniquely verify the successful deployment for cleanup.'
                    }
                    $ownedFullName = $recovered[0].PackageFullName
                } catch {
                    $status = 'failed'
                    $detail += " Cleanup ownership lookup failed for $identity`: $($_.Exception.Message)"
                }
            }
            if ($ownedFullName) {
                try {
                    Invoke-Appx 'remove' '' $ownedFullName (Join-Path $lane 'cleanup.log') -AllUsers:$allUsers
                    $verifyLog = Join-Path $lane 'cleanup-verification.json'
                    Invoke-Appx 'query' '' $identity $verifyLog -AllUsers:$allUsers
                    if (-not [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($verifyLog))) {
                        throw 'Owned package is still registered after cleanup.'
                    }
                }
                catch {
                    $status = 'failed'
                    $detail += " Cleanup failed for exact owned package $ownedFullName`: $($_.Exception.Message)"
                }
            }
            $lanes.Add([pscustomobject]@{
                lane = $laneName; status = $status; detail = $detail; exitCode = $exitCode
            })
            $lanes | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'matrix.json')
        }
    }
}
$lanes | Format-Table lane, status, exitCode, detail -Wrap
if (@($lanes | Where-Object status -ne 'passed').Count) { exit 1 }
