---
title: "Factories: Media and icons"
description: "Images, icons, animated visuals, media playback and person pictures."
sidebar:
  label: "Media and icons"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## `AnimatedIcon` [#animatedicon]

```csharp
AnimatedIconElement AnimatedIcon(object? source = null, IconSource? fallbackIconSource = null)
```

**Returns:** [`AnimatedIconElement`](/microsoft-ui-reactor/main/reference/elements/media#animatediconelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `object?` | `null` |  |
| `fallbackIconSource` | `IconSource?` | `null` |  |

## `AnimatedVisualPlayer` [#animatedvisualplayer]

```csharp
AnimatedVisualPlayerElement AnimatedVisualPlayer()
```

**Returns:** [`AnimatedVisualPlayerElement`](/microsoft-ui-reactor/main/reference/elements/media#animatedvisualplayerelement).

## `BitmapIcon` [#bitmapicon]

```csharp
BitmapIconData BitmapIcon(Uri source, bool showAsMonochrome = true)
```

**Returns:** [`BitmapIconData`](/microsoft-ui-reactor/main/reference/controls/icons#bitmapicondata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `Uri` |  |  |
| `showAsMonochrome` | `bool` | `true` |  |

## `FontIcon` [#fonticon]

```csharp
FontIconData FontIcon(string glyph, string? fontFamily = null, double? fontSize = null)
```

**Returns:** [`FontIconData`](/microsoft-ui-reactor/main/reference/controls/icons#fonticondata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `glyph` | `string` |  |  |
| `fontFamily` | `string?` | `null` |  |
| `fontSize` | `double?` | `null` |  |

## `Icon` [#icon]

```csharp
IconElement Icon(IconData data)
IconElement Icon(Symbol symbol)
IconElement Icon(string symbol)
```

| Overload | Description |
| --- | --- |
| `Icon(IconData)` | Creates a standalone icon element from an [`IconData`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) instance. |
| `Icon(Symbol)` | Creates a standalone symbol icon element from a [`Symbol`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.symbol) enum value. |
| `Icon(string)` | Creates a standalone symbol icon element (e.g. `Icon("Home")`). |

**Returns:** [`IconElement`](/microsoft-ui-reactor/main/reference/elements/media#iconelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | [`IconData`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) |  |
| `symbol` | `Symbol` or `string` |  |

## `Image` [#image]

```csharp
ImageElement Image(string source)
```

**Returns:** [`ImageElement`](/microsoft-ui-reactor/main/reference/elements/media#imageelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `string` |  |

## `ImageIcon` [#imageicon]

```csharp
ImageIconData ImageIcon(Uri source)
```

**Returns:** [`ImageIconData`](/microsoft-ui-reactor/main/reference/controls/icons#imageicondata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Uri` |  |

## `MediaPlayerElement` [#mediaplayerelement]

```csharp
MediaPlayerElementElement MediaPlayerElement(string? source = null)
```

**Returns:** [`MediaPlayerElementElement`](/microsoft-ui-reactor/main/reference/elements/media#mediaplayerelementelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `string?` | `null` |  |

## `PathIcon` [#pathicon]

```csharp
PathIconData PathIcon(string data)
```

**Returns:** [`PathIconData`](/microsoft-ui-reactor/main/reference/controls/icons#pathicondata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `string` |  |

## `PersonPicture` [#personpicture]

```csharp
PersonPictureElement PersonPicture()
```

**Returns:** [`PersonPictureElement`](/microsoft-ui-reactor/main/reference/elements/media#personpictureelement).

## `SymbolIcon` [#symbolicon]

```csharp
SymbolIconData SymbolIcon(string symbol)
```

**Returns:** [`SymbolIconData`](/microsoft-ui-reactor/main/reference/controls/icons#symbolicondata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `symbol` | `string` |  |
