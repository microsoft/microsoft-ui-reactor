
# Visual Studio Embedded Preview

> ⚠️ **Very rough / experimental.** This is the roughest surface in an already experimental Reactor repository. It is meant for early feedback on the embedded-preview direction, not for reliable day-to-day development yet. Expect install friction, solution-load races, HWND/DPI edge cases, blank-window bugs, and breaking changes while this area is hardened.

The Visual Studio Embedded Preview hosts a live Microsoft.UI.Reactor (Reactor) component inside a Visual Studio 2022 tool window. Unlike the VS Code preview, which streams screenshots, the Visual Studio extension embeds the real WinUI surface so mouse, keyboard, focus, popups, and accessibility tree inspection work against the running app.

## When to use it

Use the Visual Studio extension when you are building a Reactor desktop app in Visual Studio and want an interactive preview docked next to the editor. Use the VS Code extension when you need a cross-editor thumbnail stream or are working outside Visual Studio; it remains the portable preview option, but it is not interactive.

## Install

Build and install the VSIX using the developer instructions in the [VS extension README](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/vs-reactor/README.md). For local validation, install into the Experimental hive first, then open a Reactor sample or app project and show the **Reactor Preview** tool window.

Do not treat the VSIX as a polished product install. It is currently a developer-smoke artifact for experimentation and feedback.

## Tool window basics

Open the Reactor Preview tool window from Visual Studio after loading a Reactor project. The extension discovers Reactor component classes in the active project, starts the target under `dotnet watch run -- --devtools run --embed`, then reparents the child WinUI HWND into the tool window placeholder.

The component picker is a ComboBox in the tool window chrome. By default it auto-tracks the active editor file and selects the first component found in that file. Choosing a component manually pins that selection until you switch again. The ↻ button force-reloads the child process when a build, hot reload, or handshake gets stuck.

## Hot reload behavior

- **L1 hot reload** — edits inside a `Render` body usually apply in place. The embedded UI refreshes without recreating the placeholder HWND.
- **L2 respawn** — rude edits such as record/type shape changes cause `dotnet watch` to rebuild and restart the child. The extension detects the new port/token pair and re-embeds the new child into the same placeholder, normally within about 10 seconds.

## Known limitations

- WinAppSDK 1.6 or newer is required by the target Reactor app.
- Windows 11 23H2 or newer is recommended for the most reliable cross-process WinUI HWND hosting.
- The Visual Studio tool window and embedded Reactor surface must agree on DPI awareness; mismatch is refused with an actionable error.
- Visual Studio and the Reactor app must run with the same elevation. In Phase 1, elevated Visual Studio is blocked because UIPI can silently drop input.
- ARM64-native validation requires Reactor to ship an ARM64 nupkg; Phase 1 packaging is x64/amd64-oriented.
- This feature intentionally has a lower maturity bar than the rest of the guide. The protocol, install flow, tool-window lifecycle, and solution-load behavior may change without compatibility guarantees.

## Troubleshooting

See the [VSIX README troubleshooting section](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/vs-reactor/README.md#troubleshooting).

## Phase 2 roadmap

Planned follow-ups include a Tools → Options page, richer solution-load behavior, optional marketplace distribution, expanded ARM64/signing validation, and enough soak time to decide whether the VSIX should graduate out of "rough experiment" status.
