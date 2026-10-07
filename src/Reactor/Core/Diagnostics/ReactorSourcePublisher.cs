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

    /// <summary>
    /// Adds the host-root fields (<c>root=</c>, and <c>hooks=</c> when the value has none) to a
    /// value already published for a host's root content control. Used when the root element
    /// is a <c>Memo(key, …)</c>: its factory output published itself when it mounted, and the
    /// factory must not run again just to describe it. Fields stay in grammar order; an
    /// existing <c>root=</c> is replaced. Returns <c>null</c> when nothing was published.
    /// </summary>
    internal static string? WithRoot(string? published, string rootName, string? rootHooks)
        => WithRoot(published, rootName, rootHooks, previousHostHooks: null, out _);

    /// <summary>
    /// <see cref="WithRoot(string?, string, string?)"/> for a host that may have added
    /// <c>hooks=</c> itself on an earlier pass: <paramref name="previousHostHooks"/> (what it
    /// added then) is removed first, so a remounted root with different hooks, or none, does
    /// not keep the previous root's. <paramref name="addedHooks"/> reports whether
    /// <paramref name="rootHooks"/> was added this time.
    /// </summary>
    internal static string? WithRoot(
        string? published, string rootName, string? rootHooks, string? previousHostHooks, out bool addedHooks)
    {
        addedHooks = false;
        if (published is null) return null;
        var fields = new List<string>(published.Split('|'));
        bool hadRoot = fields.RemoveAll(static f => f.StartsWith("root=", StringComparison.Ordinal)) > 0;
        // Only a value that still carries the host's root fields can carry hooks the host
        // added: a fresh output (a new memo key mounted new controls) has its own hooks only,
        // even when their text happens to match.
        if (hadRoot && !string.IsNullOrEmpty(previousHostHooks))
        {
            var stale = "hooks=" + ReactorSourceFormat.Escape(previousHostHooks);
            fields.RemoveAll(f => string.Equals(f, stale, StringComparison.Ordinal));
        }

        int insertAt = 1;
        for (int i = 1; i < fields.Count; i++)
        {
            var name = FieldName(fields[i]);
            if (name is "at" or "rel" or "owner" or "element" or "mounts") insertAt = i + 1;
        }
        fields.Insert(insertAt, "root=" + ReactorSourceFormat.Escape(rootName));

        if (!string.IsNullOrEmpty(rootHooks) && !fields.Exists(static f => f.StartsWith("hooks=", StringComparison.Ordinal)))
        {
            fields.Add("hooks=" + ReactorSourceFormat.Escape(rootHooks));
            addedHooks = true;
        }
        return string.Join('|', fields);
    }

    /// <summary>
    /// Replaces the <c>owner=</c> field of <paramref name="value"/> when it names
    /// <paramref name="previousOwner"/>; <c>null</c> when it names anyone else (or no one).
    /// </summary>
    internal static string? WithOwner(string value, string previousOwner, string owner)
    {
        var expected = "owner=" + ReactorSourceFormat.Escape(previousOwner);
        var fields = value.Split('|');
        for (int i = 1; i < fields.Length; i++)
        {
            if (!fields[i].StartsWith("owner=", StringComparison.Ordinal)) continue;
            if (!string.Equals(fields[i], expected, StringComparison.Ordinal)) return null;
            fields[i] = "owner=" + ReactorSourceFormat.Escape(owner);
            return string.Join('|', fields);
        }
        return null;
    }

    /// <summary>Whether a published value describes a component wrapper (<c>mounts=</c>).</summary>
    internal static bool IsComponentWrapper(string value)
        => value.Contains("|mounts=", StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="value"/> without the static facts that no longer hold, or <c>null</c>
    /// when all still do. After a hot-reload update every <c>name=</c> and <c>hooks=</c> is
    /// unknown; otherwise a value whose call site's file has become unattributable (another
    /// source-mapped assembly claims the same path with different facts) loses its
    /// <c>name=</c> and its render-function <c>hooks=</c> (class-component hooks are keyed by
    /// type, not location, and stay).
    /// </summary>
    internal static string? WithoutStaleFacts(string value)
    {
        bool hotReloaded = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.StaticFactsInvalidatedByHotReload;
        if (!hotReloaded)
        {
            if (ResolvePublishedValue(value) is not { } site
                || global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.IsFileAttributable(site.FilePath))
                return null;
        }
        var mounts = Field(value, "mounts");
        bool dropHooks = hotReloaded || mounts is "FuncElement" or "MemoElement";
        var fields = new List<string>(value.Split('|'));
        int removed = fields.RemoveAll(f => f.StartsWith("name=", StringComparison.Ordinal)
            || (dropHooks && f.StartsWith("hooks=", StringComparison.Ordinal)));
        return removed == 0 ? null : string.Join('|', fields);
    }

    /// <summary>The raw (still escaped) text of field <paramref name="name"/>, or <c>null</c>.</summary>
    internal static string? Field(string value, string name)
    {
        var marker = "|" + name + "=";
        int start = value.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = value.IndexOf('|', start);
        return end < 0 ? value.Substring(start) : value.Substring(start, end - start);
    }

    /// <summary>The unescaped <c>owner=</c> of a published value, or <c>null</c>.</summary>
    internal static string? Owner(string value)
        => Field(value, "owner") is { } owner
            ? owner.Replace("%7C", "|", StringComparison.Ordinal).Replace("%25", "%", StringComparison.Ordinal)
            : null;

    /// <summary>
    /// Whether <paramref name="current"/>, a control's published value, describes an element
    /// other than <paramref name="element"/>: another call site or another kind. A per-host
    /// <c>RegisterType</c> callback can return the control it mounted for a child element; that
    /// control's value is the child's (as its tag stays the child's), not the registration's.
    /// </summary>
    internal static bool DescribesAnotherElement(string current, Element element)
    {
        var mine = Format(element, owner: null);
        return !string.Equals(AtKey(current), AtKey(mine), StringComparison.Ordinal)
            || !string.Equals(Field(current, "element"), Field(mine, "element"), StringComparison.Ordinal);
    }

    private static string FieldName(string field)
    {
        int eq = field.IndexOf('=');
        return eq < 0 ? field : field.Substring(0, eq);
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
