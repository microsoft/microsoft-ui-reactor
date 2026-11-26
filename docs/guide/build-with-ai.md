
# Build with AI

Reactor is a new framework, so AI models know little about it. Without help,
an agent tends to invent API names or fall back to XAML, data binding, and
view models. Reactor gives agents four kinds of help:

- **Knowledge:** an agent plugin and a skill kit that teach the DSL, the hooks
  rules, and the tools.
- **Build feedback:** `mur check` prints each diagnostic on one line with a
  fix hint.
- **Eyes on the app:** the WinUI DevTools in the winapp CLI let an agent see
  and drive the running app.
- **Docs for AI tools:** the docs site publishes `llms.txt` and a Markdown
  copy of each page.

## The tools at a glance

| Tool | What it gives the agent | Where to get it |
|---|---|---|
| Agent plugin | The `reactor-dev` agent and 16 skills | `plugins/reactor` in the repository, or `agentkit/plugins/reactor/` in the `Microsoft.UI.Reactor` package |
| Skill kit | `SKILL.md`, the topic files in `skills/`, and `mur` | The `reactor-skill-kit-<version>.zip` release asset |
| `mur check` | Build output with fix hints | The [mur CLI](mur-cli.md) |
| WinUI DevTools | A live view of the running app, and input | The winapp CLI and its `winui-devtools` skill; see [Devtools](devtools.md) |
| Docs for AI tools | `llms.txt`, `llms-full.txt`, and a Markdown copy of each page | The docs site |

## The agent plugin

The plugin is in
[`plugins/reactor`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/plugins/reactor).
It has a manifest for GitHub Copilot CLI (`plugin.json`) and one for Claude
Code (`.claude-plugin/plugin.json`). It contains:

- **The `reactor-dev` agent.** It starts every new app with
  `dotnet new reactor`, drafts the component tree, writes the files, and then
  runs `mur check`.
- **16 skills.** The agent loads `reactor-getting-started` for most tasks. It
  loads a topic skill only when the task needs it, which keeps the context
  small.

| Skill | Use it for |
|---|---|
| `reactor-getting-started` | The mental model, hooks, common factories, gotchas, and project setup. Enough for most tasks. |
| `reactor-dsl` | The full index of public signatures, `reactor.api.txt`. |
| `reactor-build-and-check` | Building with `mur check` and fixing common diagnostics. |
| `reactor-devtools` | Translating an old `mur devtools` command into its winapp equivalent. |
| `reactor-recipes` | Single-file recipes to copy and adjust. |
| `reactor-design` | Windows 11 design rules: theme tokens, typography, accessibility. |
| `reactor-async` | `UseResource`, `UseMutation`, and `UseInfiniteResource`. |
| `reactor-forms` | `UseValidationContext`, validators, `FormField`, and masked input. |
| `reactor-navigation` | `UseNavigation`, `NavigationHost`, `NavigationView`, and `TabView`. |
| `reactor-input` | Gestures, pointer events, drag and drop, and focus. |
| `reactor-commanding` | `Command`, `UseCommand`, and keyboard shortcuts. |
| `reactor-charts` | Charts and data visualization. |
| `reactor-windowing` | Top-level windows, placement, and file pickers. |
| `reactor-docking` | `DockManager` and dockable tool windows. |
| `reactor-wrapping` | Wrapping any WinUI control with `[GenerateReactorWrapper]`. |
| `reactor-advanced` | The Win2D canvases in `Microsoft.UI.Reactor.Advanced`. |

Install the plugin in your agent:

<!-- tabs -->

### GitHub Copilot CLI

Copilot CLI can install a plugin from a folder in a GitHub repository:

```shell
copilot plugin install microsoft/microsoft-ui-reactor:plugins/reactor
```

Run `copilot plugin list` to check that it is installed. To load the plugin
from a local folder instead, start Copilot CLI with `--plugin-dir`:

```shell
copilot --plugin-dir <repo>\plugins\reactor
```

### Claude Code

Pass the plugin folder with `--plugin-dir`. Claude Code loads it for that
session:

```shell
claude --plugin-dir <repo>\plugins\reactor
```

In a clone of the repository, `bootstrap.ps1` also places the plugin at
`~/.claude/plugins/reactor`. It uses a symbolic link when it can, so edits in
the clone show up at once. Otherwise it copies the folder. `mur upgrade`
refreshes the copy, and `mur doctor` reports whether the plugin is there. Pass
`-SkipPlugin` to `bootstrap.ps1` to skip this step.

### From the NuGet package

You don't need a clone. The `Microsoft.UI.Reactor` package carries the plugin.
After a restore, it is in the NuGet cache:

```text
%USERPROFILE%\.nuget\packages\microsoft.ui.reactor\<version>\agentkit\plugins\reactor
```

Pass that folder to `--plugin-dir`. The plugin then matches the Reactor
version your app uses.

<!-- /tabs -->

### Other agents

The repository has no install steps for other agents. Point the agent at the
`plugins/reactor` folder, or at the single-file skill described in the next
section.

## The skill kit

The skill kit is for agents that can't load plugins. It contains:

- `SKILL.md`, one file with the Reactor essentials;
- `skills/`, topic files for design, async data, forms, input, and more,
  plus `reactor.api.txt`, the index of every public signature;
- `mur` builds for x64 and Arm64.

<!-- steps -->

1. **Download the kit.** Get `reactor-skill-kit-<version>.zip` from the
   [releases page](https://github.com/microsoft/microsoft-ui-reactor/releases)
   and extract it.

2. **Run the install script.** Run it from the extracted folder:

   ```powershell
   cd reactor
   .\install-skill-kit.ps1
   ```

   The script copies the kit to `~/.claude/skills/reactor` and adds the
   matching `bin\<arch>` folder to your user `PATH`. Pass `-Path <folder>` to
   install somewhere else, or `-SkipPath` to leave `PATH` alone. `mur` needs
   the .NET 10 runtime. The script warns you if it is missing.

<!-- /steps -->

The same files ship in the `Microsoft.UI.Reactor` package under `agentkit/`.
If `mur` is on your `PATH`, these commands print them:

```shell
mur --skill
mur --api
```

## Build feedback with mur check

`mur check` runs `dotnet build` and returns the same exit code. It prints each
diagnostic on one line. For some `REACTOR_*` rules, it adds the skill to read
and a `→ try:` line with a fix to use as-is. That makes it a better build
command for an agent than raw MSBuild output.

```shell
mur check MyApp.csproj
mur check --final MyApp.csproj
```

The default mode hides low-value warnings while the agent iterates. Run
`--final` before you finish, to see every warning. See
[Analyzers](analyzers.md#mur-check) for how the hints map to the analyzer
rules, and the [mur CLI](mur-cli.md#check-your-code) page for every option.

## Let the agent see the app

The WinUI DevTools in the [winapp CLI](https://github.com/microsoft/winappCli)
let an agent read the component tree and hook state, take screenshots, and
click, type, and scroll. They attach from outside the app, so the project needs
nothing extra:

```shell
winapp run . --devtools on --detach --json
winapp devtools inspect --json
winapp ui screenshot
winapp ui click "<Editor>/Button" --json
```

The `winui-devtools` skill in winapp's agent plugin teaches these commands. See
[Devtools](devtools.md) for the selectors and the full list.

Reactor no longer has an MCP server. The one that ran inside the app, behind
`mur devtools`, has been removed.

## Docs for AI tools

The [docs site](https://microsoft.github.io/microsoft-ui-reactor/) is built
with Blume, which writes these files for each docs version:

| Path | Contents |
|---|---|
| `llms.txt` | A list of every page, with a link and a one-line summary |
| `llms-full.txt` | The full text of every page in one file |
| `<page URL>.md` | A Markdown copy of one page. Add `.md` to the page URL. |
| `skill.md` | A short skill that tells an agent to read these docs |
| `.well-known/agent-skills/index.json` | An index that points agents at that skill |

The paths are relative to the root of a docs version, for example
`https://microsoft.github.io/microsoft-ui-reactor/latest/` for the latest
release or `https://microsoft.github.io/microsoft-ui-reactor/main/` for the
main branch. Give an agent `llms.txt` when it should find the right page
itself. Give it one page's `.md` copy when you know which page it needs.

## Good prompts

The skills teach a set of habits. A prompt that asks for them gets better
results:

- **Start from the template.** Ask for `dotnet new reactor`. The agent skill
  warns that hand-written project files lead to invented API names.
- **Ask for `mur check` after each batch of edits,** and for every `REACTOR_*`
  warning to be fixed. Ask for `mur check --final` at the end.
- **Ask the agent to look at the app.** A screenshot and the component tree
  from the WinUI DevTools show layout and contrast bugs that a build can't.
- **Name the feature.** Words like "form validation", "navigation", or
  "docking" match a topic skill, so the agent loads the right one.

<!-- prompt -->
```text
Create a Reactor app named Notes with `dotnet new reactor`. Show a list of
notes and a form to add one, with validation. Run `mur check` after each batch
of edits and fix every REACTOR_* warning.
```

<!-- prompt -->
```text
The Save button is cut off. Start the app with `winapp run . --devtools on`,
take a screenshot, read the component tree, and fix the layout.
```

## Next Steps

- **[mur CLI](mur-cli.md)**: install `mur` and see every command
- **[Devtools](devtools.md)**: the WinUI DevTools commands and selectors
- **[Analyzers](analyzers.md)**: every `REACTOR_*` rule that `mur check` reports
- **[Rules of Reactor](rules-of-reactor.md)**: the rules behind the most common
  diagnostics
- **[Developer tools](dev-tooling.md)**: an overview of every Reactor tool
