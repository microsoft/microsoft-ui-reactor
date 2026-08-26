# Microsoft.UI.Reactor.ProjectTemplates

**`dotnet new` templates for scaffolding [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) apps — a ready-to-run WinUI 3 Reactor project in one command.**

> [!IMPORTANT]
> **This package is superseded.** The recommended way to scaffold a Reactor app
> is now the official Windows App SDK template pack, which ships first-class
> Reactor templates alongside the WinUI 3 XAML ones:
>
> ```shell
> dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
> dotnet new reactor -n MyApp
> ```
>
> Short names there: `reactor`, `reactor-mvu`, `reactor-navview`,
> `reactor-tabview`. Those templates scaffold **packaged** (single-project MSIX)
> apps, so `dotnet run` launches with full package identity.
>
> This package is still built and published for the **unpackaged**
> (`WindowsPackageType=None`) shape, but `bootstrap.ps1` no longer installs it
> and it is no longer the documented default. Prefer `dotnet new reactor` unless
> you specifically need the unpackaged, zip-and-go project layout.

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

### Template options

- `--NativeAot` (bool, default `false`) — configure the project for Native AOT publishing.
- `--UseProgramMain` (bool, default `false`) — generate an explicit `Program.Main` instead of top-level statements.
- `--Framework <tfm>` (default `net10.0`) — choose the target framework.

```shell
# Example: a Native AOT-ready app with an explicit Main method
dotnet new reactorapp -n MyApp --NativeAot --UseProgramMain
```

## Included Templates

- **Microsoft WinUI Reactor App** (short name `reactorapp`) — a Windows WinUI 3 application using Reactor (C#).

## Updating and uninstalling

```shell
# Update to the latest published templates
dotnet new update

# Remove the templates
dotnet new uninstall Microsoft.UI.Reactor.ProjectTemplates
```

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
