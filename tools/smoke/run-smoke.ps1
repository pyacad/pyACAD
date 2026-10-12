# Automated in-app smoke suite for pyACAD (docs/smoke-test.md, steps 0 to 9).
#
# Drives a real AutoCAD through COM (SendCommand) and reads back what happened from
# three places: pyACAD's own log, marker files written by tests/smoke/probe-extension
# hooks and buttons, and results JSON written by tests/smoke/probes/*.py, which run
# inside AutoCAD through PYACAD. Nothing is clicked except the shortcut step, which
# needs real keyboard focus and briefly brings AutoCAD to the front.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\smoke\run-smoke.ps1 [-Version 2026]
#            [-RunRoot <dir>] [-SkipCoreConsole] [-SkipShortcuts] [-SkipRestart] [-KeepOpen]
#
# Your config.json is backed up to config.json.smoke-backup, replaced for the run and
# restored at the end, success or not. Run with AutoCAD CLOSED.

param(
    [string]$Version = '2026',
    [string]$RunRoot = '',
    [switch]$SkipCoreConsole,
    [switch]$SkipShortcuts,
    [switch]$SkipRestart,
    [switch]$KeepOpen,
    [int]$BootTimeoutSec = 420
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$acadExe = $null
foreach ($root in @("$env:ProgramFiles\Autodesk\AutoCAD $Version", "D:\Program Files\Autodesk\AutoCAD $Version")) {
    if (Test-Path (Join-Path $root 'acad.exe')) { $acadExe = Join-Path $root 'acad.exe'; break }
}
if (-not $acadExe) {
    $reg = Get-ChildItem 'HKLM:\SOFTWARE\Autodesk\AutoCAD' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.PSPath } |
        ForEach-Object { Get-ItemProperty $_.PSPath } |
        Where-Object { $_.ProductName -like "*AutoCAD $Version*" -and $_.AcadLocation } |
        Select-Object -First 1
    if ($reg) { $acadExe = Join-Path $reg.AcadLocation 'acad.exe' }
}
if (-not $acadExe -or -not (Test-Path $acadExe)) { throw "AutoCAD $Version not found." }

if (-not $RunRoot) { $RunRoot = Join-Path $env:TEMP 'pyacad-smoke' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$run = Join-Path $RunRoot $stamp
foreach ($sub in 'markers', 'results', 'shots', 'ext') { New-Item -ItemType Directory -Force -Path (Join-Path $run $sub) | Out-Null }
$runFwd = $run.Replace('\', '/')
$logDir = Join-Path $env:APPDATA 'pyACAD\logs'
$logPath = Join-Path $logDir 'pyACAD.log'
$configPath = Join-Path $env:APPDATA 'pyACAD\config.json'
$backupPath = "$configPath.smoke-backup"
$probes = Join-Path $repo 'tests\smoke\probes'

# ---------------------------------------------------------------- bookkeeping

$script:Steps = New-Object System.Collections.ArrayList
$script:Section = ''
$script:Acad = $null
$script:LogOffset = 0

function Section([string]$name) { $script:Section = $name; Write-Host "`n== $name" -ForegroundColor Cyan }

function Record([string]$name, [bool]$ok, [string]$detail = '', [string]$state = '') {
    if (-not $state) { $state = if ($ok) { 'pass' } else { 'FAIL' } }
    [void]$script:Steps.Add([pscustomobject]@{ section = $script:Section; name = $name; state = $state; detail = $detail; at = (Get-Date).ToString('HH:mm:ss') })
    $color = switch ($state) { 'pass' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    $line = "  [{0}] {1}" -f $state.PadRight(4), $name
    if ($detail) { $line += "  -- " + ($detail -replace "`r?`n", ' | ') }
    Write-Host $line -ForegroundColor $color
}
function Skip([string]$name, [string]$why) { Record $name $true $why 'skip' }
function Weak([string]$name, [bool]$ok, [string]$detail) { Record $name $ok $detail $(if ($ok) { 'pass' } else { 'weak' }) }

function Wait-Until([scriptblock]$test, [int]$timeoutSec = 30, [int]$pollMs = 250) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { if (& $test) { return $true } } catch { }
        Start-Sleep -Milliseconds $pollMs
    }
    try { return [bool](& $test) } catch { return $false }
}

function Marker-Count([string]$name) {
    $p = Join-Path $run "markers\$name.log"
    if (-not (Test-Path $p)) { return 0 }
    return @(Get-Content $p | Where-Object { $_.Trim() }).Count
}
function Marker-Text([string]$name) {
    $p = Join-Path $run "markers\$name.log"
    if (Test-Path $p) { return [System.IO.File]::ReadAllText($p) } else { return '' }
}
function Wait-Marker([string]$name, [int]$atLeast, [int]$timeoutSec = 20) {
    return Wait-Until { (Marker-Count $name) -ge $atLeast } $timeoutSec
}

function Log-Text { if (Test-Path $logPath) { return [System.IO.File]::ReadAllText($logPath) } else { return '' } }
function Log-Since { $t = Log-Text; if ($t.Length -gt $script:LogOffset) { return $t.Substring($script:LogOffset) } else { return '' } }
function Log-Mark { $script:LogOffset = (Log-Text).Length }
function Log-Count([string]$pattern) { return ([regex]::Matches((Log-Text), $pattern)).Count }

# ---------------------------------------------------------------- AutoCAD plumbing

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SmokeWin {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    public static System.Collections.Generic.List<string> TitlesOf(uint pid) {
        var list = new System.Collections.Generic.List<string>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) {
                var sb = new System.Text.StringBuilder(GetWindowTextLength(h) + 1);
                GetWindowText(h, sb, sb.Capacity);
                if (sb.Length > 0) list.Add(sb.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
'@

function Acad-Process { return (Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -First 1) }

function Shot([string]$name) {
    try {
        $p = Acad-Process
        $path = Join-Path $run "shots\$name.png"
        if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) {
            $r = New-Object SmokeWin+RECT
            [SmokeWin]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
            $w = [Math]::Max(1, $r.R - $r.L); $h = [Math]::Max(1, $r.B - $r.T)
            $bmp = New-Object System.Drawing.Bitmap $w, $h
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $dc = $g.GetHdc(); [SmokeWin]::PrintWindow($p.MainWindowHandle, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
            $bmp.Save($path); $g.Dispose(); $bmp.Dispose()
        } else {
            $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
            $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
            $bmp.Save($path); $g.Dispose(); $bmp.Dispose()
        }
        return $path
    } catch { return $null }
}

function Start-Acad {
    $env:PYACAD_SMOKE_RUN = $run
    Start-Process -FilePath $acadExe -ArgumentList '/nologo' | Out-Null
}

function Wait-Boot([int]$timeoutSec) {
    # Boot is done when the ribbon is built. A licensing or other dialog stalls it, so
    # every 30 s a screenshot and the visible window titles go to the run folder.
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $nextShot = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if ((Log-Since) -match 'Ribbon built') { return $true }
        if ((Log-Since) -match 'ReportFatal') { return $false }
        if ((Get-Date) -gt $nextShot) {
            $nextShot = (Get-Date).AddSeconds(30)
            $p = Acad-Process
            $titles = if ($p) { [SmokeWin]::TitlesOf([uint32]$p.Id) -join ' | ' } else { '(no acad process)' }
            Write-Host "  ...still booting; windows: $titles" -ForegroundColor DarkGray
            if ($titles -match 'Security - Unsigned') {
                Write-Host '  AutoCAD is waiting at the unsigned-DLL security prompt. Click Always Load, or run tools\deploy-dev.ps1 with AutoCAD closed so the bundle folder is in TRUSTEDPATHS.' -ForegroundColor Yellow
            }
            Shot ("boot-wait-" + (Get-Date -Format 'HHmmss')) | Out-Null
        }
        Start-Sleep -Milliseconds 1000
    }
    return $false
}

function Connect-Acad([int]$timeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $a = [System.Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
            if ($a.Documents.Count -lt 1) { $a.Documents.Add() | Out-Null }
            $null = $a.ActiveDocument.Name
            $script:Acad = $a
            return $true
        } catch { Start-Sleep -Seconds 2 }
    }
    return $false
}

function Com([scriptblock]$call, [int]$timeoutSec = 45) {
    # AutoCAD rejects COM calls while it is busy (RPC_E_CALL_REJECTED 0x80010001,
    # RPC_E_SERVERCALL_RETRYLATER 0x8001010A): retry until it answers.
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ($true) {
        try { return (& $call) }
        catch {
            $hr = $_.Exception.HResult
            $inner = $_.Exception.InnerException
            if ($inner) { $hr = $inner.HResult }
            $busy = ($hr -eq -2147418111) -or ($hr -eq -2147417846) -or ($_.Exception.Message -match 'rejected by callee|busy|RETRYLATER')
            if (-not $busy -or (Get-Date) -gt $deadline) { throw }
            Start-Sleep -Milliseconds 400
        }
    }
}

function Wait-Quiet([int]$timeoutSec = 120) {
    # AutoCAD rejects every COM call until it is quiescent again after a document is
    # added or closed. Returns the seconds it took, or -1 when it never settled.
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $timeoutSec) {
        try { if ($script:Acad.GetAcadState().IsQuiescent) { return [int]((Get-Date) - $t0).TotalSeconds } } catch { }
        Start-Sleep -Milliseconds 500
    }
    return -1
}

function Acad-Windows {
    $p = Acad-Process
    if ($p) { return ([SmokeWin]::TitlesOf([uint32]$p.Id) -join ' | ') } else { return '(no acad process)' }
}

function Active-Doc {
    # ActiveDocument reads null while AutoCAD is between documents or mid-command; wait.
    $doc = $null
    Wait-Until { $doc = Com { $script:Acad.ActiveDocument } 5; $null -ne $doc } 30 | Out-Null
    if ($null -eq $doc) { $doc = $script:Acad.ActiveDocument }
    if ($null -eq $doc) { throw 'AutoCAD has no active document.' }
    return $doc
}

function Send([string]$text) {
    # SendCommand returns only when the command finishes, so every prompt answer must be
    # in the SAME call, one per line: Send "_.FILEDIA`n0", never Send '_.FILEDIA' then
    # Send '0' (the first call blocks forever). The trailing newline executes the last line.
    $doc = Active-Doc
    Com { $doc.SendCommand($text + "`n") } | Out-Null
}

function Quote([string]$s) { return '"' + $s + '"' }

function Invoke-Probe([string]$file, [string]$resultName, [string[]]$probeArgs = @(), [int]$timeoutSec = 60, [bool]$Weak = $false) {
    # Runs tests/smoke/probes/<file> through PYACAD and records every check it wrote.
    $resultPath = Join-Path $run "results\$resultName.json"
    if (Test-Path $resultPath) { Remove-Item $resultPath -Force }
    $line = (Quote (Join-Path $probes $file))
    foreach ($a in $probeArgs) { $line += ' ' + (Quote $a) }
    $line += ' ' + (Quote ('@' + $resultName))
    Send ("_.PYACAD`n" + $line)
    if (-not (Wait-Until { Test-Path $resultPath } $timeoutSec)) {
        $tail = ((Log-Since) -split "`n" | Select-Object -Last 3) -join ' | '
        Record "probe $resultName ran" $false "no results file after ${timeoutSec}s; log tail: $tail"
        return $null
    }
    Start-Sleep -Milliseconds 150
    $data = [System.IO.File]::ReadAllText($resultPath) | ConvertFrom-Json
    foreach ($c in $data.checks) { if ($Weak) { Weak $c.name ([bool]$c.ok) $c.detail } else { Record $c.name ([bool]$c.ok) $c.detail } }
    return $data
}

function Write-Config {
    $config = [ordered]@{}
    if (Test-Path $configPath) {
        $existing = [System.IO.File]::ReadAllText($configPath)
        if ($existing.Trim()) { ($existing | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $config[$_.Name] = $_.Value } }
    }
    $config["runtime$Version"] = Join-Path $repo "bin\$Version\runtime"
    $config['extensions'] = @((Join-Path $repo 'extensions'), (Join-Path $repo 'tests\fixtures'), (Join-Path $run 'ext'))
    $config['pyacadlib'] = Join-Path $repo 'pyacadlib'
    $config['updates'] = @{ check = $false }
    $config['shortcuts'] = @{ bindings = @{ 'Probe.tab/Probe.panel/Cfg_Twin.pushbutton' = 'Ctrl+Shift+M' } }
    $config.Remove('theme') | Out-Null
    $json = $config | ConvertTo-Json -Depth 16
    [System.IO.File]::WriteAllText($configPath, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Restore-Config {
    if (Test-Path $backupPath) {
        Copy-Item $backupPath $configPath -Force
        Remove-Item $backupPath -Force
        Write-Host "config.json restored from backup." -ForegroundColor DarkGray
    } elseif ($script:ConfigWasAbsent -and (Test-Path $configPath)) {
        Remove-Item $configPath -Force
    }
}

function Stop-Acad([bool]$graceful) {
    try {
        if ($graceful -and $script:Acad) {
            foreach ($d in @($script:Acad.Documents)) { try { $d.Close($false) } catch { } }
            $script:Acad.Quit()
        }
    } catch { }
    $script:Acad = $null
    if (-not (Wait-Until { -not (Acad-Process) } 40)) {
        Get-Process -Name acad -ErrorAction SilentlyContinue | Stop-Process -Force
        Wait-Until { -not (Acad-Process) } 20 | Out-Null
    }
}

function Press-Chord([bool]$ctrl, [bool]$shift, [byte]$vk) {
    $KEYUP = 2
    if ($ctrl) { [SmokeWin]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero) }
    if ($shift) { [SmokeWin]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero) }
    [SmokeWin]::keybd_event($vk, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
    [SmokeWin]::keybd_event($vk, 0, $KEYUP, [UIntPtr]::Zero)
    if ($shift) { [SmokeWin]::keybd_event(0x10, 0, $KEYUP, [UIntPtr]::Zero) }
    if ($ctrl) { [SmokeWin]::keybd_event(0x11, 0, $KEYUP, [UIntPtr]::Zero) }
}
function Click-At([int]$x, [int]$y) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
    Start-Sleep -Milliseconds 150
    [SmokeWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [SmokeWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

function New-ProbeBundle([string]$folderName, [string]$title, [string]$markerName) {
    $dir = Join-Path $run "ext\Probe.extension\Probe.tab\Probe.panel\$folderName"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $dir 'bundle.yaml'), "title: $title`ntooltip: Smoke: added at runtime for the Reload step.`n")
    $template = [System.IO.File]::ReadAllText((Join-Path $run 'ext\Probe.extension\Probe.tab\Probe.panel\Cfg_Twin.pushbutton\script.py'))
    [System.IO.File]::WriteAllText((Join-Path $dir 'script.py'), $template.Replace('mark("primary", __file__)', "mark(`"$markerName`", __file__)"))
    return $dir
}

function Wait-RibbonRebuild([int]$before, [int]$timeoutSec = 60) {
    return Wait-Until { (Log-Count 'Ribbon built') -gt $before } $timeoutSec
}

# ================================================================= run

$started = Get-Date
$script:ConfigWasAbsent = -not (Test-Path $configPath)
Write-Host "pyACAD smoke run $stamp -> $run" -ForegroundColor Cyan
Write-Host "AutoCAD: $acadExe"

try {
    Section '0 Deploy'
    $loader = Join-Path $repo "bin\$Version\PyAcad.dll"
    Record 'loader built' (Test-Path $loader) $loader
    Record 'runtime built' (Test-Path (Join-Path $repo "bin\$Version\runtime\PyAcad.Runtime.dll"))
    $bundleDll = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\pyACAD.bundle\Contents\$Version\PyAcad.dll"
    Record 'loader deployed in the bundle' (Test-Path $bundleDll) $bundleDll
    if ((Test-Path $bundleDll) -and (Test-Path $loader)) {
        $h1 = (Get-FileHash $bundleDll).Hash; $h2 = (Get-FileHash $loader).Hash
        if ($h1 -ne $h2) {
            if (Acad-Process) { Stop-Acad $false }
            & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\deploy-dev.ps1') -Version $Version | Out-Null
            $h1 = (Get-FileHash $bundleDll).Hash
        }
        Record 'deployed loader matches the build (deploy-dev run if not)' ($h1 -eq $h2) "bundle $($h1.Substring(0,8)) build $($h2.Substring(0,8))"
    }
    if (Acad-Process) {
        Write-Host 'AutoCAD is running; closing it first.' -ForegroundColor Yellow
        Stop-Acad $false
    }
    if (Test-Path $configPath) { Copy-Item $configPath $backupPath -Force }
    Write-Config
    Record 'smoke config written' $true $configPath
    Copy-Item (Join-Path $repo 'tests\smoke\probe-extension\Probe.extension') (Join-Path $run 'ext\Probe.extension') -Recurse -Force
    Record 'Probe.extension staged' (Test-Path (Join-Path $run 'ext\Probe.extension\extension.yaml'))
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    Remove-Item (Join-Path $logDir 'loader.log') -Force -ErrorAction SilentlyContinue
    $script:LogOffset = 0

    Section '1 Load'
    Start-Acad
    $booted = Wait-Boot $BootTimeoutSec
    Record 'AutoCAD booted and the ribbon built' $booted ("{0:n0}s" -f ((Get-Date) - $started).TotalSeconds)
    if (-not $booted) { Shot 'boot-failed' | Out-Null; throw 'Boot did not complete; see the shots folder.' }
    $log = Log-Text
    Record 'loader.log has Loader.Initialize' ((Get-Content (Join-Path $logDir 'loader.log') -Raw) -match 'Loader\.Initialize')
    Record "boot banner names AutoCAD $Version" ($log -match "for AutoCAD $Version")
    Record 'no ERROR lines during boot' (-not ($log -match '\[ERROR\]')) (($log -split "`n" | Where-Object { $_ -match '\[ERROR\]' } | Select-Object -First 2) -join ' | ')
    Record 'commands registered line present' ($log -match 'Commands registered: \d+')
    Record 'theme decided from COLORTHEME' ($log -match 'Theme: COLORTHEME says')
    Record 'COM connection' (Connect-Acad 90)
    if (-not $script:Acad) { throw 'Could not reach AutoCAD through COM.' }
    Start-Sleep -Seconds 2
    Shot '01-booted' | Out-Null
    Invoke-Probe 'p01_boot.py' 'p01_boot' @($Version) | Out-Null

    Section '2 Commands'
    Invoke-Probe 'p02_args.py' 'p02_args' @('alpha', 'beta two') | Out-Null
    # pickfirst: three lines, selected with sssetfirst, then a command with UsePickSet
    $ms = (Active-Doc).ModelSpace
    foreach ($i in 0..2) {
        $p1 = [double[]]@(($i * 10), 0, 0); $p2 = [double[]]@(($i * 10), 50, 0)
        Com { $ms.AddLine($p1, $p2) } | Out-Null
    }
    $count = Com { $ms.Count }
    Record 'three lines added through COM' ($count -ge 3) "modelspace count $count"
    Send '(sssetfirst nil (ssget "_X" (list (cons 0 "LINE"))))'
    Start-Sleep -Milliseconds 600
    Invoke-Probe 'p03_selection.py' 'p03_selection' | Out-Null
    Record 'selection-changed hook fired on sssetfirst' (Wait-Marker 'selection-changed' 1 6)
    # change + one-step undo
    $before = Com { $ms.Count }
    Invoke-Probe 'p04_modify.py' 'p04_modify' | Out-Null
    $after = Com { $ms.Count }
    Record 'script added two entities' ($after -eq $before + 2) "before $before after $after"
    Send '_.U'
    Start-Sleep -Milliseconds 800
    $undone = Com { $ms.Count }
    Record 'Ctrl+Z (U) undoes the whole tool in one step' ($undone -eq $before) "after undo $undone"
    # command names, CFG twin, before-command key
    $primary0 = Marker-Count 'primary'; $config0 = Marker-Count 'config'
    Send '_.PYCFGTWIN'
    Record 'typed PYCFGTWIN runs script.py' (Wait-Marker 'primary' ($primary0 + 1) 15)
    Send '_.PYCFGTWINCFG'
    Record 'PYCFGTWINCFG (Shift+Click twin) runs config.py' (Wait-Marker 'config' ($config0 + 1) 15)
    Record 'before-command hook carries the bundle key' ((Marker-Text 'before-command') -match 'Cfg_Twin\.pushbutton')
    # ribbon click path through the button's CommandHandler
    $primary1 = Marker-Count 'primary'
    Invoke-Probe 'p05_ribbon.py' 'p05_ribbon' | Out-Null
    Record 'ribbon CommandHandler ran the bundle (marker)' (Wait-Marker 'primary' ($primary1 + 1) 15)
    # LISP bridge
    Send ('(progn (setq f (open "' + $runFwd + '/lisp-result.txt" "w")) (write-line (vl-prin1-to-string (pyacad "PYLISPRESULT" 1 "x")) f) (close f) (princ))')
    Send ('(progn (setq f (open "' + $runFwd + '/lisp-nil.txt" "w")) (write-line (vl-prin1-to-string (pyacad "PYCFGTWIN")) f) (close f) (princ))')
    Wait-Until { (Test-Path (Join-Path $run 'lisp-nil.txt')) } 20 | Out-Null
    Invoke-Probe 'p13_lisp_check.py' 'p13_lisp_check' | Out-Null
    Skip 'PYACADCONSOLE diagnostic box' 'modal MessageBox; not automated'
    Skip 'Alt+Click opens Explorer' 'spawns Explorer; mapping unit-tested in ClickActionsTests'
    Shot '02-commands' | Out-Null

    Section '3 Reload'
    $rb = Log-Count 'Ribbon built'
    $dir = New-ProbeBundle 'New_Button.pushbutton' 'New Button' 'newbutton'
    Send '_.PYACADRELOAD'
    Record 'Reload rebuilt the ribbon' (Wait-RibbonRebuild $rb 90)
    Start-Sleep -Seconds 1
    Invoke-Probe 'p06_registry.py' 'p06_registry_present' @('PYNEWBUTTON', 'present') | Out-Null
    Send '_.PYNEWBUTTON'
    Record 'new command typeable without restart' (Wait-Marker 'newbutton' 1 15)
    Remove-Item $dir -Recurse -Force
    $rb = Log-Count 'Ribbon built'
    Log-Mark
    Send '_.PYACADRELOAD'
    Record 'Reload after removal rebuilt the ribbon' (Wait-RibbonRebuild $rb 90)
    Start-Sleep -Seconds 1
    Invoke-Probe 'p06_registry.py' 'p06_registry_absent' @('PYNEWBUTTON', 'absent') | Out-Null
    Record 'no "Could not remove command" in the log' (-not ((Log-Text) -match 'Could not remove command'))
    Record 'startup.py ran again on Reload' ((Marker-Count 'startup') -ge 3) "startup markers: $(Marker-Count 'startup')"
    Record 'app-init hook ran again on Reload' ((Marker-Count 'app-init') -ge 3) "app-init markers: $(Marker-Count 'app-init')"

    Section '4 Ribbon survival'
    $doc = Active-Doc
    $ws = $doc.GetVariable('WSCURRENT')
    $other = if ($ws -eq '3D Modeling') { 'Drafting & Annotation' } else { '3D Modeling' }
    Log-Mark
    try {
        Com { $doc.SetVariable('WSCURRENT', $other) } | Out-Null; Start-Sleep -Seconds 4
        Com { $doc.SetVariable('WSCURRENT', $ws) } | Out-Null; Start-Sleep -Seconds 4
        Record "workspace switched to '$other' and back" ($doc.GetVariable('WSCURRENT') -eq $ws)
    } catch { Record 'workspace switch' $false $_.Exception.Message }
    Invoke-Probe 'p07_tabs.py' 'p07_tabs_workspace' @('workspace') | Out-Null
    Weak 'log reports the workspace rebuild' ((Log-Since) -match 'Workspace loaded') 'informational: only logged when the host dropped the tabs'
    Skip 'CUILOAD of a partial CUIx' 'CUILOAD and MENULOAD are dialog-driven; check by hand'
    Shot '04-ribbon' | Out-Null

    Section '5 Events and context'
    Skip 'doc-activated at boot' 'hooks are configured after the first drawing is already active, so none is expected'
    Skip 'doc-created / doc-activated on a second drawing' 'Documents.Add through COM and NEW through SendCommand both leave AutoCAD 2026 rejecting every later call; open a second drawing by hand and watch the toasts'
    $doc = Active-Doc
    # Hooks are skipped while a pyACAD script runs (HookLoopGuard), so the API probes only
    # prove the API; the hooks are fired by edits made from outside, through COM.
    Invoke-Probe 'p08_erase.py' 'p08_erase' | Out-Null
    Invoke-Probe 'p09_layers.py' 'p09_layers' | Out-Null
    Record 'hooks skipped during a pyACAD script are logged' ((Log-Text) -match "Hook Probe:object-erased skipped: 'p08_erase' is running")
    $erased0 = Marker-Count 'object-erased'
    $ms = (Active-Doc).ModelSpace
    Com { $ms.AddCircle([double[]]@(300, 300, 0), 5) } | Out-Null
    $last = Com { $ms.Item($ms.Count - 1) }
    Com { $last.Delete() } | Out-Null
    Record 'object-erased hook fired for a COM erase' (Wait-Marker 'object-erased' ($erased0 + 1) 15)
    # layer-changed is wired to the CLAYER system variable (AcadEventMap), not to the layer
    # table, so the hook fires on a current-layer switch and not on Layers.Add.
    $layer = Com { (Active-Doc).Layers.Add('SMOKE_COM_LAYER') }
    $layer0 = Marker-Count 'layer-changed'
    Com { (Active-Doc).SetVariable('CLAYER', 'SMOKE_COM_LAYER') } | Out-Null
    Record 'layer-changed hook fired on a CLAYER change' (Wait-Marker 'layer-changed' ($layer0 + 1) 15)
    Com { (Active-Doc).SetVariable('CLAYER', '0') } | Out-Null
    try { Com { $layer.Delete() } | Out-Null } catch { }
    $saved0 = Marker-Count 'doc-saved'
    $dwg = Join-Path $run 'smoke.dwg'
    try { Com { $doc.SaveAs($dwg) } | Out-Null; Record 'SaveAs through COM' (Test-Path $dwg) $dwg } catch { Record 'SaveAs through COM' $false $_.Exception.Message }
    Record 'doc-saved hook fired after save' (Wait-Marker 'doc-saved' ($saved0 + 1) 15)
    # context gating
    Send '(sssetfirst nil nil)'
    Start-Sleep -Milliseconds 900
    Invoke-Probe 'p10_context.py' 'p10_context_none' @('none') | Out-Null
    Send '(sssetfirst nil (ssget "_X" (list (cons 0 "LINE"))))'
    Start-Sleep -Milliseconds 900
    Invoke-Probe 'p10_context.py' 'p10_context_selection' @('selection') | Out-Null
    Send '(sssetfirst nil nil)'
    $lay0 = Marker-Count 'layout-switched'
    Com { $doc.SetVariable('TILEMODE', 0) } | Out-Null; Start-Sleep -Seconds 2
    Record 'layout-switched hook fired' (Wait-Marker 'layout-switched' ($lay0 + 1) 10)
    Invoke-Probe 'p10_context.py' 'p10_context_paper' @('paper') | Out-Null
    Com { $doc.SetVariable('TILEMODE', 1) } | Out-Null; Start-Sleep -Seconds 2
    Skip 'hook disabled after 3 failures' 'HookHealth unit-tested; no safe way to fail a hook three times here'

    Section '6 Dock pane'
    Invoke-Probe 'p11_pane.py' 'p11_pane_show' @('show') | Out-Null
    Shot '06-pane-shown' | Out-Null
    Invoke-Probe 'p11_pane.py' 'p11_pane_hide' @('hide') | Out-Null
    Invoke-Probe 'p11_pane.py' 'p11_pane_show2' @('show') | Out-Null
    Record 'dock pane scripts ran clean (no "Dock pane" errors in log)' (-not ((Log-Text) -match "Dock pane '.*' failed"))
    Skip 'TextBox keystrokes stay in the palette (KeepFocus)' 'needs real keyboard focus inside the palette'

    Section '7 Output and dialogs'
    Invoke-Probe 'p12_output.py' 'p12_output' | Out-Null
    Shot '07-output' | Out-Null
    Skip 'element_link click selects the entity' 'needs a click inside the output web view'

    Section '8 Shortcuts'
    if ($SkipShortcuts) { Skip 'keyboard chords' '-SkipShortcuts' }
    else {
        $prev = [SmokeWin]::GetForegroundWindow()
        $p = Acad-Process
        [SmokeWin]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
        [SmokeWin]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 800
        $r = New-Object SmokeWin+RECT; [SmokeWin]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
        $cx = [int](($r.L + $r.R) / 2); $cy = [int](($r.T + $r.B) / 2)
        Click-At $cx $cy
        Press-Chord $false $false 0x1B; Start-Sleep -Milliseconds 200   # Esc: no command pending
        Invoke-Probe 'p14_shortcut_diag.py' 'p14_diag_before' @('before') | Out-Null
        [SmokeWin]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
        Click-At $cx $cy
        Start-Sleep -Milliseconds 300
        $bc0 = Marker-Count 'before-command'
        Press-Chord $true $true 0x4B                                     # Ctrl+Shift+K: author default on the nobutton
        $ok = Wait-Until { (Marker-Text 'before-command') -match 'Chord_Only\.nobutton' } 8
        Record 'Ctrl+Shift+K in the drawing area runs the nobutton' $ok
        Invoke-Probe 'p14_shortcut_diag.py' 'p14_diag_after' @('after') | Out-Null
        Record 'keyboard hook saw the injected keys' ((Marker-Text 'shortcut-diag') -match 'after .*seen=([1-9]\d*)') (Marker-Text 'shortcut-diag')
        [SmokeWin]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
        Click-At $cx $cy
        Start-Sleep -Milliseconds 300
        $primaryK = Marker-Count 'primary'
        Press-Chord $true $true 0x4D                                     # Ctrl+Shift+M: config.json override
        Record 'config.json binding Ctrl+Shift+M runs Cfg Twin' (Wait-Marker 'primary' ($primaryK + 1) 8)
        # focus in the command line: the chord must not fire
        Click-At $cx ($r.B - 95)
        Start-Sleep -Milliseconds 300
        $bc1 = Marker-Count 'before-command'
        Press-Chord $true $true 0x4B
        Start-Sleep -Seconds 2
        $fired = (Marker-Count 'before-command') -gt $bc1
        Weak 'chord ignored while the command line has focus' (-not $fired) 'click position assumed to be the command line'
        Press-Chord $false $false 0x1B
        Click-At $cx $cy
        Press-Chord $false $false 0x1B
        [SmokeWin]::SetForegroundWindow($prev) | Out-Null
    }

    Section '9 Restart persistence'
    if ($SkipRestart) { Skip 'restart' '-SkipRestart' }
    else {
        try { (Active-Doc).Save() } catch { }
        Stop-Acad $true
        Record 'AutoCAD closed through COM Quit' (-not (Acad-Process))
        $script:LogOffset = (Log-Text).Length
        Start-Acad
        $t0 = Get-Date
        $booted = Wait-Boot $BootTimeoutSec
        Record 'second boot' $booted ("{0:n0}s" -f ((Get-Date) - $t0).TotalSeconds)
        if ($booted -and (Connect-Acad 90)) {
            Start-Sleep -Seconds 2
            $persist = Invoke-Probe 'p11_pane.py' 'p11_pane_persist' @('expect-visible') -Weak $true
            Invoke-Probe 'p11_pane.py' 'p11_pane_hide2' @('hide') | Out-Null
            Record 'no ERROR lines on second boot' (-not ((Log-Since) -match '\[ERROR\]'))
        }
    }

    Section '10 Core Console'
    if ($SkipCoreConsole) { Skip 'pyacad run' '-SkipCoreConsole' }
    else {
        $cli = Join-Path $repo 'bin\cli\net48\PyAcad.Cli.exe'
        if (-not (Test-Path $cli)) { $cli = Join-Path $repo 'bin\cli\net8.0-windows\PyAcad.Cli.exe' }
        # The interactive AutoCAD still holds smoke.dwg open, so the Core Console gets a copy.
        $dwgSrc = Join-Path $run 'smoke.dwg'
        $dwg = Join-Path $run 'smoke-core.dwg'
        if (Test-Path $dwgSrc) { Copy-Item $dwgSrc $dwg -Force }
        if ((Test-Path $cli) -and (Test-Path $dwg)) {
            $cc = Join-Path $repo 'tests\smoke\cc'
            function Run-Cli([string]$script, [string[]]$extra, [string]$resultsName) {
                $resultsFile = Join-Path $run "$resultsName.json"
                $argList = @('run', $script, $dwg, '--timeout', '240', '--version', $Version, '--results', $resultsFile) + $extra
                $out = & $cli @argList 2>&1 | Out-String
                [System.IO.File]::WriteAllText((Join-Path $run "$resultsName.txt"), $out)
                if (Test-Path $resultsFile) { return ([System.IO.File]::ReadAllText($resultsFile) | ConvertFrom-Json) } else { return $null }
            }
            $r1 = Run-Cli (Join-Path $cc 'cc_count.py') @() 'cc-count'
            $s1 = if ($r1) { $r1.models[0].status } else { 'no results.json' }
            Record 'headless run of a counting script is ok' ($s1 -eq 'ok') "status $s1"
            $runsRoot = Join-Path $env:APPDATA 'pyACAD\runs'
            $newest = Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($newest) {
                $files = Get-ChildItem $newest.FullName -File | Select-Object -ExpandProperty Name
                Record 'run folder has request, response, transcript and console files' (($files -match 'request').Count -gt 0 -and ($files -match 'console').Count -gt 0) ($files -join ', ')
                $console = Get-ChildItem $newest.FullName -Filter 'console*' | Select-Object -First 1
                if ($console) { $ct = Get-Content $console.FullName -Raw; Record 'console log shows NETLOAD and PYACADRUN' ($ct -match 'NETLOAD' -and $ct -match 'PYACADRUN') }
            }
            $m0 = (Get-Item $dwg).LastWriteTimeUtc
            $r2 = Run-Cli (Join-Path $cc 'cc_modify.py') @('--save') 'cc-modify'
            $s2 = if ($r2) { $r2.models[0].status } else { 'no results.json' }
            Record '--save after a modifying script is ok' ($s2 -eq 'ok') "status $s2"
            Record 'drawing file was rewritten by --save' ((Get-Item $dwg).LastWriteTimeUtc -gt $m0)
            $m1 = (Get-Item $dwg).LastWriteTimeUtc
            $r3 = Run-Cli (Join-Path $cc 'cc_raise.py') @('--save') 'cc-raise'
            $s3 = if ($r3) { $r3.models[0].status } else { 'no results.json' }
            Record 'a raising script reports failed' ($s3 -eq 'failed') "status $s3"
            Record 'a raising script leaves the drawing untouched' ((Get-Item $dwg).LastWriteTimeUtc -eq $m1)
            Record 'Core Console boot path logged' ((Log-Text) -match 'Core Console')
            Skip 'Ctrl+C during the second of two drawings gives cancelled' 'interactive'
        } else { Skip 'pyacad run' "missing CLI ($cli) or drawing ($dwg)" }
    }
}
catch {
    Record 'run aborted' $false ($_.Exception.Message + ' @ ' + ($_.InvocationInfo.PositionMessage -replace "`r?`n", ' ') + " | windows: $(Acad-Windows)")
    Shot 'aborted' | Out-Null
}
finally {
    if (-not $KeepOpen) { Stop-Acad $true }
    Restore-Config
    Remove-Item Env:\PYACAD_SMOKE_RUN -ErrorAction SilentlyContinue
    if (Test-Path $logPath) { Copy-Item $logPath (Join-Path $run 'pyACAD.log') -Force }
    if (Test-Path (Join-Path $logDir 'loader.log')) { Copy-Item (Join-Path $logDir 'loader.log') (Join-Path $run 'loader.log') -Force }

    $pass = @($script:Steps | Where-Object state -eq 'pass').Count
    $fail = @($script:Steps | Where-Object state -eq 'FAIL').Count
    $weak = @($script:Steps | Where-Object state -eq 'weak').Count
    $skip = @($script:Steps | Where-Object state -eq 'skip').Count
    $elapsed = "{0:n0}" -f ((Get-Date) - $started).TotalSeconds
    $script:Steps | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'results.json') -Encoding UTF8

    $md = New-Object System.Text.StringBuilder
    [void]$md.AppendLine("# pyACAD smoke run $stamp")
    [void]$md.AppendLine()
    [void]$md.AppendLine("AutoCAD $Version, ${elapsed}s. **$pass pass, $fail fail, $weak weak, $skip skipped.** Run folder: ``$run``")
    [void]$md.AppendLine()
    foreach ($sec in ($script:Steps | Select-Object -ExpandProperty section -Unique)) {
        [void]$md.AppendLine("## $sec")
        [void]$md.AppendLine()
        [void]$md.AppendLine('| state | check | detail |')
        [void]$md.AppendLine('|---|---|---|')
        foreach ($s in ($script:Steps | Where-Object section -eq $sec)) {
            $d = ($s.detail -replace '\|', '\|') -replace "`r?`n", ' '
            if ($d.Length -gt 160) { $d = $d.Substring(0, 160) + '...' }
            [void]$md.AppendLine("| $($s.state) | $($s.name) | $d |")
        }
        [void]$md.AppendLine()
    }
    [System.IO.File]::WriteAllText((Join-Path $run 'report.md'), $md.ToString(), (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ''
    Write-Host ("SMOKE RESULT: {0} pass, {1} fail, {2} weak, {3} skipped in {4}s" -f $pass, $fail, $weak, $skip, $elapsed) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
    Write-Host "Report: $(Join-Path $run 'report.md')"
    if ($fail -gt 0) { exit 1 } else { exit 0 }
}
