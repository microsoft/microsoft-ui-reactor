using System.Text;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Builds the value of <see cref="ReactorDiagnostics.SourceProperty"/> — the per-control
/// string an out-of-process inspector reads through XamlDiagnostics with no managed agent
/// (Native AOT included).
///
/// <para><b>Grammar (v1).</b> <c>|</c>-separated <c>name=value</c> fields, in this order,
/// optional fields omitted when unknown:</para>
/// <code>
/// v=1
/// |at=&lt;path&gt;:&lt;line&gt;[:&lt;column&gt;]   DSL call site (column when the source map has one); the path
///                                    is RELATIVE, never an absolute developer path (see rel)
/// |rel=root|0                         how at's path is relative: absent = to the project
///                                    directory, root = to the solution/repo root, 0 = file
///                                    name only (the file was outside both)
/// |owner=&lt;Component&gt;                    component whose Render produced the element
/// |element=&lt;Kind&gt;                        element record kind: Button, Stack, Component, Func, Memo, …
/// |mounts=&lt;Component&gt;                   on a component's wrapper control: the component it hosts
/// |root=&lt;Component&gt;                     on a host's root content control: the host's root component
/// |key=&lt;key&gt;                             the element's key (.WithKey)
/// |name=&lt;identifier&gt;                     declared name the element was assigned to (source map)
/// |hooks=&lt;i&gt;:&lt;name&gt;@&lt;line&gt;;…              on mounts= / root= controls: the component's hooks
/// </code>
/// <para>Values escape <c>%</c> as <c>%25</c> and <c>|</c> as <c>%7C</c> — nothing else, so a
/// reader splits on <c>|</c>, then on the first <c>=</c>, then unescapes. <c>at</c> is
/// parsed from the right (a Windows path contains <c>:</c>). In <c>hooks</c>, <c>i</c> is the
/// hook's slot index in the component's <c>RenderContext</c>, or <c>?</c> once it can no
/// longer be known statically; the entries are separated by <c>;</c>. Readers ignore
/// unknown fields.</para>
///
/// <para><b>PII.</b> Paths, type names and developer-authored identifiers, plus the
/// element key. This is only ever written in diagnostics mode (build switch AND
/// <c>REACTOR_DIAGNOSTICS=1</c>), never in a shipped app by default.</para>
/// </summary>
internal static class ReactorSourceFormat
{
    internal const string Version = "1";

    internal static string Build(
        SourceLocation? at,
        string? owner,
        string element,
        string? mounts = null,
        string? root = null,
        string? key = null,
        string? name = null,
        string? hooks = null,
        string? rel = null)
    {
        var sb = new StringBuilder(96);
        sb.Append("v=").Append(Version);
        if (at is { } loc && !string.IsNullOrEmpty(loc.FilePath))
        {
            sb.Append("|at=");
            AppendEscaped(sb, loc.FilePath);
            sb.Append(':').Append(loc.LineNumber.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            if (loc.ColumnNumber > 0)
                sb.Append(':').Append(loc.ColumnNumber.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            AppendField(sb, "rel", rel);
        }
        AppendField(sb, "owner", owner);
        AppendField(sb, "element", element);
        AppendField(sb, "mounts", mounts);
        AppendField(sb, "root", root);
        AppendField(sb, "key", key);
        AppendField(sb, "name", name);
        AppendField(sb, "hooks", hooks);
        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string field, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        sb.Append('|').Append(field).Append('=');
        AppendEscaped(sb, value);
    }

    /// <summary>Escapes <c>%</c> → <c>%25</c> and <c>|</c> → <c>%7C</c>.</summary>
    internal static string Escape(string value)
    {
        if (value.IndexOfAny(s_special) < 0) return value;
        var sb = new StringBuilder(value.Length + 8);
        AppendEscaped(sb, value);
        return sb.ToString();
    }

    private static readonly char[] s_special = { '%', '|' };

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            if (c == '%') sb.Append("%25");
            else if (c == '|') sb.Append("%7C");
            else sb.Append(c);
        }
    }

    /// <summary>
    /// Element kind: the element record's type name without its generic arity and without
    /// the <c>Element</c> suffix — <c>ButtonElement</c> → <c>Button</c>,
    /// <c>ComponentElement`1</c> → <c>Component</c>, <c>FuncElement</c> → <c>Func</c>.
    /// </summary>
    internal static string KindOf(Type elementType)
    {
        var name = elementType.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name.Substring(0, tick);
        if (name.Length > "Element".Length && name.EndsWith("Element", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - "Element".Length);
        return name;
    }

    /// <summary>
    /// Component display name: the type's simple name, with a generic component's
    /// arguments in C# form (<c>ItemList&lt;Int32&gt;</c>) instead of its metadata arity.
    /// </summary>
    internal static string ComponentName(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var sb = new StringBuilder();
        AppendComponentName(sb, type);
        return sb.ToString();
    }

    private static void AppendComponentName(StringBuilder sb, Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        sb.Append(tick >= 0 ? name.Substring(0, tick) : name);
        if (!type.IsGenericType || tick < 0) return;

        var args = type.GetGenericArguments();
        int own = int.TryParse(name.AsSpan(tick + 1), out var arity) ? arity : 0;
        if (own == 0) return;
        sb.Append('<');
        for (int i = args.Length - own; i < args.Length; i++)
        {
            if (i > args.Length - own) sb.Append(", ");
            AppendComponentName(sb, args[i]);
        }
        sb.Append('>');
    }

    /// <summary>
    /// Owner/mounts name for a component element or live instance. Matches the name the
    /// component events (<c>RenderError</c>, <c>ComponentRendered</c>) report, so a reader can
    /// correlate them: function and memo components are <c>FuncElement</c> /
    /// <c>MemoElement</c>.
    /// </summary>
    internal static string ComponentName(Component? instance, Element? element)
    {
        if (instance is not null) return ComponentName(instance.GetType());
        if (element is ComponentElement ce) return ComponentName(ce.ComponentType);
        return element?.GetType().Name ?? "unknown";
    }

    /// <summary>
    /// Key text: the key's invariant string form. <c>null</c> for no key.
    /// </summary>
    internal static string? KeyText(object? key) => key switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };
}
