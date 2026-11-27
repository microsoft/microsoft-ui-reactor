# Elements

The immutable element records that describe each control.

Every control in Reactor is described by an element record. Factory methods create them and modifiers return changed copies. The reconciler turns them into real WinUI controls.

**Learn more:** [Controls](../../controls.md), [Reconciliation](../../reconciliation.md)

## Areas

| Area | Description |
| --- | --- |
| [Layout](layout.md) | Panels, stacks, grids, scrolling and other containers. |
| [Text](text.md) | Text blocks, rich text and the typography factories. |
| [Buttons](buttons.md) | Buttons, toggle buttons, split buttons and hyperlink buttons. |
| [Input](input.md) | Text boxes, check boxes, combo boxes, sliders, pickers and other input controls. |
| [Collections](collections.md) | Lists, grids, trees, flip views and virtualized collections. |
| [Navigation](navigation.md) | Navigation views, tabs, pivots, breadcrumbs and the title bar. |
| [Menus and commands](menus-and-commands.md) | Menus, menu bars, command bars and keyboard accelerators. |
| [Dialogs and flyouts](dialogs-and-flyouts.md) | Content dialogs, flyouts, popups and teaching tips. |
| [Media and icons](media.md) | Images, icons, animated visuals, media playback and person pictures. |
| [Shapes](shapes.md) | Rectangles, ellipses, lines, paths and brushes. |
| [Status and info](status-and-info.md) | Info bars, badges and progress indicators. |
| [Forms](forms.md) | Form fields, validation rules, validation visualizers and the property grid. |
| [Composition](composition.md) | Components, memoization, error boundaries, conditionals and other structural elements. |
| [Interop](interop.md) | Hosted XAML content, web views and maps. |
| [Data grid](data-grid.md) | The data grid and its column factories, from the Microsoft.UI.Reactor.Advanced package. |
| [Docking](docking.md) | The dock manager and its events, from the Microsoft.UI.Reactor.Advanced package. |
| [Win2D canvas](win2d.md) | Win2D drawing surfaces, from the Microsoft.UI.Reactor.Advanced package. |

## Types

| Type | Description |
| --- | --- |
| [`Element`](Element.md) | A lightweight, immutable description of a UI node (the "virtual DOM"). |
