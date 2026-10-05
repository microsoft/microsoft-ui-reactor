namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// The component name Reactor's diagnostics report (<c>ReactorEventSource</c>
/// <c>RenderError</c>, <c>ComponentRenderStart</c>/<c>Stop</c>,
/// <c>EffectsFlushStart</c>/<c>Stop</c>, <c>ComponentUnmount</c>, and the
/// reconciler's <c>ILogger</c> messages).
///
/// <para><b>Why not <c>element.GetType().Name</c>.</b> A class component is carried by a
/// <see cref="ComponentElement"/> — and by <c>ComponentElement&lt;TProps&gt;</c> for
/// <c>Component&lt;T, TProps&gt;(…)</c> — so naming the <em>element</em> reports
/// <c>ComponentElement</c> / <c>ComponentElement`1</c> for every class component in
/// the app. The component is <see cref="ComponentElement.ComponentType"/> (or the live
/// instance's type, which wins after a hot-reload type swap).</para>
///
/// <para><b>Shape.</b> The type's own simple name, as before, for a non-generic
/// component (<c>Counter</c>), so existing traces and greps are unchanged. A generic
/// component renders its arity in C# form (<c>Counter&lt;Int32&gt;</c>) instead of the
/// metadata name (<c>Counter`1</c>). Function and memo components have no type of their
/// own and keep reporting <c>FuncElement</c> / <c>MemoElement</c>.</para>
///
/// <para><b>PII.</b> Output is built only from developer-authored type names — never
/// props, state, keys or paths — so it is safe on the ETW payload
/// (spec 044 §6.2.1).</para>
///
/// <para>Reflection-light (<c>Type.Name</c> and <c>Type.GetGenericArguments()</c>),
/// so it is trim- and AOT-safe.</para>
/// </summary>
internal static class ComponentNames
{
    /// <summary>
    /// Name for a component node: the live <paramref name="instance"/>'s type when there
    /// is one, else the component type an element declares, else the element's own type
    /// (<c>FuncElement</c>, <c>MemoElement</c>).
    /// </summary>
    internal static string For(Component? instance, Element? element)
    {
        if (instance is not null) return For(instance.GetType());
        if (element is ComponentElement componentElement) return For(componentElement.ComponentType);
        return element?.GetType().Name ?? "unknown";
    }

    /// <summary>Display name of a component type; see the class remarks for the shape.</summary>
    internal static string For(Type type)
    {
        if (!type.IsGenericType) return type.Name;

        // Generic names are built, so cache them: the name is asked for on every traced
        // render and unmount. A weak table, so it never roots a collectible or hot-reloaded type.
        return s_genericNames.GetValue(type, static t =>
        {
            var sb = new global::System.Text.StringBuilder();
            Append(sb, t);
            return sb.ToString();
        });
    }

    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<Type, string> s_genericNames = new();

    private static void Append(global::System.Text.StringBuilder sb, Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        sb.Append(tick >= 0 ? name.Substring(0, tick) : name);

        if (!type.IsGenericType) return;

        // For a nested generic the flat argument list also carries the declaring type's
        // arguments; this name only reports the ones it introduces itself.
        var args = type.GetGenericArguments();
        int own = tick >= 0 && int.TryParse(name.AsSpan(tick + 1), out var arity) ? arity : 0;
        if (own == 0) return;

        sb.Append('<');
        for (int i = args.Length - own; i < args.Length; i++)
        {
            if (i > args.Length - own) sb.Append(", ");
            Append(sb, args[i]);
        }
        sb.Append('>');
    }
}
