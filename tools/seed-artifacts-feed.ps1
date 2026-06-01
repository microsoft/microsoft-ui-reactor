param(
    [Parameter(Mandatory = $true)]
    [string]$FeedSource,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"

function Read-Manifest {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Manifest not found: $Path"
    }

    $items = @()
    $lines = Get-Content -Path $Path
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        if ($trimmed.StartsWith("#")) { continue }

        $parts = $trimmed.Split("|", 2)
        if ($parts.Count -ne 2) {
            throw "Invalid manifest line: '$line'. Expected: PackageId|Version"
        }

        $packageId = $parts[0].Trim()
        $version = $parts[1].Trim()
        if ([string]::IsNullOrWhiteSpace($packageId) -or [string]::IsNullOrWhiteSpace($version)) {
            throw "Invalid manifest line: '$line'. PackageId/Version cannot be empty."
        }

        $items += [pscustomobject]@{
            PackageId = $packageId
            Version = $version
        }
    }

    return $items
}

function Test-PackageViaRestore {
    param(
        [string]$PackageId,
        [string]$Version,
        [string]$Source,
        [string]$WorkDir,
        [string]$PackagesDir
    )

    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

    $projPath = Join-Path $WorkDir "Probe.csproj"
    $projXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$PackageId" Version="$Version" />
  </ItemGroup>
</Project>
"@
    Set-Content -Path $projPath -Value $projXml -Encoding UTF8

    # Force the package to be pulled *through* the feed so Azure Artifacts saves
    # it from its upstream source. We must bypass the machine-wide global-packages
    # cache (%USERPROFILE%\.nuget\packages); otherwise dotnet resolves an already
    # present version locally, never contacts the feed, and the upstream save that
    # actually populates the ADO feed is never triggered. A shared isolated
    # --packages dir + --no-http-cache guarantees a real feed download for each
    # target version while deduping transitive deps so the disk doesn't fill up.
    $logPath = Join-Path $WorkDir "restore.log"
    dotnet restore $projPath --source $Source --packages $PackagesDir --no-http-cache --verbosity minimal *> $logPath
    if ($LASTEXITCODE -eq 0) {
        return "AvailableOrHydrated"
    }

    $log = ""
    if (Test-Path $logPath) {
        $log = Get-Content -Path $logPath -Raw
    }

    if ($log -match "NU1101|NU1102") {
        return "Missing"
    }

    if ($log -match "NU1201|NU1202|NU1212|NU1213|NU1701") {
        # Package exists but is not compatible with probe TFM/package type.
        return "AvailableOrHydrated"
    }

    return "UnknownFailure"
}

$manifestItems = Read-Manifest -Path $ManifestPath

$workRoot = Join-Path $env:TEMP ("reactor-feed-seed-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

# Shared isolated global-packages folder for the whole run: bypasses the machine
# cache (forcing real feed pulls) but dedupes transitive deps across probes.
$packagesDir = Join-Path $workRoot "_pkgs"
New-Item -ItemType Directory -Path $packagesDir -Force | Out-Null

$results = @()

try {
    foreach ($item in $manifestItems) {
        $id = $item.PackageId
        $version = $item.Version

        Write-Host "Hydrating/checking $id $version from feed ..."
        $itemWork = Join-Path $workRoot ($id.Replace('.', '_') + "_" + $version)
        New-Item -ItemType Directory -Path $itemWork -Force | Out-Null
        $status = "Missing"
        try {
            $status = Test-PackageViaRestore -PackageId $id -Version $version -Source $FeedSource -WorkDir $itemWork -PackagesDir $packagesDir
        } catch {
            Write-Warning "Failed to hydrate $id ${version}: $($_.Exception.Message)"
            $status = "UnknownFailure"
        }

        if ($status -eq "AvailableOrHydrated") {
            $results += [pscustomobject]@{ PackageId = $id; Version = $version; Status = $status; Source = "Feed/Upstream" }
        } elseif ($status -eq "UnknownFailure") {
            $results += [pscustomobject]@{ PackageId = $id; Version = $version; Status = $status; Source = "Check restore.log" }
        } else {
            $results += [pscustomobject]@{ PackageId = $id; Version = $version; Status = "Missing"; Source = "" }
        }
    }
}
finally {
    $reportPath = Join-Path (Get-Location) "feed-seed-report.csv"
    $results | Export-Csv -NoTypeInformation -Path $reportPath
    Write-Host "Report: $reportPath"
    $results | Group-Object Status | Sort-Object Name | ForEach-Object {
        Write-Host ("{0}: {1}" -f $_.Name, $_.Count)
    }

    if (Test-Path $workRoot) {
        Remove-Item -Path $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
