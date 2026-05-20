using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
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
    public string Message(MessageKey key, IDictionary<string, object>? args = null)
    {
        var pattern = ResolvePattern(key);
        if (pattern is null)
            return _pseudoLocalize ? PseudoLocalizer.MissingKeyMarker(key) : $"[?? {key} ??]";

        string result;
        try
        {
            result = args is null
                ? _messageCache.Format(Locale, pattern)
                : _messageCache.Format(Locale, pattern, args);
        }
        catch (Exception ex)
        {
            // SECURITY (TASK-050): one bad .resw row would otherwise tear
            // down the rendering page. Log and degrade to the raw pattern.
            DiagnosticLog.SwallowedError(LogCategory.Intl, $"Message.Format[{key}]", ex);
            result = pattern;
        }

        result = SanitizeBidi(result);
        return _pseudoLocalize ? PseudoLocalizer.Transform(result) : result;
    }

    /// <summary>
    /// Compact tuple-args overload for the common case of formatting a single
    /// message with a handful of ICU placeholders. Builds a plain
    /// <see cref="Dictionary{TKey, TValue}"/> in-place — no reflection, AOT-safe.
    /// </summary>
    /// <remarks>
    /// Tuples with a null <c>Value</c> are dropped before the dictionary is
    /// built — this matches the behavior of the prior reflection path, which
    /// skipped null-valued properties so the formatter saw the placeholder as
    /// "missing" rather than substituting an empty string.
    /// </remarks>
    /// <example>
    /// <code>
    /// t.Message(Greeting, ("name", "World"));
    /// t.Message(SearchResults, ("count", 0), ("query", "test"));
    /// </code>
    /// </example>
    public string Message(MessageKey key, (string Name, object? Value) arg1,
        params (string Name, object? Value)[] more)
        => Message(key, BuildArgs(arg1, more));

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
    public Element RichMessage(MessageKey key, IDictionary<string, object>? args = null,
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
                var escaped = EscapeForRichTags(args);
                formatted = _messageCache.Format(Locale, pattern, escaped);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Intl, $"RichMessage.Format[{key}]", ex);
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
    /// Compact tuple-args overload of the dict-based <c>RichMessage</c> —
    /// same allocation profile as the tuple-args <c>Message</c> overload.
    /// Tags must be supplied via the dict-based overload; this variant is for
    /// the common case where the developer just wants the formatted text.
    /// Null-valued tuples are dropped (see the tuple-args <c>Message</c> remarks).
    /// </summary>
    public Element RichMessage(MessageKey key, (string Name, object? Value) arg1,
        params (string Name, object? Value)[] more)
        => RichMessage(key, BuildArgs(arg1, more));

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
            pattern = _resourceProvider.GetString(_defaultLocale, key.Namespace, key.Key);
            if (pattern is not null)
            {
                // Spec 044 §6.2.1 PII: MessageKey carries developer-authored
                // identifiers (namespace + key from .resw), never user data.
                if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Intl))
                    ReactorEventSource.Log.IntlMissingKey(key.ToString(), Locale, fellBack: true);
                return pattern;
            }
        }

        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Intl))
            ReactorEventSource.Log.IntlMissingKey(key.ToString(), Locale, fellBack: false);
        return null;
    }

    // Null-valued tuples are skipped so the formatter sees a "missing"
    // placeholder rather than receiving a null arg — preserves the behavior
    // of the prior reflection path, which dropped null-valued properties.
    private static Dictionary<string, object> BuildArgs(
        (string Name, object? Value) arg1,
        (string Name, object? Value)[] more)
    {
        var dict = new Dictionary<string, object>(more.Length + 1);
        if (arg1.Value is not null) dict[arg1.Name] = arg1.Value;
        foreach (var (k, v) in more)
        {
            if (v is not null) dict[k] = v;
        }
        return dict;
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
    /// pattern that the <c>RichMessage</c> overloads will tag-parse. TASK-053.
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
