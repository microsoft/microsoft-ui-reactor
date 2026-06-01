# Reactor Figma Sync Plugin

A Figma plugin that bridges designs to Reactor apps. Select a frame in Figma,
click **Generate**, and paste the copied command into a Copilot CLI terminal —
the agent scaffolds a new Reactor project (or updates an existing one) that
reproduces the design pixel-for-pixel.

## Prerequisites

| Requirement | Notes |
|---|---|
| [Figma desktop app](https://www.figma.com/downloads/) | Plugin runs inside Figma |
| [Node.js ≥ 18](https://nodejs.org/) | Build the plugin TypeScript |
| [.NET 10 SDK](https://dotnet.microsoft.com/) | Run `mur` CLI and Reactor apps |
| Figma personal access token | For `mur figma watch` live sync |
| [Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli) | Runs the generated prompt |
| Figma MCP server (`figma-developer-mcp`) | Agent reads design data via MCP |

### Figma API Token

Create a personal access token at
[Figma → Settings → Personal access tokens](https://help.figma.com/hc/en-us/articles/8085703771159).

The token is resolved from (checked in order):

1. `FIGMA_API_KEY` environment variable
2. `~/.copilot/mcp-config.json` — `--figma-api-key` in the Figma MCP server args
3. `.vscode/mcp.json` — `--figma-api-key` in the Figma MCP server args

## Building the Plugin

```bash
cd tools/figma-plugin
npm install
npm run build          # compiles code.ts → dist/code.js, copies ui.html → dist/ui.html
```

The build produces two files in `dist/`:

- `code.js` — plugin sandbox (main thread)
- `ui.html` — plugin UI panel

## Installing in Figma

1. Open the Figma desktop app
2. Go to **Plugins → Development → Import plugin from manifest…**
3. Select `tools/figma-plugin/manifest.json`
4. The plugin appears under **Plugins → Development → reactor-figma-sync**

> **Tip:** During development, use `npm run watch` to recompile on save. Reload
> the plugin in Figma with **Plugins → Development → reactor-figma-sync** (or
> <kbd>Ctrl+Alt+P</kbd> and search for it).

## Using the Plugin

### 1. Select a Frame

Open a Figma file and select a frame, component, component set, or section.
The plugin panel shows the frame name, dimensions, and node ID.

### 2. Set the Output Mode

| Mode | Description |
|---|---|
| **New** (default) | Scaffolds a new Reactor project via `mur --create` |
| **Existing** | Updates an existing project — enter the `.csproj` path |

### 3. Generate

Click **Generate**. The plugin copies a `copilot` CLI command to your clipboard
that contains a fully-formed prompt with:

- The Figma URL (file key + node ID)
- Instructions to read `skills/figma.md` for control mapping rules
- Instructions to read `skills/design.md` for WinUI best practices
- A live-sync watch loop using `mur figma watch`

Paste it into a terminal with Copilot CLI installed:

```
copilot --yolo -p '<generated prompt>'
```

The agent will:

1. Scaffold the project (or open the existing one)
2. Fetch the Figma design via the Figma MCP server
3. Translate the design into Reactor components
4. Launch the app
5. Start `mur figma watch` to poll for design changes

### 4. Live Sync

After the initial generation, the agent enters a watch loop:

```
mur figma watch "<figma-url>" --interval 10
```

This polls the Figma REST API every N seconds. When the designer saves changes,
the watch emits a JSON event to stdout:

```json
{"event":"changed","fileKey":"abc123","nodeId":"29792:125378","fileName":"My Design","lastModified":"2026-05-27T10:30:00Z"}
```

The agent detects the event, re-fetches the design via MCP, diffs it against the
current code, and applies only the changes needed — then restarts the app.

### 5. File URL Fallback

If the plugin can't resolve the Figma file key automatically (this happens when
running as a community/development plugin without private API access), a
**File URL** input appears. Paste any URL from the current Figma file and the
plugin extracts the file key from it.

## How It Works

```
┌─────────────────────────────────────────────────┐
│  Figma Plugin (tools/figma-plugin)              │
│                                                 │
│  code.ts   — reads selected frame metadata      │
│  ui.html   — builds CLI prompt, copies to       │
│               clipboard                         │
└──────────────────────┬──────────────────────────┘
                       │  paste in terminal
                       ▼
┌─────────────────────────────────────────────────┐
│  Copilot CLI (copilot --yolo)                   │
│                                                 │
│  1. Scaffolds Reactor project (mur --create)    │
│  2. Reads skills/figma.md + skills/design.md    │
│  3. Fetches design via Figma MCP server         │
│  4. Generates Reactor C# components             │
│  5. Launches app (dotnet run)                   │
│  6. Starts watch (mur figma watch)              │
│  7. Loops: detect change → diff → patch → run   │
└──────────────────────┬──────────────────────────┘
                       │
          ┌────────────┴────────────┐
          ▼                         ▼
┌──────────────────┐    ┌──────────────────────┐
│  mur figma watch │    │  Figma MCP Server    │
│  (polling)       │    │  (figma-developer-   │
│                  │    │   mcp)               │
│  Polls Figma     │    │  Fetches full design │
│  lastModified    │    │  tree for the agent  │
│  timestamp       │    │  to translate        │
└──────────────────┘    └──────────────────────┘
```

- **No bridge server or open ports.** The plugin generates a CLI command — there
  is no WebSocket or HTTP connection between Figma and the local machine.
- **No Figma plugin required for watch.** `mur figma watch` works standalone
  with just an API token. The plugin is a convenience for generating the initial
  prompt.

## Project Structure

```
tools/figma-plugin/
├── manifest.json      Figma plugin manifest
├── package.json       npm project (build scripts, dependencies)
├── tsconfig.json      TypeScript config
├── build-ui.mjs       Copies ui.html to dist/
├── src/
│   ├── code.ts        Plugin main thread (sandbox) — frame selection logic
│   └── ui.html        Plugin UI — project config, prompt generation
├── dist/              Build output (gitignored except committed artifacts)
│   ├── code.js
│   └── ui.html
└── node_modules/      Dependencies (@figma/plugin-typings, typescript)
```

## Troubleshooting

| Problem | Solution |
|---|---|
| "Nothing selected" | Select exactly one frame, component, or section |
| Generate button disabled | A frame must be selected first |
| File URL input appears | Paste any URL from the Figma file to provide the file key |
| `mur figma watch` says "API key not found" | Set `FIGMA_API_KEY` env var or configure it in MCP config |
| `mur figma watch` gets 429 (rate limited) | Increase `--interval` (default 10s); the command auto-retries with backoff |
| `mur figma watch` gets 403 | Regenerate your Figma personal access token |
