
# Developer tools

Reactor comes with tools for the inner dev loop: the short cycle where you
change code, see the result, and fix what is wrong. This page walks that loop
one stage at a time. Each stage links to a page with the full details.

## The tools at a glance

| Tool | What it does | Where you get it | Details |
|---|---|---|---|
| Project templates | Create a new Reactor app | Windows App SDK template pack, through `winapp new` or `dotnet new` | [Install Reactor](install.md) |
| `mur` CLI | Create, check, inspect, and localize from the terminal | .NET tool `Microsoft.UI.Reactor.Cli`, or the skill kit zip | [mur CLI](mur-cli.md) |
| Hot reload | Apply code edits to a running app and keep its state | Built into `Microsoft.UI.Reactor`; run with `dotnet watch` | [Hot reload](hot-reload.md) |
| WinUI DevTools | Inspect, edit, and drive a running app from the terminal or an agent | The winapp CLI: `winapp run . --devtools on` | [Devtools](devtools.md) |
| Visual Studio extension | Live preview inside Visual Studio 2026 | `Reactor.VsExtension.vsix` on the GitHub release | [Extensions](vs-extension.md) |
| VS Code extension | Live preview panel in VS Code | Run from source in `src/vscode-reactor` | [Extensions](vs-extension.md) |
| Analyzers | Report Reactor mistakes in the editor and the build, with code fixes | Inside the `Microsoft.UI.Reactor` package | [Analyzers](analyzers.md) |
| `mur check` | Build the project and add a fix hint to each diagnostic | Part of the `mur` CLI | [mur check](analyzers.md#mur-check) |
| Agent plugin and skill kit | Teach AI coding agents the Reactor DSL and tools | `plugins/reactor` in the repo, or the skill kit zip | [Build with AI](build-with-ai.md) |
| Diagnostics | Release-safe events, traces, and logs | Built into `Microsoft.UI.Reactor` | [Diagnostics](diagnostics.md) |

![The Reactor inner dev loop. 1 Create, with project templates and mur --create. 2 Edit live, with dotnet watch, hot reload, and live preview. 3 Inspect, with the WinUI DevTools in the winapp CLI. 4 Catch mistakes early, with analyzers and mur check. 5 Debug and measure, with diagnostics. An arrow labeled next change goes back to Edit live. In the center, Work with AI agents has dashed arrows to each stage.](images/dev-tooling/inner-loop.svg)

## Create a project

Start from a project template. `winapp new -t reactor -n MyApp` creates a
packaged app with the `Microsoft.UI.Reactor` package already referenced.
[Install Reactor](install.md) lists every template and the `dotnet new`
commands.

If you work inside a clone of the Reactor repository, `mur --create MyApp`
writes a minimal project (`Program.cs`, a `.csproj`, a `.sln`, and an app
icon) that references the Reactor source instead of the package. See
[mur CLI](mur-cli.md#create-a-project).

To try an idea quickly, you do not need a component class. `ReactorApp.Run`
also takes an inline function component:

```csharp
// Alternative: inline function component, no class needed
// ReactorApp.Run("Quick Test", ctx =>
// {
//     var (n, setN) = ctx.UseState(0);
//     return VStack(12,
//         TextBlock($"Count: {n}").FontSize(20),
//         Button("+1", () => setN(n + 1))
//     ).Padding(24);
// });
```

## Edit live

Run the app under `dotnet watch`. When you save a file, .NET hot reload
applies the edit to the running process and Reactor re-renders every
component. Hook state stays: a counter keeps its value while you change the
layout around it. If an edit can't be applied in place, `dotnet watch`
restarts the app.

The Visual Studio extension and the VS Code extension run the same loop and
show the live app next to your code.

- [Hot reload and live preview](hot-reload.md): what survives a reload, rude
  edits, and limits.
- [Visual Studio and VS Code extensions](vs-extension.md): install and use the
  live preview.

## Inspect the running app

The WinUI DevTools in the [winapp CLI](https://github.com/microsoft/winappCli)
let you look inside a running Debug build. They attach from outside the app, so
you don't add anything to your project:

```
winapp run . --devtools on
```

`winapp devtools inspect` shows the component tree with the line of C# that
created each element. Other `winapp devtools` commands read and change
properties, resources, and hook state, and `winapp ui` clicks, types, and takes
screenshots.

For your own debug switches, such as a debug overlay or a "clear cache" command,
build an ordinary menu that only Debug builds compile:

```csharp
static class DevMenu
{
    // Named Use* because it consumes a hook slot: REACTOR_HOOKS_005 only
    // permits hook calls from Render() or a Use*-named method.
    public static Element UseTitleBar(RenderContext ctx)
    {
        // Subscribe during render — not inside a menu item callback, which
        // runs when the item is clicked rather than during a render pass.
        var debugUI = ctx.UseObservable(AppFlags.DebugUI).Value;

        return HStack(8,
            TextBlock("My App"),
#if DEBUG
            // An ordinary menu, compiled into Debug builds only.
            DropDownButton("Dev", MenuItems(new MenuFlyoutItemBase[]
            {
                ToggleMenuItem("Debug UI", debugUI,
                    v => AppFlags.DebugUI.Value = v),
                MenuSeparator(),
                MenuItem("Clear cache", () => CacheService.Clear()),
                MenuItem("Slow mode off", () => AppFlags.SlowMode.Value = false),
            })),
#endif
            // Only *constructed* when the flag is on: in retail the ternary
            // costs one bool read — no element tree, no children reconciled.
            debugUI ? TextBlock("debug overlay").Opacity(0.6) : null
        );
    }
}

// Stand-in for whatever your app actually caches.
static class CacheService
{
    public static void Clear() { }
}
```

Read [Devtools](devtools.md) for the commands, the overlays, and the deprecated
`Microsoft.UI.Reactor.Devtools` package that the live previews still use.

## Catch mistakes early

Reactor's Roslyn analyzers ship inside the `Microsoft.UI.Reactor` package.
They report `REACTOR_*` diagnostics in the editor and in `dotnet build`, and
many come with a code fix. For example, `REACTOR_HOOKS_001` flags a hook
inside an `if`. The fix keeps the hook call unconditional and moves the
condition into the output:

```csharp
class HookOrderGood : Component
{
    public bool ShouldCount;

    public override Element Render()
    {
        // Hook always runs; the conditional moves into the render output.
        var (count, _) = UseState(0);
        return ShouldCount
            ? TextBlock($"Count: {count}")
            : TextBlock("No counter.");
    }
}
```

`mur check` runs the same build and adds a hint under each diagnostic.

- [Analyzers](analyzers.md): every rule ID, its severity, and the code fixes.
- [Rules of Reactor](rules-of-reactor.md): the rules behind the most common
  diagnostics.

## Work with AI agents

AI coding agents can do every stage of this loop. The Reactor agent plugin and
the skill kit teach an agent the DSL, the hooks rules, and the tools on this
page. `mur check` gives the agent build feedback with fix hints, and the
winapp CLI with `--json` and the `winui-devtools` skill lets it see and drive
the running app. The docs site also
publishes `llms.txt` and a Markdown copy of each page.

Read [Build with AI](build-with-ai.md) for install steps for each agent.

## Debug and measure

In Release builds, Reactor reports swallowed exceptions and failed HRESULTs
through the `Microsoft-UI-Reactor` EventSource. Capture them with environment
variables, `dotnet-trace`, the Visual Studio profiler, or in process.
`winapp devtools logs` shows the warnings and errors among them.

- [Diagnostics](diagnostics.md): capture and read Reactor's events and traces.
- [Performance](performance.md): find and fix slow renders.
- [Testing](testing.md): unit tests and UI tests for components.

## How it works

These pages explain the machinery behind the tools. You do not need them to
use the tools.

- [Devtools internals](devtools-internals.md): the in-app preview host behind
  the Visual Studio and VS Code previews.
- [Analyzer architecture](analyzer-architecture.md): how the analyzers and
  code fixes are built.
- [Source mapping](source-mapping.md): how a control maps back to the line of
  code that created it.
- [Perf instrumentation](perf-instrumentation.md): the EventSource, keywords,
  and counters.

## Tips

- Keep `dotnet watch` running in its own terminal. Its output tells you when an
  edit was applied and when the app restarted.
- Build small components. A small `Render()` is quick to re-render and easy to
  inspect.
- Treat analyzer warnings as bugs. Most of them point at a real runtime
  problem, such as lost state or a missed update.
- On an ARM64 machine, pass the runtime explicitly if `dotnet run` picks the
  wrong one: `dotnet run -r win-arm64`.

## Next Steps

- **[Hot reload and live preview](hot-reload.md)**: edit a running app
- **[Devtools](devtools.md)**: inspect and drive a running app with winapp
- **[mur CLI](mur-cli.md)**: every command and its options
- **[Analyzers](analyzers.md)**: every `REACTOR_*` rule
- **[Build with AI](build-with-ai.md)**: use Reactor with AI coding agents
