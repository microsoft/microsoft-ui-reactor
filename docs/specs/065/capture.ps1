<#
.SYNOPSIS
    Captures the spec 065 reference screenshots of SettingsCard and SettingsExpander.

.DESCRIPTION
    Drives the "Settings reference" page of samples/apps/wct-controls through
    `winapp ui` and saves one cropped PNG per sample, theme and state.

    * Each theme gets a fresh app instance: the window is placed at (0,0), sized
      with SetWindowPos (outer size in DIPs, scaled by the window DPI), and the theme is set with the page's
      "Light theme" / "Dark theme" buttons, which set RequestedTheme on the app
      root. The Windows theme is not touched.
    * Each PNG is the sample's UI Automation bounds plus -Pad px on every side, cut
      out of a capture of the page's scroll viewport (PART_ScrollPresenter). The
      viewport capture never contains the window frame or popups such as tooltips.
    * Rest states are captured before any interaction. Interactive states follow:
      keyboard focus first, then hover. Each interactive capture is compared with
      a rest capture taken just before it and must differ, because `winapp ui
      hover` / `send-keys` can report success without effect.

    -Set Interactive (or the default, All) uses real input (SendInput), so the
    Windows session must be unlocked and the window visible; an RDP client window
    must not be minimized.

    Build the app first:
        dotnet build samples/apps/wct-controls -p:Platform=x64 -p:RuntimeIdentifier=win-x64

.EXAMPLE
    ./docs/specs/065/capture.ps1                      # static + interactive states
    ./docs/specs/065/capture.ps1 -Set Static          # no real input needed
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\..\samples\apps\wct-controls\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\wct-controls.exe'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'toolkit'),
    [string]$NavItem = 'Settings reference',
    [ValidateSet('All', 'Static', 'Interactive')]
    [string]$Set = 'All',
    [int]$Pad = 8,
    # Outer window height in DIPs. Lower it when the screen's work area is shorter, or the
    # viewport capture is clipped; samples below the fold are scrolled into view.
    [int]$WindowHeight = 1060
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Spec065 -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
'@
# Per-monitor DPI aware, so SetWindowPos takes the physical sizes computed below. A DPI-unaware
# PowerShell would have them scaled a second time by Windows on any display above 100%.
[void][Spec065.Win32]::SetProcessDpiAwarenessContext([IntPtr]-4)

# Outer window sizes. The page column is capped at 656 px (640 px cards plus an
# 8 px frame), so "wide" gives 640 px cards. "narrow" gives cards between the
# Toolkit's two wrap thresholds (286 <= width < 476: content moves below the
# header); "xnarrow" goes below 286 px, where the header icon is also hidden.
$Sizes = [ordered]@{
    wide    = @(1000, $WindowHeight)
    narrow  = @(480, $WindowHeight)
    xnarrow = @(360, $WindowHeight)
}

$Viewport = 'PART_ScrollPresenter'
$script:Proc = $null
$script:Hwnd = $null
$script:Window = $null
$script:Log = [System.Collections.Generic.List[object]]::new()
# Window DPI / 96. Window sizes and the crop padding are in DIPs and scaled by it, so every
# display scale gives the same layout (and the PNGs are that many times larger).
$script:Scale = 1.0

function Invoke-Ui {
    # UIA occasionally reports a just-found element as gone while the page settles;
    # every verb used here is idempotent, so retry that case.
    for ($attempt = 1; ; $attempt++) {
        $out = & winapp ui @args -w $script:Hwnd 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) { return $out }
        if ($attempt -ge 3 -or $out -notmatch 'no longer accessible') { throw "winapp ui $($args -join ' ') failed:`n$out" }
        Start-Sleep -Milliseconds 750
    }
}

# Scrolls the page viewport; a page that fits without scrolling is already in place.
function Move-Viewport([ValidateSet('top', 'bottom')][string]$To) {
    $out = & winapp ui scroll $Viewport --to $To -w $script:Hwnd 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -and $out -notmatch 'cannot scroll') { throw "winapp ui scroll --to $To failed:`n$out" }
    Start-Sleep -Milliseconds 300
}

# UI Automation bounds (screen pixels) of the one element with this AutomationId.
function Get-Bounds([string]$AutomationId) {
    $json = & winapp ui search $AutomationId -w $script:Hwnd --json 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "winapp ui search $AutomationId failed:`n$json" }
    $found = @((ConvertFrom-Json $json).matches | Where-Object { $_.automationId -ceq $AutomationId })
    if ($found.Count -ne 1) { throw "Expected one element with AutomationId '$AutomationId', found $($found.Count)." }
    $found[0]
}

function Set-WindowSize([string]$Name) {
    $w, $h = $Sizes[$Name]
    $pw = [int][Math]::Round($w * $script:Scale); $ph = [int][Math]::Round($h * $script:Scale)
    [void][Spec065.Win32]::SetWindowPos($script:Hwnd, [IntPtr]::Zero, 0, 0, $pw, $ph, 0x14) # NOZORDER | NOACTIVATE
    Start-Sleep -Milliseconds 1500
    $script:Window = "$Name ${w}x$h"
}

# A fresh app instance per theme: hover and focus leave state behind (the
# expander's animated chevron keeps a different color after a hover), so rest
# captures must come from an instance nobody has interacted with yet.
function Start-App([string]$Theme) {
    # Keep the pointer off the samples so nothing starts in a hover state: the window opens
    # at the screen's top-left corner, where its title bar is.
    [void][Spec065.Win32]::SetCursorPos(0, 0)
    $script:Proc = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
    for ($i = 0; $i -lt 40 -and $script:Proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $script:Proc.Refresh() }
    if ($script:Proc.MainWindowHandle -eq 0) { throw 'The app window did not appear.' }
    $script:Hwnd = $script:Proc.MainWindowHandle
    $dpi = [Spec065.Win32]::GetDpiForWindow($script:Hwnd)
    $script:Scale = $dpi / 96.0
    Write-Host "  window DPI $dpi (scale $script:Scale)"
    Start-Sleep -Seconds 2
    Set-WindowSize wide
    try { Invoke-Ui invoke $NavItem | Out-Null }
    catch {
        # A window narrower than the NavigationView's compact threshold hides the pane.
        Invoke-Ui invoke 'Open Navigation' | Out-Null
        Start-Sleep -Milliseconds 800
        Invoke-Ui invoke $NavItem | Out-Null
    }
    Start-Sleep -Milliseconds 1500
    Invoke-Ui invoke "$Theme theme" --type Button | Out-Null
    Start-Sleep -Milliseconds 1000
}

function Stop-App {
    if ($script:Proc -and -not $script:Proc.HasExited) { Stop-Process -Id $script:Proc.Id }
    $script:Proc = $null
}

function Reset-Pointer {
    # Park the pointer on the page heading, away from every sample.
    $vp = Get-Bounds $Viewport
    Invoke-Ui hover --at "$($vp.x + 4),$($vp.y + 4)" --dwell-time 400 | Out-Null
}

function Test-InViewport($El, $Vp) {
    # An element scrolled out of view reports empty UI Automation bounds.
    $p = $Pad * $script:Scale
    $El.width -gt 0 -and $El.height -gt 0 -and ($El.y - $p) -ge $Vp.y -and ($El.y + $El.height + $p) -le ($Vp.y + $Vp.height)
}

function Save-Sample([string]$Id, [string]$File, [string]$State, [switch]$KeepScroll, [string]$Path) {
    if (-not $KeepScroll) { Move-Viewport top }
    $vp = Get-Bounds $Viewport
    $el = Get-Bounds $Id
    if (-not $KeepScroll -and -not (Test-InViewport $el $vp)) {
        Move-Viewport bottom
        $vp = Get-Bounds $Viewport
        $el = Get-Bounds $Id
    }
    if (-not $KeepScroll -and -not (Test-InViewport $el $vp)) {
        # A page taller than two viewports: bring the sample into view.
        Invoke-Ui scroll-into-view $Id | Out-Null
        Start-Sleep -Milliseconds 300
        $vp = Get-Bounds $Viewport
        $el = Get-Bounds $Id
    }
    $pad = [int][Math]::Round($Pad * $script:Scale)
    $x = $el.x - $pad - $vp.x; $y = $el.y - $pad - $vp.y
    $w = $el.width + 2 * $pad; $h = $el.height + 2 * $pad
    if ($x -lt 0 -or $y -lt 0 -or ($x + $w) -gt $vp.width -or ($y + $h) -gt $vp.height) {
        throw "'$Id' plus ${pad}px is not fully inside the viewport ($x,$y ${w}x$h in $($vp.width)x$($vp.height))."
    }

    $target = if ($Path) { $Path } else { Join-Path $OutDir $File }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "spec065-viewport-$PID.png"
    Invoke-Ui screenshot $Viewport -o $tmp | Out-Null
    $img = [System.Drawing.Bitmap]::FromFile($tmp)
    try {
        if ($img.Width -ne $vp.width -or $img.Height -ne $vp.height) {
            throw "Viewport capture is $($img.Width)x$($img.Height), expected $($vp.width)x$($vp.height)."
        }
        $crop = $img.Clone([System.Drawing.Rectangle]::new($x, $y, $w, $h), $img.PixelFormat)
        try { $crop.Save($target, [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $crop.Dispose() }
    } finally {
        $img.Dispose()
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
    if ($Path) { return }
    $script:Log.Add([pscustomobject]@{
        File = $File; Sample = $Id; State = $State; Window = $script:Window
        Element = "$($el.width)x$($el.height)"; Png = "${w}x$h"
    })
    Write-Host "  $File  ($Id, $($el.width)x$($el.height))"
}

function Get-ChangedPixelCount([string]$PathA, [string]$PathB) {
    $a = [System.Drawing.Bitmap]::FromFile($PathA)
    $b = [System.Drawing.Bitmap]::FromFile($PathB)
    try {
        if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) { return [int]::MaxValue }
        $n = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                if ($a.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) { $n++ }
            }
        }
        $n
    } finally { $a.Dispose(); $b.Dispose() }
}

# Captures an interactive state and proves it rendered: `winapp ui hover` and
# `send-keys` can report success without effect when the desktop is not
# interactive, which would silently save a copy of the rest state.
function Save-InteractiveSample([string]$Id, [string]$File, [string]$State, [scriptblock]$Enter) {
    $rest = Join-Path ([IO.Path]::GetTempPath()) "spec065-rest-$PID.png"
    try {
        Save-Sample $Id -KeepScroll -Path $rest
        & $Enter
        Save-Sample $Id $File $State -KeepScroll
        $changed = Get-ChangedPixelCount $rest (Join-Path $OutDir $File)
        if ($changed -lt 100) {
            throw "$File matches the rest state ($changed px differ). Is the desktop unlocked and the window visible?"
        }
    } finally { Remove-Item $rest -ErrorAction SilentlyContinue }
}

# Samples on the page, by AutomationId: (id, file pattern, state).
$Static = @(
    @('ref-card',              'card-{0}-rest.png',            'rest'),
    @('ref-card-clickable',    'card-clickable-{0}-rest.png',  'rest'),
    @('ref-card-vertical',     'card-vertical-{0}-rest.png',   'rest'),
    @('ref-card-disabled',     'card-disabled-{0}-rest.png',   'disabled'),
    @('ref-expander',          'expander-{0}-collapsed.png',   'collapsed'),
    @('ref-expander-expanded', 'expander-{0}-expanded.png',    'expanded'),
    @('ref-card-left',         'card-left-{0}-rest.png',       'rest'),
    @('ref-card-actionicon',   'card-actionicon-{0}-rest.png', 'rest'),
    @('ref-expander-items',    'expander-items-{0}-expanded.png', 'expanded')
)

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
foreach ($theme in 'Light', 'Dark') {
    $t = $theme.ToLowerInvariant()
    Write-Host "$theme theme"
    try {
        Start-App $theme
        if ($Set -ne 'Interactive') {
            foreach ($s in $Static) { Save-Sample $s[0] ($s[1] -f $t) $s[2] }

            Set-WindowSize narrow
            Save-Sample 'ref-card' "card-$t-narrow.png" 'rest'
            Save-Sample 'ref-card-vertical' "card-vertical-$t-narrow.png" 'rest'
            Save-Sample 'ref-card-left' "card-left-$t-narrow.png" 'rest'
            Save-Sample 'ref-expander-expanded' "expander-$t-narrow.png" 'expanded'
            Set-WindowSize xnarrow
            Save-Sample 'ref-card' "card-$t-xnarrow.png" 'rest'
            Set-WindowSize wide
        }

        if ($Set -ne 'Static') {
            Move-Viewport top
            Reset-Pointer

            # Keyboard focus first, before any hover has touched the samples. Tab
            # from the previous tab stop, so the focus state is Keyboard (the
            # system focus visual), not Programmatic.
            Save-InteractiveSample 'ref-card-clickable' "card-clickable-$t-focus.png" 'keyboard focus' {
                Invoke-Ui focus Notifications --type Button | Out-Null
                Invoke-Ui send-keys tab --via send-input | Out-Null
                Start-Sleep -Milliseconds 500
            }
            Save-InteractiveSample 'ref-expander' "expander-$t-focus.png" 'keyboard focus' {
                Invoke-Ui focus Theme --type ComboBox | Out-Null
                Invoke-Ui send-keys tab --via send-input | Out-Null
                Start-Sleep -Milliseconds 500
            }
            # Move focus off the samples so it does not show in the hover captures.
            Invoke-Ui focus "$theme theme" --type Button | Out-Null

            # Hover: only a clickable card has pointer-over visuals; the expander
            # header shows the Expander's own pointer-over state.
            Save-InteractiveSample 'ref-card-clickable' "card-clickable-$t-hover.png" 'hover' {
                Invoke-Ui hover ref-card-clickable | Out-Null
            }
            Reset-Pointer
            Save-InteractiveSample 'ref-expander' "expander-$t-hover.png" 'hover' {
                Invoke-Ui hover ref-expander | Out-Null
            }
            Reset-Pointer

            # A card without a click action shows no pointer-over visual; this capture
            # records that (compare it with card-<theme>-rest.png), so no change is expected.
            Move-Viewport top
            Invoke-Ui hover ref-card | Out-Null
            Save-Sample 'ref-card' "card-$t-hover.png" 'hover (not clickable)' -KeepScroll
            Reset-Pointer
        }
    } finally {
        Stop-App
    }
}

Write-Host ''
Write-Host '| File | Sample | State | Window (outer) | Element | PNG |'
Write-Host '| --- | --- | --- | --- | --- | --- |'
foreach ($r in $script:Log) {
    Write-Host "| ``$($r.File)`` | ``$($r.Sample)`` | $($r.State) | $($r.Window) | $($r.Element) | $($r.Png) |"
}
