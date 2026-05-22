# Spec 045 Phase 2 — Human Review Gate

Use this checklist during the §2.29 review pass. It reuses the §4.7 P1
script (items 1–8) plus the ten new P2 items.

**How to drive:**
1. Build the showcase:
   ```
   dotnet build samples/apps/dock-showcase/DockShowcase.csproj -c Debug -p:Platform=x64
   ```
2. Run it:
   ```
   samples/apps/dock-showcase/bin/x64/Debug/net10.0-windows10.0.22621.0/DockShowcase.exe
   ```
3. **NOTE:** the §2.19 unhooking removed the
   `REACTOR_DOCK_XAML=1` A/B flip — the native renderer is the only
   path in this build. The Phase-1 wrapper is unavailable for
   side-by-side comparison.

Mark each item ✅ / ❌ / ➖ (N/A or deferred). Sign at the bottom.

---

## Phase 1 script (re-run against P2)

- [ ] **Item 1** — Drag a center tab to each of 5 split targets
  (center, SplitL/T/R/B). Verify preview match, drop landing, Esc
  snap-back. *(Native path; native renderer is the showcase default.)*
- [ ] **Item 2** — Drag to each of 4 edge dock targets. Same
  checklist. *(Native path.)*
- [ ] **Item 3** — Drag a tab out of the title bar into open space;
  floating window appears at pointer; title bar matches adapter content.
  *(Native renderer opens the floating window via `ReactorApp.OpenWindow`;
  the custom title bar adapter routing is §2.6 follow-up, so the
  fallback title is the pane's Title or `DockingStrings.FloatingWindowDefaultTitle`.)*
- [ ] **Item 4** — Drag a floating tab back into a tab group; floating
  window auto-closes if last document. *(Cross-host re-dock is a
  follow-up; tear-out + open is the proven path.)*
- [ ] **Item 5** — Resize splits via splitter; min sizes respected;
  re-mount restores sizes. *(§2.1 splitter fix from session 2 covers
  this; M15/M17/M19 selftests are green.)*
- [ ] **Item 6** — Pin a tab to a side; click side icon; popup shows;
  resize popup; re-pin from popup; close from popup. *(§2.5 popup
  open/collapse works; light-dismiss + sizer are §2.5 follow-ups.)*
- [ ] **Item 7** — Save layout to JSON, quit, restart, load; layout
  matches. *(§2.7 v2 JSON; round-trip + invariant-culture covered by
  unit tests.)*
- [ ] **Item 8** — (negative) tear out a tab while resizing a different
  split; no crash. *(Drag session payload is in-memory only; no
  cross-state interaction.)*

## P2-specific items

- [ ] **Item 9** — Documents vs tool windows visual distinction matches
  intent. *(All-ToolWindow groups auto-flip to bottom-position +
  compact tabs via §2.8 default resolution; verify in showcase Scene A.)*
- [ ] **Item 10** — Per-pane content state survives save→quit→restart
  →load (e.g. editor scroll position). *(`Document<TState>` envelope
  + JSON round-trip; full `WindowPersistedScope` wiring is open per
  §2.9. Verify shape preservation; per-pane state typed envelope
  round-trip via composition.)*
- [ ] **Item 11** — `Ctrl+Tab` pane navigator opens, navigates,
  closes correctly. *(Deferred — needs the navigator overlay
  primitive. Ctrl+PageUp/PageDown navigation through tabs is wired
  and live (§2.10 chord set). Mark as N/A for P2 if the navigator
  itself isn't present.)*
- [ ] **Item 12** — Layout JSON v1 file (P1 build) loads correctly in
  P2 build. *(§2.11 migration ladder covers this; unit-tested in
  `LayoutMigrationTests`.)*
- [ ] **Item 13** — Drop preview latency feels equivalent (timed where
  reasonable; subjective otherwise). *(Hot-path `ComputePreviewBounds`
  is allocation-free per §2.20 perf budget tests; visual smoothness
  should match P1.)*
- [ ] **Item 14** — AOT-published binary runs the showcase end-to-end.
  *(Open — verify via `dotnet publish -p:PublishAot=true` of the
  showcase. The docking subsystem is AOT-clean by construction
  — `JsonSerializerContext` source-gen, no reflection — but the
  end-to-end AOT publish hasn't been driven this session.)*
- [ ] **Item 15** — Run under `de-DE` and `ar-SA` (RTL); titles
  localize; drop targets / context-menu items localize; layout mirrors;
  pointer hit-tests resolve in mirrored regions. *(§2.21 localization
  routing landed via `DockingStrings.Resolver`; apps wire their
  `IntlAccessor` to translate. §2.23 FlowDirection inheritance
  from ancestor handles the bulk; explicit drop-target glyph
  mirror + splitter direction inversion are §2.23 follow-ups.)*
- [ ] **Item 16** — Screen reader pass (Narrator/NVDA): pane roles
  announced; AutomationIds stable; focus never lost; drop-target
  navigation keyboard-only with arrow+Enter. *(§2.22: per-pane
  AutomationId = `pane:<key>` + `AutomationProperties.Name` from
  Title; host has `Custom` landmark + localized name. Live-region
  announcements are §2.22 follow-up.)*
- [ ] **Item 17** — Reduced-motion: transitions disappear; static
  positioning correct. *(No animation is wired today — reduced-motion
  is the default by omission. The animation pass adds the
  `UISettings.AnimationsEnabled` gate when transitions are added.)*
- [ ] **Item 18** — Corrupt layout recovery: hand-edit JSON to invalid;
  app starts with default; error event logged; no crash dialog.
  *(§2.7 / §2.25: corrupt JSON falls back via
  `DockLayoutSerializer.Load` returning `IsFallback=true` + a
  `ReactorEventSource.DockingLayoutLoadFallback` event with
  PII-safe category; selftest
  `NativeDocking_Reliability_CorruptLayoutFallback_HostMounted`
  is green.)*

## Sign-off

- **Reviewer:** _____________________________
- **Date:** _____________________________
- **PR:** _____________________________
- **Net result:** _____________________________
- **Followups filed (issue / PR):**
  - _____________________________
  - _____________________________

---

## Selftest baseline before review

Run before the visual pass to capture the regression-test baseline:

```
dotnet test tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64 \
  --filter "FullyQualifiedName~Docking" --no-build

"tests/Reactor.AppTests.Host/bin/x64/Debug/net10.0-windows10.0.22621.0/Reactor.AppTests.Host.exe" \
  --self-test --filter NativeDocking
```

Known flakes (per the spec-045-next-agent-prompt.md handoff):
- `NativeDocking_SplitterProgrammaticVisualDemo_TIMEOUT` — visual demo
  with intentional `Task.Delay`s.
- `M07_*` / `M08_*` / `M03_BodyBReachable` / `DocsByComposition_*` /
  `Reliability_Effect_BodyRendered` / `Sim_OverlayFound` — `FindText`/
  timing flakes that fire only in the full-filter run.

If any other selftest fails, treat as a regression and block sign-off.
