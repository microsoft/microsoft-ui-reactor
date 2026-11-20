# Components

Component base classes, the render context, refs, context and error types.

A component is a class whose `Render` method returns an element tree. These types are the building blocks every component uses.

**Learn more:** [Components](../../components.md), [Thinking in Reactor](../../thinking-in-reactor.md), [Context](../../context.md)

## Types

| Type | Description |
| --- | --- |
| [Render errors](render-errors.md) | Types that describe an error thrown while a component renders. |
| [`AppContexts`](AppContexts.md) | Static context registrations shared across the Reactor core. |
| [`Callbacks<T>`](Callbacks.md) | An opt-in, always-equal wrapper for the delegate (callback) portion of a component's props, so callback identity never forces a re-render. |
| [`Component`](Component.md) | Base class for stateful components (like React class components, but using hooks). |
| [`Component<TProps>`](Component-1.md) | Base class for components that receive typed props (e.g., navigation parameters). |
| [`Context<T>`](Context.md) | A typed, named context that can be provided to a subtree and consumed by any descendant. |
| [`ContextBase`](ContextBase.md) | Non-generic base for type-erased storage in the context scope stack. |
| [`ElementFactory<T>`](ElementFactory.md) | Bridges WinUI's ItemsRepeater/IElementFactory to Reactor's Reconciler. |
| [`IReactorKeyed`](IReactorKeyed.md) | Identity-on-data convention for items rendered into Reactor collection elements (`TemplatedListElementBase` and `LazyStackElementBase`) and into hand-built keyed children (`FlexColumn(items.Select(... .WithKey(item)))`). |
| [`Observable<T>`](Observable.md) | A single-value cell that raises `INotifyPropertyChanged.PropertyChanged` when `Observable.Value` is assigned a different value. |
| [`Optional<T>`](Optional.md) | Represents either an explicit value or an unset state. |
| [`Ref<T>`](Ref.md) | A mutable reference that persists across renders (like React's useRef). |
| [`RenderContext`](RenderContext.md) | Passed to function components and provides access to hooks. |
| [`SourceLocation`](SourceLocation.md) | Spec 010 — the C# source location that produced an `Element`. |
