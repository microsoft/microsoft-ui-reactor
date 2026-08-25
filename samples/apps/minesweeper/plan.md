# Reactor Minesweeper — Session Plan

This file is the recovery doc for the Minesweeper sample. It captures what's
been built, what's known to work, what's known to be unverified, and what to
do next. Pick this up on any machine.

> **Branch:** `user/jevansa/minesweeper` on the `fork` remote
> (https://github.com/jevansaks/microsoft-ui-reactor)
> **Last commit:** "Add Minesweeper sample app"

## What this is

A faithful Reactor port of classic Minesweeper, sitting at
`samples/apps/minesweeper/`. Pure C#, no XAML, theme-aware, with a full
unit-test suite. All visuals are written from scratch — no copied artwork.

## Architecture

```
samples/apps/minesweeper/
  Minesweeper.csproj          ← net10.0-windows10.0.22621.0, RollForward LatestMajor
  Program.cs                  ← ReactorApp.Run<MinesweeperApp>(...)
  App.cs                      ← root component (timer effect, layout, menu, modals)
  Game/
    Difficulty.cs             ← record + Beginner/Intermediate/Expert presets
    Board.cs                  ← immutable Board record + pure BoardReducer
    AppReducer.cs             ← AppState record + AppAction hierarchy + pure AppReducer
  Components/
    BoardView.cs              ← Grid of CellComponents, propagates chord-preview state
    CellComponent.cs          ← bevel styling, glyph rendering, pointer/chord input
    LedDisplay.cs             ← red-on-black 3-digit display
    SmileyButton.cs           ← 🙂 😮 😎 😵 reset button
    StatusPanel.cs            ← LED|smiley|LED row in a sunken bevel
    ModalOverlay.cs           ← custom Border-based modal (sidesteps WinUI ContentDialog bug)
    Dialogs/DialogContent.cs  ← pure-function bodies for HighScores/NewBest/Custom
  Persistence/
    HighScoreRecord.cs        ← HighScores/HighScoreEntry records + JsonSerializerContext (AOT)
    HighScoreStore.cs         ← reads/writes %LocalAppData%/ReactorMinesweeper/highscores.json

tests/Reactor.Tests/Minesweeper/
  BoardReducerTests.cs        ← 12 tests (game rules, first-click safety, win/loss)
  AppReducerTests.cs          ← 17 tests (chord preview, reset, dialogs, save score)
  HighScoreStoreTests.cs      ← 12 tests (round-trip, atomic write, IsNewRecord)
```

## Key design decisions

- **Single immutable `AppState`** driven by one `UseReducer` hook. All
  transitions live in `AppReducer.Reduce` — pure function, easy to test, no
  WinUI dependencies.
- **First-click safety**: the click and all 8 neighbors are guaranteed
  mine-free. Mine placement happens lazily on the first reveal.
- **Custom `ModalOverlay`** instead of WinUI's `ContentDialog`. Reason:
  Reactor's reconciler mounts dialog content with no XamlRoot (because it's
  not in the visual tree yet); `ContentDialog.ShowAsync` then throws and the
  framework swallows the exception. The overlay is a `Border` stacked above
  the board in a Grid cell — works reliably.
- **Right-press chord preview**: pointer-driven. Right-button press on a
  revealed numbered cell → reducer flips on a `ChordPreview` flag →
  adjacent cells flatten visually → right-button release commits the chord.
  (The original L+R simultaneous-press gesture proved finicky; switched to
  right-press-on-number which is unambiguous and easier to discover.)
- **Theme-aware**: every color goes through `Theme.*` tokens. Number
  palette has separate light/dark variants tuned for WCAG-AA against
  `Theme.LayerFill`.
- **AOT-friendly persistence**: `[JsonSerializable]` + source-generated
  `JsonSerializerContext` so the JSON read/write doesn't reflect.
- **Atomic save**: write to `*.tmp`, then `File.Move(overwrite:true)`. A
  crash mid-save can never corrupt the score file.
- **`<RollForward>LatestMajor</RollForward>`** in csproj so the exe runs
  on any installed 10.x runtime (saved trouble after the SDK preview built
  it once and the user's stable runtime refused).

## Test status (last run)

```
Passed!  - Failed: 0, Passed: 41, Skipped: 0, Total: 41
```

Run with:
```
dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64 \
  --filter "FullyQualifiedName~Microsoft.UI.Reactor.Tests.Minesweeper"
```

> Note: the broader Reactor.Tests suite has **one unrelated failing test**
> (`IntlAccessorTests.FormatDate_Short_ReturnsShortDate` in the
> Localization namespace). Not caused by this work; pre-existing.

## What's UNVERIFIED

I made the bug fixes from the user's bug list but **never visually
confirmed** them in a running app. Source-level verification only:

| Fix | Source location | Tests cover it? |
|---|---|---|
| Timer ticks reliably after reset | `App.cs:31-67` (UseRef pattern) | indirectly via `AppReducerTests.Tick_*` |
| Bomb glyph not clipped | `CellComponent.cs:63` (font factor 0.42) + `LineHeight` pin | no — pure visual |
| L+R chord preview + commit | `CellComponent.cs:90-125` + `AppReducer.cs:88-108` | yes (`AppReducerTests.BeginChordPreview_*`, `EndChordPreview_*`) — gesture changed to right-press-and-hold on a number; reducer/action shape unchanged |
| Reset doesn't visually leave revealed tiles | removed `Transition()` from CellComponent + `AppReducerTests.Reset_ClearsElapsedAndChordPreviewAndStartsFreshBoard` | reducer-level only |
| High Scores dialog opens | rewrite to `ModalOverlay` (custom Border) | reducer covers `OpenHighScoresAction` |

## Next-session priorities

1. **Pull the branch:** `git fetch fork; git checkout user/jevansa/minesweeper`
2. **Build with the local SDK** (don't let the agent rebuild — that picks up
   10.0.300-preview if installed and breaks VS):
   `dotnet build samples/apps/minesweeper/Minesweeper.csproj -p:Platform=x64`
3. **Visually verify each fix by playing the game** — see the table above.
   If any fail, the source is in the listed location.
4. **Confirm intent re: app location** — user said "Chris said to move it
   to samples/apps" but it's already there. Maybe Chris meant `samples/Minesweeper`
   (top level)?
5. **Optional polish:**
   - Add `samples/apps/minesweeper/Minesweeper.sln` for slim VS load
     (mirror `samples/apps/headtrax/HeadTrax.sln`).
   - Keyboard navigation (arrow keys to move focus on the board, Space to
     reveal, F to flag) — currently mouse/touch only.
   - About dialog — menu item exists but is a no-op.

## Known framework quirks worth remembering

- **`UseReducer<T,A>` needs explicit type args** when `A` is an abstract
  record — overload resolution otherwise picks `UseReducer<T>(T initial)`
  and the compiler complains the lambda return type can't be a `bool`.
- **`ContentDialog` silently fails** when mounted by Reactor without an
  ancestor XamlRoot. This is why I built `ModalOverlay`. Could file an
  upstream issue; the framework's
  `Reconciler.Mount.cs:1670-1690 ShowContentDialog` swallows the exception
  in a bare `catch`.
- **`<RollForward>LatestMajor</RollForward>` matters** for samples that
  may be built once and run on different machines/runtimes.
- **`PrintWindow(hwnd, hdc, 0x2)` returns blank for WinUI 3 with Mica
  backdrop** in some configurations — the `uxmcp-capture_screenshot` tool
  goes through screen region capture instead, which means it can grab
  whatever's on top of the target window. **Be careful** screenshotting:
  always check the captured image for unintended content (the user has
  Teams/internal slides up). Better: bring window to known coords first
  AND look for a way to read pixels directly from the WinUI surface.

## Bug list status

All five from the playtest session are addressed in source:

- ✅ Chord with preview-box highlight (gesture: **right-press-and-hold** on a revealed number; release to commit)
- ✅ Bomb glyph clipping (font sized + LineHeight pinned)
- ✅ Timer stuck at 000 after reset (switched to UseRef pattern)
- ✅ Reset shows old revealed tiles (removed Transition modifiers)
- ✅ High Scores dialog opens (rewrote with ModalOverlay)

…but **none have been visually confirmed by playing**. That's the next step.
