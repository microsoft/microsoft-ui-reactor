---
title: "Components"
description: "Component base classes, the render context, refs, context and error types."
sidebar:
  label: "Overview"
---

A component is a class whose `Render` method returns an element tree. These types are the building blocks every component uses.

**Learn more:** [Components](/microsoft-ui-reactor/main/guides/components), [Thinking in Reactor](/microsoft-ui-reactor/main/get-started/thinking-in-reactor), [Context](/microsoft-ui-reactor/main/guides/context)

## Types [#types]

| Type | Description |
| --- | --- |
| [Render errors](/microsoft-ui-reactor/main/reference/components/render-errors) | Types that describe an error thrown while a component renders. |
| [`AppContexts`](/microsoft-ui-reactor/main/reference/components/AppContexts) | Static context registrations shared across the Reactor core. |
| [`Callbacks<T>`](/microsoft-ui-reactor/main/reference/components/Callbacks) | An opt-in, always-equal wrapper for the delegate (callback) portion of a component's props, so callback identity never forces a re-render. |
| [`Component`](/microsoft-ui-reactor/main/reference/components/Component) | Base class for stateful components (like React class components, but using hooks). |
| [`Component<TProps>`](/microsoft-ui-reactor/main/reference/components/Component-1) | Base class for components that receive typed props (e.g., navigation parameters). |
| [`Context<T>`](/microsoft-ui-reactor/main/reference/components/Context) | A typed, named context that can be provided to a subtree and consumed by any descendant. |
| [`ContextBase`](/microsoft-ui-reactor/main/reference/components/ContextBase) | Non-generic base for type-erased storage in the context scope stack. |
| [`ElementFactory<T>`](/microsoft-ui-reactor/main/reference/components/ElementFactory) | Bridges WinUI's ItemsRepeater/IElementFactory to Reactor's Reconciler. |
| [`IReactorKeyed`](/microsoft-ui-reactor/main/reference/components/IReactorKeyed) | Identity-on-data convention for items rendered into Reactor collection elements (`TemplatedListElementBase` and `LazyStackElementBase`) and into hand-built keyed children (`FlexColumn(items.Select(... .WithKey(item)))`). |
| [`Observable<T>`](/microsoft-ui-reactor/main/reference/components/Observable) | A single-value cell that raises `INotifyPropertyChanged.PropertyChanged` when `Observable.Value` is assigned a different value. |
| [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) | Represents either an explicit value or an unset state. |
| [`Ref<T>`](/microsoft-ui-reactor/main/reference/components/Ref) | A mutable reference that persists across renders (like React's useRef). |
| [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext) | Passed to function components and provides access to hooks. |
| [`SourceLocation`](/microsoft-ui-reactor/main/reference/components/SourceLocation) | Spec 010 — the C# source location that produced an `Element`. |
