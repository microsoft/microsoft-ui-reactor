using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Generates hierarchical key names from code context.
/// Pattern: {ClassName (stripped)}.{ContextHint}
/// Example: Text("Save") in SettingsPage → Settings.Save
/// </summary>
internal static class KeyNamer
{
    private static readonly string[] ClassSuffixes = { "Page", "Component", "View", "Panel", "Dialog", "Window", "Control" };

    public static List<KeyedLocString> AssignKeys(List<LocalizableString> extractions)
    {
        var results = new List<KeyedLocString>();
        var usedKeys = new Dictionary<string, int>(); // track (reswFile, key) for uniqueness

        foreach (var ls in extractions)
        {
            var reswFileName = StripClassSuffix(ls.ClassName);
            var contextHint = GenerateContextHint(ls);
            var key = contextHint;

            // Handle ternary branches — append branch-specific suffix
            if (ls.TernaryBranch.HasValue)
            {
                var branchHint = GenerateHintFromValue(ls.Value);
                key = $"{contextHint}{branchHint}";
            }

            // Ensure uniqueness within the same .resw file
            var fullKey = $"{reswFileName}/{key}";
            if (usedKeys.TryGetValue(fullKey, out var count))
            {
                usedKeys[fullKey] = count + 1;
                key = $"{key}{count + 1}";
            }
            else
            {
                usedKeys[fullKey] = 1;
            }

            // Build comment
            string? comment = null;
            if (ls.IsInterpolation)
            {
                comment = "auto-extracted from interpolation";
                // Check for quantity-suggesting variable names
                if (ls.ArgumentMap != null)
                {
                    foreach (var paramName in ls.ArgumentMap.Keys.Concat(
                        ls.ArgumentMap.Values.Where(v => !v.Contains('.'))))
                    {
                        if (InterpolationConverter.IsQuantityName(paramName))
                        {
                            comment += "; consider adding plural support";
                            break;
                        }
                    }
                }
                // Also check ICU value for direct quantity names
                else if (HasQuantityPlaceholder(ls.Value))
                {
                    comment += "; consider adding plural support";
                }
            }

            results.Add(new KeyedLocString
            {
                ReswFileName = reswFileName,
                Namespace = null, // Flat layout: keys go directly into {ClassName}.resw
                Key = key,
                Value = ls.Value,
                Comment = comment,
                Warning = ls.Warning,
                Source = ls,
            });
        }

        return results;
    }

    /// <summary>
    /// Strips common suffixes from class names.
    /// "SettingsPage" → "Settings", "ProductCard" stays "ProductCard".
    /// </summary>
    internal static string StripClassSuffix(string className)
    {
        foreach (var suffix in ClassSuffixes)
        {
            if (className.Length > suffix.Length && className.EndsWith(suffix, StringComparison.Ordinal))
            {
                return className.Substring(0, className.Length - suffix.Length);
            }
        }
        return className;
    }

    /// <summary>
    /// Generates a context hint from the DSL context and string value.
    /// </summary>
    private static string GenerateContextHint(LocalizableString ls)
    {
        var context = ls.Context;

        // For modifier contexts like "ToolTip", "Header", "Placeholder", append to value hint
        if (context.Contains('.'))
        {
            // Named param: "TextBox.placeholder" -> hint from value + "Placeholder"
            var parts = context.Split('.');
            var suffix = ToPascalCase(parts[^1]);
            var valueHint = GenerateHintFromValue(ls.Value);
            return $"{valueHint}{suffix}";
        }

        if (context is "ToolTip" or "Header" or "Placeholder" or "PaneTitle" or "Subtitle")
        {
            var valueHint = GenerateHintFromValue(ls.Value);
            return $"{valueHint}{context}";
        }

        // For DSL methods, use value as the hint
        return GenerateHintFromValue(ls.Value);
    }

    /// <summary>
    /// Converts a string value to a PascalCase key hint.
    /// "Add to Cart" → "AddToCart"
    /// "Hello, {name}" → "HelloName"
    /// Truncated to keep keys reasonable length.
    /// </summary>
    internal static string GenerateHintFromValue(string value)
    {
        // Remove ICU placeholders for hint generation
        var cleaned = Regex.Replace(value, @"\{[^}]+\}", " ");
        // Remove punctuation
        cleaned = Regex.Replace(cleaned, @"[^\w\s]", "");
        // Split into words
        var words = cleaned.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) return "Text";

        var sb = new StringBuilder();
        var maxWords = 5; // Truncate after 5 words
        foreach (var word in words.Take(maxWords))
        {
            sb.Append(ToPascalCase(word));
        }

        var result = sb.ToString();

        // Ensure it starts with a letter
        if (result.Length > 0 && !char.IsLetter(result[0]))
            result = "Key" + result;

        // Truncate to reasonable length
        if (result.Length > 40)
            result = result.Substring(0, 40);

        return result.Length > 0 ? result : "Text";
    }

    private static string ToPascalCase(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
    }

    private static bool HasQuantityPlaceholder(string icuMessage)
    {
        // Check if the ICU message contains placeholders with quantity-suggesting names
        var matches = Regex.Matches(icuMessage, @"\{(\w+)");
        foreach (Match match in matches)
        {
            if (InterpolationConverter.IsQuantityName(match.Groups[1].Value))
                return true;
        }
        return false;
    }
}
