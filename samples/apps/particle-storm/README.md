# Particle Storm

Particle Storm is a Reactor + Win2D sample that renders up to 100,000 particles in a `Win2DAnimatedCanvas` while Reactor owns the WinUI sidebar, sliders, palette picker, pause switch, and burst button.

## What to look for

- `App.cs` wires Reactor state into `Win2DAnimatedCanvas(onUpdate, onDraw, drawState, isPaused)` (factories imported via `using static Microsoft.UI.Reactor.Advanced.Factories;`).
- `ParticleField.cs` owns the flat `Particle[]` buffer, physics step, palette LUTs, and Win2D sprite-batch rendering.
- `Sidebar.cs` polls the live FPS ref every ~250 ms so the retained UI does not re-render at the canvas frame rate.

## Baseline machine

<!-- TODO measured by: run the Release app on a real display, leave the default 50k target long enough for FPS to stabilize, then record the values below. -->

| Field | Value |
|---|---|
| CPU | <!-- pending measurement --> |
| GPU | <!-- pending measurement --> |
| RAM | <!-- pending measurement --> |
| Windows version | <!-- pending measurement --> |
| Win2D version | Microsoft.Graphics.Win2D 1.3.0 |

## FPS measurements

I could build/publish from this non-interactive environment, but did not claim interactive FPS because there is no reliable real display measurement here.

| Particle count | FPS | Notes |
|---:|---:|---|
| 1,000 | <!-- pending measurement --> | Measure with Release build on a real dev laptop. |
| 10,000 | <!-- pending measurement --> | Measure with Release build on a real dev laptop. |
| 50,000 | <!-- pending measurement --> | Target: sustained 60 FPS on documented baseline hardware. |
| 100,000 | <!-- pending measurement --> | Upper stress setting. |

## AOT / trim caveat

Spec 053 §10 notes that Win2D native assets are copied by NuGet's `runtimes/` mechanism even when managed code trims cleanly. That fixed native payload is a Win2D packaging limitation, not a Reactor.Advanced handler reachability issue.
