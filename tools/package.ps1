# Builds a pyACAD release: Release binaries for every supported AutoCAD year, a staged
# payload, and the per-user setup.exe.
#
#   1. dotnet build -c Release -p:AcadVersion=<year>   for 2021-2026. Any build error
#      is fatal (the AutoCAD references come from NuGet, so every year can build).
#   2. dist\stage\bundle\    PackageContents.xml + Contents\<year>\PyAcad.dll
#      dist\stage\appdata\   <year>\runtime\ (pyacadlib inside it, no .pdb),
#                            extensions\pyACAD.extension\ (with its .pyacad-manifest)
#                            and cli\pyacad.exe
#      dist\stage\manifests.txt   every release's manifest, for the installer's upgrade
#                            guard; this release's is also saved to
#                            tools\installer\manifests\<version>.txt, to commit
#   3. ISCC.exe compiles tools\installer\pyACAD.iss to
#      dist\pyACAD-<version>-setup.exe   (version comes from pyacadlib\pyacad\__init__.py)
#
# The version is plain MAJOR.MINOR.PATCH and CHANGELOG.md must lead with it (see
# "Releasing" in README.md); the script stops before building when either is off.
#
# The Release build writes to the same bin\<year> folders as a Debug build, so the repo's
# dev output is replaced. Run tools\deploy-dev.ps1 afterwards to get back to a dev deploy.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Versions 2026
# Run with AutoCAD CLOSED.

param(
    [string[]]$Versions = @('2021', '2022', '2023', '2024', '2025', '2026'),
    [string]$Iscc,        # default: the usual Inno Setup 6 locations
    [string]$OutputDir    # default: <repo>\dist
)

$ErrorActionPreference = 'Stop'
# powershell.exe -File hands an array parameter only its first space-separated value and
# passes the rest positionally, so "-Versions 2025,2026" arrives as one string. Split it.
$Versions = $Versions | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'dist' }

# Fixed for the life of the product: Autodesk matches an upgrade by these two codes.
$productCode = '{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}'
$upgradeCode = '{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}'

function Get-PyAcadVersion {
    $initPy = Join-Path $repo 'pyacadlib\pyacad\__init__.py'
    $text = [System.IO.File]::ReadAllText($initPy)
    if ($text -match "__version__\s*=\s*['`"]([^'`"]+)['`"]") { return $Matches[1] }
    throw "Could not read __version__ from $initPy"
}

# AutoCAD's release number per year, which the ApplicationPlugins manifest calls a series.
$seriesByYear = @{ '2021' = 'R24.0'; '2022' = 'R24.1'; '2023' = 'R24.2'; '2024' = 'R24.3'; '2025' = 'R25.0'; '2026' = 'R25.1' }

# One <Components> block per year. SeriesMin = SeriesMax: a loader built against one
# year's API is only ever offered to that year's host.
# Platform AutoCAD* covers AutoCAD and its verticals; LoadOnAutoCADStartup because
# the loader builds the ribbon and cannot wait for a command.
function New-ComponentsBlock([string]$Year, [string]$AppVersion) {
    $series = $seriesByYear[$Year]
    @"
  <Components Description="AutoCAD $Year">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="$series" SeriesMax="$series" />
    <ComponentEntry AppName="pyACAD" AppType=".Net" LoadOnAutoCADStartup="True" Version="$AppVersion" ModuleName="./Contents/$Year/PyAcad.dll" AppDescription="pyACAD loader" />
  </Components>
"@
}

function Find-Iscc {
    if ($Iscc) {
        if (-not (Test-Path $Iscc)) { throw "ISCC.exe not found at $Iscc" }
        return $Iscc
    }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'Inno Setup 6 not found. Install it (winget install --id JRSoftware.InnoSetup -e) or pass -Iscc <path to ISCC.exe>.'
}

# Native tools write progress to stderr, which a Stop-preference session would turn into
# a terminating error before the exit code could be read. Run them at Continue and judge
# them by $LASTEXITCODE instead.
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($lines -join [Environment]::NewLine) }
    }
    finally { $ErrorActionPreference = $previous }
}

# A running host memory-maps the loader DLL, so the build would fail on the copy step.
if (Get-Process -Name 'acad' -ErrorAction SilentlyContinue) {
    Write-Host 'AutoCAD is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

function Assert-ReleaseNotes([string]$Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$Version' is not plain MAJOR.MINOR.PATCH (the Autodesk manifest and the installer's version resource take digits and dots only)."
    }
    $changelog = Join-Path $repo 'CHANGELOG.md'
    if (-not (Test-Path $changelog)) { throw "CHANGELOG.md is missing from $repo." }
    $heading = [System.IO.File]::ReadAllLines($changelog) | Where-Object { $_ -match '^## \[' } | Select-Object -First 1
    if (-not $heading -or $heading -notmatch '^## \[([^\]]+)\] - \d{4}-\d{2}-\d{2}\s*$') {
        throw "CHANGELOG.md has no '## [x.y.z] - yyyy-mm-dd' heading."
    }
    if ($Matches[1] -ne $Version) {
        throw "CHANGELOG.md leads with $($Matches[1]) but __version__ is $Version. Add the $Version entry first."
    }
}

$version = Get-PyAcadVersion
Assert-ReleaseNotes $version
Write-Host "Packaging pyACAD $version" -ForegroundColor Cyan

# --- 1. Release builds, one per year that has an Autodesk reference ---
# Building the loader project pulls in PyAcad.Runtime as a project reference, so both
# bin\<year>\PyAcad.dll and bin\<year>\runtime\ come out of this one build.
$loaderProj = Join-Path $repo 'src\PyAcad\PyAcad.csproj'
$built = @()
$skipped = @()
foreach ($v in $Versions) {
    Write-Host "  Building AutoCAD $v ..." -NoNewline
    # "-p:AcadVersion=$v", not '-p:AcadVersion=' + $v: inside an array literal the
    # comma binds tighter than +, so the year would become its own argument.
    $result = Invoke-Native 'dotnet' @('build', $loaderProj, '-c', 'Release', "-p:AcadVersion=$v", '--nologo', '-v', 'minimal')
    if ($result.ExitCode -eq 0) {
        Write-Host ' ok' -ForegroundColor Green
        $built += "$v"
        continue
    }
    Write-Host ' FAILED' -ForegroundColor Red
    Write-Host $result.Output
    throw "Release build failed for AutoCAD $v."
}
if ($built.Count -eq 0) { throw 'No year could be built, so there is nothing to package.' }

# The command line is version-independent (no AutoCAD reference), so it builds once,
# for both frameworks; the installer ships the net48 one, which every Windows 10 and 11
# machine can run without a separate .NET install.
Write-Host '  Building the command line ...' -NoNewline
$result = Invoke-Native 'dotnet' @('build', (Join-Path $repo 'src\PyAcad.Cli\PyAcad.Cli.csproj'), '-c', 'Release', '--nologo', '-v', 'minimal')
if ($result.ExitCode -ne 0) {
    Write-Host ' FAILED' -ForegroundColor Red
    Write-Host $result.Output
    throw 'Release build failed for the pyACAD command line.'
}
Write-Host ' ok' -ForegroundColor Green

# --- 2. Stage the payload ---
$stage = Join-Path $OutputDir 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$bundleStage = Join-Path $stage 'bundle'
$appDataStage = Join-Path $stage 'appdata'
foreach ($v in $built) {
    $contentsDir = Join-Path $bundleStage "Contents\$v"
    New-Item -ItemType Directory -Force -Path $contentsDir | Out-Null
    Copy-Item (Join-Path $repo "bin\$v\PyAcad.dll") $contentsDir -Force

    $runtimeDst = Join-Path $appDataStage "$v\runtime"
    New-Item -ItemType Directory -Force -Path $runtimeDst | Out-Null
    Copy-Item (Join-Path $repo "bin\$v\runtime\*") $runtimeDst -Recurse -Force
    Copy-Item (Join-Path $repo 'pyacadlib') (Join-Path $runtimeDst 'pyacadlib') -Recurse -Force
    # Debug symbols are dev-time only; they would roughly double the download.
    Get-ChildItem $runtimeDst -Recurse -File -Filter '*.pdb' | Remove-Item -Force
}

# The exe ships as pyacad.exe so "pyacad env" reads naturally at a prompt. The
# assembly is named PyAcad.Cli to avoid a CLR name clash with the loader, so the
# binding config has to be renamed with it or the framework stops finding it.
$cliStage = Join-Path $appDataStage 'cli'
New-Item -ItemType Directory -Force -Path $cliStage | Out-Null
$cliBin = Join-Path $repo 'bin\cli\net48'
Copy-Item (Join-Path $cliBin 'PyAcad.Cli.exe') (Join-Path $cliStage 'pyacad.exe') -Force
Get-ChildItem $cliBin -Filter '*.dll' | Copy-Item -Destination $cliStage -Force
$cliConfig = Join-Path $cliBin 'PyAcad.Cli.exe.config'
if (Test-Path $cliConfig) { Copy-Item $cliConfig (Join-Path $cliStage 'pyacad.exe.config') -Force }

$extStage = Join-Path $appDataStage 'extensions\pyACAD.extension'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $extStage) | Out-Null
Copy-Item (Join-Path $repo 'extensions\pyACAD.extension') $extStage -Recurse -Force
Get-ChildItem $extStage -Recurse -Directory -Filter '__pycache__' | Remove-Item -Recurse -Force

# The release's shipped-files manifest, written by the command line so the hashing is
# the upgrade guard's own (line endings aside, see ShippedFiles.ContentHash). It goes
# inside the extension, where the runtime's startup check compares against it, and into
# tools\installer\manifests, so every later installer's guard knows these files too.
# The installer carries every release's manifest, in one file.
$cliExe = Join-Path $repo 'bin\cli\net48\PyAcad.Cli.exe'
$ownManifest = Join-Path $extStage '.pyacad-manifest'
$result = Invoke-Native $cliExe @('write-manifest', $extStage, $ownManifest, '--version', $version)
if ($result.ExitCode -ne 0) {
    Write-Host $result.Output
    throw 'Could not write the shipped-files manifest.'
}
$history = Join-Path $PSScriptRoot 'installer\manifests'
New-Item -ItemType Directory -Force -Path $history | Out-Null
New-Item -ItemType Directory -Force -Path $history | Out-Null
Copy-Item $ownManifest (Join-Path $history "$version.txt") -Force
$allManifests = Get-ChildItem $history -Filter '*.txt' | Sort-Object Name |
    ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }
[System.IO.File]::WriteAllText((Join-Path $stage 'manifests.txt'), ($allManifests -join "`n"),
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("  Shipped-files manifest: {0} release(s) known to the upgrade guard" -f @($allManifests).Count) -ForegroundColor Green

$blocks = ($built | ForEach-Object { New-ComponentsBlock $_ $version }) -join "`r`n"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" Name="pyACAD" Description="Python scripting and ribbon tools for AutoCAD" AppVersion="$version" FriendlyVersion="$version" ProductCode="$productCode" UpgradeCode="$upgradeCode" Author="pyACAD" SupportedLocales="Enu" OnlineDocumentation="https://wiki.pyacad.com">
  <CompanyDetails Name="pyACAD" Url="https://pyacad.com" />
$blocks
</ApplicationPackage>
"@
[System.IO.File]::WriteAllText((Join-Path $bundleStage 'PackageContents.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

$stageBytes = (Get-ChildItem $stage -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("  Staged {0} for AutoCAD {1} ({2:N1} MB)" -f $stage, ($built -join ', '), ($stageBytes / 1MB)) -ForegroundColor Green

# --- 3. Compile the installer ---
$isccPath = Find-Iscc
$iss = Join-Path $PSScriptRoot 'installer\pyACAD.iss'
$baseName = "pyACAD-$version-setup"
$result = Invoke-Native $isccPath @(
    "/DAppVersion=$version",
    "/DStageDir=$stage",
    "/DOutputDir=$OutputDir",
    "/DOutputBaseFilename=$baseName",
    $iss
)
if ($result.ExitCode -ne 0) {
    Write-Host $result.Output
    throw "Inno Setup compilation failed (exit code $($result.ExitCode))."
}

$setupExe = Join-Path $OutputDir "$baseName.exe"
if (-not (Test-Path $setupExe)) { throw "Inno Setup reported success but $setupExe is missing." }
$setupMB = (Get-Item $setupExe).Length / 1MB

Write-Host ''
Write-Host "pyACAD $version packaged." -ForegroundColor Green
Write-Host ("  Installer : {0} ({1:N1} MB)" -f $setupExe, $setupMB)
Write-Host ("  Years     : {0}" -f ($built -join ', '))
if ($skipped.Count -gt 0) { Write-Host ("  Skipped   : {0}" -f ($skipped -join ', ')) }
