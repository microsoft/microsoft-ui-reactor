using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Localization;

/// <summary>
/// Provides locale-aware message formatting, number/date formatting,
/// and text direction for the active locale. Obtained via UseIntl() hook.
/// </summary>
public sealed class IntlAccessor
{
    private readonly IStringResourceProvider _resourceProvider;
    private readonly MessageCache _messageCache;
    private readonly string _defaultLocale;
    private readonly CultureInfo _culture;
    private readonly bool _pseudoLocalize;
    private readonly Dictionary<string, string> _assetCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    public IntlAccessor(
        string locale,
        IStringResourceProvider resourceProvider,
        MessageCache messageCache,
        string defaultLocale = "en-US",
        bool pseudoLocalize = false)
    {
        Locale = locale;
        _resourceProvider = resourceProvider;
        _messageCache = messageCache;
        _defaultLocale = defaultLocale;
        _pseudoLocalize = pseudoLocalize;
        _culture = new CultureInfo(locale);
        Direction = RtlHelper.IsRtlLocale(locale)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    /// <summary>Current locale (e.g., "en-US", "ar-SA").</summary>
    public string Locale { get; }

    /// <summary>Current text direction.</summary>
    public FlowDirection Direction { get; }

    /// <summary>True if the current locale is right-to-left.</summary>
    public bool IsRtl => Direction == FlowDirection.RightToLeft;

    /// <summary>
    /// Loads a string resource by key, then formats it with ICU MessageFormat.
    /// Falls back to the default locale if the key is missing in the current locale.
    /// </summary>
    public string Message(MessageKey key, object? args = null)
    {
        var pattern = ResolvePattern(key);
        if (pattern is null)
            return _pseudoLocalize ? PseudoLocalizer.MissingKeyMarker(key) : $"[?? {key} ??]";

        string result;
        try
        {
            if (args is null)
                result = _messageCache.Format(Locale, pattern);
            else
            {
                var dict = ToArgsDictionary(args);
                result = _messageCache.Format(Locale, pattern, dict);
            }
        }
        catch (Exception ex)
        {
            // SECURITY (TASK-050): one bad .resw row would otherwise tear
            // down the rendering page. Log and degrade to the raw pattern.
            Debug.WriteLine($"[Reactor.Intl] Format failed for '{key}': {ex.GetType().Name}: {ex.Message}");
            result = pattern;
        }

        result = SanitizeBidi(result);
        return _pseudoLocalize ? PseudoLocalizer.Transform(result) : result;
    }

    /// <summary>
    /// Formats a message that contains rich text tags (e.g., &lt;bold&gt;text&lt;/bold&gt;),
    /// mapping each tag to an element factory. Returns a GroupElement containing the
    /// resulting child elements (text spans + wrapped elements).
    /// </summary>
    /// <remarks>
    /// The .resw value uses XML-like tags: "Click &lt;link&gt;here&lt;/link&gt; to read the &lt;bold&gt;docs&lt;/bold&gt;."
    /// Tags are mapped via the <paramref name="tags"/> dictionary. Unrecognized tags are
    /// rendered as plain text (tag markers stripped). Nested tags are not supported — only
    /// the outermost tag is processed.
    /// </remarks>
    public Element RichMessage(MessageKey key, object? args = null,
        Dictionary<string, Func<string, Element>>? tags = null)
    {
        var pattern = ResolvePattern(key);
        if (pattern is null)
        {
            var marker = _pseudoLocalize ? PseudoLocalizer.MissingKeyMarker(key) : $"[?? {key} ??]";
            return new TextBlockElement(marker);
        }

        string formatted;
        try
        {
            if (args is null)
                formatted = _messageCache.Format(Locale, pattern);
            else
            {
                // SECURITY (TASK-053): escape `<`, `>`, `&` in arg values
                // BEFORE formatting so a translator-controlled arg can't
                // mint a `<link>` tag that ParseRichText would dispatch to a
                // developer-supplied factory.
                var dict = ToArgsDictionary(args);
                var escaped = EscapeForRichTags(dict);
                formatted = _messageCache.Format(Locale, pattern, escaped);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor.Intl] RichMessage format failed for '{key}': {ex.GetType().Name}: {ex.Message}");
            formatted = pattern;
        }

        formatted = SanitizeBidi(formatted);
        if (_pseudoLocalize)
            formatted = PseudoLocalizer.Transform(formatted);

        if (tags is null || tags.Count == 0)
            return new TextBlockElement(formatted);

        return ParseRichText(formatted, tags);
    }

    /// <summary>
    /// Resolves a locale-qualified asset path. Falls back to the unqualified path
    /// if no locale-specific asset exists.
    /// </summary>
    /// <param name="path">The unqualified asset path (e.g., "Assets/hero-banner.png").</param>
    /// <returns>The locale-qualified path (e.g., "Assets/en-US/hero-banner.png") if it exists,
    /// otherwise the original unqualified path.</returns>
    public string Asset(string path)
    {
        if (_assetCache.TryGetValue(path, out var cached))
            return cached;

        // Build locale-qualified path: insert locale before filename
        // e.g., "Assets/hero-banner.png" -> "Assets/en-US/hero-banner.png"
        var dir = global::System.IO.Path.GetDirectoryName(path) ?? "";
        var fileName = global::System.IO.Path.GetFileName(path);
        var localePath = string.IsNullOrEmpty(dir)
            ? global::System.IO.Path.Combine(Locale, fileName)
            : global::System.IO.Path.Combine(dir, Locale, fileName);

        // Check if locale-specific asset exists
        if (global::System.IO.File.Exists(localePath))
        {
            _assetCache[path] = localePath;
            return localePath;
        }

        // Try base language (e.g., "en" from "en-US")
        var baseLang = Locale.Split('-')[0];
        if (baseLang != Locale)
        {
            var basePath = string.IsNullOrEmpty(dir)
                ? global::System.IO.Path.Combine(baseLang, fileName)
                : global::System.IO.Path.Combine(dir, baseLang, fileName);
            if (global::System.IO.File.Exists(basePath))
            {
                _assetCache[path] = basePath;
                return basePath;
            }
        }

        // Fall back to unqualified path
        _assetCache[path] = path;
        return path;
    }

    /// <summary>
    /// Formats a number for the current locale.
    /// </summary>
    public string FormatNumber(double value, NumberFormatOptions? options = null)
    {
        var style = options?.Style ?? NumberStyle.Default;
        var nfi = (NumberFormatInfo)_culture.NumberFormat.Clone();

        if (options?.MinimumFractionDigits is int min && options?.MaximumFractionDigits is int max)
        {
            var effectiveMin = Math.Min(min, max);
            var effectiveMax = Math.Max(min, max);
            ApplyFractionDigits(nfi, style,
                d => Math.Clamp(d, effectiveMin, effectiveMax));
        }
        else if (options?.MinimumFractionDigits is int minOnly)
            ApplyFractionDigits(nfi, style, d => Math.Max(d, minOnly));
        else if (options?.MaximumFractionDigits is int maxOnly)
            ApplyFractionDigits(nfi, style, d => Math.Min(d, maxOnly));

        return style switch
        {
            NumberStyle.Currency => value.ToString("C", nfi),
            NumberStyle.Percent => value.ToString("P", nfi),
            _ => value.ToString("N", nfi)
        };
    }

    private static void ApplyFractionDigits(
        NumberFormatInfo nfi, NumberStyle style, Func<int, int> transform)
    {
        switch (style)
        {
            case NumberStyle.Percent:
                nfi.PercentDecimalDigits = transform(nfi.PercentDecimalDigits);
                break;
            case NumberStyle.Currency:
                nfi.CurrencyDecimalDigits = transform(nfi.CurrencyDecimalDigits);
                break;
            default:
                nfi.NumberDecimalDigits = transform(nfi.NumberDecimalDigits);
                break;
        }
    }

    /// <summary>
    /// Formats a date for the current locale.
    /// </summary>
    public string FormatDate(DateTimeOffset value, DateFormatOptions? options = null)
    {
        var style = options?.Style ?? DateStyle.Default;
        var format = style switch
        {
            DateStyle.Short => "d",   // 1/15/2026
            DateStyle.Long => "D",    // Thursday, January 15, 2026
            DateStyle.Full => "F",    // Thursday, January 15, 2026 2:30:00 PM
            _ => "G"                  // 1/15/2026 2:30:00 PM
        };

        return value.ToString(format, _culture);
    }

    /// <summary>
    /// Formats a list of strings with locale-aware joining (e.g., "A, B, and C").
    /// </summary>
    public string FormatList(IEnumerable<string> values, ListFormatType type = ListFormatType.Conjunction)
    {
        var list = values.ToList();

        if (list.Count == 0) return string.Empty;
        if (list.Count == 1) return list[0];
        if (list.Count == 2)
        {
            var joiner = type == ListFormatType.Conjunction ? GetAndWord() : GetOrWord();
            return $"{list[0]} {joiner} {list[1]}";
        }

        // 3+: "A, B, and C" or "A, B, or C"
        var lastJoiner = type == ListFormatType.Conjunction ? GetAndWord() : GetOrWord();
        var head = string.Join(", ", list.Take(list.Count - 1));
        return $"{head}, {lastJoiner} {list[^1]}";
    }

    // Matches <tagName>content</tagName> — non-greedy, no nesting.
    private static readonly Regex TagPattern = new(@"<(\w+)>(.*?)</\1>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static Element ParseRichText(string formatted, Dictionary<string, Func<string, Element>> tags)
    {
        var elements = new List<Element>();
        int lastIndex = 0;

        foreach (Match match in TagPattern.Matches(formatted))
        {
            // Add plain text before this tag
            if (match.Index > lastIndex)
            {
                elements.Add(new TextBlockElement(formatted[lastIndex..match.Index]));
            }

            var tagName = match.Groups[1].Value;
            var tagContent = match.Groups[2].Value;

            if (tags.TryGetValue(tagName, out var factory))
            {
                elements.Add(factory(tagContent));
            }
            else
            {
                // Unknown tag — render content as plain text
                elements.Add(new TextBlockElement(tagContent));
            }

            lastIndex = match.Index + match.Length;
        }

        // Add trailing plain text
        if (lastIndex < formatted.Length)
        {
            elements.Add(new TextBlockElement(formatted[lastIndex..]));
        }

        // Single element: return it directly. Multiple: wrap in GroupElement.
        return elements.Count == 1 ? elements[0] : new GroupElement(elements.ToArray());
    }

    private string? ResolvePattern(MessageKey key)
    {
        // Try current locale first
        var pattern = _resourceProvider.GetString(Locale, key.Namespace, key.Key);
        if (pattern is not null)
            return pattern;

        // Fallback to default locale
        if (!string.Equals(Locale, _defaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[Reactor.Intl] Missing key '{key}' for locale '{Locale}', falling back to {_defaultLocale}");
            pattern = _resourceProvider.GetString(_defaultLocale, key.Namespace, key.Key);
            if (pattern is not null)
                return pattern;
        }

        Debug.WriteLine($"[Reactor.Intl] Missing key '{key}' for locale '{Locale}' — no fallback available");
        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ToArgsDictionary uses reflection on anonymous types for localization args.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "ToArgsDictionary uses reflection on anonymous types for localization args.")]
    private static IDictionary<string, object> ToArgsDictionary(object args)
    {
        if (args is IDictionary<string, object> dict)
            return dict;

        // SECURITY (TASK-051): refuse arbitrary DTOs. Anonymous types and the
        // [LocArgs]-marked record contract are the only accepted shapes;
        // anything else (e.g. a domain DTO with `AccessToken`/`Email`/...)
        // would expose every public property to translator-controlled patterns.
        var type = args.GetType();
        if (!IsAcceptableArgsContainer(type))
            throw new ArgumentException(
                $"Localization args must be an IDictionary<string,object>, an anonymous type, or a record marked with [LocArgs]. Got '{type.FullName}'.",
                nameof(args));

        var result = new Dictionary<string, object>();
        var props = _propertyCache.GetOrAdd(type,
            t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        foreach (var prop in props)
        {
            var value = prop.GetValue(args);
            if (value is not null)
                result[prop.Name] = value;
        }
        return result;
    }

    private static bool IsAcceptableArgsContainer(Type type)
    {
        // Anonymous types are sealed, generic, public-property-only with a
        // compiler-generated name like `<>f__AnonymousType0`.
        if (type.Name.StartsWith("<>", StringComparison.Ordinal)) return true;
        // Allow opt-in via [LocArgs] attribute name match.
        foreach (var attr in type.GetCustomAttributes(inherit: false))
        {
            var n = attr.GetType().Name;
            if (n == "LocArgsAttribute" || n == "LocArgs") return true;
        }
        return false;
    }

    /// <summary>
    /// Strips bidi-override codepoints (U+202A..U+202E, U+2066..U+2069) from
    /// formatted output. TASK-052: hostile patterns + arg values would
    /// otherwise reorder rendered UI to spoof file extensions / homoglyphs.
    /// </summary>
    private static string SanitizeBidi(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        bool needsScrub = false;
        foreach (var c in text)
        {
            if ((c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩'))
            { needsScrub = true; break; }
        }
        if (!needsScrub) return text;
        var sb = new global::System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ((c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩')) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// HTML-escapes string-valued args before they are substituted into a
    /// pattern that <see cref="RichMessage"/> will tag-parse. TASK-053.
    /// Non-string values pass through untouched.
    /// </summary>
    private static IDictionary<string, object> EscapeForRichTags(IDictionary<string, object> args)
    {
        var escaped = new Dictionary<string, object>(args.Count);
        foreach (var (k, v) in args)
        {
            escaped[k] = v is string s
                ? s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                : v;
        }
        return escaped;
    }

    /// <summary>
    /// Gets the locale-appropriate "and" conjunction word.
    /// </summary>
    private string GetAndWord()
    {
        var lang = Locale.Split('-')[0].ToLowerInvariant();
        return lang switch
        {
            "es" => "y",
            "fr" => "et",
            "de" => "und",
            "it" => "e",
            "pt" => "e",
            "ja" => "と",
            "ko" => "그리고",
            "zh" => "和",
            "ar" => "و",
            "he" => "ו",
            "ru" => "и",
            "nl" => "en",
            "pl" => "i",
            "tr" => "ve",
            _ => "and"
        };
    }

    /// <summary>
    /// Gets the locale-appropriate "or" disjunction word.
    /// </summary>
    private string GetOrWord()
    {
        var lang = Locale.Split('-')[0].ToLowerInvariant();
        return lang switch
        {
            "es" => "o",
            "fr" => "ou",
            "de" => "oder",
            "it" => "o",
            "pt" => "ou",
            "ja" => "または",
            "ko" => "또는",
            "zh" => "或",
            "ar" => "أو",
            "he" => "או",
            "ru" => "или",
            "nl" => "of",
            "pl" => "lub",
            "tr" => "veya",
            _ => "or"
        };
    }
}
