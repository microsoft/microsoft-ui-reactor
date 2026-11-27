
# mur CLI

`mur` is the Reactor command-line tool. It checks your code with fix hints,
starts and drives devtools sessions, manages localization files, prints the
API index for AI agents, and keeps a source checkout of Reactor up to date.
Run `mur --help` to see the list of commands, and `mur --version` to see
which version you have.

## Install mur

`mur` is not published to NuGet.org. Get it in one of two ways.

**From a release.** Download `reactor-skill-kit-<version>.zip` from the
[GitHub releases](https://github.com/microsoft/microsoft-ui-reactor/releases).
It contains the agent skills and `mur` builds for x64 and Arm64. The installer
replaces the entire destination directory if it already exists. Back up local
edits before reinstalling, or pass `-Path <new-folder>` to use a fresh
destination. Before running the script, make sure `dotnet.exe` is on PATH
and the .NET 10 runtime is installed (`winget install Microsoft.DotNet.Runtime.10`).
Extract the ZIP and run the installer.

```powershell
cd reactor
.\install-skill-kit.ps1
```

The default destination is `~/.claude/skills/reactor`. The script adds the
`bin\<arch>` folder to your user `PATH`. Pass `-SkipPath` to leave `PATH` alone.
To verify the kit, open its installed `bin\<arch>` folder and run
`.\mur.exe --version`. If you let the installer configure PATH, open a new
terminal and check that `Get-Command mur` points to the same folder.

**From source.** In a clone of the repository, run `./bootstrap.ps1`. It packs
the CLI into `local-nupkgs/` and installs it as a .NET global tool
(`Microsoft.UI.Reactor.Cli`). Pass `-SkipMurInstall` to pack it without
installing it. If the release kit's `mur` is already on PATH, see the
[bootstrap steps for a source clone](https://github.com/microsoft/microsoft-ui-reactor/blob/main/SKILL.md#bootstrap-selfhost-fresh-clone)
to give the source CLI precedence. The manual steps are in
[CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md#manual-setup).

## Create a project

For most apps, use a project template, not `mur`:

```powershell
winapp new -t reactor -n MyApp
```

[Installation](install.md) covers the templates and the `dotnet new` commands.

`mur --create <Name>` is for work inside a clone of the Reactor repository. It
writes a folder with `Program.cs`, a `.csproj`, a `.sln`, and
`Assets/AppIcon.ico`. The project references `..\src\Reactor\Reactor.csproj`,
so create it in the root of the clone. The name must start with a letter or an
underscore, and can contain letters, digits, underscores, and dots.

`mur templates status` tells you if `dotnet new reactor` works on this
machine:

| Exit code | Meaning |
|---|---|
| 0 | The Reactor templates are installed. |
| 1 | `mur` could not list the `dotnet new` templates. |
| 2 | The template pack is installed, but it has no Reactor templates. |
| 3 | The template pack is not installed. Run `winapp new` once to install it. |

## Check your code

`mur check` builds the project and prints one line for each diagnostic, with a
hint on how to fix it:

```text
mur check [<path>] [options] [-- <dotnet build arguments>]
```

| Option | What it does |
|---|---|
| `<path>` | The project, solution, or folder to build. The default is the current folder. |
| `--strict` | Rank every warning as an error, so all warnings print. It does not change the exit code. |
| `--final` | Print every diagnostic, with nothing hidden. Use it before you merge. |
| `--quiet` | Print errors only. |
| `--suggest-threshold <N>` | Change how close a name must be to get a "did you mean" suggestion. `0` turns suggestions off. |
| `--emit-threshold <N>` | Change the score a warning needs before it prints in the default mode. |
| `--disable-rule <Id>` | Turn off one hint rule. You can repeat it. |
| `--list-rules` | List the hint rules and exit. |
| `--trace <file>` | Write a trace of what `mur check` did to a file. |

Without a mode option, `mur check` prints errors and the warnings most likely
to matter, and hides info messages. It passes `--nologo`, `-v:m`, and the
platform of your machine to `dotnet build`, unless you pass your own after
`--`. The exit code is the exit code of the build. To fail on warnings in
CI, pass `-- -p:TreatWarningsAsErrors=true`.

[Analyzers](analyzers.md#mur-check) explains how `mur check` relates to the
`REACTOR_*` analyzers. The
[rule reference](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/reference/mur-check-did-you-mean.md)
lists every hint.

## Drive a running app

`mur devtools` builds your app, starts it with the devtools MCP server, and
restarts it after a reload. In a second terminal, commands such as
`mur devtools tree`, `mur devtools screenshot --out shot.png`, and
`mur devtools click "#id"` act on the running app.

| Command | What it does |
|---|---|
| `mur devtools [project.csproj]` | Build and start the app with devtools. |
| `mur devtools --component <Name>` | Open one component instead of the app's root. |
| `mur devtools --mcp-port <N>` | Use a fixed port. |
| `mur devtools --print-config` | Print MCP client config for Claude Code, VS Code, and GitHub Copilot. |
| `mur devtools session list` | List the running sessions. |
| `mur devtools session clean` | Remove lock files left by sessions that are no longer running. Add `--dry-run` to only list them. |
| `mur devtools <verb> ...` | Run one devtools command against the running app. |

[Devtools](devtools.md#mcp-server) lists every verb, the selectors, and the
exit codes.

## Localize

The `mur loc` commands work with `.resw` resource files. Run them from the
project folder.

| Command | What it does | Main options |
|---|---|---|
| `mur loc extract` | Find text in your source code that should be localized, and write it to `.resw` files. | `--source <dir>` (default `.`), `--output <dir>` (default `Strings/en-US/`), `--rewrite` to change the source to use the new keys, `--dry-run` |
| `mur loc translate` | Translate the `.resw` files with AI. | `--target <locales>` (required, for example `fr-FR,ar-SA`), `--source <dir>` (default `Strings/en-US/`), `--missing-only`, `--model <name>` |
| `mur loc validate` | Check message syntax, and check that each translation uses the same parameters as the source. | `--resources <dir>` (default `Strings/`), `--default-locale <locale>` (default `en-US`) |
| `mur loc status` | Show how much of each locale is translated. | `--resources <dir>`, `--default-locale <locale>` |
| `mur loc prune` | Find keys that the code no longer uses, and remove them. | `--source <dir>`, `--resources <dir>` (default `Strings/en-US/`), `--dry-run` to only list them |

`mur loc translate` uses the GitHub Copilot SDK, so it needs a GitHub Copilot
subscription and a signed-in `gh` CLI. The default model is `gpt-5.4-mini`.
You can also set it with the `COPILOT_MODEL` environment variable. Each
translation is marked as an AI draft that needs review. See
[Localization](localization.md) for the full workflow.

## Help for AI agents

| Command | What it does |
|---|---|
| `mur --skill` | Print the Reactor `SKILL.md` reference. |
| `mur --api` | Print `reactor.api.txt`, a list of every public Reactor signature. |
| `mur --regen-api` | Rebuild the API index from the source. Only works in a clone of the repository. |

If you don't have `mur`, the NuGet package also contains the API index, at
`%USERPROFILE%\.nuget\packages\microsoft.ui.reactor\<version>\agentkit\reactor.api.txt`.
[Build with AI](build-with-ai.md) explains how agents use these files.

## Work on Reactor itself

These commands are for a clone of the Reactor repository. Most of them stop
with an error when you run them anywhere else.

| Command | What it does |
|---|---|
| `mur pack-local` | Pack the framework from your working tree into `<repo>/local-nupkgs/`. Options: `--version` (default `0.0.0-local`), `--configuration` (default `Debug`). |
| `mur clean-local` | Remove the local packages, their NuGet cache entries, and old local templates. |
| `mur upgrade` | After `git pull`: pack the framework again, install the template pack if it is missing, and refresh the agent plugin and the Visual Studio extension. Options: `--skip-plugin`, `--skip-vs-extension`. |
| `mur doctor` | Check the .NET SDK, `mur`, the local packages, the template pack, and the agent plugin. Add `--verbose` for more detail. |

`mur upgrade` does not update `mur` itself, because a running program can't
replace its own file. Run `./bootstrap.ps1` again for that.

The docs build is not part of `mur`. `mur docs` prints the new command and
exits. See the
[doc pipeline guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/contributing/doc-pipeline.md).

## Design files

`mur figma watch <figma-url>` checks a Figma file for changes, every 10 seconds
by default (`--interval <seconds>`). When the file changes, it prints one line
of JSON. An agent can read that line and fetch the new design. It needs a Figma
API key. `mur` reads it from the `FIGMA_API_KEY` environment variable first.
If that is not set, it looks for an MCP server entry that sets
`FIGMA_API_KEY` in `~/.copilot/mcp-config.json`, and then in
`.vscode/mcp.json`.

## Next Steps

- **[Developer tools](dev-tooling.md)**: how `mur` fits into the inner loop
- **[Devtools](devtools.md)**: the in-app dev menu and the MCP server behind `mur devtools`
- **[Analyzers](analyzers.md)**: the rules behind `mur check`
- **[Localization](localization.md)**: resource files and `mur loc`
- **[Build with AI](build-with-ai.md)**: the skill kit and agent plugin
