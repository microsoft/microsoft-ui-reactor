# Control modifiers: Media and icons

Modifiers that only the controls in the Media and icons area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Text and Media](../../text-and-media.md)

## `DisplayName`

```csharp
PersonPictureElement DisplayName(this PersonPictureElement el, string name)
```

Declared in `ElementExtensions`.

**Applies to:** [`PersonPictureElement`](../elements/media.md#personpictureelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

## `ImageFailed`

Wires the image-failed handler. Receives the failure message. Passing `null` clears.

```csharp
ImageElement ImageFailed(this ImageElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ImageElement`](../elements/media.md#imageelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `ImageOpened`

Wires the image-opened handler (fires after the source loads successfully). Passing `null` clears.

```csharp
ImageElement ImageOpened(this ImageElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ImageElement`](../elements/media.md#imageelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `Initials`

```csharp
PersonPictureElement Initials(this PersonPictureElement el, string initials)
```

Declared in `ElementExtensions`.

**Applies to:** [`PersonPictureElement`](../elements/media.md#personpictureelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `initials` | `string` |  |

## `MediaEnded`

Wires the media-ended handler. Marshalled to UI thread. Passing `null` clears.

```csharp
MediaPlayerElementElement MediaEnded(this MediaPlayerElementElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`MediaPlayerElementElement`](../elements/media.md#mediaplayerelementelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `MediaFailed`

Wires the media-failed handler. Receives the failure error message. Marshalled to UI thread. Passing `null` clears.

```csharp
MediaPlayerElementElement MediaFailed(this MediaPlayerElementElement el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`MediaPlayerElementElement`](../elements/media.md#mediaplayerelementelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |

## `MediaOpened`

Wires the media-opened handler. Marshalled to UI thread. Passing `null` clears.

```csharp
MediaPlayerElementElement MediaOpened(this MediaPlayerElementElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`MediaPlayerElementElement`](../elements/media.md#mediaplayerelementelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `NineGrid`

Nine-grid (slice) values for resolution-independent corner stretching.

```csharp
ImageElement NineGrid(this ImageElement el, Thickness nineGrid)
```

Declared in `ElementExtensions`.

**Applies to:** [`ImageElement`](../elements/media.md#imageelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `nineGrid` | `Thickness` |  |

## `ProfilePicture`

Sets the avatar image from an absolute URI string, such as an `https://` address or an `ms-appx:///` asset path. A `null`, empty, malformed, or relative value shows no picture, so the control falls back to its initials or the generic person glyph; removing the picture on a later render clears the previous image.

```csharp
PersonPictureElement ProfilePicture(this PersonPictureElement el, string? uri)
```

Declared in `ElementExtensions`.

**Applies to:** [`PersonPictureElement`](../elements/media.md#personpictureelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `uri` | `string?` |  |
