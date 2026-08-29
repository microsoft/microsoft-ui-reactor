# Source map explorer

Interactive proof of spec 010 per-element source mapping. Run it, and every
control in the sample panel reports the exact `file:line` of the DSL call that
created it.

```powershell
dotnet run --project samples/apps/source-map-explorer -c Debug -p:Platform=x64
```

## What to try

**Scan visual tree** walks the realized WinUI tree and prints every control with
its source location:

```
14 of 14 controls mapped

StackPanel  App.cs:181
  TextBlock  App.cs:183
  Border  App.cs:184
    StackPanel  App.cs:185
      TextBlock  App.cs:186
      TextBlock  App.cs:187
  ...
```

Line numbers here are a capture from one run and move whenever `App.cs` is
edited — which is the feature working, not the transcript rotting. Compare the
shape, and open the lines your own run prints.

Open `App.cs` at those lines and you will find exactly the factory call named.
`StackPanel` entries come from `VStack` / `HStack` — the `params Element?[]`
factories, which are the ones `[CallerFilePath]` structurally cannot reach,
because C# forbids a trailing optional parameter after `params`.

**Click any element** in the left panel to inspect just that one. The pointer
handler lives on the panel root and hit-tests down, so the leaves themselves stay
callback-free.

**Inspect deepest leaf** runs the same hit-test without needing a mouse. It walks
to the deepest mapped leaf, computes that control's own centre in host
coordinates, hit-tests that point, and compares the two answers:

```
TextBlock
App.cs:187

hit-test at its own centre (102,261)
  -> TextBlock  App.cs:187
  AGREES with the tree walk
```

The comparison is the assertion. If the hit-test resolved to a different control
or a different location than the tree walk reported, it prints `DISAGREES`
instead — so a broken hit-test shows up as a mismatch rather than as a
plausible-looking location.

**Source mapping: ON / OFF** flips `ReactorSourceMap.Enabled` live. With it off,
the same scan reports `0 of 14 controls mapped`. That is the runtime gate the
devtools session controls.

Toggling needs no remount. The flag governs only *new* stamps, so after flipping it
off an unchanged `TextBlock` produces an element whose `CallSite` is `null` while the
mounted one's is not. The two are still shallow-equal — `ShallowEquals` ignores
`CallSite` — so the reconciler still takes its skip path; `CallSiteChangedOnSkip` is
evaluated inside that skip arm and refreshes the control's back-pointer in place,
without a full update. An earlier version of this sample forced a generation-key
remount because without that refresh the count read `8 of 14` — the retained location
was still correct, but it made the gate look half-broken. The selftest
`SourceMapReadPath_FlagOffClears` pins the behaviour that made the workaround
unnecessary.

## Why the leaves matter

The callback-free `TextBlock`s and stacks in the inspected panel carry no
callback, key, or reference modifier. Those are precisely the elements the
reconciler deliberately does *not* tag (`Reconciler.NeedsTag`, PR #468) — tagging
every leaf cost ~301 B/op. They are addressable here only because the source-map
stamp puts them back on the map via the extras bucket.

It is not the whole panel, and the distinction is worth keeping straight: the
`Border`s calling `.Background(Theme.CardBackground)` / `.Background(Theme.SubtleFill)`
carry `ThemeBindings` in `Element.Extensions`, and `NeedsTag` already tags anything
with a non-null extras bucket — so those are tagged with or without source mapping.
The leaves are the part that demonstrates the gap.

## Two flags, and why both exist

Source locations are compile-time constants, so there is a build gate and a
runtime gate:

| Gate | Set by | Effect |
|---|---|---|
| `ReactorSourceMap` (MSBuild) | `true` in Debug automatically | Generates the interceptors that stamp `Element.CallSite` |
| `ReactorSourceMap.Enabled` (runtime) | the devtools verb, or the in-app toggle | Whether a stamped call site actually writes the location |

This project sets **no** source-map property in its `.csproj`. The Debug default
in `build/Reactor.targets` does it, mirroring how WPF gates XAML source info
behind `XamlDebuggingInformation`. Build it `-c Release` and every location
reports `-`: no interceptors are generated and no source paths ship in the
binary.

A Debug build that never runs devtools pays nothing at runtime — the interceptor
checks the flag and returns the original element untouched.

## Known gaps (both route-independent)

- **Helper methods attribute to themselves.** A helper `MyHeader()` that calls
  `TextBlock(...)` reports the line inside `MyHeader`, because that *is* the call
  site being intercepted.
- **Bare-string children are not stamped.** `VStack("hi")` routes through the
  implicit `string` -> `Element` conversion, whose `TextBlock` call lives in the
  framework rather than your code, so those report no location rather than a
  misleading framework one.

