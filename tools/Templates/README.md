# Microsoft.UI.Reactor.ProjectTemplates

**`dotnet new` templates for scaffolding [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) apps — a ready-to-run WinUI 3 Reactor project in one command.**

## About

This package installs project templates for the .NET CLI and Visual Studio so you can create a new declarative WinUI 3 desktop app powered by Reactor without wiring up the project by hand.

## How to Use

Install the templates:

```shell
dotnet new install Microsoft.UI.Reactor.ProjectTemplates
```

Create a new Reactor app:

```shell
dotnet new reactorapp -n MyApp
cd MyApp
dotnet run -p:Platform=x64
```

This scaffolds a runnable WinUI 3 project that references `Microsoft.UI.Reactor` and the Windows App SDK, with a root component already wired up through `ReactorApp.Run`.

## Included Templates

| Template | Short name | Description |
|----------|-----------|-------------|
| Microsoft WinUI Reactor App | `reactorapp` | A Windows WinUI 3 application using Reactor (C#). |

## Additional Documentation

- [Getting started guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/getting-started.md)
- [`dotnet new` documentation](https://learn.microsoft.com/dotnet/core/tools/dotnet-new)
- [Custom templates for dotnet new](https://learn.microsoft.com/dotnet/core/tools/custom-templates)

## Related Packages

- [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — the core declarative WinUI 3 framework.
- [`Microsoft.UI.Reactor.Advanced`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Advanced) — optional Win2D/graphics components.
- [`Microsoft.UI.Reactor.Devtools`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Devtools) — optional developer-loop devtools host.

## Feedback & Contributing

These templates are part of the open-source Reactor project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
