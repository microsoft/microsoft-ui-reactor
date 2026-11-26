---
title: "Devtools"
description: "Devtools let you look inside a running Reactor app and control it. They come in the Microsoft.UI.Reactor.Devtools NuGet package, and they only work in a build where you turn them on."
---

There are two parts. The in-app dev menu adds a
button to your window with debug overlays and your own debug commands. The MCP
server lets `mur devtools` and AI agents read the visual tree and hook state,
take screenshots, and click or type into the app.

## Launch modes [#launch-modes]

Devtools stay off until you start the app with a `--devtools` argument. With
`dotnet run`, put it after `--`:

```
dotnet run -- --devtools app
```

| Argument | What it does |
|---|---|
| none | A normal run. `UseDevtools()` returns `false`, and the dev menu renders nothing. |
| `--devtools app` | Turns on the dev menu, the overlays, and `UseDevtools()`. No MCP server. |
| `--devtools run [Component]` | Everything in `app`, plus the MCP server and log capture. Pass a component name to open that component instead of the app's root. `--devtools` with no verb means `run`. |
| `--devtools list [file]` | Prints the names of the app's components and exits. With a file name, it also writes the list to that file. |
| `--devtools screenshot [Component] --out shot.png` | Opens one component, saves a PNG of the window, and exits. `--out` is required. |

`--preview` is an old name for `--devtools run`. It still works, but prints a
warning.

## Set up devtools [#set-up-devtools]

Devtools need two things at build time: the package and a build switch.

1. Add the package. Reference it in Debug builds only, so a Release build
   doesn't include it:

   ```xml
   <ItemGroup Condition="'$(Configuration)' == 'Debug'">
     <PackageReference Include="Microsoft.UI.Reactor.Devtools" Version="0.1.0-preview.18" />
   </ItemGroup>
   ```

2. Check the build switch. `Reactor.DevtoolsSupport` decides if the devtools
   code is kept in the build. In a Debug build, the `Microsoft.UI.Reactor`
   package turns it on for you. In every other configuration it is off, and
   the trimmer removes the devtools code. To set it yourself, add it to the
   project:

   ```xml
   <ItemGroup Condition="'$(Configuration)' == 'Debug'">
     <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="true" Trim="true" />
   </ItemGroup>
   ```

Then start the app with a `--devtools` argument. The build gives the app the
ability to run devtools. The argument turns them on for one session.

![How devtools turn on. The build switch Reactor.DevtoolsSupport, on by default in Debug builds, and the Microsoft.UI.Reactor.Devtools package reference are both needed. Then the launch flag decides: --devtools app gives the in-app dev menu and overlays; --devtools run also starts the MCP server on 127.0.0.1, which mur devtools and agents can attach to; no flag gives a normal run. In a Release or Native AOT build of the same project, the switch defaults to false, so the trimmer removes the devtools code.](/microsoft-ui-reactor/main/images/devtools/opt-in.svg)

If the switch or the package is missing and you pass a `--devtools` argument,
the app does not open a window. It prints a message that shows the
`RuntimeHostConfigurationOption` line above, then exits with code 2.

If the versions of `Microsoft.UI.Reactor` and `Microsoft.UI.Reactor.Devtools`
don't match, the app prints both versions and stops. This usually means NuGet
restored an old local package. Run `mur pack-local`, delete the app's `bin`
and `obj` folders, and build again.

You can also turn the switch on or off for one build from the command line.
`-p:ReactorDiagnostics=true` turns it on, even in Release. It also turns on
[source mapping](/microsoft-ui-reactor/main/how-it-works/source-mapping). `-p:ReactorDiagnostics=false` turns it
off, even in Debug. Don't ship a build made with `ReactorDiagnostics=true`.

## In-app dev menu [#in-app-dev-menu]

The dev menu is a small amber button with a flyout. Put it in your title bar
or toolbar with `DevtoolsMenu(...)`. When devtools are off, `DevtoolsMenu`
returns an empty element and never calls your item list.

First, declare your debug flags. `Observable<T>` is a small value cell that
components can watch. Make the flags `static readonly`, so every component
shares one instance:

```csharp
// Observable<T> is the lightweight INPC cell that backs dev-only flags.
// Declare them as static readonly so every component shares one instance.
public static class AppFlags
{
    public static readonly Observable<bool> DebugUI = new(false);
    public static readonly Observable<bool> SlowMode = new(false);
    public static readonly Observable<bool> ForceDark = new(false);
}
```

Then build the menu. Read the flags with `ctx.UseObservable(...)` in the render
body, and pass your items to `DevtoolsMenu`:

```csharp
static class DevMenu
{
    public static Element UseTitleBar(RenderContext ctx)
    {
        // Subscribe in the render body, not inside the menu builder. The builder
        // runs only when devtools are on, so a hook call in there would be a
        // conditional hook and break hook ordering.
        //
        // Named Use* because UseObservable is a hook, and REACTOR_HOOKS_005
        // only permits hook calls from Render() or a Use*-named helper.
        var debugUI = ctx.UseObservable(AppFlags.DebugUI).Value;

        return HStack(8,
            TextBlock("My App"),
            // DevtoolsMenu renders nothing unless UseDevtools() is true,
            // so the same cost model carries over.
            DevtoolsMenu(() => new MenuFlyoutItemBase[]
            {
                ToggleMenuItem("Debug UI", debugUI,
                    v => AppFlags.DebugUI.Value = v),
                MenuSeparator(),
                MenuItem("Clear cache", () => CacheService.Clear()),
                MenuItem("Slow mode off", () => AppFlags.SlowMode.Value = false),
            })
        );
    }
}

// Stand-in for whatever your app actually caches.
static class CacheService
{
    public static void Clear() { }
}
```

The item list is a function. It runs only when devtools are on, so never call
a hook inside it. A hook there would run in some renders and not in others,
which breaks the hook order. Changing a flag re-renders every component that
reads it with `UseObservable`. See [Advanced patterns](/microsoft-ui-reactor/main/guides/advanced) for more
about observables.

After your items, the menu adds two built-in items:

| Item | What it does |
|---|---|
| Highlight reconcile changes | Toggles an overlay. After each render, controls that were mounted flash red and controls that changed flash yellow, for about 600 ms. |
| Keyed-list diagnostics | Opens a dialog that lists recent duplicate or null keys in `ListView<T>`, `GridView<T>`, `LazyVStack<T>`, and `LazyHStack<T>`. The item shows the count, or "(none)". |

The highlight overlay helps you find renders you did not expect. It does not
take input, and when it is off the reconciler collects nothing. The
[Rules of Reactor](/microsoft-ui-reactor/main/get-started/rules-of-reactor) page covers the most common causes of
extra renders.

### Gate your own debug UI [#gate-your-own-debug-ui]

`UseDevtools()` returns `true` only when devtools are on for this session. Use
it to show your own debug UI:

```csharp
static class DevGate
{
    // UseDevtools() is a RenderContext extension, so it is reached from a
    // function component's ctx (or any helper you hand the ctx to) — there is
    // no Component-level forwarder. It returns true only when BOTH the
    // Reactor.DevtoolsSupport build switch and `--devtools app` (or `run`) are set.
    //
    // UseDevtools() uses no hook slot, but it is still a Use* call:
    // REACTOR_HOOKS_005 only permits Use* calls from Render() or a Use*-named
    // method, so a helper called `Shell` here would warn.
    public static Element UseShell(RenderContext ctx)
    {
        var dev = ctx.UseDevtools();

        return VStack(8,
            MainContent(),
            dev ? DebugOverlay() : Empty()
        );
    }

    private static Element MainContent() => TextBlock("App content");

    // Only *constructed* when dev is true. In retail the ternary costs one
    // bool read plus one branch — no element tree, no children reconciled.
    private static Element DebugOverlay() =>
        TextBlock("debug overlay").Opacity(0.6);
}
```

The value is fixed when the app starts. `UseDevtools()` does not use a hook
slot and never causes a render. It is still named like a hook, so call it from
`Render()` or from a helper whose name starts with `Use`. Otherwise the
`REACTOR_HOOKS_005` analyzer warns. Build the debug elements inside the
`true` branch, so a normal run doesn't create them.

## MCP server [#mcp-server]

`--devtools run` starts a [Model Context Protocol](https://modelcontextprotocol.io/)
server inside the app. The server describes the running app, not your source
code: its windows, visual tree, hook state, screenshots, and logs. It can also
click, type, and scroll through UI Automation.

The easy way to start it is `mur devtools`. Run it from the project folder:

```
mur devtools
```

`mur devtools` finds the `.csproj` in the current folder, builds the app, and
starts it with `--devtools run`. It watches the app and starts it again after
a `reload`. Options:

| Option | What it does |
|---|---|
| `[project.csproj]` | The project to run. The default is the `.csproj` in the current folder. |
| `--component <Name>` | Open this component instead of the app's root. |
| `--mcp-port <N>` | Use a fixed port. Without it, `mur devtools` picks a free port. |
| `--print-config` | Print MCP client config for Claude Code, VS Code, and GitHub Copilot. |

Then, from a second terminal, talk to the app:

```
mur devtools session list --pretty
mur devtools tree --view summary
mur devtools screenshot --out shot.png
mur devtools click "#increment"
mur devtools state
mur devtools logs --tail 50
mur devtools shutdown
```

Each command finds the running session by itself. Run `mur devtools shutdown`
when you are done. It closes the app and frees the build output, so the next
build can write to it.

### Commands [#commands]

| Group | Commands |
|---|---|
| Look | `tree`, `screenshot`, `state`, `properties`, `styles`, `ancestors`, `resources`, `components`, `windows`, `version` |
| Act | `click`, `type`, `focus`, `invoke`, `toggle`, `select`, `scroll`, `expand`, `collapse`, `wait` |
| Change | `set-property`, `set-resource`, `fire` |
| Session | `switch`, `reload`, `windows.open`, `windows.activate`, `windows.close`, `shutdown`, `session list`, `session clean` |
| Logs and raw tools | `logs`, `call <tool> --args <json>` |

Add `--pretty` to any command for indented JSON. The exit code tells you what
happened:

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Bad arguments |
| 2 | The app could not be reached |
| 3 | Another session is already running for this project |
| 4 | No session is running |
| 5 | The tool returned an error |

### Selectors [#selectors]

Commands that act on a control take a selector:

| Selector | Example | Matches |
|---|---|---|
| Node id | `r:main/CounterDemo.SubmitButton` | The id that `tree` returns |
| Automation id | `#increment` | `.AutomationId("increment")` |
| Name | `[name='Increment']` | The UI Automation name |
| Type path | `Button`, `Button[2]`, `StackPanel > Button` | Controls by type and position |

Give important controls an `.AutomationId(...)`. It is the most stable
selector, and it also helps [UI tests](/microsoft-ui-reactor/main/guides/testing). Node ids change after
`switch` and `reload`, so get them again with `tree`.

### Connect an MCP client [#connect-an-mcp-client]

> **Caveat:** The server listens on `127.0.0.1` only. Each launch creates a random bearer
> token and writes it, with the endpoint, to a lock file under
> `%TEMP%\reactor-devtools\`. The `mur devtools` commands read that file and
> send the token for you. The config from `mur devtools --print-config` does
> not include the token yet, so a client that uses it as-is gets
> `401 Unauthorized`.

For an MCP client that starts its own servers, launch the app with
`--devtools run --mcp-transport stdio`. The server then reads requests from
stdin and writes replies to stdout, with no port and no token.

For agents, the Reactor agent plugin includes a devtools skill that teaches
these commands. See [Build with AI](/microsoft-ui-reactor/main/tools/build-with-ai).

### Logs [#logs]

In `run` mode, the app keeps the latest console output, debug output, and
trace output in memory, 4 MB by default. `mur devtools logs` reads it.
Filter with `--tail <N>`, `--filter <text>`, or
`--source stdout|stderr|debug|trace`, and add `--follow` to keep reading. To
change the buffer, start the app with `--devtools-logs-capacity <MB>`, or turn
capture off with `--devtools-logs off`. Reactor's own diagnostic events are in
the same stream; see [Diagnostics](/microsoft-ui-reactor/main/tools/diagnostics).

## Release builds and Native AOT [#release-builds-and-native-aot]

A Release build has no devtools by default. The switch is off and the trimmer
removes the devtools code. If you also reference the package only in Debug, as
shown above, it isn't in the Release build at all.

You can keep devtools in a Native AOT build for testing. The sample
[`hello-world-aot-devtools-on`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples/apps/hello-world-aot-devtools-on)
shows how:

- Its project file sets the switch to `true` in every configuration and
  references the devtools project.
- Its `Program.cs` calls `ReactorDevtools.EnsureRegistered()` before
  `ReactorApp.Run`. Reactor normally loads the devtools assembly by name, and
  the AOT compiler can't see that. The direct call makes the compiler keep the
  assembly.

Under Native AOT, `properties` and `set-property` can't find dependency
properties, because their metadata is trimmed. Use a JIT build to inspect
properties. [Native AOT](/microsoft-ui-reactor/main/guides/native-aot#devtools-in-aot-builds) has the full
steps.

## Tips [#tips]

- Give the controls you want to drive an `.AutomationId(...)`. Then
  `mur devtools click "#id"` keeps working when the layout changes.
- Use `mur devtools screenshot --out shot.png` to check a layout without
  looking at the screen. It works well in scripts and for agents.
- Only one session can run for each project. If `mur devtools` exits with
  code 3, run `mur devtools shutdown` first.
- Popups are not in the `tree` output. ComboBox drop-downs, flyouts, and
  context menus live outside the window's main visual tree.
- Keep the devtools package reference in a Debug-only `ItemGroup`.

## Next Steps [#next-steps]

- **[Developer tools](/microsoft-ui-reactor/main/tools)**: the whole inner loop
- **[mur CLI](/microsoft-ui-reactor/main/tools/mur-cli)**: every `mur` command
- **[Build with AI](/microsoft-ui-reactor/main/tools/build-with-ai)**: let an agent drive the app
- **[Diagnostics](/microsoft-ui-reactor/main/tools/diagnostics)**: Reactor's events, traces, and logs
- **[DevTools Internals](/microsoft-ui-reactor/main/how-it-works/devtools-internals)**: how the MCP server, selectors, and overlays work
- **[Source mapping](/microsoft-ui-reactor/main/how-it-works/source-mapping)**: map a control back to its line of code
