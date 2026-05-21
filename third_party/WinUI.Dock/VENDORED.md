# Vendored: WinUI.Dock

| | |
|---|---|
| **Upstream** | https://github.com/qian-o/WinUI.Dock |
| **License**  | MIT (preserved verbatim at `./LICENSE`) |
| **Snapshot commit** | `2f5247f10d0abfde0fcb181e3037391d4a27952e` |
| **Snapshot date** | 2026-04-17 |
| **Vendored on** | 2026-05-19 |
| **Reactor spec** | [045 — Docking Windows](../../docs/specs/045-docking-windows-design.md) §4.1, §4.2 |

## Why this is in the repo

Spec 045 Phase 1 vendors WinUI.Dock so Reactor can ship docking inside the
next release cycle without first writing a docking system from scratch.
A thin Reactor wrapper at `src/Reactor.Docking.Xaml/` reconciles a Reactor
element tree onto the upstream control. Phase 2 replaces the runtime with a
Reactor-native rewrite, at which point the source here becomes
reference-only — but it stays in the tree for license compliance and
side-by-side comparison (spec 045 §5.6).

## Light edits applied

Per spec 045 §4.2, four light edits to upstream source are tolerated.

1. **Uno code paths stripped.** Original `WinUI.Dock.csproj` multi-targeted
   `net10.0;net10.0-windows10.0.19041.0` with a `<Choose>/<Otherwise>`
   branch that pulled `Uno.WinUI`. The new csproj single-targets
   `net10.0-windows10.0.22621.0` (matching `src/Reactor/Reactor.csproj`).
   The Uno-branch `Page Include` glob is dropped; the WinUI3 SDK's default
   *.xaml handling is in effect. `Microsoft.WindowsAppSDK` version is
   sourced from `$(WindowsAppSDKVersion)` in `Directory.Build.props` so it
   stays in lockstep with the rest of Reactor.
2. **`.editorconfig` formatting.** Whitespace-only normalization, no
   semantic changes. (Note: applied opportunistically; large blocks of
   upstream source remain in their original style.)
3. **`[assembly: InternalsVisibleTo]`.** Added `Properties/AssemblyInfo.cs`
   exposing internals to `Microsoft.UI.Reactor.Docking.Xaml` (the wrapper)
   and `Reactor.Docking.Xaml.Tests`. The wrapper needs internal access to
   call helpers like `DragDropHelpers` from reconciler code.
4. **Cross-window DnD bug.** Upstream has a documented fragility around
   tearing out a tab while the source window is mid-close — see
   <https://github.com/qian-o/WinUI.Dock/issues>. At snapshot time the
   patch had not landed upstream; the wrapper restricts drag-out to within
   a single `DockManager` (spec 045 §4.6) which sidesteps the path.
   **No source edit applied here.** Re-evaluate at re-snapshot.

## XAMLTools / Themes/Generic.xaml

Upstream uses `XAMLTools.MSBuild` to combine root `*.xaml` files
(`DockManager.xaml`, `Document.xaml`, `DocumentGroup.xaml`,
`LayoutPanel.xaml`) into `Themes/Generic.xaml` at build time. We've removed
`XAMLTools.MSBuild` (it's an additional vendored dependency we don't want)
and **check in the pre-merged `Themes/Generic.xaml` directly**. The root
inputs are excluded from XAML page compilation via `<Page Remove="...">` in
the csproj to avoid duplicate-style-key errors at runtime. If you re-snapshot
upstream, re-run their build once locally to refresh `Themes/Generic.xaml`,
then drop it back into this folder.

## Sunset

Phase 2 has feature parity with the vendored upstream; the native
renderer at `src/Reactor/Docking/Native/` is the canonical runtime
path. The vendored sources are now **runtime-unused by default**
(spec 045 §5.6 / §2.19 disposition):

- Apps consume docking via the native renderer (`DockingNativeInterop.Register`).
- The wrapper at `src/Reactor.Docking.Xaml/` (consuming this vendored
  source) is built but no longer the default — apps that flip
  `REACTOR_DOCK_XAML=1` in their shell still get the P1 chrome for
  side-by-side comparison during the §2.29 human-review pass.
- Once §2.29 sign-off lands, the showcase's XAML flip is removed
  and the `Reactor.Docking.Xaml` project + this `third_party/WinUI.Dock/`
  source drop out of the default solution. The source stays in
  the tree for:
  - license compliance (MIT requires we retain notices for as long
    as we distributed binary code based on the work);
  - A/B regression checks between the native rewrite and the original;
  - documentation reference.
- The actual `Reactor.slnx` removal is scheduled with the §2.19
  phase-exit PR after the §2.29 human review gate signs off; this
  ensures the reviewer can still drive the XAML version side-by-
  side without a worktree dance.

`ThirdPartyNoticeText.txt` (repo root) records the MIT license block under
the **WinUI.Dock** heading. Do not remove that block while these sources
remain in the tree, even after the runtime reference is dropped.

## Re-snapshot checklist

When refreshing from upstream:
1. Pin the new commit hash + date in the table at the top of this file.
2. Diff the upstream `src/WinUI.Dock` tree against `third_party/WinUI.Dock`
   here. Apply changes that are not in the four light-edit set.
3. Re-run upstream's build once to regenerate `Themes/Generic.xaml`; copy
   into place.
4. Reapply light edits 1, 3 to the csproj and AssemblyInfo (these don't
   exist upstream).
5. Verify the wrapper's `DockingSmokeFixture` still mounts in the AppTests
   harness.
6. Bump the snapshot date in `ThirdPartyNoticeText.txt` if the upstream
   license text changes (unlikely — MIT is stable).
