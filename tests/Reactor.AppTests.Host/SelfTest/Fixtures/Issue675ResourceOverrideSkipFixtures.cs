using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #675 — live-FrameworkElement proofs that <c>ResourceOverrides.ThemeRefs</c>
/// re-resolve across ALL reconciler skip fast-paths (element-level shallow-skip,
/// positional child-skip, keyed prefix/suffix child-skip) plus the
/// <c>ApplyResourceOverrides</c> stale-key removal gate.
///
/// <para>Issue #701 audit completion — extends the proofs to the sixth and final reconciler
/// surface, the keyed-MIDDLE (LIS reorder) patch (<see cref="KeyedMiddleReorderReResolves"/>).
/// The #701 cross-cutting audit confirmed every reconciler skip/patch surface re-resolves a
/// ThemeRef <c>ResourceOverrides</c>: surfaces 1-5 (element-level, positional child-skip,
/// keyed prefix, keyed suffix, bulk array fast-path) decline the skip — via
/// <c>Element.CanSkipUpdate</c> and <c>ChildDiffHints.IsThemeSensitive</c> — and re-resolve in
/// <c>Reconciler.Update</c>'s element-level shallow-skip arm; the keyed-middle (#6) has no skip
/// arm at all and re-resolves by unconditionally routing every survivor through
/// <c>UpdateChild</c> → <c>Update</c>. <c>Reconciler.Update</c> is the single source of truth
/// for skip-path theme re-resolution.</para>
///
/// <para>WHY A SOURCE-RESOURCE MUTATION, NOT A RequestedTheme TOGGLE: the existing
/// <c>StructuralSkipFixtures</c> remarks document that a parent <c>RequestedTheme</c>
/// toggle is an UNRELIABLE observable for the <c>ThemeRef.Resolve</c> →
/// <c>fe.Resources[key]</c> snapshot — its effective-theme view lags the propagation
/// within a synchronous reconcile pass. These fixtures sidestep that entirely: they
/// register a plain (non-themed) brush under a unique key in
/// <c>Application.Current.Resources</c>, which <c>ThemeRef.Resolve</c> picks up via its
/// <c>TryResolveNonThemed</c> fallback, then MUTATE that source brush between two
/// shallow-equal renders. A correctly-behaving skip path re-runs
/// <c>ApplyResourceOverrides</c> and the control's <c>Resources[targetKey]</c> flips to
/// the new brush; a skip path that drops the re-resolution leaves the stale brush. This
/// is deterministic and theme-independent.</para>
/// </summary>
internal static class Issue675ResourceOverrideSkipFixtures
{
    private const string SrcKey = "Issue675_SourceBrush";
    private const string TargetKey = "Issue675_TargetBrush";

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b) =>
        new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, r, g, b));

    private static SolidColorBrush? ResourceBrush(FrameworkElement? fe, string key)
    {
        if (fe?.Resources is { } res && res.TryGetValue(key, out var v))
            return v as SolidColorBrush;
        return null;
    }

    // The app-level ResourceDictionary has a Source set (XamlControlsResources), so it
    // rejects direct local values. Register the ThemeRef source brush in our OWN
    // (Source-less) dictionary merged into Application.Current.Resources, which
    // ThemeRef.Resolve discovers via its TryResolveNonThemed MergedDictionaries scan.
    // Mutating the brush is then a write to our dictionary, never the sealed app one.
    //
    // #660 — ThemeRef.Resolve now caches (resourceKey, themeName) -> Brush, cleared only
    // by InvalidateResolutionCache (wired to the hosts' theme-change handlers). Our test
    // mutates the source brush WITHOUT a theme change, so we must invalidate the cache
    // ourselves — faithfully mirroring what the host does on an effective-theme change —
    // both here (clears any prior fixture's cached entry for the shared key) and after
    // each mutation. Without it the cache returns the stale resolved brush and a skipped
    // child's re-resolution can't be observed.
    private static ResourceDictionary InstallSourceDict(string key, SolidColorBrush initial)
    {
        var dict = new ResourceDictionary { [key] = initial };
        Application.Current.Resources.MergedDictionaries.Add(dict);
        global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
        return dict;
    }

    // Mutate the source brush AND invalidate the #660 resolution cache, so the next
    // ThemeRef.Resolve re-reads the merged dictionary (as it would after a real theme
    // change). A skip path that drops re-resolution still leaves fe.Resources[key] stale.
    private static void SetSource(ResourceDictionary dict, string key, SolidColorBrush brush)
    {
        dict[key] = brush;
        global::Microsoft.UI.Reactor.Core.ThemeRef.InvalidateResolutionCache();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Element-level shallow-skip (Reconciler.Update.cs) — GREEN today
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A themed leaf carrying ONLY <c>ResourceOverrides.ThemeRefs</c> is the sole output
    /// of a function component, so it is reconciled through <c>UpdateChild</c> →
    /// <c>Update</c> (no child-skip). When it re-renders shallow-equal, Update's
    /// element-level shallow-skip (Update.cs ~97-98) re-applies the ThemeRef. This is the
    /// behavior test missing for that branch (issue #675 Finding C / TC-M2) and the
    /// regression guard that keeps the element-level path re-resolving after the fix.
    /// </summary>
    internal sealed class ElementLevelSkipReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = MakeBrush(220, 0, 0);
            var blue = MakeBrush(0, 0, 220);
            var srcDict = InstallSourceDict(SrcKey, red);
            try
            {
                // #721 positive guard: the leaf is structurally shallow-equal across renders, so
                // Update takes the element-level shallow-skip arm (Update.cs:81 -> 97-98
                // skip-AND-re-resolve, return null) rather than a full property diff. This pins the
                // GREEN to the skip-and-re-resolve path, not an incidental full update.
                H.Check("Issue675_ElementLevel_CellSkipEligible", Element.ShallowEquals(
                    TextBlock("elemMarker").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))),
                    TextBlock("elemMarker").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))));

                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (_, setN) = ctx.UseState(0);
                    bump = setN;
                    // Leaf output → reconciled via Update directly. Shallow-equal across the
                    // state bump (ResourceOverrides is not part of ShallowEquals), so the
                    // element-level skip arm runs and re-resolves the ThemeRef.
                    return TextBlock("elemMarker")
                        .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)));
                });

                await Harness.Render();
                var tb = H.FindControl<TextBlock>(t => t.Text == "elemMarker");
                H.Check("Issue675_ElementLevel_MountResolvedRed",
                    ReferenceEquals(ResourceBrush(tb, TargetKey), red));

                // Mutate the SOURCE brush, then re-render shallow-equal.
                SetSource(srcDict, SrcKey, blue);
                bump!(1);
                await Harness.Render();

                tb = H.FindControl<TextBlock>(t => t.Text == "elemMarker");
                H.Check("Issue675_ElementLevel_ReResolvedBlueOnSkip",
                    ReferenceEquals(ResourceBrush(tb, TargetKey), blue));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(srcDict);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Positional child-skip (ChildReconciler.UpdateCommonChild) — RED pre-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A themed child (ResourceOverrides.ThemeRefs only) sits at a stable index in a
    /// VStack whose sibling changes every render (so the VStack itself is not
    /// shallow-equal and its children reconcile positionally). Before the fix the themed
    /// cell satisfies <c>CanSkipUpdate</c> (it gates only on ThemeBindings), so the
    /// positional skip arm short-circuits and the ThemeRef is never re-resolved — the
    /// control's <c>Resources[TargetKey]</c> stays stale. After the fix
    /// <c>CanSkipUpdate</c> declines the skip, routing the cell through Update where it
    /// re-resolves. RED pre-fix, GREEN post-fix.
    /// </summary>
    internal sealed class PositionalChildSkipReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = MakeBrush(0, 160, 0);
            var blue = MakeBrush(120, 0, 160);
            var srcDict = InstallSourceDict(SrcKey, red);
            try
            {
                // #721 positive skip-eligibility guard: prove the themed cell is structurally
                // shallow-equal across renders, so its child-skip eligibility hinges ONLY on the
                // ResourceOverrides.ThemeRefs gate — not an incidental Setters/modifier difference
                // that would route it through full Update and mask a broken child-skip re-resolve as
                // a false-green. ShallowEquals ignores ResourceOverrides, so this stays true pre/post
                // fix; a refactor that makes the cell decline the skip for any OTHER reason flips it.
                H.Check("Issue675_Positional_CellSkipEligible", Element.ShallowEquals(
                    TextBlock("posCell").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))),
                    TextBlock("posCell").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))));

                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    bump = setN;
                    return VStack(
                        // Changing sibling → fresh VStack children array AND a non-skippable
                        // index 0, so the positional walk runs over the themed cell at index 1.
                        TextBlock($"posSibling{n}"),
                        TextBlock("posCell")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))));
                });

                await Harness.Render();
                var cell = H.FindControl<TextBlock>(t => t.Text == "posCell");
                H.Check("Issue675_Positional_MountResolvedRed",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), red));

                SetSource(srcDict, SrcKey, blue);
                bump!(1);
                await Harness.Render();

                cell = H.FindControl<TextBlock>(t => t.Text == "posCell");
                H.Check("Issue675_Positional_ReResolvedBlueOnChildSkip",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), blue));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(srcDict);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed child-skip (ChildReconciler keyed prefix/suffix) — RED pre-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Same scenario as <see cref="PositionalChildSkipReResolves"/> but the children carry
    /// keys (<c>.WithKey</c>), so reconciliation takes the keyed path. The themed cell is
    /// matched in the keyed prefix/suffix scan, which shares <c>CanSkipUpdate</c> with the
    /// positional arm — proving the fix applies to the keyed skip too. RED pre-fix,
    /// GREEN post-fix.
    /// </summary>
    internal sealed class KeyedChildSkipReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = MakeBrush(200, 120, 0);
            var blue = MakeBrush(0, 120, 200);
            var srcDict = InstallSourceDict(SrcKey, red);
            try
            {
                // #721 positive skip-eligibility guard (see PositionalChildSkipReResolves): the keyed
                // cell is structurally shallow-equal across renders (Key is not compared by
                // ShallowEquals — it is matched separately via KeyMatch), so its keyed child-skip
                // eligibility hinges only on the ResourceOverrides.ThemeRefs gate. Guards against a
                // refactor silently turning the RED into a false-green.
                H.Check("Issue675_Keyed_CellSkipEligible", Element.ShallowEquals(
                    TextBlock("keyCell").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))).WithKey("cell"),
                    TextBlock("keyCell").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))).WithKey("cell")));

                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    bump = setN;
                    return VStack(
                        TextBlock($"keySibling{n}").WithKey("sibling"),
                        TextBlock("keyCell")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))
                            .WithKey("cell"));
                });

                await Harness.Render();
                var cell = H.FindControl<TextBlock>(t => t.Text == "keyCell");
                H.Check("Issue675_Keyed_MountResolvedRed",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), red));

                SetSource(srcDict, SrcKey, blue);
                bump!(1);
                await Harness.Render();

                cell = H.FindControl<TextBlock>(t => t.Text == "keyCell");
                H.Check("Issue675_Keyed_ReResolvedBlueOnChildSkip",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), blue));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(srcDict);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ApplyResourceOverrides removal gate (oldOverrides == null) — RED pre-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drives <c>Reconciler.ApplyResourceOverrides</c> directly on a live control with
    /// <c>oldOverrides == null</c> on BOTH calls (the Mount path always passes null). The
    /// first call seeds two Reactor-managed keys; the second supplies overrides missing
    /// one of them. Before the fix the removal block was gated on
    /// <c>oldOverrides is not null</c>, so the dropped key leaked into <c>fe.Resources</c>;
    /// after the fix removal is driven off the managed-key set vs the new overrides,
    /// independent of <c>oldOverrides</c> nullness. RED pre-fix, GREEN post-fix.
    /// </summary>
    internal sealed class RemovalGateStaleKeyWhenOldOverridesNull(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var fe = new TextBlock();
            var brushA = MakeBrush(10, 20, 30);
            var brushB = MakeBrush(40, 50, 60);

            var first = new ResourceOverrides(
                Literals: new Dictionary<string, object> { ["KeyA"] = brushA, ["KeyB"] = brushB },
                ThemeRefs: new Dictionary<string, ThemeRef>());

            // Seed managed keys with oldOverrides == null (the Mount-path shape).
            Reconciler.ApplyResourceOverrides(fe, null, first);
            H.Check("Issue675_Removal_SeededKeyA", fe.Resources.ContainsKey("KeyA"));
            H.Check("Issue675_Removal_SeededKeyB", fe.Resources.ContainsKey("KeyB"));

            // New overrides drop KeyB; oldOverrides is STILL null. The stale-key removal
            // must run regardless and strip KeyB.
            var second = new ResourceOverrides(
                Literals: new Dictionary<string, object> { ["KeyA"] = brushA },
                ThemeRefs: new Dictionary<string, ThemeRef>());
            Reconciler.ApplyResourceOverrides(fe, null, second);

            H.Check("Issue675_Removal_KeyARemains", fe.Resources.ContainsKey("KeyA"));
            H.Check("Issue675_Removal_StaleKeyBRemoved", !fe.Resources.ContainsKey("KeyB"));

            return Task.CompletedTask;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ResourceOverrides transition-away (remove side of the contract) — RED pre-fix
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cell carrying a <c>ResourceOverrides.ThemeRefs</c> override DROPS it while
    /// otherwise shallow-equal (a conditional <c>.Resources(...)</c> whose condition flips).
    /// <c>ShallowEquals</c> does not compare ResourceOverrides, and the new element has no
    /// ThemeRefs so the <c>CanSkipUpdate</c> gate doesn't decline either — so before the
    /// ShallowEquals ResourceOverrides comparison the cell is child-skipped,
    /// <c>ApplyResourceOverrides</c> never runs, and the resolved brush survives stale in
    /// <c>fe.Resources[key]</c>. After the fix <c>ShallowEquals</c> declines (overrides
    /// differ) → full Update → the removal gate strips the dropped managed key. RED pre-fix,
    /// GREEN post-fix. This closes the remove side, symmetric with ThemeBindings transition-away.
    /// </summary>
    internal sealed class TransitionAwayRemovesStaleOverride(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = MakeBrush(180, 30, 30);
            var srcDict = InstallSourceDict(SrcKey, red);
            try
            {
                // #721 positive guard: absent the override the leaf is structurally
                // shallow-equal, so the transition's skip-decline is driven by the override
                // DROP (detected by the ShallowEquals ResourceOverrides compare), not an
                // incidental modifier.
                H.Check("Issue675_TransitionAway_BaseShapeSkipEligible",
                    Element.ShallowEquals(TextBlock("transCell"), TextBlock("transCell")));

                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    bump = setN;
                    // n==0: carries the ThemeRef override; n>=1: drops it (otherwise identical).
                    var cell = TextBlock("transCell");
                    if (n == 0)
                        cell = cell.Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)));
                    return VStack(cell);
                });

                await Harness.Render();
                var cell = H.FindControl<TextBlock>(t => t.Text == "transCell");
                var cellBefore = cell;
                H.Check("Issue675_TransitionAway_MountHasOverride",
                    ResourceBrush(cell, TargetKey) is not null);

                // Drop the override while the cell is otherwise shallow-equal.
                bump!(1);
                await Harness.Render();

                cell = H.FindControl<TextBlock>(t => t.Text == "transCell");
                // #721 right-reason: the control is REUSED in place (not remounted), so the
                // override removal is the removal-gate stripping the managed key on the full
                // Update path — a remount would trivially drop resources for the wrong reason.
                H.Check("Issue675_TransitionAway_ControlReusedInPlace",
                    ReferenceEquals(cellBefore, cell));
                H.Check("Issue675_TransitionAway_StaleOverrideRemoved",
                    ResourceBrush(cell, TargetKey) is null);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(srcDict);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed SUFFIX child-skip re-resolve (coverage parity with the keyed prefix)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Coverage companion to <see cref="KeyedChildSkipReResolves"/> (which exercises the keyed
    /// PREFIX arm). Here the FIRST child's KEY changes every render, so the keyed prefix scan
    /// stops at index 0 (KeyMatch fails) and the trailing themed cell (stable key) is matched
    /// by the keyed SUFFIX scan instead — locking the shared <c>CanSkipUpdate</c> contract on
    /// the suffix arm too. The themed cell re-resolves its ThemeRef across a source-brush change.
    /// </summary>
    internal sealed class KeyedSuffixChildSkipReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = MakeBrush(60, 60, 200);
            var blue = MakeBrush(200, 60, 60);
            var srcDict = InstallSourceDict(SrcKey, red);
            try
            {
                // #721 positive guard: the themed cell is structurally shallow-equal across
                // renders, so its keyed-suffix skip eligibility hinges only on the ThemeRefs gate.
                H.Check("Issue675_KeyedSuffix_CellSkipEligible", Element.ShallowEquals(
                    TextBlock("keySuffixCell").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))).WithKey("cellSuffix"),
                    TextBlock("keySuffixCell").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))).WithKey("cellSuffix")));

                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    bump = setN;
                    // First child's KEY changes each render → keyed prefix scan stops at index 0,
                    // so the trailing themed cell (stable key) is consumed by the SUFFIX scan.
                    return VStack(
                        TextBlock("keySuffixSibling").WithKey($"sib{n}"),
                        TextBlock("keySuffixCell")
                            .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))
                            .WithKey("cellSuffix"));
                });

                await Harness.Render();
                var cell = H.FindControl<TextBlock>(t => t.Text == "keySuffixCell");
                var cellBefore = cell;
                H.Check("Issue675_KeyedSuffix_MountResolvedRed",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), red));

                SetSource(srcDict, SrcKey, blue);
                bump!(1);
                await Harness.Render();

                cell = H.FindControl<TextBlock>(t => t.Text == "keySuffixCell");
                // #721 right-reason: the cell is updated IN PLACE (control reused), confirming
                // it was matched by the keyed SUFFIX arm, not remounted by the keyed middle.
                H.Check("Issue675_KeyedSuffix_ControlReusedInPlace",
                    ReferenceEquals(cellBefore, cell));
                H.Check("Issue675_KeyedSuffix_ReResolvedBlueOnChildSkip",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), blue));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(srcDict);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Literal-override no-false-decline (skip-floor guard for the RO compare)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Live (brush creation needs WinUI activation): proves the new ShallowEquals
    /// ResourceOverrides comparison does NOT regress the skip-floor for literal-override
    /// cells. A hex literal is re-parsed to a FRESH brush instance each render, so a
    /// reference compare would false-decline an UNCHANGED override; ResourceOverridesEqual
    /// compares brush literals by value (BrushesEqual) → same hex stays shallow-equal, a
    /// changed hex declines.
    /// </summary>
    internal sealed class LiteralOverrideNoFalseDecline(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var same1 = TextBlock("lit").Resources(r => r.Set("ButtonBackground", "#0078D4"));
            var same2 = TextBlock("lit").Resources(r => r.Set("ButtonBackground", "#0078D4"));
            H.Check("Issue675_LiteralBrush_SameHex_ShallowEqual",
                Element.ShallowEquals(same1, same2));

            var diff = TextBlock("lit").Resources(r => r.Set("ButtonBackground", "#106EBE"));
            H.Check("Issue675_LiteralBrush_DiffHex_NotShallowEqual",
                !Element.ShallowEquals(same1, diff));

            return Task.CompletedTask;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed-MIDDLE (LIS reorder) patch re-resolve — #701 audit completion
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Issue #701 audit completion — the keyed-MIDDLE (LIS reorder) patch surface, the one
    /// reconciler skip/patch arm the #675 fixtures above did not cover. The keyed prefix/suffix
    /// arms (<see cref="KeyedChildSkipReResolves"/> / <see cref="KeyedSuffixChildSkipReResolves"/>)
    /// share <c>Element.CanSkipUpdate</c>, but a REORDERED keyed survivor is patched by
    /// <c>RealKeyedMiddleSink.Patch</c> instead, which has NO skip arm — it unconditionally routes
    /// every survivor through <c>UpdateChild</c> → <c>Update</c>, whose element-level shallow-skip
    /// re-applies <c>ApplyResourceOverrides</c>. So the middle path re-resolves a ThemeRef override
    /// by construction; this fixture pins that and is the teeth guarding against a future
    /// middle-path skip optimization that forgets to re-resolve.
    ///
    /// <para>Three keyed children <c>[anchor(a), themed(t), other(o)]</c> reorder to
    /// <c>[anchor(a), other(o), themed(t)]</c>: the keyed prefix consumes <c>a</c>, the suffix is
    /// empty (tail <c>o</c> vs <c>t</c> mismatch), and the <c>{t,o}</c> middle reorders, so
    /// <c>ReconcileKeyedMiddle</c>'s LIS pass patches the themed survivor via
    /// <c>RealKeyedMiddleSink.Patch</c> (<c>RunKeyedMiddleCore</c> calls <c>sink.Patch</c> for
    /// every survivor, LIS member or mover alike). Uses the file's deterministic source-brush
    /// mutation rather than a <c>RequestedTheme</c> toggle — an unreliable observable for the
    /// <c>ThemeRef.Resolve</c> snapshot (see the class remarks).</para>
    /// </summary>
    internal sealed class KeyedMiddleReorderReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var red = MakeBrush(30, 90, 150);
            var blue = MakeBrush(150, 90, 30);
            var srcDict = InstallSourceDict(SrcKey, red);
            try
            {
                // #721-style positive guard: the themed cell is structurally shallow-equal across
                // renders (its Key is matched separately via KeyMatch, not by ShallowEquals), so
                // its re-resolution hinges only on the middle path routing through Update — not an
                // incidental modifier diff that would force a full property update and mask a broken
                // middle-path re-resolve as a false-green.
                H.Check("Issue701_KeyedMiddle_CellSkipEligible", Element.ShallowEquals(
                    TextBlock("kmThemed").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))).WithKey("t"),
                    TextBlock("kmThemed").Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey))).WithKey("t")));

                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    bump = setN;
                    var anchor = TextBlock("kmAnchor").WithKey("a");
                    var themed = TextBlock("kmThemed")
                        .Resources(r => r.Set(TargetKey, Theme.Ref(SrcKey)))
                        .WithKey("t");
                    var other = TextBlock("kmOther").WithKey("o");
                    // n even: [a, t, o]; n odd: [a, o, t]. The {t,o} middle reorders — the keyed
                    // prefix consumes 'a', the suffix is empty — forcing ReconcileKeyedMiddle.
                    return (n % 2 == 0)
                        ? VStack(anchor, themed, other)
                        : VStack(anchor, other, themed);
                });

                await Harness.Render();
                var cell = H.FindControl<TextBlock>(t => t.Text == "kmThemed");
                var cellBefore = cell;
                int idxBefore = ChildIndex(cell);
                H.Check("Issue701_KeyedMiddle_MountResolvedRed",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), red));

                // Mutate the SOURCE brush, then trigger the reorder render.
                SetSource(srcDict, SrcKey, blue);
                bump!(1);
                await Harness.Render();

                cell = H.FindControl<TextBlock>(t => t.Text == "kmThemed");
                // Right-reason A: the control is REUSED in place (not remounted) — proves it was
                // patched by RealKeyedMiddleSink.Patch, not dropped + freshly mounted (a remount
                // would carry the new brush for the wrong reason).
                H.Check("Issue701_KeyedMiddle_ControlReusedInPlace",
                    ReferenceEquals(cellBefore, cell));
                // Right-reason B: the themed cell actually MOVED within its parent panel — proves
                // the keyed-MIDDLE reorder engaged, not an incidental prefix/suffix skip.
                int idxAfter = ChildIndex(cell);
                H.Check($"Issue701_KeyedMiddle_ReorderEngaged[{idxBefore}->{idxAfter}]",
                    idxBefore >= 0 && idxAfter >= 0 && idxBefore != idxAfter);
                // The payload: the themed survivor re-resolved its ThemeRef through the middle patch.
                H.Check("Issue701_KeyedMiddle_ReResolvedBlueOnMiddlePatch",
                    ReferenceEquals(ResourceBrush(cell, TargetKey), blue));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(srcDict);
            }
        }

        private static int ChildIndex(TextBlock? cell) =>
            cell?.Parent is Panel panel ? panel.Children.IndexOf(cell) : -1;
    }
}
