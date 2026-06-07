# Manual smoke checklist for Reactor VS Extension (Tier C.1)

**Must pass before every release.** Release engineers should check every item below on a release-candidate VSIX and attach the results to the release PR description.

## Setup

1. Build the full installable VSIX with desktop MSBuild:

   ```powershell
   pwsh -File src\vs-reactor\Build-Vsix.ps1
   ```

2. Install the VSIX into the Experimental hive:
   - Open `src\vs-reactor\Reactor.VsExtension\bin\Debug\Reactor.VsExtension.vsix`.
   - Choose **Visual Studio Community 2022 — Experimental Instance**.
   - Complete installation and restart the Experimental Instance if prompted.

3. Launch the Experimental hive:

   ```powershell
   devenv /RootSuffix Exp
   ```

## Checklist

- [ ] 1. Launch Exp hive. Tool window opens. Placeholder visible.
- [ ] 2. Open a Reactor sample project (`samples/apps/minesweeper`). Tool window auto-discovers it.
- [ ] 3. Status reaches "Live" within 15s. Mouse click registers.
- [ ] 4. Type into a TextBox. Backspace/arrow keys work.
- [ ] 5. Tab traversal between focusable controls works.
- [ ] 6. ComboBox opens a popup; the popup may extend outside the placeholder rect.
- [ ] 7. Switch component via the dropdown. Auto-track active file follows.
- [ ] 8. Edit a Render body, save → embedded UI refreshes in place (L1 hot reload).
- [ ] 9. Edit a record/type shape, save → child respawns within ~10s, placeholder HWND survives (L2 respawn).
- [ ] 10. Click ↻ → status returns to "Live" within ~10s.
- [ ] 11. Float the tool window. Drag across monitors at different DPIs. No blur, no input drop.
- [ ] 12. Force-close VS (Task Manager). Within 1s, dotnet watch + child are gone (verify in Task Manager).
- [ ] 13. UIA: open inspect.exe, walk to the tool window → the WinUI tree is visible. Narrator can hop between focusable elements.

## Known gaps from Block 0b residual matrix

- **#19 elevation mismatch** — Phase 1 now refuses embedded preview when Visual Studio is elevated and shows an actionable error. Verify the message during smoke if elevation handling changed.
- **#20 ARM64 native** — still depends on Reactor shipping an ARM64 native package. Phase 1 VSIX packaging targets amd64; ARM64-native validation remains deferred until an ARM64 nupkg is available.

## Tier B SDK test status

Tier B is partially running under `dotnet test src\vs-reactor\Tests\Reactor.VsExtension.SdkTests\Reactor.VsExtension.SdkTests.csproj`: 3 pass, 3 are intentionally skipped.

Running coverage for Tier A with `--collect:"XPlat Code Coverage"` is not currently available in this project; `dotnet test` reports `Unable to find a datacollector with friendly name 'XPlat Code Coverage'`.

Current Tier B gaps:

- `Commands_DispatchedThroughOleCommandTarget` is skipped because `Microsoft.VisualStudio.SDK.TestFramework` does not surface `IMenuCommandService` through `AsyncPackage.GetServiceAsync` under `dotnet test`.
- `Package_FindsToolWindowAsync_ReturnsInstance` is skipped because the lightweight SDK harness does not provide enough `IVsUIShell` frame services for `AsyncPackage.FindToolWindowAsync`.
- `Package_OnSolutionClose_DisposesLauncher` is skipped because it requires a real `IVsSolution` event source; Tier C.1 covers the user-visible close/cleanup behavior.

Tier A discovery currently finds 64 tests in `src\vs-reactor\Tests\Reactor.VsExtension.Tests` (63 passing, 1 environment-gated elevation skip on this machine).

## Reporting results

Attach the completed checklist, machine details, VS version, Windows version, WinAppSDK/Reactor package version, and any screenshots/logs to the release PR description.
