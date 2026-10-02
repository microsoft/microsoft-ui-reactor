using System.Runtime.CompilerServices;
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

    /// <summary>What was last written to a control, so an unchanged value skips the DP write.</summary>
    private sealed class Published(string? owner, string value)
    {
        public string? Owner { get; } = owner;
        public string Value { get; } = value;
    }

    private static readonly ConditionalWeakTable<UIElement, Published> s_published = new();

    /// <summary>
    /// The owner last published for <paramref name="control"/>. A shallow-skip refresh reuses
    /// it: a skipped control kept its place in the tree, so its owner did not change.
    /// </summary>
    internal static string? LastOwner(UIElement control)
        => s_published.TryGetValue(control, out var p) ? p.Owner : null;

    internal static void Write(UIElement control, string? owner, string value)
    {
        if (s_published.TryGetValue(control, out var p) && string.Equals(p.Value, value, StringComparison.Ordinal))
            return;
        s_published.AddOrUpdate(control, new Published(owner, value));
        control.SetValue(ReactorDiagnostics.SourceProperty, value);
    }

    /// <summary>
    /// Builds and writes the value for <paramref name="control"/>, realized from
    /// <paramref name="element"/>. <paramref name="component"/> is the live component when
    /// <paramref name="control"/> is a component wrapper; <paramref name="root"/> names the
    /// host's root component when it is a host's root content control.
    /// </summary>
    internal static void Publish(
        UIElement control,
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

        // Published paths are relative (XAML parity); CallSite keeps the full path.
        string? rel = null;
        SourceLocation? published = site is { } full && !string.IsNullOrEmpty(full.FilePath)
            ? new SourceLocation(
                global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ToPublishedPath(full.FilePath, out rel),
                full.LineNumber,
                full.ColumnNumber)
            : site;

        var value = ReactorSourceFormat.Build(
            published,
            owner,
            ReactorSourceFormat.KindOf(effective.GetType()),
            mounts,
            root,
            ReactorSourceFormat.KeyText(effective.Key ?? element.Key),
            site?.DeclaredName,
            hooks ?? rootHooks,
            rel);
        Write(control, owner, value);
    }
}
