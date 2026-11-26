# Elements: Dialogs and flyouts

Content dialogs, flyouts, popups and teaching tips.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Dialogs and Flyouts](../../dialogs-and-flyouts.md)

## `ContentDialogElement`

Declarative content dialog. Set IsOpen to true to show, false to close; content is reconciled live while the dialog is open. OnClosed reports how the dialog was dismissed — the button the user chose, or [`ContentDialogResult.None`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.contentdialogresult.none) for a declared IsOpen = false. It does not fire when the dialog is torn down because its owner unmounted or its host/reconciler was disposed. Disposal also cancels deferred opens without OnOpened. State reset needed during teardown belongs in effect cleanup, not OnClosed.

```csharp
public record ContentDialogElement(string Title, Element Content, string PrimaryButtonText = "OK") : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ContentDialog`](../factories/dialogs-and-flyouts.md#contentdialog)

**Modifiers:** [`Closed`](../modifiers/shared-controls.md#closed), [`IsPrimaryButtonEnabled`](../modifiers/dialogs-and-flyouts.md#isprimarybuttonenabled), [`IsSecondaryButtonEnabled`](../modifiers/dialogs-and-flyouts.md#issecondarybuttonenabled), [`Opened`](../modifiers/dialogs-and-flyouts.md#opened), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `CloseButtonText` | `string?` |  |  |
| `Content` | [`Element`](Element.md) |  |  |
| `DefaultButton` | `ContentDialogButton` |  |  |
| `IsOpen` | `bool` |  |  |
| `IsPrimaryButtonEnabled` | `bool` |  | Enables/disables the primary button while the dialog is open. Defaults to `true`. |
| `IsSecondaryButtonEnabled` | `bool` |  | Enables/disables the secondary button while the dialog is open. Defaults to `true`. |
| `OnClosed` | `Action<ContentDialogResult>?` |  |  |
| `OnOpened` | `Action?` |  | Raised after the dialog finishes opening. |
| `PrimaryButtonText` | `string` | `"OK"` |  |
| `SecondaryButtonText` | `string?` |  |  |
| `Title` | `string` |  |  |

## `ContentFlyoutElement`

Describes a content flyout (used as a slot value on buttons or as a modifier attachment). NOT independently mountable — the reconciler recognizes it in flyout slots.

```csharp
public record ContentFlyoutElement(Element Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ContentFlyout`](../factories/dialogs-and-flyouts.md#contentflyout)

| Property | Type | Description |
| --- | --- | --- |
| `Content` | [`Element`](Element.md) |  |
| `Placement` | `FlyoutPlacementMode` | Preferred position of the flyout relative to its target. Defaults to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto), which means "no opinion — let WinUI decide": Reactor clears `FlyoutBase.Placement` so it falls back to the control's own default ([`FlyoutPlacementMode.Top`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.top)) instead of writing `Auto`, because WinUI's show-time validator rejects `Auto` and terminates the process. Changing an already-mounted flyout from an explicit placement back to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto) therefore returns it to that default, rather than leaving a stale local value that would outrank a `Style` setter. |

## `FlyoutElement`

A flyout attached to another element. Wrap the target element.

```csharp
public record FlyoutElement(Element Target, Element FlyoutContent) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Flyout`](../factories/dialogs-and-flyouts.md#flyout)

**Modifiers:** [`AreOpenCloseAnimationsEnabled`](../modifiers/dialogs-and-flyouts.md#areopencloseanimationsenabled), [`Closed`](../modifiers/shared-controls.md#closed), [`Opened`](../modifiers/dialogs-and-flyouts.md#opened), [`OverlayInputPassThroughElement`](../modifiers/dialogs-and-flyouts.md#overlayinputpassthroughelement), [`Set`](../modifiers/native-properties.md#set), [`ShowMode`](../modifiers/dialogs-and-flyouts.md#showmode)

| Property | Type | Description |
| --- | --- | --- |
| `AreOpenCloseAnimationsEnabled` | `bool` | Whether the flyout animates on open/close. Defaults to `true`. |
| `FlyoutContent` | [`Element`](Element.md) |  |
| `IsOpen` | `bool` | Opens the flyout declaratively. A one-shot trigger: the flyout is shown on mount when already `true`, and on the `false` → `true` edge of an update, against any target (a Button/SplitButton target holds the flyout in its own `Flyout` slot, any other target as `FlyoutBase.AttachedFlyout`). Setting it back to `false` does not close the flyout — the user dismisses it. Reset your state from [`FlyoutElement.OnClosed`](#flyoutelement) so the next `true` is a fresh edge. |
| `OnClosed` | `Action?` |  |
| `OnOpened` | `Action?` |  |
| `OverlayInputPassThroughElement` | [`Element?`](Element.md) | Element whose input is passed through the light-dismiss overlay (lets the user interact with one element behind the flyout). |
| `Placement` | `FlyoutPlacementMode` | Preferred position of the flyout relative to its target. Defaults to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto), which means "no opinion — let WinUI decide": Reactor clears `FlyoutBase.Placement` so it falls back to the control's own default ([`FlyoutPlacementMode.Top`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.top)) instead of writing `Auto`, because WinUI's show-time validator rejects `Auto` and terminates the process. Changing an already-mounted flyout from an explicit placement back to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto) therefore returns it to that default, rather than leaving a stale local value that would outrank a `Style` setter. |
| `ShowMode` | `FlyoutShowMode` | How the flyout reacts to clicks outside its bounds (Auto / Standard / Transient / TransientWithDismissOnPointerMoveAway). |
| `Target` | [`Element`](Element.md) |  |

## `PopupElement`

```csharp
public record PopupElement(Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Popup`](../factories/dialogs-and-flyouts.md#popup)

**Modifiers:** [`Closed`](../modifiers/shared-controls.md#closed), [`IsLightDismissEnabled`](../modifiers/dialogs-and-flyouts.md#islightdismissenabled), [`LightDismiss`](../modifiers/dialogs-and-flyouts.md#lightdismiss), [`Offset`](../modifiers/dialogs-and-flyouts.md#offset), [`Opened`](../modifiers/dialogs-and-flyouts.md#opened), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `HorizontalOffset` | `double` |  |
| `IsLightDismissEnabled` | `bool` |  |
| `IsOpen` | `bool` |  |
| `OnClosed` | `Action?` |  |
| `OnOpened` | `Action?` |  |
| `VerticalOffset` | `double` |  |

## `TeachingTipElement`

The element record that describes a WinUI [`TeachingTip`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.teachingtip) control.

```csharp
public record TeachingTipElement(string Title, string? Subtitle = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`TeachingTip`](../factories/dialogs-and-flyouts.md#teachingtip)

**Modifiers:** [`ActionButtonClick`](../modifiers/shared-controls.md#actionbuttonclick), [`Closed`](../modifiers/shared-controls.md#closed), [`HeroContent`](../modifiers/dialogs-and-flyouts.md#herocontent), [`IconSource`](../modifiers/shared-controls.md#iconsource), [`PlacementMargin`](../modifiers/dialogs-and-flyouts.md#placementmargin), [`PreferredPlacement`](../modifiers/dialogs-and-flyouts.md#preferredplacement), [`Set`](../modifiers/native-properties.md#set), [`Target`](../modifiers/dialogs-and-flyouts.md#target)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ActionButtonContent` | `string?` |  |  |
| `CloseButtonContent` | `string?` |  |  |
| `Content` | [`Element?`](Element.md) |  |  |
| `HeroContent` | [`Element?`](Element.md) |  | Optional "hero" Element (image / banner) rendered above the title. |
| `IconSource` | [`IconData?`](../controls/icons.md#icondata) |  | Custom icon source rendered in the tip's leading slot. |
| `IsOpen` | `bool` |  | Whether the tip is shown. Defaults to `false` — drive it from state.  **Edge-triggered.** This declares a *transition*, not a mirror: after the initial render Reactor writes the control only when the declared value *changes*. A light dismiss or the tip's own close button sets `IsOpen` to `false` on the control without touching the declared value, so re-rendering the same declared `true` will not re-open it. Wire [`TeachingTipElement.OnClosed`](#teachingtipelement) back into the state that drives this property; a later `false → true` transition then re-opens the tip.  **Mount is the one exception** (issue #949): a first render that declares `true` does open the tip. WinUI drops a pending open if the tip is written again while it is still unparented, and Reactor configures controls before parenting them, so the mount-time open is deferred until the control is loaded. One consequence worth knowing: because that write lands after the setter pass, a `.Set(t => t.IsOpen = false)` cannot override a declared mount-time `true` the way setters normally win. Declare the value instead of reaching for `.Set` — this property is the supported channel for it. |
| `OnActionButtonClick` | `Action?` |  |  |
| `OnClosed` | `Action?` |  |  |
| `PlacementMargin` | `Thickness` |  | Extra margin around the tip when placed relative to its target. |
| `PreferredPlacement` | `TeachingTipPlacementMode` |  | Preferred placement edge. Defaults to `Auto`. |
| `Subtitle` | `string?` | `null` |  |
| `Target` | [`ElementRef?`](../input/element-refs.md#elementref) |  | Control the tip anchors to, referenced by `ElementRef` so it resolves regardless of mount order. |
| `Title` | `string` |  |  |
