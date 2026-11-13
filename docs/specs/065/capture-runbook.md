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
```

Capture both sets in one session, one after the other, so they share the display scale,
accent color and Windows build.

Hover and keyboard focus use real input (SendInput). The Windows session must be
unlocked and the app window visible. In a Remote Desktop session, do not minimize
the RDP window. The script checks that every hover and focus capture differs from
the rest state, and stops with an error if it does not.

What [`capture.ps1`](capture.ps1) does for each theme (light, then dark):

1. Moves the mouse pointer off the window, starts a fresh app instance, places the
   window at (0, 0) at 1000 × 1060, opens the page (`-NavItem`) and selects the theme.
2. Static states: crops each sample from a capture of the page's scroll viewport
   (`PART_ScrollPresenter`): the sample's UI Automation bounds plus 8 px on every
   side. The viewport capture has no window frame and no popups (tooltips).
3. Resizes the window to 480 × 1060 and 360 × 1060 for the narrow captures,
   scrolling the page to the top or bottom so each sample is fully visible.
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

| Name | Window | NavigationView pane | Card width | Toolkit layout |
| --- | --- | --- | --- | --- |
| wide | 1000 × 1060 | Compact | 640 | `Right`: content next to the header |
| narrow | 480 × 1060 | Minimal | 368 | `RightWrapped` (286 ≤ width < 476): content below the header |
| xnarrow | 360 × 1060 | Minimal | 248 | `RightWrappedNoIcon` (width < 286): header icon hidden too |

## Files

Each file name is `<sample>-<theme>-<state>.png`. Every PNG is the element plus 8 px
on each side.

| File (light and dark) | Sample | State | Window | Element | PNG |
| --- | --- | --- | --- | --- | --- |
| `card-{light,dark}-rest.png` | `ref-card` | rest | wide | 640 × 70 | 656 × 86 |
| `card-clickable-{light,dark}-rest.png` | `ref-card-clickable` | rest | wide | 640 × 69 | 656 × 85 |
| `card-clickable-{light,dark}-hover.png` | `ref-card-clickable` | hover | wide | 640 × 69 | 656 × 85 |
| `card-clickable-{light,dark}-focus.png` | `ref-card-clickable` | keyboard focus | wide | 640 × 69 | 656 × 85 |
| `card-vertical-{light,dark}-rest.png` | `ref-card-vertical` | rest | wide | 640 × 109 | 656 × 125 |
| `card-disabled-{light,dark}-rest.png` | `ref-card-disabled` | disabled | wide | 640 × 70 | 656 × 86 |
| `expander-{light,dark}-collapsed.png` | `ref-expander` | collapsed | wide | 640 × 70 | 656 × 86 |
| `expander-{light,dark}-hover.png` | `ref-expander` | hover (header) | wide | 640 × 70 | 656 × 86 |
| `expander-{light,dark}-focus.png` | `ref-expander` | keyboard focus (header) | wide | 640 × 70 | 656 × 86 |
| `expander-{light,dark}-expanded.png` | `ref-expander-expanded` | expanded | wide | 640 × 174 | 656 × 190 |
| `card-{light,dark}-narrow.png` | `ref-card` | rest | narrow | 368 × 113 | 384 × 129 |
| `card-vertical-{light,dark}-narrow.png` | `ref-card-vertical` | rest | narrow | 368 × 109 | 384 × 125 |
| `expander-{light,dark}-narrow.png` | `ref-expander-expanded` | expanded | narrow | 368 × 264 | 384 × 280 |
| `card-{light,dark}-xnarrow.png` | `ref-card` | rest | xnarrow | 248 × 113 | 264 × 129 |

Center pixel of a card, measured on the page background:

| State | Light | Dark |
| --- | --- | --- |
| Rest (card and expander header) | `#FBFBFB` | `#2B2B2B` |
| Hover (clickable card and expander header) | `#F6F6F6` | `#323232` |

## Not captured

- **High contrast.** Checked by hand.
- **Pressed.** `winapp ui` has no press-and-hold action.

## Environment of the Toolkit set

| | |
| --- | --- |
| Toolkit package | `CommunityToolkit.WinUI.Controls.SettingsControls` 8.3.260402-preview2 |
| Windows App SDK | 2.2.0 |
| Windows | build 26300.9911, Remote Desktop session |
| Display scale | 100% (96 DPI) |
| Accent color | Windows default blue (`#0078D4`) |
| winapp CLI | 0.8.0 |
| Repository | `main` at `4cd63979` plus the Settings reference page |

The `toolkit/` and `reactor/` file names match one to one, so a set is compared file by file.
