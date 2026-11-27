using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Theme bindings (FrameworkElement.SetThemeResourceBinding per ThemeRef property) and resource overrides.
internal static partial class ThemeBindingApplier
{
    /// <inheritdoc cref="Reconciler.ApplyThemeBindings(FrameworkElement, IReadOnlyDictionary{string, ThemeRef})"/>
    internal static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
        => ApplyThemeBindings(fe, bindings, modifiers: null, refresh: false, clearStale: true);

    /// <summary>
    /// What Reactor knows about one theme-bound property of a control (see
    /// <see cref="Reconciler.ReactorState.ThemeResourceBindings"/>).
    /// </summary>
    internal sealed class ThemeBindingRecord
    {
        /// <summary>The resource key the property is bound (or, while suppressed, declared) to.</summary>
        public string Key = "";

        /// <summary>
        /// The local value right after Reactor last installed the binding. A value read later
        /// that is still this object is Reactor's own, not a takeover by another writer.
        /// </summary>
        public object? Installed;

        /// <summary>
        /// An explicit brush modifier holds the property, possibly on a layer that shares the
        /// control (a keyed Memo, a flyout target). The key is kept so the binding can come back
        /// when that modifier goes away.
        /// </summary>
        public bool Suppressed;
    }

    /// <summary>
    /// Reconciler form of <see cref="Reconciler.ApplyThemeBindings(FrameworkElement, IReadOnlyDictionary{string, ThemeRef})"/>.
    /// <paramref name="modifiers"/> are the element's effective modifiers: an explicit brush
    /// modifier for the same property (<c>.Background(brush)</c> next to
    /// <c>.Background(ThemeRef)</c>) wins whatever the call order, as it did against the old
    /// Style. <paramref name="refresh"/> is set on a resource-refresh pass
    /// (<c>Theme.NotifyResourcesChanged</c>): a binding resolves its key when installed and only
    /// re-resolves on a theme change or reparent, so an edited dictionary entry is picked up by
    /// installing it again. A binding that this render's setters or element properties wrote
    /// over was already handed over (see <see cref="ReleaseReplacedThemeBindings"/>).
    /// <paramref name="clearStale"/> drops bound properties missing from
    /// <paramref name="bindings"/>, for direct callers. The reconciler passes false: it clears
    /// what an element dropped before the element's setters run
    /// (<see cref="ClearDroppedThemeBindings"/>), and another layer sharing the control may
    /// still declare the rest.
    /// </summary>
    internal static void ApplyThemeBindings(
        FrameworkElement fe,
        IReadOnlyDictionary<string, ThemeRef> bindings,
        ElementModifiers? modifiers,
        bool refresh = false,
        bool clearStale = false)
    {
        var state = Reconciler.TryGetReactorState(fe, out var existingState) ? existingState : null;
        var bound = state?.ThemeResourceBindings;

        if (clearStale && bound is { Count: > 0 })
        {
            List<string>? stale = null;
            foreach (var name in bound.Keys)
            {
                if (!bindings.ContainsKey(name))
                    (stale ??= new List<string>()).Add(name);
            }
            if (stale is not null)
            {
                foreach (var name in stale)
                {
                    var staleRecord = bound[name];
                    bound.Remove(name);
                    if (!staleRecord.Suppressed && !HasExplicitBrushModifier(name, modifiers)
                        && GetThemeBindingProperty(fe, name) is { } staleDp)
                        fe.ClearValue(staleDp);
                }
            }
        }

        foreach (var (name, themeRef) in bindings)
        {
            var dp = GetThemeBindingProperty(fe, name);
            if (dp is null) continue;

            ThemeBindingRecord? record = null;
            bound?.TryGetValue(name, out record);

            if (HasExplicitBrushModifier(name, modifiers))
            {
                // ApplyModifiers wrote the explicit brush over any binding we had.
                if (record is not null)
                {
                    record.Key = themeRef.ResourceKey;
                    record.Suppressed = true;
                }
                continue;
            }

            bool hasLocal = !ReferenceEquals(fe.ReadLocalValue(dp), DependencyProperty.UnsetValue);
            if (record is { Suppressed: true })
            {
                // A brush modifier on a layer sharing the control still holds the property.
                record.Key = themeRef.ResourceKey;
                if (hasLocal)
                    continue;
            }
            else if (record is not null)
            {
                // The binding is live. Installing it again would only replace a value written
                // over it outside a render (an InteractionStates brush), so re-bind only when
                // the key changed, the value was cleared, or a refresh asks for re-resolution.
                if (hasLocal && !refresh && string.Equals(record.Key, themeRef.ResourceKey, StringComparison.Ordinal))
                    continue;
                if (hasLocal && IsInteractionOverrideActive(fe, name))
                {
                    // An InteractionStates brush holds the property. Keep it, and let the state's
                    // restore install the binding for the current key.
                    record.Key = themeRef.ResourceKey;
                    continue;
                }
            }
            else if (hasLocal)
            {
                // A value Reactor did not bind beats the ThemeRef, as it beat the old Style. The
                // exception is an InteractionStates brush over a property with no explicit normal
                // value: record the ThemeRef, and the state's restore installs it.
                if (InteractionHoldsUnclaimed(fe, name))
                {
                    state ??= Reconciler.GetOrCreateReactorState(fe);
                    bound = state.ThemeResourceBindings ??= new Dictionary<string, ThemeBindingRecord>(StringComparer.Ordinal);
                    bound[name] = new ThemeBindingRecord { Key = themeRef.ResourceKey };
                }
                continue;
            }

            state ??= Reconciler.GetOrCreateReactorState(fe);
            if (!TryInstallThemeBinding(fe, state, name, dp, themeRef.ResourceKey, "ApplyThemeBindings")
                && record is { Suppressed: false } && state.ThemeResourceBindings is { } owned)
            {
                // The key changed to one that can't be resolved, or a refresh removed it: the old
                // key's brush must not stay as if it were the new key's. Release the property
                // (unless another writer holds it) and the record, so a later render retries.
                owned.Remove(name);
                if (ReferenceEquals(fe.ReadLocalValue(dp), record.Installed))
                    fe.ClearValue(dp);
                if (owned.Count == 0)
                    state.ThemeResourceBindings = null;
            }
            bound = state.ThemeResourceBindings;
        }
    }

    /// <summary>
    /// Installs the binding and records it on <paramref name="state"/>. An unknown key
    /// (<c>COMException</c>) or a value the property can't hold (<c>ArgumentException</c>) is
    /// logged, the record is left as it was for the caller to decide, and the render goes on.
    /// </summary>
    private static bool TryInstallThemeBinding(
        FrameworkElement fe, Reconciler.ReactorState state, string name, DependencyProperty dp, string key, string operation)
    {
        try
        {
            fe.SetThemeResourceBinding(dp, key);
        }
        catch (Exception ex) when (ex is ArgumentException or global::System.Runtime.InteropServices.COMException)
        {
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.SwallowedError(
                Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
                operation,
                ex);
            return false;
        }
        var bound = state.ThemeResourceBindings ??= new Dictionary<string, ThemeBindingRecord>(StringComparer.Ordinal);
        if (!bound.TryGetValue(name, out var record))
            bound[name] = record = new ThemeBindingRecord();
        record.Key = key;
        record.Installed = fe.ReadLocalValue(dp);
        record.Suppressed = false;
        return true;
    }

    /// <summary>
    /// The local values of a control's theme-bound properties, read by Update before the
    /// element's setters and element properties run (see <see cref="SnapshotThemeBindings"/>).
    /// </summary>
    internal readonly struct ThemeBindingSnapshot
    {
        internal readonly (string Name, object? Local)[]? Values;
        internal readonly ElementTheme Theme;

        internal ThemeBindingSnapshot((string Name, object? Local)[] values, ElementTheme theme)
        {
            Values = values;
            Theme = theme;
        }

        internal bool HasValues => Values is not null;
    }

    /// <summary>Reads <see cref="ThemeBindingSnapshot"/> for <paramref name="fe"/>; empty when nothing is bound.</summary>
    internal static ThemeBindingSnapshot SnapshotThemeBindings(FrameworkElement fe)
    {
        if (!Reconciler.TryGetReactorState(fe, out var state) || state.ThemeResourceBindings is not { Count: > 0 } bound)
            return default;
        var values = new (string Name, object? Local)[bound.Count];
        int i = 0;
        foreach (var name in bound.Keys)
            values[i++] = (name, GetThemeBindingProperty(fe, name) is { } dp ? fe.ReadLocalValue(dp) : null);
        return new ThemeBindingSnapshot(values, fe.ActualTheme);
    }

    /// <summary>
    /// Hands over every theme binding whose local value changed since <paramref name="snapshot"/>
    /// was read to something Reactor didn't install: a setter or element property wrote over it
    /// during this render. It wins, as a local value beat the old Style, and Reactor stops owning
    /// the property, so a later key change, refresh or ThemeRef removal leaves it alone. A binding
    /// a nested layer sharing the control installed in that window is Reactor's own and stays.
    /// Update calls this after the handler and before ApplyModifiers, so the control's own
    /// <c>RequestedTheme</c> modifier, and an ancestor's (applied after its children update),
    /// haven't run yet. A nested layer sharing the control (a flyout target) can still change
    /// its theme in that window, which re-resolves the binding natively; nothing is handed over
    /// then, since a changed value no longer proves a takeover.
    /// </summary>
    internal static void ReleaseReplacedThemeBindings(FrameworkElement fe, ThemeBindingSnapshot snapshot)
    {
        if (snapshot.Values is not { } values
            || !Reconciler.TryGetReactorState(fe, out var state)
            || state.ThemeResourceBindings is not { } bound
            || fe.ActualTheme != snapshot.Theme)
            return;
        foreach (var (name, before) in values)
        {
            if (!bound.TryGetValue(name, out var record) || record.Suppressed
                || GetThemeBindingProperty(fe, name) is not { } dp)
                continue;
            var local = fe.ReadLocalValue(dp);
            if (!ReferenceEquals(local, before) && !ReferenceEquals(local, record.Installed))
                bound.Remove(name);
        }
        if (bound.Count == 0)
            state.ThemeResourceBindings = null;
    }

    /// <summary>
    /// Issue #522 — clears the theme bindings an Update's element stopped declaring
    /// (in <paramref name="oldBindings"/> but not <paramref name="newBindings"/>), so e.g. the red
    /// Foreground from an Error arm does not bleed into a subsequent Loading / Data render on the
    /// recycled control. Runs before the element's setters and element properties, so a brush one
    /// of them writes in place of a dropped ThemeRef survives. Only bound properties are cleared:
    /// an author <c>Style</c>, properties Reactor never bound, and properties another layer
    /// sharing the control declares are left alone. A property an InteractionStates state or a
    /// brush modifier currently holds keeps that brush.
    /// </summary>
    internal static void ClearDroppedThemeBindings(
        FrameworkElement fe,
        IReadOnlyDictionary<string, ThemeRef> oldBindings,
        IReadOnlyDictionary<string, ThemeRef>? newBindings)
    {
        if (!Reconciler.TryGetReactorState(fe, out var state) || state.ThemeResourceBindings is not { } bound)
            return;
        foreach (var name in oldBindings.Keys)
        {
            if (newBindings is not null && newBindings.ContainsKey(name))
                continue;
            if (!bound.TryGetValue(name, out var record))
                continue;
            bound.Remove(name);
            if (!record.Suppressed && !IsInteractionOverrideActive(fe, name)
                && GetThemeBindingProperty(fe, name) is { } dp)
                fe.ClearValue(dp);
        }
        if (bound.Count == 0)
            state.ThemeResourceBindings = null;
    }

    /// <summary>
    /// Clears every property Reactor theme-bound on <paramref name="fe"/>, for a control returning
    /// to the pool (issue #522): a renter must not inherit the previous element's brushes.
    ///
    /// <para>Ownership lives in <see cref="Reconciler.ReactorState.ThemeResourceBindings"/> on the native
    /// control, so it survives a collected managed wrapper, unlike a
    /// <c>ConditionalWeakTable</c> keyed on the wrapper.</para>
    /// </summary>
    internal static void ClearThemeBindings(FrameworkElement fe)
    {
        if (!Reconciler.TryGetReactorState(fe, out var state) || state.ThemeResourceBindings is not { } bound)
            return;
        state.ThemeResourceBindings = null;
        foreach (var name in bound.Keys)
        {
            if (GetThemeBindingProperty(fe, name) is { } dp)
                fe.ClearValue(dp);
        }
    }

    /// <summary>Whether Reactor's theme binding currently owns <paramref name="property"/>.</summary>
    internal static bool IsThemeBound(FrameworkElement fe, string property) =>
        Reconciler.TryGetReactorState(fe, out var state)
        && state.ThemeResourceBindings is { } bound
        && bound.TryGetValue(property, out var record)
        && !record.Suppressed;

    /// <summary>
    /// Marks the theme bindings for the properties <paramref name="m"/> sets an explicit brush
    /// for as suppressed. A refresh or key change then leaves the modifier's brush alone, as the
    /// modifier's local value beat the old Style, and the binding comes back when the modifier
    /// goes away (<see cref="RestoreSuppressedThemeBinding"/>).
    /// </summary>
    internal static void SuppressModifierOwnedThemeBindings(FrameworkElement fe, ElementModifiers m)
    {
        if (m.Background is null && m.Foreground is null && m.BorderBrush is null)
            return;
        if (!Reconciler.TryGetReactorState(fe, out var state) || state.ThemeResourceBindings is not { Count: > 0 } bound)
            return;
        if (m.Background is not null && bound.TryGetValue("Background", out var bg)) bg.Suppressed = true;
        if (m.Foreground is not null && bound.TryGetValue("Foreground", out var fg)) fg.Suppressed = true;
        if (m.BorderBrush is not null && bound.TryGetValue("BorderBrush", out var bb)) bb.Suppressed = true;
    }

    /// <summary>
    /// After a brush modifier is removed and its property cleared: re-installs a theme binding the
    /// modifier was suppressing, for a ThemeRef declared on a layer sharing the control that won't
    /// necessarily update in this pass (a keyed Memo with an unchanged key).
    /// </summary>
    internal static void RestoreSuppressedThemeBinding(FrameworkElement fe, string property)
    {
        if (!Reconciler.TryGetReactorState(fe, out var state)
            || state.ThemeResourceBindings is not { } bound
            || !bound.TryGetValue(property, out var record)
            || !record.Suppressed)
            return;
        // Not suppressed from here on, even if the install fails: the next render retries.
        record.Suppressed = false;
        if (GetThemeBindingProperty(fe, property) is { } dp)
            TryInstallThemeBinding(fe, state, property, dp, record.Key, "RestoreSuppressedThemeBinding");
    }

    /// <summary>
    /// For an InteractionStates state that held a property while its brush modifier was removed:
    /// hands a suppressed theme binding back so the state's restore installs it.
    /// </summary>
    internal static void UnsuppressThemeBinding(FrameworkElement fe, string property)
    {
        if (Reconciler.TryGetReactorState(fe, out var state)
            && state.ThemeResourceBindings is { } bound
            && bound.TryGetValue(property, out var record))
            record.Suppressed = false;
    }

    /// <summary>
    /// Whether an InteractionStates state holds <paramref name="property"/> with its own brush,
    /// over a normal value that is no explicit brush (nothing, or a theme binding since dropped).
    /// </summary>
    private static bool InteractionHoldsUnclaimed(FrameworkElement fe, string property)
    {
        if (Reconciler.GetCompositionState(fe)?.InteractionTracker is not { } tracker
            || GetThemeBindingProperty(fe, property) is not { } dp)
            return false;
        var (active, unclaimed) = property switch
        {
            "Background" => (tracker.BackgroundOverrideApplied ? tracker.ActiveBackground : null,
                tracker.NormalBackground is not (Brush or Microsoft.UI.Xaml.Data.BindingBase)),
            "Foreground" => (tracker.ForegroundOverrideApplied ? tracker.ActiveForeground : null,
                tracker.NormalForeground is not (Brush or Microsoft.UI.Xaml.Data.BindingBase)),
            "BorderBrush" => (tracker.BorderBrushOverrideApplied ? tracker.ActiveBorderBrush : null,
                !tracker.NormalBorderBrushHasLocalValue || tracker.NormalBorderBrushThemeBound),
            _ => (null, false),
        };
        return active is not null && unclaimed && ReferenceEquals(fe.ReadLocalValue(dp), active);
    }

    private static bool IsInteractionOverrideActive(FrameworkElement fe, string property) =>
        Reconciler.GetCompositionState(fe)?.InteractionTracker is { } tracker && property switch
        {
            "Background" => tracker.BackgroundOverrideApplied,
            "Foreground" => tracker.ForegroundOverrideApplied,
            "BorderBrush" => tracker.BorderBrushOverrideApplied,
            _ => false,
        };

    /// <summary>
    /// Re-installs the theme binding on <paramref name="property"/> when Reactor owns it, for
    /// code that wrote a temporary value over it (InteractionStates returning to Normal).
    /// Returns false when the property is not theme-bound or the install failed, so the caller
    /// restores its own way.
    /// </summary>
    internal static bool TryRestoreThemeBinding(FrameworkElement fe, string property)
    {
        if (!Reconciler.TryGetReactorState(fe, out var state)
            || state.ThemeResourceBindings is not { } bound
            || !bound.TryGetValue(property, out var record)
            || record.Suppressed
            || GetThemeBindingProperty(fe, property) is not { } dp)
            return false;
        return TryInstallThemeBinding(fe, state, property, dp, record.Key, "RestoreThemeBinding");
    }

    private static bool HasExplicitBrushModifier(string property, ElementModifiers? m) => m is not null && property switch
    {
        "Background" => m.Background is not null,
        "Foreground" => m.Foreground is not null,
        "BorderBrush" => m.BorderBrush is not null,
        _ => false,
    };

    /// <summary>
    /// The dependency property a <see cref="ThemeRef"/> for <paramref name="property"/> binds on
    /// <paramref name="fe"/>, or null when the control has no such property. Which controls take
    /// which property is decided by <see cref="GetDependencyPropertyName"/>.
    /// </summary>
    internal static DependencyProperty? GetThemeBindingProperty(FrameworkElement fe, string property)
        => GetDependencyPropertyName(fe, property) switch
        {
            "Background" => fe switch
            {
                WinUI.Panel => WinUI.Panel.BackgroundProperty,
                WinUI.Control => WinUI.Control.BackgroundProperty,
                WinUI.Border => WinUI.Border.BackgroundProperty,
                _ => null,
            },
            "Foreground" => ForegroundModifier.GetProperty(fe),
            "BorderBrush" => fe switch
            {
                WinUI.Control => WinUI.Control.BorderBrushProperty,
                WinUI.Border => WinUI.Border.BorderBrushProperty,
                WinUI.Grid => WinUI.Grid.BorderBrushProperty,
                WinUI.StackPanel => WinUI.StackPanel.BorderBrushProperty,
                WinUI.RelativePanel => WinUI.RelativePanel.BorderBrushProperty,
                _ => null,
            },
            "PaneBackground" => fe is WinUI.SplitView ? WinUI.SplitView.PaneBackgroundProperty : null,
            _ => null,
        };

    // The common-modifier rows (Background, Foreground, BorderBrush) are generated from the
    // schema's Theme-role receivers (ThemeBindingApplier.Generated.cs, spec 067 layer 2); the
    // element-specific rows stay here.
    private static string? GetDependencyPropertyName(FrameworkElement fe, string property)
    {
        if (GetModifierDependencyPropertyName(fe, property) is { } modifierProperty)
            return modifierProperty;
        if (property == "PaneBackground" && fe is WinUI.SplitView)
            return "PaneBackground";
        return null;
    }

    /// <summary>
    /// The value of a <c>{ThemeResource}</c> setter for <paramref name="resourceKey"/>, escaped
    /// for an XML attribute, or null when the key cannot be written in a markup extension: it
    /// is empty, contains both quote characters, or contains characters an XML attribute does
    /// not keep as they are (tab, carriage return, line feed, or characters XML does not allow).
    /// </summary>
    /// <remarks>
    /// The key is quoted, so commas, braces, '=' and spaces survive markup-extension parsing.
    /// Inside the quotes a backslash escapes the next character, so it is doubled. A backslash
    /// does not escape the quote character itself, so a key containing ' is quoted with ".
    /// Used by the ThemeRef probe (<see cref="ThemeResourceProbe"/>). Theme modifiers no longer
    /// write markup: <see cref="FrameworkElement.SetThemeResourceBinding"/> takes the key as is.
    /// </remarks>
    internal static string? FormatThemeResourceReference(string? resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey) || !IsWritableAsAttribute(resourceKey)) return null;

        bool hasSingle = resourceKey.Contains('\'');
        if (hasSingle && resourceKey.Contains('"')) return null;
        char quote = hasSingle ? '"' : '\'';
        var reference = "{ThemeResource " + quote + resourceKey.Replace("\\", "\\\\") + quote + "}";
        return global::System.Security.SecurityElement.Escape(reference);
    }

    // XML 1.0 characters, minus tab, CR and LF, which an attribute value would turn into spaces.
    // Checked by hand rather than with XmlConvert, which would root System.Xml in every app that
    // uses a ThemeRef.
    private static bool IsWritableAsAttribute(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c < '\u0020' || c is '\uFFFE' or '\uFFFF')
                return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return false;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }
        return true;
    }

    // ── Lightweight Styling: per-control resource overrides ────────────────

    /// <summary>
    /// Removes every <c>Resources</c> key this control's resource overrides wrote, leaving keys
    /// set outside Reactor in place. <see cref="ElementPool.CleanElement"/> calls it on pool
    /// return, because mount applies overrides only for an element that declares some.
    /// </summary>
    internal static void RemoveManagedResourceKeys(FrameworkElement fe)
        => ApplyResourceOverrides(fe, null, null);

    /// <inheritdoc cref="Reconciler.ApplyResourceOverrides(FrameworkElement, Microsoft.UI.Reactor.Elements.ResourceOverrides, Microsoft.UI.Reactor.Elements.ResourceOverrides)"/>
    internal static void ApplyResourceOverrides(
        FrameworkElement fe,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? oldOverrides,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? newOverrides)
        => ApplyResourceOverrides(fe, oldOverrides, newOverrides, ElementTheme.Default);

    /// <inheritdoc cref="Reconciler.ApplyResourceOverrides(FrameworkElement, Microsoft.UI.Reactor.Elements.ResourceOverrides, Microsoft.UI.Reactor.Elements.ResourceOverrides, ElementTheme)"/>
    internal static void ApplyResourceOverrides(
        FrameworkElement fe,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? oldOverrides,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? newOverrides,
        ElementTheme effectiveTheme)
    {
        // Keys Reactor has set on this element, as opposed to keys set by XAML or other
        // sources: only these are removed when the overrides change. They live on the
        // native object (ReactorState.ManagedResourceKeys), because the managed wrapper can
        // be collected and re-projected between renders. A removal-only call on a control
        // that never got state has nothing of ours to strip, so it skips creating one.
        HashSet<string> managed;
        if (newOverrides is not null)
            managed = Reconciler.GetOrCreateReactorState(fe).ManagedResourceKeys ??= new HashSet<string>();
        else if (Reconciler.TryGetReactorState(fe, out var existing) && existing.ManagedResourceKeys is { } keys)
            managed = keys;
        else
            return;

        // Remove managed keys that are no longer present in the new overrides.
        // Issue #675 — drive removal off `managed` vs `newOverrides`, independent of
        // `oldOverrides` nullness. A control can carry Reactor-managed keys that did
        // NOT arrive via `oldOverrides` (e.g. a pooled/reused control, or the Mount
        // path which always passes oldOverrides==null): gating removal on
        // `oldOverrides is not null` would leak those stale keys into fe.Resources.
        // Removal only strips managed keys absent from newOverrides; the apply loop
        // below re-adds the current keys, so this never fights the apply path. At a
        // truly-fresh mount `managed` is empty and this is a no-op.
        if (managed.Count > 0)
        {
            var newKeys = newOverrides?.AllKeys.ToHashSet() ?? new HashSet<string>();
            foreach (var key in managed.ToArray())
            {
                if (!newKeys.Contains(key))
                {
                    fe.Resources?.Remove(key);
                    managed.Remove(key);
                }
            }
        }

        if (newOverrides is null) return;

        fe.Resources ??= new ResourceDictionary();

        // Apply literal resources
        foreach (var (key, value) in newOverrides.Literals)
        {
            fe.Resources[key] = value;
            managed.Add(key);
        }

        // Apply ThemeRef resources (resolved from Application.Current.Resources).
        // When the reconciler threaded an explicit ancestor-aware effective theme,
        // resolve against it: at mount fe is not yet parented, so its own visual-tree
        // walk cannot see an ancestor RequestedTheme and would otherwise fall back to
        // the app theme (the mount-theme bug). When no ambient theme is supplied
        // (Default — for example, direct public-API callers), keep the fe-based resolution.
        foreach (var (key, themeRef) in newOverrides.ThemeRefs)
        {
            var resolved = effectiveTheme != ElementTheme.Default
                ? ThemeRef.Resolve(themeRef.ResourceKey, effectiveTheme == ElementTheme.Dark)
                : ThemeRef.Resolve(themeRef.ResourceKey, fe);
            if (resolved is not null)
            {
                fe.Resources[key] = resolved;
                managed.Add(key);
            }
            else if (!newOverrides.Literals.ContainsKey(key) && managed.Remove(key))
            {
                // The source resource went away (e.g. a dictionary removed before
                // Theme.NotifyResourcesChanged): drop the brush this override wrote earlier
                // instead of leaving it stale. A literal for the same key, written above, stays.
                fe.Resources.Remove(key);
            }
        }
    }
}
