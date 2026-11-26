
# Visual Studio and VS Code extensions

Reactor has two editor extensions. Both are named **Reactor Preview**, and both
show your running app next to the code.

Each one starts the app with
`dotnet watch` and devtools turned on, so edits reach the preview as described
in [Hot reload and live preview](hot-reload.md). The Visual Studio extension
embeds the real app window. The VS Code extension shows screenshots of it.

Both extensions are experimental. The Visual Studio extension is the least
stable part of Reactor. Expect install problems, races while a solution loads,
window and DPI issues, and breaking changes. Use it to try the embedded preview
and to send feedback, not as your only way to run the app.

| | Visual Studio extension | VS Code extension |
|---|---|---|
| Editor | Visual Studio 2026 (version 18.x) | VS Code 1.85 or later |
| Preview | The real app window, embedded in a tool window. Mouse, keyboard, and focus work. | Screenshots of the app in a panel. It is not interactive. |
| Get it | `Reactor.VsExtension.vsix` from GitHub Releases, or `bootstrap.ps1` | Run it from source in `src/vscode-reactor` |
| Starts the app with | `dotnet watch run --project <csproj> -- --devtools run --embed` | `dotnet watch run --project <csproj> -- --devtools run --vscode` |
| Marketplace | Not listed | Not published |

## Visual Studio extension

The extension adds a **Reactor Preview** tool window. It finds the project of
the file you are editing and starts it under `dotnet watch`. Then it places the
app's window inside the tool window. You can click, type, open popups, and
inspect the accessibility tree, because it is the real WinUI window.

### Requirements

- Visual Studio 2026 (version 18.x): Community, Professional, or Enterprise, on
  x64 or Arm64. Visual Studio 2022 is not supported.
- The .NET 10 SDK on `PATH`.
- Visual Studio must not run as administrator. If it does, the tool window
  shows an error and does not start the app, because Windows can drop input
  between an elevated and a non-elevated process.
- The app you preview must use Windows App SDK 1.6 or later and declare
  PerMonitorV2 DPI awareness in its project file:

  ```xml
  <PropertyGroup>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  </PropertyGroup>
  ```

- Windows 11 23H2 or later is recommended.

### Install

Pick one of these:

<!-- tabs -->

#### From a release

Download `Reactor.VsExtension.vsix` from
[GitHub Releases](https://github.com/microsoft/microsoft-ui-reactor/releases),
close Visual Studio, and double-click the file. It installs for your user
account. The file may be unsigned, so Visual Studio can show a warning.

#### With bootstrap.ps1

In a source checkout, `bootstrap.ps1` builds the VSIX and installs it into the
newest Visual Studio 2026 that has the **Visual Studio extension development**
workload. If no such instance exists, it skips this step. Pass
`-SkipVsExtension` to skip it on purpose. Later, `mur upgrade` refreshes the
extension; see [mur CLI](mur-cli.md).

#### Build it yourself

This also needs the **Visual Studio extension development** workload:

```powershell
# Build the VSIX
pwsh -File src\vs-reactor\Build-Vsix.ps1
# The file is src\vs-reactor\Reactor.VsExtension\bin\Debug\Reactor.VsExtension.vsix

# Or build and install it in one step (close Visual Studio first)
pwsh -File src\vs-reactor\Reinstall-Vsix.ps1
```

If the restore cannot reach nuget.org, pass `-NuGetSource <url>` or
`-NuGetConfig <path>` to either script. A plain `dotnet build` of the
extension project makes a VSIX for CI checks only. It cannot be installed.

<!-- /tabs -->

To work on the extension itself, open `src\vs-reactor` in Visual Studio, set
`Reactor.VsExtension` as the startup project, and press F5. Visual Studio starts
a separate Experimental instance (`/RootSuffix Exp`) with the extension loaded.

### Open the preview

<!-- steps -->

1. **Load the solution.** Open a solution that contains a Reactor app, and
   wait until it has loaded.
2. **Open a component.** Open a `.cs` file that contains a component (a class
   that derives from `Component`).
3. **Show the tool window.** Choose **View > Other Windows > Reactor Preview**.
   The command is also on the **Tools** menu.

<!-- /steps -->

If the active file belongs to a loaded project, the preview starts right away.
If not, the tool window opens empty. Open a component file, then select
**Preview Active File** on the tool window's toolbar.

### Tool window

| Control | What it does |
|---|---|
| **Preview Active File** | Starts the preview for the project that contains the active file. Use it to switch to another project. |
| **Stop Preview** | Stops the app. |
| **Force Reload** (toolbar), **↻** (next to the picker) | Stops the app and starts it again. Use it when a build, a hot reload, or the start-up handshake gets stuck. |
| Component picker | Lists the components of the project. By default it follows the active editor and shows the first component in that file. When you pick a component yourself, it stays selected until you pick another one. |
| **Reactor Preview** output pane | Shows the `dotnet watch` output, the port and token handshake, and errors. Check it first when something goes wrong. |

The picker only follows files in the project that is running. A file from
another project does not switch the preview. Select **Preview Active File** to
start that project instead.

### Edits and rude edits

- Most edits, such as changes inside `Render`, apply in place through hot
  reload. The embedded window stays where it is.
- A rude edit, such as a change to a record's shape, makes `dotnet watch`
  rebuild and start the app again. The extension finds the new process and
  embeds it in the same place, usually within about 10 seconds.
- If the `dotnet watch` process exits on its own, the extension starts it again
  once. If it exits a second time, the tool window shows **Preview crashed**.

### Packaged apps

Packaged (MSIX) projects work without changes. Apps made from the Reactor
templates already reference `Microsoft.Windows.SDK.BuildTools.WinApp`, which
provides the run support the preview needs. The extension sets
`WinAppRunUseExecutionAlias=true` for the app it starts, so the app keeps its
console output and can report its port and token. A project that you converted
to MSIX by hand must add that package first. To preview the app unpackaged,
set `<WindowsPackageType>None</WindowsPackageType>`.

### Known limits

- Visual Studio and the app must use the same DPI awareness. On a mismatch, the
  extension retries once in owner mode: the app window floats above Visual
  Studio instead of inside the tool window. If that fails too, the tool window
  shows an error.
- There are no settings yet. A **Tools > Options** page is planned.
- The control reference graph is not shown in Visual Studio yet. Use the
  VS Code panel or the devtools `references` tool.

For more fixes, see the
[troubleshooting section of the extension README](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/vs-reactor/README.md#troubleshooting).
The design is in
[spec 056](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/056-visual-studio-embedded-preview.md).

## VS Code extension

The VS Code extension opens a **Reactor Preview** panel beside the editor. The
app runs in its own window. The panel shows screenshots of that window and
updates them while the app runs. It is not on the Visual Studio Marketplace.

### Install from source

You need Node.js with npm, and the .NET SDK on `PATH`.

<!-- steps -->

1. **Open the extension folder.** Open the `src/vscode-reactor` folder of a
   source checkout in VS Code.
2. **Install the build dependencies.**

   ```powershell
   npm install
   ```

3. **Start the extension.** Press F5 and pick **Run Extension**. VS Code
   compiles the extension and opens a second window, the Extension
   Development Host, with the extension loaded.
4. **Open your app.** In that window, open your Reactor app folder.

<!-- /steps -->

### Commands

Run these from the Command Palette:

| Command | What it does |
|---|---|
| **Reactor: Preview Component** | Starts the preview for the active C# file. It finds the nearest `.csproj` above the file and runs `dotnet watch run --project <csproj> -- --devtools run --vscode`. |
| **Reactor: Connect to Preview** | Connects the panel to an app you started yourself. It asks for the `CAPTURE_PORT` and `CAPTURE_TOKEN` values that the app prints when it runs with `--devtools run --vscode`. |
| **Reactor: Stop Preview** | Stops the app and closes the panel. |
| **Reactor: Focus Preview Window** | Brings the real app window to the front. The status bar item does the same. |

### The preview panel

- A component picker lists the components of the project. Pick one to switch.
  The app does not restart.
- When you switch to another C# file in the same project, the preview switches
  to the first component in that file. A file from another project starts that
  project.
- **Focus Window**, or a click on the image, brings the real window to the
  front so you can interact with it.
- **References** shows the control reference graph of the current tree, with
  cycles and unresolved references.
- The status text shows **Building...** while `dotnet watch` rebuilds and
  **Live** when frames arrive.
- The **Reactor Preview** output channel shows the app's output.

If the app uses an older Reactor package without devtools, the extension
starts it again with the old `--preview --vscode` flags.

## Tips

- Check the **Reactor Preview** output pane or channel first. It shows the
  `dotnet watch` output and the handshake.
- Restart the preview before you assume the app is broken. The extensions are
  rougher than the framework.
- For a preview with no editor, run `mur devtools` in the project folder. See
  [Devtools](devtools.md).
- Both extensions need a debug build with devtools available. See
  [Devtools](devtools.md) to set it up.

## Next Steps

- **[Hot reload and live preview](hot-reload.md)**: what survives an edit and what a rude edit is
- **[Devtools](devtools.md)**: the launch flags and the MCP server that both extensions use
- **[mur CLI](mur-cli.md)**: `mur devtools` and `mur upgrade`
- **[Developer tools](dev-tooling.md)**: overview of all Reactor tools
