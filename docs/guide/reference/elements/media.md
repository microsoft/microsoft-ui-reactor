# Elements: Media and icons

Images, icons, animated visuals, media playback and person pictures.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Text and Media](../../text-and-media.md)

## `AnimatedIconElement`

The element record that describes a WinUI [`AnimatedIcon`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.animatedicon) control.

```csharp
public record AnimatedIconElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`AnimatedIcon`](../factories/media.md#animatedicon)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `FallbackIconSource` | `IconSource?` |  |
| `Source` | `object?` |  |

## `AnimatedVisualPlayerElement`

The element record that describes a WinUI [`AnimatedVisualPlayer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.animatedvisualplayer) control.

```csharp
public record AnimatedVisualPlayerElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`AnimatedVisualPlayer`](../factories/media.md#animatedvisualplayer)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `AutoPlay` | `bool` |  |

## `IconElement`

Standalone icon element that can be placed in the element tree. Wraps an [`IconData`](../controls/icons.md#icondata) and mounts to the corresponding native [`IconElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.iconelement) subtype.

```csharp
public record IconElement(IconData Data) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Icon`](../factories/media.md#icon)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Data` | [`IconData`](../controls/icons.md#icondata) |  |

## `ImageElement`

The element record that describes a WinUI [`Image`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.image) control.

```csharp
public record ImageElement(string Source) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Image`](../factories/media.md#image)

**Modifiers:** [`ImageFailed`](../modifiers/media.md#imagefailed), [`ImageOpened`](../modifiers/media.md#imageopened), [`NineGrid`](../modifiers/media.md#ninegrid), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Height` | `double?` |  |
| `NineGrid` | `Thickness?` | Nine-grid (slice) values for resolution-independent corner stretching. |
| `OnImageFailed` | `Action<string>?` | Raised when the image fails to load. Receives the failure message. |
| `OnImageOpened` | `Action?` | Raised after the image source loads successfully (marshalled to UI thread). |
| `Source` | `string` |  |
| `Stretch` | `string?` |  |
| `Width` | `double?` |  |

## `MediaPlayerElementElement`

The element record that describes a WinUI [`MediaPlayerElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.mediaplayerelement) control.

```csharp
public record MediaPlayerElementElement(string? Source = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`MediaPlayerElement`](../factories/media.md#mediaplayerelement)

**Modifiers:** [`MediaEnded`](../modifiers/media.md#mediaended), [`MediaFailed`](../modifiers/media.md#mediafailed), [`MediaOpened`](../modifiers/media.md#mediaopened), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AreTransportControlsEnabled` | `bool` |  |  |
| `AutoPlay` | `bool` |  |  |
| `OnMediaEnded` | `Action?` |  | Raised when playback reaches the end of the source. Marshalled to the UI thread. |
| `OnMediaFailed` | `Action<string>?` |  | Raised when the underlying `MediaPlayer` fails to open or play. Receives the failure error message as a string. Marshalled to the UI thread. |
| `OnMediaOpened` | `Action?` |  | Raised when the underlying `MediaPlayer` finishes opening the source. Marshalled to the element's UI thread; may fire after the element has unmounted (the handler is safe to ignore in that case). |
| `Source` | `string?` | `null` |  |

## `PersonPictureElement`

The element record that describes a WinUI [`PersonPicture`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.personpicture) control.

```csharp
public record PersonPictureElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`PersonPicture`](../factories/media.md#personpicture)

**Modifiers:** [`DisplayName`](../modifiers/media.md#displayname), [`Initials`](../modifiers/media.md#initials), [`ProfilePicture`](../modifiers/media.md#profilepicture), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `BadgeNumber` | `int` |  |
| `DisplayName` | `string?` |  |
| `Initials` | `string?` |  |
| `IsGroup` | `bool` |  |
| `ProfilePicture` | `string?` |  |
