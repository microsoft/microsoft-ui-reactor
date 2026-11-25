# Factories: Media and icons

Images, icons, animated visuals, media playback and person pictures.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Text and Media](../../text-and-media.md)

## `AnimatedIcon`

```csharp
AnimatedIconElement AnimatedIcon(object? source = null, IconSource? fallbackIconSource = null)
```

**Returns:** [`AnimatedIconElement`](../elements/media.md#animatediconelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `object?` | `null` |  |
| `fallbackIconSource` | `IconSource?` | `null` |  |

## `AnimatedVisualPlayer`

```csharp
AnimatedVisualPlayerElement AnimatedVisualPlayer()
```

**Returns:** [`AnimatedVisualPlayerElement`](../elements/media.md#animatedvisualplayerelement).

## `BitmapIcon`

```csharp
BitmapIconData BitmapIcon(Uri source, bool showAsMonochrome = true)
```

**Returns:** [`BitmapIconData`](../controls/icons.md#bitmapicondata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `Uri` |  |  |
| `showAsMonochrome` | `bool` | `true` |  |

## `FontIcon`

```csharp
FontIconData FontIcon(string glyph, string? fontFamily = null, double? fontSize = null)
```

**Returns:** [`FontIconData`](../controls/icons.md#fonticondata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `glyph` | `string` |  |  |
| `fontFamily` | `string?` | `null` |  |
| `fontSize` | `double?` | `null` |  |

## `Icon`

```csharp
IconElement Icon(IconData data)
IconElement Icon(Symbol symbol)
IconElement Icon(string symbol)
```

| Overload | Description |
| --- | --- |
| `Icon(IconData)` | Creates a standalone icon element from an [`IconData`](../controls/icons.md#icondata) instance. |
| `Icon(Symbol)` | Creates a standalone symbol icon element from a [`Symbol`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.symbol) enum value. |
| `Icon(string)` | Creates a standalone symbol icon element (e.g. `Icon("Home")`). |

**Returns:** [`IconElement`](../elements/media.md#iconelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | [`IconData`](../controls/icons.md#icondata) |  |
| `symbol` | `Symbol` or `string` |  |

## `Image`

```csharp
ImageElement Image(string source)
```

**Returns:** [`ImageElement`](../elements/media.md#imageelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `string` |  |

## `ImageIcon`

```csharp
ImageIconData ImageIcon(Uri source)
```

**Returns:** [`ImageIconData`](../controls/icons.md#imageicondata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Uri` |  |

## `MediaPlayerElement`

```csharp
MediaPlayerElementElement MediaPlayerElement(string? source = null)
```

**Returns:** [`MediaPlayerElementElement`](../elements/media.md#mediaplayerelementelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `string?` | `null` |  |

## `PathIcon`

```csharp
PathIconData PathIcon(string data)
```

**Returns:** [`PathIconData`](../controls/icons.md#pathicondata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `string` |  |

## `PersonPicture`

```csharp
PersonPictureElement PersonPicture()
```

**Returns:** [`PersonPictureElement`](../elements/media.md#personpictureelement).

## `SymbolIcon`

```csharp
SymbolIconData SymbolIcon(string symbol)
```

**Returns:** [`SymbolIconData`](../controls/icons.md#symbolicondata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `symbol` | `string` |  |
