---
title: "Factories"
description: "The DSL factory methods that create element records."
sidebar:
  label: "Overview"
---

Add `using static Microsoft.UI.Reactor.Factories;` and call these methods to build an element tree, for example `VStack(TextBlock("Hello"), Button("OK", onClick))`. A few factories live on other classes, such as `ValidationRuleDsl`; their sections name the `using static` they need. The Data grid, Markdown and Win2D canvas factories come from the `Microsoft.UI.Reactor.Advanced` package and are on `Microsoft.UI.Reactor.Advanced.Factories`.

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls), [Layout](/microsoft-ui-reactor/main/guides/layout), [Cheat Sheet](/microsoft-ui-reactor/main/get-started/cheat-sheet)

## Areas [#areas]

| Area | Description |
| --- | --- |
| [Layout](/microsoft-ui-reactor/main/reference/factories/layout) | Panels, stacks, grids, scrolling and other containers. |
| [Text](/microsoft-ui-reactor/main/reference/factories/text) | Text blocks, rich text and the typography factories. |
| [Buttons](/microsoft-ui-reactor/main/reference/factories/buttons) | Buttons, toggle buttons, split buttons and hyperlink buttons. |
| [Input](/microsoft-ui-reactor/main/reference/factories/input) | Text boxes, check boxes, combo boxes, sliders, pickers and other input controls. |
| [Collections](/microsoft-ui-reactor/main/reference/factories/collections) | Lists, grids, trees, flip views and virtualized collections. |
| [Navigation](/microsoft-ui-reactor/main/reference/factories/navigation) | Navigation views, tabs, pivots, breadcrumbs and the title bar. |
| [Menus and commands](/microsoft-ui-reactor/main/reference/factories/menus-and-commands) | Menus, menu bars, command bars and keyboard accelerators. |
| [Dialogs and flyouts](/microsoft-ui-reactor/main/reference/factories/dialogs-and-flyouts) | Content dialogs, flyouts, popups and teaching tips. |
| [Media and icons](/microsoft-ui-reactor/main/reference/factories/media) | Images, icons, animated visuals, media playback and person pictures. |
| [Shapes](/microsoft-ui-reactor/main/reference/factories/shapes) | Rectangles, ellipses, lines, paths and brushes. |
| [Status and info](/microsoft-ui-reactor/main/reference/factories/status-and-info) | Info bars, badges and progress indicators. |
| [Forms](/microsoft-ui-reactor/main/reference/factories/forms) | Form fields, validation rules, validation visualizers and the property grid. |
| [Composition](/microsoft-ui-reactor/main/reference/factories/composition) | Components, memoization, error boundaries, conditionals and other structural elements. |
| [Interop](/microsoft-ui-reactor/main/reference/factories/interop) | Hosted XAML content, web views and maps. |
| [Data grid](/microsoft-ui-reactor/main/reference/factories/data-grid) | The data grid and its column factories, from the Microsoft.UI.Reactor.Advanced package. |
| [Markdown](/microsoft-ui-reactor/main/reference/factories/markdown) | Rendered Markdown, from the Microsoft.UI.Reactor.Advanced package. |
| [Win2D canvas](/microsoft-ui-reactor/main/reference/factories/win2d) | Win2D drawing surfaces, from the Microsoft.UI.Reactor.Advanced package. |
