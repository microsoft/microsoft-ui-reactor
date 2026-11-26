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

// Theme bindings (synthesized {ThemeResource} Styles), the style cache, and resource overrides.
internal static class ThemeBindingApplier
{
    // ── Style cache: avoids redundant XamlReader.Load() for identical theme binding sets ──
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, Style> _styleCache = new();

    // Issue #522 — per-control marker holding the themed Style instance
    // Reactor most recently applied via ApplyThemeBindings. Used by
    // ClearThemeBindings to decide whether the control's current Style is
    // still ours (vs. replaced by an author Setter) without depending on
    // _styleCache contents — the cache can be cleared at any time and would
    // otherwise create a window where the in-place clear silently fails.
    // Attached DPs are RCW-safe (the value is stored in the underlying
    // DependencyObject's effective value table, accessed via COM SetValue/
    // GetValue) — unlike ConditionalWeakTable, which keys on managed
    // instance identity and desyncs when WinUI hands out duplicate RCWs.
    private static readonly DependencyProperty ReactorAppliedThemeStyleProperty =
        DependencyProperty.RegisterAttached(
            "ReactorAppliedThemeStyle",
            typeof(Style),
            typeof(Reconciler),
            new PropertyMetadata(null));

    // Stamped on an app base Style: the id that keys the themed Styles built BasedOn it.
    private static readonly DependencyProperty ReactorBaseStyleIdProperty =
        DependencyProperty.RegisterAttached(
            "ReactorBaseStyleId",
            typeof(int),
            typeof(Reconciler),
            new PropertyMetadata(0));

    private static int _nextBaseStyleId;

    // Themed Styles built BasedOn an app Style, keyed "baseId#" + BuildCacheKey. Bounded: a base
    // Style created on every render gets a new id each time, so on reaching the cap the cache
    // starts over rather than staying full of entries no render asks for again.
    private const int BasedStyleCacheCap = 256;
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, Style> _basedStyleCache = new();

    // Ids of app base Styles a themed Style could not be built on (a TargetType that is not a
    // base of the control's WinUI type, such as a Style for an app subclass). Remembered so later
    // renders skip straight to the unbased Style instead of failing the same way each time.
    // Bounded and reset like _basedStyleCache.
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<int, byte> _incompatibleBaseStyleIds = new();

    /// <summary>
    /// Builds a deterministic cache key for a style based on its target type and
    /// the set of ThemeRef bindings. Keys are sorted by property name so that
    /// dictionaries with the same entries in different enumeration order produce
    /// the same key.
    /// </summary>
    internal static string BuildCacheKey(string targetType, IReadOnlyDictionary<string, ThemeRef> bindings)
    {
        // Format: "TargetType|Prop1=Key1|Prop2=Key2" with properties sorted by Ordinal. A resource
        // key may itself contain '|' or '=' (they are quoted in the setter), so those and the
        // escape character are backslash-escaped: otherwise { Background = "A|Foreground=B" }
        // and { Background = "A", Foreground = "B" } would share a key and a cached Style.
        var sortedKeys = bindings.Keys.ToArray();
        Array.Sort(sortedKeys, StringComparer.Ordinal);
        var sb = new global::System.Text.StringBuilder(targetType);
        foreach (var key in sortedKeys)
        {
            sb.Append('|');
            AppendCacheKeyPart(sb, key);
            sb.Append('=');
            AppendCacheKeyPart(sb, bindings[key].ResourceKey);
        }
        return sb.ToString();
    }

    private static void AppendCacheKeyPart(global::System.Text.StringBuilder sb, string? part)
    {
        if (part is null) return;
        if (part.AsSpan().IndexOfAny('\\', '|', '=') < 0)
        {
            sb.Append(part);
            return;
        }
        foreach (var c in part)
        {
            if (c is '\\' or '|' or '=') sb.Append('\\');
            sb.Append(c);
        }
    }

    /// <summary>
    /// Clears the compiled style cache. Called on theme change as conservative
    /// memory cleanup — not a correctness requirement since {ThemeResource}
    /// setters are live-resolved by WinUI.
    /// </summary>
    internal static void ClearStyleCache()
    {
        _styleCache.Clear();
        _basedStyleCache.Clear();
        _incompatibleBaseStyleIds.Clear();
    }

    // Test seam: whether Reactor's themed-Style ownership marker is set on a control.
    internal static bool HasThemeStyleMarkerForTests(FrameworkElement fe)
        => fe.GetValue(ReactorAppliedThemeStyleProperty) is Style;

    /// <inheritdoc cref="Reconciler.ApplyThemeBindings(FrameworkElement, IReadOnlyDictionary{string, ThemeRef})"/>
    internal static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
    {
        // A Style the app put on the control (a .Set setter, .ApplyStyle, a named-style fluent
        // such as .AccentButton(), a typography factory such as Title(...)) is kept: the themed
        // Style is built BasedOn it rather than replacing it. When fe.Style is the themed Style
        // Reactor wrote last time, its base is its BasedOn, so re-applying never chains another
        // level. BasedOn cannot bring back an implicit (keyless) style, but an explicit Style
        // had already replaced that.
        var authorBase = ResolveAuthorBaseStyle(fe);
        int? authorBaseId = authorBase is null ? null : GetOrStampBaseStyleId(authorBase);
        if (authorBaseId is { } knownId && _incompatibleBaseStyleIds.ContainsKey(knownId))
        {
            authorBase = null;
            authorBaseId = null;
        }
        var baseStyle = authorBase ?? GetRequiredFrameworkBaseStyle(fe);
        int? baseId = authorBase is not null ? authorBaseId
            : baseStyle is not null ? GetOrStampBaseStyleId(baseStyle) : null;

        // BasedOn requires the base's TargetType to equal or be a base of the derived one. A
        // Style the control accepted targets its type or a base of it, which for any WinUI type
        // is a base of the control's first WinUI type. The loose mapping is kept when there is
        // no base, so those Styles stay shared across control types.
        var targetType = baseStyle is null ? GetStyleTargetType(fe) : GetExactStyleTargetType(fe);
        if (targetType is null) return;

        var cacheKey = BuildCacheKey(targetType, bindings);
        string? basedCacheKey = baseId is { } id
            ? id.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "#" + cacheKey
            : null;

        // Cache hit: reuse the previously compiled Style
        if (baseStyle is null && _styleCache.TryGetValue(cacheKey, out var cachedStyle))
        {
            ApplyStyleToElement(fe, cachedStyle, null);
            return;
        }
        if (basedCacheKey is not null && _basedStyleCache.TryGetValue(basedCacheKey, out var cachedBasedStyle))
        {
            if (!TryApplyBasedStyle(fe, cachedBasedStyle, baseStyle!))
                RetryWithoutAuthorBase(fe, bindings, authorBase, authorBaseId);
            return;
        }

        // Cache miss: build XAML, parse, and cache
        var setters = new global::System.Text.StringBuilder();
        foreach (var (property, themeRef) in bindings)
        {
            var dp = GetDependencyPropertyName(fe, property);
            if (dp is null) continue;
            if (FormatThemeResourceReference(themeRef.ResourceKey) is not { } reference)
            {
                WarnUnrepresentableThemeKey(property, themeRef.ResourceKey);
                continue;
            }
            setters.Append("<Setter Property='").Append(dp).Append("' Value=\"").Append(reference).Append("\"/>");
        }

        // No property can be bound (every key was unrepresentable): take off a themed Style a
        // previous render applied, so a property whose key just became unrepresentable is left
        // unset rather than keeping the old brush. The app's own Style is restored, not cleared.
        if (setters.Length == 0)
        {
            ClearThemeBindings(fe);
            return;
        }

        Style style;
        try
        {
            var xaml =
                $"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{targetType}'>" +
                setters.ToString() +
                "</Style>";
            style = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
            if (baseStyle is not null)
                style.BasedOn = baseStyle;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // A parse failure (for example a resource key WinUI can't find) says nothing about the
            // base: BasedOn is only validated when the Style is applied. So the base is not marked
            // incompatible here, and a corrected key on a later render still builds on it.
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.SwallowedError(
                Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
                "ApplyThemeBindings",
                ex);
            return;
        }

        if (baseStyle is null)
        {
            _styleCache.TryAdd(cacheKey, style);
            try
            {
                ApplyStyleToElement(fe, style, null);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.SwallowedError(
                    Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
                    "ApplyThemeBindings",
                    ex);
            }
            return;
        }

        if (!TryApplyBasedStyle(fe, style, baseStyle))
        {
            RetryWithoutAuthorBase(fe, bindings, authorBase, authorBaseId);
            return;
        }

        // Styles built for a base are cached per base identity. A base Style created anew on every
        // render gets a new id each time, so on reaching the cap the cache starts over instead of
        // staying full of entries nothing asks for again. Without an id the Style is built per
        // apply, which is still correct.
        if (basedCacheKey is not null)
        {
            if (_basedStyleCache.Count >= BasedStyleCacheCap)
                _basedStyleCache.Clear();
            _basedStyleCache.TryAdd(basedCacheKey, style);
        }
    }

    /// <summary>
    /// Applies a themed Style built BasedOn <paramref name="baseStyle"/>. WinUI validates
    /// BasedOn only when the Style is applied, so a base whose TargetType is not a base of the
    /// control's WinUI type fails here. The control is then put back on
    /// <paramref name="baseStyle"/> when it accepts that write, and the failure is logged.
    /// </summary>
    private static bool TryApplyBasedStyle(FrameworkElement fe, Style style, Style baseStyle)
    {
        try
        {
            ApplyStyleToElement(fe, style, baseStyle);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.SwallowedError(
                Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
                "ApplyThemeBindings",
                ex);
            try
            {
                // The write that failed may have been this same one (a loaded SplitView rejects
                // most Styles), so the rollback is guarded too: nothing here may escape into
                // reconcile.
                if (!ReferenceEquals(fe.Style, baseStyle))
                    fe.Style = baseStyle;
                fe.ClearValue(ReactorAppliedThemeStyleProperty);
            }
            catch (Exception rollbackEx) when (rollbackEx is not OutOfMemoryException and not StackOverflowException)
            {
                Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.SwallowedError(
                    Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
                    "ApplyThemeBindings",
                    rollbackEx);
            }
            return false;
        }
    }

    /// <summary>
    /// After a themed Style could not be built on the app's <paramref name="authorBase"/>,
    /// remembers that base as incompatible and applies the ThemeRefs without it, which replaces
    /// the app's Style as Reactor did before Styles were kept. Without an id (the base rejected
    /// the stamp) nothing can be remembered, so the app's Style is left as it is instead of
    /// failing again on every render.
    /// </summary>
    private static void RetryWithoutAuthorBase(
        FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings, Style? authorBase, int? authorBaseId)
    {
        if (authorBase is null || authorBaseId is not { } id)
            return;
        if (_incompatibleBaseStyleIds.Count >= BasedStyleCacheCap)
            _incompatibleBaseStyleIds.Clear();
        if (!_incompatibleBaseStyleIds.TryAdd(id, 0))
            return;
        WarnIncompatibleBaseStyle(fe);
        ApplyThemeBindings(fe, bindings);
    }

    private static void WarnIncompatibleBaseStyle(FrameworkElement fe)
    {
        if (!Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.IsWarningEnabled)
            return;
        Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(
            Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
            "ApplyThemeBindings",
            $"A ThemeRef on a {fe.GetType().Name} cannot keep the control's Style, because the Style's TargetType " +
            "is not a WinUI type the control derives from, so the themed Style replaces it. Target the WinUI " +
            "base type in that Style, or set the brush in the Style itself.");
    }

    /// <summary>
    /// The Style the app put on <paramref name="fe"/>, or null when it has none. When
    /// <c>fe.Style</c> is the themed Style Reactor applied, that is the base it was built on.
    /// </summary>
    private static Style? ResolveAuthorBaseStyle(FrameworkElement fe)
    {
        if (fe.Style is not { } current)
            return null;
        if (fe.GetValue(ReactorAppliedThemeStyleProperty) is Style owned && ReferenceEquals(current, owned))
            return owned.BasedOn;
        return current;
    }

    /// <summary>
    /// For a control that cannot take a Style that doesn't carry its default look, that
    /// default Style, so the themed Style is built on it like on an author base. A loaded
    /// <c>SplitView</c> fails with E_INVALIDARG on any write to <c>Style</c> (an empty Style, or
    /// clearing it) unless the new Style is BasedOn <c>DefaultSplitViewStyle</c>, which also makes
    /// <see cref="ClearThemeBindings"/> restore that Style rather than clear it.
    /// </summary>
    private static Style? GetRequiredFrameworkBaseStyle(FrameworkElement fe)
        => fe is WinUI.SplitView
            && ResourceLookup.TryFind<Style>(Application.Current?.Resources, "DefaultSplitViewStyle", out var style)
            ? style
            : null;

    /// <summary>
    /// The name of the first WinUI type in <paramref name="fe"/>'s type hierarchy, which XAML
    /// can resolve in the default namespace. Null for a control with no WinUI ancestor type.
    /// </summary>
    private static string? GetExactStyleTargetType(FrameworkElement fe)
    {
        for (var type = fe.GetType(); type is not null; type = type.BaseType)
        {
            if (type.Namespace is { } ns && ns.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal))
                return type.Name;
        }
        return null;
    }

    /// <summary>
    /// A process-unique id stamped on <paramref name="style"/>, which keys the Styles built
    /// BasedOn it. Stored as an attached DP on the Style itself rather than in a table keyed by
    /// the managed wrapper, which WinUI can hand out more than once for the same native object.
    /// Null when the Style rejects the write.
    /// </summary>
    private static int? GetOrStampBaseStyleId(Style style)
    {
        try
        {
            if (style.GetValue(ReactorBaseStyleIdProperty) is int id && id != 0)
                return id;
            var next = global::System.Threading.Interlocked.Increment(ref _nextBaseStyleId);
            style.SetValue(ReactorBaseStyleIdProperty, next);
            return next;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
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
    /// Shared with the ThemeRef probe (<see cref="ThemeResourceProbe"/>).
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

    // A ThemeRef is applied on every render, so one bad key would warn on every render. Warn once
    // per key; past the cap, or for an overlong key, keep warning without remembering, so a
    // data-driven caller cannot retain unbounded strings and a typo is never hidden.
    private const int WarnedThemeKeysCap = 256;
    private const int MaxWarnedThemeKeyChars = 256;
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warnedThemeKeys = new();

    private static void WarnUnrepresentableThemeKey(string property, string? resourceKey)
    {
        var key = resourceKey ?? string.Empty;
        if (_warnedThemeKeys.ContainsKey(key)
            || !Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.IsWarningEnabled)
            return;
        var bounded = key.Length <= MaxWarnedThemeKeyChars;
        if (bounded && _warnedThemeKeys.Count < WarnedThemeKeysCap && !_warnedThemeKeys.TryAdd(key, 0))
            return;

        // Spec 044 §6.2.1: a resource key may go on the ETW payload, length-bounded. Control
        // characters (the reason a key may be rejected) are shown escaped so the warning stays
        // on one line.
        var shown = bounded ? key : string.Concat(key.AsSpan(0, MaxWarnedThemeKeyChars), "…");
        var safe = new global::System.Text.StringBuilder(shown.Length);
        foreach (var c in shown)
        {
            if (c < '\u0020') safe.Append("\\u").Append(((int)c).ToString("X4", global::System.Globalization.CultureInfo.InvariantCulture));
            else safe.Append(c);
        }
        Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(
            Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
            "ApplyThemeBindings",
            $"ThemeRef key '{safe}' for {property} cannot be written as a {{ThemeResource}} reference (it is empty, " +
            "contains both quote characters, or contains a tab, line break or character XML does not allow), so " +
            $"{property} is left unset. Rename the resource key, or resolve it with ThemeRef.Resolve and pass the Brush.");
    }

    /// <summary>
    /// Issue #522 — counterpart to <see cref="ApplyThemeBindings"/>. When an
    /// element on a recycled control transitions from having
    /// <see cref="Element.ThemeBindings"/> to none, drop the previously
    /// applied themed Style so e.g. the red Foreground from an Error arm
    /// does not bleed into a subsequent Loading / Data render.
    ///
    /// <para>Ownership is tracked via the
    /// <c>ReactorAppliedThemeStyleProperty</c> attached DP, which stores the
    /// Style instance we wrote on the most recent <see cref="ApplyThemeBindings"/>
    /// call. The Style is dropped only when <c>fe.Style</c> still references the
    /// same instance — preserving an author Setter (e.g.
    /// <c>.Set(c =&gt; c.Style = mine)</c>) that may have replaced our Style in
    /// between. When the themed Style was built <c>BasedOn</c> the app's own
    /// Style, that base is restored instead of clearing <c>Style</c>.</para>
    ///
    /// <para>This avoids per-FrameworkElement tracking via
    /// <c>ConditionalWeakTable</c> (fragile under RCW churn) and is robust to
    /// the global <c>_styleCache</c> being cleared between apply and clear —
    /// the marker holds the actual Style reference, not a cache lookup key.</para>
    /// </summary>
    internal static void ClearThemeBindings(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAppliedThemeStyleProperty) is not Style ownedStyle)
            return;
        if (ReferenceEquals(fe.Style, ownedStyle))
        {
            // Hand the control back to the app's own Style when the themed one was built on it.
            if (ownedStyle.BasedOn is { } baseStyle)
                fe.Style = baseStyle;
            else
                fe.ClearValue(FrameworkElement.StyleProperty);
        }
        fe.ClearValue(ReactorAppliedThemeStyleProperty);
    }

    /// <summary>
    /// Applies a cached style to an element. Clears any existing style first to
    /// force WinUI to re-evaluate <c>{ThemeResource}</c> setters against the
    /// element's current effective theme (which may have changed due to a parent's
    /// <see cref="FrameworkElement.RequestedTheme"/> override).
    /// </summary>
    private static void ApplyStyleToElement(FrameworkElement fe, Style cachedStyle, Style? baseStyle)
    {
        // Clearing first forces WinUI to process the subsequent set as a
        // genuine change, re-resolving {ThemeResource} values. Without this,
        // re-applying the same cached Style reference is a no-op. With an app
        // base Style, step through the base instead of null: dropping to the
        // default style would swap a Template the base sets and rebuild it.
        if (baseStyle is not null)
        {
            if (!ReferenceEquals(fe.Style, baseStyle))
                fe.Style = baseStyle;
        }
        else if (fe.Style is not null)
        {
            fe.Style = null;
        }
        fe.Style = cachedStyle;

        // Track ownership for the symmetric ClearThemeBindings path (issue
        // #522). Always overwrite so an A → B ThemeBindings transition
        // updates the marker to the new Style.
        fe.SetValue(ReactorAppliedThemeStyleProperty, cachedStyle);
    }

    private static string? GetStyleTargetType(FrameworkElement fe) => fe switch
    {
        WinUI.Border => "Border",
        WinUI.StackPanel => "StackPanel",
        WinUI.Grid => "Grid",
        WinUI.RelativePanel => "RelativePanel",
        WinUI.Button => "Button",
        WinUI.TextBox => "TextBox",
        TextBlock => "TextBlock",
        WinUI.SplitView => "SplitView",
        WinUI.ContentControl => "ContentControl",
        WinUI.Panel => "Panel",
        WinUI.Control => "Control",
        _ => fe.GetType().Name,
    };

    private static string? GetDependencyPropertyName(FrameworkElement fe, string property)
    {
        if (property == "Background" && (fe is WinUI.Panel || fe is WinUI.Control || fe is WinUI.Border))
            return "Background";
        if (property == "Foreground" && (fe is WinUI.Control || fe is TextBlock || fe is WinUI.RichTextBlock))
            return "Foreground";
        if (property == "BorderBrush" && (fe is WinUI.Control || fe is WinUI.Border
            || fe is WinUI.Grid || fe is WinUI.StackPanel || fe is WinUI.RelativePanel))
            return "BorderBrush";
        if (property == "PaneBackground" && fe is WinUI.SplitView)
            return "PaneBackground";
        return null;
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
