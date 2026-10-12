# Deploys the pyACAD DEV build:
#   1. Checks the build exists and AutoCAD is closed
#   2. Copies the loader (PyAcad.dll) into the per-user Autodesk bundle:
#      %APPDATA%\Autodesk\ApplicationPlugins\pyACAD.bundle\Contents\<year>\PyAcad.dll
#      and merges a <Components> block for this year into the bundle's PackageContents.xml,
#      leaving any other year's block alone.
#   3. Writes %APPDATA%\pyACAD\config.json pointing the runtime at this repo's bin folder,
#      so C#/python changes only need a rebuild (+ AutoCAD restart for C#), never a redeploy.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\deploy-dev.ps1 [-Version 2026]
# Run with AutoCAD CLOSED (the loader DLL is locked while it runs).

param(
    [string]$Version = '2026'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# AutoCAD's release number per year, which the ApplicationPlugins manifest calls a series.
$seriesByYear = @{ '2021' = 'R24.0'; '2022' = 'R24.1'; '2023' = 'R24.2'; '2024' = 'R24.3'; '2025' = 'R25.0'; '2026' = 'R25.1' }
if (-not $seriesByYear.ContainsKey($Version)) { throw "Unsupported AutoCAD version '$Version' (expected 2021-2026)." }
$series = $seriesByYear[$Version]

# Fixed for the life of the product: Autodesk matches an upgrade by these two codes.
$productCode = '{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}'
$upgradeCode = '{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}'

$initPy = Join-Path $repo 'pyacadlib\pyacad\__init__.py'
$initText = [System.IO.File]::ReadAllText($initPy)
if ($initText -match "__version__\s*=\s*['`"]([^'`"]+)['`"]") { $pyACADVersion = $Matches[1] }
else { throw "Could not read __version__ from $initPy" }

# The bundle DLL is memory-mapped by a running host, so a copy would fail halfway.
if (Get-Process -Name 'acad' -ErrorAction SilentlyContinue) {
    Write-Host 'AutoCAD is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

$loaderDll = Join-Path $repo "bin\$Version\PyAcad.dll"
if (-not (Test-Path $loaderDll)) { throw "Loader not built: $loaderDll  (run: dotnet build -p:AcadVersion=$Version)" }

$runtimeDir = Join-Path $repo "bin\$Version\runtime"
if (-not (Test-Path (Join-Path $runtimeDir 'PyAcad.Runtime.dll'))) { throw "Runtime not built: $runtimeDir" }

# --- 2. Loader -> the per-user Autodesk bundle ---
$bundleRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\pyACAD.bundle'
$contentsDir = Join-Path $bundleRoot "Contents\$Version"
New-Item -ItemType Directory -Force -Path $contentsDir | Out-Null
Copy-Item $loaderDll $contentsDir -Force
$pdb = [System.IO.Path]::ChangeExtension($loaderDll, '.pdb')
if (Test-Path $pdb) { Copy-Item $pdb $contentsDir -Force }
Get-ChildItem $contentsDir -File | Unblock-File
Write-Host "Loader deployed -> $contentsDir" -ForegroundColor Green

# --- 2a. Trust the bundle folder: SECURELOAD=1 (the default) makes AutoCAD stop at a
#         "Security - Unsigned Executable File" prompt for every new build of the unsigned
#         dev loader. A folder listed in TRUSTEDPATHS loads silently. AutoCAD rewrites its
#         profile on exit, so this only sticks because the script runs with it closed. ---
$releaseKey = "HKCU:\SOFTWARE\Autodesk\AutoCAD\$series"
if (Test-Path $releaseKey) {
    foreach ($product in Get-ChildItem $releaseKey | Where-Object { $_.PSChildName -like 'ACAD-*' }) {
        $profiles = Join-Path $product.PSPath 'Profiles'
        if (-not (Test-Path $profiles)) { continue }
        foreach ($profile in Get-ChildItem $profiles) {
            $vars = Join-Path $profile.PSPath 'Variables'
            if (-not (Test-Path $vars)) { continue }
            $current = (Get-ItemProperty $vars -ErrorAction SilentlyContinue).TRUSTEDPATHS
            $paths = @()
            if ($current) { $paths = @($current -split ';' | Where-Object { $_ }) }
            if ($paths -notcontains $contentsDir) {
                $paths += $contentsDir
                Set-ItemProperty $vars -Name 'TRUSTEDPATHS' -Value ($paths -join ';')
                Write-Host "TRUSTEDPATHS += $contentsDir  (profile '$($profile.PSChildName)')" -ForegroundColor Green
            }
        }
    }
}

# --- 2b. Merge this year's <Components> block into the bundle manifest ---
#         Other years' blocks survive, and so does any extra ComponentEntry: only this
#         year's PyAcad.dll entry is rewritten, never a whole block that already exists.
$manifestPath = Join-Path $bundleRoot 'PackageContents.xml'
$xml = New-Object System.Xml.XmlDocument
$loaded = $false
if (Test-Path $manifestPath) {
    # SelectSingleNode, not $xml.DocumentElement.Name: PowerShell's XML adapter exposes
    # attributes as properties and shadows the real ones, and ApplicationPackage has a
    # Name attribute ("pyACAD"), so the element name check would never match and every
    # deploy would silently rebuild the manifest and drop the other years.
    try { $xml.Load($manifestPath); $loaded = ($null -ne $xml.SelectSingleNode('/ApplicationPackage')) }
    catch {
        Write-Host "PackageContents.xml would not parse, so it is being rebuilt: $manifestPath" -ForegroundColor Yellow
        Write-Host "  $($_.Exception.Message)"
    }
}
if (-not $loaded) {
    $xml.LoadXml(@"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" Name="pyACAD" Description="Python scripting and ribbon tools for AutoCAD" AppVersion="$pyACADVersion" FriendlyVersion="$pyACADVersion" ProductCode="$productCode" UpgradeCode="$upgradeCode" Author="pyACAD" SupportedLocales="Enu" OnlineDocumentation="https://wiki.pyacad.com">
  <CompanyDetails Name="pyACAD" Url="https://pyacad.com" />
</ApplicationPackage>
"@)
}
$root = $xml.DocumentElement
$root.SetAttribute('AppVersion', $pyACADVersion)
$root.SetAttribute('FriendlyVersion', $pyACADVersion)

$block = $null
foreach ($c in @($root.SelectNodes('Components'))) {
    $req = $c.SelectSingleNode('RuntimeRequirements')
    if ($req -and $req.GetAttribute('SeriesMin') -eq $series) { $block = $c; break }
}
if (-not $block) {
    $block = $xml.CreateElement('Components')
    $block.SetAttribute('Description', "AutoCAD $Version")
    $req = $xml.CreateElement('RuntimeRequirements')
    $req.SetAttribute('OS', 'Win64')
    # AutoCAD* is the autoloader wildcard for AutoCAD and every vertical on it.
    $req.SetAttribute('Platform', 'AutoCAD*')
    $req.SetAttribute('SeriesMin', $series)
    $req.SetAttribute('SeriesMax', $series)
    $block.AppendChild($req) | Out-Null
    $root.AppendChild($block) | Out-Null
}
$moduleName = "./Contents/$Version/PyAcad.dll"
$entry = $null
foreach ($e in @($block.SelectNodes('ComponentEntry'))) {
    if ($e.GetAttribute('ModuleName') -eq $moduleName) { $entry = $e; break }
}
if (-not $entry) {
    $entry = $xml.CreateElement('ComponentEntry')
    $block.AppendChild($entry) | Out-Null
}
$entry.SetAttribute('AppName', 'pyACAD')
# Load at startup: the loader builds the ribbon, so it cannot wait for a command.
$entry.SetAttribute('AppType', '.Net')
$entry.SetAttribute('LoadOnAutoCADStartup', 'True')
$entry.SetAttribute('Version', $pyACADVersion)
$entry.SetAttribute('ModuleName', $moduleName)
$entry.SetAttribute('AppDescription', 'pyACAD loader')

# Temp file then swap: AutoCAD reads this at every start, so an interrupted deploy
# must never leave it truncated.
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Indent = $true
$writerSettings.IndentChars = '  '
$writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
$tempPath = "$manifestPath.tmp"
$writer = [System.Xml.XmlWriter]::Create($tempPath, $writerSettings)
try { $xml.Save($writer) } finally { $writer.Close() }
# [NullString]::Value, not $null: PowerShell hands a string parameter "" for $null, and
# File.Replace rejects an empty backup path ("The path is not of a legal form").
if (Test-Path $manifestPath) { [System.IO.File]::Replace($tempPath, $manifestPath, [NullString]::Value) }
else { [System.IO.File]::Move($tempPath, $manifestPath) }
Write-Host "Bundle manifest updated -> $manifestPath  ($series)" -ForegroundColor Green

# --- 3. Dev config: runtime resolves straight from the repo build output.
#        Keys are per-version ("runtime2026") so multiple AutoCAD releases can
#        coexist; merge into any existing config instead of overwriting it. ---
$configDir = Join-Path $env:APPDATA 'pyACAD'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$configPath = Join-Path $configDir 'config.json'
$config = [ordered]@{}
if (Test-Path $configPath) {
    # [IO.File], not Get-Content -Raw: PS 5.1 reads BOM-less UTF-8 as ANSI, which
    # mojibakes every non-ASCII character on the round trip.
    $existing = [System.IO.File]::ReadAllText($configPath)
    if ($existing.Trim()) {
        # A config that will not parse STOPS the deploy. "Recreate it" would drop the
        # shortcut bindings and the other releases' runtime keys over one stray comma;
        # same rule as PyAcadConfig in the runtime.
        try { $parsed = $existing | ConvertFrom-Json }
        catch {
            Write-Host "config.json is not valid JSON, so it was left untouched: $configPath" -ForegroundColor Red
            Write-Host "Fix the file (or delete it to start fresh), then run this script again." -ForegroundColor Yellow
            Write-Host "  $($_.Exception.Message)"
            exit 1
        }
        $parsed.PSObject.Properties | ForEach-Object { $config[$_.Name] = $_.Value }
    }
}
$config["runtime$Version"] = $runtimeDir
$config["extensions"] = @(Join-Path $repo 'extensions')
$config["pyacadlib"] = Join-Path $repo 'pyacadlib'

# -Depth: the default of 2 flattens anything nested deeper into a type-name string.
# Temp file then swap: the loader regex-reads this file at every AutoCAD start, so
# an interrupted deploy must never leave it truncated.
$json = $config | ConvertTo-Json -Depth 32
$tempPath = "$configPath.tmp"
[System.IO.File]::WriteAllText($tempPath, $json, (New-Object System.Text.UTF8Encoding($false)))
# [NullString]::Value, not $null: PowerShell hands a string parameter "" for $null, and
# File.Replace rejects an empty backup path ("The path is not of a legal form").
if (Test-Path $configPath) { [System.IO.File]::Replace($tempPath, $configPath, [NullString]::Value) }
else { [System.IO.File]::Move($tempPath, $configPath) }
Write-Host "Dev config updated -> $configPath  (runtime$Version = $runtimeDir)" -ForegroundColor Green

Write-Host "`nDone. Start AutoCAD $Version and look for the 'pyACAD' ribbon tab."
Write-Host "Logs: %APPDATA%\pyACAD\logs\{loader.log, pyACAD.log}"
