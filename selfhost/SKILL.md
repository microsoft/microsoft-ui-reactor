---
name: mur
description: >
  Bootstrap skill for the Reactor CLI tool (patch.exe). Patch is a React-inspired functional UI
  framework for WinUI 3 — declarative C# with no XAML. Use this when asked to "create a Reactor app",
  "build a desktop app with Reactor", "scaffold a WinUI project", or anything involving the Reactor
  framework. This skill tells you how to call the CLI; the CLI itself contains the full API reference.
---

# Reactor CLI — Bootstrap Skill

## What is Patch?

Reactor (Functional UI) is a **React-inspired declarative C# framework for WinUI 3**. It replaces
XAML, data binding, and templates with pure C# — components, hooks, and a virtual-DOM diffing
engine, just like React but targeting native Windows desktop controls.

## CLI Tool: `patch.exe`

The `patch.exe` CLI lives alongside this skill file. It is the primary tool for working with the
Patch framework.

### Commands

| Flag               | Description                                                            |
|--------------------|------------------------------------------------------------------------|
| `--help`, `-h`     | Show usage and all available options                                   |
| `--version`, `-v`  | Print the CLI version                                                  |
| `--skill`          | **Print the full SKILL.md** — the complete API reference, patterns, and project setup guide. Call this first whenever you need to generate or modify Patch code. |
| `--create <name>`  | Scaffold a new Reactor project (`.sln`, `.csproj`, `Program.cs`)         |

### Recommended Workflow

1. **Before writing any Patch code**, run `patch.exe --skill` and read the output. It contains
   the complete API surface, all element helpers, hook signatures, layout patterns, and project
   setup instructions needed to generate correct code in one shot.

2. **To scaffold a new project**, run `patch.exe --create MyApp`. This creates a ready-to-build
   directory with the solution, project file, and a hello-world `Program.cs` that references the
   Patch runtime.

3. **Build the scaffolded project** with `dotnet build MyApp/MyApp.sln`.

### Key Concepts (summary — full details in `--skill` output)

- **No XAML.** Everything is pure C#. No `.xaml` files, no `App.xaml`, no markup.
- **Components** extend `Component` and override `Render()` returning an `Element` tree.
- **Hooks** (`UseState`, `UseEffect`, `UseMemo`, `UseRef`) manage state just like React.
- **DSL helpers** (`Text()`, `Button()`, `VStack()`, `Grid()`, etc.) are imported via
  `using static Microsoft.UI.Reactor.Factories;`.
- **Entry point** is a single top-level statement: `ReactorApp.Run<MyComponent>("Title");`
- **Project type** is `WinExe` targeting `net8.0-windows10.0.22621.0` with `UseWinUI=true`
  and `WindowsPackageType=None` (unpackaged).

### Important

Always call `patch.exe --skill` to get the full, up-to-date API reference before generating
Patch code. The bootstrap skill (this file) only provides enough context to know *how* to call
the CLI — the real knowledge lives in the `--skill` output.
