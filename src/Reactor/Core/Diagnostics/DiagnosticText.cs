using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// How <see cref="ReactorDiagnostics"/> turns live values into text and text back into values:
/// formatting (quoted strings, summarised collections, truncation), secret redaction and
/// state parsing. Reactor owns this so every inspector shows and parses values the same way
/// and never has to hold a live object to do it. Mirrors the winapp DevTools agent's
/// <c>ReactorComponents</c> / <c>ReactorElementProps</c> rules so its output is unchanged.
/// </summary>
internal static class DiagnosticText
{
    internal const string Redacted = "<redacted>";
    internal const int MaxValueLength = 200;

    /// <summary><c>int</c>, <c>string?</c>, <c>List&lt;TaskItem&gt;</c>: the type as C# reads it.</summary>
    internal static string FriendlyTypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return FriendlyTypeName(inner) + "?";
        if (!type.IsEnum)
        {
            var alias = Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => "bool", TypeCode.Byte => "byte", TypeCode.SByte => "sbyte", TypeCode.Char => "char",
                TypeCode.Int16 => "short", TypeCode.UInt16 => "ushort", TypeCode.Int32 => "int", TypeCode.UInt32 => "uint",
                TypeCode.Int64 => "long", TypeCode.UInt64 => "ulong", TypeCode.Single => "float", TypeCode.Double => "double",
                TypeCode.Decimal => "decimal", TypeCode.String => "string",
                _ => null,
            };
            if (alias is not null) return alias;
        }
        if (type == typeof(object)) return "object";
        if (type.IsArray && type.GetElementType() is { } element)
            return FriendlyTypeName(element) + "[]";
        var name = TypeName(type);
        return type.IsGenericType
            ? $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeName))}>"
            : name;
    }

    /// <summary>A type's name without its generic arity suffix (<c>List`1</c> → <c>List</c>).</summary>
    internal static string TypeName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        return tick > 0 ? name.Substring(0, tick) : name;
    }

    /// <summary>
    /// What a delegate runs, as the developer wrote it: <c>Counter.Render</c>, or
    /// <c>{fallback} in Counter.Render</c> for a lambda or local function.
    /// </summary>
    internal static string DelegateName(Delegate? render, string fallback)
    {
        global::System.Reflection.MethodInfo? method;
        try { method = render?.Method; }
        catch (NotSupportedException) { method = null; }
        if (method is null) return fallback;

        var owner = method.DeclaringType;
        while (owner is not null && owner.Name.StartsWith('<'))
            owner = owner.DeclaringType;
        var name = method.Name;
        if (name.StartsWith('<') && name.IndexOf('>') is var close and > 1)
            return $"{fallback} in {(owner is null ? "" : TypeName(owner) + ".")}{name.Substring(1, close - 1)}";
        return owner is null ? name : $"{TypeName(owner)}.{name}";
    }

    /// <summary>A type a value can be typed as text: primitives, strings, enums, dates, times, GUIDs and their nullables.</summary>
    internal static bool IsEditable(Type? type)
    {
        if (type is null) return false;
        type = Nullable.GetUnderlyingType(type) ?? type;
        // nint/nuint are primitive but have no text form the parser accepts.
        return (type.IsPrimitive && type != typeof(nint) && type != typeof(nuint)) || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid);
    }

    /// <summary>Whether a value of this type is a secret: a <c>SecureString</c> or a password-, secret- or credential-like type.</summary>
    internal static bool IsSecretType(Type? type)
    {
        if (type is null) return false;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.FullName == "System.Security.SecureString" || IsSecretName(TypeName(type));
    }

    /// <summary>
    /// <see cref="IsSecretType"/>, or a collection of secrets: an array whose element type, or a
    /// generic type with a type argument, that is a secret (<c>List&lt;UserCredential&gt;</c>).
    /// </summary>
    internal static bool HoldsSecretType(Type? type)
    {
        if (type is null) return false;
        if (IsSecretType(type)) return true;
        if (type.IsArray && type.GetElementType() is { } element) return HoldsSecretType(element);
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                if (HoldsSecretType(argument)) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a member or type name says its value is a secret: it ends in <c>Password</c>,
    /// <c>Secret</c>, <c>Credential</c>, <c>Token</c>, <c>ApiKey</c>, <c>PrivateKey</c> or
    /// <c>ConnectionString</c> (any case). A bare <c>Key</c> is not enough — WinUI's
    /// <c>AccessKey</c> is an ordinary property.
    /// </summary>
    internal static bool IsSecretName(string? name)
    {
        if (name is not { Length: > 0 }) return false;
        foreach (var suffix in s_secretSuffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static readonly string[] s_secretSuffixes =
        ["Password", "Secret", "Credential", "Token", "ApiKey", "PrivateKey", "ConnectionString"];

    // A member of a secret name followed by a value, inside an object's own text or a string:
    // "Password = …" in a record's compiler-generated ToString(), "ApiSecret: …" in a hand-written
    // one, "AccessToken=…" in a connection-style string, "\"Password\":\"…\"" in JSON. A bare label
    // ("Password:") or an empty quoted value has nothing after the separator and does not match.
    private static readonly global::System.Text.RegularExpressions.Regex s_secretMemberInText = new(
        @"\b\w*(?:Password|Secret|Credential|Token|ApiKey|PrivateKey|ConnectionString)[""']?\s*[=:]\s*[""']?[^\s,;}\]""']",
        global::System.Text.RegularExpressions.RegexOptions.IgnoreCase | global::System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A component value (prop, hook, context) as shown to the developer: redacted when its name
    /// or type says it is a secret, collections summarised, strings quoted, long text cut.
    /// </summary>
    internal static (string Text, bool Redacted) Format(string name, Type? declared, object? value)
    {
        if (value is not null && (IsSecretName(name) || HoldsSecretType(declared) || HoldsSecretType(value.GetType())))
            return (Redacted, true);

        if (value is not null && IsListLike(value))
        {
            // A collection expression's runtime type is compiler-generated (<>z__ReadOnlyArray); name the declared one.
            var shownType = value.GetType().Name.StartsWith('<') && declared is not null ? declared : value.GetType();
            var countText = TryCount(value) is int count
                ? $"{count.ToString(CultureInfo.InvariantCulture)} items"
                : "count unknown";
            return ($"{FriendlyTypeName(shownType)} ({countText})", false);
        }

        if (value is string s)
        {
            // A string can carry a labelled secret itself ("AccessToken=…", a connection string).
            if (s_secretMemberInText.IsMatch(s)) return (Redacted, true);
            return (Truncate(Quote(s)), false);
        }

        return FormatPlain(name, value);
    }

    /// <summary>
    /// A value as an element record or modifier carries it: unquoted, redacted when the name says
    /// it is a secret or when the value's own text shows a secret-named member, long text cut.
    /// Used for applied properties.
    /// </summary>
    internal static (string Text, bool Redacted) FormatPlain(string name, object? value)
    {
        if (IsSecretName(name) && value is not null and not Delegate)
            return (Redacted, true);

        string text;
        switch (value)
        {
            case null: text = "null"; break;
            case string str: text = str; break;
            case bool b: text = b ? "True" : "False"; break;
            case Delegate d: text = "handler " + DelegateName(d, "lambda"); break;
            case Element e: text = TypeName(e.GetType()); break;
            case Microsoft.UI.Reactor.Input.ElementRef r:
                text = r.ExpectedType is { } t ? $"ElementRef<{FriendlyTypeName(t)}>" : "ElementRef";
                break;
            case IFormattable f:
                text = OwnText(f, () => f.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                text = OwnText(value, value.ToString);
                break;
        }
        // Any text — a string, a formattable value, an object's own ToString() (a record prints
        // its members) — can carry a labelled secret ("AccessToken=…", "Password = …"); withhold
        // the whole value rather than leak it.
        if (value is not null && s_secretMemberInText.IsMatch(text)) return (Redacted, true);
        return (Truncate(text), false);
    }

    /// <summary>
    /// An app value's own text. App code can throw from <c>ToString()</c>; one bad value must
    /// not fail a whole description, so it is shown as its type instead.
    /// </summary>
    private static string OwnText(object value, Func<string?> toString)
    {
        try { return toString() ?? ""; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return $"{FriendlyTypeName(value.GetType())} (ToString() threw {ex.GetType().Name})";
        }
    }

    /// <summary>
    /// True when every bit set in a <c>[Flags]</c> value belongs to a declared member, so a
    /// numeric string cannot inject an undefined combination.
    /// </summary>
    private static bool OnlyDefinedFlags(Type enumType, object value)
    {
        ulong mask = 0;
        foreach (var member in Enum.GetValuesAsUnderlyingType(enumType))
            mask |= ToBits(member);
        return (ToBits(value) & ~mask) == 0;
    }

    // Read through IConvertible (which a boxed enum implements for its underlying type) rather
    // than by unboxing, and widen signed and unsigned alike so a value and the mask agree.
    private static ulong ToBits(object value)
    {
        var convertible = (IConvertible)value;
        return convertible.GetTypeCode() switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                => unchecked((ulong)convertible.ToInt64(CultureInfo.InvariantCulture)),
            TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
                => convertible.ToUInt64(CultureInfo.InvariantCulture),
            _ => ulong.MaxValue,
        };
    }

    /// <summary>
    /// Parses text to <paramref name="type"/>. No conversion beyond that: a value of any other
    /// type is refused with a reason, because a complex value cannot be typed safely from text.
    /// <c>"null"</c> sets a string or nullable to null.
    /// </summary>
    internal static bool TryParse(string text, Type type, out object? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        error = null;
        var underlying = Nullable.GetUnderlyingType(type);
        var target = underlying ?? type;
        // The editable-type gate comes first so "null" cannot clear a value that is
        // otherwise not settable from text (a List<T>, a record) — Editable says so.
        if (!IsEditable(type))
        {
            error = $"a {FriendlyTypeName(type)} value cannot be typed as text; only numbers, characters, text, true/false, enums, dates, times, GUIDs and their nullables can be set";
            return false;
        }
        if (text == "null" && (underlying is not null || !type.IsValueType))
            return true;

        var culture = CultureInfo.InvariantCulture;
        try
        {
            if (target == typeof(string))
            {
                value = text;
                return true;
            }
            if (target.IsEnum)
            {
                if (Enum.TryParse(target, text, ignoreCase: true, out var parsed) &&
                    (Enum.IsDefined(target, parsed!) || (target.IsDefined(typeof(FlagsAttribute), false) && OnlyDefinedFlags(target, parsed!))))
                {
                    value = parsed;
                    return true;
                }
                error = $"'{text}' is not a {FriendlyTypeName(target)}; use one of {string.Join(", ", Enum.GetNames(target))}";
                return false;
            }
            if (target == typeof(bool))
            {
                if (bool.TryParse(text, out var flag))
                {
                    value = flag;
                    return true;
                }
                error = $"'{text}' is not true or false";
                return false;
            }
            if (target == typeof(char))
            {
                if (text.Length == 1)
                {
                    value = text[0];
                    return true;
                }
                error = $"'{text}' is not a single character";
                return false;
            }
            value = target == typeof(Guid) ? Guid.Parse(text)
                : target == typeof(TimeSpan) ? TimeSpan.Parse(text, culture)
                : target == typeof(DateTime) ? DateTime.Parse(text, culture, DateTimeStyles.RoundtripKind)
                : target == typeof(DateTimeOffset) ? DateTimeOffset.Parse(text, culture)
                : Convert.ChangeType(text, target, culture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException or ArgumentException)
        {
            value = null;
            error = $"'{text}' is not a valid {FriendlyTypeName(type)}";
            return false;
        }
    }

    /// <summary>
    /// A component's props as name/value rows. A scalar props value is one row named <c>Props</c>.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Diagnostics-only read of the props object's public properties. When trimming has removed that " +
                        "metadata the reader finds no properties and falls back to a single row naming the props type " +
                        "(never its ToString(), which could print a secret member), so it degrades rather than fails.")]
    internal static IReadOnlyList<(string Name, Type Type, object? Value)> PropRows(object? props, Type? declared)
    {
        if (props is null) return global::System.Array.Empty<(string, Type, object?)>();
        var type = props.GetType();
        // A secret-bearing props type stays one row, so Format redacts it whole instead of the
        // members of, say, a SessionToken(string Value) being listed one by one.
        if (type.IsPrimitive || props is string or decimal or Enum || IsListLike(props)
            || HoldsSecretType(declared) || HoldsSecretType(type))
            return new[] { ("Props", declared ?? type, (object?)props) };

        var rows = new List<(string, Type, object?)>();
        // A public getter, not just CanRead: `public string X { private get; set; }` is not readable surface.
        var readable = type.GetProperties(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance)
            .Where(static p => p.Name != "EqualityContract" && p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true });
        foreach (var property in readable)
        {
            object? value;
            try
            {
                value = property.GetValue(props);
            }
            catch (global::System.Reflection.TargetInvocationException)
            {
                // A throwing getter still gets its row, so the member is visible; the exception
                // text is not shown because it can carry the value.
                value = new OpaqueValue("<value unavailable>");
            }
            rows.Add((property.Name, property.PropertyType, value));
        }
        // No readable members (none declared, or their metadata trimmed away): name the type rather
        // than fall back to ToString(), which for a record would print every member, secrets included.
        if (rows.Count == 0)
            rows.Add(("Props", declared ?? type, new OpaqueValue($"{FriendlyTypeName(type)} (members unavailable)")));
        return rows;
    }

    /// <summary>A value shown by a fixed description instead of its own text.</summary>
    internal sealed class OpaqueValue(string text)
    {
        public override string ToString() => text;
    }

    // A collection whose ToString() is object's (prints the type name) is summarised by count; a
    // collection that formats itself (e.g. a record implementing IEnumerable) keeps its own text.
    // Compared against the runtime type name rather than reflected on, so it is trim-safe.
    private static bool IsListLike(object value) =>
        value is ICollection ||
        (value is IEnumerable and not string && OwnText(value, value.ToString) == value.GetType().ToString());

    /// <summary>
    /// The item count a collection advertises, without enumerating it: a lazy sequence can be
    /// expensive, infinite, or have side effects, so one that does not advertise a count reports
    /// none (the rule <c>Reactor.Devtools</c>' state tool already follows).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Diagnostics-only read of a public Count property (ICollection<T> / IReadOnlyCollection<T> implementers). " +
                        "When trimming has removed it the count is reported as unknown.")]
    internal static int? TryCount(object value)
    {
        try
        {
            if (value is ICollection collection) return collection.Count;
            var count = value.GetType().GetProperty("Count", global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance);
            if (count is not null && count.PropertyType == typeof(int) && count.GetIndexParameters().Length == 0)
                return (int?)count.GetValue(value);
            return null;
        }
        // App code: a collection's Count can throw (directly, or wrapped by reflection).
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return null;
        }
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\"";

    private static string Truncate(string text) =>
        text.Length > MaxValueLength ? text.Substring(0, MaxValueLength) + "..." : text;
}
