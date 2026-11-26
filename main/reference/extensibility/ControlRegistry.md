---
title: "ControlRegistry"
---

Spec 048 §8 — the global, lazy, lock-free control registry that backs the factory-as-registration pattern. Holds [`Type`](https://learn.microsoft.com/dotnet/api/system.type) → factory-of- `IV1HandlerEntry` entries; never roots a handler or WinUI control type by itself. Every static reference to a handler/control type lives in the *callers* of [`ControlRegistry.Register`](#register) — per-control factory cctors (Pattern A) or `Reg<…>` static-field initializers (Pattern B), each on a per-control rooted path. The trimmer therefore keeps a control iff its factory is reachable from the app entry point.

**Idempotent first-wins.** Repeat [`ControlRegistry.Register`](#register) calls for the same element type are a silent no-op (spec §12.1). Multiple factories legitimately map to the same element type — e.g. `TextBlock()`, `Heading()`, and `Subheading()` all produce `TextBlockElement` (spec §10.3) — and a throw from a cctor would surface as a non-deterministic `TypeInitializationException` at the first-use point. The strict throw-on-duplicate policy from spec 047 §13 Q17 is preserved on the explicit per-host [`Reconciler.RegisterHandler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler#registerhandler) path.

**Hot path.** Per spec §9, [`ControlRegistry.Register`](#register) runs at most once per element type per process, on the cold first-use path. The steady-state per-host dispatch lookup short-circuits in the per-host `_v1Handlers` cache populated on the first registry hit; the registry itself is consulted at most once per (host, element type) pair.

**AOT.** No reflection, no [`Type.MakeGenericType`](https://learn.microsoft.com/dotnet/api/system.type.makegenerictype); the closed-type capture happens inside the generic [`ControlRegistry.Register`](#register) entry point so the AOT compiler can see the closed types statically. The internal map is keyed by [`Type`](https://learn.microsoft.com/dotnet/api/system.type) and stores [`Func`](https://learn.microsoft.com/dotnet/api/system.func-1) delegates only — no MakeGenericType, no runtime type construction.

```csharp
public static class ControlRegistry
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)

## Methods [#methods]

### `Register` [#register]

Spec §8 — register a handler factory for `TElement`. Idempotent first-wins: if an entry already exists for `typeof(TElement)`, this call is a silent no-op. The handler factory is invoked at most once per (host, element type) — on the first dispatch hit — and the resulting adapter is cached into the host's `_v1Handlers` map so steady-state dispatch is the existing fast per-host lookup.

The `handlerFactory` delegate **should** be a `static` lambda (no captures) — Pattern A / Pattern B both rely on the single allocation being interned in a static field at the call site. A capturing lambda is functionally correct but allocates a closure on every `Reg<>.Init` / cctor run, undoing the cost-model claim in spec §9.

**Echo suppression for value-bearing controls (issue #206).** A custom [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) registered here that writes a value-bearing property in `Update` must guard that write with the supported public primitive [`ReactorBinding<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding-1)'s `WriteSuppressed` method (or the static [`ReactorBinding`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding)) so the control's own echo does not flow back as a spurious user edit. Never reach into the internal `ChangeEchoSuppressor`. Descriptor authors get this through `.Controlled` / `.HandCodedControlled`. See `docs/guide/extending-reactor-controls.md`.

```csharp
public static void Register<TElement, TControl>(Func<IElementHandler<TElement, TControl>> handlerFactory) where TElement : Element where TControl : UIElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handlerFactory` | `Func<IElementHandler<TElement, TControl>>` | A factory that, when invoked, returns a fresh handler. Strongly recommended to be a `static` lambda (e.g. `static () => new MarqueeHandler()`) so the delegate is cached in a static field and no closure is allocated. |

**Type parameters**

| Name | Description |
| --- | --- |
| `TElement` | The element record type the handler dispatches against. Used as the dispatch key ([`Type`](https://learn.microsoft.com/dotnet/api/system.type)). |
| `TControl` | The WinUI control the handler mounts. |

### `RegisterDecorator` [#registerdecorator]

Spec 048 §3.4 — decorator sibling of [`ControlRegistry.Register`](#register) for handlers that implement [`IDecoratorElementHandler<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/IDecoratorElementHandler) (a separate, non-inheriting interface from [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler)). Decorator handlers cover target-wrapping flyouts, modal lifecycle wrappers, polymorphic mounts (IconElement), interop bridges (XamlHost/XamlPage), and the keyed-reconcile panels (Stack/Grid/ Canvas/RelativePanel/WrapGrid/Flex) + Button/CheckBox/Expander — see [`IDecoratorElementHandler<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/IDecoratorElementHandler) for the full use-case list.

Stores the same [`Func`](https://learn.microsoft.com/dotnet/api/system.func-1) shape as the value-handler path, so [`Reconciler.TryResolveFromControlRegistry`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) (dispatch arm 3) consumes both uniformly — no dispatch changes required. The adapter wrapping is the only difference: `V1DecoratorHandlerAdapter` versus `V1HandlerAdapter`.

**One-shim invariant (§3.4 authoring rule).** For any given element type, factories must touch *exactly one* of `Reg<…>.Done` or `RegDecorator<…>.Done` — never both. The first-wins TryAdd silently keeps whichever path's cctor fires first; mixing them creates a non-deterministic dispatch outcome that depends on factory call order across the app's startup graph. This contrasts with the harmless aliasing case where multiple factories share the *same* closed-generic shim (spec §10.3 Heading/Subheading both touching `Reg<TextBlockElement, TextBlock, TextBlockHandler>`).

**Singleton-handler factories.** A decorator handler exposed as a `public static readonly` singleton whose underlying type is a private nested class cannot satisfy `RegDecorator`'s `new()` constraint. Such a factory registers directly via this method with a `static` lambda returning the singleton:

```csharp
public static MyElement My(...)
{
    ControlRegistry.RegisterDecorator<MyElement>(
        static () => MyDescriptor.Handler);
    return new MyElement(...);
}
```

This is functionally equivalent to `RegDecorator<…>.Done` — the static lambda still has no captures, the TryAdd still first-wins, and the trim story still routes every reference to the private handler type through the factory call-site path the trimmer can see. (The built-in Icon/XamlPage/XamlHost decorators that formerly used this pattern are now source-generated — spec 058 §15 P5.27/P5.28 — and self-register via a generated Pattern-A cctor.)

```csharp
public static void RegisterDecorator<TElement>(Func<IDecoratorElementHandler<TElement>> handlerFactory) where TElement : Element
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handlerFactory` | `Func<IDecoratorElementHandler<TElement>>` | A factory that, when invoked, returns a fresh decorator handler. Strongly recommended to be a `static` lambda (e.g. `static () => new ButtonHandler()`) so the delegate is cached in a static field and no closure is allocated. |

**Type parameters**

| Name | Description |
| --- | --- |
| `TElement` | The element record type the decorator handler dispatches against. Used as the dispatch key ([`Type`](https://learn.microsoft.com/dotnet/api/system.type)). |

### `RegisterDecoratorForDerivedTypes` [#registerdecoratorforderivedtypes]

Spec 048 §3.4 — decorator sibling of [`ControlRegistry.RegisterForDerivedTypes`](#registerforderivedtypes).

```csharp
public static void RegisterDecoratorForDerivedTypes<TBase>(Func<IDecoratorElementHandler<TBase>> handlerFactory) where TBase : Element
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handlerFactory` | `Func<IDecoratorElementHandler<TBase>>` |  |

### `RegisterForDerivedTypes` [#registerforderivedtypes]

Spec 048 §3.4 — base-derived sibling of [`ControlRegistry.Register`](#register). Registers a handler for `TBase` that also catches every concrete element type whose runtime [`Type`](https://learn.microsoft.com/dotnet/api/system.type) derives from `TBase`. Exact-type registrations (via [`ControlRegistry.Register`](#register) / [`ControlRegistry.RegisterDecorator`](#registerdecorator)) take precedence over a base-derived registration, matching the per-host `V1HandlerRegistry.TryGet` precedence.

Used for the four cases that registered through `Reconciler.RegisterDescriptorForDerivedTypes` on the per-host path: `TemplatedListElementBase`, `LazyStackElementBase`, `ItemsRepeaterElement`, and `ItemsViewElement` — each is a non-generic intermediate base whose closed-T variants (`TemplatedListViewElement<Person>`, etc.) should all share the same handler.

```csharp
public static void RegisterForDerivedTypes<TBase, TControl>(Func<IElementHandler<TBase, TControl>> handlerFactory) where TBase : Element where TControl : UIElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handlerFactory` | `Func<IElementHandler<TBase, TControl>>` |  |
