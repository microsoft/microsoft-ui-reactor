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
/// positional child-skip, keyed child-skip) plus the <c>ApplyResourceOverrides</c>
/// stale-key removal gate.
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
        if (fe?.Resources is { } res && res.ContainsKey(key))
            return res[key] as SolidColorBrush;
        return null;
    }

    // The app-level ResourceDictionary has a Source set (XamlControlsResources), so it
    // rejects direct local values. Register the ThemeRef source brush in our OWN
    // (Source-less) dictionary merged into Application.Current.Resources, which
    // ThemeRef.Resolve discovers via its TryResolveNonThemed MergedDictionaries scan.
    // Mutating the brush is then a write to our dictionary, never the sealed app one.
    private static ResourceDictionary InstallSourceDict(string key, SolidColorBrush initial)
    {
        var dict = new ResourceDictionary { [key] = initial };
        Application.Current.Resources.MergedDictionaries.Add(dict);
        return dict;
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
                Action<int>? bump = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
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
                srcDict[SrcKey] = blue;
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

                srcDict[SrcKey] = blue;
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

                srcDict[SrcKey] = blue;
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
}
