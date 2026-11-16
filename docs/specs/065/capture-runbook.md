# Spec 065: reference screenshot runbook

How the SettingsCard and SettingsExpander screenshots are captured, so that the Windows
Community Toolkit set (`toolkit/`) and the native Reactor port set (`reactor/`) are taken the
same way and can be compared side by side.

## What is captured

The "Settings reference" page of [`samples/apps/wct-controls`](../../../samples/apps/wct-controls)
([`SettingsReferencePage.cs`](../../../samples/apps/wct-controls/SettingsReferencePage.cs))
shows nine fixed samples built with the Toolkit controls. The "Settings port" page
([`SettingsPortReferencePage.cs`](../../../samples/apps/wct-controls/SettingsPortReferencePage.cs))
shows the same nine samples built with the Reactor port. Each sample has the same stable
AutomationId on both pages.

| AutomationId | Control | Content |
| --- | --- | --- |
| `ref-card` | SettingsCard | Icon `\uE7E7`, header "Notifications", description "Alerts from apps and the system", ToggleSwitch (on) |
| `ref-card-clickable` | SettingsCard, clickable | Icon `\uEDA2`, header "Storage", description "Free up space", chevron |
| `ref-card-vertical` | SettingsCard, `ContentAlignment.Vertical` | Icon `\uE790`, header "Theme", description "Select which app theme to display", ComboBox "Light" / "Dark" / "Use system setting" (third item selected) |
| `ref-card-disabled` | SettingsCard, `IsEnabled = false` | Icon `\uE702`, header "Bluetooth", description `Discoverable as "CONTOSO-PC"`, ToggleSwitch (off) |
| `ref-expander` | SettingsExpander, collapsed | Icon `\uE7F4`, header "Display", description "Brightness, night light and scale", ComboBox "100%" / "125%" / "150%" ("125%" selected) on the header; items: "Brightness" with a Slider (0 to 100, value 70) and "Night light" with a ToggleSwitch (off) |
| `ref-expander-expanded` | SettingsExpander, expanded | Same as `ref-expander` |
| `ref-card-left` | SettingsCard, `ContentAlignment.Left` | Header "Sync" (hidden by `Left`), CheckBox "Sync settings across devices" (checked) |
| `ref-card-actionicon` | SettingsCard, clickable, custom action icon | Icon `\uE719`, header "Microsoft Store", description "Get more apps", action icon `\uE8A7` |
| `ref-expander-items` | SettingsExpander, expanded | Icon `\uE767`, header "Sound", description "Volume and system sounds"; items header and footer (a TextBlock in a `58,12,16,12` border); items: "Volume" with a Slider (value 60) and a clickable "More sound settings" |

Page layout, which the Reactor page must copy exactly:

- One column, at most 656 px wide, with a 24 px right margin. Every sample sits in
  an 8 px frame, so wide cards are 640 px.
- The column paints `SolidBackgroundFillColorBaseBrush` (light `#F3F3F3`, dark
  `#202020`). Cards are semi-transparent, so a backdrop such as Mica would make
  the result depend on the desktop wallpaper.
- The theme is set with `RequestedTheme` on the app root (the "Light theme" and
  "Dark theme" buttons on the page). The Windows theme is not changed.

## How to capture

```powershell
dotnet build samples/apps/wct-controls -p:Platform=x64 -p:RuntimeIdentifier=win-x64
./docs/specs/065/capture.ps1                  # Toolkit set, all states, into toolkit/
./docs/specs/065/capture.ps1 -NavItem 'Settings port' -OutDir "$PWD/docs/specs/065/reactor"   # Reactor set
./docs/specs/065/capture.ps1 -Set Static      # rest, disabled, collapsed, expanded, narrow
./docs/specs/065/capture.ps1 -Set Interactive # hover and keyboard focus
./docs/specs/065/capture.ps1 -Set Interactive -Themes Dark   # redo one theme only
```

Capture both sets in one session, one after the other, so they share the display scale,
accent color and Windows build. The script is per-monitor DPI aware and scales the window
sizes and the crop margin by the display scale. `-WindowHeight` (default 1060) shortens the
window when the screen's work area is not tall enough for it. After each resize the script
checks the window's size and stops if Windows made it smaller than asked.

Hover and keyboard focus use real input (SendInput). The Windows session must be
unlocked and the app window visible. In a Remote Desktop session, do not minimize
the RDP window: while it is minimized the session takes no input, and `winapp ui hover`
fails with "No interactive desktop is available". The script checks that every hover and focus capture differs from
the rest state, and stops with an error if it does not. The one exception is
`card-{light,dark}-hover.png`: a card without a click action has no hover look, so that
capture is saved as it is, to be compared with `card-{light,dark}-rest.png`. A capture that
comes out as one flat color (Windows draws nothing while a Remote Desktop session is
disconnected) also stops the script.

Nobody may use the mouse or keyboard during a run. To check that afterwards, log input with a
low-level keyboard and mouse hook and compare it with the script's own, which is marked as
injected. In a Remote Desktop session the client echoes every pointer move the script makes:
it moves its own pointer to match and sends that move back as real input, 0–200 ms later. So
count a real pointer move as the user's only when no injected move came in the second before it.

What [`capture.ps1`](capture.ps1) does for each theme (light, then dark):

1. Moves the mouse pointer off the window, starts a fresh app instance, places the
   window at (0, 0) at 1000 × 1060, opens the page (`-NavItem`) and selects the theme.
2. Static states: crops each sample from a capture of the page's scroll viewport
   (`PART_ScrollPresenter`): the sample's UI Automation bounds plus 8 px on every
   side. The viewport capture has no window frame and no popups (tooltips). A sample
   below the fold is scrolled into view, then one step further so the frame below it
   shows too; the last sample on the page is scrolled to the end. `winapp ui scroll --to bottom`
   is not used for the others: near the end of the page the viewport's UI Automation bounds
   grow upward past the window, and the viewport capture then comes back clipped.
3. Resizes the window to 480 × 1060 and 360 × 1060 for the narrow captures, again
   scrolling each sample into view.
4. Keyboard focus: focuses the previous tab stop through UI Automation, then
   presses Tab (`send-keys tab --via send-input`), so the focus state is Keyboard
   and the system focus rectangle shows. Then focus moves to the theme button.
5. Hover: `winapp ui hover <id>` (pointer to the center of the sample, 800 ms
   dwell), then parks the pointer on the page heading. A hover over `ref-card`,
   which has no click action, is saved too, to show that it changes nothing.

Rest states come first and each theme gets a new app instance, because interaction
leaves state behind: after one hover, the Toolkit expander's chevron keeps a
different color.

The static set is byte-identical across repeated runs. Interactive captures from two
runs matched to within 1 px (one color step), except where the earlier run had the
chevron effect above.

## Window sizes

Sizes are outer window sizes in DIPs. The script multiplies them, and the 8 px crop margin,
by the window's display scale, so every scale gives the same layout and the PNGs are that
many times larger. The screen must be large enough for the wide window: Windows does not
make a window larger than the screen, and a smaller window gives the narrow layout.

| Name | Window | NavigationView pane | Card width, DIPs (pixels at 150%) | Toolkit layout |
| --- | --- | --- | --- | --- |
| wide | 1000 × 1060 | Compact | 640 (960) | `Right`: content next to the header |
| narrow | 480 × 1060 | Minimal | 368 (552) | `RightWrapped` (286 ≤ width < 476): content below the header |
| xnarrow | 360 × 1060 | Minimal | 249 (373) | `RightWrappedNoIcon` (width < 286): header icon hidden too |

## Files

Each file name is `<sample>-<theme>-<state>.png`. Every PNG is the element plus 8 DIPs (12 px)
on each side. Sizes are in pixels, at the 150% scale of the committed sets.

| File (light and dark) | Sample | State | Window | Element | PNG |
| --- | --- | --- | --- | --- | --- |
| `card-{light,dark}-rest.png` | `ref-card` | rest | wide | 960 × 106 | 984 × 130 |
| `card-{light,dark}-hover.png` | `ref-card` | hover (no click action) | wide | 960 × 106 | 984 × 130 |
| `card-clickable-{light,dark}-rest.png` | `ref-card-clickable` | rest | wide | 960 × 104 | 984 × 128 |
| `card-clickable-{light,dark}-hover.png` | `ref-card-clickable` | hover | wide | 960 × 104 | 984 × 128 |
| `card-clickable-{light,dark}-focus.png` | `ref-card-clickable` | keyboard focus | wide | 960 × 104 | 984 × 128 |
| `card-vertical-{light,dark}-rest.png` | `ref-card-vertical` | rest | wide | 960 × 164 | 984 × 188 |
| `card-disabled-{light,dark}-rest.png` | `ref-card-disabled` | disabled | wide | 960 × 105 | 984 × 129 |
| `card-left-{light,dark}-rest.png` | `ref-card-left` | rest | wide | 960 × 102 | 984 × 126 |
| `card-actionicon-{light,dark}-rest.png` | `ref-card-actionicon` | rest | wide | 960 × 103 | 984 × 127 |
| `expander-{light,dark}-collapsed.png` | `ref-expander` | collapsed | wide | 960 × 106 | 984 × 130 |
| `expander-{light,dark}-hover.png` | `ref-expander` | hover (header) | wide | 960 × 106 | 984 × 130 |
| `expander-{light,dark}-focus.png` | `ref-expander` | keyboard focus (header) | wide | 960 × 106 | 984 × 130 |
| `expander-{light,dark}-expanded.png` | `ref-expander-expanded` | expanded | wide | 960 × 263 | 984 × 287 |
| `expander-items-{light,dark}-expanded.png` | `ref-expander-items` | expanded | wide | 960 × 389 | 984 × 413 |
| `card-{light,dark}-narrow.png` | `ref-card` | rest | narrow | 552 × 170 | 576 × 194 |
| `card-vertical-{light,dark}-narrow.png` | `ref-card-vertical` | rest | narrow | 552 × 163 | 576 × 187 |
| `card-left-{light,dark}-narrow.png` | `ref-card-left` | rest | narrow | 552 × 140 | 576 × 164 |
| `expander-{light,dark}-narrow.png` | `ref-expander-expanded` | expanded | narrow | 552 × 397 | 576 × 421 |
| `card-{light,dark}-xnarrow.png` | `ref-card` | rest | xnarrow | 373 × 170 | 397 × 194 |

Center pixel of a card, measured on the page background:

| State | Light | Dark |
| --- | --- | --- |
| Rest (card and expander header) | `#FBFBFB` | `#2B2B2B` |
| Hover (clickable card and expander header) | `#F6F6F6` | `#323232` |

## Not captured

- **High contrast.** Checked by hand.
- **Pressed.** `winapp ui` has no press-and-hold action.

## Environment

Both sets were captured in one run, one after the other.

| | |
| --- | --- |
| Toolkit package | `CommunityToolkit.WinUI.Controls.SettingsControls` 8.3.260402-preview2 |
| Windows App SDK | 2.2.0 |
| Windows | build 26300.9911, Remote Desktop session |
| Display | 2496 × 1664 pixels at 150% (144 DPI), so 1664 × 1109 DIPs; work area 1664 × 1061 DIPs, so the 1060-DIP window height fits |
| Accent color | Windows default blue (`#0078D4`) |
| winapp CLI | 0.8.0 |
| Repository | `niels9001-settings-card` at `30543396` |

The first Toolkit set was taken at 100% before the port existed. It is replaced by this one, so
that both images of a pair have the same scale.

The `toolkit/` and `reactor/` file names match one to one, so a set is compared file by file.

## Whole-page comparison (`page/`)

The [spec](../065-settings-card.md#31-how-the-screenshots-are-taken) also compares the two pages
as a whole. These captures need no wide screen, so they were taken on the 540 × 1118-DIP, 200%
display that could not show the wide window. For each page ("Settings reference", "Settings
port") and theme, a fresh app instance is started with the pointer at (0, 0), the page and the
theme are selected, and the scroll viewport is captured at the top and at the bottom:

```powershell
winapp ui invoke "Open Navigation" -w $hwnd     # the pane is hidden at this width
winapp ui invoke "Settings port" -w $hwnd
winapp ui invoke "Dark theme" --type Button -w $hwnd
winapp ui scroll PART_ScrollPresenter --to top -w $hwnd
winapp ui screenshot PART_ScrollPresenter -w $hwnd -o docs/specs/065/page/reactor-dark-top.png
```

A fresh instance per page and theme matters: after a theme switch, a disabled toggle switch
keeps the outline color of the theme it was disabled in, in both controls.

| | |
| --- | --- |
| Display | 1080 × 2236 pixels at 200% (540 × 1118 DIPs), Remote Desktop session |
| Windows | build 26300.9911 |
| Toolkit package | `CommunityToolkit.WinUI.Controls.SettingsControls` 8.3.260402-preview2 |
| winapp CLI | 0.8.0 |
| Repository | `niels9001-settings-card` at `925c3828` |
