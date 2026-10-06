using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Writes <see cref="ReactorDiagnostics.SourceProperty"/> on realized controls in
/// diagnostics mode. Every call site in the reconciler and hosts is guarded by
/// <c>ReactorFeatures.DevtoolsSupported &amp;&amp; IsEnabled</c>, in that order:
/// <list type="bullet">
///   <item><see cref="Hosting.ReactorFeatures.DevtoolsSupported"/> is the <c>Reactor.DevtoolsSupport</c> feature switch.
///   With <c>Trim="true"</c> (Reactor.targets' default) ILC/ILLink substitute it with the
///   configured constant, so in a trimmed or Native AOT app built without the switch every
///   guarded branch — and this class with it — is removed. Under JIT it is a static readonly
///   value the tier-1 JIT folds, so a Release app pays nothing per control.</item>
///   <item><see cref="IsEnabled"/> is the launch-time consent: <c>REACTOR_DIAGNOSTICS=1</c>.
///   A Debug build that was not launched for inspection publishes nothing.</item>
/// </list>
/// </summary>
internal static class ReactorSourcePublisher
{
    internal const string EnvironmentVariable = "REACTOR_DIAGNOSTICS";

    /// <summary>
    /// The <c>Reactor.DevtoolsSupport</c> build-time switch. A convenience for tests and
    /// fixtures only: production call sites guard on
    /// <see cref="Hosting.ReactorFeatures.DevtoolsSupported"/> directly, so that once the
    /// trimmer folds it to false nothing references this type and it is removed whole.
    /// </summary>
    internal static bool IsSupported => Hosting.ReactorFeatures.DevtoolsSupported;

    private static int s_enabled =
        IsEnabledByEnvironment(global::System.Environment.GetEnvironmentVariable(EnvironmentVariable)) ? 1 : 0;

    /// <summary>
    /// <c>REACTOR_DIAGNOSTICS=1</c> at launch. Settable for tests and for a host that embeds
    /// its own inspector; only takes effect where <see cref="IsSupported"/> is true.
    /// </summary>
    internal static bool IsEnabled
    {
        get => Volatile.Read(ref s_enabled) != 0;
        set => Volatile.Write(ref s_enabled, value ? 1 : 0);
    }

    /// <summary>Exactly <c>"1"</c> enables, like <c>REACTOR_SOURCEMAP</c>.</summary>
    internal static bool IsEnabledByEnvironment(string? value)
        => string.Equals(value, "1", StringComparison.Ordinal);

    /// <summary>
    /// No managed inspector agent can load into this process: Native AOT has no runtime to
    /// load one into. Decided once at startup (ILC compiles
    /// <see cref="global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"/>
    /// to a constant). Settable so tests can exercise both tagging paths in one JIT process.
    /// </summary>
    internal static bool NoManagedAgent { get; set; } =
        !global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>
    /// Publishing replaces the call-site-only element tag (see
    /// <c>Reconciler.SkipsCallSiteOnlyTags</c>): only when values are being published and
    /// nothing managed could read the tag instead.
    /// </summary>
    internal static bool SkipsCallSiteOnlyTags => IsEnabled && NoManagedAgent;

    /// <summary>
    /// The full call site behind a control's published value, for
    /// <c>ReactorSourceMap.GetSource</c> on a control that carries no element tag. Published
    /// paths are relative, so the value alone cannot name the file; every value is built
    /// from a cached shape whose full <see cref="SourceLocation"/> is recorded against its
    /// <c>at=</c> text. Returns <c>null</c> when nothing was published. When two call sites
    /// share the same <c>at=</c> text (see <see cref="IsAmbiguous"/>) the first one is
    /// returned: every control published after the collision keeps its element tag, so an
    /// untagged control with that text was published before it, for the first site.
    /// </summary>
    internal static SourceLocation? ResolvePublishedSource(UIElement control)
        => control.GetValue(ReactorDiagnostics.SourceProperty) is string value ? ResolvePublishedValue(value) : null;

    /// <summary>The full call site a published value was built for (see <see cref="ResolvePublishedSource"/>).</summary>
    internal static SourceLocation? ResolvePublishedValue(string value)
        => AtKey(value) is { } key && Cache.Sources.TryGetValue(key, out var site) ? site : null;

    private static int s_anyAmbiguous;

    /// <summary>
    /// Whether <paramref name="value"/>'s <c>at=</c> text is shared by different call sites
    /// (the same relative path, line and column from two assemblies, or file-name-only paths
    /// from two directories). Such a control keeps its element tag so <c>GetSource</c> stays
    /// exact. A static check first: collisions are rare, so normally this costs one read.
    /// </summary>
    internal static bool IsAmbiguous(string value)
        => Volatile.Read(ref s_anyAmbiguous) != 0
            && AtKey(value) is { } key
            && Cache.Ambiguous.ContainsKey(key);

    private static void RecordSource(string atKey, SourceLocation site)
    {
        if (Cache.Sources.TryAdd(atKey, site)
            || !Cache.Sources.TryGetValue(atKey, out var first)
            || first == site)
            return;
        Cache.Ambiguous.TryAdd(atKey, 0);
        Volatile.Write(ref s_anyAmbiguous, 1);
    }

    /// <summary>
    /// The <c>at=</c> field of a published value, with its <c>rel=</c> marker when present
    /// (<c>at=Views/Card.cs:12:9|rel=root</c>): the part that identifies the call site.
    /// </summary>
    internal static string? AtKey(string value)
    {
        int start = value.IndexOf("|at=", StringComparison.Ordinal);
        if (start < 0) return null;
        start++;
        int end = value.IndexOf('|', start);
        if (end >= 0 && string.CompareOrdinal(value, end, "|rel=", 0, 5) == 0)
            end = value.IndexOf('|', end + 5);
        return end < 0 ? value.Substring(start) : value.Substring(start, end - start);
    }

    // No per-control bookkeeping: a side table keyed by the managed UIElement would be keyed by
    // a CsWinRT wrapper, which is collected and re-created for the same native control across
    // GCs (so entries silently vanish), and would cost memory on every control. The reconciler
    // instead decides when to (re)publish from the old/new elements (see IdentityChanged).

    /// <summary>
    /// Whether an in-place update can change the published value. The value is a function of
    /// the call site (location, declared name, render-function hooks), the key, the element
    /// kind and, for components, the component type; the owner of a control updated in place
    /// cannot change. An unchanged update therefore skips building and writing entirely.
    /// </summary>
    internal static bool IdentityChanged(Element oldEl, Element newEl)
    {
        Describe(oldEl, out var oldSite, out var oldKey, out var oldEffective);
        Describe(newEl, out var newSite, out var newKey, out var newEffective);
        return oldSite != newSite
            || !Equals(oldKey, newKey)
            || oldEffective.GetType() != newEffective.GetType()
            || (oldEffective is ComponentElement oc && newEffective is ComponentElement nc && oc.ComponentType != nc.ComponentType);
    }

    private static void Describe(Element element, out SourceLocation? site, out object? key, out Element effective)
    {
        while (element is ModifiedElement modified) element = modified.Inner;
        effective = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.UnwrapDecorators(element) ?? element;
        site = effective.CallSite ?? element.CallSite;
        key = effective.Key ?? element.Key;
    }

    /// <summary>
    /// Builds and writes the value for <paramref name="control"/>, realized from
    /// <paramref name="element"/>. <paramref name="component"/> is the live component when
    /// <paramref name="control"/> is a component wrapper; <paramref name="root"/> names the
    /// host's root component when it is a host's root content control. Returns the value;
    /// when it equals <paramref name="unchangedIf"/> the DP write is skipped.
    /// </summary>
    internal static string Publish(
        UIElement control,
        Element element,
        string? owner,
        Component? component = null,
        string? root = null,
        string? rootHooks = null,
        string? unchangedIf = null)
    {
        var value = Format(element, owner, component, root, rootHooks);
        if (!string.Equals(value, unchangedIf, StringComparison.Ordinal))
            control.SetValue(ReactorDiagnostics.SourceProperty, value);
        return value;
    }

    /// <summary>The value <see cref="Publish"/> writes, from the same inputs.</summary>
    internal static string Format(
        Element element,
        string? owner,
        Component? component = null,
        string? root = null,
        string? rootHooks = null)
    {
        while (element is ModifiedElement modified) element = modified.Inner;

        // A target-wrapping decorator realizes its target's control; describe that.
        var effective = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.UnwrapDecorators(element) ?? element;
        var site = effective.CallSite ?? element.CallSite;

        string? mounts = null;
        string? hooks = null;
        switch (effective)
        {
            case ComponentElement componentElement:
                mounts = ReactorSourceFormat.ComponentName(component, componentElement);
                hooks = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetComponentHooks(
                    component?.GetType() ?? componentElement.ComponentType);
                break;
            case FuncElement or MemoElement:
                mounts = effective.GetType().Name;
                hooks = site is { } s
                    ? global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetRenderFunctionHooks(s)
                    : null;
                break;
        }

        string? name = site?.DeclaredName;
        var parts = Cache.Parts.GetOrAdd(
            new ValueShape(site, owner, effective.GetType(), mounts, root, name, hooks ?? rootHooks),
            static shape => ValueParts.Create(shape));
        var keyText = ReactorSourceFormat.KeyText(effective.Key ?? element.Key);
        return keyText is null
            ? parts.Full
            : string.Concat(parts.Head, "|key=", ReactorSourceFormat.Escape(keyText), parts.Tail);
    }

    // Everything in a value except the key comes from the program's static structure (call
    // site, component and element types, the source map's names and hooks), so the formatted
    // text is cached per shape: the cache is bounded by the app's call sites, the per-control
    // work is a lookup plus (for keyed elements) one concat, and unkeyed controls with the
    // same shape share one string instance.
    private readonly record struct ValueShape(
        SourceLocation? Site, string? Owner, Type Kind, string? Mounts, string? Root, string? Name, string? Hooks);

    // In a holder class: the guard on every reconciler call site reads IsEnabled, which runs this
    // type's static initializer even when publishing is off; the cache is only allocated on first use.
    private static class Cache
    {
        internal static readonly global::System.Collections.Concurrent.ConcurrentDictionary<ValueShape, ValueParts> Parts = new();

        /// <summary><c>at=</c> text → the full call site first published with it.</summary>
        internal static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, SourceLocation> Sources =
            new(StringComparer.Ordinal);

        /// <summary><c>at=</c> texts published by more than one call site.</summary>
        internal static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, byte> Ambiguous =
            new(StringComparer.Ordinal);
    }

    private sealed class ValueParts(string head, string tail)
    {
        /// <summary>Fields before <c>key</c> (<c>v</c> … <c>root</c>).</summary>
        public string Head { get; } = head;

        /// <summary>Fields after <c>key</c> (<c>name</c>, <c>hooks</c>), each with its leading <c>|</c>.</summary>
        public string Tail { get; } = tail;

        /// <summary>The whole value for an unkeyed element.</summary>
        public string Full { get; } = head + tail;

        public static ValueParts Create(ValueShape shape)
        {
            // Published paths are relative (XAML parity); CallSite keeps the full path.
            string? rel = null;
            SourceLocation? published = shape.Site is { } full && !string.IsNullOrEmpty(full.FilePath)
                ? new SourceLocation(
                    global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath(full.FilePath, out rel),
                    full.LineNumber,
                    full.ColumnNumber)
                : shape.Site;
            var head = ReactorSourceFormat.Build(
                published, shape.Owner, ReactorSourceFormat.KindOf(shape.Kind), shape.Mounts, shape.Root, rel: rel);
            if (shape.Site is { } site && AtKey(head) is { } atKey)
                RecordSource(atKey, site);
            // Build's fixed "v=1" prefix, then only name/hooks: exactly the fields after key.
            var tail = ReactorSourceFormat.Build(null, null, string.Empty, name: shape.Name, hooks: shape.Hooks)
                .Substring(("v=" + ReactorSourceFormat.Version).Length);
            return new ValueParts(head, tail);
        }
    }
}
