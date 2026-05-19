# Hooks reference

Hooks are how Reactor components subscribe to state, schedule effects,
and reach into the framework's lifecycle from a function-shaped
`Render` body. Each entry on this index is generated from the
`<summary>` XML doc comments authored alongside the source in
`src/Reactor/Hooks/` — the canonical surface — and is grouped by the
`hooks` category in `docs/_pipeline/reference-map.yaml`.

For the narrative walkthrough of how hooks compose with components,
context, and effects, see the [Hooks](../../hooks.md) and
[Effects](../../effects.md) guide pages. The reference leaves below
are the exhaustive index.

## Generated leaves

The leaf pages in this folder are produced by `mur docs compile`'s
reference-generation step (spec 041 §10.4). Each page covers one
public type / method / property / field / event in the Hooks
namespace, with sections for Summary, Parameters, Returns,
Discussion, Examples, Caveats, and See Also.

> **Heads up.** This index is one of the few hand-authored files
> under `docs/guide/reference/` — the per-member pages around it are
> compiler output and should not be hand-edited. See
> [spec 041 §10.4](../../../specs/041-docs-comprehensive-uplift.md)
> for the full rationale.
