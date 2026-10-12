# pyACAD end-user installer. Per-user: no administrator rights, nothing under Program Files.
#
# For every AutoCAD release that has a pyACAD build in bin\<year>:
#   1. Loader     -> %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\Contents\<year>\PyAcad.dll
#   2. Manifest   -> %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\PackageContents.xml
#   3. Runtime    -> %APPDATA%\pyACAD\<year>\runtime\            (incl. the python stdlib)
#   4. pyacadlib -> %APPDATA%\pyACAD\<year>\runtime\pyacadlib\
#   5. Extensions -> %APPDATA%\pyACAD\extensions\pyACAD.extension\
#
# Autodesk's ApplicationPlugins mechanism loads a *.bundle folder out of the user profile,
# which is why this install needs no elevation. The manifest carries one <Components> block
# per year being installed, keyed by AutoCAD's release number (2026 is R25.1). A reinstall
# rewrites the manifest but never deletes files it did not put there.
#
# config.json and the logs live in %APPDATA%\pyACAD. This script never writes and never
# removes config.json - the loader resolves the runtime as: PYACAD_RUNTIME env var, then
# config.json "runtime<year>", then %APPDATA%\pyACAD\<year>\runtime, then
# %PROGRAMDATA%\pyACAD\<year>\runtime.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1                # every year with a build
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Versions 2026
# Run with AutoCAD CLOSED.

param(
    [string[]]$Versions,          # default: every year 2021-2026 that has a build in bin\<year>
    [string]$BinRoot,             # default: <repo>\bin
    [string]$BundleRoot,          # default: %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle  (override for tests)
    [string]$AppDataRoot          # default: %APPDATA%\pyACAD                                     (override for tests)
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $BinRoot) { $BinRoot = Join-Path $repo 'bin' }
if (-not $BundleRoot) { $BundleRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\pyACAD.bundle' }
if (-not $AppDataRoot) { $AppDataRoot = Join-Path $env:APPDATA 'pyACAD' }

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

# The DLL in the bundle is memory-mapped by a running host, so a copy would fail
# halfway and leave a torn install.
if (Get-Process -Name 'acad' -ErrorAction SilentlyContinue) {
    Write-Host 'AutoCAD is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

$version = Get-PyAcadVersion
if (-not $Versions) { $Versions = 2021..2026 | ForEach-Object { "$_" } }

$installed = @()
foreach ($v in $Versions) {
    $loaderDll = Join-Path $BinRoot "$v\PyAcad.dll"
    $runtimeSrc = Join-Path $BinRoot "$v\runtime"
    if (-not (Test-Path $loaderDll) -or -not (Test-Path (Join-Path $runtimeSrc 'PyAcad.Runtime.dll'))) {
        Write-Host "AutoCAD $v : no build in $BinRoot\$v (run: dotnet build -p:AcadVersion=$v) - skipped."
        continue
    }

    # --- 1. Loader -> the bundle's Contents\<year>\ ---
    # Copied in place, not into a wiped folder: files this script did not put there stay.
    $contentsDir = Join-Path $BundleRoot "Contents\$v"
    New-Item -ItemType Directory -Force -Path $contentsDir | Out-Null
    Copy-Item $loaderDll $contentsDir -Force
    Get-ChildItem $contentsDir -File | Unblock-File

    # --- 2+3. Runtime (+ pyacadlib inside it) -> per-user AppData, clean update ---
    $runtimeDst = Join-Path $AppDataRoot "$v\runtime"
    if (Test-Path $runtimeDst) { Remove-Item $runtimeDst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $runtimeDst | Out-Null
    Copy-Item (Join-Path $runtimeSrc '*') $runtimeDst -Recurse -Force
    Copy-Item (Join-Path $repo 'pyacadlib') (Join-Path $runtimeDst 'pyacadlib') -Recurse -Force
    Get-ChildItem $runtimeDst -Recurse -File | Unblock-File

    Write-Host "AutoCAD $v : loader -> $contentsDir ; runtime -> $runtimeDst" -ForegroundColor Green
    $installed += $v
}

if ($installed.Count -eq 0) {
    Write-Host 'Nothing installed.' -ForegroundColor Yellow
    exit 0
}

# --- 4. The bundle manifest, covering exactly the years just installed ---
$blocks = ($installed | ForEach-Object { New-ComponentsBlock $_ $version }) -join "`r`n"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" Name="pyACAD" Description="Python scripting and ribbon tools for AutoCAD" AppVersion="$version" FriendlyVersion="$version" ProductCode="$productCode" UpgradeCode="$upgradeCode" Author="pyACAD" SupportedLocales="Enu" OnlineDocumentation="https://wiki.pyacad.com">
  <CompanyDetails Name="pyACAD" Url="https://pyacad.com" />
$blocks
</ApplicationPackage>
"@
$manifestPath = Join-Path $BundleRoot 'PackageContents.xml'
# Temp file then swap: AutoCAD reads this at every start, so an interrupted
# install must never leave it truncated.
$tempPath = "$manifestPath.tmp"
[System.IO.File]::WriteAllText($tempPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))
# [NullString]::Value, not $null: PowerShell hands a string parameter "" for $null, and
# File.Replace rejects an empty backup path ("The path is not of a legal form").
if (Test-Path $manifestPath) { [System.IO.File]::Replace($tempPath, $manifestPath, [NullString]::Value) }
else { [System.IO.File]::Move($tempPath, $manifestPath) }
Unblock-File $manifestPath
Write-Host "Bundle manifest -> $manifestPath" -ForegroundColor Green

# --- 5. Extensions (shared across versions), clean update of the shipped one only ---
$extDst = Join-Path $AppDataRoot 'extensions'
New-Item -ItemType Directory -Force -Path $extDst | Out-Null
$shipped = Join-Path $extDst 'pyACAD.extension'
if (Test-Path $shipped) { Remove-Item $shipped -Recurse -Force }
Copy-Item (Join-Path $repo 'extensions\pyACAD.extension') $shipped -Recurse -Force
Get-ChildItem $shipped -Recurse -File | Unblock-File
Write-Host "Extensions -> $shipped" -ForegroundColor Green

$userConfig = Join-Path $AppDataRoot 'config.json'
if (Test-Path $userConfig) {
    $configText = [System.IO.File]::ReadAllText($userConfig)
    $overrides = $installed | Where-Object { $configText -match "runtime$_" }
    if ($overrides) {
        Write-Host ''
        Write-Host "NOTE: $userConfig overrides runtime$($overrides -join ', runtime') for THIS user (dev setup). Delete those keys to use the installed runtime." -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host "Installed pyACAD $version for AutoCAD $($installed -join ', '). Start AutoCAD and look for the 'pyACAD' ribbon tab."
