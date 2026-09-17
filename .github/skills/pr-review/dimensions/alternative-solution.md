# Alternative-solution review

You are reviewing a PR diff for the `microsoft/microsoft-ui-reactor` repo and asking:
**is there a simpler, more idiomatic, or already-existing way to do this in
this codebase?** Apply the shared output contract in `_shared-contract.md`.
Set `Domain: alternative-solution` on every finding.

## Repo-specific patterns to enforce

- **Adding a control → `ControlDescriptor`, not a hand-rolled handler.** The
  primary path for a new WinUI control is a
  `ControlDescriptor<TElement, TControl>`. Registration is lazy: the factory body
  carries the `Reg<…>.Done` touch, so the handler registers on first factory call
  (spec-048 §3.4 deleted the old eager `RegisterV1BuiltInHandlers` bootstrap for
  trimming). Explicit registration goes through `ControlRegistry.Register` /
  `RegisterDecorator`; `ReactorApp.RegisterAllBuiltIns()` is the opt-in bulk path.
  A hand-coded `IElementHandler<TElement, TControl>`
  is only for irregular controls. The legacy `MountXxx`/`UpdateXxx`
  dispatch-switch path is gone — flag new code that re-introduces it or hand-rolls
  mount/update logic a descriptor could express. (See
  `docs/guide/extending-reactor-controls.md`, spec-047 and spec-048.)
- **Echo handling → `WriteSuppressed` / `.Controlled` / `valueDiffEcho`, never
  the suppressor directly.** New value-control code should use the stable
  `WriteSuppressed` primitive or declare `.Controlled` / `valueDiffEcho` on a
  descriptor. Flag direct use of `ChangeEchoSuppressor` from author/handler code
  and direct raw control writes that bypass the suppression contract.
- **Per-element state → `ReactorAttached.StateProperty`.** Don't stash
  per-element state on `FrameworkElement.Tag` or a new
  `ConditionalWeakTable` — the attached DP store already exists.
- **Layout containers → reuse existing primitives.** `VStack` / `HStack` /
  `Grid` / `Border` / `FlexPanel` exist. Flag new layout code that re-implements
  stacking/flex behavior instead of composing them. For Flexbox, defer to the
  Yoga engine rather than re-deriving layout math.
- **Single-child container → `Border`, not `Grid`/`VStack`.** A one-child
  wrapper used only for padding/background/corner radius should be a `Border`.
  Flag a `Grid` or `VStack` wrapping exactly one child for styling.
- **Hooks → reuse the existing hook family.** Before adding a new `Use*`,
  confirm `UseState` / `UseEffect` / `UseReducer` / `UseMemo` / `UseCallback` /
  `UseRef` (and async hooks like `UseResource` / `UseMutation`) can't express it.
  Flag a new hook that duplicates an existing one, or hand-managed state/effects
  that should be a hook.
- **Factories & modifiers → extend the partial classes.** `Factories` and the
  modifier extensions are `partial` and split across files. Flag a parallel
  registry / new static entry point when the existing partial class is the home.
- **Theming → `Theme.*` tokens / `Theme.Ref()`.** Flag new hardcoded colors on
  themed surfaces where a token exists (also a `REACTOR_THEME_001` concern).
- **No XAML.** Everything is C# (except `ReactorApplication.xaml`). Flag any new
  `.xaml` UI file or runtime XAML-string parsing for layout.
- **AOT / trimming.** New reflection in the core library must be annotated; a
  reflection-based approach where a source generator or static dispatch exists
  is the wrong tool. Flag reflection that a generator (`Reactor.Wrappers.Generator`,
  `Reactor.Localization.Generator`) or a descriptor could replace.

## Cross-cutting checks

- Does this change duplicate logic that already exists in another handler,
  hook, modifier, or helper? Search for similar patterns and recommend reuse.
- Could a new method be a thin wrapper over an existing helper plus 2-3 lines?
  If so, recommend the wrapper.
- Is a new abstraction premature (one caller, no anticipated second)?
  Recommend inlining.
- Does a new descriptor/handler re-derive children-reconciliation logic the base
  reconciler already provides via a children strategy?

## What to drop

- Generic "this could be more functional" / "consider LINQ" without a concrete
  callable alternative in the repo.
- Refactor suggestions that exceed the scope of the PR ("rewrite the whole
  reconciler") — note them only as `low` with a tight recommendation, or skip.

## Severity guide for this dimension

- Re-implementing existing engine logic (reconciler mount/update, echo
  suppression, Yoga layout, attached-state store) → medium.
- Hand-rolled handler where a `ControlDescriptor` fits → medium.
- New hook / layout primitive duplicating an existing one → medium.
- Minor "could reuse helper X" with marginal benefit → low.
