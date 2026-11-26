# Factories

The DSL factory methods that create element records.

Add `using static Microsoft.UI.Reactor.Factories;` and call these methods to build an element tree, for example `VStack(TextBlock("Hello"), Button("OK", onClick))`. A few factories live on other classes, such as `ValidationRuleDsl`; their sections name the `using static` they need. The Data grid, Markdown and Win2D canvas factories come from the `Microsoft.UI.Reactor.Advanced` package and are on `Microsoft.UI.Reactor.Advanced.Factories`.

**Learn more:** [Controls](../../controls.md), [Layout](../../layout.md), [Cheat Sheet](../../cheat-sheet.md)

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
| [Markdown](markdown.md) | Rendered Markdown, from the Microsoft.UI.Reactor.Advanced package. |
| [Win2D canvas](win2d.md) | Win2D drawing surfaces, from the Microsoft.UI.Reactor.Advanced package. |
