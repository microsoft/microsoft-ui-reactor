---
name: reactor-getting-started
description: "Project setup and the minimal Reactor app shape — single-file `#:package` default, `.csproj` for multi-file apps, the agent-kit cache map, selfhost vs. NuGet-consumer mode detection, and how to bootstrap a fresh source clone with `mur pack-local`. Load this first for any Microsoft.UI.Reactor work."
---

## Minimal app — single file, runnable with `dotnet run`

This is the default starter. It compiles, launches, and demonstrates state + a control. Adapt it; don't re-derive it.

```csharp
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Hello", width: 480, height: 320);

class App : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(12,
            Heading($"Count: {count}"),
            HStack(8,
                Button("-1", () => setCount(count - 1)),
                Button("+1", () => setCount(count + 1))
            )
        ).Padding(24);
    }
}
```

Run with `dotnet run App.cs -p:Platform=ARM64` (or `x64`). On a fresh source clone, run `mur pack-local` once first — see "Bootstrap" below.

**Substitute the version**: `0.0.0-local` is the selfhost default. Outside the source clone, replace with whatever Microsoft.UI.Reactor release you depend on.

## Use a `.csproj` when you need …

… multiple files, **analyzers** (single-file `.cs` builds don't load them), or shared project references.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <Platforms>x64;ARM64</Platforms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.UI.Reactor" Version="0.0.0-local" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.1" />
  </ItemGroup>
</Project>
```

`WindowsPackageType` MUST be `None` (unpackaged, no App.xaml). `UseWinUI` MUST be `true`. **No XAML files of any kind.**

## Where the skill content comes from

You're reading this through the **`reactor` plugin** — the most efficient channel. The plugin SDK preloads `reactor-getting-started` and `reactor-dsl`; topical skills (`reactor-async`, `reactor-design`, etc.) load only when needed. Stick to that flow.

If the plugin isn't available, the same content ships in the NuGet package. **Fall back to filesystem only if you can't get it via the plugin or `mur`** — both are cheaper:

```
%USERPROFILE%\.nuget\packages\microsoft.ui.reactor\<version>\
├─ agentkit\
│  ├─ plugins\reactor\          ← preferred — same content as this skill
│  ├─ SKILL.md                  ← legacy single-file skill (fallback)
│  ├─ reactor.api.txt           ← full signatures index (fallback)
│  └─ skills\                   ← legacy topical skills (fallback)
├─ analyzers\dotnet\cs\Reactor.Analyzers.dll
└─ lib\net10.0-windows10.0.22621\Reactor.dll
```

`mur --api` / `mur --skill` / `mur check` are CLI conveniences that work from any cwd, but **the loaded plugin skill is always cheaper than a tool call.** Use `mur` only when the plugin isn't installed.

## Mode detection — selfhost vs. NuGet consumer

| Mode | Detect | Bootstrap |
|---|---|---|
| **Selfhost** — you're inside a Reactor source clone | `src/Reactor/Reactor.csproj` exists at the repo root | Build `mur` once, then **`mur pack-local`** to populate `local-nupkgs/Microsoft.UI.Reactor.0.0.0-local.nupkg`. Re-run after framework changes. |
| **Consumer** — you're in an app that depends on Microsoft.UI.Reactor | No `src/Reactor/` next to your project | Nothing extra. The package already carries the analyzers and the agent kit. Drop the `nuget.config` shown below if your project lives outside the source clone. |

If selfhost restore fails with "package Microsoft.UI.Reactor 0.0.0-local was not found", run `mur pack-local`.

### Bootstrap (selfhost, fresh clone)

```powershell
dotnet build src/Reactor.Cli -p:Platform=ARM64        # builds the `mur` CLI
.\bin\arm64\mur.exe pack-local                         # packs framework → local-nupkgs/
```

After this, any project under the clone resolves `Microsoft.UI.Reactor 0.0.0-local` from `local-nupkgs/` automatically (the repo-level `nuget.config` configures it).

### `nuget.config` (consumer outside the source clone)

Drop this next to your `.csproj` if you're consuming the local pack from outside the Reactor clone:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="reactor-local" value="C:\path\to\reactor2\local-nupkgs" />
    <add key="nuget.org"     value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

## Required imports

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;   // FlexDirection, FlexJustify, FlexAlign (when using Flex)
using Microsoft.UI.Xaml;             // Thickness, HorizontalAlignment, VerticalAlignment
using Microsoft.UI.Xaml.Controls;    // Orientation, InfoBarSeverity, etc.
using static Microsoft.UI.Reactor.Factories;   // bare TextBlock(), Button(), VStack()
```

## App entry point

```csharp
// Component root
ReactorApp.Run<MyRoot>("Title", width: 1024, height: 768);

// Inline render function (good for tiny demos)
ReactorApp.Run("Title", ctx =>
{
    var (msg, setMsg) = ctx.UseState("Hello!");
    return VStack(TextBlock(msg), Button("Change", () => setMsg("Changed!")));
});
```

## Capture build output

`dotnet run` exits with code 1 on build failure. **Always read the output** — don't assume success. After non-trivial edits, run `mur check <path>` for one-line diagnostics with skill-file pointers (see `reactor-build-and-check`).
