
A Microsoft.UI.Reactor (Reactor) app is a tree of [components](components.md) driven by
[hooks](hooks.md), hosted in a WinUI window that the framework opens and
manages for you. You write one C# file, call `ReactorApp.Run<T>`, and your
component's `Render()` method returns the element tree that becomes the
native control tree. State lives in `UseState` and friends; every setter
invocation re-runs `Render()`; the reconciler diffs the new tree against
the previous one and patches the WinUI controls in place. This page is
the bootstrap walkthrough — installing the framework, scaffolding a
project, and growing from hello-world to a todo list and a calculator.
By the end you will have run code, seen a screenshot of each step, and
recognized the [layout](layout.md) primitives and [hooks](hooks.md) that
the rest of the docset elaborates.

# Getting Started with Reactor

<!-- ai:lock -->
> **Prerequisites:** .NET 10+ and the Windows App SDK.
<!-- /ai:lock -->

> **Heads up — no signed NuGet yet.** Reactor doesn't ship a signed NuGet
> package today, so you build the framework, the `mur` CLI, and the project
> template from source. `bootstrap.ps1` automates all of that — one command,
> ~3 minutes per machine. The signed distribution is tracked in
> [spec 022](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/022-packaging-and-distribution.md).

Reactor is a declarative UI framework for building native Windows apps in pure C#.
No XAML, no data binding, no view models. You describe your UI as a function of
state and Reactor keeps the screen in sync.

## Setup (one-time)

```powershell
git clone https://github.com/microsoft/microsoft-ui-reactor.git
cd microsoft-ui-reactor
./bootstrap.ps1
```

That's it. `bootstrap.ps1` packs and installs `mur` as a `dotnet tool` global
install (so it's on PATH cross-shell with no manual `$env:Path` edits), runs
`mur pack-local` to produce `local-nupkgs/Microsoft.UI.Reactor.0.0.0-local.nupkg`
and the matching `ProjectTemplates` nupkg, registers the `dotnet new reactorapp`
template, and drops the Reactor agent plugin under `~/.claude/plugins/reactor`
(symlink when allowed, copy otherwise).

When it finishes you can immediately run:

```powershell
dotnet new reactorapp -n MyApp
cd MyApp
dotnet run
```

### After `git pull`

The framework changes — your local NuGet snapshot and `dotnet new` template do
not, unless you repack them. Two options:

```powershell
mur upgrade           # repacks the framework + templates and refreshes plugin
./bootstrap.ps1       # same, plus updates the `mur` global tool itself
```

`mur upgrade` is the lightweight path. Re-run `bootstrap.ps1` when you want to
pick up CLI changes (a `mur` process can't replace its own binary mid-run).

### Verify the install

```powershell
mur doctor
```

Lists every dependency the rest of this guide assumes — .NET 10+ SDK, `mur` on
PATH, current `local-nupkgs/` feed, the `reactorapp` template registration, and
the optional Claude plugin. Each line is PASS / WARN / FAIL with a one-line
remediation for anything broken.

> **What this gets you.** A globally-resolvable `mur` (via `~/.dotnet/tools`),
> a local NuGet feed at `<repo>/local-nupkgs/` that apps in any folder can
> consume via `<PackageReference Include="Microsoft.UI.Reactor"
> Version="0.0.0-local" />`, and an agent plugin so AI assistants generate
> against the real factories (`mur --skill` / `mur --api` print the same
> content). Run `mur upgrade` whenever you pull new framework changes.

> **Already have a signed package?** Skip the bootstrap. Reference the
> published `Microsoft.UI.Reactor` package directly and run the consumer-side
> `install-skill-kit.ps1` shipped in the release archive (covered in
> [spec 022](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/022-packaging-and-distribution.md) §4.4).
> Until that release ships, the bootstrap above is the supported path.

### Manual setup

If you'd rather not run `bootstrap.ps1`, here is exactly what it does, step
by step. Every command is a stock `dotnet` or `git` invocation — no Reactor
tooling required until the very last step, where you build `mur` from source
and install it as a global tool. Each block below corresponds to a numbered
phase in `bootstrap.ps1`, so the script is a useful cross-reference if
anything goes wrong.

| # | Step | What it produces |
|---|------|------------------|
| 1 | `dotnet --list-sdks` | Confirms .NET 10+ is installed |
| 2 | `git clone` + `cd` | Local source checkout |
| 3 | `dotnet pack src/Reactor.Cli` | `Microsoft.UI.Reactor.Cli.<ver>.nupkg` in `local-nupkgs/` |
| 4 | `dotnet tool install -g` | `mur` resolvable cross-shell from `~/.dotnet/tools` |
| 5 | `mur pack-local` | Framework + `ProjectTemplates` 0.0.0-local nupkgs |
| 6 | `dotnet new uninstall` + `install` | `dotnet new reactorapp` template registered |
| 7 | Symlink/copy `plugins/reactor` | Reactor agent kit under `~/.claude/plugins/reactor` (optional) |
| 8 | `mur doctor` | Verification that 1–7 all stuck |

**1. Confirm prerequisites.** Reactor requires .NET 10 or later. The Windows
App SDK is pulled in transitively when the framework builds.

```powershell
dotnet --list-sdks
# Expect at least one entry starting with "10."
```

**2. Clone and enter the repo.** Everything below assumes the working
directory is the repo root.

```powershell
git clone https://github.com/microsoft/microsoft-ui-reactor.git
cd microsoft-ui-reactor
```

**3. Pack `mur` as a global-tool nupkg.** `Reactor.Cli.csproj` sets
`PackAsTool=true`, so `dotnet pack` produces a tool package. The
`-p:Platform` flag matters because the build step runs the SignaturesGen
apphost to refresh `skills/reactor.api.txt`; that apphost must match the
host architecture.

```powershell
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
dotnet pack src/Reactor.Cli/Reactor.Cli.csproj `
    -c Release `
    "-p:Platform=$hostArch" `
    -o local-nupkgs `
    --nologo
# Produces local-nupkgs/Microsoft.UI.Reactor.Cli.<version>.nupkg
```

**4. Install `mur` as a dotnet global tool.** This is what puts `mur` on
PATH cross-shell with no `$env:Path` edits. If a previous install exists,
use `update` instead of `install`.

```powershell
dotnet tool install -g `
    --add-source ./local-nupkgs `
    Microsoft.UI.Reactor.Cli `
    --no-cache --ignore-failed-sources

# If `mur` was already installed:
# dotnet tool update -g --add-source ./local-nupkgs Microsoft.UI.Reactor.Cli --no-cache --ignore-failed-sources
```

`dotnet tool install -g` adds `~/.dotnet/tools` to the **user** PATH, which
the current shell does not automatically inherit. For this session only,
prepend it manually so the next step finds `mur`:

```powershell
$env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
```

New PowerShell windows pick up the user-PATH change on their own.

**5. Pack the framework and project templates.** This is what produces the
two `0.0.0-local` nupkgs that the `dotnet new reactorapp` template
references.

```powershell
mur pack-local
# Produces:
#   local-nupkgs/Microsoft.UI.Reactor.0.0.0-local.nupkg
#   local-nupkgs/Microsoft.UI.Reactor.ProjectTemplates.0.0.0-local.nupkg
```

If you'd rather not depend on the freshly-installed `mur`, you can invoke
the source project directly:

```powershell
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj `
    -c Release "-p:Platform=$hostArch" -- pack-local
```

**6. Install the `dotnet new reactorapp` template.** The template engine
caches by package id, so a same-version repack can lose to the cached copy.
Always uninstall first.

```powershell
dotnet new uninstall Microsoft.UI.Reactor.ProjectTemplates 2>$null
dotnet new install local-nupkgs/Microsoft.UI.Reactor.ProjectTemplates.0.0.0-local.nupkg
```

**7. (Optional) Install the Reactor agent plugin.** If you use Claude Code
or another agent and want it to author Reactor code with the right
factories, drop the in-repo plugin folder into your agent's plugin path. A
symlink is preferred so edits in the checkout are immediately visible; a
copy works when you can't create symlinks (Developer Mode off + non-admin
shell).

```powershell
$pluginSrc = (Resolve-Path "plugins/reactor").Path
$pluginDst = "$env:USERPROFILE\.claude\plugins\reactor"
New-Item -ItemType Directory -Path (Split-Path $pluginDst) -Force | Out-Null
if (Test-Path $pluginDst) { Remove-Item $pluginDst -Recurse -Force }
try {
    New-Item -ItemType SymbolicLink -Path $pluginDst -Target $pluginSrc -ErrorAction Stop | Out-Null
} catch {
    Copy-Item $pluginSrc $pluginDst -Recurse -Force
}
```

For Copilot CLI or other agents, follow that tool's plugin-install path and
point it at `<repo>/plugins/reactor`.

**8. Verify.** `mur doctor` performs the same checks the bootstrap script's
final stage relies on — SDK version, `mur` resolvability, both `0.0.0-local`
nupkgs present, template registered, and plugin installed.

```powershell
mur doctor
```

#### Refreshing after `git pull`

Without the bootstrap script, repeat **steps 5 and 6** after every pull —
the framework nupkg and the template both need to be regenerated against
the new source. Repeat **steps 3 and 4** only when `src/Reactor.Cli/`
itself changes (a running `mur` process cannot replace its own binary, so
the install must happen from a shell that isn't already running `mur`).

> **Why a global tool instead of just running from `bin/<arch>/`?** Both
> work. The repo's CLI csproj still mirrors `mur.exe` to `bin/<arch>/` after
> every build for the legacy PATH layout. The global-tool install is just
> the friendliest default — it puts `mur` on PATH cross-shell, cross-CWD,
> with no arch-aware PATH munging, and `dotnet tool update -g` becomes the
> upgrade verb.

> **Caveat:** The bootstrap is the supported developer path *only* until the signed public
> NuGet ships under spec 022. If you skip `bootstrap.ps1` and try `dotnet new
> reactorapp` anyway, the scaffolder fails with `NU1101: Unable to find package
> Microsoft.UI.Reactor.ProjectTemplates` because nothing has produced
> `local-nupkgs/` yet — `dotnet` looks at the configured feeds and the package
> isn't on nuget.org. The fix is to re-run `bootstrap.ps1` (or `mur upgrade`)
> after every `git pull`; the snapshot is regenerated from the current branch,
> not cached. The template installer caches by package id, so a same-version
> repack can lose to the cached copy — `mur upgrade` handles this by running
> `dotnet new uninstall` first.

## Creating a Project

With the template installed, scaffold a new app from anywhere on disk:

```powershell
dotnet new reactorapp -n MyApp
cd MyApp
dotnet run
```

The template wires up the `Microsoft.UI.Reactor` package reference, the
WinUI 3 target framework, and a working `App.cs` that mounts a single
Reactor component. No `App.xaml`, no `MainWindow.xaml.cs` — just one C#
file.

> **Why a custom template?** A `dotnet new console` does not produce a WinUI
> app — it builds a console target with no UI thread, no `OutputType=WinExe`,
> no WindowsAppSDK reference, and no `[STAThread]` entry point. `reactorapp`
> sets all of those plus the Reactor package reference and a backdrop-aware
> root component, so you get a window on first `dotnet run` instead of a
> console-host stub.

## Your First App

The template's `App.cs` is the canonical hello-world. Replace its contents
with the snippet below to match the rest of this guide (the template
defaults to a slightly richer starter; the simpler form is easier to walk
through):

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<GettingStartedApp>("Getting Started", width: 600, height: 400, devtools: true);

class GettingStartedApp : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("World");

        return VStack(16,
            TextBlock($"Hello, {name}!").FontSize(24).Bold(),
            TextBox(name, setName, placeholderText: "Enter your name").Width(250)
        ).Padding(24);
    }
}
```

Run it with `dotnet run` and you'll see this:

![Hello World app running](images/getting-started/hello-world.png)

Here's what's happening:

- **`ReactorApp.Run<T>`** launches a window and mounts your root component.
- **`devtools: true`** enables the in-app dev menu and screenshot capture. In a
  real app you'd normally guard this under `#if DEBUG` so release builds don't
  ship the dev surface; we skip the conditional here for brevity.
- **[`UseState`](hooks.md)** returns the current value and a setter. When you call the
  setter, Reactor re-renders the component with the new value.
- **[`VStack`](layout.md)** stacks children vertically. The number `16` is the pixel spacing.
- **`Text(...).FontSize(24).Bold()`** is the fluent modifier pattern — every
  element supports chainable modifiers for styling and layout.

Type in the text box and the greeting updates instantly. There's no event
wiring or property notification — just state in, UI out.

## Understanding State

Every interactive UI needs state. In Reactor, `UseState` is the primary hook for
managing values that change over time.

### Counter Example

Here's a counter that tracks a single number:

```csharp
// Launch with:
//   ReactorApp.Run<CounterExample>("Counter", width: 600, height: 400);

class CounterExample : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            TextBlock($"Count: {count}").FontSize(20).SemiBold(),
            HStack(8,
                Button("- 1", () => setCount(count - 1)),
                Button("Reset", () => setCount(0)),
                Button("+ 1", () => setCount(count + 1))
            )
        ).Padding(24);
    }
}
```

![Counter with buttons](images/getting-started/counter.png)

Each call to `setCount` triggers a re-render. Reactor diffs the old and new
element trees and updates only the WinUI controls that actually changed.

### Multiple State Values

Components can call `UseState` multiple times — each call tracks an independent
value:

```csharp
// Launch with:
//   ReactorApp.Run<MultipleStateExample>("Multiple State", width: 600, height: 400);

class MultipleStateExample : Component
{
    public override Element Render()
    {
        var (firstName, setFirstName) = UseState("");
        var (lastName, setLastName) = UseState("");
        var (fontSize, setFontSize) = UseState(16.0);

        var fullName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
            ? "Anonymous"
            : $"{firstName} {lastName}".Trim();

        return VStack(12,
            TextBlock($"Hello, {fullName}!").FontSize(fontSize).Bold(),
            TextBox(firstName, setFirstName, placeholderText: "First name").Width(200),
            TextBox(lastName, setLastName, placeholderText: "Last name").Width(200),
            HStack(8,
                TextBlock("Font size:"),
                Slider(fontSize, 10, 40, setFontSize).Width(200),
                TextBlock($"{fontSize:F0}px")
            )
        ).Padding(24);
    }
}
```

The `fullName` variable is derived from `firstName` and `lastName` on every
render. In Reactor, you don't need computed properties or bindings — plain C#
expressions work because `Render()` runs every time state changes.

## Layout Basics

Reactor provides a small set of layout primitives that compose together:

```csharp
// Launch with:
//   ReactorApp.Run<LayoutBasicsExample>("Layout", width: 600, height: 400);

class LayoutBasicsExample : Component
{
    public override Element Render()
    {
        return VStack(16,
            Heading("Layout Demo"),

            SubHeading("Horizontal Stack"),
            HStack(8,
                Button("One"),
                Button("Two"),
                Button("Three")
            ),

            SubHeading("Nested Layout"),
            HStack(16,
                VStack(4,
                    TextBlock("Left Column").Bold(),
                    TextBlock("Item A"),
                    TextBlock("Item B")
                ),
                VStack(4,
                    TextBlock("Right Column").Bold(),
                    TextBlock("Item X"),
                    TextBlock("Item Y")
                )
            )
        ).Padding(24);
    }
}
```

![Layout demo](images/getting-started/layout-basics.png)

| Element | Purpose |
|---------|---------|
| `VStack` | Vertical stack (children top to bottom) |
| `HStack` | Horizontal stack (children left to right) |
| `Grid` | Row/column grid with proportional sizing |
| `ScrollView` | Scrollable wrapper for overflow content |
| `Border` | Container with background, corner radius, stroke |

All layout elements accept an optional spacing parameter as their first
argument: `VStack(12, child1, child2)` adds 12px between children.

## Building a Todo App

Let's put these pieces together into something real. A todo app needs a list
of items, a way to add new ones, and checkboxes to mark them done.

First, define a simple record for items:

```csharp
record TodoItem(string Text, bool Done);
```

Now the full component:

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<TodoApp>("Todo App", width: 550, height: 600, devtools: true);

class TodoApp : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<TodoItem>
        {
            new("Learn Reactor basics", true),
            new("Build a todo app", false),
            new("Explore hooks", false),
        });
        var (newText, setNewText) = UseState("");

        var doneCount = items.Count(i => i.Done);

        return VStack(16,
            Heading("Todo List"),
            TextBlock($"{doneCount}/{items.Count} completed").Opacity(0.6),

            // Input row
            HStack(8,
                TextBox(newText, setNewText, placeholderText: "What needs to be done?")
                    .Width(300),
                Button("Add", () =>
                {
                    if (!string.IsNullOrWhiteSpace(newText))
                    {
                        updateItems(list => [.. list, new TodoItem(newText.Trim(), false)]);
                        setNewText("");
                    }
                }).IsEnabled(!(string.IsNullOrWhiteSpace(newText)))
            ),

            // Item list
            VStack(4,
                items.Select((item, index) =>
                    HStack(8,
                        CheckBox(item.Done, done =>
                            updateItems(list =>
                            {
                                var copy = new List<TodoItem>(list);
                                copy[index] = item with { Done = done };
                                return copy;
                            }),
                            label: item.Text
                        ),
                        Button("Remove", () =>
                            updateItems(list =>
                            {
                                var copy = new List<TodoItem>(list);
                                copy.RemoveAt(index);
                                return copy;
                            })
                        )
                    ).WithKey($"todo-{index}")
                ).ToArray()
            ),

            // Clear completed button
            When(doneCount > 0, () =>
                Button($"Clear completed ({doneCount})", () =>
                    updateItems(list => list.Where(i => !i.Done).ToList())
                )
            )
        ).Padding(24);
    }
}
```

![Todo app](images/todo-app/todo-app.png)

Key patterns to notice:

- **[`UseReducer`](hooks.md)** is like `UseState` but the setter receives a function
  `Func<T, T>` — you transform the previous value into the next value. This
  is the right tool when your new state depends on the old state (like
  appending to a list).
- **`items.Select(...).ToArray()`** maps data into elements. Reactor reconciles
  the list efficiently using keys.
- **`WithKey`** gives each item a stable identity so Reactor can reorder, add,
  and remove items without rebuilding the entire list.
- **`When(condition, () => element)`** conditionally renders content without
  an if/else cluttering the tree.

## Building a Calculator

Here's a more complex example that manages multiple pieces of related state:

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<CalculatorApp>("Calculator", width: 380, height: 500, devtools: true);

class CalculatorApp : Component
{
    public override Element Render()
    {
        var (display, setDisplay) = UseState("0");
        var (operand, setOperand) = UseState<double?>(null);
        var (op, setOp) = UseState<string?>(null);
        var (resetNext, setResetNext) = UseState(false);

        void PressDigit(string digit)
        {
            if (resetNext || display == "0")
            {
                setDisplay(digit);
                setResetNext(false);
            }
            else
            {
                setDisplay(display + digit);
            }
        }

        void PressOp(string nextOp)
        {
            var current = double.Parse(display);
            if (operand.HasValue && op != null)
            {
                var result = Calculate(operand.Value, current, op);
                setDisplay(FormatResult(result));
                setOperand(result);
            }
            else
            {
                setOperand(current);
            }
            setOp(nextOp);
            setResetNext(true);
        }

        void PressEquals()
        {
            if (operand.HasValue && op != null)
            {
                var current = double.Parse(display);
                var result = Calculate(operand.Value, current, op);
                setDisplay(FormatResult(result));
                setOperand(null);
                setOp(null);
                setResetNext(true);
            }
        }

        void PressClear()
        {
            setDisplay("0");
            setOperand(null);
            setOp(null);
            setResetNext(false);
        }

        Element NumButton(string digit) =>
            Button(digit, () => PressDigit(digit))
                .Width(60).Height(48);

        Element OpButton(string label, string opCode) =>
            Button(label, () => PressOp(opCode))
                .Width(60).Height(48);

        return VStack(4,
            // Display
            TextBlock(display)
                .FontSize(32).Bold()
                .HAlign(HorizontalAlignment.Right)
                .Padding(horizontal: 12, vertical: 8),

            // Button grid
            HStack(4, Button("C", PressClear).Width(60).Height(48),
                       NumButton("7"), NumButton("8"), NumButton("9")),
            HStack(4, OpButton("/", "/"),
                       NumButton("4"), NumButton("5"), NumButton("6")),
            HStack(4, OpButton("*", "*"),
                       NumButton("1"), NumButton("2"), NumButton("3")),
            HStack(4, OpButton("-", "-"),
                       NumButton("0"), OpButton("+", "+"),
                       Button("=", PressEquals).Width(60).Height(48))
        ).Padding(16);
    }

    static double Calculate(double a, double b, string op) => op switch
    {
        "+" => a + b,
        "-" => a - b,
        "*" => a * b,
        "/" => b != 0 ? a / b : 0,
        _ => b,
    };

    static string FormatResult(double value) =>
        value == Math.Floor(value) ? $"{value:F0}" : $"{value:G10}";
}
```

![Calculator](images/calculator/calculator.png)

This demonstrates how plain C# control flow (methods, switch expressions,
local functions) works naturally inside Reactor components. There's no special
command pattern needed — just call `setDisplay(...)` and the UI updates.

## Patterns

### Hot reload with `dotnet watch`

The single fastest authoring loop is `dotnet watch run` from the project
directory. Reactor's [dev tooling](dev-tooling.md) hooks watch's
file-change events so a save in `App.cs` re-runs `Render()` without
restarting the window. State that lives in `UseState` is preserved
across the patch (the hook slot table survives), so a counter at 42
stays at 42 after a layout tweak. State held in static fields is
*not* preserved — keep startup state in `UseState` if you want it to
survive hot reload.

### First event, first state — the minimum interactive app

Every Reactor app eventually has the same two ingredients: an event
handler that calls a setter, and a value rendered from the setter's
state slot. The hello-world snippet above wires `setName` to
`TextBox`'s change handler and reads `name` back in the
`Text("Hello, ...")` line — that round trip is the entire reactivity
contract. Once it feels routine, every other [hook](hooks.md) is just
a specialization (`UseReducer` for derived updates, `UseEffect` for
side effects, `UseRef` for non-rendering bookkeeping).

### Running with devtools

Launch with `dotnet run -c Debug` and Reactor mounts the in-app dev
menu (Ctrl+Shift+D by default). The reconcile-highlight overlay flashes
on every commit, the layout-cost overlay attributes per-component time,
and the [dev tooling](dev-tooling.md) page covers the full menu. The
overlays are no-cost in Release builds — the dev menu compiles out
under `#if DEBUG`.

## Common Mistakes

### Editing `bin/` artifacts to "see your change"

Reactor doesn't watch the build output. Edit the source files under
your project (`App.cs`, components in subdirectories) and rebuild —
either via `dotnet run` or under `dotnet watch run`. The `bin/` tree
is regenerated on every build; any hand-edit there is silently
overwritten.

### Trying to use Reactor inside a WinUI `Page` or `UserControl`

Reactor expects to own the window. `ReactorApp.Run<T>` opens a
`Window`, mounts your component tree directly, and drives the
reconciler from that root. Mounting a Reactor component inside a
WinUI `Page` (via `xmlns:reactor=...` markup) does not work — there
is no XAML loader for Reactor elements. If you need Reactor inside an
existing WinUI/WinForms host, see [WinForms interop](winforms-interop.md)
for `XamlIslandControl` or use `ReactorHostControl` from
[components](components.md) for the WinUI host case.

### Reaching for `INotifyPropertyChanged` out of habit

XAML developers often try to back state with a view model. In Reactor,
state IS the binding — `UseState` returns `(value, setter)` and the
setter triggers the re-render. You can still bridge an existing
`INotifyPropertyChanged` source with `UseObservable` (see
[advanced](advanced.md)), but for new screens, hooks are the shorter
path. The [Reactor for XAML developers](xaml-developers.md) page maps
each XAML idiom to its Reactor equivalent.

## Tips

**Think in functions, not objects.** Your `Render()` method is a pure function
from state to UI. Every time state changes, it runs again from the top. Don't
try to mutate the UI imperatively.

**Keep state as high as it needs to be, but no higher.** If only one component
uses a value, `UseState` in that component. If siblings need to share state,
lift it to their parent.

**Use records for data.** C# records give you immutable data with value
equality for free. Reactor uses this for efficient memoization — if your props
haven't changed structurally, the component skips re-rendering.

**Prefer composition over inheritance.** Build small components that each do
one thing, then compose them in parent components. You'll rarely need more than
`Component` or `Component<TProps>` as a base class.

**Fluent modifiers are your friend.** Instead of wrapping elements in layout
containers for simple styling, chain modifiers: `Text("hi").Margin(8).Bold()`
reads cleanly and avoids unnecessary nesting.

## Next Steps

- **[Dev Tooling](dev-tooling.md)** — Set up hot reload and preview mode for a faster development loop
- **[Components](components.md)** — Break your app into reusable pieces with `Component<TProps>` and typed record props
- **[Hooks](hooks.md)** — Deep dive into UseState, UseReducer, UseEffect, UseMemo, and more
- **[Layout](layout.md)** — Master VStack, HStack, Grid, and responsive patterns
- **[Effects and Lifecycle](effects.md)** — Use `UseEffect` for side effects like timers, file I/O, and API calls
