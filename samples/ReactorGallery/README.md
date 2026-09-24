# ReactorGallery

Every WinUI 3 control, rebuilt with the Reactor declarative C# DSL — the Reactor
counterpart to [microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery).

## Build and run

```powershell
# Unpackaged — the default, and what `dotnet build Reactor.slnx` / CI use
dotnet build samples/ReactorGallery -p:Platform=x64
dotnet run   --project samples/ReactorGallery -p:Platform=x64

# Packaged (MSIX) — opt in
dotnet build samples/ReactorGallery -p:Platform=x64 -p:GalleryPackaged=true

# …and actually produce the .msix (slower; lands in AppPackages/)
dotnet build samples/ReactorGallery -p:Platform=x64 -p:GalleryPackaged=true `
             -p:GenerateAppxPackageOnBuild=true
```

`-p:Platform=x64` (or `ARM64`) is required — `AnyCPU` is not a supported WinUI app
platform. Signing is off by default so a fresh clone builds without a developer
certificate; for a deployable signed package pass
`-p:AppxPackageSigningEnabled=true -p:PackageCertificateKeyFile=<path.pfx>` and set
`Package.appxmanifest`'s `<Identity Publisher="...">` to your certificate subject.

Both flavours build from the **same project and the same sources** — see
`ReactorGallery.csproj`. The only thing that differs is packaging, and the only
feature that notices is deep linking.

## Deep linking

Every page in the gallery has a shareable URL under the `reactor-gallery://` scheme.
The link button in the title bar copies the link for whatever is currently on screen.

| Link | Opens |
|---|---|
| `reactor-gallery:///` | Home |
| `reactor-gallery:///home` | Home |
| `reactor-gallery:///settings` | Settings |
| `reactor-gallery:///search?q=toggle` | Search results for "toggle" |
| `reactor-gallery:///category/basic-input` | The Basic Input category grid |
| `reactor-gallery:///item/button` | The Button page |

`/control/{tag}` is accepted as an alias for `/item/{tag}`. Matching is
case-insensitive, tolerates a trailing slash, and accepts the two-slash spelling
(`reactor-gallery://item/button`).

Try it from anywhere:

```powershell
Start-Process "reactor-gallery:///item/button"
```

If the gallery is already running the link navigates the existing window instead of
opening a second one.

### How it is wired

| Piece | File | Notes |
|---|---|---|
| URI space (both directions) | `DeepLink/GalleryRoutes.cs` | Built on Reactor's `DeepLinkMap<TRoute>`. No WinRT dependency, so it is unit-tested headlessly in `tests/Reactor.Tests/Samples/GalleryDeepLinkTests.cs`. |
| Scheme registration | `DeepLink/GalleryProtocol.cs` | Unpackaged only — see below. |
| Single-instancing + activation | `DeepLink/GalleryActivation.cs` | `AppInstance.FindOrRegisterForKey` + `RedirectActivationToAsync`; warm activations are marshalled onto the UI thread. |
| Navigation | `GalleryShell.cs` | Seeds initial state from `GalleryActivation.InitialRoute`, then listens for `RouteActivated`. |

Resolved control and category tags are validated against `ControlRegistry`, so a
malformed or hostile link falls back to Home rather than handing an arbitrary string
to the `NavigationView` as a selected tag.

### Why navigation runs on `OnItemInvoked`

The shell treats `NavigationView.SelectedTag` as **controlled output** and never derives
navigation from it. WinUI raises `SelectionChanged` for programmatic `SelectedItem`
writes as well as user clicks, and Reactor forwards both to `OnSelectedTagChanged` — so a
handler there cannot tell a deep link's own echo from a real click. Handling navigation
there makes a link undo itself: it clears the query a `/search?q=` link just set, and
overwrites the back target with the destination.

`OnItemInvoked` is user-only, so every navigation source (nav items, control cards, the
Home page, Back, and deep links) funnels through one `NavigateTo` helper and no echo can
reach it. The behaviour difference is pinned by the
`SelectionEvt_NavigationViewProgrammaticIsNotInvoked` selftest fixture.

### Packaged vs. unpackaged registration

This is the one place the two flavours genuinely diverge, and it is a *runtime*
branch (`GalleryProtocol.IsPackaged`), never a `#if`:

- **Packaged (MSIX)** — `Package.appxmanifest` declares a `windows.protocol`
  extension, so Windows registers the scheme at install time and removes it at
  uninstall. The app does nothing at startup, and the Settings card shows no
  registration UI at all — there is nothing to configure, and an app cannot revoke a
  manifest-declared protocol anyway.
- **Unpackaged** — there is no package manifest, so the app registers itself under
  `HKCU\Software\Classes` via `ActivationRegistrationManager` on every launch. Because
  that is a real, persistent side effect on the user's machine, **Settings → Deep
  links** exposes it with a toggle to turn it back off. Registering on every launch
  (rather than only when missing) is what re-points the handler after a rebuild moves
  the binary, so turning it off from Settings lasts for the session and the next launch
  registers again.

`GalleryProtocol.IsPackaged` answers the question with the non-throwing Win32
`GetCurrentPackageFullName` probe rather than `Windows.ApplicationModel.Package.Current`,
which reports "unpackaged" by throwing — the normal case here, and not something worth
a first-chance exception in every debugger session.

## Search index

`reactor-search-index.json` is generated from this app's source and consumed by the
external `winui-search` CLI (`winapp find-ui --source reactor`). After adding, renaming,
or removing a control — or changing **any** of its sample snippets, since every clean
`SampleCard` on a page is now emitted — regenerate it:

```powershell
dotnet run --project tools/Reactor.SearchIndex
```

A `Reactor.Tests` gate byte-compares the committed file, so a stale index fails CI.
Curate keywords and overrides in `tools/Reactor.SearchIndex/editorial.json`, never in
the generated JSON.

### Fundamentals

The `Fundamentals` category holds framework mechanics rather than controls — hooks,
element refs, keyboard and pointer input. They are ordinary gallery pages, so an agent
searching the index for `UseState hook` or `key down event handler` reaches a snippet
that compiles (issue #1275). Their prose is lifted verbatim from the shipped agent-kit
skills by `<!-- index:… -->` marker, so the two never drift; see
[spec 064](../../docs/specs/064-search-index-framework-concepts.md).

## See also

- [`GAPS.md`](GAPS.md) — WinUI features with no direct Reactor equivalent, and what to
  use instead.
